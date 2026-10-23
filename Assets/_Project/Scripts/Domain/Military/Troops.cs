using System;

namespace ZombieSLG.Domain
{
    public enum TroopType
    {
        Infantry = 0,
        Shooter = 1,
        Vehicle = 2,
    }

    public sealed class TroopDef
    {
        public TroopType Type;
        public string Name;
        public string Description;
        public float Hp, Damage, FireInterval, Range;
        public double TrainSeconds;
        public Cost TrainCost;
        public double HealSeconds;
        public Cost HealCost;
        /// <summary>Damage multiplier against each ZombieType (counter system).</summary>
        public float[] VsZombie = { 1f, 1f, 1f };

        public float CounterAgainst(ZombieType zombie) => VsZombie[(int)zombie];
    }

    public sealed class TroopRules
    {
        public int CapacityBase, CapacityPerLevel;
        public double WoundedRatio;
        public double HealSpeedPerClinic, HealSpeedMin;
        public int StartInfantry;

        public const int TypeCount = 3;
        /// <summary>Who absorbs damage first in a squad.</summary>
        public static readonly TroopType[] FrontOrder = { TroopType.Infantry, TroopType.Vehicle, TroopType.Shooter };

        public int Capacity(int barracksLevel) => barracksLevel <= 0 ? 0 : CapacityBase + CapacityPerLevel * (barracksLevel - 1);
        public double HealTimeMultiplier(int clinicLevel) => Math.Max(HealSpeedMin, 1.0 - HealSpeedPerClinic * Math.Max(0, clinicLevel));
    }

    /// <summary>
    /// One squad holding a gate during the night. Pure rules: damage eats the front unit type first,
    /// each fallen unit becomes wounded or dead by a deterministic ratio accumulator (no RNG drift).
    /// </summary>
    public sealed class SquadState
    {
        private readonly float[] _unitHp = new float[TroopRules.TypeCount];
        private readonly float[] _carry = new float[TroopRules.TypeCount];
        private readonly int[] _alive = new int[TroopRules.TypeCount];
        private readonly int[] _dead = new int[TroopRules.TypeCount];
        private readonly int[] _wounded = new int[TroopRules.TypeCount];
        private readonly double _woundedRatio;
        private double _woundAcc;

        public SquadState(GameData data, int[] counts, float hpMultiplier)
        {
            _woundedRatio = data.Troops.WoundedRatio;
            for (int i = 0; i < TroopRules.TypeCount; i++)
            {
                _alive[i] = counts != null && i < counts.Length ? Math.Max(0, counts[i]) : 0;
                _unitHp[i] = data.Troop((TroopType)i).Hp * Math.Max(0.01f, hpMultiplier);
            }
        }

        public int Alive(TroopType t) => _alive[(int)t];
        public int Dead(TroopType t) => _dead[(int)t];
        public int Wounded(TroopType t) => _wounded[(int)t];

        public int TotalAlive
        {
            get
            {
                int n = 0;
                foreach (var a in _alive) n += a;
                return n;
            }
        }

        public bool IsDefeated => TotalAlive == 0;

        /// <summary>Applies damage to the front-most surviving type. Returns how many units fell.</summary>
        public int TakeDamage(float damage)
        {
            int fell = 0;
            while (damage > 0f && !IsDefeated)
            {
                int t = -1;
                foreach (var type in TroopRules.FrontOrder)
                    if (_alive[(int)type] > 0) { t = (int)type; break; }
                if (t < 0) break;

                float need = _unitHp[t] - _carry[t];
                if (damage < need)
                {
                    _carry[t] += damage;
                    break;
                }
                damage -= need;
                _carry[t] = 0f;
                _alive[t]--;
                fell++;

                _woundAcc += _woundedRatio;
                if (_woundAcc >= 1.0 - 1e-9) { _woundAcc -= 1.0; _wounded[t]++; }
                else _dead[t]++;
            }
            return fell;
        }

        /// <summary>Front unit's health 0..1 (for a squad HP bar).</summary>
        public float HealthRatio(GameData data, int[] startCounts)
        {
            float max = 0f, cur = 0f;
            for (int i = 0; i < TroopRules.TypeCount; i++)
            {
                max += startCounts[i] * _unitHp[i];
                cur += _alive[i] * _unitHp[i] - (_alive[i] > 0 ? _carry[i] : 0f);
            }
            return max <= 0f ? 0f : Math.Max(0f, cur / max);
        }
    }

    public static class SquadMath
    {
        /// <summary>Total damage of one volley by every living unit of a type against a zombie type.</summary>
        public static float Volley(TroopDef def, int alive, ZombieType target, float damageMultiplier) =>
            def.Damage * Math.Max(0, alive) * def.CounterAgainst(target) * damageMultiplier;

        /// <summary>Splits a troop count across gates: even share, remainder to the first gates.</summary>
        public static int[] Split(int count, int gates)
        {
            var result = new int[gates];
            for (int g = 0; g < gates; g++) result[g] = count / gates + (g < count % gates ? 1 : 0);
            return result;
        }
    }
}
