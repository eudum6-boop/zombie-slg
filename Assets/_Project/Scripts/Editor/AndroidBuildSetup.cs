using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZombieSLG.EditorTools
{
    /// <summary>
    /// M5 release setup in code (reproducible, reviewable in git instead of clicking through Player Settings):
    /// identity, portrait, IL2CPP/ARM64, SDK levels, icon, scene list, and a one-click test APK build.
    /// </summary>
    public static class AndroidBuildSetup
    {
        public const string CompanyName = "eudum6";
        public const string ProductName = "좀비 생존";
        public const string PackageId = "com.eudum6.zombieslg";
        public const string Version = "0.1.0";
        public const int VersionCode = 1;
        public const string IconPath = "Assets/_Project/Art/Icon/app_icon.png";
        public const string ScenePath = "Assets/_Project/Scenes/Battle_M0.unity";
        public const string ApkPath = "Builds/Android/zombie-slg-test.apk";

        [MenuItem("ZombieSLG/출시/1. 안드로이드 설정 적용", priority = 40)]
        public static void Apply()
        {
            string oldDataDir = UnityEngine.Application.persistentDataPath;

            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.Android.bundleVersionCode = VersionCode;

            // Portrait only (the HUD is built for 1080x1920).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            // Google Play requires 64-bit; IL2CPP is needed for ARM64 and is faster than Mono.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            else Debug.LogWarning("[ZombieSLG] 아이콘을 찾지 못했습니다: " + IconPath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ShaderReferences.Ensure(false);
            AssetDatabase.SaveAssets();

            CarrySaveOver(oldDataDir);
            Debug.Log($"[ZombieSLG] 안드로이드 설정 적용 — {ProductName} ({PackageId}) v{Version} ({VersionCode}), 세로, IL2CPP/ARM64, 최소 API 24");
        }

        /// <summary>
        /// persistentDataPath depends on company/product name, so renaming would orphan the local save.
        /// Copy it to the new folder once (the cloud save would restore it anyway, this keeps offline play intact).
        /// </summary>
        private static void CarrySaveOver(string oldDir)
        {
            try
            {
                var companyDir = Directory.GetParent(oldDir);
                var root = companyDir?.Parent;
                if (root == null) return;
                string newDir = Path.Combine(root.FullName, CompanyName, ProductName);
                if (string.Equals(Path.GetFullPath(oldDir), Path.GetFullPath(newDir), StringComparison.OrdinalIgnoreCase)) return;
                foreach (var name in new[] { "save.dat", "save.dat.bak" })
                {
                    string from = Path.Combine(oldDir, name), to = Path.Combine(newDir, name);
                    if (!File.Exists(from) || File.Exists(to)) continue;
                    Directory.CreateDirectory(newDir);
                    File.Copy(from, to);
                    Debug.Log($"[ZombieSLG] 세이브를 새 위치로 복사했습니다: {to}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ZombieSLG] 세이브 복사 실패 (클라우드 세이브에서 복원됩니다): " + e.Message);
            }
        }

        [MenuItem("ZombieSLG/출시/2. 테스트 APK 빌드", priority = 41)]
        public static void BuildTestApk()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                EditorUtility.DisplayDialog("ZombieSLG",
                    "Android 빌드 모듈이 설치되어 있지 않습니다.\n\nUnity Hub ▸ 설치 ▸ 6000.0.84f1 ▸ 톱니바퀴 ▸ 모듈 추가 ▸ " +
                    "'Android Build Support' (OpenJDK, Android SDK & NDK 포함)를 설치한 뒤 Unity를 다시 여세요.", "확인");
                return;
            }

            Apply();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[ZombieSLG] Android 플랫폼으로 전환하지 못했습니다.");
                return;
            }

            AddressablesSetup.BuildContent();
            EditorUserBuildSettings.buildAppBundle = false; // APK for sideloading; Play Store needs an AAB (later)
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath) ?? "Builds");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });

            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                // summary.totalSize counts intermediate files too (hundreds of MB); report the real APK size.
                long bytes = File.Exists(ApkPath) ? new FileInfo(ApkPath).Length : 0;
                Debug.Log($"[ZombieSLG] 테스트 APK 빌드 성공 — {ApkPath} ({bytes / (1024f * 1024f):0.0} MB, {summary.totalTime.TotalMinutes:0.0}분)");
                EditorUtility.RevealInFinder(ApkPath);
            }
            else
            {
                Debug.LogError($"[ZombieSLG] APK 빌드 실패: {summary.result}, 오류 {summary.totalErrors}건 — Console 위쪽의 빨간 줄을 확인하세요.");
            }
        }
    }
}
