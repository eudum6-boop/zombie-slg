using System;

namespace ZombieSLG.Domain
{
    public enum ZombieType
    {
        Walker = 0,
        Runner = 1,
        Brute = 2,
    }

    public readonly struct ZombieStats
    {
        public readonly float MaxHp;
        public readonly float Speed;
        public readonly float AttackDamage;
        public readonly float AttackInterval;
        public readonly float Radius;
        public readonly float VisualScale;

        public ZombieStats(float maxHp, float speed, float attackDamage, float attackInterval, float radius, float visualScale)
        {
            MaxHp = maxHp;
            Speed = speed;
            AttackDamage = attackDamage;
            AttackInterval = attackInterval;
            Radius = radius;
            VisualScale = visualScale;
        }

        public ZombieStats WithHpMultiplier(float multiplier) =>
            new ZombieStats(MaxHp * multiplier, Speed, AttackDamage, AttackInterval, Radius, VisualScale);
    }

    /// <summary>Base stats per zombie type. Hard-coded for M0; moves to the CSV → ScriptableObject pipeline in M2.</summary>
    public static class ZombieStatTable
    {
        private static readonly ZombieStats Walker = new ZombieStats(38f, 1.35f, 10f, 1.0f, 0.32f, 1.0f);
        private static readonly ZombieStats Runner = new ZombieStats(22f, 2.9f, 6f, 0.7f, 0.28f, 0.85f);
        private static readonly ZombieStats Brute = new ZombieStats(240f, 0.88f, 38f, 1.4f, 0.55f, 1.6f);

        public static ZombieStats Get(ZombieType type)
        {
            switch (type)
            {
                case ZombieType.Runner: return Runner;
                case ZombieType.Brute: return Brute;
                default: return Walker;
            }
        }
    }

    public readonly struct HitResult
    {
        public readonly float Damage;
        public readonly bool IsCritical;

        public HitResult(float damage, bool isCritical)
        {
            Damage = damage;
            IsCritical = isCritical;
        }
    }

    public static class CombatRules
    {
        /// <param name="roll01">Uniform random value in [0,1). Injected so tests are deterministic.</param>
        public static HitResult RollHit(float baseDamage, float critChance, float critMultiplier, double roll01)
        {
            if (baseDamage < 0f) baseDamage = 0f;
            bool crit = roll01 < critChance;
            return new HitResult(crit ? baseDamage * critMultiplier : baseDamage, crit);
        }

        /// <summary>Area damage that falls off linearly from full at the centre to <paramref name="edgeFactor"/> at the rim.</summary>
        public static float AreaDamage(float damage, float distance, float radius, float edgeFactor = 0.5f)
        {
            if (radius <= 0f || distance > radius) return 0f;
            float t = distance / radius;
            return damage * (1f - t * (1f - edgeFactor));
        }
    }

    public static class NightScaling
    {
        public static int Clamp(int night) => night < 1 ? 1 : night;
        public static float HpMultiplier(int night) => 1f + 0.2f * (Clamp(night) - 1);
        public static float SpawnRateMultiplier(int night) => 1f + 0.2f * (Clamp(night) - 1);
        public static float HordeSizeMultiplier(int night) => 1f + 0.25f * (Clamp(night) - 1);
        public static float RewardMultiplier(int night) => 1f + 0.25f * (Clamp(night) - 1);
    }

    public readonly struct NightReward
    {
        public readonly int Food;
        public readonly int Fuel;
        public readonly int Materials;
        public readonly int HeroShards;

        public NightReward(int food, int fuel, int materials, int heroShards)
        {
            Food = food;
            Fuel = fuel;
            Materials = materials;
            HeroShards = heroShards;
        }
    }

    public static class NightRewardCalculator
    {
        /// <summary>Stars by remaining base HP: 3 at 70%+, 2 at 30%+, 1 otherwise. 0 on defeat.</summary>
        public static int Stars(bool victory, float hqHpRatio)
        {
            if (!victory) return 0;
            if (hqHpRatio >= 0.7f) return 3;
            if (hqHpRatio >= 0.3f) return 2;
            return 1;
        }

        public static NightReward Calculate(int night, bool victory, int kills, float hqHpRatio)
        {
            if (kills < 0) kills = 0;
            float mult = NightScaling.RewardMultiplier(night) * (victory ? 1f : 0.3f);
            int food = (int)Math.Round((100 + kills * 2) * mult);
            int fuel = (int)Math.Round((60 + kills) * mult);
            int materials = (int)Math.Round((40 + kills * 1.5) * mult);
            int shards = Stars(victory, hqHpRatio);
            return new NightReward(food, fuel, materials, shards);
        }
    }
}
