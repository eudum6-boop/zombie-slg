using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Everything the battle needs from the horde. Two implementations share it so they can be profiled
    /// side by side (BattleBootstrap ▸ Zombie Mode):
    /// <list type="bullet">
    /// <item><see cref="ZombieManager"/> — M0 baseline: one GameObject per zombie, main-thread steering.</item>
    /// <item><see cref="ZombieSystemJobs"/> — M4: Burst jobs for steering/separation, GPU-instanced drawing, no GameObjects.</item>
    /// </list>
    /// </summary>
    public interface IZombieSystem
    {
        /// <summary>Short label for the debug overlay.</summary>
        string Mode { get; }
        int AliveCount { get; }
        int ActiveCount { get; }
        int Kills { get; set; }
        /// <summary>Main-thread milliseconds spent in the last <see cref="Tick"/> (simulation + draw submission).</summary>
        float LastTickMs { get; }

        event Action<ZombieAgent> Killed;

        void Prewarm(int walkers, int runners, int brutes);
        bool Spawn(ZombieType type, Vector3 pos, float hpMultiplier);
        void Tick(float dt, bool simulate);
        ZombieAgent FindNearest(Vector3 p, float range);
        void QueryRadius(Vector3 p, float radius, List<ZombieAgent> results);
        void ApplyDamage(ZombieAgent z, float damage, bool crit, Vector3 knockDir, float knockForce, DamageStyle style);
        void KillAll(bool withEffects);
        void DespawnAll();
    }
}
