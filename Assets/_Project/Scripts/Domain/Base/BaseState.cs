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
        TrainingCompleted,
        HealCompleted,
        ResearchStarted,
        ResearchCompleted,
    }

    public readonly struct BaseEvent
    {
        public readonly BaseEventKind Kind;
        public readonly int BuildingId;
        public readonly int Value;
        /// <summary>Extra key for events that are not about a building: troop type name or research id.</summary>
        public readonly string Tag;

        public BaseEvent(BaseEventKind kind, int buildingId, int value, string tag = null)
        {
            Kind = kind;
            BuildingId = buildingId;
            Value = value;
            Tag = tag;
        }
    }

    /// <summary>
    /// Rules of the base economy: production with staffing and morale, storage caps, food upkeep,
    /// a single construction queue and night results. Time is injected (seconds) so the same code
    /// handles live play, offline catch-up and, later, server-side validation.
    /// </summary>
    public sealed partial class BaseState
    {
        private readonly GameSave _s;
        private readonly GameData _data;
        private readonly EconomyRules _eco;

        /// <summary>One-off events produced by the last calls. The application layer drains and publishes them.</summary>
        public readonly List<BaseEvent> Events = new List<BaseEvent>();

        public BaseState(GameSave save, GameData data)
        {
            _s = save ?? throw new ArgumentNullException(nameof(save));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _eco = data.Economy;
        }

        public GameSave Data => _s;
        public GameData Rules => _data;
        public IReadOnlyList<BuildingSave> Buildings => _s.Buildings;
        public int Night => _s.Night;
        public int Survivors => _s.Survivors;
        public double Morale => _s.Morale;
        public bool IsStarving => _s.Starving;
        public int RecruitTickets => _s.RecruitTickets;

        /// <summary>Production bonus from the hero in the Manager slot (0.1 = +10%).</summary>
        public double ManagerBonus => HeroRoster.SlotBonus(_s, _data, HeroSlot.Manager);

        public BuildingSave Get(int id)
        {
            var list = _s.Buildings;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i];
            return null;
        }

        public int LevelOf(int id) => Get(id)?.Level ?? 0;
        public int HqLevel => LevelOf(BasePlots.Headquarters);
        public int StorageCap => (int)Math.Round(_eco.StorageCap(HqLevel) * (1 + ResearchBonus(ResearchEffect.Storage)));

        public int SurvivorCapacity
        {
            get
            {
                int cap = 0;
                foreach (var b in _s.Buildings)
                    if (b.Type == BuildingType.Shelter) cap += _eco.ShelterCapacity(b.Level);
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

        /// <summary>economy.morale_floor at zero morale, 1.0 at full morale.</summary>
        public double MoraleMultiplier => _eco.MoraleMultiplier(_s.Morale);

        /// <summary>Unstaffed buildings still trickle (economy.unstaffed_efficiency); workers add the rest proportionally.</summary>
        public double Staffing(BuildingSave b)
        {
            var def = _data.Building(b.Type);
            int slots = def.WorkerSlots(b.Level);
            if (slots <= 0) return 0;
            double floor = _eco.UnstaffedEfficiency;
            return floor + (1 - floor) * Math.Min(b.Workers, slots) / slots;
        }

        public double EffectiveRatePerHour(BuildingSave b)
        {
            var def = _data.Building(b.Type);
            if (!def.IsProducer) return 0;
            return def.RatePerHour(b.Level) * Staffing(b) * MoraleMultiplier
                   * (1 + ManagerBonus + ResearchMath.ProductionBonus(_s, _data, def.Produces));
        }

        public double NetRatePerHour(ResourceType type)
        {
            double rate = 0;
            foreach (var b in _s.Buildings)
            {
                var def = _data.Building(b.Type);
                if (def.IsProducer && def.Produces == type) rate += EffectiveRatePerHour(b);
            }
            if (type == ResourceType.Food) rate -= FoodConsumptionPerHour;
            return rate;
        }

        public double FoodConsumptionPerHour => _s.Survivors * _eco.FoodPerSurvivorPerHour;

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
            var def = _data.Building(b.Type);
            int target = b.Level + 1;
            if (target > def.MaxLevel) return UpgradeBlock.MaxLevel;
            if (b.Type != BuildingType.Headquarters && (target > HqLevel || HqLevel < def.RequiredHqLevel)) return UpgradeBlock.HqTooLow;
            int busy = 0;
            foreach (var x in _s.Buildings) if (x.Upgrading) busy++;
            if (busy >= _eco.BuilderSlots) return UpgradeBlock.QueueBusy;
            if (!CanAfford(def.CostFor(target))) return UpgradeBlock.NotEnoughResources;
            return UpgradeBlock.None;
        }

        /// <summary>Build time after research (eco_engineering).</summary>
        public double BuildSecondsFor(BuildingType type, int targetLevel) =>
            Math.Round(_data.Building(type).BuildSecondsFor(targetLevel) * (1 - ResearchBonus(ResearchEffect.BuildSpeed)));

        public UpgradeBlock StartUpgrade(int id, double now)
        {
            Advance(now);
            var block = CanUpgrade(id);
            if (block != UpgradeBlock.None) return block;

            var b = Get(id);
            var def = _data.Building(b.Type);
            int target = b.Level + 1;
            Pay(def.CostFor(target));
            b.Upgrading = true;
            b.UpgradeEnd = now + BuildSecondsFor(b.Type, target);
            Events.Add(new BaseEvent(BaseEventKind.UpgradeStarted, b.Id, target));
            return UpgradeBlock.None;
        }

        public double RemainingSeconds(BuildingSave b, double now) => b.Upgrading ? Math.Max(0, b.UpgradeEnd - now) : 0;

        public bool ChangeWorkers(int id, int delta)
        {
            var b = Get(id);
            if (b == null || delta == 0) return false;
            int slots = _data.Building(b.Type).WorkerSlots(b.Level);
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

        private void Pay(Cost cost)
        {
            _s.Food -= cost.Food;
            _s.Fuel -= cost.Fuel;
            _s.Materials -= cost.Materials;
        }

        private enum Completion { None, Building, Research, Training, Healing }

        /// <summary>
        /// Advances the simulation to <paramref name="now"/>. Every completion inside the interval
        /// (construction, research, a training or healing batch) splits it, in time order, so
        /// production after an upgrade or a finished research uses the new values (correct offline catch-up).
        /// </summary>
        public void Advance(double now)
        {
            if (now <= _s.LastTick) return;

            while (true)
            {
                var kind = Completion.None;
                double at = double.MaxValue;
                BuildingSave building = null;

                foreach (var b in _s.Buildings)
                    if (b.Upgrading && b.UpgradeEnd <= now && b.UpgradeEnd < at) { at = b.UpgradeEnd; building = b; kind = Completion.Building; }
                if (_s.ResearchActive != null && _s.ResearchEnd <= now && _s.ResearchEnd < at) { at = _s.ResearchEnd; kind = Completion.Research; }
                if (_s.Training.Count > 0 && _s.Training[0].End <= now && _s.Training[0].End < at) { at = _s.Training[0].End; kind = Completion.Training; }
                if (_s.Healing.Count > 0 && _s.Healing[0].End <= now && _s.Healing[0].End < at) { at = _s.Healing[0].End; kind = Completion.Healing; }

                if (kind == Completion.None) break;
                AccrueTo(Math.Max(_s.LastTick, at));
                switch (kind)
                {
                    case Completion.Building:
                        building.Upgrading = false;
                        building.Level++;
                        Events.Add(new BaseEvent(BaseEventKind.UpgradeCompleted, building.Id, building.Level));
                        break;
                    case Completion.Research: CompleteResearch(); break;
                    case Completion.Training: CompleteTraining(); break;
                    case Completion.Healing: CompleteHealing(); break;
                }
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

            double target = starving ? _eco.StarvingMorale : _eco.FedMorale;
            double step = _eco.MoralePerHour * dtHours;
            if (_s.Morale < target) _s.Morale = Math.Min(target, _s.Morale + step);
            else _s.Morale = Math.Max(target, _s.Morale - step);

            _s.LastTick = t;
        }

        /// <summary>
        /// Night outcome: rewards bypass the storage cap; a victory advances the night and may bring a survivor.
        /// <paramref name="dead"/>/<paramref name="wounded"/> are per <see cref="TroopType"/> (null = no losses).
        /// </summary>
        public void ApplyNightResult(bool victory, NightReward reward, int kills, int[] dead = null, int[] wounded = null)
        {
            ApplyCasualties(dead, wounded);
            _s.Food += reward.Food;
            _s.Fuel += reward.Fuel;
            _s.Materials += reward.Materials;
            _s.RecruitTickets += reward.Tickets;
            _s.TotalKills += Math.Max(0, kills);

            if (!victory) return;
            _s.BestNight = Math.Max(_s.BestNight, _s.Night);
            _s.Night++;
            int arrive = Math.Min(_data.Night.SurvivorsPerVictory, SurvivorCapacity - _s.Survivors);
            if (arrive > 0)
            {
                _s.Survivors += arrive;
                Events.Add(new BaseEvent(BaseEventKind.SurvivorArrived, -1, arrive));
            }
        }
    }
}
