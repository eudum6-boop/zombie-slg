using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieSLG.EditorTools
{
    /// <summary>
    /// The game builds every material at runtime with Shader.Find. In a player build Unity only ships shaders
    /// (and keyword variants) that some asset references, so these template materials under Resources keep
    /// URP Lit / Lit+Emission / Unlit / Particles-Unlit — with GPU instancing — in the build.
    /// </summary>
    public static class ShaderReferences
    {
        public const string Folder = "Assets/_Project/Resources/ShaderRefs";

        private static readonly (string file, string shader, bool emission)[] Templates =
        {
            ("Lit", "Universal Render Pipeline/Lit", false),
            ("LitEmissive", "Universal Render Pipeline/Lit", true),
            ("Unlit", "Universal Render Pipeline/Unlit", false),
            ("ParticleUnlit", "Universal Render Pipeline/Particles/Unlit", false),
        };

        [InitializeOnLoadMethod]
        private static void EnsureOnLoad() => EditorApplication.delayCall += () => Ensure(false);

        [MenuItem("ZombieSLG/출시/셰이더 참조 머티리얼 다시 만들기", priority = 45)]
        public static void Rebuild() => Ensure(true);

        public static void Ensure(bool force)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }

            bool changed = false;
            foreach (var (file, shaderName, emission) in Templates)
            {
                string path = $"{Folder}/{file}.mat";
                if (!force && File.Exists(path)) continue;
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning("[ZombieSLG] 셰이더를 찾을 수 없습니다: " + shaderName);
                    continue;
                }
                var mat = new Material(shader) { enableInstancing = true, name = file };
                if (emission)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mat, path);
                changed = true;
            }
            if (!changed) return;
            AssetDatabase.SaveAssets();
            Debug.Log("[ZombieSLG] 셰이더 참조 머티리얼을 만들었습니다 (빌드에 셰이더가 포함되도록): " + Folder);
        }
    }
}
