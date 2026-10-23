using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>Troops sent out tonight and research multipliers (1 = no bonus).</summary>
    public sealed class NightArmy
    {
        /// <summary>Healthy troops per TroopType; split across the gates.</summary>
        public int[] Troops = new int[TroopRules.TypeCount];
        public float TroopDamage = 1f;
        public float TroopHp = 1f;
        public float TowerDamage = 1f;
        public float HqHp = 1f;
        public float HeroDamage = 1f;
        public float SkillDamage = 1f;
    }

    /// <summary>Everything the base hands over to a night.</summary>
    public readonly struct NightSetup
    {
        public readonly int Night;
        public readonly int HqLevel;
        public readonly int ClinicLevel;
        public readonly int[] TowerLevels;
        public readonly string LeaderName;
        public readonly HeroCombatStats LeaderStats;
        public readonly Color LeaderColor;
        public readonly double ScoutBonus;
        public readonly NightArmy Army;

        public NightSetup(int night, int hqLevel, int clinicLevel, int[] towerLevels,
            string leaderName, HeroCombatStats leaderStats, Color leaderColor, double scoutBonus, NightArmy army)
        {
            Army = army ?? new NightArmy();
            Night = night;
            HqLevel = hqLevel;
            ClinicLevel = clinicLevel;
            TowerLevels = towerLevels;
            LeaderName = leaderName;
            LeaderStats = leaderStats;
            LeaderColor = leaderColor;
            ScoutBonus = scoutBonus;
        }
    }

    public readonly struct NightOutcome
    {
        public readonly bool Victory;
        public readonly NightReward Reward;
        public readonly int Kills;
        public readonly float HqHpRatio;
        /// <summary>Per TroopType. Fallen troops are either dead or wounded (heal them at the clinic).</summary>
        public readonly int[] Dead;
        public readonly int[] Wounded;

        public NightOutcome(bool victory, NightReward reward, int kills, float hqHpRatio, int[] dead, int[] wounded)
        {
            Dead = dead;
            Wounded = wounded;
            Victory = victory;
            Reward = reward;
            Kills = kills;
            HqHpRatio = hqHpRatio;
        }
    }

    /// <summary>
    /// Night-raid state machine: Idle → Playing (3 min) → Result → (return to base).
    /// Owns the frame order of all battle systems so ordering is deterministic
    /// (input → spawn → hero → towers → skill → zombies → UI).
    /// </summary>
    public sealed class BattleController
    {
        private enum Phase { Idle, Playing, Result }

        private readonly BattleContext _ctx;
        private readonly WaveSettings _waveSettings;
        private readonly List<SpawnRequest> _spawns = new List<SpawnRequest>(128);
        private readonly PointerInput _input;

        private WaveDirector _director;
        private Phase _phase = Phase.Idle;
        private int _night = 1;
        private NightOutcome _outcome;
        private float _resultDelay;
        private string _pendingResultBody;
        private int _lastHordeWarned = -1;
        private double _scoutBonus;

        /// <summary>Raised when the player leaves the result screen.</summary>
        public event Action<NightOutcome> ReturnedToBase;

        public bool IsActive => _phase != Phase.Idle;
        public float Remaining => _director?.Remaining ?? _waveSettings.Duration;
        public int Night => _night;

        public BattleController(BattleContext ctx, PointerInput input)
        {
            _ctx = ctx;
            _input = input;
            _waveSettings = ctx.Data.Wave;
        }

        public string HordeTimes()
        {
            var parts = new List<string>();
            foreach (var h in _waveSettings.Hordes) parts.Add(FormatTime(h.Time));
            return string.Join(" / ", parts);
        }

        public void BeginNight(NightSetup setup)
        {
            var ctx = _ctx;
            _night = Mathf.Max(1, setup.Night);

            ctx.Feedback.ResetTime();
            ctx.Zombies.DespawnAll();
            ctx.Zombies.Kills = 0;

            ctx.Hq.SetLevel(setup.HqLevel);
            var defense = ctx.Data.Defense;
            var army = setup.Army;
            ctx.Hq.ResetState(ctx.Config.HqMaxHp * defense.HqHpMultiplier(setup.HqLevel) * army.HqHp);
            ctx.Hero.DamageMultiplier = army.HeroDamage;
            ctx.Hero.SkillMultiplier = army.SkillDamage;
            ctx.Hero.RegenMultiplier = defense.HeroRegenMultiplier(setup.ClinicLevel);
            ctx.Hero.RespawnMultiplier = defense.HeroRespawnMultiplier(setup.ClinicLevel);
            ctx.Hero.ApplyHero(setup.LeaderName, setup.LeaderStats, setup.LeaderColor);
            ctx.Hero.ResetState();
            ctx.Skill.ResetState();
            _scoutBonus = setup.ScoutBonus;
            for (int i = 0; i < ctx.Towers.Count; i++)
            {
                int lv = setup.TowerLevels != null && i < setup.TowerLevels.Length ? setup.TowerLevels[i] : 0;
                ctx.Towers[i].SetLevel(lv);
                ctx.Towers[i].ResearchMultiplier = army.TowerDamage;
                ctx.Towers[i].ResetState();
            }

            DeploySquads(army);

            _director = new WaveDirector(_waveSettings, ctx.Data.Night, _night, _night * 7919 + 17);
            _lastHordeWarned = -1;
            _resultDelay = 0f;
            _phase = Phase.Playing;

            ctx.Hud.HideOverlays();
            ctx.Hud.SetBattleVisible(true);
            ctx.Hud.ShowBanner("NIGHT " + _night + "\n습격 시작!", 1.8f, new Color(1f, 0.55f, 0.15f));
            if (!string.IsNullOrEmpty(setup.LeaderName)) ctx.Hud.SetHint($"출전 영웅: {setup.LeaderName}  ·  땅을 탭해 이동");
            ctx.Feedback.Sfx.Play(SfxId.Siren, 0f);
            SkyLighting.Dawn(ctx.Sun, 0f);
        }

        public void OnSkillButton()
        {
            if (_phase != Phase.Playing) return;
            _ctx.Skill.ToggleTargeting();
            _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
        }

        public void OnStressTest()
        {
            if (_phase != Phase.Playing) return;
            float hp = _ctx.Data.Night.HpMultiplier(_night);
            for (int i = 0; i < _ctx.Config.StressTestCount; i++)
            {
                var side = (SpawnSide)(i % 3);
                _ctx.Zombies.Spawn(ZombieType.Walker, _ctx.Map.RandomSpawnPoint(side, _ctx.Rng), hp);
            }
            _ctx.Hud.ShowBanner("+" + _ctx.Config.StressTestCount + " 스트레스 테스트", 1.2f, new Color(0.6f, 1f, 0.6f));
        }

        public void OnResultButton()
        {
            if (_phase != Phase.Result || _resultDelay > 0f) return;
            _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
            _ctx.Feedback.ResetTime();
            _ctx.Zombies.DespawnAll();
            _ctx.Skill.ResetState();
            _ctx.Hero.ResetState();
            foreach (var sq in _ctx.Squads) sq.Hide();
            _ctx.Hud.HideOverlays();
            _ctx.Hud.SetBattleVisible(false);
            _phase = Phase.Idle;
            ReturnedToBase?.Invoke(_outcome);
        }

        /// <summary>Called every frame while a night is active. Input must already be updated.</summary>
        public void Tick(float dt)
        {
            if (_phase == Phase.Idle) return;
            HandleKeys();

            var ctx = _ctx;
            if (_phase == Phase.Playing)
            {
                HandleWorldInput();

                _spawns.Clear();
                _director.Tick(dt, ctx.Zombies.AliveCount, _spawns);
                float hp = _ctx.Data.Night.HpMultiplier(_night);
                for (int i = 0; i < _spawns.Count; i++)
                {
                    var s = _spawns[i];
                    ctx.Zombies.Spawn(s.Type, ctx.Map.RandomSpawnPoint(s.Side, ctx.Rng), hp);
                }

                ctx.Hero.Tick(dt);
                foreach (var sq in ctx.Squads) sq.Tick(dt);
                foreach (var t in ctx.Towers) t.Tick(dt);
                ctx.Skill.Tick(dt);
                ctx.Zombies.Tick(dt, true);

                WarnHorde();
                SkyLighting.Dawn(ctx.Sun, Mathf.InverseLerp(_director.Duration * 0.88f, _director.Duration, _director.Elapsed));

                if (ctx.Hq.IsDestroyed) EndNight(false);
                else if (_director.IsFinished) EndNight(true);
            }
            else // Result
            {
                ctx.Skill.Tick(dt);
                ctx.Zombies.Tick(dt, false);
                if (_resultDelay > 0f)
                {
                    _resultDelay -= dt;
                    if (_resultDelay <= 0f) ctx.Hud.ShowResult(_outcome.Victory, _pendingResultBody, "기지로 돌아가기");
                }
            }

            ctx.Hud.Tick(_director.Remaining, _night);
        }

        private void HandleKeys()
        {
            if ((PointerInput.KeyDown(Key.Space) || PointerInput.KeyDown(Key.Enter)) && _phase == Phase.Result) OnResultButton();
            if (PointerInput.KeyDown(Key.Q)) OnSkillButton();
            if (PointerInput.KeyDown(Key.T)) OnStressTest();
        }

        private void HandleWorldInput()
        {
            var skill = _ctx.Skill;
            bool onGround = _ctx.CameraRig.ScreenToGround(_input.Position, out var ground);

            if (skill.IsTargeting)
            {
                if (_input.CancelPressed) { skill.CancelTargeting(); return; }
                if (onGround && !_input.OverUi) skill.Aim(ground);
                if (_input.ReleasedThisFrame && !_input.PressStartedOverUi && onGround)
                {
                    skill.Cast(ground);
                    _ctx.Hud.SetHint("");
                }
                return;
            }

            // Tap or drag on the ground steers the hero.
            if (onGround && _input.IsPressed && !_input.PressStartedOverUi && (_input.PressedThisFrame || !_input.OverUi))
            {
                _ctx.Hero.CommandMove(ground);
                if (_input.PressedThisFrame) _ctx.Hud.SetHint("");
            }
        }

        private void WarnHorde()
        {
            if (!_director.TryGetUpcomingHorde(5f, out _, out var side)) return;
            if (_lastHordeWarned == _director.HordesTriggered) return;
            _lastHordeWarned = _director.HordesTriggered;
            _ctx.Hud.ShowBanner($"대규모 습격 접근!\n{BattleHud.SideName(side)}에서 몰려옵니다", 4.5f, new Color(1f, 0.3f, 0.2f));
            _ctx.Feedback.Sfx.Play(SfxId.Siren, 0f);
        }

        private void EndNight(bool victory)
        {
            var ctx = _ctx;
            _phase = Phase.Result;
            ctx.Skill.CancelTargeting();
            ctx.Hud.SetHint("");

            int kills = ctx.Zombies.Kills;
            float hpRatio = ctx.Hq.HpRatio;
            var reward = ctx.Data.Rewards.Calculate(_night, victory, kills, hpRatio, _scoutBonus);
            int stars = ctx.Data.Rewards.Stars(victory, hpRatio);
            var dead = new int[TroopRules.TypeCount];
            var wounded = new int[TroopRules.TypeCount];
            foreach (var sq in ctx.Squads)
                for (int t = 0; t < TroopRules.TypeCount; t++)
                {
                    dead[t] += sq.Dead((TroopType)t);
                    wounded[t] += sq.Wounded((TroopType)t);
                }
            _outcome = new NightOutcome(victory, reward, kills, hpRatio, dead, wounded);

            if (victory)
            {
                ctx.Zombies.KillAll(true);
                SkyLighting.Dawn(ctx.Sun, 1f);
                ctx.Hud.ShowBanner("새벽이 밝았다", 2f, new Color(1f, 0.85f, 0.5f));
                ctx.Feedback.Sfx.Play(SfxId.Victory, 0f);
            }
            else
            {
                ctx.Feedback.Sfx.Play(SfxId.Defeat, 0f);
            }

            string starText = victory ? new string('★', stars) + new string('☆', 3 - stars) + "\n\n" : "";
            _pendingResultBody =
                starText +
                $"처치  {kills}     본부 체력  {Mathf.RoundToInt(hpRatio * 100f)}%\n" +
                $"생존 시간  {FormatTime(_director.Elapsed)}\n\n" +
                "<color=#ffb060>— 보상 —</color>\n" +
                $"식량 +{reward.Food}    연료 +{reward.Fuel}\n" +
                $"자재 +{reward.Materials}    모집권 +{reward.Tickets}" +
                CasualtyLine(dead, wounded) +
                (_scoutBonus > 0 ? $"\n<color=#e0b060>정찰 보너스 +{_scoutBonus * 100:0}%</color>" : "") +
                (victory ? "\n\n<color=#9fd8ff>새 생존자가 기지를 찾아올 수 있습니다</color>" : "\n\n<color=#9aa4b0>같은 밤에 다시 도전합니다</color>");
            _resultDelay = 1.6f;
        }

        private string CasualtyLine(int[] dead, int[] wounded)
        {
            int d = 0, w = 0;
            for (int t = 0; t < TroopRules.TypeCount; t++) { d += dead[t]; w += wounded[t]; }
            if (d + w == 0) return "";
            return $"\n\n<color=#ff9a80>— 병력 피해 —</color>\n전사 {d}    부상 {w} <color=#9aa4b0>(진료소에서 치료)</color>";
        }

        /// <summary>Splits each troop type across the gate squads (north first), applying research multipliers.</summary>
        private void DeploySquads(NightArmy army)
        {
            var squads = _ctx.Squads;
            if (squads.Count == 0) return;
            var perGate = new int[squads.Count][];
            for (int g = 0; g < squads.Count; g++) perGate[g] = new int[TroopRules.TypeCount];
            for (int t = 0; t < TroopRules.TypeCount; t++)
            {
                var split = SquadMath.Split(army.Troops[t], squads.Count);
                for (int g = 0; g < squads.Count; g++) perGate[g][t] = split[g];
            }
            for (int g = 0; g < squads.Count; g++) squads[g].Deploy(perGate[g], army.TroopDamage, army.TroopHp);
        }

        public static string FormatTime(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
