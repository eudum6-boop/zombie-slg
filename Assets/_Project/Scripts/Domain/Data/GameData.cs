using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    /// <summary>Key → number table (balance.csv). Supports runtime overrides (Remote Config in M3).</summary>
    public sealed class BalanceTable
    {
        private readonly Dictionary<string, double> _values = new Dictionary<string, double>(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _values.Keys;
        public bool Has(string key) => _values.ContainsKey(key);

        public double Get(string key) =>
            _values.TryGetValue(key, out double v) ? v : throw new KeyNotFoundException("balance key missing: " + key);

        public float F(string key) => (float)Get(key);

        internal void Set(string key, double value) => _values[key] = value;

        /// <summary>Replaces existing keys only; returns how many were applied.</summary>
        public int ApplyOverrides(IEnumerable<KeyValuePair<string, double>> overrides)
        {
            int n = 0;
            foreach (var kv in overrides)
            {
                if (!_values.ContainsKey(kv.Key)) continue;
                _values[kv.Key] = kv.Value;
                n++;
            }
            return n;
        }
    }

    public sealed class EconomyRules
    {
        public double StartFood, StartFuel, StartMaterials, StartMorale;
        public int StartSurvivors;
        public double FoodPerSurvivorPerHour;
        public double MoralePerHour, FedMorale, StarvingMorale, MoraleFloor;
        public double UnstaffedEfficiency;
        public int WorkerSlotsBase, WorkerSlotEveryLevels;
        public int BuilderSlots;
        public double StorageBase, StorageGrowth;
        public int ShelterBase, ShelterPerLevel;

        public int StorageCap(int hqLevel) => (int)Math.Round(StorageBase * Math.Pow(StorageGrowth, Math.Max(0, hqLevel - 1)));
        public int ShelterCapacity(int level) => level <= 0 ? 0 : ShelterBase + ShelterPerLevel * level;

        public double MoraleMultiplier(double morale) =>
            MoraleFloor + (1 - MoraleFloor) * Math.Max(0, Math.Min(100, morale)) / 100.0;
    }

    public sealed class NightRules
    {
        public float HpPerNight, SpawnPerNight, HordePerNight, RewardPerNight;
        public int SurvivorsPerVictory;

        public static int Clamp(int night) => night < 1 ? 1 : night;
        public float HpMultiplier(int night) => 1f + HpPerNight * (Clamp(night) - 1);
        public float SpawnRateMultiplier(int night) => 1f + SpawnPerNight * (Clamp(night) - 1);
        public float HordeSizeMultiplier(int night) => 1f + HordePerNight * (Clamp(night) - 1);
        public float RewardMultiplier(int night) => 1f + RewardPerNight * (Clamp(night) - 1);
    }

    public sealed class RewardRules
    {
        public double FoodBase, FoodPerKill, FuelBase, FuelPerKill, MaterialsBase, MaterialsPerKill;
        public double DefeatFactor;
        public float Star3Hp, Star2Hp;
        public NightRules Night;

        /// <summary>Stars by remaining base HP. 0 on defeat.</summary>
        public int Stars(bool victory, float hqHpRatio)
        {
            if (!victory) return 0;
            if (hqHpRatio >= Star3Hp) return 3;
            if (hqHpRatio >= Star2Hp) return 2;
            return 1;
        }

        public NightReward Calculate(int night, bool victory, int kills, float hqHpRatio)
        {
            if (kills < 0) kills = 0;
            double mult = Night.RewardMultiplier(night) * (victory ? 1.0 : DefeatFactor);
            int food = (int)Math.Round((FoodBase + kills * FoodPerKill) * mult);
            int fuel = (int)Math.Round((FuelBase + kills * FuelPerKill) * mult);
            int materials = (int)Math.Round((MaterialsBase + kills * MaterialsPerKill) * mult);
            return new NightReward(food, fuel, materials, Stars(victory, hqHpRatio));
        }
    }

    public sealed class DefenseRules
    {
        public float HqHpPerLevel, TowerDamagePerLevel, TowerRangePerLevel, TowerRatePerLevel;
        public float ClinicRegenPerLevel, ClinicRespawnPerLevel, ClinicRespawnMin;

        public float HqHpMultiplier(int hqLevel) => 1f + HqHpPerLevel * Math.Max(0, hqLevel - 1);
        public float TowerDamageMultiplier(int level) => 1f + TowerDamagePerLevel * Math.Max(0, level - 1);
        public float TowerRangeBonus(int level) => TowerRangePerLevel * Math.Max(0, level - 1);
        public float TowerIntervalMultiplier(int level) => 1f / (1f + TowerRatePerLevel * Math.Max(0, level - 1));
        public float HeroRegenMultiplier(int clinicLevel) => 1f + ClinicRegenPerLevel * Math.Max(0, clinicLevel);
        public float HeroRespawnMultiplier(int clinicLevel) => Math.Max(ClinicRespawnMin, 1f - ClinicRespawnPerLevel * Math.Max(0, clinicLevel));
    }

    /// <summary>
    /// Every tunable number in the game, built from the CSV tables. Passed into systems instead of
    /// static tables, so tests, the server (M3) and Remote Config overrides all use the same rules.
    /// </summary>
    public sealed class GameData
    {
        private readonly Dictionary<ZombieType, ZombieStats> _zombies = new Dictionary<ZombieType, ZombieStats>();
        private readonly Dictionary<BuildingType, BuildingDef> _buildings = new Dictionary<BuildingType, BuildingDef>();

        public BalanceTable Balance { get; } = new BalanceTable();
        public EconomyRules Economy { get; } = new EconomyRules();
        public NightRules Night { get; } = new NightRules();
        public RewardRules Rewards { get; } = new RewardRules();
        public DefenseRules Defense { get; } = new DefenseRules();
        public WaveSettings Wave { get; } = new WaveSettings();

        public ZombieStats Zombie(ZombieType type) => _zombies[type];
        public BuildingDef Building(BuildingType type) => _buildings[type];
        public IEnumerable<BuildingDef> Buildings => _buildings.Values;

        internal void AddZombie(ZombieType t, ZombieStats s) => _zombies[t] = s;
        internal void AddBuilding(BuildingDef d) => _buildings[d.Type] = d;
        internal bool HasZombie(ZombieType t) => _zombies.ContainsKey(t);
        internal bool HasBuilding(BuildingType t) => _buildings.ContainsKey(t);
    }

    public static class GameDataBuilder
    {
        /// <summary>Validates the tables and builds rules. Returns null (with messages in <paramref name="errors"/>) when invalid.</summary>
        public static GameData Build(GameTables tables, List<string> errors)
        {
            if (tables == null) { errors.Add("데이터 테이블이 없습니다"); return null; }
            int before = errors.Count;
            var data = new GameData();

            // ---- zombies ----
            foreach (var r in tables.Zombies)
            {
                if (!Enum.TryParse(r.Id, true, out ZombieType type) || !Enum.IsDefined(typeof(ZombieType), type))
                {
                    errors.Add($"{GameTables.ZombiesFile}: 알 수 없는 좀비 id '{r.Id}' (가능: {string.Join(", ", Enum.GetNames(typeof(ZombieType)))})");
                    continue;
                }
                if (data.HasZombie(type)) { errors.Add($"{GameTables.ZombiesFile}: id '{r.Id}'가 중복됩니다"); continue; }
                data.AddZombie(type, new ZombieStats(r.Hp, r.Speed, r.Damage, r.AttackInterval, r.Radius, r.Scale));
            }
            foreach (ZombieType t in Enum.GetValues(typeof(ZombieType)))
                if (!data.HasZombie(t)) errors.Add($"{GameTables.ZombiesFile}: '{t}' 행이 없습니다");

            // ---- balance (needed before buildings: worker slot rule) ----
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in tables.Balance)
            {
                if (string.IsNullOrEmpty(r.Key)) { errors.Add($"{GameTables.BalanceFile}: key가 빈 행이 있습니다"); continue; }
                if (!seen.Add(r.Key)) errors.Add($"{GameTables.BalanceFile}: key '{r.Key}'가 중복됩니다");
                data.Balance.Set(r.Key, r.Value);
            }

            var b = new Reader(data.Balance, errors);
            var e = data.Economy;
            e.StartFood = b.D("economy.start_food", 0);
            e.StartFuel = b.D("economy.start_fuel", 0);
            e.StartMaterials = b.D("economy.start_materials", 0);
            e.StartSurvivors = b.I("economy.start_survivors", 0);
            e.StartMorale = b.D("economy.start_morale", 0, 100);
            e.FoodPerSurvivorPerHour = b.D("economy.food_per_survivor_hour", 0);
            e.MoralePerHour = b.D("economy.morale_per_hour", 0);
            e.FedMorale = b.D("economy.fed_morale", 0, 100);
            e.StarvingMorale = b.D("economy.starving_morale", 0, 100);
            e.MoraleFloor = b.D("economy.morale_floor", 0, 1);
            e.UnstaffedEfficiency = b.D("economy.unstaffed_efficiency", 0, 1);
            e.WorkerSlotsBase = b.I("economy.worker_slots_base", 1);
            e.WorkerSlotEveryLevels = b.I("economy.worker_slot_every_levels", 1);
            e.BuilderSlots = b.I("economy.builder_slots", 1);
            e.StorageBase = b.D("economy.storage_base", 1);
            e.StorageGrowth = b.D("economy.storage_growth", 1);
            e.ShelterBase = b.I("economy.shelter_base", 0);
            e.ShelterPerLevel = b.I("economy.shelter_per_level", 0);

            var n = data.Night;
            n.HpPerNight = b.F("night.hp_per_night", 0);
            n.SpawnPerNight = b.F("night.spawn_per_night", 0);
            n.HordePerNight = b.F("night.horde_per_night", 0);
            n.RewardPerNight = b.F("night.reward_per_night", 0);
            n.SurvivorsPerVictory = b.I("night.survivors_per_victory", 0);

            var rw = data.Rewards;
            rw.Night = n;
            rw.FoodBase = b.D("reward.food_base", 0);
            rw.FoodPerKill = b.D("reward.food_per_kill", 0);
            rw.FuelBase = b.D("reward.fuel_base", 0);
            rw.FuelPerKill = b.D("reward.fuel_per_kill", 0);
            rw.MaterialsBase = b.D("reward.materials_base", 0);
            rw.MaterialsPerKill = b.D("reward.materials_per_kill", 0);
            rw.DefeatFactor = b.D("reward.defeat_factor", 0, 1);
            rw.Star3Hp = b.F("reward.star3_hp", 0, 1);
            rw.Star2Hp = b.F("reward.star2_hp", 0, 1);
            if (rw.Star2Hp > rw.Star3Hp) errors.Add($"{GameTables.BalanceFile}: reward.star2_hp는 reward.star3_hp보다 클 수 없습니다");

            var d = data.Defense;
            d.HqHpPerLevel = b.F("defense.hq_hp_per_level", 0);
            d.TowerDamagePerLevel = b.F("defense.tower_damage_per_level", 0);
            d.TowerRangePerLevel = b.F("defense.tower_range_per_level", 0);
            d.TowerRatePerLevel = b.F("defense.tower_rate_per_level", 0);
            d.ClinicRegenPerLevel = b.F("defense.clinic_regen_per_level", 0);
            d.ClinicRespawnPerLevel = b.F("defense.clinic_respawn_per_level", 0);
            d.ClinicRespawnMin = b.F("defense.clinic_respawn_min", 0, 1);

            var w = data.Wave;
            w.Duration = b.F("wave.duration", 1);
            w.BaseRate = b.F("wave.base_rate", 0);
            w.PeakRate = b.F("wave.peak_rate", 0);
            w.RampEnd = b.F("wave.ramp_end", 0);
            w.RunnerUnlockTime = b.F("wave.runner_unlock", 0);
            w.BruteUnlockTime = b.F("wave.brute_unlock", 0);
            w.RunnerWeight = b.F("wave.runner_weight", 0, 1);
            w.BruteWeight = b.F("wave.brute_weight", 0, 1);
            w.MaxAlive = b.I("wave.max_alive", 1);
            w.NorthWeight = b.F("wave.side_north", 0, 1);
            w.EastWeight = b.F("wave.side_east", 0, 1);
            if (w.RunnerWeight + w.BruteWeight > 1f) errors.Add($"{GameTables.BalanceFile}: wave.runner_weight + wave.brute_weight는 1 이하여야 합니다");
            if (w.NorthWeight + w.EastWeight > 1f) errors.Add($"{GameTables.BalanceFile}: wave.side_north + wave.side_east는 1 이하여야 합니다");

            // Keys read by the presentation layer (hero, tower, skill...). Checked here so a typo fails at import.
            foreach (var key in PresentationKeys) b.D(key, null);

            // ---- hordes ----
            var hordes = new List<HordeEvent>();
            float last = -1f;
            foreach (var h in tables.Hordes)
            {
                if (h.Time <= last) errors.Add($"{GameTables.HordesFile}: time은 위에서 아래로 커져야 합니다 ({h.Time})");
                if (h.Time >= w.Duration) errors.Add($"{GameTables.HordesFile}: time {h.Time}이 밤 길이({w.Duration}초)보다 깁니다");
                last = h.Time;
                hordes.Add(new HordeEvent(h.Time, h.Count));
            }
            w.Hordes = hordes.ToArray();

            // ---- buildings ----
            foreach (var r in tables.Buildings)
            {
                if (!Enum.TryParse(r.Id, true, out BuildingType type) || !Enum.IsDefined(typeof(BuildingType), type))
                {
                    errors.Add($"{GameTables.BuildingsFile}: 알 수 없는 건물 id '{r.Id}' (가능: {string.Join(", ", Enum.GetNames(typeof(BuildingType)))})");
                    continue;
                }
                if (data.HasBuilding(type)) { errors.Add($"{GameTables.BuildingsFile}: id '{r.Id}'가 중복됩니다"); continue; }

                bool producer = !string.IsNullOrEmpty(r.Produces);
                var produces = ResourceType.Food;
                if (producer && !Enum.TryParse(r.Produces, true, out produces))
                {
                    errors.Add($"{GameTables.BuildingsFile}: '{r.Id}'의 produces '{r.Produces}'를 알 수 없습니다 (Food/Fuel/Materials)");
                    continue;
                }
                if (producer && r.RatePerHour <= 0) errors.Add($"{GameTables.BuildingsFile}: 생산 건물 '{r.Id}'의 rate_per_hour가 0입니다");

                data.AddBuilding(new BuildingDef(type, string.IsNullOrEmpty(r.Name) ? r.Id : r.Name, r.Description,
                    r.MaxLevel, r.RequiredHq, new Cost(r.CostFood, r.CostFuel, r.CostMaterials), r.CostGrowth,
                    r.BuildSeconds, r.BuildGrowth, producer, produces, r.RatePerHour, r.RateGrowth,
                    e.WorkerSlotsBase, e.WorkerSlotEveryLevels));
            }
            foreach (BuildingType t in Enum.GetValues(typeof(BuildingType)))
                if (!data.HasBuilding(t)) errors.Add($"{GameTables.BuildingsFile}: '{t}' 행이 없습니다");

            return errors.Count == before ? data : null;
        }

        /// <summary>balance.csv keys consumed outside the domain (BattleConfig). Listed so validation covers them.</summary>
        public static readonly string[] PresentationKeys =
        {
            "zombie.aggro_range", "hq.max_hp",
            "hero.max_hp", "hero.move_speed", "hero.range", "hero.fire_interval", "hero.damage",
            "hero.crit_chance", "hero.crit_multiplier", "hero.regen_per_second", "hero.respawn_seconds",
            "skill.firebomb.cooldown", "skill.firebomb.radius", "skill.firebomb.damage", "skill.firebomb.edge_factor",
            "skill.firebomb.burn_dps", "skill.firebomb.burn_duration",
            "tower.range", "tower.fire_interval", "tower.damage", "tower.crit_chance", "tower.crit_multiplier",
        };

        private readonly struct Reader
        {
            private readonly BalanceTable _t;
            private readonly List<string> _errors;

            public Reader(BalanceTable t, List<string> errors) { _t = t; _errors = errors; }

            public double D(string key, double? min, double? max = null)
            {
                if (!_t.Has(key)) { _errors.Add($"{GameTables.BalanceFile}: '{key}' 행이 없습니다"); return 0; }
                double v = _t.Get(key);
                if (min.HasValue && v < min.Value) _errors.Add($"{GameTables.BalanceFile}: {key} = {v} — {min.Value} 이상이어야 합니다");
                if (max.HasValue && v > max.Value) _errors.Add($"{GameTables.BalanceFile}: {key} = {v} — {max.Value} 이하여야 합니다");
                return v;
            }

            public float F(string key, double? min, double? max = null) => (float)D(key, min, max);

            public int I(string key, double? min)
            {
                double v = D(key, min);
                if (Math.Abs(v - Math.Round(v)) > 1e-9) _errors.Add($"{GameTables.BalanceFile}: {key} = {v} — 정수여야 합니다");
                return (int)Math.Round(v);
            }
        }
    }
}
