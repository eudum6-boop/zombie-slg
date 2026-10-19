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
        public static readonly string[] FileNames = { ZombiesFile, BuildingsFile, HordesFile, BalanceFile };

        public List<ZombieRow> Zombies = new List<ZombieRow>();
        public List<BuildingRow> Buildings = new List<BuildingRow>();
        public List<HordeRow> Hordes = new List<HordeRow>();
        public List<BalanceRow> Balance = new List<BalanceRow>();

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
