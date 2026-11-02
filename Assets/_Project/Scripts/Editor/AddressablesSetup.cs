using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using ZombieSLG.Infrastructure;

namespace ZombieSLG.EditorTools
{
    /// <summary>
    /// Keeps the project's Addressables layout in code so it is reproducible:
    /// group "GameData" ← GameData.asset (address "GameData"). Called by the data importer.
    /// </summary>
    public static class AddressablesSetup
    {
        public const string GroupName = "GameData";

        /// <summary>Marks the asset as addressable in the GameData group with the given address.</summary>
        public static void EnsureEntry(string assetPath, string address)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Debug.LogWarning("[ZombieSLG] Addressables 설정을 만들 수 없습니다.");
                return;
            }

            var group = settings.FindGroup(GroupName);
            if (group == null)
                group = settings.CreateGroup(GroupName, false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return;

            var entry = settings.FindAssetEntry(guid);
            if (entry != null && entry.parentGroup == group && entry.address == address) return;

            entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = address;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ZombieSLG] Addressables 등록: {assetPath} → 그룹 '{GroupName}', 주소 '{address}'");
        }

        [MenuItem("ZombieSLG/Addressables 콘텐츠 빌드", priority = 12)]
        public static void BuildContent()
        {
            GameDataImporter.Import(false);
            AddressableAssetSettings.CleanPlayerContent();
            AddressableAssetSettings.BuildPlayerContent();
            Debug.Log("[ZombieSLG] Addressables 콘텐츠 빌드 완료 (플레이어 빌드에 포함됩니다).");
        }
    }
}
