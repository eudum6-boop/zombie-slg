using System.Collections.Generic;
using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>Shared references for the battle systems. Built once by <see cref="BattleBootstrap"/>.</summary>
    public sealed class BattleContext
    {
        public GameData Data;
        public BattleConfig Config;
        public MapLayout Map;
        public MaterialLibrary Materials;
        public CameraRig CameraRig;
        public Feedback Feedback;
        public ZombieManager Zombies;
        public HeroController Hero;
        public HeadquartersView Hq;
        public FirebombSkill Skill;
        public readonly List<TowerController> Towers = new List<TowerController>();
        public BattleHud Hud;
        public Transform WorldRoot;
        public Light Sun;
        public System.Random Rng = new System.Random(1234);

        public double Roll() => Rng.NextDouble();
    }

    /// <summary>
    /// Player-side battle numbers, read from balance.csv (hero.*, tower.*, skill.firebomb.*, hq.*).
    /// Debug values come from the BattleBootstrap inspector.
    /// </summary>
    public sealed class BattleConfig
    {
        public float HqMaxHp;

        public float HeroMaxHp;
        public float HeroMoveSpeed;
        public float HeroRange;
        public float HeroFireInterval;
        public float HeroDamage;
        public float HeroCritChance;
        public float HeroCritMultiplier;
        public float HeroRegenPerSecond;
        public float HeroRespawnSeconds;

        public float SkillCooldown;
        public float SkillRadius;
        public float SkillDamage;
        public float SkillEdgeFactor;
        public float SkillBurnDps;
        public float SkillBurnDuration;

        public float TowerRange;
        public float TowerFireInterval;
        public float TowerDamage;
        public float TowerCritChance;
        public float TowerCritMultiplier;

        public float ZombieAggroRange;

        // Debug (inspector)
        public int ZombiePrewarm = 300;
        public int StressTestCount = 200;

        public static BattleConfig From(BalanceTable b) => new BattleConfig
        {
            HqMaxHp = b.F("hq.max_hp"),

            HeroMaxHp = b.F("hero.max_hp"),
            HeroMoveSpeed = b.F("hero.move_speed"),
            HeroRange = b.F("hero.range"),
            HeroFireInterval = b.F("hero.fire_interval"),
            HeroDamage = b.F("hero.damage"),
            HeroCritChance = b.F("hero.crit_chance"),
            HeroCritMultiplier = b.F("hero.crit_multiplier"),
            HeroRegenPerSecond = b.F("hero.regen_per_second"),
            HeroRespawnSeconds = b.F("hero.respawn_seconds"),

            SkillCooldown = b.F("skill.firebomb.cooldown"),
            SkillRadius = b.F("skill.firebomb.radius"),
            SkillDamage = b.F("skill.firebomb.damage"),
            SkillEdgeFactor = b.F("skill.firebomb.edge_factor"),
            SkillBurnDps = b.F("skill.firebomb.burn_dps"),
            SkillBurnDuration = b.F("skill.firebomb.burn_duration"),

            TowerRange = b.F("tower.range"),
            TowerFireInterval = b.F("tower.fire_interval"),
            TowerDamage = b.F("tower.damage"),
            TowerCritChance = b.F("tower.crit_chance"),
            TowerCritMultiplier = b.F("tower.crit_multiplier"),

            ZombieAggroRange = b.F("zombie.aggro_range"),
        };
    }
}
