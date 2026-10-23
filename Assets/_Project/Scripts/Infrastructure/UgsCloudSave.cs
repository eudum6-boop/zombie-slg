using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Infrastructure
{
    /// <summary>
    /// Unity Gaming Services connection: initialise → anonymous sign-in → Cloud Save (player Default data).
    /// The whole save is stored as one JSON string under <see cref="Key"/>.
    /// M3 next step: progress-changing writes move to Cloud Code (server time, Protected data).
    /// </summary>
    public sealed class UgsCloudSave : ICloudSave
    {
        public const string Key = "save";

        public bool IsAvailable { get; private set; }
        public string PlayerId { get; private set; }

        /// <summary>Initialises UGS and signs in anonymously. Returns false (offline mode) on failure or timeout.</summary>
        public async Task<bool> ConnectAsync(int timeoutMs = 8000)
        {
            var connect = ConnectInternalAsync();
            var finished = await Task.WhenAny(connect, Task.Delay(timeoutMs));
            if (finished != connect)
            {
                Debug.LogWarning("[ZombieSLG] 서버 연결 시간 초과 — 오프라인 모드로 시작합니다.");
                return false;
            }
            return IsAvailable;
        }

        private async Task ConnectInternalAsync()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                    await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                PlayerId = AuthenticationService.Instance.PlayerId;
                IsAvailable = true;
                Debug.Log("[ZombieSLG] 서버 로그인 완료 (익명) PlayerId=" + PlayerId);
            }
            catch (Exception e)
            {
                IsAvailable = false;
                Debug.LogWarning("[ZombieSLG] 서버 로그인 실패 — 오프라인 모드: " + e.Message);
            }
        }

        public async Task<GameSave> PullAsync()
        {
            var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { Key });
            if (!items.TryGetValue(Key, out var item)) return null;
            string json = item.Value.GetAs<string>();
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<GameSave>(json);
        }

        public async Task PushAsync(GameSave save)
        {
            // Serialise now: the caller keeps mutating the live save while the request is in flight.
            string json = JsonUtility.ToJson(save);
            await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { Key, json } });
        }
    }
}
