using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// The single M0 hero: tap-to-move, auto-fires at the nearest zombie, respawns at the HQ when downed.
    /// </summary>
    public sealed class HeroController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private BattleContext _ctx;
        private BattleConfig _cfg;
        private Transform _visual;
        private Transform _muzzle;
        private Transform _muzzleFlash;
        private Transform _moveMarker;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _hurtBlock;
        private Vector3 _spawn;
        private Vector3 _moveTarget;
        private bool _hasMoveTarget;
        private float _fireTimer;
        private float _regenDelay;
        private float _respawnTimer;
        private float _hurtTimer;
        private float _phase;
        private float _flashTimer;
        private float _recoil;

        /// <summary>Set from the clinic level before each night.</summary>
        public float RegenMultiplier { get; set; } = 1f;
        public float RespawnMultiplier { get; set; } = 1f;

        public float MaxHp => _stats.MaxHp;
        public float SkillPower => _stats.SkillPower;
        public string HeroName { get; private set; } = "";
        private HeroCombatStats _stats = new HeroCombatStats(300f, 13f, 0.25f, 7.5f, 5.5f, 0.1f, 1f);
        private Renderer _bodyRenderer;
        public float Hp { get; private set; }
        public bool IsAlive => _respawnTimer <= 0f;
        public float RespawnRemaining => Mathf.Max(0f, _respawnTimer);
        public Vector3 Position => transform.position;

        public void Init(BattleContext ctx, Vector3 spawn)
        {
            _ctx = ctx;
            _cfg = ctx.Config;
            _spawn = spawn;
            var m = ctx.Materials;

            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            var suit = m.Lit(new Color(0.20f, 0.42f, 0.78f));
            var dark = m.Lit(new Color(0.15f, 0.16f, 0.18f));
            var skin = m.Lit(new Color(0.93f, 0.76f, 0.6f));
            _bodyRenderer = m.Primitive(PrimitiveType.Capsule, "Body", _visual, new Vector3(0f, 0.75f, 0f), new Vector3(0.75f, 0.75f, 0.75f), suit)
                .GetComponent<Renderer>();
            m.Primitive(PrimitiveType.Sphere, "Head", _visual, new Vector3(0f, 1.62f, 0f), new Vector3(0.45f, 0.45f, 0.45f), skin);
            m.Primitive(PrimitiveType.Cube, "Backpack", _visual, new Vector3(0f, 0.95f, -0.38f), new Vector3(0.5f, 0.55f, 0.25f), dark);
            m.Primitive(PrimitiveType.Cube, "Gun", _visual, new Vector3(0.25f, 1.0f, 0.45f), new Vector3(0.12f, 0.14f, 0.8f), dark);
            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_visual, false);
            _muzzle.localPosition = new Vector3(0.25f, 1.0f, 0.9f);
            _muzzleFlash = m.Primitive(PrimitiveType.Cube, "MuzzleFlash", _muzzle, Vector3.zero, Vector3.one * 0.3f,
                m.Unlit(new Color(4f, 3f, 1f))).transform;
            _muzzleFlash.gameObject.SetActive(false);

            var ring = m.Primitive(PrimitiveType.Cylinder, "Ring", transform, new Vector3(0f, 0.02f, 0f), new Vector3(1.3f, 0.01f, 1.3f),
                m.Unlit(new Color(0.3f, 0.7f, 1.6f)));
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _moveMarker = m.Primitive(PrimitiveType.Cylinder, "MoveMarker", ctx.WorldRoot, Vector3.zero, new Vector3(0.6f, 0.01f, 0.6f),
                m.Unlit(new Color(0.4f, 0.9f, 1.8f))).transform;
            _moveMarker.gameObject.SetActive(false);

            _renderers = _visual.GetComponentsInChildren<Renderer>(true);
            _hurtBlock = new MaterialPropertyBlock();
            _hurtBlock.SetColor(BaseColorId, new Color(1.6f, 0.3f, 0.3f));
            ResetState();
        }

        /// <summary>Sets the night's leader: stats (stars applied) and body colour from heroes.csv.</summary>
        public void ApplyHero(string heroName, HeroCombatStats stats, Color color)
        {
            HeroName = heroName;
            _stats = stats;
            if (_bodyRenderer != null) _bodyRenderer.sharedMaterial = _ctx.Materials.Lit(color);
            Hp = MaxHp;
        }

        public void ResetState()
        {
            Hp = MaxHp;
            _respawnTimer = 0f;
            _hasMoveTarget = false;
            _moveMarker.gameObject.SetActive(false);
            transform.position = _spawn;
            transform.rotation = Quaternion.LookRotation(Vector3.forward);
            _visual.gameObject.SetActive(true);
            SetHurt(false);
        }

        public void CommandMove(Vector3 worldPos)
        {
            if (!IsAlive) return;
            _moveTarget = MapLayout.ClampToMap(worldPos, 0.5f);
            _hasMoveTarget = true;
            _moveMarker.position = _moveTarget + new Vector3(0f, 0.03f, 0f);
            _moveMarker.localScale = new Vector3(1.0f, 0.01f, 1.0f);
            _moveMarker.gameObject.SetActive(true);
        }

        public void Tick(float dt)
        {
            if (_hurtTimer > 0f)
            {
                _hurtTimer -= dt;
                if (_hurtTimer <= 0f) SetHurt(false);
            }

            if (_moveMarker.gameObject.activeSelf)
            {
                var s = _moveMarker.localScale;
                float k = Mathf.MoveTowards(s.x, 0.4f, dt * 2f);
                _moveMarker.localScale = new Vector3(k, 0.01f, k);
            }

            if (!IsAlive)
            {
                _respawnTimer -= dt;
                if (_respawnTimer <= 0f)
                {
                    ResetState();
                    _ctx.Feedback.Fire(_spawn, 12, 0.6f, 1.5f, 0.2f);
                }
                return;
            }

            // Movement with wall sliding.
            bool moving = false;
            Vector3 pos = transform.position;
            if (_hasMoveTarget)
            {
                Vector3 to = _moveTarget - pos;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.08f)
                {
                    _hasMoveTarget = false;
                    _moveMarker.gameObject.SetActive(false);
                }
                else
                {
                    Vector3 step = to / dist * Mathf.Min(dist, _stats.MoveSpeed * dt);
                    Vector3 next = pos + step;
                    var map = _ctx.Map;
                    if (!map.IsWalkableForHero(next))
                    {
                        Vector3 nx = new Vector3(next.x, 0f, pos.z);
                        Vector3 nz = new Vector3(pos.x, 0f, next.z);
                        if (map.IsWalkableForHero(nx)) next = nx;
                        else if (map.IsWalkableForHero(nz)) next = nz;
                        else { next = pos; _hasMoveTarget = false; _moveMarker.gameObject.SetActive(false); }
                    }
                    moving = (next - pos).sqrMagnitude > 1e-6f;
                    pos = next;
                    transform.position = pos;
                }
            }

            // Regen when out of combat.
            if (_regenDelay > 0f) _regenDelay -= dt;
            else Hp = Mathf.Min(MaxHp, Hp + _cfg.HeroRegenPerSecond * RegenMultiplier * dt);

            // Auto-fire at the nearest zombie.
            var target = _ctx.Zombies.FindNearest(pos, _stats.Range);
            Vector3 face = moving ? (_moveTarget - pos) : Vector3.zero;
            if (target != null) face = target.Pos - pos;
            face.y = 0f;
            if (face.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), 900f * dt);

            _fireTimer -= dt;
            if (target != null && _fireTimer <= 0f)
            {
                _fireTimer = _stats.FireInterval;
                Shoot(target);
            }

            // Procedural animation: run bob, recoil, muzzle flash.
            _phase += dt * (moving ? 14f : 3f);
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 2.5f);
            float bob = moving ? Mathf.Abs(Mathf.Sin(_phase)) * 0.12f : Mathf.Sin(_phase) * 0.02f;
            _visual.localPosition = new Vector3(0f, bob, -_recoil);
            _visual.localRotation = Quaternion.Euler(moving ? 8f : 0f, 0f, moving ? Mathf.Sin(_phase) * 4f : 0f);

            if (_flashTimer > 0f)
            {
                _flashTimer -= dt;
                if (_flashTimer <= 0f) _muzzleFlash.gameObject.SetActive(false);
            }
        }

        private void Shoot(ZombieAgent target)
        {
            var hit = CombatRules.RollHit(_stats.Damage, _stats.CritChance, _cfg.HeroCritMultiplier, _ctx.Roll());
            Vector3 from = _muzzle.position;
            Vector3 to = target.AimPoint + Random.insideUnitSphere * 0.1f;

            var fx = _ctx.Feedback;
            fx.Tracer(from, to, hit.IsCritical ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.95f, 0.75f), hit.IsCritical ? 0.12f : 0.07f);
            fx.Sfx.Play(SfxId.HeroShot);
            if (hit.IsCritical) fx.Shake(0.08f);

            _muzzleFlash.gameObject.SetActive(true);
            _muzzleFlash.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));
            _flashTimer = 0.04f;
            _recoil = 0.12f;

            _ctx.Zombies.ApplyDamage(target, hit.Damage, hit.IsCritical, to - from, hit.IsCritical ? 3f : 1.4f, DamageStyle.Hero);
        }

        public void TakeDamage(float damage)
        {
            if (!IsAlive) return;
            Hp -= damage;
            _regenDelay = 3f;
            _hurtTimer = 0.1f;
            SetHurt(true);
            _ctx.Feedback.Sfx.Play(SfxId.HeroHurt);
            _ctx.Feedback.Shake(0.1f);

            if (Hp <= 0f)
            {
                Hp = 0f;
                _respawnTimer = _cfg.HeroRespawnSeconds * RespawnMultiplier;
                _hasMoveTarget = false;
                _moveMarker.gameObject.SetActive(false);
                _visual.gameObject.SetActive(false);
                _ctx.Feedback.Chunks(transform.position + Vector3.up, 16, new Color(0.2f, 0.42f, 0.78f), new Color(0.5f, 0.1f, 0.1f), 5f, 0.18f);
                _ctx.Feedback.Shake(0.35f);
            }
        }

        private void SetHurt(bool on)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i].transform == _muzzleFlash) continue;
                _renderers[i].SetPropertyBlock(on ? _hurtBlock : null);
            }
        }
    }
}
