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
        public const int CurrentVersion = 1;

        public int Version;
        public double LastTick;
        public double Food;
        public double Fuel;
        public double Materials;
        public int HeroShards;
        public int Night = 1;
        public int BestNight;
        public int TotalKills;
        public int Survivors;
        public double Morale;
        public bool Starving;
        public List<BuildingSave> Buildings = new List<BuildingSave>();
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
        };
    }

    public static class SaveMigrator
    {
        public static GameSave NewGame(double now)
        {
            var s = new GameSave
            {
                Version = GameSave.CurrentVersion,
                LastTick = now,
                Food = 400,
                Fuel = 250,
                Materials = 400,
                Night = 1,
                Survivors = 4,
                Morale = 70,
            };
            EnsureBuildings(s);
            return s;
        }

        /// <summary>
        /// Upgrades an older save in place. Also repairs missing plots, so adding a building in a
        /// later update never breaks existing players.
        /// </summary>
        public static GameSave Migrate(GameSave s, double now)
        {
            if (s == null) return NewGame(now);
            if (s.Buildings == null) s.Buildings = new List<BuildingSave>();

            if (s.Version < 1)
            {
                // v0 → v1: pre-release saves had no morale/survivor fields.
                if (s.Survivors <= 0) s.Survivors = 4;
                if (s.Morale <= 0) s.Morale = 70;
                if (s.Night <= 0) s.Night = 1;
                if (s.LastTick <= 0) s.LastTick = now;
            }

            EnsureBuildings(s);
            s.Version = GameSave.CurrentVersion;
            return s;
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
