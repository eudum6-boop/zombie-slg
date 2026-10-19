using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZombieSLG.Battle;
using ZombieSLG.Infrastructure;

namespace ZombieSLG.EditorTools
{
    /// <summary>
    /// One-click project setup for the M0 prototype.
    /// The project was created from the URP 2D template; the GDD calls for a 3D quarter-view,
    /// so this creates a URP asset with the Universal (3D) renderer and makes it the default.
    /// </summary>
    public static class ProjectSetupMenu
    {
        private const string SettingsDir = "Assets/_Project/Settings";
        private const string RendererPath = SettingsDir + "/URP_3D_Renderer.asset";
        private const string PipelinePath = SettingsDir + "/URP_3D.asset";
        private const string ScenesDir = "Assets/_Project/Scenes";
        private const string BattleScenePath = ScenesDir + "/Battle_M0.unity";

        [MenuItem("ZombieSLG/0. 전체 설정 (렌더러 + 씬 생성)", priority = 0)]
        public static void SetupAll()
        {
            SetupRenderer();
            CreateBattleScene();
        }

        [MenuItem("ZombieSLG/1. 3D 렌더러(URP Universal) 설정", priority = 1)]
        public static void SetupRenderer()
        {
            EnsureFolder(SettingsDir);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null) rendererData = CreateRendererData();
            if (rendererData == null)
            {
                EditorUtility.DisplayDialog("ZombieSLG",
                    "Universal Renderer 에셋을 자동으로 만들지 못했습니다.\n\n" +
                    "Project 창에서 Create ▸ Rendering ▸ URP Asset (with Universal Renderer)를 만든 뒤\n" +
                    "Project Settings ▸ Graphics 의 Default Render Pipeline 에 지정해 주세요.", "확인");
                return;
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            pipeline.shadowDistance = 90f;
            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = true;
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            // Quality levels may override the default; point them all at the 3D pipeline.
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline != null) QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            AssetDatabase.SaveAssets();
            Debug.Log($"[ZombieSLG] 3D URP 렌더러 설정 완료: {PipelinePath}");
        }

        private static UniversalRendererData CreateRendererData()
        {
            // URP's own factory (internal) fills in shader/post-process resources. Try it first.
            try
            {
                var method = typeof(UniversalRenderPipelineAsset).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "CreateRendererAsset" && m.GetParameters().Length >= 2);
                if (method != null)
                {
                    var ps = method.GetParameters();
                    var args = new object[ps.Length];
                    args[0] = RendererPath;
                    args[1] = Enum.Parse(ps[1].ParameterType, "UniversalRenderer");
                    for (int i = 2; i < ps.Length; i++)
                    {
                        if (ps[i].ParameterType == typeof(bool)) args[i] = false; // relativePath = false
                        else args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    }
                    method.Invoke(null, args);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    var created = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                    if (created != null) return created;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ZombieSLG] URP internal factory unavailable, falling back: " + e.Message);
            }

            // Fallback: plain instance. URP fills missing defaults on load in the editor.
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, RendererPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }

        [MenuItem("ZombieSLG/2. M0 전투 씬 생성 및 열기", priority = 2)]
        public static void CreateBattleScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(ScenesDir);
            Scene scene;
            if (File.Exists(BattleScenePath))
            {
                scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("BattleBootstrap").AddComponent<BattleBootstrap>();
                EditorSceneManager.SaveScene(scene, BattleScenePath);
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != BattleScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(BattleScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[ZombieSLG] 전투 씬 준비 완료. Play 버튼을 누르세요. (Game 뷰를 1080x1920 같은 세로 해상도로 두면 실제 화면과 같습니다)");
        }

        [MenuItem("ZombieSLG/진행 데이터 초기화 (새 게임)", priority = 20)]
        public static void ResetProgress()
        {
            if (UnityEngine.Application.isPlaying)
            {
                EditorUtility.DisplayDialog("ZombieSLG", "플레이를 멈춘 뒤에 초기화하세요. (실행 중에는 종료할 때 다시 저장됩니다)", "확인");
                return;
            }
            new LocalSaveRepository().Delete();
            foreach (var k in new[] { "zslg.night", "zslg.food", "zslg.fuel", "zslg.materials", "zslg.shards" }) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
            Debug.Log("[ZombieSLG] 세이브를 삭제했습니다. 다음 Play는 새 게임으로 시작합니다.");
        }

        [MenuItem("ZombieSLG/세이브 폴더 열기", priority = 21)]
        public static void OpenSaveFolder()
        {
            EditorUtility.RevealInFinder(LocalSaveRepository.DefaultPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
