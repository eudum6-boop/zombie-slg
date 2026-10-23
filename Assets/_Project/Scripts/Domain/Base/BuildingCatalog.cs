using System;

namespace ZombieSLG.Domain
{
    public enum ResourceType
    {
        Food = 0,
        Fuel = 1,
        Materials = 2,
    }

    public enum BuildingType
    {
        Headquarters = 0,
        Farm = 1,
        FuelPump = 2,
        Workshop = 3,
        Shelter = 4,
        Clinic = 5,
        GunTower = 6,
        Barracks = 7,
        ResearchLab = 8,
    }

    public readonly struct Cost
    {
        public readonly int Food;
        public readonly int Fuel;
        public readonly int Materials;

        public Cost(int food, int fuel, int materials)
        {
            Food = food;
            Fuel = fuel;
            Materials = materials;
        }

        public Cost Scale(double factor) =>
            new Cost((int)Math.Round(Food * factor), (int)Math.Round(Fuel * factor), (int)Math.Round(Materials * factor));

        public int Get(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Fuel: return Fuel;
                case ResourceType.Materials: return Materials;
                default: return Food;
            }
        }

        public override string ToString() => $"Food {Food}, Fuel {Fuel}, Materials {Materials}";
    }

    /// <summary>Definition of a building type, built from buildings.csv (base value × growth per level).</summary>
    public sealed class BuildingDef
    {
        public readonly BuildingType Type;
        public readonly string Name;
        public readonly string Description;
        public readonly int MaxLevel;
        public readonly int RequiredHqLevel;
        public readonly Cost BaseCost;
        public readonly double CostGrowth;
        public readonly double BaseBuildSeconds;
        public readonly double BuildGrowth;
        public readonly bool IsProducer;
        public readonly ResourceType Produces;
        public readonly double BaseRatePerHour;
        public readonly double RateGrowth;
        public readonly int WorkerSlotsBase;
        public readonly int WorkerSlotEveryLevels;

        public BuildingDef(BuildingType type, string name, string description, int maxLevel, int requiredHqLevel,
            Cost baseCost, double costGrowth, double baseBuildSeconds, double buildGrowth,
            bool isProducer, ResourceType produces, double baseRatePerHour, double rateGrowth,
            int workerSlotsBase, int workerSlotEveryLevels)
        {
            Type = type;
            Name = name;
            Description = description;
            MaxLevel = maxLevel;
            RequiredHqLevel = requiredHqLevel;
            BaseCost = baseCost;
            CostGrowth = costGrowth;
            BaseBuildSeconds = baseBuildSeconds;
            BuildGrowth = buildGrowth;
            IsProducer = isProducer;
            Produces = produces;
            BaseRatePerHour = baseRatePerHour;
            RateGrowth = rateGrowth;
            WorkerSlotsBase = workerSlotsBase;
            WorkerSlotEveryLevels = Math.Max(1, workerSlotEveryLevels);
        }

        /// <summary>Cost to reach <paramref name="targetLevel"/> (1 = construct from an empty plot).</summary>
        public Cost CostFor(int targetLevel) => BaseCost.Scale(Math.Pow(CostGrowth, Math.Max(0, targetLevel - 1)));

        public double BuildSecondsFor(int targetLevel) => Math.Round(BaseBuildSeconds * Math.Pow(BuildGrowth, Math.Max(0, targetLevel - 1)));

        public double RatePerHour(int level) =>
            !IsProducer || level <= 0 ? 0 : BaseRatePerHour * Math.Pow(RateGrowth, level - 1);

        public int WorkerSlots(int level) =>
            !IsProducer || level <= 0 ? 0 : WorkerSlotsBase + level / WorkerSlotEveryLevels;
    }


}
