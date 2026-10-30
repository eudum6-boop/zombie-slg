using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    // =====================================================================================
    // Server-authoritative rewards. The SAME code runs in the client (tests) and in the
    // Cloud Code C# module (ZombieServer/), which compiles this folder and the CSV tables.
    // =====================================================================================

    /// <summary>
    /// What the server trusts about a player: the recruit ticket balance, the gacha state and owned
    /// heroes, and how far the player has progressed. Stored in Cloud Save's Protected bucket (only
    /// the server can write it).
    /// </summary>
    [Serializable]
    public sealed class ServerLedger
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        /// <summary>The night the player is on (the next one to be played).</summary>
        public int Night = 1;
        public int BestNight;
        /// <summary>Server time (Unix seconds) of the last accepted night claim.</summary>
        public double LastClaimAt;
        public int Tickets;
        public int GachaSeed;
        public int GachaPulls;
        public int GachaPity;
        public List<HeroSave> Heroes = new List<HeroSave>();
        public int Claims;
        public int Rejections;
    }

    // ---- wire contracts (plain fields so both JsonUtility and Newtonsoft round-trip them) ----

    [Serializable]
    public sealed class NightClaimRequest
    {
        public int Night;
        public bool Victory;
        public int Kills;
        public float HqHpRatio;
        public double ScoutBonus;
    }

    [Serializable]
    public sealed class NightClaimResponse
    {
        public bool Accepted;
        /// <summary>Why the claim was refused (Korean, shown to the player); null when accepted.</summary>
        public string Reason;
        public int Food, Fuel, Materials, TicketsGained;
        /// <summary>Authoritative balances after the claim.</summary>
        public int Tickets;
        public int Night;
        /// <summary>Kills actually rewarded (clamped to what the night could spawn).</summary>
        public int KillsCounted;
    }

    [Serializable]
    public sealed class PullDto
    {
        public string HeroId;
        public int Rarity;
        public bool IsNew;
        public int Shards;
        public bool FromPity;
        public bool FromGuarantee;

        public static PullDto From(GachaResult r) => new PullDto
        {
            HeroId = r.HeroId, Rarity = (int)r.Rarity, IsNew = r.IsNew, Shards = r.ShardsGained,
            FromPity = r.FromPity, FromGuarantee = r.FromGuarantee,
        };

        public GachaResult ToResult() =>
            new GachaResult(HeroId, (HeroRarity)Rarity, IsNew, Shards, FromPity, FromGuarantee);
    }

    [Serializable]
    public sealed class RecruitResponse
    {
        public bool Accepted;
        public string Reason;
        public List<PullDto> Results = new List<PullDto>();
        public int Tickets;
        public int GachaPulls;
        public int GachaPity;
    }

    /// <summary>Authoritative balances, returned on login so the client can line up with the server.</summary>
    [Serializable]
    public sealed class LedgerSummary
    {
        public int Tickets;
        public int Night;
        public int BestNight;
        public int GachaPulls;
        public int GachaPity;
        public bool Imported;
    }

    public static class ServerRules
    {
        /// <summary>A victory needs at least this share of the night's length since the previous claim.</summary>
        public const double MinVictoryDurationShare = 0.8;
        /// <summary>Defeats can be quick, but not spammed.</summary>
        public const double MinDefeatGapSeconds = 20;
        /// <summary>Nights the client may be ahead of the server (played offline, rewards not claimed).</summary>
        public const int MaxNightsAhead = 3;

        /// <summary>
        /// First contact: adopt the player's existing progress from their (client-written) cloud save.
        /// This is the only moment client data is trusted; afterwards the ledger is the authority.
        /// </summary>
        public static ServerLedger Import(GameSave clientSave, GameData data, double now)
        {
            var s = clientSave ?? SaveMigrator.NewGame(now, data);
            var heroes = new List<HeroSave>();
            if (s.Heroes != null)
                foreach (var h in s.Heroes)
                    if (h != null && data.HasHero(h.Id))
                        heroes.Add(new HeroSave { Id = h.Id, Stars = h.Stars, Shards = h.Shards });
            if (!heroes.Exists(h => h.Id == data.StarterHero.Id))
                heroes.Insert(0, new HeroSave { Id = data.StarterHero.Id, Stars = 1 });

            return new ServerLedger
            {
                Night = Math.Max(1, s.Night),
                BestNight = Math.Max(0, s.BestNight),
                Tickets = Math.Max(0, s.RecruitTickets),
                GachaSeed = s.GachaSeed != 0 ? s.GachaSeed : 1,
                GachaPulls = Math.Max(0, s.GachaPulls),
                GachaPity = Math.Max(0, s.GachaPity),
                Heroes = heroes,
                LastClaimAt = 0,
            };
        }

        public static LedgerSummary Summary(ServerLedger l, bool imported = false) => new LedgerSummary
        {
            Tickets = l.Tickets, Night = l.Night, BestNight = l.BestNight,
            GachaPulls = l.GachaPulls, GachaPity = l.GachaPity, Imported = imported,
        };

        /// <summary>Most zombies a night can produce (deterministic wave director, nobody ever alive).</summary>
        public static int MaxSpawns(GameData data, int night)
        {
            var director = new WaveDirector(data.Wave, data.Night, night, night * 7919 + 17);
            var sink = new List<SpawnRequest>(64);
            while (!director.IsFinished)
            {
                sink.Clear();
                director.Tick(0.25f, 0, sink);
            }
            return director.TotalSpawned;
        }

        /// <summary>Highest scout bonus any hero can give (clamps a client-sent value).</summary>
        public static double MaxScoutBonus(GameData data)
        {
            double best = 0;
            foreach (var h in data.Heroes)
            {
                if (h.Role != HeroRole.Explore) continue;
                var probe = new GameSave { ScoutHero = h.Id };
                probe.Heroes.Add(new HeroSave { Id = h.Id, Stars = data.HeroRules.MaxStars });
                best = Math.Max(best, HeroRoster.SlotBonus(probe, data, HeroSlot.Scout));
            }
            return best;
        }

        /// <summary>
        /// Validates a finished night and grants its reward from the server's own formula.
        /// Mutates <paramref name="l"/> only when accepted (except the rejection counter).
        /// </summary>
        public static NightClaimResponse ClaimNight(ServerLedger l, GameData data, NightClaimRequest req, double now)
        {
            var res = new NightClaimResponse { Tickets = l.Tickets, Night = l.Night };
            string reason = Validate(l, data, req, now);
            if (reason != null)
            {
                l.Rejections++;
                res.Reason = reason;
                return res;
            }

            int kills = Math.Min(Math.Max(0, req.Kills), MaxSpawns(data, req.Night));
            float hp = Math.Max(0f, Math.Min(1f, req.HqHpRatio));
            if (!req.Victory) hp = 0f;
            double scout = Math.Max(0, Math.Min(req.ScoutBonus, MaxScoutBonus(data)));
            var reward = data.Rewards.Calculate(req.Night, req.Victory, kills, hp, scout);

            l.Tickets += reward.Tickets;
            l.LastClaimAt = now;
            l.Claims++;
            if (req.Victory)
            {
                l.BestNight = Math.Max(l.BestNight, req.Night);
                l.Night = req.Night + 1;
            }
            else l.Night = Math.Max(l.Night, req.Night);

            res.Accepted = true;
            res.Food = reward.Food;
            res.Fuel = reward.Fuel;
            res.Materials = reward.Materials;
            res.TicketsGained = reward.Tickets;
            res.Tickets = l.Tickets;
            res.Night = l.Night;
            res.KillsCounted = kills;
            return res;
        }

        private static string Validate(ServerLedger l, GameData data, NightClaimRequest req, double now)
        {
            if (req == null) return "잘못된 요청입니다";
            if (req.Night < l.Night) return $"이미 보상을 받은 밤입니다 (서버 기준 NIGHT {l.Night})";
            if (req.Night > l.Night + MaxNightsAhead) return $"서버 기록(NIGHT {l.Night})보다 너무 앞선 밤입니다";
            double gap = now - l.LastClaimAt;
            if (req.Victory)
            {
                double need = data.Wave.Duration * MinVictoryDurationShare;
                if (gap < need) return $"밤이 너무 빨리 끝났습니다 ({gap:0}초 < {need:0}초)";
            }
            else if (gap < MinDefeatGapSeconds) return "보상 요청이 너무 잦습니다";
            return null;
        }

        /// <summary>Server-side recruit: same gacha code as the client, but on the server's ledger and tickets.</summary>
        public static RecruitResponse Recruit(ServerLedger l, GameData data, int count)
        {
            var res = new RecruitResponse { Tickets = l.Tickets, GachaPulls = l.GachaPulls, GachaPity = l.GachaPity };
            var scratch = new GameSave
            {
                RecruitTickets = l.Tickets,
                GachaSeed = l.GachaSeed,
                GachaPulls = l.GachaPulls,
                GachaPity = l.GachaPity,
                Heroes = l.Heroes ?? new List<HeroSave>(),
            };
            var results = new List<GachaResult>(count > 0 ? count : 1);
            var block = new HeroRoster(scratch, data).Pull(count, results);
            if (block != PullBlock.None)
            {
                res.Reason = block == PullBlock.NotEnoughTickets ? "모집권이 부족합니다" : "잘못된 모집 횟수입니다";
                return res;
            }

            l.Tickets = scratch.RecruitTickets;
            l.GachaPulls = scratch.GachaPulls;
            l.GachaPity = scratch.GachaPity;
            l.Heroes = scratch.Heroes;

            res.Accepted = true;
            foreach (var r in results) res.Results.Add(PullDto.From(r));
            res.Tickets = l.Tickets;
            res.GachaPulls = l.GachaPulls;
            res.GachaPity = l.GachaPity;
            return res;
        }
    }
}
