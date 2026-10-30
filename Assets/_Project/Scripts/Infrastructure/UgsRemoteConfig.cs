using System;
using System.Threading.Tasks;
using Unity.Services.RemoteConfig;
using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Infrastructure
{
    /// <summary>
    /// Remote Config: fetches the JSON under <see cref="RemoteBalance.ConfigKey"/> and caches it, so the last
    /// server values still apply when the game starts offline.
    /// </summary>
    public static class UgsRemoteConfig
    {
        private const string CacheKey = "zslg.remote_balance";

        private struct UserAttributes { }
        private struct AppAttributes { }

        /// <summary>Last successfully fetched JSON (or empty).</summary>
        public static string Cached => PlayerPrefs.GetString(CacheKey, "");

        /// <summary>
        /// Returns the server JSON, or the cached copy on failure/timeout. Requires a signed-in player.
        /// An empty string means "no overrides".
        /// </summary>
        public static async Task<(string json, bool fresh)> FetchBalanceAsync(int timeoutMs = 4000)
        {
            try
            {
                var fetch = RemoteConfigService.Instance.FetchConfigsAsync(new UserAttributes(), new AppAttributes());
                var done = await Task.WhenAny(fetch, Task.Delay(timeoutMs));
                if (done != fetch)
                {
                    Debug.LogWarning("[ZombieSLG] Remote Config 시간 초과 — 저장된 값을 사용합니다.");
                    return (Cached, false);
                }
                await fetch;
                var cfg = RemoteConfigService.Instance.appConfig;
                string json = cfg.HasKey(RemoteBalance.ConfigKey) ? cfg.GetJson(RemoteBalance.ConfigKey, "") : "";
                if (string.IsNullOrEmpty(json))
                    Debug.Log($"[ZombieSLG] Remote Config 받음 — '{RemoteBalance.ConfigKey}' 키 없음 (서버 키: {string.Join(", ", cfg.GetKeys())})");
                else
                    Debug.Log($"[ZombieSLG] Remote Config 받음 — {RemoteBalance.ConfigKey} = {json}");
                PlayerPrefs.SetString(CacheKey, json ?? "");
                PlayerPrefs.Save();
                return (json ?? "", true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ZombieSLG] Remote Config 실패 — 저장된 값을 사용합니다: " + e.Message);
                return (Cached, false);
            }
        }
    }
}
