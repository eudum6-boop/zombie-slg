using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZombieSLG.Domain;

namespace ZombieSLG.Application
{
    /// <summary>Cloud Code module "ZombieServer" (production) or a fake (tests).</summary>
    public interface IServerRewards
    {
        /// <summary>Signed in (the module itself may still be missing; see <see cref="ServerRewardService.Enabled"/>).</summary>
        bool IsSignedIn { get; }
        Task<LedgerSummary> SyncAsync(GameSave clientSave);
        Task<NightClaimResponse> ClaimNightAsync(NightClaimRequest request);
        Task<RecruitResponse> RecruitAsync(int count);
    }

    public enum SettlementSource
    {
        /// <summary>Server computed and granted the reward.</summary>
        Server,
        /// <summary>Server refused the claim (cheat check or duplicate); nothing granted.</summary>
        Rejected,
        /// <summary>No server: resources granted locally, recruit tickets withheld.</summary>
        Offline,
    }

    public readonly struct NightSettlement
    {
        public readonly SettlementSource Source;
        public readonly NightReward Reward;
        public readonly string Reason;

        public NightSettlement(SettlementSource source, NightReward reward, string reason)
        {
            Source = source;
            Reward = reward;
            Reason = reason;
        }
    }

    /// <summary>
    /// Routes night rewards and recruiting through the server when it is reachable. The server owns the
    /// recruit-ticket balance and gacha state; the client mirrors them. Without a server the game keeps
    /// working: resources are granted locally but tickets are not (they would be overwritten on the next sync).
    /// </summary>
    public sealed class ServerRewardService
    {
        private readonly IServerRewards _server;
        private readonly BaseService _base;
        private readonly Action<string> _log;

        /// <summary>True after a successful <see cref="SyncAsync"/> this session.</summary>
        public bool Enabled { get; private set; }

        public ServerRewardService(IServerRewards server, BaseService baseService, Action<string> log = null)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _base = baseService ?? throw new ArgumentNullException(nameof(baseService));
            _log = log;
        }

        /// <summary>Call after <see cref="BaseService.Load"/>. Aligns tickets/gacha counters with the server.</summary>
        public async Task<LedgerSummary> SyncAsync()
        {
            Enabled = false;
            if (!_server.IsSignedIn) return null;
            try
            {
                var summary = await _server.SyncAsync(_base.State.Data);
                if (summary == null) return null;
                _base.ApplyServerBalances(summary.Tickets, summary.GachaPulls, summary.GachaPity);
                Enabled = true;
                return summary;
            }
            catch (Exception e)
            {
                _log?.Invoke("서버 보상 모듈 연결 실패 — 이번 실행은 기기에서 처리합니다: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Settles a finished night: asks the server for the reward and applies it. <paramref name="night"/>
        /// is the night that was played (before it advances).
        /// </summary>
        public async Task<NightSettlement> SettleNightAsync(int night, bool victory, int kills, float hqHpRatio,
            double scoutBonus, NightReward localReward, int[] dead, int[] wounded)
        {
            if (Enabled)
            {
                try
                {
                    var res = await _server.ClaimNightAsync(new NightClaimRequest
                    {
                        Night = night, Victory = victory, Kills = kills, HqHpRatio = hqHpRatio, ScoutBonus = scoutBonus,
                    });
                    if (res != null && res.Accepted)
                    {
                        var reward = new NightReward(res.Food, res.Fuel, res.Materials, 0);
                        _base.ApplyNight(victory, reward, kills, dead, wounded);
                        _base.ApplyServerBalances(res.Tickets, _base.State.Data.GachaPulls, _base.State.Data.GachaPity);
                        return new NightSettlement(SettlementSource.Server,
                            new NightReward(res.Food, res.Fuel, res.Materials, res.TicketsGained), null);
                    }
                    if (res != null)
                    {
                        // Refused: progress (night, casualties) still happens, rewards don't.
                        _base.ApplyNight(victory, new NightReward(0, 0, 0, 0), kills, dead, wounded);
                        _base.ApplyServerBalances(res.Tickets, _base.State.Data.GachaPulls, _base.State.Data.GachaPity);
                        return new NightSettlement(SettlementSource.Rejected, new NightReward(0, 0, 0, 0), res.Reason);
                    }
                }
                catch (Exception e)
                {
                    _log?.Invoke("서버 보상 요청 실패 — 기기에서 처리합니다: " + e.Message);
                }
            }

            var offline = new NightReward(localReward.Food, localReward.Fuel, localReward.Materials, 0);
            _base.ApplyNight(victory, offline, kills, dead, wounded);
            return new NightSettlement(SettlementSource.Offline, offline, null);
        }

        /// <summary>Server-side recruit. Returns null on success, otherwise a message for the player.</summary>
        public async Task<string> RecruitAsync(int count, List<GachaResult> results)
        {
            results.Clear();
            if (!Enabled) return "모집은 서버에 연결되어 있을 때만 할 수 있습니다";
            try
            {
                var res = await _server.RecruitAsync(count);
                if (res == null) return "서버 응답이 없습니다";
                if (!res.Accepted)
                {
                    _base.ApplyServerBalances(res.Tickets, res.GachaPulls, res.GachaPity);
                    return res.Reason ?? "모집할 수 없습니다";
                }
                _base.ApplyServerRecruit(res, results);
                return null;
            }
            catch (Exception e)
            {
                _log?.Invoke("서버 모집 실패: " + e.Message);
                return "서버에 연결할 수 없어 모집하지 못했습니다";
            }
        }
    }
}
