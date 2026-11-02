using System.Collections.Generic;
using NUnit.Framework;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class ServerRulesTests
    {
        private static GameData D => TestData.Data;
        private const double T0 = 5_000_000;

        private static ServerLedger Fresh(int night = 1, int tickets = 10)
        {
            var save = SaveMigrator.NewGame(T0, D);
            save.Night = night;
            save.RecruitTickets = tickets;
            return ServerRules.Import(save, D, T0);
        }

        private static NightClaimRequest Win(int night, int kills = 50, float hp = 1f) =>
            new NightClaimRequest { Night = night, Victory = true, Kills = kills, HqHpRatio = hp };

        [Test]
        public void Import_AdoptsClientProgressOnce()
        {
            var l = Fresh(night: 4, tickets: 7);
            Assert.AreEqual(4, l.Night);
            Assert.AreEqual(7, l.Tickets);
            Assert.IsTrue(l.Heroes.Exists(h => h.Id == D.StarterHero.Id));
        }

        [Test]
        public void Victory_GrantsServerComputedReward_AndAdvancesNight()
        {
            var l = Fresh();
            var res = ServerRules.ClaimNight(l, D, Win(1, kills: 40, hp: 0.9f), T0 + D.Wave.Duration);
            Assert.IsTrue(res.Accepted, res.Reason);
            var expected = D.Rewards.Calculate(1, true, 40, 0.9f);
            Assert.AreEqual(expected.Food, res.Food);
            Assert.AreEqual(expected.Tickets, res.TicketsGained);
            Assert.AreEqual(10 + expected.Tickets, l.Tickets);
            Assert.AreEqual(2, l.Night);
            Assert.AreEqual(1, l.BestNight);
        }

        [Test]
        public void SameNightTwice_IsRejected()
        {
            var l = Fresh();
            double t = T0 + D.Wave.Duration;
            Assert.IsTrue(ServerRules.ClaimNight(l, D, Win(1), t).Accepted);
            int tickets = l.Tickets;
            var again = ServerRules.ClaimNight(l, D, Win(1), t + D.Wave.Duration);
            Assert.IsFalse(again.Accepted);
            Assert.AreEqual(tickets, l.Tickets, "no double reward");
            Assert.AreEqual(1, l.Rejections);
        }

        [Test]
        public void TooFastVictory_IsRejected()
        {
            var l = Fresh();
            Assert.IsTrue(ServerRules.ClaimNight(l, D, Win(1), T0 + D.Wave.Duration).Accepted);
            var fast = ServerRules.ClaimNight(l, D, Win(2), T0 + D.Wave.Duration + 10);
            Assert.IsFalse(fast.Accepted);
            Assert.AreEqual(2, l.Night);
        }

        [Test]
        public void FarFutureNight_IsRejected()
        {
            var l = Fresh(night: 3);
            Assert.IsFalse(ServerRules.ClaimNight(l, D, Win(3 + ServerRules.MaxNightsAhead + 1), T0 + 9999).Accepted);
            Assert.IsTrue(ServerRules.ClaimNight(l, D, Win(3 + ServerRules.MaxNightsAhead), T0 + 9999).Accepted, "a few offline nights are fine");
        }

        [Test]
        public void ImpossibleKills_AreClampedToWhatTheNightSpawns()
        {
            var l = Fresh();
            int max = ServerRules.MaxSpawns(D, 1);
            Assert.Greater(max, 0);
            var res = ServerRules.ClaimNight(l, D, Win(1, kills: 1_000_000), T0 + D.Wave.Duration);
            Assert.AreEqual(max, res.KillsCounted);
            Assert.AreEqual(D.Rewards.Calculate(1, true, max, 1f).Food, res.Food);
        }

        [Test]
        public void ScoutBonus_IsClamped()
        {
            var l = Fresh();
            var req = Win(1);
            req.ScoutBonus = 50; // +5000%
            var res = ServerRules.ClaimNight(l, D, req, T0 + D.Wave.Duration);
            var capped = D.Rewards.Calculate(1, true, req.Kills, 1f, ServerRules.MaxScoutBonus(D));
            Assert.AreEqual(capped.Food, res.Food);
        }

        [Test]
        public void Defeat_GivesNoTickets_AndKeepsNight()
        {
            var l = Fresh(night: 2);
            var res = ServerRules.ClaimNight(l, D, new NightClaimRequest { Night = 2, Victory = false, Kills = 10, HqHpRatio = 0.5f }, T0 + 60);
            Assert.IsTrue(res.Accepted, res.Reason);
            Assert.AreEqual(0, res.TicketsGained);
            Assert.AreEqual(2, l.Night);
        }

        [Test]
        public void Recruit_UsesServerTickets_AndMatchesClientGacha()
        {
            var save = SaveMigrator.NewGame(T0, D);
            save.RecruitTickets = 20;
            var l = ServerRules.Import(save, D, T0);

            // Same seed + pull index on both sides → identical results (deterministic gacha).
            var clientResults = new List<GachaResult>();
            new HeroRoster(save, D).Pull(10, clientResults);
            var res = ServerRules.Recruit(l, D, 10);

            Assert.IsTrue(res.Accepted, res.Reason);
            Assert.AreEqual(10, res.Results.Count);
            for (int i = 0; i < 10; i++) Assert.AreEqual(clientResults[i].HeroId, res.Results[i].HeroId);
            Assert.AreEqual(10, l.Tickets);
            Assert.AreEqual(save.GachaPity, l.GachaPity);
        }

        [Test]
        public void Recruit_WithoutTickets_IsRefused()
        {
            var l = Fresh(tickets: 0);
            var res = ServerRules.Recruit(l, D, 1);
            Assert.IsFalse(res.Accepted);
            Assert.AreEqual(0, l.GachaPulls);
        }

        [Test]
        public void Client_AppliesServerRecruit()
        {
            var l = Fresh(tickets: 10);
            var res = ServerRules.Recruit(l, D, 10);

            var save = SaveMigrator.NewGame(T0, D);
            save.RecruitTickets = 999; // tampered local value
            var roster = new HeroRoster(save, D);
            var shown = new List<GachaResult>();
            roster.ApplyServerRecruit(res, shown);

            Assert.AreEqual(10, shown.Count);
            Assert.AreEqual(0, save.RecruitTickets, "server balance wins");
            Assert.AreEqual(l.GachaPulls, save.GachaPulls);
            foreach (var r in shown) Assert.IsTrue(save.Heroes.Exists(h => h.Id == r.HeroId));
        }
    }
}
