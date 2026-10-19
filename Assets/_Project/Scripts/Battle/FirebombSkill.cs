using System.Collections.Generic;
using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// The hero's active skill. Tap the button → aim (marker follows the pointer) → tap the ground.
    /// A bomb falls, explodes with falloff damage + knockback, then leaves a burning zone (DoT).
    /// </summary>
    public sealed class FirebombSkill
    {
        private const float FallTime = 0.35f;
        private const float BurnTickInterval = 0.25f;

        private readonly BattleContext _ctx;
        private readonly BattleConfig _cfg;
        private readonly Transform _marker;
        private readonly Transform _bomb;
        private readonly Transform _burnZone;
        private readonly Light _flashLight;
        private readonly List<ZombieAgent> _hits = new List<ZombieAgent>(128);

        private Vector3 _aim;
        private Vector3 _impact;
        private float _fallTimer = -1f;
        private float _burnTimer;
        private float _burnTick;
        private float _fireFxTimer;
        private float _lightTimer;

        public bool IsTargeting { get; private set; }
        public float CooldownRemaining { get; private set; }
        public float Cooldown => _cfg.SkillCooldown;
        public bool IsReady => CooldownRemaining <= 0f;

        public FirebombSkill(BattleContext ctx)
        {
            _ctx = ctx;
            _cfg = ctx.Config;
            var m = ctx.Materials;
            float d = _cfg.SkillRadius * 2f;

            _marker = m.Primitive(PrimitiveType.Cylinder, "SkillMarker", ctx.WorldRoot, Vector3.zero, new Vector3(d, 0.01f, d),
                m.Unlit(new Color(1.6f, 0.35f, 0.1f))).transform;
            NoShadow(_marker);
            m.Primitive(PrimitiveType.Cylinder, "Inner", _marker, new Vector3(0f, 0.6f, 0f), new Vector3(0.9f, 1f, 0.9f),
                m.Unlit(new Color(0.25f, 0.07f, 0.04f)));
            NoShadow(_marker.Find("Inner"));
            _marker.gameObject.SetActive(false);

            _bomb = m.Primitive(PrimitiveType.Sphere, "Bomb", ctx.WorldRoot, Vector3.zero, Vector3.one * 0.6f,
                m.Emissive(new Color(0.2f, 0.2f, 0.2f), new Color(3f, 0.8f, 0.2f))).transform;
            _bomb.gameObject.SetActive(false);

            _burnZone = m.Primitive(PrimitiveType.Cylinder, "BurnZone", ctx.WorldRoot, Vector3.zero, new Vector3(d, 0.01f, d),
                m.Unlit(new Color(0.9f, 0.25f, 0.05f))).transform;
            NoShadow(_burnZone);
            _burnZone.gameObject.SetActive(false);

            var lightGo = new GameObject("ExplosionLight");
            lightGo.transform.SetParent(ctx.WorldRoot, false);
            _flashLight = lightGo.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.color = new Color(1f, 0.6f, 0.25f);
            _flashLight.range = 12f;
            _flashLight.intensity = 0f;
            _flashLight.enabled = false;
        }

        private static void NoShadow(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void ToggleTargeting()
        {
            if (IsTargeting) { CancelTargeting(); return; }
            if (!IsReady) return;
            IsTargeting = true;
            if (_ctx.Hero != null) _aim = _ctx.Hero.Position + new Vector3(0f, 0f, 4f);
            _marker.position = _aim + new Vector3(0f, 0.04f, 0f);
            _marker.gameObject.SetActive(true);
        }

        public void CancelTargeting()
        {
            IsTargeting = false;
            _marker.gameObject.SetActive(false);
        }

        public void Aim(Vector3 ground)
        {
            if (!IsTargeting) return;
            _aim = MapLayout.ClampToMap(ground);
            _marker.position = _aim + new Vector3(0f, 0.04f, 0f);
        }

        public void Cast(Vector3 ground)
        {
            if (!IsTargeting || !IsReady) return;
            Aim(ground);
            IsTargeting = false;
            _marker.gameObject.SetActive(false);

            CooldownRemaining = _cfg.SkillCooldown;
            _impact = _aim;
            _fallTimer = FallTime;
            _bomb.position = _impact + new Vector3(0f, 14f, 0f);
            _bomb.gameObject.SetActive(true);
            _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
        }

        public void Tick(float dt)
        {
            if (CooldownRemaining > 0f) CooldownRemaining = Mathf.Max(0f, CooldownRemaining - dt);

            if (IsTargeting)
            {
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.03f;
                float d = _cfg.SkillRadius * 2f * pulse;
                _marker.localScale = new Vector3(d, 0.01f, d);
            }

            if (_fallTimer >= 0f)
            {
                _fallTimer -= dt;
                float t = 1f - Mathf.Clamp01(_fallTimer / FallTime);
                _bomb.position = _impact + new Vector3(0f, Mathf.Lerp(14f, 0.3f, t * t), 0f);
                if (_fallTimer <= 0f)
                {
                    _fallTimer = -1f;
                    _bomb.gameObject.SetActive(false);
                    Explode();
                }
            }

            if (_burnTimer > 0f)
            {
                _burnTimer -= dt;
                _burnTick -= dt;
                if (_burnTick <= 0f)
                {
                    _burnTick += BurnTickInterval;
                    _ctx.Zombies.QueryRadius(_impact, _cfg.SkillRadius, _hits);
                    float dmg = _cfg.SkillBurnDps * BurnTickInterval;
                    for (int i = 0; i < _hits.Count; i++)
                        _ctx.Zombies.ApplyDamage(_hits[i], dmg, false, Vector3.zero, 0f, DamageStyle.Burn);
                }

                _fireFxTimer -= dt;
                if (_fireFxTimer <= 0f)
                {
                    _fireFxTimer = 0.05f;
                    _ctx.Feedback.Fire(_impact, 3, _cfg.SkillRadius * 0.9f, 2f, 0.3f);
                }

                float k = Mathf.Clamp01(_burnTimer / _cfg.SkillBurnDuration);
                float d = _cfg.SkillRadius * 2f * (0.6f + 0.4f * k);
                _burnZone.localScale = new Vector3(d, 0.01f, d);
                if (_burnTimer <= 0f) _burnZone.gameObject.SetActive(false);
            }

            if (_lightTimer > 0f)
            {
                _lightTimer -= dt;
                _flashLight.intensity = Mathf.Max(0f, _lightTimer / 0.4f) * 25f;
                if (_lightTimer <= 0f) _flashLight.enabled = false;
            }
        }

        private void Explode()
        {
            var zombies = _ctx.Zombies;
            zombies.QueryRadius(_impact, _cfg.SkillRadius, _hits);
            for (int i = 0; i < _hits.Count; i++)
            {
                var z = _hits[i];
                Vector3 away = z.Pos - _impact;
                away.y = 0f;
                float dist = away.magnitude;
                float dmg = CombatRules.AreaDamage(_cfg.SkillDamage, dist, _cfg.SkillRadius);
                Vector3 dir = dist > 0.01f ? away / dist : Random.insideUnitSphere;
                zombies.ApplyDamage(z, dmg, dist < _cfg.SkillRadius * 0.35f, dir, 9f, DamageStyle.Skill);
            }

            var fx = _ctx.Feedback;
            fx.Shake(0.65f);
            fx.HitStop(0.06f);
            fx.Sfx.Play(SfxId.Explosion, 0.05f);
            fx.Fire(_impact, 70, _cfg.SkillRadius * 0.6f, 5f, 0.45f);
            fx.Chunks(_impact + Vector3.up * 0.3f, 24, new Color(0.25f, 0.22f, 0.2f), new Color(0.1f, 0.1f, 0.1f), 8f, 0.2f);

            _flashLight.transform.position = _impact + new Vector3(0f, 2f, 0f);
            _flashLight.enabled = true;
            _lightTimer = 0.4f;

            _burnTimer = _cfg.SkillBurnDuration;
            _burnTick = BurnTickInterval;
            _burnZone.position = _impact + new Vector3(0f, 0.03f, 0f);
            _burnZone.gameObject.SetActive(true);
        }

        public void ResetState()
        {
            CancelTargeting();
            CooldownRemaining = 0f;
            _fallTimer = -1f;
            _burnTimer = 0f;
            _lightTimer = 0f;
            _flashLight.enabled = false;
            _bomb.gameObject.SetActive(false);
            _burnZone.gameObject.SetActive(false);
        }
    }
}
