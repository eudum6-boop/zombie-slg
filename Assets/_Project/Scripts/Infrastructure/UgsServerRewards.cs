using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Infrastructure
{
    /// <summary>
    /// Client for the Cloud Code C# module "ZombieServer" (repo folder ZombieServer/, deployed from
    /// Services ▸ Deployment). The module runs the same ZombieSLG.Domain code on the server.
    /// </summary>
    public sealed class UgsServerRewards : IServerRewards
    {
        public const string Module = "ZombieServer";

        private readonly UgsCloudSave _session;

        public UgsServerRewards(UgsCloudSave session) => _session = session;

        public bool IsSignedIn => _session.IsAvailable;

        public Task<LedgerSummary> SyncAsync(GameSave clientSave) =>
            CloudCodeService.Instance.CallModuleEndpointAsync<LedgerSummary>(Module, "Sync",
                new Dictionary<string, object> { { "clientSaveJson", JsonUtility.ToJson(clientSave) } });

        public Task<NightClaimResponse> ClaimNightAsync(NightClaimRequest r) =>
            CloudCodeService.Instance.CallModuleEndpointAsync<NightClaimResponse>(Module, "ClaimNight",
                new Dictionary<string, object>
                {
                    { "night", r.Night },
                    { "victory", r.Victory },
                    { "kills", r.Kills },
                    { "hqHpRatio", r.HqHpRatio },
                    { "scoutBonus", r.ScoutBonus },
                });

        public Task<RecruitResponse> RecruitAsync(int count) =>
            CloudCodeService.Instance.CallModuleEndpointAsync<RecruitResponse>(Module, "Recruit",
                new Dictionary<string, object> { { "count", count } });
    }
}
