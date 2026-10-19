using UnityEngine;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Fixed quarter-view orthographic camera. Auto-fits the map to any aspect (portrait first)
    /// and adds trauma-based screen shake.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public float Pitch = 55f;
        public float Yaw = 0f;
        public float Distance = 60f;
        public float MaxShakeOffset = 0.7f;
        public float MaxShakeAngle = 1.5f;
        public float TraumaDecay = 1.6f;

        private Camera _cam;
        private Vector3 _focus;
        private float _halfWidth;
        private float _halfDepth;
        private float _trauma;
        private float _seed;

        public Camera Camera => _cam;

        public void Init(Camera cam, Vector3 focus, float halfWidth, float halfDepth)
        {
            _cam = cam;
            _focus = focus;
            _halfWidth = halfWidth;
            _halfDepth = halfDepth;
            _seed = Random.value * 100f;
            _cam.orthographic = true;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 200f;
            Apply(Vector3.zero, 0f);
        }

        /// <summary>Adds shake. 0.2 = light hit, 0.6 = explosion. Effect scales with trauma².</summary>
        public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

        private void LateUpdate()
        {
            if (_cam == null) return;

            float pitchRad = Pitch * Mathf.Deg2Rad;
            float aspect = Mathf.Max(0.1f, _cam.aspect);
            float size = Mathf.Max(_halfWidth / aspect, _halfDepth * Mathf.Sin(pitchRad));
            _cam.orthographicSize = size * 1.02f;

            Vector3 offset = Vector3.zero;
            float roll = 0f;
            if (_trauma > 0f)
            {
                float shake = _trauma * _trauma;
                float t = Time.unscaledTime * 25f;
                offset = new Vector3(
                    (Mathf.PerlinNoise(_seed, t) * 2f - 1f),
                    (Mathf.PerlinNoise(_seed + 10f, t) * 2f - 1f),
                    0f) * (MaxShakeOffset * shake);
                roll = (Mathf.PerlinNoise(_seed + 20f, t) * 2f - 1f) * MaxShakeAngle * shake;
                _trauma = Mathf.Max(0f, _trauma - TraumaDecay * Time.unscaledDeltaTime);
            }
            Apply(offset, roll);
        }

        private void Apply(Vector3 localOffset, float roll)
        {
            Quaternion rot = Quaternion.Euler(Pitch, Yaw, roll);
            transform.rotation = rot;
            transform.position = _focus - rot * Vector3.forward * Distance + rot * localOffset;
        }

        /// <summary>Projects a screen point onto the ground plane (y = 0).</summary>
        public bool ScreenToGround(Vector2 screen, out Vector3 point)
        {
            point = Vector3.zero;
            if (_cam == null) return false;
            Ray ray = _cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            point = ray.origin + ray.direction * t;
            point.y = 0f;
            return true;
        }
    }
}
