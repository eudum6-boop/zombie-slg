using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    /// <summary>Client ↔ server flow with the real ServerRules running in an in-memory "server".</summary>
    public class ServerRewardServiceTests
    {
        private sealed class Clock : IClock { public double Now { get; set; } = 7_000_000; }

        private sealed class Repo : ISaveRepository
        {
            public GameSave Stored;
            public GameSave Load() => Stored;
            public void Save(GameSave s) => Stored = s;
            public void Delete() => Stored = null;
        }

        private sealed class FakeServer : IServerRewards
        {
            public bool IsSignedIn { get; set; } = true;
            public bool Broken;
            public ServerLedger Ledger;
            public Clock Clock;

            public Task<LedgerSummary> SyncAsync(GameSave clientSave)
            {
                if (Broken) return Task.FromException<LedgerSummary>(new Exception("module not deployed"));
                bool imported = Ledger == null;
                if (imported) Ledger = ServerRules.Import(clientSave, TestData.Data, Clock.Now);
                return Task.FromResult(ServerRules.Summary(Ledger, imported));
            }

            public Task<NightClaimResponse> ClaimNightAsync(NightClaimRequest r) =>
                Task.FromResult(ServerRules.ClaimNight(Ledger, TestData.Data, r, Clock.Now));

            public Task<RecruitResponse> RecruitAsync(int count) =>
                Task.FromResult(ServerRules.Recruit(Ledger, TestData.Data, count));
        }

        private Clock _clock;
        private BaseService _base;
        private FakeServer _server;
        private ServerRewardService _rewards;

        private void Setup(bool broken = false)
        {
            _clock = new Clock();
            _base = new BaseService(_clock, new Repo(), new EventBus(), TestData.Data);
            _base.Load();
            _server = new FakeServer { Clock = _clock, Broken = broken };
            _rewards = new ServerRewardService(_server, _base);
        }

        [Test]
        public void Sync_ImportsOnce_ThenServerTicketsWin()
        {
            Setup();
            int start = _base.State.Data.RecruitTickets;
            Assert.IsNotNull(_rewards.SyncAsync().Result);
            Assert.IsTrue(_rewards.Enabled);
            Assert.AreEqual(start, _server.Ledger.Tickets);

            _base.State.Data.RecruitTickets = 9999; // local tampering
            _rewards.SyncAsync().Wait();
            Assert.AreEqual(start, _base.State.Data.RecruitTickets);
        }

        [Test]
        public void Night_UsesServerReward_AndServerTickets()
        {
            Setup();
            _rewards.SyncAsync().Wait();
            int tickets = _base.State.Data.RecruitTickets;
            _clock.Now += TestData.Data.Wave.Duration;
            _base.State.Advance(_clock.Now); // production while the night ran
            double food = _base.State.Data.Food;

            var fakeLocal = new NightReward(99999, 99999, 99999, 99);
            var s = _rewards.SettleNightAsync(1, true, 30, 1f, 0, fakeLocal, null, null).Result;

            Assert.AreEqual(SettlementSource.Server, s.Source);
            var expected = TestData.Data.Rewards.Calculate(1, true, 30, 1f);
            Assert.AreEqual(food + expected.Food, _base.State.Data.Food, 1.0, "local numbers ignored");
            Assert.AreEqual(tickets + expected.Tickets, _base.State.Data.RecruitTickets);
            Assert.AreEqual(2, _base.State.Night);
        }

        [Test]
        public void RepeatedClaim_IsRejected_ButNightStillAdvancesLocally()
        {
            Setup();
            _rewards.SyncAsync().Wait();
            _clock.Now += TestData.Data.Wave.Duration;
            _rewards.SettleNightAsync(1, true, 30, 1f, 0, default, null, null).Wait();
            _clock.Now += 5; // way too fast for night 2
            _base.State.Advance(_clock.Now);
            double food = _base.State.Data.Food;
            var s = _rewards.SettleNightAsync(2, true, 30, 1f, 0, new NightReward(500, 500, 500, 3), null, null).Result;
            Assert.AreEqual(SettlementSource.Rejected, s.Source);
            Assert.IsNotNull(s.Reason);
            Assert.AreEqual(food, _base.State.Data.Food, 1.0);
        }

        [Test]
        public void NoModule_FallsBackToLocalResources_WithoutTickets()
        {
            Setup(broken: true);
            Assert.IsNull(_rewards.SyncAsync().Result);
            Assert.IsFalse(_rewards.Enabled);
            int tickets = _base.State.Data.RecruitTickets;
            double food = _base.State.Data.Food;

            var s = _rewards.SettleNightAsync(1, true, 30, 1f, 0, new NightReward(100, 50, 70, 3), null, null).Result;
            Assert.AreEqual(SettlementSource.Offline, s.Source);
            Assert.AreEqual(food + 100, _base.State.Data.Food, 1.0);
            Assert.AreEqual(tickets, _base.State.Data.RecruitTickets, "tickets only come from the server");
            Assert.IsNotNull(_rewards.RecruitAsync(1, new List<GachaResult>()).Result, "recruit needs the server");
        }

        [Test]
        public void Recruit_GoesThroughServer()
        {
            Setup();
            _rewards.SyncAsync().Wait();
            int tickets = _base.State.Data.RecruitTickets;
            var results = new List<GachaResult>();
            Assert.IsNull(_rewards.RecruitAsync(1, results).Result);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(tickets - 1, _base.State.Data.RecruitTickets);
            Assert.AreEqual(_server.Ledger.GachaPulls, _base.State.Data.GachaPulls);
            Assert.IsTrue(_base.State.Data.Heroes.Exists(h => h.Id == results[0].HeroId));
        }
    }
}
