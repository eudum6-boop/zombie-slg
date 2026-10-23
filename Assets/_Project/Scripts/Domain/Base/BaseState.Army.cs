using System;

namespace ZombieSLG.Domain
{
    public enum ArmyBlock
    {
        None = 0,
        NoBuilding,
        InvalidCount,
        OverCapacity,
        NotEnoughWounded,
        NotEnoughResources,
    }

    /// <summary>Troops: training at the Barracks, healing wounded at the Clinic, night casualties.</summary>
    public sealed partial class BaseState
    {
        public TroopStock Stock(TroopType t) => SaveMigrator.Stock(_s, t);
        public int Healthy(TroopType t) => Stock(t).Healthy;
        public int Wounded(TroopType t) => Stock(t).Wounded;

        public int BarracksLevel => HighestLevel(BuildingType.Barracks);
        public int ClinicLevel => HighestLevel(BuildingType.Clinic);
        public int TroopCapacity => _data.Troops.Capacity(BarracksLevel);

        private int HighestLevel(BuildingType type)
        {
            int lvl = 0;
            foreach (var b in _s.Buildings) if (b.Type == type) lvl = Math.Max(lvl, b.Level);
            return lvl;
        }

        /// <summary>Every soldier the barracks houses: healthy, wounded, and in either queue.</summary>
        public int TroopsHoused
        {
            get
            {
                int n = 0;
                foreach (var x in _s.Troops) n += x.Healthy + x.Wounded;
                foreach (var q in _s.Training) n += q.Count;
                foreach (var q in _s.Healing) n += q.Count;
                return n;
            }
        }

        public int TotalHealthy
        {
            get
            {
                int n = 0;
                foreach (var x in _s.Troops) n += x.Healthy;
                return n;
            }
        }

        public int[] HealthyCounts()
        {
            var c = new int[TroopRules.TypeCount];
            foreach (var x in _s.Troops) c[(int)x.Type] = x.Healthy;
            return c;
        }

        public Cost TrainCost(TroopType t, int count) => _data.Troop(t).TrainCost.Scale(count);

        public double TrainSeconds(TroopType t, int count) =>
            Math.Round(_data.Troop(t).TrainSeconds * count * (1 - ResearchBonus(ResearchEffect.TrainSpeed)));

        public Cost HealCost(TroopType t, int count) =>
            _data.Troop(t).HealCost.Scale(count * (1 - ResearchBonus(ResearchEffect.HealCost)));

        public double HealSeconds(TroopType t, int count) =>
            Math.Round(_data.Troop(t).HealSeconds * count * _data.Troops.HealTimeMultiplier(ClinicLevel));

        public ArmyBlock CanTrain(TroopType t, int count)
        {
            if (BarracksLevel <= 0) return ArmyBlock.NoBuilding;
            if (count <= 0) return ArmyBlock.InvalidCount;
            if (TroopsHoused + count > TroopCapacity) return ArmyBlock.OverCapacity;
            if (!CanAfford(TrainCost(t, count))) return ArmyBlock.NotEnoughResources;
            return ArmyBlock.None;
        }

        public ArmyBlock Train(TroopType t, int count, double now)
        {
            Advance(now);
            var block = CanTrain(t, count);
            if (block != ArmyBlock.None) return block;
            Pay(TrainCost(t, count));
            Enqueue(_s.Training, t, count, TrainSeconds(t, count), now);
            return ArmyBlock.None;
        }

        public ArmyBlock CanHeal(TroopType t, int count)
        {
            if (ClinicLevel <= 0) return ArmyBlock.NoBuilding;
            if (count <= 0) return ArmyBlock.InvalidCount;
            if (Wounded(t) < count) return ArmyBlock.NotEnoughWounded;
            if (!CanAfford(HealCost(t, count))) return ArmyBlock.NotEnoughResources;
            return ArmyBlock.None;
        }

        public ArmyBlock Heal(TroopType t, int count, double now)
        {
            Advance(now);
            var block = CanHeal(t, count);
            if (block != ArmyBlock.None) return block;
            Pay(HealCost(t, count));
            Stock(t).Wounded -= count;
            Enqueue(_s.Healing, t, count, HealSeconds(t, count), now);
            return ArmyBlock.None;
        }

        /// <summary>Batches run one after another: a new batch starts when the previous one ends.</summary>
        private static void Enqueue(System.Collections.Generic.List<QueueItem> queue, TroopType t, int count, double seconds, double now)
        {
            double start = queue.Count > 0 ? Math.Max(now, queue[queue.Count - 1].End) : now;
            queue.Add(new QueueItem { Type = t, Count = count, End = start + seconds });
        }

        public double QueueRemaining(System.Collections.Generic.IReadOnlyList<QueueItem> queue, double now) =>
            queue.Count == 0 ? 0 : Math.Max(0, queue[queue.Count - 1].End - now);

        public System.Collections.Generic.IReadOnlyList<QueueItem> TrainingQueue => _s.Training;
        public System.Collections.Generic.IReadOnlyList<QueueItem> HealingQueue => _s.Healing;

        private void CompleteTraining()
        {
            var item = _s.Training[0];
            _s.Training.RemoveAt(0);
            Stock(item.Type).Healthy += item.Count;
            Events.Add(new BaseEvent(BaseEventKind.TrainingCompleted, -1, item.Count, item.Type.ToString()));
        }

        private void CompleteHealing()
        {
            var item = _s.Healing[0];
            _s.Healing.RemoveAt(0);
            Stock(item.Type).Healthy += item.Count;
            Events.Add(new BaseEvent(BaseEventKind.HealCompleted, -1, item.Count, item.Type.ToString()));
        }

        private void ApplyCasualties(int[] dead, int[] wounded)
        {
            for (int i = 0; i < TroopRules.TypeCount; i++)
            {
                var st = Stock((TroopType)i);
                int d = dead != null && i < dead.Length ? Math.Max(0, dead[i]) : 0;
                int w = wounded != null && i < wounded.Length ? Math.Max(0, wounded[i]) : 0;
                d = Math.Min(d, st.Healthy);
                w = Math.Min(w, st.Healthy - d);
                st.Healthy -= d + w;
                st.Wounded += w;
            }
        }
    }
}
