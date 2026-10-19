using System.Collections.Generic;
using UnityEngine;

namespace ZombieSLG.Battle
{
    /// <summary>Shared references for the battle systems. Built once by <see cref="BattleBootstrap"/>.</summary>
    public sealed class BattleContext
    {
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
    /// Inspector-tunable numbers for M0. Zombie stats and wave settings live in the Domain layer;
    /// these are the player-side values that will move to data tables in M2.
    /// </summary>
    [System.Serializable]
    public sealed class BattleConfig
    {
        /// <summary>Bump when defaults change so scenes saved with old values are reset (see BattleBootstrap).</summary>
        public const int CurrentVersion = 3;
        [HideInInspector] public int Version;

        [Header("Headquarters")]
        public float HqMaxHp = 2300f;

        [Header("Hero")]
        public float HeroMaxHp = 300f;
        public float HeroMoveSpeed = 5.5f;
        public float HeroRange = 7.5f;
        public float HeroFireInterval = 0.25f;
        public float HeroDamage = 13f;
        public float HeroCritChance = 0.14f;
        public float HeroCritMultiplier = 2f;
        public float HeroRegenPerSecond = 6.5f;
        public float HeroRespawnSeconds = 6f;

        [Header("Firebomb skill")]
        public float SkillCooldown = 13f;
        public float SkillRadius = 3.5f;
        public float SkillDamage = 120f;
        public float SkillBurnDps = 22f;
        public float SkillBurnDuration = 3f;

        [Header("Machine-gun tower")]
        public float TowerRange = 8f;
        public float TowerFireInterval = 0.18f;
        public float TowerDamage = 6f;
        public float TowerCritChance = 0.05f;

        [Header("Zombies")]
        public float ZombieAggroRange = 2.8f;
        public int ZombiePrewarm = 300;

        [Header("Debug")]
        public int StressTestCount = 200;
        public bool ShowFlowFieldGizmos = false;
    }
}
