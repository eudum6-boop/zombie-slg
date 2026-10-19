using System.Collections.Generic;
using UnityEngine;

namespace ZombieSLG.Battle
{
    public enum DamageStyle
    {
        Hero,
        Tower,
        Skill,
        Burn,
    }

    /// <summary>
    /// "Game feel" services: particles, tracers, hit-stop, shake routing and sound.
    /// The GDD bets on feedback over asset fidelity, so it is all centralised here and cheap.
    /// </summary>
    public sealed class Feedback
    {
        private readonly CameraRig _rig;
        private readonly ParticleSystem _chunks;
        private readonly ParticleSystem _fire;
        private readonly TracerPool _tracers;
        private float _hitStopTimer;
        private bool _hitStopActive;

        public Sfx Sfx { get; }

        public Feedback(Transform root, MaterialLibrary mats, CameraRig rig)
        {
            _rig = rig;
            _chunks = CreateSystem("ChunkParticles", root, mats, Color.white, gravity: 2.2f, maxParticles: 3000);
            _fire = CreateSystem("FireParticles", root, mats, new Color(3f, 3f, 3f, 1f), gravity: -0.4f, maxParticles: 1500);
            _tracers = new TracerPool(root, mats, 48);
            Sfx = new Sfx(root);
        }

        private static ParticleSystem CreateSystem(string name, Transform root, MaterialLibrary mats, Color tint, float gravity, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.duration = 10f;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = maxParticles;
            main.startRotation3D = true;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var rol = ps.rotationOverLifetime;
            rol.enabled = true;
            rol.separateAxes = true;
            rol.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rol.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rol.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            psr.mesh = mats.CubeMesh;
            psr.sharedMaterial = mats.Particle(tint);
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            ps.Play();
            return ps;
        }

        public void Shake(float trauma) => _rig.AddTrauma(trauma);

        /// <summary>Freezes time briefly to sell big impacts. Uses unscaled time.</summary>
        public void HitStop(float seconds)
        {
            if (seconds <= 0f) return;
            _hitStopTimer = Mathf.Max(_hitStopTimer, seconds);
            _hitStopActive = true;
            Time.timeScale = 0.05f;
        }

        public void Tracer(Vector3 from, Vector3 to, Color color, float width = 0.07f) => _tracers.Fire(from, to, color, width);

        /// <summary>Low-poly chunk burst (gore, debris).</summary>
        public void Chunks(Vector3 pos, int count, Color a, Color b, float speed = 4f, float size = 0.18f)
        {
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.insideUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.6f;
                ep.position = pos + Random.insideUnitSphere * 0.2f;
                ep.velocity = dir.normalized * speed * Random.Range(0.5f, 1.2f);
                ep.startColor = Color.Lerp(a, b, Random.value);
                ep.startSize = size * Random.Range(0.6f, 1.3f);
                ep.startLifetime = Random.Range(0.45f, 0.8f);
                ep.rotation3D = Random.insideUnitSphere * 180f;
                _chunks.Emit(ep, 1);
            }
        }

        /// <summary>Rising fire cubes; the material tint is HDR so they bloom.</summary>
        public void Fire(Vector3 pos, int count, float radius, float speed = 2.5f, float size = 0.35f)
        {
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector2 c = Random.insideUnitCircle * radius;
                ep.position = pos + new Vector3(c.x, 0.1f, c.y);
                ep.velocity = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.6f, 1.4f) * speed, Random.Range(-0.6f, 0.6f));
                // EmitParams colours are 8-bit; the HDR boost for bloom comes from the material tint.
                ep.startColor = Color.Lerp(new Color(1f, 0.3f, 0.05f), new Color(1f, 0.8f, 0.2f), Random.value);
                ep.startSize = size * Random.Range(0.5f, 1.4f);
                ep.startLifetime = Random.Range(0.35f, 0.75f);
                ep.rotation3D = Random.insideUnitSphere * 180f;
                _fire.Emit(ep, 1);
            }
        }

        public void Tick()
        {
            float udt = Time.unscaledDeltaTime;
            _tracers.Tick(udt);

            if (_hitStopActive)
            {
                _hitStopTimer -= udt;
                if (_hitStopTimer <= 0f)
                {
                    _hitStopActive = false;
                    Time.timeScale = 1f;
                }
            }
        }

        public void ResetTime()
        {
            _hitStopActive = false;
            _hitStopTimer = 0f;
            Time.timeScale = 1f;
        }
    }

    /// <summary>Pooled LineRenderer bullet tracers.</summary>
    public sealed class TracerPool
    {
        private sealed class Tracer
        {
            public LineRenderer Line;
            public float Life;
        }

        private readonly List<Tracer> _items = new List<Tracer>();
        private int _cursor;
        private const float Lifetime = 0.06f;

        public TracerPool(Transform root, MaterialLibrary mats, int size)
        {
            var parent = new GameObject("Tracers").transform;
            parent.SetParent(root, false);
            var mat = mats.Particle(new Color(4f, 4f, 4f, 1f));
            for (int i = 0; i < size; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(parent, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.sharedMaterial = mat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 0;
                lr.enabled = false;
                _items.Add(new Tracer { Line = lr });
            }
        }

        public void Fire(Vector3 from, Vector3 to, Color color, float width)
        {
            var t = _items[_cursor];
            _cursor = (_cursor + 1) % _items.Count;
            t.Line.SetPosition(0, from);
            t.Line.SetPosition(1, to);
            t.Line.startColor = color;
            t.Line.endColor = color;
            t.Line.startWidth = width * 0.4f;
            t.Line.endWidth = width;
            t.Line.enabled = true;
            t.Life = Lifetime;
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var t = _items[i];
                if (!t.Line.enabled) continue;
                t.Life -= dt;
                if (t.Life <= 0f) t.Line.enabled = false;
            }
        }
    }
}
