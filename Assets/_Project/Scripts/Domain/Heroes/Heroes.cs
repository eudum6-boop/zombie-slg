using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    public enum HeroRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3,
    }

    public enum HeroRole
    {
        Combat = 0,   // fights at night (Leader slot)
        Economy = 1,  // Manager slot: base production bonus
        Explore = 2,  // Scout slot: night reward bonus
    }

    public enum HeroSlot
    {
        Leader = 0,
        Manager = 1,
        Scout = 2,
    }

    public sealed class HeroDef
    {
        public string Id;
        public string Name;
        public HeroRarity Rarity;
        public HeroRole Role;
        public float Hp, Damage, FireInterval, Range, MoveSpeed, CritChance, SkillPower;
        public double EconBonus, ScoutBonus;
        public string ColorHex;
        public bool Starter;
        public string Description;
        public int Order; // table order, keeps gacha picks deterministic

        public static HeroRole RoleFor(HeroSlot slot) =>
            slot == HeroSlot.Manager ? HeroRole.Economy : slot == HeroSlot.Scout ? HeroRole.Explore : HeroRole.Combat;
    }

    public readonly struct HeroCombatStats
    {
        public readonly float MaxHp, Damage, FireInterval, Range, MoveSpeed, CritChance, SkillPower;

        public HeroCombatStats(float maxHp, float damage, float fireInterval, float range, float moveSpeed, float critChance, float skillPower)
        {
            MaxHp = maxHp;
            Damage = damage;
            FireInterval = fireInterval;
            Range = range;
            MoveSpeed = moveSpeed;
            CritChance = critChance;
            SkillPower = skillPower;
        }
    }

    public sealed class HeroRules
    {
        public float StarStatBonus;
        /// <summary>StarCosts[s] = shards needed to go from ★(s-1) to ★s. Index 0/1 unused.</summary>
        public int[] StarCosts = new int[0];
        public int[] DupShards = new int[4];

        public int MaxStars => Math.Max(1, StarCosts.Length - 1);
        public float StatMultiplier(int stars) => 1f + StarStatBonus * Math.Max(0, stars - 1);
        public int CostToReach(int stars) => stars >= 2 && stars < StarCosts.Length ? StarCosts[stars] : 0;
        public int DuplicateShards(HeroRarity rarity) => DupShards[(int)rarity];
    }

    public sealed class GachaRules
    {
        /// <summary>Indexed by HeroRarity; sums to 1.</summary>
        public double[] RarityRates = new double[4];
        public int PityLegendary;
        public bool TenPullGuaranteeRare;
        public int StartTickets;

        public readonly struct Disclosure
        {
            public readonly string HeroId;
            public readonly string Name;
            public readonly HeroRarity Rarity;
            public readonly double Probability;

            public Disclosure(string heroId, string name, HeroRarity rarity, double probability)
            {
                HeroId = heroId;
                Name = name;
                Rarity = rarity;
                Probability = probability;
            }
        }

        /// <summary>Per-hero probability of a single (non-pity) pull, for the legally required disclosure screen.</summary>
        public List<Disclosure> Disclose(IReadOnlyList<HeroDef> heroes)
        {
            var list = new List<Disclosure>();
            for (int r = 0; r < 4; r++)
            {
                int count = 0;
                foreach (var h in heroes) if ((int)h.Rarity == r) count++;
                if (count == 0) continue;
                foreach (var h in heroes)
                    if ((int)h.Rarity == r) list.Add(new Disclosure(h.Id, h.Name, h.Rarity, RarityRates[r] / count));
            }
            return list;
        }
    }

    public readonly struct GachaResult
    {
        public readonly string HeroId;
        public readonly HeroRarity Rarity;
        public readonly bool IsNew;
        public readonly int ShardsGained;
        public readonly bool FromPity;
        public readonly bool FromGuarantee;

        public GachaResult(string heroId, HeroRarity rarity, bool isNew, int shardsGained, bool fromPity, bool fromGuarantee)
        {
            HeroId = heroId;
            Rarity = rarity;
            IsNew = isNew;
            ShardsGained = shardsGained;
            FromPity = fromPity;
            FromGuarantee = fromGuarantee;
        }
    }

    public enum PullBlock { None, NotEnoughTickets, InvalidCount }
    public enum PromoteBlock { None, NotOwned, MaxStars, NotEnoughShards }
    public enum AssignBlock { None, NotOwned, WrongRole }

    /// <summary>
    /// The player's heroes: recruiting (gacha with pity), shard promotion and slot assignment.
    /// Pulls are deterministic from the save's seed and pull counter, so the server (M3) can
    /// replay and verify any result.
    /// </summary>
    public sealed class HeroRoster
    {
        private readonly GameSave _s;
        private readonly GameData _data;

        public HeroRoster(GameSave save, GameData data)
        {
            _s = save ?? throw new ArgumentNullException(nameof(save));
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public int Tickets => _s.RecruitTickets;
        public int PityCounter => _s.GachaPity;
        public int PullsUntilPity => Math.Max(1, _data.Gacha.PityLegendary - _s.GachaPity);
        public int TotalPulls => _s.GachaPulls;

        public HeroSave Get(string id)
        {
            foreach (var h in _s.Heroes) if (h.Id == id) return h;
            return null;
        }

        public bool Owns(string id) => Get(id) != null;
        public int Stars(string id) => Get(id)?.Stars ?? 0;

        public string AssignedId(HeroSlot slot)
        {
            switch (slot)
            {
                case HeroSlot.Manager: return _s.ManagerHero;
                case HeroSlot.Scout: return _s.ScoutHero;
                default: return _s.LeaderHero;
            }
        }

        public HeroDef Assigned(HeroSlot slot)
        {
            string id = AssignedId(slot);
            return !string.IsNullOrEmpty(id) && Owns(id) ? _data.Hero(id) : null;
        }

        // ---------------------------------------------------------------- gacha

        public PullBlock CanPull(int count)
        {
            if (count != 1 && count != 10) return PullBlock.InvalidCount;
            return _s.RecruitTickets < count ? PullBlock.NotEnoughTickets : PullBlock.None;
        }

        public PullBlock Pull(int count, List<GachaResult> results)
        {
            var block = CanPull(count);
            if (block != PullBlock.None) return block;
            results.Clear();
            _s.RecruitTickets -= count;

            var g = _data.Gacha;
            bool batchHasRarePlus = false;
            for (int i = 0; i < count; i++)
            {
                var rng = new Random(Mix(_s.GachaSeed, _s.GachaPulls));
                bool pity = _s.GachaPity + 1 >= g.PityLegendary;
                HeroRarity rarity = pity ? HeroRarity.Legendary : RollRarity(rng.NextDouble(), g.RarityRates);

                bool guarantee = false;
                if (count == 10 && i == count - 1 && g.TenPullGuaranteeRare && !batchHasRarePlus && rarity == HeroRarity.Common)
                {
                    rarity = HeroRarity.Rare;
                    guarantee = true;
                }
                if (rarity >= HeroRarity.Rare) batchHasRarePlus = true;

                var pool = _data.HeroesOfRarity(rarity);
                var def = pool[rng.Next(pool.Count)];

                _s.GachaPity = rarity == HeroRarity.Legendary ? 0 : _s.GachaPity + 1;
                _s.GachaPulls++;

                var owned = Get(def.Id);
                int shards = 0;
                if (owned == null) _s.Heroes.Add(new HeroSave { Id = def.Id, Stars = 1, Shards = 0 });
                else
                {
                    shards = _data.HeroRules.DuplicateShards(rarity);
                    owned.Shards += shards;
                }
                results.Add(new GachaResult(def.Id, rarity, owned == null, shards, pity, guarantee));
            }
            return PullBlock.None;
        }

        private static HeroRarity RollRarity(double roll, double[] rates)
        {
            // Highest rarity first so float rounding never hands out an extra legendary.
            double acc = 0;
            for (int r = 3; r >= 1; r--)
            {
                acc += rates[r];
                if (roll < acc) return (HeroRarity)r;
            }
            return HeroRarity.Common;
        }

        private static int Mix(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)index * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        // ---------------------------------------------------------------- promotion / slots

        public PromoteBlock CanPromote(string id)
        {
            var h = Get(id);
            if (h == null) return PromoteBlock.NotOwned;
            var rules = _data.HeroRules;
            if (h.Stars >= rules.MaxStars) return PromoteBlock.MaxStars;
            return h.Shards < rules.CostToReach(h.Stars + 1) ? PromoteBlock.NotEnoughShards : PromoteBlock.None;
        }

        public PromoteBlock Promote(string id)
        {
            var block = CanPromote(id);
            if (block != PromoteBlock.None) return block;
            var h = Get(id);
            h.Shards -= _data.HeroRules.CostToReach(h.Stars + 1);
            h.Stars++;
            return PromoteBlock.None;
        }

        public AssignBlock CanAssign(HeroSlot slot, string id)
        {
            if (!Owns(id)) return AssignBlock.NotOwned;
            return _data.Hero(id).Role != HeroDef.RoleFor(slot) ? AssignBlock.WrongRole : AssignBlock.None;
        }

        public AssignBlock Assign(HeroSlot slot, string id)
        {
            var block = CanAssign(slot, id);
            if (block != AssignBlock.None) return block;
            switch (slot)
            {
                case HeroSlot.Manager: _s.ManagerHero = id; break;
                case HeroSlot.Scout: _s.ScoutHero = id; break;
                default: _s.LeaderHero = id; break;
            }
            return AssignBlock.None;
        }

        // ---------------------------------------------------------------- effects

        public HeroCombatStats CombatStats(string id)
        {
            var def = _data.Hero(id);
            float m = _data.HeroRules.StatMultiplier(Math.Max(1, Stars(id)));
            // Fire interval shrinks instead of growing with stars.
            return new HeroCombatStats(def.Hp * m, def.Damage * m, def.FireInterval / m, def.Range, def.MoveSpeed,
                def.CritChance, def.SkillPower * m);
        }

        /// <summary>Night leader; falls back to the starter if the slot is empty.</summary>
        public HeroDef Leader => Assigned(HeroSlot.Leader) ?? _data.StarterHero;

        public double ManagerBonus => SlotBonus(_s, _data, HeroSlot.Manager);
        public double ScoutBonus => SlotBonus(_s, _data, HeroSlot.Scout);

        /// <summary>Static so BaseState can use it without holding a roster.</summary>
        public static double SlotBonus(GameSave s, GameData data, HeroSlot slot)
        {
            string id = slot == HeroSlot.Manager ? s.ManagerHero : slot == HeroSlot.Scout ? s.ScoutHero : null;
            if (string.IsNullOrEmpty(id) || !data.HasHero(id)) return 0;
            HeroSave owned = null;
            foreach (var h in s.Heroes) if (h.Id == id) owned = h;
            if (owned == null) return 0;
            var def = data.Hero(id);
            double baseBonus = slot == HeroSlot.Manager ? def.EconBonus : def.ScoutBonus;
            return baseBonus * data.HeroRules.StatMultiplier(owned.Stars);
        }
    }
}
