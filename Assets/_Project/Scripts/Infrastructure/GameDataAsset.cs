using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using ZombieSLG.Domain;

namespace ZombieSLG.Infrastructure
{
    /// <summary>
    /// Imported data tables (rows only, no CSV parsing at runtime). Written by the editor importer from
    /// Assets/_Project/Data/Tables/*.csv to Assets/_Project/Data/GameData.asset and registered with
    /// Addressables under <see cref="Address"/> (group "GameData").
    /// </summary>
    public sealed class GameDataAsset : ScriptableObject
    {
        public const string Address = "GameData";
        /// <summary>Pre-M4 location, still read as a fallback.</summary>
        public const string ResourcesPath = "GameData";

        [Tooltip("CSV를 저장하면 자동으로 다시 채워집니다. 직접 수정하지 마세요.")]
        public GameTables Tables = new GameTables();

        public string ImportedAt;

        /// <summary>Where the last <see cref="LoadAsync"/> found the data ("Addressables" / "Resources").</summary>
        public static string LoadedFrom { get; private set; }

        private static AsyncOperationHandle<GameDataAsset> _handle;

        public GameData Build(List<string> errors) => GameDataBuilder.Build(Tables, errors);

        /// <summary>
        /// Loads the data through Addressables. Falls back to Resources (older projects) so the game still
        /// starts if the Addressables entry is missing. The handle is kept for the app's lifetime.
        /// </summary>
        public static async Task<GameDataAsset> LoadAsync()
        {
            try
            {
                var locations = Addressables.LoadResourceLocationsAsync(Address, typeof(GameDataAsset));
                await locations.Task;
                bool found = locations.Status == AsyncOperationStatus.Succeeded && locations.Result != null && locations.Result.Count > 0;
                Addressables.Release(locations);

                if (found)
                {
                    var handle = Addressables.LoadAssetAsync<GameDataAsset>(Address);
                    await handle.Task;
                    if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                    {
                        _handle = handle;
                        LoadedFrom = "Addressables";
                        return handle.Result;
                    }
                    Addressables.Release(handle);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ZombieSLG] Addressables로 GameData를 불러오지 못했습니다: " + e.Message);
            }

            LoadedFrom = "Resources";
            return Resources.Load<GameDataAsset>(ResourcesPath);
        }

        /// <summary>Releases the Addressables handle (on shutdown).</summary>
        public static void Unload()
        {
            if (_handle.IsValid()) Addressables.Release(_handle);
        }
    }
}
