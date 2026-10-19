using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>Machine-gun tower: rotating head, auto-targets nearest zombie in range.</summary>
    public sealed class TowerController : MonoBehaviour
    {
        private BattleContext _ctx;
        private Transform _head;
        private Transform _muzzle;
        private float _fireTimer;
        private float _recoil;
        private bool _alternate;
        private float _damageMult = 1f;
        private float _rangeBonus;
        private float _intervalMult = 1f;

        public int Level { get; private set; }
        public bool IsBuilt => Level > 0;

        public void Init(BattleContext ctx)
        {
            _ctx = ctx;
            var m = ctx.Materials;
            var metal = m.Lit(new Color(0.35f, 0.37f, 0.33f));
            var dark = m.Lit(new Color(0.16f, 0.17f, 0.17f));
            var sand = m.Lit(new Color(0.62f, 0.55f, 0.40f));

            m.Primitive(PrimitiveType.Cylinder, "Sandbags", transform, new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.3f, 1.6f), sand);
            m.Primitive(PrimitiveType.Cube, "Pillar", transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 0.9f, 0.5f), metal);

            _head = new GameObject("Head").transform;
            _head.SetParent(transform, false);
            _head.localPosition = new Vector3(0f, 1.45f, 0f);
            m.Primitive(PrimitiveType.Cube, "Body", _head, Vector3.zero, new Vector3(0.7f, 0.4f, 0.8f), metal);
            m.Primitive(PrimitiveType.Cube, "BarrelL", _head, new Vector3(-0.15f, 0f, 0.65f), new Vector3(0.1f, 0.1f, 0.7f), dark);
            m.Primitive(PrimitiveType.Cube, "BarrelR", _head, new Vector3(0.15f, 0f, 0.65f), new Vector3(0.1f, 0.1f, 0.7f), dark);
            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_head, false);
            _muzzle.localPosition = new Vector3(0f, 0f, 1.0f);
        }

        /// <summary>Applies the base building level. Level 0 = not built (hidden, never fires).</summary>
        public void SetLevel(int level)
        {
            Level = level;
            var defense = _ctx.Data.Defense;
            _damageMult = defense.TowerDamageMultiplier(level);
            _rangeBonus = defense.TowerRangeBonus(level);
            _intervalMult = defense.TowerIntervalMultiplier(level);
            gameObject.SetActive(level > 0);
            float s = 1f + 0.06f * Mathf.Max(0, level - 1);
            transform.localScale = new Vector3(s, s, s);
        }

        public void ResetState()
        {
            _fireTimer = 0f;
            _head.localRotation = Quaternion.identity;
        }

        public void Tick(float dt)
        {
            var cfg = _ctx.Config;
            if (!IsBuilt) return;
            var target = _ctx.Zombies.FindNearest(transform.position, cfg.TowerRange + _rangeBonus);

            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 2f);
            _head.localPosition = new Vector3(0f, 1.45f, 0f) - _head.localRotation * Vector3.forward * _recoil;

            if (target == null) return;

            Vector3 dir = target.Pos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                _head.rotation = Quaternion.RotateTowards(_head.rotation, Quaternion.LookRotation(dir), 720f * dt);

            _fireTimer -= dt;
            if (_fireTimer > 0f) return;
            _fireTimer = cfg.TowerFireInterval * _intervalMult;

            var hit = CombatRules.RollHit(cfg.TowerDamage * _damageMult, cfg.TowerCritChance, cfg.TowerCritMultiplier, _ctx.Roll());
            _alternate = !_alternate;
            Vector3 from = _muzzle.position + _head.right * (_alternate ? 0.15f : -0.15f);
            Vector3 to = target.AimPoint + Random.insideUnitSphere * 0.12f;
            _ctx.Feedback.Tracer(from, to, new Color(1f, 0.7f, 0.35f), 0.05f);
            _ctx.Feedback.Sfx.Play(SfxId.TowerShot, 0.12f);
            _recoil = 0.08f;
            _ctx.Zombies.ApplyDamage(target, hit.Damage, hit.IsCritical, to - from, 0.8f, DamageStyle.Tower);
        }
    }
}
