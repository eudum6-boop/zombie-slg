using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    public enum ResearchBranch
    {
        Economy = 0,
        Military = 1,
        Defense = 2,
    }

    public enum ResearchEffect
    {
        ProductionFood,
        ProductionFuel,
        ProductionMaterials,
        Storage,
        BuildSpeed,
        TroopDamage,
        TroopHp,
        TrainSpeed,
        HealCost,
        TowerDamage,
        HqHp,
        HeroDamage,
        SkillDamage,
    }

    public sealed class ResearchDef
    {
        public string Id;
        public string Name;
        public string Description;
        public ResearchBranch Branch;
        public int Tier;
        public string Requires;
        public int MaxLevel;
        public Cost BaseCost;
        public double CostGrowth;
        public double BaseSeconds;
        public double TimeGrowth;
        public ResearchEffect Effect;
        public double PerLevel;
        public int Order;

        public Cost CostFor(int targetLevel) => BaseCost.Scale(Math.Pow(CostGrowth, Math.Max(0, targetLevel - 1)));
        public double SecondsFor(int targetLevel) => Math.Round(BaseSeconds * Math.Pow(TimeGrowth, Math.Max(0, targetLevel - 1)));

        /// <summary>True for effects that reduce time/cost (capped by research.max_reduction).</summary>
        public static bool IsReduction(ResearchEffect e) =>
            e == ResearchEffect.BuildSpeed || e == ResearchEffect.TrainSpeed || e == ResearchEffect.HealCost;

        public static ResearchEffect? ParseEffect(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string compact = raw.Replace("_", "");
            return Enum.TryParse(compact, true, out ResearchEffect e) && Enum.IsDefined(typeof(ResearchEffect), e) ? e : (ResearchEffect?)null;
        }
    }

    public sealed class ResearchRules
    {
        public double MaxReduction;
    }

    public static class ResearchMath
    {
        public static int Level(GameSave s, string id)
        {
            if (s.Research == null) return 0;
            foreach (var r in s.Research) if (r.Id == id) return r.Level;
            return 0;
        }

        /// <summary>Summed effect of all completed research levels. Reductions are capped.</summary>
        public static double Effect(GameSave s, GameData data, ResearchEffect effect)
        {
            double total = 0;
            if (s.Research != null)
                foreach (var r in s.Research)
                {
                    if (r.Level <= 0 || !data.HasResearch(r.Id)) continue;
                    var def = data.ResearchNode(r.Id);
                    if (def.Effect == effect) total += def.PerLevel * r.Level;
                }
            return ResearchDef.IsReduction(effect) ? Math.Min(total, data.Research.MaxReduction) : total;
        }

        public static double ProductionBonus(GameSave s, GameData data, ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Fuel: return Effect(s, data, ResearchEffect.ProductionFuel);
                case ResourceType.Materials: return Effect(s, data, ResearchEffect.ProductionMaterials);
                default: return Effect(s, data, ResearchEffect.ProductionFood);
            }
        }

        public static IEnumerable<ResearchEffect> All
        {
            get
            {
                foreach (ResearchEffect e in Enum.GetValues(typeof(ResearchEffect))) yield return e;
            }
        }
    }
}
