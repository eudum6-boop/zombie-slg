using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    // Row types: plain serializable fields so the editor importer can store them in a ScriptableObject
    // and the runtime can rebuild GameData without parsing CSV.

    [Serializable]
    public sealed class ZombieRow
    {
        public string Id;
        public float Hp;
        public float Speed;
        public float Damage;
        public float AttackInterval;
        public float Radius;
        public float Scale;
    }

    [Serializable]
    public sealed class BuildingRow
    {
        public string Id;
        public string Name;
        public string Description;
        public int MaxLevel;
        public int RequiredHq;
        public int CostFood;
        public int CostFuel;
        public int CostMaterials;
        public double CostGrowth;
        public double BuildSeconds;
        public double BuildGrowth;
        public string Produces;
        public double RatePerHour;
        public double RateGrowth;
    }

    [Serializable]
    public sealed class HeroRow
    {
        public string Id;
        public string Name;
        public string Rarity;
        public string Role;
        public float Hp;
        public float Damage;
        public float FireInterval;
        public float Range;
        public float MoveSpeed;
        public float CritChance;
        public float SkillPower;
        public double EconBonus;
        public double ScoutBonus;
        public string Color;
        public bool Starter;
        public string Description;
    }

    [Serializable]
    public sealed class TroopRow
    {
        public string Id;
        public string Name;
        public string Description;
        public float Hp;
        public float Damage;
        public float FireInterval;
        public float Range;
        public double TrainSeconds;
        public int CostFood;
        public int CostFuel;
        public int CostMaterials;
        public double HealSeconds;
        public int HealFood;
        public int HealFuel;
        public int HealMaterials;
        public float VsWalker;
        public float VsRunner;
        public float VsBrute;
    }

    [Serializable]
    public sealed class ResearchRow
    {
        public string Id;
        public string Name;
        public string Branch;
        public int Tier;
        public string Requires;
        public int MaxLevel;
        public int CostFood;
        public int CostFuel;
        public int CostMaterials;
        public double CostGrowth;
        public double Seconds;
        public double TimeGrowth;
        public string Effect;
        public double PerLevel;
        public string Description;
    }

    [Serializable]
    public sealed class HordeRow
    {
        public float Time;
        public int Count;
    }

    [Serializable]
    public sealed class BalanceRow
    {
        public string Key;
        public double Value;
        public string Note;
    }

    /// <summary>All raw table rows. Produced by <see cref="GameTables.Parse"/>; turned into rules by <see cref="GameDataBuilder"/>.</summary>
    [Serializable]
    public sealed class GameTables
    {
        public const string ZombiesFile = "zombies.csv";
        public const string BuildingsFile = "buildings.csv";
        public const string HordesFile = "hordes.csv";
        public const string BalanceFile = "balance.csv";
        public const string HeroesFile = "heroes.csv";
        public const string TroopsFile = "troops.csv";
        public const string ResearchFile = "research.csv";
        public static readonly string[] FileNames = { ZombiesFile, BuildingsFile, HordesFile, BalanceFile, HeroesFile, TroopsFile, ResearchFile };

        public List<ZombieRow> Zombies = new List<ZombieRow>();
        public List<BuildingRow> Buildings = new List<BuildingRow>();
        public List<HordeRow> Hordes = new List<HordeRow>();
        public List<BalanceRow> Balance = new List<BalanceRow>();
        public List<HeroRow> Heroes = new List<HeroRow>();
        public List<TroopRow> Troops = new List<TroopRow>();
        public List<ResearchRow> Research = new List<ResearchRow>();

        /// <param name="readFile">Returns the text of a table file by name, or null if missing.</param>
        public static GameTables Parse(Func<string, string> readFile, List<string> errors)
        {
            var t = new GameTables();

            var zombies = Load(readFile, ZombiesFile, errors);
            if (zombies != null)
                foreach (var r in zombies.Records)
                    t.Zombies.Add(new ZombieRow
                    {
                        Id = r.Str("id"),
                        Hp = (float)r.Num("hp", errors, 1),
                        Speed = (float)r.Num("speed", errors, 0.01),
                        Damage = (float)r.Num("damage", errors, 0),
                        AttackInterval = (float)r.Num("attack_interval", errors, 0.05),
                        Radius = (float)r.Num("radius", errors, 0.05),
                        Scale = (float)r.Num("scale", errors, 0.1),
                    });

            var buildings = Load(readFile, BuildingsFile, errors);
            if (buildings != null)
                foreach (var r in buildings.Records)
                    t.Buildings.Add(new BuildingRow
                    {
                        Id = r.Str("id"),
                        Name = r.Str("name"),
                        Description = r.Str("description"),
                        MaxLevel = r.Int("max_level", errors, 1),
                        RequiredHq = r.Int("required_hq", errors, 0),
                        CostFood = r.Int("cost_food", errors, 0),
                        CostFuel = r.Int("cost_fuel", errors, 0),
                        CostMaterials = r.Int("cost_materials", errors, 0),
                        CostGrowth = r.Num("cost_growth", errors, 1),
                        BuildSeconds = r.Num("build_seconds", errors, 0),
                        BuildGrowth = r.Num("build_growth", errors, 1),
                        Produces = r.Str("produces"),
                        RatePerHour = r.Num("rate_per_hour", errors, 0),
                        RateGrowth = r.Num("rate_growth", errors, 1),
                    });

            var hordes = Load(readFile, HordesFile, errors);
            if (hordes != null)
                foreach (var r in hordes.Records)
                    t.Hordes.Add(new HordeRow { Time = (float)r.Num("time", errors, 0), Count = r.Int("count", errors, 0) });

            var balance = Load(readFile, BalanceFile, errors);
            if (balance != null)
                foreach (var r in balance.Records)
                    t.Balance.Add(new BalanceRow { Key = r.Str("key"), Value = r.Num("value", errors), Note = r.Str("note") });

            var heroes = Load(readFile, HeroesFile, errors);
            if (heroes != null)
                foreach (var r in heroes.Records)
                    t.Heroes.Add(new HeroRow
                    {
                        Id = r.Str("id"),
                        Name = r.Str("name"),
                        Rarity = r.Str("rarity"),
                        Role = r.Str("role"),
                        Hp = (float)r.Num("hp", errors, 0),
                        Damage = (float)r.Num("damage", errors, 0),
                        FireInterval = (float)r.Num("fire_interval", errors, 0),
                        Range = (float)r.Num("range", errors, 0),
                        MoveSpeed = (float)r.Num("move_speed", errors, 0),
                        CritChance = (float)r.Num("crit_chance", errors, 0),
                        SkillPower = (float)r.Num("skill_power", errors, 0),
                        EconBonus = r.Num("econ_bonus", errors, 0),
                        ScoutBonus = r.Num("scout_bonus", errors, 0),
                        Color = r.Str("color"),
                        Starter = r.Int("starter", errors, 0) != 0,
                        Description = r.Str("description"),
                    });

            var troops = Load(readFile, TroopsFile, errors);
            if (troops != null)
                foreach (var r in troops.Records)
                    t.Troops.Add(new TroopRow
                    {
                        Id = r.Str("id"),
                        Name = r.Str("name"),
                        Description = r.Str("description"),
                        Hp = (float)r.Num("hp", errors, 1),
                        Damage = (float)r.Num("damage", errors, 0),
                        FireInterval = (float)r.Num("fire_interval", errors, 0.05),
                        Range = (float)r.Num("range", errors, 0.5),
                        TrainSeconds = r.Num("train_seconds", errors, 0),
                        CostFood = r.Int("cost_food", errors, 0),
                        CostFuel = r.Int("cost_fuel", errors, 0),
                        CostMaterials = r.Int("cost_materials", errors, 0),
                        HealSeconds = r.Num("heal_seconds", errors, 0),
                        HealFood = r.Int("heal_food", errors, 0),
                        HealFuel = r.Int("heal_fuel", errors, 0),
                        HealMaterials = r.Int("heal_materials", errors, 0),
                        VsWalker = (float)r.Num("vs_walker", errors, 0),
                        VsRunner = (float)r.Num("vs_runner", errors, 0),
                        VsBrute = (float)r.Num("vs_brute", errors, 0),
                    });

            var research = Load(readFile, ResearchFile, errors);
            if (research != null)
                foreach (var r in research.Records)
                    t.Research.Add(new ResearchRow
                    {
                        Id = r.Str("id"),
                        Name = r.Str("name"),
                        Branch = r.Str("branch"),
                        Tier = r.Int("tier", errors, 1),
                        Requires = r.Str("requires"),
                        MaxLevel = r.Int("max_level", errors, 1),
                        CostFood = r.Int("cost_food", errors, 0),
                        CostFuel = r.Int("cost_fuel", errors, 0),
                        CostMaterials = r.Int("cost_materials", errors, 0),
                        CostGrowth = r.Num("cost_growth", errors, 1),
                        Seconds = r.Num("seconds", errors, 0),
                        TimeGrowth = r.Num("time_growth", errors, 1),
                        Effect = r.Str("effect"),
                        PerLevel = r.Num("per_level", errors, 0),
                        Description = r.Str("description"),
                    });

            return t;
        }

        private static CsvTable Load(Func<string, string> readFile, string name, List<string> errors)
        {
            string text = readFile(name);
            if (text == null)
            {
                errors.Add($"{name}: 파일을 찾을 수 없습니다");
                return null;
            }
            return CsvTable.Parse(name, text, errors);
        }
    }
}
