using System;
using ZombieSLG.Domain;

namespace ZombieSLG.Application
{
    public interface IClock
    {
        /// <summary>Unix time in seconds. Local clock for M1; server time (Cloud Code) in M3.</summary>
        double Now { get; }
    }

    public interface ISaveRepository
    {
        /// <summary>Returns null when there is no save yet.</summary>
        GameSave Load();
        void Save(GameSave save);
        void Delete();
    }

    // ---- messages published on the EventBus ----

    public readonly struct BuildingUpgradeStarted
    {
        public readonly int BuildingId;
        public readonly int TargetLevel;
        public BuildingUpgradeStarted(int id, int target) { BuildingId = id; TargetLevel = target; }
    }

    public readonly struct BuildingUpgradeCompleted
    {
        public readonly int BuildingId;
        public readonly int NewLevel;
        public BuildingUpgradeCompleted(int id, int level) { BuildingId = id; NewLevel = level; }
    }

    public readonly struct SurvivorArrived
    {
        public readonly int Count;
        public SurvivorArrived(int count) { Count = count; }
    }

    public readonly struct StarvationChanged
    {
        public readonly bool Starving;
        public StarvationChanged(bool starving) { Starving = starving; }
    }

    public readonly struct OfflineProgressReport
    {
        public readonly double Seconds;
        public readonly double Food;
        public readonly double Fuel;
        public readonly double Materials;
        public readonly int UpgradesCompleted;

        public OfflineProgressReport(double seconds, double food, double fuel, double materials, int upgrades)
        {
            Seconds = seconds;
            Food = food;
            Fuel = fuel;
            Materials = materials;
            UpgradesCompleted = upgrades;
        }
    }

    public readonly struct NightApplied
    {
        public readonly bool Victory;
        public readonly NightReward Reward;
        public NightApplied(bool victory, NightReward reward) { Victory = victory; Reward = reward; }
    }

    /// <summary>
    /// Use-case layer for the base: wraps the domain state with a clock, persistence and the event bus.
    /// Presentation calls this; it never touches the save or the clock directly.
    /// </summary>
    public sealed class BaseService
    {
        public const double OfflineReportThresholdSeconds = 60;
        public const double AutosaveIntervalSeconds = 10;

        private readonly IClock _clock;
        private readonly ISaveRepository _repo;
        private readonly EventBus _bus;
        private double _lastSave;

        public BaseState State { get; private set; }
        public EventBus Bus => _bus;
        public double Now => _clock.Now;

        public BaseService(IClock clock, ISaveRepository repo, EventBus bus)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        /// <summary>Loads (or creates) the save, migrates it and catches up offline time.</summary>
        public void Load()
        {
            double now = _clock.Now;
            var save = SaveMigrator.Migrate(_repo.Load(), now);
            State = new BaseState(save);

            double away = now - save.LastTick;
            double food = save.Food, fuel = save.Fuel, materials = save.Materials;
            State.Advance(now);

            int upgrades = 0;
            foreach (var e in State.Events) if (e.Kind == BaseEventKind.UpgradeCompleted) upgrades++;
            DrainEvents();

            if (away >= OfflineReportThresholdSeconds)
            {
                _bus.Publish(new OfflineProgressReport(away,
                    State.Data.Food - food, State.Data.Fuel - fuel, State.Data.Materials - materials, upgrades));
            }
            Save();
        }

        public void Tick()
        {
            double now = _clock.Now;
            State.Advance(now);
            DrainEvents();
            if (now - _lastSave >= AutosaveIntervalSeconds) Save();
        }

        public UpgradeBlock TryUpgrade(int buildingId)
        {
            var result = State.StartUpgrade(buildingId, _clock.Now);
            DrainEvents();
            if (result == UpgradeBlock.None) Save();
            return result;
        }

        public bool ChangeWorkers(int buildingId, int delta)
        {
            State.Advance(_clock.Now); // bank production at the old staffing first
            bool changed = State.ChangeWorkers(buildingId, delta);
            DrainEvents();
            if (changed) Save();
            return changed;
        }

        public void ApplyNight(bool victory, NightReward reward, int kills)
        {
            State.Advance(_clock.Now);
            State.ApplyNightResult(victory, reward, kills);
            DrainEvents();
            _bus.Publish(new NightApplied(victory, reward));
            Save();
        }

        public void Save()
        {
            _repo.Save(State.Data);
            _lastSave = _clock.Now;
        }

        public void ResetProgress()
        {
            _repo.Delete();
            State = new BaseState(SaveMigrator.NewGame(_clock.Now));
            Save();
        }

        private void DrainEvents()
        {
            var events = State.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                switch (e.Kind)
                {
                    case BaseEventKind.UpgradeStarted: _bus.Publish(new BuildingUpgradeStarted(e.BuildingId, e.Value)); break;
                    case BaseEventKind.UpgradeCompleted: _bus.Publish(new BuildingUpgradeCompleted(e.BuildingId, e.Value)); break;
                    case BaseEventKind.SurvivorArrived: _bus.Publish(new SurvivorArrived(e.Value)); break;
                    case BaseEventKind.StarvationStarted: _bus.Publish(new StarvationChanged(true)); break;
                    case BaseEventKind.StarvationEnded: _bus.Publish(new StarvationChanged(false)); break;
                }
            }
            events.Clear();
        }
    }
}
