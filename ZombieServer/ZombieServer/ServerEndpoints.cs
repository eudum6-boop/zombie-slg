using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;
using ZombieSLG.Domain;

namespace ZombieServer
{
    /// <summary>
    /// Server-authoritative endpoints. All rules live in ZombieSLG.Domain.ServerRules (shared with the
    /// client and unit-tested there); this class only loads/stores the ledger in Cloud Save (Protected
    /// bucket, server-write only) and stamps server time.
    /// </summary>
    public class ServerEndpoints
    {
        private const string LedgerKey = "server_ledger";

        private static readonly Lazy<GameData> Data = new Lazy<GameData>(LoadData);

        private readonly IGameApiClient _api;
        private readonly ILogger<ServerEndpoints> _log;

        public ServerEndpoints(IGameApiClient api, ILogger<ServerEndpoints> log)
        {
            _api = api;
            _log = log;
        }

        /// <summary>
        /// Called once per session after sign-in. The first ever call imports the player's progress from
        /// <paramref name="clientSaveJson"/> (their existing save); later calls ignore it.
        /// </summary>
        [CloudCodeFunction("Sync")]
        public async Task<LedgerSummary> Sync(IExecutionContext context, string clientSaveJson)
        {
            var ledger = await LoadAsync(context);
            if (ledger != null) return ServerRules.Summary(ledger);

            GameSave save = null;
            try
            {
                if (!string.IsNullOrEmpty(clientSaveJson)) save = JsonConvert.DeserializeObject<GameSave>(clientSaveJson);
            }
            catch (Exception e)
            {
                _log.LogWarning("Sync: unreadable client save ({Message}); starting fresh", e.Message);
            }
            ledger = ServerRules.Import(save, Data.Value, Now);
            await StoreAsync(context, ledger);
            _log.LogInformation("Sync: imported player {Player} at night {Night} with {Tickets} tickets",
                context.PlayerId, ledger.Night, ledger.Tickets);
            return ServerRules.Summary(ledger, imported: true);
        }

        [CloudCodeFunction("ClaimNight")]
        public async Task<NightClaimResponse> ClaimNight(IExecutionContext context,
            int night, bool victory, int kills, float hqHpRatio, double scoutBonus)
        {
            var ledger = await LoadAsync(context) ?? ServerRules.Import(null, Data.Value, Now);
            var res = ServerRules.ClaimNight(ledger, Data.Value, new NightClaimRequest
            {
                Night = night, Victory = victory, Kills = kills, HqHpRatio = hqHpRatio, ScoutBonus = scoutBonus,
            }, Now);
            await StoreAsync(context, ledger); // also persists the rejection counter
            if (!res.Accepted) _log.LogWarning("ClaimNight rejected for {Player}: {Reason}", context.PlayerId, res.Reason);
            return res;
        }

        [CloudCodeFunction("Recruit")]
        public async Task<RecruitResponse> Recruit(IExecutionContext context, int count)
        {
            var ledger = await LoadAsync(context) ?? ServerRules.Import(null, Data.Value, Now);
            var res = ServerRules.Recruit(ledger, Data.Value, count);
            if (res.Accepted) await StoreAsync(context, ledger);
            return res;
        }

        // ------------------------------------------------------------------ storage

        private static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        private async Task<ServerLedger> LoadAsync(IExecutionContext context)
        {
            var result = await _api.CloudSaveData.GetProtectedItemsAsync(
                context, context.ServiceToken, context.ProjectId, context.PlayerId,
                new List<string> { LedgerKey });
            var value = result.Data?.Results?.FirstOrDefault()?.Value?.ToString();
            return string.IsNullOrEmpty(value) ? null : JsonConvert.DeserializeObject<ServerLedger>(value);
        }

        private async Task StoreAsync(IExecutionContext context, ServerLedger ledger)
        {
            var body = new SetItemBody(LedgerKey, JsonConvert.SerializeObject(ledger));
            await _api.CloudSaveData.SetProtectedItemAsync(
                context, context.ServiceToken, context.ProjectId, context.PlayerId, body);
        }

        // ------------------------------------------------------------------ data

        private static GameData LoadData()
        {
            var asm = Assembly.GetExecutingAssembly();
            string Read(string fileName)
            {
                using var stream = asm.GetManifestResourceStream("Tables." + fileName);
                if (stream == null) return null;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }

            var errors = new List<string>();
            var data = GameDataBuilder.Build(GameTables.Parse(Read, errors), errors);
            if (data == null || errors.Count > 0)
                throw new InvalidOperationException("Data tables are invalid: " + string.Join("; ", errors));
            return data;
        }
    }
}
