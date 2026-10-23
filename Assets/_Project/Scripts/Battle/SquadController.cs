using System.Collections.Generic;
using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>Anything zombies can stop to attack instead of walking to the HQ.</summary>
    public interface IDefender
    {
        Vector3 Position { get; }
        bool IsAlive { get; }
        /// <summary>Body radius zombies must reach to land a hit.</summary>
        float HitRadius { get; }
        void TakeDamage(float damage);
    }

    /// <summary>
    /// A squad holding one gate during the night. Rules live in <see cref="SquadState"/> (domain);
    /// this class only draws soldiers, picks targets and fires volleys. Each troop type fires as one
    /// volley (damage × living count × counter vs. the target's zombie type).
    /// </summary>
    public sealed class SquadController : IDefender
    {
        private const int MaxFigures = 9;

        private static readonly Color[] TypeColors =
        {
            new Color(0.32f, 0.45f, 0.30f), // Infantry
            new Color(0.62f, 0.52f, 0.34f), // Shooter
            new Color(0.38f, 0.40f, 0.44f), // Vehicle
        };

        private readonly BattleContext _ctx;
        private readonly Transform _root;
        private readonly Vector3 _post;
        private readonly Vector3 _facing;
        private readonly List<Figure> _figures = new List<Figure>();
        private readonly float[] _fireTimers = new float[TroopRules.TypeCount];
        private readonly int[] _start = new int[TroopRules.TypeCount];

        private SquadState _state;
        private float _damageMult = 1f;
        private float _hurtTimer;

        private sealed class Figure
        {
            public TroopType Type;
            public Transform Tr;
            public Vector3 Home;
            public float Recoil;
        }

        public string Name { get; }
        public Vector3 Position => _post;
        public bool IsAlive => _state != null && !_state.IsDefeated;
        public float HitRadius => 1.1f;
        public int TotalAlive => _state?.TotalAlive ?? 0;
        public int StartTotal { get; private set; }
        public float HealthRatio => _state == null ? 0f : _state.HealthRatio(_ctx.Data, _start);
        public float LastHitTime { get; private set; } = -10f;

        public SquadController(BattleContext ctx, string name, Vector3 post, Vector3 facing)
        {
            _ctx = ctx;
            Name = name;
            _post = post;
            _facing = facing.normalized;
            _root = new GameObject("Squad_" + name).transform;
            _root.SetParent(ctx.WorldRoot, false);
            _root.position = post;
            _root.rotation = Quaternion.LookRotation(_facing);

            var m = ctx.Materials;
            var ring = m.Primitive(PrimitiveType.Cylinder, "Post", _root, new Vector3(0f, 0.015f, 0f), new Vector3(2.4f, 0.01f, 2.4f),
                m.Unlit(new Color(0.35f, 0.6f, 0.3f, 1f)));
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _root.gameObject.SetActive(false);
        }

        /// <summary>Sets the squad for tonight. counts are per TroopType.</summary>
        public void Deploy(int[] counts, float damageMultiplier, float hpMultiplier)
        {
            _state = new SquadState(_ctx.Data, counts, hpMultiplier);
            _damageMult = damageMultiplier;
            StartTotal = 0;
            for (int i = 0; i < TroopRules.TypeCount; i++)
            {
                _start[i] = counts != null && i < counts.Length ? counts[i] : 0;
                StartTotal += _start[i];
                _fireTimers[i] = 0.2f * i;
            }
            BuildFigures();
            _root.gameObject.SetActive(StartTotal > 0);
        }

        public void Hide()
        {
            _state = null;
            _root.gameObject.SetActive(false);
        }

        public int Dead(TroopType t) => _state?.Dead(t) ?? 0;
        public int Wounded(TroopType t) => _state?.Wounded(t) ?? 0;

        /// <summary>Zombie hit: eats the front unit type first; fallen units become wounded or dead.</summary>
        public void TakeDamage(float damage)
        {
            if (!IsAlive) return;
            int fell = _state.TakeDamage(damage);
            LastHitTime = Time.time;
            _hurtTimer = 0.1f;
            if (fell > 0)
            {
                _ctx.Feedback.Chunks(_post + Vector3.up, 6 * fell, new Color(0.3f, 0.4f, 0.3f), new Color(0.5f, 0.1f, 0.1f), 3.5f, 0.12f);
                _ctx.Feedback.Sfx.Play(SfxId.HeroHurt, 0.2f);
                SyncFigures();
                if (!IsAlive)
                {
                    _ctx.Hud.ShowBanner(Name + " 방어선 붕괴!", 2f, new Color(1f, 0.4f, 0.3f));
                    _ctx.Feedback.Shake(0.25f);
                }
            }
        }

        public void Tick(float dt)
        {
            if (!IsAlive) return;
            if (_hurtTimer > 0f) _hurtTimer -= dt;

            for (int t = 0; t < TroopRules.TypeCount; t++)
            {
                var type = (TroopType)t;
                int alive = _state.Alive(type);
                if (alive <= 0) continue;
                var def = _ctx.Data.Troop(type);

                _fireTimers[t] -= dt;
                if (_fireTimers[t] > 0f) continue;
                var target = _ctx.Zombies.FindNearest(_post, def.Range);
                if (target == null) { _fireTimers[t] = 0.1f; continue; }
                _fireTimers[t] = def.FireInterval;
                Volley(type, def, alive, target);
            }

            foreach (var f in _figures)
            {
                if (!f.Tr.gameObject.activeSelf) continue;
                f.Recoil = Mathf.MoveTowards(f.Recoil, 0f, dt * 1.5f);
                f.Tr.localPosition = f.Home - Vector3.forward * f.Recoil;
            }
        }

        private void Volley(TroopType type, TroopDef def, int alive, ZombieAgent target)
        {
            float dmg = SquadMath.Volley(def, alive, target.Type, _damageMult);
            Vector3 to = target.AimPoint;

            // A few tracers from the figures of this type; the volley itself is one hit.
            int shots = 0;
            foreach (var f in _figures)
            {
                if (f.Type != type || !f.Tr.gameObject.activeSelf) continue;
                Vector3 from = f.Tr.position + Vector3.up * (type == TroopType.Vehicle ? 1.0f : 0.9f);
                _ctx.Feedback.Tracer(from, to + Random.insideUnitSphere * 0.15f,
                    type == TroopType.Vehicle ? new Color(1f, 0.55f, 0.25f) : new Color(0.95f, 0.9f, 0.65f),
                    type == TroopType.Vehicle ? 0.12f : 0.04f);
                f.Recoil = type == TroopType.Vehicle ? 0.15f : 0.06f;
                if (++shots >= 3) break;
            }
            _ctx.Feedback.Sfx.Play(type == TroopType.Vehicle ? SfxId.Explosion : SfxId.TowerShot, 0.2f);
            float counter = def.CounterAgainst(target.Type);
            _ctx.Zombies.ApplyDamage(target, dmg, counter > 1.01f, to - _post, type == TroopType.Vehicle ? 2.5f : 0.8f, DamageStyle.Troop);
        }

        // ---------------------------------------------------------------- figures

        private void BuildFigures()
        {
            foreach (var f in _figures) Object.Destroy(f.Tr.gameObject);
            _figures.Clear();
            if (StartTotal <= 0) return;

            // Representative figures: each type gets a share of MaxFigures (at least one if present).
            var m = _ctx.Materials;
            int slot = 0;
            foreach (var type in TroopRules.FrontOrder)
            {
                int count = _start[(int)type];
                if (count <= 0) continue;
                int shown = Mathf.Clamp(Mathf.RoundToInt((float)count / StartTotal * MaxFigures), 1, type == TroopType.Vehicle ? 2 : MaxFigures);
                for (int i = 0; i < shown; i++, slot++)
                {
                    var go = new GameObject(type.ToString());
                    go.transform.SetParent(_root, false);
                    var mat = m.Lit(TypeColors[(int)type]);
                    if (type == TroopType.Vehicle)
                    {
                        m.Primitive(PrimitiveType.Cube, "Hull", go.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.0f, 0.6f, 1.4f), mat);
                        m.Primitive(PrimitiveType.Cube, "Turret", go.transform, new Vector3(0f, 0.9f, 0.1f), new Vector3(0.5f, 0.3f, 0.6f), mat);
                        m.Primitive(PrimitiveType.Cube, "Gun", go.transform, new Vector3(0f, 0.95f, 0.6f), new Vector3(0.12f, 0.12f, 0.7f),
                            m.Lit(new Color(0.15f, 0.15f, 0.15f)));
                    }
                    else
                    {
                        m.Primitive(PrimitiveType.Capsule, "Body", go.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.4f, 0.5f, 0.4f), mat);
                        m.Primitive(PrimitiveType.Sphere, "Helmet", go.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.32f, 0.22f, 0.32f),
                            m.Lit(TypeColors[(int)type] * 0.7f));
                        m.Primitive(PrimitiveType.Cube, "Rifle", go.transform, new Vector3(0.14f, 0.7f, 0.25f),
                            new Vector3(0.06f, 0.06f, type == TroopType.Shooter ? 0.75f : 0.5f), m.Lit(new Color(0.15f, 0.15f, 0.15f)));
                    }
                    var home = FormationSpot(slot, type);
                    go.transform.localPosition = home;
                    _figures.Add(new Figure { Type = type, Tr = go.transform, Home = home });
                }
            }
        }

        /// <summary>Front row (infantry/vehicles) ahead of the post, shooters behind.</summary>
        private static Vector3 FormationSpot(int slot, TroopType type)
        {
            float row = type == TroopType.Shooter ? -0.6f : 0.5f;
            if (type == TroopType.Vehicle) row = 1.1f;
            float x = ((slot % 5) - 2) * 0.55f;
            return new Vector3(x, 0f, row + (slot / 5) * -0.45f);
        }

        /// <summary>Hides figures in proportion to losses of each type.</summary>
        private void SyncFigures()
        {
            for (int t = 0; t < TroopRules.TypeCount; t++)
            {
                var type = (TroopType)t;
                int total = 0;
                foreach (var f in _figures) if (f.Type == type) total++;
                if (total == 0) continue;
                int start = Mathf.Max(1, _start[t]);
                int visible = Mathf.CeilToInt((float)_state.Alive(type) / start * total);
                foreach (var f in _figures)
                {
                    if (f.Type != type) continue;
                    if (f.Tr.gameObject.activeSelf && visible <= 0) f.Tr.gameObject.SetActive(false);
                    else if (f.Tr.gameObject.activeSelf) visible--;
                }
            }
        }
    }
}
