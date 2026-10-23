using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    /// <summary>
    /// Persistent game state. Plain public fields so it serialises with JsonUtility (client) and
    /// System.Text.Json (Cloud Code) alike. <see cref="Version"/> drives <see cref="SaveMigrator"/>.
    /// </summary>
    [Serializable]
    public sealed class GameSave
    {
        public const int CurrentVersion = 3;

        public int Version;
        public double LastTick;
        public double Food;
        public double Fuel;
        public double Materials;
        /// <summary>v1 generic shards; converted to recruit tickets by the v2 migration.</summary>
        public int HeroShards;
        public int Night = 1;
        public int BestNight;
        public int TotalKills;
        public int Survivors;
        public double Morale;
        public bool Starving;
        public List<BuildingSave> Buildings = new List<BuildingSave>();

        // v2: heroes & recruiting
        public int RecruitTickets;
        public int GachaSeed;
        public int GachaPulls;
        public int GachaPity;
        public string LeaderHero;
        public string ManagerHero;
        public string ScoutHero;
        public List<HeroSave> Heroes = new List<HeroSave>();

        // v3: troops & research
        public List<TroopStock> Troops = new List<TroopStock>();
        public List<QueueItem> Training = new List<QueueItem>();
        public List<QueueItem> Healing = new List<QueueItem>();
        public string ResearchActive;
        public double ResearchEnd;
        public List<ResearchSave> Research = new List<ResearchSave>();
    }

    [Serializable]
    public sealed class TroopStock
    {
        public TroopType Type;
        public int Healthy;
        public int Wounded;
    }

    /// <summary>One batch in the training or healing queue. Batches run one after another.</summary>
    [Serializable]
    public sealed class QueueItem
    {
        public TroopType Type;
        public int Count;
        public double End;
    }

    [Serializable]
    public sealed class ResearchSave
    {
        public string Id;
        public int Level;
    }

    [Serializable]
    public sealed class HeroSave
    {
        public string Id;
        public int Stars;
        public int Shards;
    }

    [Serializable]
    public sealed class BuildingSave
    {
        public int Id;
        public BuildingType Type;
        public int Level;
        public int Workers;
        public bool Upgrading;
        public double UpgradeEnd;
    }

    /// <summary>Fixed building plots on the M1 base map. Ids are stable save keys.</summary>
    public static class BasePlots
    {
        public const int Headquarters = 0;
        public const int Farm = 1;
        public const int FuelPump = 2;
        public const int Workshop = 3;
        public const int Shelter = 4;
        public const int Clinic = 5;
        public const int TowerA = 6;
        public const int TowerB = 7;
        public const int Barracks = 8;
        public const int ResearchLab = 9;

        /// <summary>(id, type, starting level, starting workers)</summary>
        public static readonly (int id, BuildingType type, int level, int workers)[] Defaults =
        {
            (Headquarters, BuildingType.Headquarters, 1, 0),
            (Farm, BuildingType.Farm, 1, 1),
            (FuelPump, BuildingType.FuelPump, 1, 1),
            (Workshop, BuildingType.Workshop, 1, 1),
            (Shelter, BuildingType.Shelter, 1, 0),
            (Clinic, BuildingType.Clinic, 0, 0),
            (TowerA, BuildingType.GunTower, 1, 0),
            (TowerB, BuildingType.GunTower, 0, 0),
            (Barracks, BuildingType.Barracks, 0, 0),
            (ResearchLab, BuildingType.ResearchLab, 0, 0),
        };
    }

    public static class SaveMigrator
    {
        public static GameSave NewGame(double now, GameData data)
        {
            var economy = data.Economy;
            var s = new GameSave
            {
                Version = GameSave.CurrentVersion,
                LastTick = now,
                Food = economy.StartFood,
                Fuel = economy.StartFuel,
                Materials = economy.StartMaterials,
                Night = 1,
                Survivors = economy.StartSurvivors,
                Morale = economy.StartMorale,
                RecruitTickets = data.Gacha.StartTickets,
                GachaSeed = SeedFrom(now),
            };
            EnsureBuildings(s);
            EnsureStarterHero(s, data);
            EnsureTroops(s);
            Stock(s, TroopType.Infantry).Healthy = data.Troops.StartInfantry;
            return s;
        }

        /// <summary>
        /// Upgrades an older save in place. Also repairs missing plots, so adding a building in a
        /// later update never breaks existing players.
        /// </summary>
        public static GameSave Migrate(GameSave s, double now, GameData data)
        {
            if (s == null) return NewGame(now, data);
            var economy = data.Economy;
            if (s.Buildings == null) s.Buildings = new List<BuildingSave>();
            if (s.Heroes == null) s.Heroes = new List<HeroSave>();
            if (s.Troops == null) s.Troops = new List<TroopStock>();
            if (s.Training == null) s.Training = new List<QueueItem>();
            if (s.Healing == null) s.Healing = new List<QueueItem>();
            if (s.Research == null) s.Research = new List<ResearchSave>();

            if (s.Version < 1)
            {
                // v0 → v1: pre-release saves had no morale/survivor fields.
                if (s.Survivors <= 0) s.Survivors = economy.StartSurvivors;
                if (s.Morale <= 0) s.Morale = economy.StartMorale;
                if (s.Night <= 0) s.Night = 1;
                if (s.LastTick <= 0) s.LastTick = now;
            }

            if (s.Version < 2)
            {
                // v1 → v2: heroes arrive. Old generic hero shards become recruit tickets (1:1),
                // plus the new-game starter tickets so existing players can try recruiting.
                s.RecruitTickets += s.HeroShards + data.Gacha.StartTickets;
                s.HeroShards = 0;
                if (s.GachaSeed == 0) s.GachaSeed = SeedFrom(now);
            }

            if (s.Version < 3)
            {
                // v2 → v3: troops arrive. Existing players get the same starting squad as a new game.
                EnsureTroops(s);
                Stock(s, TroopType.Infantry).Healthy += data.Troops.StartInfantry;
            }

            EnsureBuildings(s);
            EnsureStarterHero(s, data);
            EnsureTroops(s);
            // Research removed from the table: drop progress (and a running project) instead of crashing.
            s.Research.RemoveAll(r => !data.HasResearch(r.Id));
            if (s.ResearchActive != null && !data.HasResearch(s.ResearchActive)) s.ResearchActive = null;
            // Drop heroes removed from the table so a data change can't crash an old save.
            s.Heroes.RemoveAll(h => !data.HasHero(h.Id));
            if (!string.IsNullOrEmpty(s.ManagerHero) && !data.HasHero(s.ManagerHero)) s.ManagerHero = null;
            if (!string.IsNullOrEmpty(s.ScoutHero) && !data.HasHero(s.ScoutHero)) s.ScoutHero = null;
            s.Version = GameSave.CurrentVersion;
            return s;
        }

        private static void EnsureStarterHero(GameSave s, GameData data)
        {
            var starter = data.StarterHero;
            if (!s.Heroes.Exists(h => h.Id == starter.Id))
                s.Heroes.Insert(0, new HeroSave { Id = starter.Id, Stars = 1, Shards = 0 });
            if (string.IsNullOrEmpty(s.LeaderHero) || !s.Heroes.Exists(h => h.Id == s.LeaderHero)) s.LeaderHero = starter.Id;
        }

        /// <summary>One stock row per troop type, in enum order.</summary>
        internal static void EnsureTroops(GameSave s)
        {
            foreach (TroopType t in Enum.GetValues(typeof(TroopType)))
                if (!s.Troops.Exists(x => x.Type == t)) s.Troops.Add(new TroopStock { Type = t });
            s.Troops.Sort((a, b) => a.Type.CompareTo(b.Type));
        }

        internal static TroopStock Stock(GameSave s, TroopType t)
        {
            foreach (var x in s.Troops) if (x.Type == t) return x;
            var row = new TroopStock { Type = t };
            s.Troops.Add(row);
            return row;
        }

        private static int SeedFrom(double now)
        {
            unchecked
            {
                long t = (long)(now * 1000);
                int seed = (int)(t ^ (t >> 32)) & 0x7FFFFFFF;
                return seed == 0 ? 1 : seed;
            }
        }

        private static void EnsureBuildings(GameSave s)
        {
            foreach (var (id, type, level, workers) in BasePlots.Defaults)
            {
                if (s.Buildings.Exists(b => b.Id == id)) continue;
                s.Buildings.Add(new BuildingSave { Id = id, Type = type, Level = level, Workers = workers });
            }
            s.Buildings.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
    }
}
