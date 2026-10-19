using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    public enum UpgradeBlock
    {
        None = 0,
        MaxLevel,
        HqTooLow,
        QueueBusy,
        NotEnoughResources,
        UnknownBuilding,
    }

    public enum BaseEventKind
    {
        UpgradeStarted,
        UpgradeCompleted,
        SurvivorArrived,
        StarvationStarted,
        StarvationEnded,
    }

    public readonly struct BaseEvent
    {
        public readonly BaseEventKind Kind;
        public readonly int BuildingId;
        public readonly int Value;

        public BaseEvent(BaseEventKind kind, int buildingId, int value)
        {
            Kind = kind;
            BuildingId = buildingId;
            Value = value;
        }
    }

    /// <summary>
    /// Rules of the base economy: production with staffing and morale, storage caps, food upkeep,
    /// a single construction queue and night results. Time is injected (seconds) so the same code
    /// handles live play, offline catch-up and, later, server-side validation.
    /// </summary>
    public sealed class BaseState
    {
        public const double FoodPerSurvivorPerHour = 60;
        public const double MoralePerHour = 300;     // morale points moved toward target per hour
        public const double FedMorale = 85;
        public const double StarvingMorale = 20;
        public const int BuilderSlots = 1;

        private readonly GameSave _s;

        /// <summary>One-off events produced by the last calls. The application layer drains and publishes them.</summary>
        public readonly List<BaseEvent> Events = new List<BaseEvent>();

        public BaseState(GameSave save)
        {
            _s = save ?? throw new ArgumentNullException(nameof(save));
        }

        public GameSave Data => _s;
        public IReadOnlyList<BuildingSave> Buildings => _s.Buildings;
        public int Night => _s.Night;
        public int Survivors => _s.Survivors;
        public double Morale => _s.Morale;
        public bool IsStarving => _s.Starving;
        public int HeroShards => _s.HeroShards;

        public BuildingSave Get(int id)
        {
            var list = _s.Buildings;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i];
            return null;
        }

        public int LevelOf(int id) => Get(id)?.Level ?? 0;
        public int HqLevel => LevelOf(BasePlots.Headquarters);
        public int StorageCap => BuildingCatalog.StorageCap(HqLevel);

        public int SurvivorCapacity
        {
            get
            {
                int cap = 0;
                foreach (var b in _s.Buildings)
                    if (b.Type == BuildingType.Shelter) cap += BuildingCatalog.ShelterCapacity(b.Level);
                return Math.Max(cap, 1);
            }
        }

        public int AssignedWorkers
        {
            get
            {
                int n = 0;
                foreach (var b in _s.Buildings) n += b.Workers;
                return n;
            }
        }

        public int IdleSurvivors => Math.Max(0, _s.Survivors - AssignedWorkers);

        public double GetAmount(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Fuel: return _s.Fuel;
                case ResourceType.Materials: return _s.Materials;
                default: return _s.Food;
            }
        }

        private void SetAmount(ResourceType type, double value)
        {
            switch (type)
            {
                case ResourceType.Fuel: _s.Fuel = value; break;
                case ResourceType.Materials: _s.Materials = value; break;
                default: _s.Food = value; break;
            }
        }

        /// <summary>0.6 at zero morale, 1.0 at full morale.</summary>
        public double MoraleMultiplier => 0.6 + 0.4 * Math.Max(0, Math.Min(100, _s.Morale)) / 100.0;

        /// <summary>Unstaffed buildings still trickle at 25%; every worker adds the rest proportionally.</summary>
        public double Staffing(BuildingSave b)
        {
            var def = BuildingCatalog.Get(b.Type);
            int slots = def.WorkerSlots(b.Level);
            if (slots <= 0) return 0;
            return 0.25 + 0.75 * Math.Min(b.Workers, slots) / slots;
        }

        public double EffectiveRatePerHour(BuildingSave b)
        {
            var def = BuildingCatalog.Get(b.Type);
            if (!def.IsProducer) return 0;
            return def.RatePerHour(b.Level) * Staffing(b) * MoraleMultiplier;
        }

        public double NetRatePerHour(ResourceType type)
        {
            double rate = 0;
            foreach (var b in _s.Buildings)
            {
                var def = BuildingCatalog.Get(b.Type);
                if (def.IsProducer && def.Produces == type) rate += EffectiveRatePerHour(b);
            }
            if (type == ResourceType.Food) rate -= FoodConsumptionPerHour;
            return rate;
        }

        public double FoodConsumptionPerHour => _s.Survivors * FoodPerSurvivorPerHour;

        public BuildingSave ActiveConstruction
        {
            get
            {
                foreach (var b in _s.Buildings) if (b.Upgrading) return b;
                return null;
            }
        }

        public bool CanAfford(Cost c) => _s.Food >= c.Food && _s.Fuel >= c.Fuel && _s.Materials >= c.Materials;

        public UpgradeBlock CanUpgrade(int id)
        {
            var b = Get(id);
            if (b == null) return UpgradeBlock.UnknownBuilding;
            var def = BuildingCatalog.Get(b.Type);
            int target = b.Level + 1;
            if (target > def.MaxLevel) return UpgradeBlock.MaxLevel;
            if (b.Type != BuildingType.Headquarters && (target > HqLevel || HqLevel < def.RequiredHqLevel)) return UpgradeBlock.HqTooLow;
            int busy = 0;
            foreach (var x in _s.Buildings) if (x.Upgrading) busy++;
            if (busy >= BuilderSlots) return UpgradeBlock.QueueBusy;
            if (!CanAfford(def.CostFor(target))) return UpgradeBlock.NotEnoughResources;
            return UpgradeBlock.None;
        }

        public UpgradeBlock StartUpgrade(int id, double now)
        {
            Advance(now);
            var block = CanUpgrade(id);
            if (block != UpgradeBlock.None) return block;

            var b = Get(id);
            var def = BuildingCatalog.Get(b.Type);
            int target = b.Level + 1;
            var cost = def.CostFor(target);
            _s.Food -= cost.Food;
            _s.Fuel -= cost.Fuel;
            _s.Materials -= cost.Materials;
            b.Upgrading = true;
            b.UpgradeEnd = now + def.BuildSecondsFor(target);
            Events.Add(new BaseEvent(BaseEventKind.UpgradeStarted, b.Id, target));
            return UpgradeBlock.None;
        }

        public double RemainingSeconds(BuildingSave b, double now) => b.Upgrading ? Math.Max(0, b.UpgradeEnd - now) : 0;

        public bool ChangeWorkers(int id, int delta)
        {
            var b = Get(id);
            if (b == null || delta == 0) return false;
            int slots = BuildingCatalog.Get(b.Type).WorkerSlots(b.Level);
            if (delta > 0)
            {
                int add = Math.Min(delta, Math.Min(IdleSurvivors, slots - b.Workers));
                if (add <= 0) return false;
                b.Workers += add;
                return true;
            }
            int remove = Math.Min(-delta, b.Workers);
            if (remove <= 0) return false;
            b.Workers -= remove;
            return true;
        }

        /// <summary>
        /// Advances the simulation to <paramref name="now"/>. Construction completions inside the
        /// interval split it, so production after an upgrade uses the new level (correct offline catch-up).
        /// </summary>
        public void Advance(double now)
        {
            if (now <= _s.LastTick) return;

            while (true)
            {
                BuildingSave next = null;
                foreach (var b in _s.Buildings)
                    if (b.Upgrading && b.UpgradeEnd <= now && (next == null || b.UpgradeEnd < next.UpgradeEnd)) next = b;

                if (next == null) break;
                AccrueTo(Math.Max(_s.LastTick, next.UpgradeEnd));
                next.Upgrading = false;
                next.Level++;
                Events.Add(new BaseEvent(BaseEventKind.UpgradeCompleted, next.Id, next.Level));
            }

            AccrueTo(now);
        }

        private void AccrueTo(double t)
        {
            double dtHours = (t - _s.LastTick) / 3600.0;
            if (dtHours <= 0) return;
            int cap = StorageCap;

            foreach (ResourceType type in new[] { ResourceType.Food, ResourceType.Fuel, ResourceType.Materials })
            {
                double net = NetRatePerHour(type) * dtHours;
                double cur = GetAmount(type);
                double next;
                if (net >= 0) next = cur >= cap ? cur : Math.Min(cap, cur + net);
                else next = Math.Max(0, cur + net);
                SetAmount(type, next);
            }

            bool starving = _s.Food <= 0.0001 && NetRatePerHour(ResourceType.Food) < 0;
            if (starving != _s.Starving)
            {
                _s.Starving = starving;
                Events.Add(new BaseEvent(starving ? BaseEventKind.StarvationStarted : BaseEventKind.StarvationEnded, -1, 0));
            }

            double target = starving ? StarvingMorale : FedMorale;
            double step = MoralePerHour * dtHours;
            if (_s.Morale < target) _s.Morale = Math.Min(target, _s.Morale + step);
            else _s.Morale = Math.Max(target, _s.Morale - step);

            _s.LastTick = t;
        }

        /// <summary>Night outcome: rewards bypass the storage cap; a victory advances the night and may bring a survivor.</summary>
        public void ApplyNightResult(bool victory, NightReward reward, int kills)
        {
            _s.Food += reward.Food;
            _s.Fuel += reward.Fuel;
            _s.Materials += reward.Materials;
            _s.HeroShards += reward.HeroShards;
            _s.TotalKills += Math.Max(0, kills);

            if (!victory) return;
            _s.BestNight = Math.Max(_s.BestNight, _s.Night);
            _s.Night++;
            if (_s.Survivors < SurvivorCapacity)
            {
                _s.Survivors++;
                Events.Add(new BaseEvent(BaseEventKind.SurvivorArrived, -1, 1));
            }
        }
    }
}
