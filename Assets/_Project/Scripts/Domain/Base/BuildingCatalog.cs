using System;
using System.Collections.Generic;

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

    /// <summary>
    /// Static definition of a building type. Values are formula-driven for M1 and move to the
    /// CSV → ScriptableObject table pipeline in M2 (same shape: base value + growth per level).
    /// </summary>
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

        public BuildingDef(BuildingType type, string name, string description, int maxLevel, int requiredHqLevel,
            Cost baseCost, double costGrowth, double baseBuildSeconds, double buildGrowth,
            bool isProducer = false, ResourceType produces = ResourceType.Food, double baseRatePerHour = 0, double rateGrowth = 1)
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
        }

        /// <summary>Cost to reach <paramref name="targetLevel"/> (1 = construct from an empty plot).</summary>
        public Cost CostFor(int targetLevel) => BaseCost.Scale(Math.Pow(CostGrowth, Math.Max(0, targetLevel - 1)));

        public double BuildSecondsFor(int targetLevel) => Math.Round(BaseBuildSeconds * Math.Pow(BuildGrowth, Math.Max(0, targetLevel - 1)));

        public double RatePerHour(int level) =>
            !IsProducer || level <= 0 ? 0 : BaseRatePerHour * Math.Pow(RateGrowth, level - 1);

        public int WorkerSlots(int level) => !IsProducer || level <= 0 ? 0 : 1 + level / 2;
    }

    public static class BuildingCatalog
    {
        private static readonly Dictionary<BuildingType, BuildingDef> Defs = new Dictionary<BuildingType, BuildingDef>
        {
            [BuildingType.Headquarters] = new BuildingDef(BuildingType.Headquarters, "본부",
                "다른 건물의 최대 레벨과 창고 용량을 정합니다. 밤에는 본부 체력이 늘어납니다.",
                10, 0, new Cost(150, 100, 200), 1.7, 15, 1.65),
            [BuildingType.Farm] = new BuildingDef(BuildingType.Farm, "농장",
                "식량을 생산합니다. 생존자 1명은 시간당 식량 60을 먹습니다.",
                10, 1, new Cost(40, 30, 70), 1.55, 8, 1.6, true, ResourceType.Food, 1800, 1.35),
            [BuildingType.FuelPump] = new BuildingDef(BuildingType.FuelPump, "연료 펌프",
                "연료를 생산합니다.",
                10, 1, new Cost(60, 20, 70), 1.55, 8, 1.6, true, ResourceType.Fuel, 1200, 1.35),
            [BuildingType.Workshop] = new BuildingDef(BuildingType.Workshop, "작업장",
                "건설 자재를 생산합니다.",
                10, 1, new Cost(70, 40, 40), 1.55, 8, 1.6, true, ResourceType.Materials, 1500, 1.35),
            [BuildingType.Shelter] = new BuildingDef(BuildingType.Shelter, "숙소",
                "생존자 수용 인원을 늘립니다. 밤을 버틸 때마다 새 생존자가 찾아옵니다.",
                10, 1, new Cost(80, 40, 100), 1.6, 10, 1.6),
            [BuildingType.Clinic] = new BuildingDef(BuildingType.Clinic, "의무실",
                "밤에 영웅의 체력 회복이 빨라지고 부활 대기 시간이 줄어듭니다.",
                10, 2, new Cost(100, 100, 150), 1.6, 12, 1.6),
            [BuildingType.GunTower] = new BuildingDef(BuildingType.GunTower, "기관총 포탑",
                "밤 습격 때 자동으로 사격합니다. 레벨이 오르면 공격력·사거리·연사가 늘어납니다.",
                10, 1, new Cost(30, 80, 150), 1.6, 10, 1.6),
        };

        public static BuildingDef Get(BuildingType type) => Defs[type];
        public static IEnumerable<BuildingDef> All => Defs.Values;

        /// <summary>Per-resource storage cap from HQ level.</summary>
        public static int StorageCap(int hqLevel) => (int)Math.Round(1000 * Math.Pow(1.6, Math.Max(0, hqLevel - 1)));

        public static int ShelterCapacity(int shelterLevel) => shelterLevel <= 0 ? 0 : 4 + 2 * shelterLevel;
    }

    /// <summary>How base levels translate into night-defense numbers.</summary>
    public static class DefenseStats
    {
        public static float HqHpMultiplier(int hqLevel) => 1f + 0.3f * Math.Max(0, hqLevel - 1);
        public static float TowerDamageMultiplier(int level) => 1f + 0.3f * Math.Max(0, level - 1);
        public static float TowerRangeBonus(int level) => 0.3f * Math.Max(0, level - 1);
        public static float TowerIntervalMultiplier(int level) => 1f / (1f + 0.08f * Math.Max(0, level - 1));
        public static float HeroRegenMultiplier(int clinicLevel) => 1f + 0.15f * Math.Max(0, clinicLevel);
        public static float HeroRespawnMultiplier(int clinicLevel) => Math.Max(0.4f, 1f - 0.1f * Math.Max(0, clinicLevel));
    }
}
