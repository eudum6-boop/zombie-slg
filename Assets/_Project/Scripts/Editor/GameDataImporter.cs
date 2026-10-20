using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZombieSLG.Domain;
using ZombieSLG.Infrastructure;

namespace ZombieSLG.EditorTools
{
    /// <summary>
    /// CSV → ScriptableObject pipeline. Saving any table under Data/Tables re-imports automatically;
    /// invalid tables are reported as file:line errors and the last good GameData is kept.
    /// </summary>
    public sealed class GameDataImporter : AssetPostprocessor
    {
        public const string TablesDir = "Assets/_Project/Data/Tables";
        public const string AssetPath = "Assets/_Project/Resources/GameData.asset";

        private static bool _queued;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touched = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(p => p.StartsWith(TablesDir, StringComparison.Ordinal) && p.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            if (touched) Queue();
        }

        [InitializeOnLoadMethod]
        private static void EnsureAssetExists()
        {
            if (!File.Exists(AssetPath)) Queue();
        }

        private static void Queue()
        {
            if (_queued) return;
            _queued = true;
            // Creating assets from inside an import callback is unsafe; run on the next editor tick.
            EditorApplication.delayCall += () =>
            {
                _queued = false;
                Import(false);
            };
        }

        [MenuItem("ZombieSLG/데이터 테이블 가져오기 (CSV → GameData)", priority = 10)]
        public static void ImportFromMenu() => Import(true);

        [MenuItem("ZombieSLG/데이터 테이블 폴더 열기", priority = 11)]
        public static void OpenTablesFolder() => EditorUtility.RevealInFinder(Path.Combine(TablesDir, GameTables.BalanceFile));

        public static bool Import(bool fromMenu)
        {
            var errors = new List<string>();
            var tables = GameTables.Parse(ReadTable, errors);
            var data = errors.Count == 0 ? GameDataBuilder.Build(tables, errors) : null;

            if (data == null)
            {
                foreach (var e in errors) Debug.LogError("[ZombieSLG][데이터] " + e);
                Debug.LogError($"[ZombieSLG][데이터] 테이블 오류 {errors.Count}건 — 고칠 때까지 이전 GameData를 그대로 사용합니다.");
                if (fromMenu) EditorUtility.DisplayDialog("ZombieSLG", $"데이터 테이블 오류 {errors.Count}건이 있습니다.\nConsole 창에서 파일:줄 위치를 확인하세요.", "확인");
                return false;
            }

            EnsureFolder(Path.GetDirectoryName(AssetPath)?.Replace('\\', '/'));
            var asset = AssetDatabase.LoadAssetAtPath<GameDataAsset>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<GameDataAsset>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.Tables = tables;
            asset.ImportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ZombieSLG] 데이터 테이블 가져오기 완료 — 좀비 {tables.Zombies.Count}, 건물 {tables.Buildings.Count}, " +
                      $"대규모 습격 {tables.Hordes.Count}, 영웅 {tables.Heroes.Count}, 수치 {tables.Balance.Count}개");
            return true;
        }

        private static string ReadTable(string fileName)
        {
            string path = Path.Combine(TablesDir, fileName);
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            // Excel's plain "CSV" saves in the system code page (CP949); Hangul then decodes as U+FFFD.
            if (text.IndexOf('�') >= 0)
                Debug.LogWarning($"[ZombieSLG][데이터] {fileName}: 한글이 깨져 보입니다. 엑셀에서는 'CSV UTF-8' 형식으로 저장하세요.");
            return text;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
