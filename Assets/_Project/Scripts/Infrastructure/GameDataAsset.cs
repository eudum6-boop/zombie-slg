using System.Collections.Generic;
using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Infrastructure
{
    /// <summary>
    /// Imported data tables (rows only, no CSV parsing at runtime). Written by the editor importer
    /// from Assets/_Project/Data/Tables/*.csv; lives in Resources until Addressables arrive in M4.
    /// </summary>
    public sealed class GameDataAsset : ScriptableObject
    {
        public const string ResourcesPath = "GameData";

        [Tooltip("CSV를 저장하면 자동으로 다시 채워집니다. 직접 수정하지 마세요.")]
        public GameTables Tables = new GameTables();

        public string ImportedAt;

        public GameData Build(List<string> errors) => GameDataBuilder.Build(Tables, errors);

        public static GameDataAsset LoadDefault() => Resources.Load<GameDataAsset>(ResourcesPath);
    }
}
