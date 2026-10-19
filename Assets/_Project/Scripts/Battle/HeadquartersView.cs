using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>The base the player must keep alive for the whole night.</summary>
    public sealed class HeadquartersView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private BattleContext _ctx;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _flash;
        private float _flashTimer;
        private float _shakeCooldown;
        private Transform _visual;

        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public float HpRatio => MaxHp <= 0f ? 0f : Mathf.Clamp01(Hp / MaxHp);
        public bool IsDestroyed => Hp <= 0f;
        public float LastHitTime { get; private set; } = -99f;

        public void Init(BattleContext ctx)
        {
            _ctx = ctx;
            var m = ctx.Materials;
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);

            var concrete = m.Lit(new Color(0.52f, 0.54f, 0.56f));
            var dark = m.Lit(new Color(0.22f, 0.24f, 0.27f));
            var accent = m.Lit(new Color(0.85f, 0.55f, 0.15f));
            m.Primitive(PrimitiveType.Cube, "Base", _visual, new Vector3(0f, 0.8f, 0f), new Vector3(3.7f, 1.6f, 3.7f), concrete);
            m.Primitive(PrimitiveType.Cube, "Top", _visual, new Vector3(0f, 2.05f, 0f), new Vector3(2.5f, 0.9f, 2.5f), dark);
            m.Primitive(PrimitiveType.Cube, "Stripe", _visual, new Vector3(0f, 1.25f, 0f), new Vector3(3.75f, 0.18f, 3.75f), accent);
            m.Primitive(PrimitiveType.Cylinder, "Antenna", _visual, new Vector3(0.7f, 3.2f, 0.6f), new Vector3(0.08f, 0.8f, 0.08f), dark);
            m.Primitive(PrimitiveType.Cube, "Window", _visual, new Vector3(0f, 2.1f, -1.26f), new Vector3(1.6f, 0.25f, 0.05f),
                m.Emissive(new Color(0.9f, 0.8f, 0.5f), new Color(2.5f, 1.8f, 0.6f)));
            m.Primitive(PrimitiveType.Cube, "Door", _visual, new Vector3(0f, 0.5f, 1.86f), new Vector3(1.0f, 1.0f, 0.05f), dark);

            _renderers = _visual.GetComponentsInChildren<Renderer>();
            _flash = new MaterialPropertyBlock();
            _flash.SetColor(BaseColorId, new Color(1.4f, 0.35f, 0.3f));
        }

        /// <summary>HQ level makes the building visibly taller.</summary>
        public void SetLevel(int level)
        {
            Level = level;
            _levelScale = 1f + 0.08f * Mathf.Max(0, level - 1);
            _visual.localScale = new Vector3(1f, _levelScale, 1f);
        }

        public int Level { get; private set; } = 1;
        private float _levelScale = 1f;

        public void ResetState(float maxHp)
        {
            MaxHp = maxHp;
            Hp = maxHp;
            _visual.localPosition = Vector3.zero;
            _visual.localRotation = Quaternion.identity;
            _visual.localScale = new Vector3(1f, _levelScale, 1f);
            SetFlash(false);
        }

        public void TakeDamage(float damage)
        {
            if (IsDestroyed) return;
            Hp = Mathf.Max(0f, Hp - damage);
            LastHitTime = Time.time;

            if (_flashTimer <= 0f) SetFlash(true);
            _flashTimer = 0.1f;

            if (_shakeCooldown <= 0f)
            {
                _ctx.Feedback.Shake(0.12f);
                _ctx.Feedback.Sfx.Play(SfxId.HqHit);
                _ctx.Feedback.Chunks(transform.position + new Vector3(Random.Range(-1.5f, 1.5f), 1.2f, Random.Range(-1.5f, 1.5f)), 3,
                    new Color(0.5f, 0.5f, 0.5f), new Color(0.3f, 0.3f, 0.3f), 3f, 0.15f);
                _shakeCooldown = 0.35f;
            }

            if (IsDestroyed) Collapse();
        }

        private void Collapse()
        {
            var fx = _ctx.Feedback;
            fx.Shake(1f);
            fx.HitStop(0.12f);
            fx.Sfx.Play(SfxId.Explosion, 0f);
            fx.Fire(transform.position, 60, 2f, 4f, 0.5f);
            fx.Chunks(transform.position + Vector3.up, 40, new Color(0.5f, 0.5f, 0.5f), new Color(0.2f, 0.2f, 0.2f), 7f, 0.35f);
            _visual.localPosition = new Vector3(0f, -0.9f, 0f);
            _visual.localRotation = Quaternion.Euler(6f, 0f, -4f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_shakeCooldown > 0f) _shakeCooldown -= dt;
            if (_flashTimer > 0f)
            {
                _flashTimer -= dt;
                if (_flashTimer <= 0f) SetFlash(false);
            }
        }

        private void SetFlash(bool on)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].SetPropertyBlock(on ? _flash : null);
        }
    }
}
