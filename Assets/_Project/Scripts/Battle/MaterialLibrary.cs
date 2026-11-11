using System.Collections.Generic;
using UnityEngine;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Creates and caches flat-colour materials for the placeholder low-poly look.
    /// One material per colour keeps the SRP Batcher happy. Replaced by asset-pack materials later.
    /// </summary>
    public sealed class MaterialLibrary
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Shader _lit;
        private readonly Shader _unlit;
        private readonly Shader _particle;
        private readonly Dictionary<Color, Material> _litCache = new Dictionary<Color, Material>();
        private readonly Dictionary<Color, Material> _unlitCache = new Dictionary<Color, Material>();

        public Mesh CubeMesh { get; }
        public Mesh SphereMesh { get; }
        public Mesh CylinderMesh { get; }

        public MaterialLibrary()
        {
            // Template materials under Resources/ShaderRefs keep these shaders (and their instancing/emission
            // variants) in player builds; Shader.Find alone returns null for stripped shaders on device.
            _lit = FromTemplate("Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _unlit = FromTemplate("Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _particle = FromTemplate("ParticleUnlit") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? _unlit;
            if (_lit == null || _unlit == null)
                throw new System.InvalidOperationException(
                    "URP 셰이더가 빌드에 포함되지 않았습니다 (Resources/ShaderRefs 머티리얼 확인)");

            CubeMesh = GrabMesh(PrimitiveType.Cube);
            SphereMesh = GrabMesh(PrimitiveType.Sphere);
            CylinderMesh = GrabMesh(PrimitiveType.Cylinder);
        }

        private static Shader FromTemplate(string name)
        {
            var m = Resources.Load<Material>("ShaderRefs/" + name);
            return m != null ? m.shader : null;
        }

        private static Mesh GrabMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return mesh;
        }

        public Material Lit(Color color, float smoothness = 0.1f)
        {
            if (_litCache.TryGetValue(color, out var m)) return m;
            m = new Material(_lit) { enableInstancing = true, name = "Lit_" + ColorUtility.ToHtmlStringRGB(color) };
            SetColor(m, color);
            if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, smoothness);
            _litCache[color] = m;
            return m;
        }

        /// <summary>Lit material with emission, for windows, muzzle flashes and fire.</summary>
        public Material Emissive(Color baseColor, Color emission)
        {
            var m = new Material(_lit) { enableInstancing = true, name = "Emissive" };
            SetColor(m, baseColor);
            if (m.HasProperty(EmissionColorId))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor(EmissionColorId, emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return m;
        }

        /// <summary>Unlit; HDR colours (&gt;1) bloom with post-processing.</summary>
        public Material Unlit(Color color)
        {
            if (_unlitCache.TryGetValue(color, out var m)) return m;
            m = new Material(_unlit) { enableInstancing = true, name = "Unlit" };
            SetColor(m, color);
            _unlitCache[color] = m;
            return m;
        }

        /// <summary>Particle/line material; vertex colours are multiplied by <paramref name="tint"/> (HDR allowed).</summary>
        public Material Particle(Color tint)
        {
            var m = new Material(_particle) { name = "ParticleUnlit" };
            SetColor(m, tint);
            return m;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
        }

        /// <summary>Creates a primitive without its collider (we never use physics).</summary>
        public GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
    }
}
