using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class BaseStateTests
    {
        private const double T0 = 1_000_000;

        private static BaseState NewBase(out GameSave save)
        {
            save = SaveMigrator.NewGame(T0);
            return new BaseState(save);
        }

        [Test]
        public void NewGame_HasAllPlotsAndStarterValues()
        {
            var s = NewBase(out var save);
            Assert.AreEqual(BasePlots.Defaults.Length, save.Buildings.Count);
            Assert.AreEqual(1, s.HqLevel);
            Assert.AreEqual(0, s.LevelOf(BasePlots.Clinic));
            Assert.AreEqual(4, s.Survivors);
            Assert.AreEqual(1, s.IdleSurvivors);
        }

        [Test]
        public void Production_AccruesOverTime()
        {
            var s = NewBase(out var save);
            double fuel0 = save.Fuel;
            double rate = s.NetRatePerHour(ResourceType.Fuel);
            s.Advance(T0 + 600); // 10 minutes
            Assert.AreEqual(fuel0 + rate / 6.0, save.Fuel, 0.5f);
        }

        [Test]
        public void Production_StopsAtStorageCap_ButRewardsCanExceedIt()
        {
            var s = NewBase(out var save);
            s.Advance(T0 + 3600 * 24);
            Assert.AreEqual(s.StorageCap, save.Materials, 0.001f);

            s.ApplyNightResult(true, new NightReward(0, 0, 500, 0), 0);
            Assert.AreEqual(s.StorageCap + 500, save.Materials, 0.001f);
            s.Advance(T0 + 3600 * 25);
            Assert.AreEqual(s.StorageCap + 500, save.Materials, 0.001f, "over-cap stock is kept, not trimmed");
        }

        [Test]
        public void Upgrade_DeductsCostAndCompletesOnTime()
        {
            var s = NewBase(out var save);
            var def = BuildingCatalog.Get(BuildingType.Headquarters);
            var cost = def.CostFor(2);
            double mat0 = save.Materials;

            Assert.AreEqual(UpgradeBlock.None, s.StartUpgrade(BasePlots.Headquarters, T0));
            Assert.AreEqual(mat0 - cost.Materials, save.Materials, 0.001f);
            Assert.IsTrue(s.Get(BasePlots.Headquarters).Upgrading);

            double secs = def.BuildSecondsFor(2);
            s.Advance(T0 + secs - 1);
            Assert.AreEqual(1, s.HqLevel);
            s.Advance(T0 + secs);
            Assert.AreEqual(2, s.HqLevel);
            Assert.IsTrue(s.Events.Any(e => e.Kind == BaseEventKind.UpgradeCompleted && e.Value == 2));
        }

        [Test]
        public void Upgrade_BlockedByQueueHqAndResources()
        {
            var s = NewBase(out var save);
            Assert.AreEqual(UpgradeBlock.HqTooLow, s.CanUpgrade(BasePlots.Farm), "farm L2 needs HQ 2");
            Assert.AreEqual(UpgradeBlock.HqTooLow, s.CanUpgrade(BasePlots.Clinic), "clinic needs HQ 2");

            s.StartUpgrade(BasePlots.Headquarters, T0);
            Assert.AreEqual(UpgradeBlock.QueueBusy, s.CanUpgrade(BasePlots.TowerB));

            s.Advance(T0 + 10_000);
            save.Food = save.Fuel = save.Materials = 0;
            Assert.AreEqual(UpgradeBlock.NotEnoughResources, s.CanUpgrade(BasePlots.Farm));
        }

        [Test]
        public void OfflineCatchUp_OneJumpEqualsSteppedSimulation()
        {
            // Same start, same upgrade; one save jumps 2h in a single call, the other ticks every second.
            GameSave Make()
            {
                var g = SaveMigrator.NewGame(T0);
                g.Food = 900; g.Fuel = 900; g.Materials = 500; // below cap so production stays visible
                return g;
            }

            var jumpSave = Make();
            var stepSave = Make();
            var jump = new BaseState(jumpSave);
            var step = new BaseState(stepSave);
            Assert.AreEqual(UpgradeBlock.None, jump.StartUpgrade(BasePlots.Headquarters, T0));
            Assert.AreEqual(UpgradeBlock.None, step.StartUpgrade(BasePlots.Headquarters, T0));

            double end = T0 + 7200;
            jump.Advance(end);
            for (double t = T0 + 1; t <= end; t += 1) step.Advance(t);

            Assert.AreEqual(2, jump.HqLevel);
            Assert.AreEqual(stepSave.Food, jumpSave.Food, 0.5);
            Assert.AreEqual(stepSave.Fuel, jumpSave.Fuel, 0.5);
            Assert.AreEqual(stepSave.Materials, jumpSave.Materials, 0.5);
            Assert.AreEqual(stepSave.Morale, jumpSave.Morale, 0.01);
        }

        [Test]
        public void Workers_RespectIdleAndSlots()
        {
            var s = NewBase(out _);
            Assert.IsFalse(s.ChangeWorkers(BasePlots.Farm, 1), "farm L1 has 1 slot, already full");
            Assert.IsTrue(s.ChangeWorkers(BasePlots.Farm, -1));
            Assert.AreEqual(2, s.IdleSurvivors);
            Assert.IsTrue(s.ChangeWorkers(BasePlots.Farm, 1));
            Assert.IsFalse(s.ChangeWorkers(BasePlots.Shelter, 1), "shelter has no jobs");
        }

        [Test]
        public void Staffing_AffectsRate()
        {
            var s = NewBase(out _);
            var farm = s.Get(BasePlots.Farm);
            double staffed = s.EffectiveRatePerHour(farm);
            s.ChangeWorkers(BasePlots.Farm, -1);
            Assert.Less((float)s.EffectiveRatePerHour(farm), (float)staffed);
        }

        [Test]
        public void Starvation_DropsMoraleAndFlags()
        {
            var s = NewBase(out var save);
            save.Food = 0;
            s.ChangeWorkers(BasePlots.Farm, -1);
            save.Survivors = 12;
            s.Advance(T0 + 3600);
            Assert.IsTrue(s.IsStarving);
            Assert.AreEqual(BaseState.StarvingMorale, s.Morale, 0.001f);
            Assert.IsTrue(s.Events.Any(e => e.Kind == BaseEventKind.StarvationStarted));
        }

        [Test]
        public void NightVictory_AdvancesNightAndAddsSurvivor()
        {
            var s = NewBase(out var save);
            s.ApplyNightResult(true, new NightReward(10, 20, 30, 2), 50);
            Assert.AreEqual(2, s.Night);
            Assert.AreEqual(5, s.Survivors);
            Assert.AreEqual(2, s.HeroShards);
            Assert.AreEqual(50, save.TotalKills);

            s.ApplyNightResult(false, new NightReward(1, 1, 1, 0), 5);
            Assert.AreEqual(2, s.Night, "defeat does not advance");
            Assert.AreEqual(5, s.Survivors);
        }

        [Test]
        public void Survivors_CappedByShelter()
        {
            var s = NewBase(out var save);
            save.Survivors = s.SurvivorCapacity;
            s.ApplyNightResult(true, new NightReward(0, 0, 0, 0), 0);
            Assert.AreEqual(s.SurvivorCapacity, s.Survivors);
        }
    }

    public class SaveMigratorTests
    {
        [Test]
        public void Migrate_Null_CreatesNewGame()
        {
            var s = SaveMigrator.Migrate(null, 42);
            Assert.AreEqual(GameSave.CurrentVersion, s.Version);
            Assert.AreEqual(42f, (float)s.LastTick, 0.001f);
        }

        [Test]
        public void Migrate_V0_FillsDefaultsAndMissingPlots()
        {
            var old = new GameSave { Version = 0, Night = 3, Food = 999 };
            old.Buildings.Add(new BuildingSave { Id = BasePlots.Headquarters, Type = BuildingType.Headquarters, Level = 4 });
            var s = SaveMigrator.Migrate(old, 100);
            Assert.AreEqual(GameSave.CurrentVersion, s.Version);
            Assert.AreEqual(3, s.Night);
            Assert.AreEqual(4, s.Buildings.First(b => b.Id == BasePlots.Headquarters).Level, "existing data kept");
            Assert.AreEqual(BasePlots.Defaults.Length, s.Buildings.Count);
            Assert.AreEqual(4, s.Survivors);
            Assert.AreEqual(100f, (float)s.LastTick, 0.001f);
        }
    }

    public class EventBusTests
    {
        private readonly struct Ping { public readonly int V; public Ping(int v) { V = v; } }

        [Test]
        public void PublishSubscribeUnsubscribe()
        {
            var bus = new EventBus();
            int sum = 0;
            var sub = bus.Subscribe<Ping>(p => sum += p.V);
            bus.Publish(new Ping(3));
            sub.Dispose();
            bus.Publish(new Ping(5));
            Assert.AreEqual(3, sum);
            Assert.AreEqual(0, bus.HandlerCount<Ping>());
        }

        [Test]
        public void HandlerMayUnsubscribeDuringDispatch()
        {
            var bus = new EventBus();
            int calls = 0;
            System.IDisposable sub = null;
            sub = bus.Subscribe<Ping>(_ => { calls++; sub.Dispose(); });
            bus.Subscribe<Ping>(_ => calls++);
            bus.Publish(new Ping(1));
            bus.Publish(new Ping(1));
            Assert.AreEqual(3, calls);
        }
    }

    public class BaseServiceTests
    {
        private sealed class FakeClock : IClock { public double Now { get; set; } }

        private sealed class MemoryRepo : ISaveRepository
        {
            public GameSave Stored;
            public int Saves;
            public GameSave Load() => Stored;
            public void Save(GameSave save) { Stored = save; Saves++; }
            public void Delete() { Stored = null; }
        }

        [Test]
        public void Load_ReportsOfflineProgress()
        {
            var clock = new FakeClock { Now = 5000 };
            var repo = new MemoryRepo { Stored = SaveMigrator.NewGame(5000 - 7200) };
            var bus = new EventBus();
            var reports = new List<OfflineProgressReport>();
            bus.Subscribe<OfflineProgressReport>(reports.Add);

            var svc = new BaseService(clock, repo, bus);
            svc.Load();

            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual(7200f, (float)reports[0].Seconds, 0.01f);
            Assert.Greater((float)reports[0].Fuel, 0f);
            Assert.Greater(repo.Saves, 0);
        }

        [Test]
        public void Upgrade_PublishesStartAndCompletion()
        {
            var clock = new FakeClock { Now = 1000 };
            var repo = new MemoryRepo();
            var bus = new EventBus();
            var started = new List<BuildingUpgradeStarted>();
            var done = new List<BuildingUpgradeCompleted>();
            bus.Subscribe<BuildingUpgradeStarted>(started.Add);
            bus.Subscribe<BuildingUpgradeCompleted>(done.Add);

            var svc = new BaseService(clock, repo, bus);
            svc.Load();
            Assert.AreEqual(UpgradeBlock.None, svc.TryUpgrade(BasePlots.Headquarters));
            Assert.AreEqual(1, started.Count);

            clock.Now += 10_000;
            svc.Tick();
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual(2, done[0].NewLevel);
        }

        [Test]
        public void ApplyNight_PersistsAndPublishes()
        {
            var clock = new FakeClock { Now = 1000 };
            var repo = new MemoryRepo();
            var bus = new EventBus();
            int arrived = 0;
            bus.Subscribe<SurvivorArrived>(_ => arrived++);
            var svc = new BaseService(clock, repo, bus);
            svc.Load();
            svc.ApplyNight(true, new NightReward(100, 100, 100, 1), 10);
            Assert.AreEqual(2, repo.Stored.Night);
            Assert.AreEqual(1, arrived);
        }
    }
}
