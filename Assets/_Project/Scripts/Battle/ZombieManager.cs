using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSLG.Domain;
using Random = UnityEngine.Random;

namespace ZombieSLG.Battle
{
    /// <summary>One pooled zombie. Plain class: the manager updates all agents in a single loop (no per-zombie Update).</summary>
    public sealed class ZombieAgent
    {
        public GameObject Go;
        public Transform Tr;
        public Transform Visual;
        public Renderer[] Renderers;
        public ZombieType Type;
        public ZombieStats Stats;
        public float Hp;
        public Vector3 Pos;
        public Vector3 Vel;
        public Vector3 Knock;
        public float AttackTimer;
        public float FlashTimer;
        public float LungeTimer;
        public float Phase;
        public float DeathTimer;
        public bool Dying;

        public bool IsAlive => !Dying;
        public Vector3 AimPoint => Pos + new Vector3(0f, 0.8f * Stats.VisualScale, 0f);
    }

    /// <summary>
    /// M0 baseline from the GDD optimisation plan: GameObjects + object pooling + a shared flow field.
    /// Neighbour queries use a uniform-grid spatial hash rebuilt every frame (O(n)).
    /// M4 replaces the agent loop with Jobs/Burst and rendering with GPU instancing; keep this as the "before" profile.
    /// </summary>
    public sealed class ZombieManager
    {
        public const int HardCap = 800;
        private const float DeathDuration = 0.45f;
        private const float FlashDuration = 0.08f;
        private const float SeparationStrength = 6f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly BattleContext _ctx;
        private readonly Transform _root;
        private readonly Dictionary<ZombieType, Stack<ZombieAgent>> _pools = new Dictionary<ZombieType, Stack<ZombieAgent>>();
        private readonly List<ZombieAgent> _active = new List<ZombieAgent>(512);
        private readonly int[] _cellHead = new int[MapLayout.Cols * MapLayout.Rows];
        private readonly int[] _next = new int[HardCap];
        private readonly MaterialPropertyBlock _flashBlock = new MaterialPropertyBlock();

        private readonly Dictionary<ZombieType, Material> _bodyMats = new Dictionary<ZombieType, Material>();
        private readonly Material _armMat;
        private readonly Material _eyeMat;

        public int AliveCount { get; private set; }
        public int ActiveCount => _active.Count;
        public int Kills { get; set; }

        public event Action<ZombieAgent> Killed;

        public ZombieManager(BattleContext ctx)
        {
            _ctx = ctx;
            _root = new GameObject("Zombies").transform;
            _root.SetParent(ctx.WorldRoot, false);

            var m = ctx.Materials;
            _bodyMats[ZombieType.Walker] = m.Lit(new Color(0.36f, 0.48f, 0.33f));
            _bodyMats[ZombieType.Runner] = m.Lit(new Color(0.55f, 0.52f, 0.30f));
            _bodyMats[ZombieType.Brute] = m.Lit(new Color(0.42f, 0.30f, 0.32f));
            _armMat = m.Lit(new Color(0.26f, 0.33f, 0.24f));
            _eyeMat = m.Unlit(new Color(3f, 0.4f, 0.2f));

            _flashBlock.SetColor(BaseColorId, new Color(1.6f, 1.6f, 1.6f));
            for (int i = 0; i < _cellHead.Length; i++) _cellHead[i] = -1;
        }

        public void Prewarm(int walkers, int runners, int brutes)
        {
            PrewarmType(ZombieType.Walker, walkers);
            PrewarmType(ZombieType.Runner, runners);
            PrewarmType(ZombieType.Brute, brutes);
        }

        private void PrewarmType(ZombieType type, int count)
        {
            var pool = GetPool(type);
            for (int i = 0; i < count; i++) pool.Push(Create(type));
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

        private ZombieAgent Create(ZombieType type)
        {
            var stats = ZombieStatTable.Get(type);
            var go = new GameObject("Zombie_" + type);
            go.transform.SetParent(_root, false);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);

            var m = _ctx.Materials;
            var body = m.Primitive(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 0.7f, 0f), new Vector3(0.7f, 0.7f, 0.7f), _bodyMats[type]);
            var arms = m.Primitive(PrimitiveType.Cube, "Arms", visual, new Vector3(0f, 1.0f, 0.32f), new Vector3(0.62f, 0.12f, 0.5f), _armMat);
            var eyes = m.Primitive(PrimitiveType.Cube, "Eyes", visual, new Vector3(0f, 1.18f, 0.3f), new Vector3(0.36f, 0.06f, 0.06f), _eyeMat);
            eyes.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            go.transform.localScale = Vector3.one * stats.VisualScale;
            go.SetActive(false);

            return new ZombieAgent
            {
                Go = go,
                Tr = go.transform,
                Visual = visual,
                Renderers = new[] { body.GetComponent<Renderer>(), arms.GetComponent<Renderer>() },
                Type = type,
                Stats = stats,
            };
        }

        public bool Spawn(ZombieType type, Vector3 pos, float hpMultiplier)
        {
            if (_active.Count >= HardCap) return false;

            var pool = GetPool(type);
            var z = pool.Count > 0 ? pool.Pop() : Create(type);

            z.Stats = ZombieStatTable.Get(type).WithHpMultiplier(hpMultiplier);
            z.Hp = z.Stats.MaxHp;
            z.Pos = pos;
            z.Vel = Vector3.zero;
            z.Knock = Vector3.zero;
            z.AttackTimer = Random.Range(0f, z.Stats.AttackInterval);
            z.FlashTimer = 0f;
            z.LungeTimer = 0f;
            z.Phase = Random.value * 10f;
            z.DeathTimer = 0f;
            z.Dying = false;

            z.Tr.position = pos;
            z.Tr.rotation = Quaternion.LookRotation(Vector3.back);
            z.Visual.localPosition = Vector3.zero;
            z.Visual.localRotation = Quaternion.identity;
            ClearFlash(z);
            z.Go.SetActive(true);

            _active.Add(z);
            AliveCount++;
            return true;
        }

        /// <summary>Runs AI, steering, attacks and animation for every active zombie.</summary>
        public void Tick(float dt, bool simulate)
        {
            var map = _ctx.Map;
            var hero = _ctx.Hero;
            var hq = _ctx.Hq;
            bool heroAlive = hero != null && hero.IsAlive;
            Vector3 heroPos = heroAlive ? hero.Position : Vector3.zero;
            float aggro = _ctx.Config.ZombieAggroRange;

            for (int i = 0; i < _active.Count; i++)
            {
                var z = _active[i];

                if (z.Dying)
                {
                    AnimateDeath(z, dt);
                    continue;
                }

                if (z.FlashTimer > 0f)
                {
                    z.FlashTimer -= dt;
                    if (z.FlashTimer <= 0f) ClearFlash(z);
                }

                if (!simulate)
                {
                    Animate(z, dt, false);
                    continue;
                }

                // --- decide goal ---
                Vector3 desired;
                bool attacking = false;
                Vector3 facing = z.Vel;
                float reach = z.Stats.Radius + 0.35f;

                Vector3 toHero = heroPos - z.Pos;
                toHero.y = 0f;
                float heroDist = toHero.magnitude;

                if (heroAlive && heroDist < aggro)
                {
                    if (heroDist <= reach + 0.25f)
                    {
                        desired = Vector3.zero;
                        attacking = true;
                        facing = toHero;
                        z.AttackTimer -= dt;
                        if (z.AttackTimer <= 0f)
                        {
                            z.AttackTimer = z.Stats.AttackInterval;
                            z.LungeTimer = 0.18f;
                            hero.TakeDamage(z.Stats.AttackDamage);
                        }
                    }
                    else
                    {
                        desired = toHero / Mathf.Max(heroDist, 0.001f);
                    }
                }
                else if (map.DistanceToHq(z.Pos) <= reach)
                {
                    desired = Vector3.zero;
                    attacking = true;
                    facing = hq.transform.position - z.Pos;
                    z.AttackTimer -= dt;
                    if (z.AttackTimer <= 0f)
                    {
                        z.AttackTimer = z.Stats.AttackInterval;
                        z.LungeTimer = 0.18f;
                        hq.TakeDamage(z.Stats.AttackDamage);
                    }
                }
                else
                {
                    desired = map.FlowAt(z.Pos);
                    if (desired == Vector3.zero)
                    {
                        Vector3 toHq = hq.transform.position - z.Pos;
                        toHq.y = 0f;
                        desired = toHq.normalized;
                    }
                }

                // --- separation (spatial hash, 3x3 cells) ---
                Vector3 push = Separation(i, z);

                Vector3 targetVel = desired * z.Stats.Speed;
                z.Vel = Vector3.MoveTowards(z.Vel, targetVel, 10f * dt);
                Vector3 move = (z.Vel + push * SeparationStrength + z.Knock) * dt;
                z.Knock = Vector3.MoveTowards(z.Knock, Vector3.zero, 14f * dt);

                z.Pos = MapLayout.ClampToMap(TryMove(map, z.Pos, move), 0.05f);

                facing.y = 0f;
                if (!attacking && z.Vel.sqrMagnitude > 0.01f) facing = z.Vel;
                if (facing.sqrMagnitude > 0.0001f)
                {
                    var targetRot = Quaternion.LookRotation(facing);
                    z.Tr.rotation = Quaternion.RotateTowards(z.Tr.rotation, targetRot, 540f * dt);
                }
                z.Tr.position = z.Pos;

                Animate(z, dt, attacking);
            }

            // Return finished corpses to the pool (reverse so swap-remove is safe).
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var z = _active[i];
                if (!z.Dying || z.DeathTimer < DeathDuration) continue;
                z.Go.SetActive(false);
                int last = _active.Count - 1;
                _active[i] = _active[last];
                _active.RemoveAt(last);
                GetPool(z.Type).Push(z);
            }

            // Rebuilt after removals so indices stay valid for next frame's hero/tower queries.
            RebuildHash();
        }

        private Vector3 Separation(int self, ZombieAgent z)
        {
            MapLayout.WorldToCell(z.Pos, out int cx, out int cy);
            Vector3 push = Vector3.zero;
            for (int y = cy - 1; y <= cy + 1; y++)
            {
                if (y < 0 || y >= MapLayout.Rows) continue;
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    if (x < 0 || x >= MapLayout.Cols) continue;
                    for (int j = _cellHead[y * MapLayout.Cols + x]; j != -1; j = _next[j])
                    {
                        if (j == self || j >= _active.Count) continue;
                        var o = _active[j];
                        if (o.Dying) continue;
                        float dx = z.Pos.x - o.Pos.x;
                        float dz = z.Pos.z - o.Pos.z;
                        float min = z.Stats.Radius + o.Stats.Radius;
                        float d2 = dx * dx + dz * dz;
                        if (d2 >= min * min) continue;
                        if (d2 < 1e-6f)
                        {
                            // Perfect overlap: nudge deterministically by index.
                            dx = (self & 1) == 0 ? 0.01f : -0.01f;
                            dz = (self & 2) == 0 ? 0.01f : -0.01f;
                            d2 = dx * dx + dz * dz;
                        }
                        float d = Mathf.Sqrt(d2);
                        // Heavier zombies get pushed less.
                        float weight = o.Stats.Radius / (z.Stats.Radius + o.Stats.Radius);
                        push.x += dx / d * (min - d) * weight;
                        push.z += dz / d * (min - d) * weight;
                    }
                }
            }
            return push;
        }

        private static Vector3 TryMove(MapLayout map, Vector3 from, Vector3 delta)
        {
            Vector3 to = from + delta;
            if (map.IsWalkableForZombie(to)) return to;
            Vector3 alongX = new Vector3(from.x + delta.x, 0f, from.z);
            if (map.IsWalkableForZombie(alongX)) return alongX;
            Vector3 alongZ = new Vector3(from.x, 0f, from.z + delta.z);
            if (map.IsWalkableForZombie(alongZ)) return alongZ;
            return from;
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

            z.Visual.localPosition = new Vector3(0f, bob, lunge);
            z.Visual.localRotation = Quaternion.Euler(attacking ? 12f : 4f, 0f, sway);
        }

        private static void AnimateDeath(ZombieAgent z, float dt)
        {
            z.DeathTimer += dt;
            float t = Mathf.Clamp01(z.DeathTimer / DeathDuration);
            float fall = 1f - (1f - t) * (1f - t);
            z.Visual.localRotation = Quaternion.Euler(-85f * fall, 0f, 0f);
            z.Visual.localPosition = new Vector3(0f, -0.6f * t * t, -0.3f * fall);
        }

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

        /// <summary>Nearest living zombie within <paramref name="range"/>, or null.</summary>
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

        /// <summary>All living zombies within <paramref name="radius"/>. Clears <paramref name="results"/> first.</summary>
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

        public void ApplyDamage(ZombieAgent z, float damage, bool crit, Vector3 knockDir, float knockForce, DamageStyle style)
        {
            if (z == null || z.Dying || damage <= 0f) return;

            z.Hp -= damage;
            knockDir.y = 0f;
            if (knockDir.sqrMagnitude > 0.0001f)
                z.Knock += knockDir.normalized * (knockForce / (z.Stats.VisualScale * z.Stats.VisualScale));

            z.FlashTimer = FlashDuration;
            for (int r = 0; r < z.Renderers.Length; r++) z.Renderers[r].SetPropertyBlock(_flashBlock);

            _ctx.Hud?.ShowDamage(z.AimPoint, damage, crit, style);

            if (z.Hp <= 0f) Kill(z, true);
        }

        private void Kill(ZombieAgent z, bool countKill)
        {
            if (z.Dying) return;
            z.Dying = true;
            z.DeathTimer = 0f;
            ClearFlash(z);
            AliveCount--;
            if (countKill) Kills++;

            var fx = _ctx.Feedback;
            int chunks = z.Type == ZombieType.Brute ? 18 : 8;
            fx.Chunks(z.AimPoint, chunks, new Color(0.35f, 0.5f, 0.3f), new Color(0.45f, 0.08f, 0.06f), 4f, 0.16f * z.Stats.VisualScale);
            fx.Sfx.Play(SfxId.ZombieDeath, 0.15f);
            if (z.Type == ZombieType.Brute) fx.Shake(0.25f);

            Killed?.Invoke(z);
        }

        /// <summary>Dawn: every remaining zombie collapses (not counted as kills).</summary>
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
            for (int i = 0; i < _active.Count; i++)
            {
                var z = _active[i];
                ClearFlash(z);
                z.Go.SetActive(false);
                GetPool(z.Type).Push(z);
            }
            _active.Clear();
            AliveCount = 0;
            RebuildHash();
        }

        private static void ClearFlash(ZombieAgent z)
        {
            z.FlashTimer = 0f;
            for (int r = 0; r < z.Renderers.Length; r++) z.Renderers[r].SetPropertyBlock(null);
        }
    }
}
