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


    public readonly struct NightReward
    {
        public readonly int Food;
        public readonly int Fuel;
        public readonly int Materials;
        public readonly int Tickets;

        public NightReward(int food, int fuel, int materials, int tickets)
        {
            Food = food;
            Fuel = fuel;
            Materials = materials;
            Tickets = tickets;
        }
    }

}
