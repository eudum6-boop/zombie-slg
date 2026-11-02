using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using ZombieSLG.Domain;
using Random = UnityEngine.Random;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// M4 horde: no GameObjects. Each frame
    /// <list type="number">
    /// <item>agent state is copied into NativeArrays,</item>
    /// <item>a Burst job buckets agents into the map grid (counting sort),</item>
    /// <item>a parallel Burst job steers every agent (target choice, flow field, separation, wall sliding),</item>
    /// <item>the main thread applies attacks/animation and submits GPU-instanced draws (3 parts × type).</item>
    /// </list>
    /// Behaviour matches <see cref="ZombieManager"/> so the two can be compared on the same night.
    /// </summary>
    public sealed class ZombieSystemJobs : IZombieSystem, IDisposable
    {
        public const int HardCap = ZombieManager.HardCap;
        private const int MaxDefenders = 8;
        private const int BatchLimit = 1023;
        private const float DeathDuration = 0.45f;
        private const float FlashDuration = 0.08f;
        private const float SeparationStrength = 6f;

        private static readonly ProfilerMarker SimMarker = new ProfilerMarker("Zombies.Jobs.Simulate");
        private static readonly ProfilerMarker DrawMarker = new ProfilerMarker("Zombies.Jobs.Draw");

        private readonly BattleContext _ctx;
        private readonly List<ZombieAgent> _active = new List<ZombieAgent>(512);
        private readonly Dictionary<ZombieType, Stack<ZombieAgent>> _pools = new Dictionary<ZombieType, Stack<ZombieAgent>>();
        private readonly Stopwatch _watch = new Stopwatch();

        // managed spatial hash for gameplay queries (hero/tower/skill targeting)
        private readonly int[] _cellHead = new int[MapLayout.Cols * MapLayout.Rows];
        private readonly int[] _next = new int[HardCap];

        // ---- native buffers (persistent, sized to the hard cap) ----
        private NativeArray<float2> _pos, _vel, _knock, _outPos, _outVel, _outKnock, _facing;
        private NativeArray<float> _radius, _speed;
        private NativeArray<byte> _live;
        private NativeArray<int> _target, _cellOf, _sorted, _cellStart, _cellCount, _cellFill;
        private NativeArray<byte> _blocked;
        private NativeArray<float2> _flow;
        private NativeArray<float3> _defenders; // x, z, hit radius
        private bool _disposed;

        // ---- rendering ----
        private readonly Mesh _capsule, _cube;
        private readonly Material[] _bodyMats = new Material[3];
        private readonly Material _armMat, _eyeMat, _flashMat;
        private readonly Matrix4x4[][] _bodies = new Matrix4x4[3][];
        private readonly Matrix4x4[][] _arms = new Matrix4x4[3][];
        private readonly Matrix4x4[][] _eyes = new Matrix4x4[3][];
        private readonly int[] _bodyCount = new int[3];
        private readonly int[] _eyeCount = new int[3];
        private readonly IDefender[] _defRefs = new IDefender[MaxDefenders];
        private readonly Matrix4x4[] _flashBodies = new Matrix4x4[HardCap];
        private readonly Matrix4x4[] _flashArms = new Matrix4x4[HardCap];
        private int _flashCount;
        private readonly Matrix4x4 _bodyLocal, _armsLocal, _eyesLocal;
        private readonly Bounds _worldBounds;

        public string Mode => "Jobs+Burst / GPU 인스턴싱";
        public int AliveCount { get; private set; }
        public int ActiveCount => _active.Count;
        public int Kills { get; set; }
        public float LastTickMs { get; private set; }

        public event Action<ZombieAgent> Killed;

        public ZombieSystemJobs(BattleContext ctx)
        {
            _ctx = ctx;
            var m = ctx.Materials;
            _bodyMats[(int)ZombieType.Walker] = Instanced(m.Lit(new Color(0.36f, 0.48f, 0.33f)));
            _bodyMats[(int)ZombieType.Runner] = Instanced(m.Lit(new Color(0.55f, 0.52f, 0.30f)));
            _bodyMats[(int)ZombieType.Brute] = Instanced(m.Lit(new Color(0.42f, 0.30f, 0.32f)));
            _armMat = Instanced(m.Lit(new Color(0.26f, 0.33f, 0.24f)));
            _eyeMat = Instanced(m.Unlit(new Color(3f, 0.4f, 0.2f)));
            _flashMat = Instanced(m.Lit(new Color(1.6f, 1.6f, 1.6f)));
            _capsule = MeshOf(PrimitiveType.Capsule);
            _cube = MeshOf(PrimitiveType.Cube);

            // Same part layout as the GameObject zombie (ZombieManager.Create).
            _bodyLocal = Matrix4x4.TRS(new Vector3(0f, 0.7f, 0f), Quaternion.identity, new Vector3(0.7f, 0.7f, 0.7f));
            _armsLocal = Matrix4x4.TRS(new Vector3(0f, 1.0f, 0.32f), Quaternion.identity, new Vector3(0.62f, 0.12f, 0.5f));
            _eyesLocal = Matrix4x4.TRS(new Vector3(0f, 1.18f, 0.3f), Quaternion.identity, new Vector3(0.36f, 0.06f, 0.06f));

            for (int t = 0; t < 3; t++)
            {
                _bodies[t] = new Matrix4x4[HardCap];
                _arms[t] = new Matrix4x4[HardCap];
                _eyes[t] = new Matrix4x4[HardCap];
            }

            var size = new Vector3(MapLayout.MaxX - MapLayout.MinX, 10f, MapLayout.MaxZ - MapLayout.MinZ);
            _worldBounds = new Bounds(MapLayout.Center + new Vector3(0f, 2f, 0f), size + new Vector3(4f, 0f, 4f));

            Allocate();
            BakeMap(ctx.Map);
            for (int i = 0; i < _cellHead.Length; i++) _cellHead[i] = -1;
        }

        private static Material Instanced(Material m)
        {
            m.enableInstancing = true;
            return m;
        }

        private static Mesh MeshOf(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(go);
            return mesh;
        }

        private void Allocate()
        {
            const Allocator a = Allocator.Persistent;
            int cells = MapLayout.Cols * MapLayout.Rows;
            _pos = new NativeArray<float2>(HardCap, a);
            _vel = new NativeArray<float2>(HardCap, a);
            _knock = new NativeArray<float2>(HardCap, a);
            _outPos = new NativeArray<float2>(HardCap, a);
            _outVel = new NativeArray<float2>(HardCap, a);
            _outKnock = new NativeArray<float2>(HardCap, a);
            _facing = new NativeArray<float2>(HardCap, a);
            _radius = new NativeArray<float>(HardCap, a);
            _speed = new NativeArray<float>(HardCap, a);
            _live = new NativeArray<byte>(HardCap, a);
            _target = new NativeArray<int>(HardCap, a);
            _cellOf = new NativeArray<int>(HardCap, a);
            _sorted = new NativeArray<int>(HardCap, a);
            _cellStart = new NativeArray<int>(cells, a);
            _cellCount = new NativeArray<int>(cells, a);
            _cellFill = new NativeArray<int>(cells, a);
            _blocked = new NativeArray<byte>(cells, a);
            _flow = new NativeArray<float2>(cells, a);
            _defenders = new NativeArray<float3>(MaxDefenders, a);
        }

        /// <summary>Copies walls and the flow field into native arrays (the map never changes during a night).</summary>
        private void BakeMap(MapLayout map)
        {
            var field = map.Field;
            for (int y = 0; y < MapLayout.Rows; y++)
            {
                for (int x = 0; x < MapLayout.Cols; x++)
                {
                    int c = y * MapLayout.Cols + x;
                    _blocked[c] = (byte)(field.IsBlocked(x, y) ? 1 : 0);
                    field.GetDirection(x, y, out int dx, out int dy);
                    _flow[c] = (dx == 0 && dy == 0) ? float2.zero : math.normalize(new float2(dx, dy));
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pos.Dispose(); _vel.Dispose(); _knock.Dispose();
            _outPos.Dispose(); _outVel.Dispose(); _outKnock.Dispose(); _facing.Dispose();
            _radius.Dispose(); _speed.Dispose(); _live.Dispose(); _target.Dispose();
            _cellOf.Dispose(); _sorted.Dispose(); _cellStart.Dispose(); _cellCount.Dispose(); _cellFill.Dispose();
            _blocked.Dispose(); _flow.Dispose(); _defenders.Dispose();
        }

        // ------------------------------------------------------------------ pool / spawn

        public void Prewarm(int walkers, int runners, int brutes)
        {
            PrewarmType(ZombieType.Walker, walkers);
            PrewarmType(ZombieType.Runner, runners);
            PrewarmType(ZombieType.Brute, brutes);
        }

        private void PrewarmType(ZombieType type, int count)
        {
            var pool = GetPool(type);
            for (int i = 0; i < count; i++) pool.Push(new ZombieAgent { Type = type, Stats = _ctx.Data.Zombie(type) });
        }

        private Stack<ZombieAgent> GetPool(ZombieType type)
        {
            if (!_pools.TryGetValue(type, out var pool))
            {
                pool = new Stack<ZombieAgent>(128);
                _pools[type] = pool;
            }
            return pool;
        }

        public bool Spawn(ZombieType type, Vector3 pos, float hpMultiplier)
        {
            if (_active.Count >= HardCap) return false;
            var pool = GetPool(type);
            var z = pool.Count > 0 ? pool.Pop() : new ZombieAgent { Type = type };

            z.Type = type;
            z.Stats = _ctx.Data.Zombie(type).WithHpMultiplier(hpMultiplier);
            z.Hp = z.Stats.MaxHp;
            z.Pos = pos;
            z.Vel = Vector3.zero;
            z.Knock = Vector3.zero;
            z.Rot = Quaternion.LookRotation(Vector3.back);
            z.AttackTimer = Random.Range(0f, z.Stats.AttackInterval);
            z.FlashTimer = 0f;
            z.LungeTimer = 0f;
            z.Phase = Random.value * 10f;
            z.DeathTimer = 0f;
            z.Dying = false;
            z.VisualPos = Vector3.zero;
            z.VisualRot = Quaternion.identity;

            _active.Add(z);
            AliveCount++;
            return true;
        }

        // ------------------------------------------------------------------ frame

        public void Tick(float dt, bool simulate)
        {
            _watch.Restart();
            int n = _active.Count;

            using (SimMarker.Auto())
            {
                if (simulate && n > 0) Simulate(dt, n);

                for (int i = 0; i < n; i++)
                {
                    var z = _active[i];
                    if (z.Dying) { AnimateDeath(z, dt); continue; }
                    if (z.FlashTimer > 0f) z.FlashTimer -= dt;
                    if (!simulate) Animate(z, dt, false);
                }

                // Return finished corpses to the pool (reverse so swap-remove is safe).
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var z = _active[i];
                    if (!z.Dying || z.DeathTimer < DeathDuration) continue;
                    int last = _active.Count - 1;
                    _active[i] = _active[last];
                    _active.RemoveAt(last);
                    GetPool(z.Type).Push(z);
                }
                RebuildHash();
            }

            using (DrawMarker.Auto()) Draw();
            LastTickMs = (float)_watch.Elapsed.TotalMilliseconds;
        }

        private void Simulate(float dt, int n)
        {
            // 1) managed → native
            for (int i = 0; i < n; i++)
            {
                var z = _active[i];
                _pos[i] = new float2(z.Pos.x, z.Pos.z);
                _vel[i] = new float2(z.Vel.x, z.Vel.z);
                _knock[i] = new float2(z.Knock.x, z.Knock.z);
                _radius[i] = z.Stats.Radius;
                _speed[i] = z.Stats.Speed;
                _live[i] = (byte)(z.Dying ? 0 : 1);
            }

            var defenders = _ctx.Defenders;
            int defCount = 0;
            for (int d = 0; d < defenders.Count && defCount < MaxDefenders; d++)
            {
                var def = defenders[d];
                if (!def.IsAlive) continue;
                _defRefs[defCount] = def;
                _defenders[defCount++] = new float3(def.Position.x, def.Position.z, def.HitRadius);
            }

            var hq = _ctx.Map.HqBounds;
            float2 origin = new float2(MapLayout.Origin.x, MapLayout.Origin.z);

            // 2) jobs: grid bucketing → parallel steering
            var grid = new GridJob
            {
                Count = n, Cols = MapLayout.Cols, Rows = MapLayout.Rows, Origin = origin, CellSize = MapLayout.CellSize,
                Pos = _pos, Live = _live, CellOf = _cellOf, Sorted = _sorted,
                CellStart = _cellStart, CellCount = _cellCount, CellFill = _cellFill,
            }.Schedule();

            var steer = new SteerJob
            {
                Dt = dt,
                Cols = MapLayout.Cols, Rows = MapLayout.Rows, Origin = origin, CellSize = MapLayout.CellSize,
                MapMin = new float2(MapLayout.MinX, MapLayout.MinZ), MapMax = new float2(MapLayout.MaxX, MapLayout.MaxZ),
                HqMin = new float2(hq.min.x, hq.min.z), HqMax = new float2(hq.max.x, hq.max.z),
                HqCenter = new float2(hq.center.x, hq.center.z),
                Aggro = _ctx.Config.ZombieAggroRange, SeparationStrength = SeparationStrength,
                DefenderCount = defCount, Defenders = _defenders,
                Pos = _pos, Vel = _vel, Knock = _knock, Radius = _radius, Speed = _speed, Live = _live,
                CellStart = _cellStart, CellCount = _cellCount, Sorted = _sorted,
                Blocked = _blocked, Flow = _flow,
                OutPos = _outPos, OutVel = _outVel, OutKnock = _outKnock, Facing = _facing, Target = _target,
            }.Schedule(n, 64, grid);
            steer.Complete();

            // 3) native → managed, attacks, animation
            var hqView = _ctx.Hq;
            for (int i = 0; i < n; i++)
            {
                var z = _active[i];
                if (z.Dying) continue;
                float2 p = _outPos[i], v = _outVel[i], k = _outKnock[i], f = _facing[i];
                z.Pos = new Vector3(p.x, 0f, p.y);
                z.Vel = new Vector3(v.x, 0f, v.y);
                z.Knock = new Vector3(k.x, 0f, k.y);

                int target = _target[i];
                bool attacking = target != SteerJob.TargetNone;
                if (attacking)
                {
                    z.AttackTimer -= dt;
                    if (z.AttackTimer <= 0f)
                    {
                        z.AttackTimer = z.Stats.AttackInterval;
                        z.LungeTimer = 0.18f;
                        if (target == SteerJob.TargetHq) hqView.TakeDamage(z.Stats.AttackDamage);
                        else _defRefs[target]?.TakeDamage(z.Stats.AttackDamage);
                    }
                }

                if (f.x * f.x + f.y * f.y > 0.0001f)
                    z.Rot = Quaternion.RotateTowards(z.Rot, Quaternion.LookRotation(new Vector3(f.x, 0f, f.y)), 540f * dt);
                Animate(z, dt, attacking);
            }
        }

        private static void Animate(ZombieAgent z, float dt, bool attacking)
        {
            float speed = z.Vel.magnitude;
            z.Phase += dt * (3f + speed * 3.5f);
            float bob = Mathf.Abs(Mathf.Sin(z.Phase)) * 0.08f * Mathf.Clamp01(speed);
            float sway = Mathf.Sin(z.Phase) * 9f;
            float lunge = 0f;
            if (z.LungeTimer > 0f)
            {
                z.LungeTimer -= dt;
                lunge = Mathf.Sin(Mathf.Clamp01(z.LungeTimer / 0.18f) * Mathf.PI) * 0.35f;
            }
            z.VisualPos = new Vector3(0f, bob, lunge);
            z.VisualRot = Quaternion.Euler(attacking ? 12f : 4f, 0f, sway);
        }

        private static void AnimateDeath(ZombieAgent z, float dt)
        {
            z.DeathTimer += dt;
            float t = Mathf.Clamp01(z.DeathTimer / DeathDuration);
            float fall = 1f - (1f - t) * (1f - t);
            z.VisualRot = Quaternion.Euler(-85f * fall, 0f, 0f);
            z.VisualPos = new Vector3(0f, -0.6f * t * t, -0.3f * fall);
        }

        // ------------------------------------------------------------------ drawing

        private void Draw()
        {
            for (int t = 0; t < 3; t++) { _bodyCount[t] = 0; _eyeCount[t] = 0; }
            _flashCount = 0;

            for (int i = 0; i < _active.Count; i++)
            {
                var z = _active[i];
                var root = Matrix4x4.TRS(z.Pos, z.Rot, Vector3.one * z.Stats.VisualScale)
                           * Matrix4x4.TRS(z.VisualPos, z.VisualRot, Vector3.one);
                int t = (int)z.Type;
                _eyes[t][_eyeCount[t]++] = root * _eyesLocal;
                if (z.FlashTimer > 0f && !z.Dying)
                {
                    // Hit flash: body and arms drawn with the white material instead.
                    _flashBodies[_flashCount] = root * _bodyLocal;
                    _flashArms[_flashCount] = root * _armsLocal;
                    _flashCount++;
                }
                else
                {
                    int k = _bodyCount[t]++;
                    _bodies[t][k] = root * _bodyLocal;
                    _arms[t][k] = root * _armsLocal;
                }
            }

            for (int t = 0; t < 3; t++)
            {
                int count = _bodyCount[t];
                if (count > 0)
                {
                    Submit(_capsule, _bodyMats[t], _bodies[t], count, true);
                    Submit(_cube, _armMat, _arms[t], count, true);
                }
                if (_eyeCount[t] > 0) Submit(_cube, _eyeMat, _eyes[t], _eyeCount[t], false);
            }
            if (_flashCount > 0)
            {
                Submit(_capsule, _flashMat, _flashBodies, _flashCount, true);
                Submit(_cube, _flashMat, _flashArms, _flashCount, true);
            }
        }

        private void Submit(Mesh mesh, Material mat, Matrix4x4[] matrices, int count, bool shadows)
        {
            var rp = new RenderParams(mat)
            {
                worldBounds = _worldBounds,
                shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
            };
            for (int start = 0; start < count; start += BatchLimit)
                Graphics.RenderMeshInstanced(rp, mesh, 0, matrices, Math.Min(BatchLimit, count - start), start);
        }

        // ------------------------------------------------------------------ queries (managed hash)

        private void RebuildHash()
        {
            for (int i = 0; i < _cellHead.Length; i++) _cellHead[i] = -1;
            for (int i = 0; i < _active.Count; i++)
            {
                var z = _active[i];
                if (z.Dying) continue;
                int c = MapLayout.CellIndexClamped(z.Pos);
                _next[i] = _cellHead[c];
                _cellHead[c] = i;
            }
        }

        public ZombieAgent FindNearest(Vector3 p, float range)
        {
            MapLayout.WorldToCell(p, out int cx, out int cy);
            int r = Mathf.CeilToInt(range / MapLayout.CellSize);
            float best = range * range;
            ZombieAgent result = null;
            int yMin = Mathf.Max(0, cy - r), yMax = Mathf.Min(MapLayout.Rows - 1, cy + r);
            int xMin = Mathf.Max(0, cx - r), xMax = Mathf.Min(MapLayout.Cols - 1, cx + r);
            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    for (int j = _cellHead[y * MapLayout.Cols + x]; j != -1; j = _next[j])
                    {
                        if (j >= _active.Count) continue;
                        var z = _active[j];
                        if (z.Dying) continue;
                        float dx = z.Pos.x - p.x, dz = z.Pos.z - p.z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 < best) { best = d2; result = z; }
                    }
                }
            }
            return result;
        }

        public void QueryRadius(Vector3 p, float radius, List<ZombieAgent> results)
        {
            results.Clear();
            float r2 = radius * radius;
            for (int i = 0; i < _active.Count; i++)
            {
                var z = _active[i];
                if (z.Dying) continue;
                float dx = z.Pos.x - p.x, dz = z.Pos.z - p.z;
                if (dx * dx + dz * dz <= r2) results.Add(z);
            }
        }

        // ------------------------------------------------------------------ damage / death

        public void ApplyDamage(ZombieAgent z, float damage, bool crit, Vector3 knockDir, float knockForce, DamageStyle style)
        {
            if (z == null || z.Dying || damage <= 0f) return;
            z.Hp -= damage;
            knockDir.y = 0f;
            if (knockDir.sqrMagnitude > 0.0001f)
                z.Knock += knockDir.normalized * (knockForce / (z.Stats.VisualScale * z.Stats.VisualScale));
            z.FlashTimer = FlashDuration;
            _ctx.Hud?.ShowDamage(z.AimPoint, damage, crit, style);
            if (z.Hp <= 0f) Kill(z, true);
        }

        private void Kill(ZombieAgent z, bool countKill)
        {
            if (z.Dying) return;
            z.Dying = true;
            z.DeathTimer = 0f;
            z.FlashTimer = 0f;
            AliveCount--;
            if (countKill) Kills++;

            var fx = _ctx.Feedback;
            int chunks = z.Type == ZombieType.Brute ? 18 : 8;
            fx.Chunks(z.AimPoint, chunks, new Color(0.35f, 0.5f, 0.3f), new Color(0.45f, 0.08f, 0.06f), 4f, 0.16f * z.Stats.VisualScale);
            fx.Sfx.Play(SfxId.ZombieDeath, 0.15f);
            if (z.Type == ZombieType.Brute) fx.Shake(0.25f);
            Killed?.Invoke(z);
        }

        public void KillAll(bool withEffects)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                var z = _active[i];
                if (z.Dying) continue;
                if (withEffects) Kill(z, false);
                else { z.Dying = true; z.DeathTimer = 0f; AliveCount--; }
            }
        }

        public void DespawnAll()
        {
            foreach (var z in _active) GetPool(z.Type).Push(z);
            _active.Clear();
            AliveCount = 0;
            RebuildHash();
        }

        // ================================================================== Burst jobs

        /// <summary>Counting sort of live agents into grid cells → Sorted[CellStart[c] .. +CellCount[c]].</summary>
        [BurstCompile]
        private struct GridJob : IJob
        {
            public int Count, Cols, Rows;
            public float2 Origin;
            public float CellSize;
            [ReadOnly] public NativeArray<float2> Pos;
            [ReadOnly] public NativeArray<byte> Live;
            public NativeArray<int> CellOf, Sorted, CellStart, CellCount, CellFill;

            public void Execute()
            {
                for (int c = 0; c < CellCount.Length; c++) CellCount[c] = 0;
                for (int i = 0; i < Count; i++)
                {
                    if (Live[i] == 0) { CellOf[i] = -1; continue; }
                    int2 cell = (int2)math.floor((Pos[i] - Origin) / CellSize);
                    cell = math.clamp(cell, int2.zero, new int2(Cols - 1, Rows - 1));
                    int c = cell.y * Cols + cell.x;
                    CellOf[i] = c;
                    CellCount[c]++;
                }
                int run = 0;
                for (int c = 0; c < CellCount.Length; c++)
                {
                    CellStart[c] = run;
                    CellFill[c] = run;
                    run += CellCount[c];
                }
                for (int i = 0; i < Count; i++)
                {
                    int c = CellOf[i];
                    if (c < 0) continue;
                    Sorted[CellFill[c]++] = i;
                }
            }
        }

        /// <summary>Per-agent steering. Reads last frame's positions, writes new ones (double-buffered).</summary>
        [BurstCompile]
        private struct SteerJob : IJobParallelFor
        {
            public const int TargetNone = -2;
            public const int TargetHq = -1;

            public float Dt;
            public int Cols, Rows;
            public float2 Origin, MapMin, MapMax, HqMin, HqMax, HqCenter;
            public float CellSize, Aggro, SeparationStrength;
            public int DefenderCount;
            [ReadOnly] public NativeArray<float3> Defenders;
            [ReadOnly] public NativeArray<float2> Pos, Vel, Knock;
            [ReadOnly] public NativeArray<float> Radius, Speed;
            [ReadOnly] public NativeArray<byte> Live;
            [ReadOnly] public NativeArray<int> CellStart, CellCount, Sorted;
            [ReadOnly] public NativeArray<byte> Blocked;
            [ReadOnly] public NativeArray<float2> Flow;
            [WriteOnly] public NativeArray<float2> OutPos, OutVel, OutKnock, Facing;
            [WriteOnly] public NativeArray<int> Target;

            public void Execute(int i)
            {
                float2 p = Pos[i];
                float2 vel = Vel[i];
                float2 knock = Knock[i];
                if (Live[i] == 0)
                {
                    OutPos[i] = p; OutVel[i] = vel; OutKnock[i] = knock; Facing[i] = float2.zero; Target[i] = TargetNone;
                    return;
                }

                float radius = Radius[i];
                float reach = radius + 0.35f;
                float2 desired;
                float2 facing = vel;
                int target = TargetNone;

                // Nearest living defender inside aggro range, measured to its body edge.
                int foe = -1;
                float foeGap = Aggro;
                float2 toFoe = float2.zero;
                for (int d = 0; d < DefenderCount; d++)
                {
                    float3 def = Defenders[d];
                    float2 to = def.xy - p;
                    float gap = math.length(to) - def.z;
                    if (gap < foeGap) { foeGap = gap; foe = d; toFoe = to; }
                }

                if (foe >= 0)
                {
                    if (foeGap <= reach - 0.1f)
                    {
                        desired = float2.zero;
                        target = foe;
                        facing = toFoe;
                    }
                    else desired = toFoe / math.max(math.length(toFoe), 0.001f);
                }
                else if (DistanceToHq(p) <= reach)
                {
                    desired = float2.zero;
                    target = TargetHq;
                    facing = HqCenter - p;
                }
                else
                {
                    desired = FlowAt(p);
                    if (math.lengthsq(desired) < 1e-6f) desired = math.normalizesafe(HqCenter - p);
                }

                float2 push = Separation(i, p, radius);
                float2 targetVel = desired * Speed[i];
                vel = MoveTowards(vel, targetVel, 10f * Dt);
                float2 move = (vel + push * SeparationStrength + knock) * Dt;
                knock = MoveTowards(knock, float2.zero, 14f * Dt);

                float2 next = math.clamp(TryMove(p, move), MapMin + 0.05f, MapMax - 0.05f);
                if (target == TargetNone && math.lengthsq(vel) > 0.01f) facing = vel;

                OutPos[i] = next;
                OutVel[i] = vel;
                OutKnock[i] = knock;
                Facing[i] = facing;
                Target[i] = target;
            }

            private float2 Separation(int self, float2 p, float radius)
            {
                int2 cell = math.clamp((int2)math.floor((p - Origin) / CellSize), int2.zero, new int2(Cols - 1, Rows - 1));
                float2 push = float2.zero;
                for (int y = cell.y - 1; y <= cell.y + 1; y++)
                {
                    if (y < 0 || y >= Rows) continue;
                    for (int x = cell.x - 1; x <= cell.x + 1; x++)
                    {
                        if (x < 0 || x >= Cols) continue;
                        int c = y * Cols + x;
                        int start = CellStart[c], end = start + CellCount[c];
                        for (int k = start; k < end; k++)
                        {
                            int j = Sorted[k];
                            if (j == self) continue;
                            float2 d = p - Pos[j];
                            float other = Radius[j];
                            float min = radius + other;
                            float d2 = math.lengthsq(d);
                            if (d2 >= min * min) continue;
                            if (d2 < 1e-6f)
                            {
                                // Perfect overlap: nudge deterministically by index.
                                d = new float2((self & 1) == 0 ? 0.01f : -0.01f, (self & 2) == 0 ? 0.01f : -0.01f);
                                d2 = math.lengthsq(d);
                            }
                            float dist = math.sqrt(d2);
                            float weight = other / (radius + other); // heavier zombies get pushed less
                            push += d / dist * (min - dist) * weight;
                        }
                    }
                }
                return push;
            }

            private float2 TryMove(float2 from, float2 delta)
            {
                float2 to = from + delta;
                if (Walkable(to)) return to;
                float2 alongX = new float2(from.x + delta.x, from.y);
                if (Walkable(alongX)) return alongX;
                float2 alongZ = new float2(from.x, from.y + delta.y);
                if (Walkable(alongZ)) return alongZ;
                return from;
            }

            private bool Walkable(float2 p)
            {
                int2 cell = (int2)math.floor((p - Origin) / CellSize);
                if (cell.x < 0 || cell.y < 0 || cell.x >= Cols || cell.y >= Rows) return false;
                return Blocked[cell.y * Cols + cell.x] == 0;
            }

            private float2 FlowAt(float2 p)
            {
                int2 cell = (int2)math.floor((p - Origin) / CellSize);
                if (cell.x < 0 || cell.y < 0 || cell.x >= Cols || cell.y >= Rows) return float2.zero;
                return Flow[cell.y * Cols + cell.x];
            }

            private float DistanceToHq(float2 p)
            {
                float dx = math.max(math.max(HqMin.x - p.x, 0f), p.x - HqMax.x);
                float dz = math.max(math.max(HqMin.y - p.y, 0f), p.y - HqMax.y);
                return math.sqrt(dx * dx + dz * dz);
            }

            private static float2 MoveTowards(float2 current, float2 target, float maxDelta)
            {
                float2 d = target - current;
                float len = math.length(d);
                if (len <= maxDelta || len < 1e-6f) return target;
                return current + d / len * maxDelta;
            }
        }
    }
}
