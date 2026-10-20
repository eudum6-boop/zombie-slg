using System.Collections.Generic;
using UnityEngine;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Top-level day/night state machine on a single scene (GDD: no scene change between day and night).
    /// Day: base management. Dusk: lighting transition. Night: battle. Dawn: back to day with rewards applied.
    /// </summary>
    public sealed class GameFlow
    {
        private enum Phase { Day, Dusk, Night, Dawn }

        private const float TransitionSeconds = 1.2f;
        private const float ProductionFloatInterval = 8f;

        private readonly BattleContext _ctx;
        private readonly BaseService _base;
        private readonly BaseView _view;
        private readonly BattleController _battle;
        private readonly PointerInput _input;
        private readonly List<System.IDisposable> _subs = new List<System.IDisposable>();
        private BaseHud _hud;
        private HeroUi _heroUi;
        private readonly List<GachaResult> _pullResults = new List<GachaResult>(10);

        private Phase _phase = Phase.Day;
        private float _transition;
        private int _selected = -1;
        private float _floatTimer;
        private OfflineProgressReport? _pendingOffline;

        public GameFlow(BattleContext ctx, BaseService baseService, BaseView view, BattleController battle, PointerInput input)
        {
            _ctx = ctx;
            _base = baseService;
            _view = view;
            _battle = battle;
            _input = input;

            var bus = baseService.Bus;
            _subs.Add(bus.Subscribe<OfflineProgressReport>(r => _pendingOffline = r));
            _subs.Add(bus.Subscribe<BuildingUpgradeStarted>(OnUpgradeStarted));
            _subs.Add(bus.Subscribe<BuildingUpgradeCompleted>(OnUpgradeCompleted));
            _subs.Add(bus.Subscribe<SurvivorArrived>(_ => Toast("새 생존자가 기지에 합류했습니다", new Color(0.6f, 0.85f, 1f))));
            _subs.Add(bus.Subscribe<StarvationChanged>(e =>
                Toast(e.Starving ? "식량이 바닥났습니다! 사기가 떨어집니다" : "식량 공급이 회복됐습니다", e.Starving ? UiKit.Bad : UiKit.Good)));

            _battle.ReturnedToBase += OnReturnedToBase;
        }

        /// <summary>Must be set before <see cref="Start"/>; the HUD needs the flow for its callbacks.</summary>
        public void AttachHud(BaseHud hud) => _hud = hud;
        public void AttachHeroUi(HeroUi heroUi) => _heroUi = heroUi;

        public void Start()
        {
            _base.Load();
            EnterDay();
            SkyLighting.DayNight(_ctx.Sun, _ctx.CameraRig.Camera, 0f);
            if (_pendingOffline.HasValue)
            {
                _hud.ShowOffline(_pendingOffline.Value);
                _pendingOffline = null;
            }
        }

        // ------------------------------------------------------------------ UI callbacks

        public void OnNightButton()
        {
            if (_phase != Phase.Day) return;
            _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
            Select(-1);
            _heroUi?.Close();
            _hud.SetVisible(false);
            _phase = Phase.Dusk;
            _transition = 0f;
        }

        public void OnUpgrade(int id)
        {
            var result = _base.TryUpgrade(id);
            if (result != UpgradeBlock.None)
            {
                _ctx.Feedback.Sfx.Play(SfxId.HeroHurt, 0f);
                Toast(result == UpgradeBlock.NotEnoughResources ? "자원이 부족합니다" : "지금은 업그레이드할 수 없습니다", UiKit.Bad);
            }
        }

        public void OnWorkers(int id, int delta)
        {
            if (_base.ChangeWorkers(id, delta)) _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
        }

        public void OnClosePanel() => Select(-1);

        // ---- heroes / recruiting ----

        public void OnHeroesButton()
        {
            if (_phase != Phase.Day) return;
            _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
            Select(-1);
            _heroUi.OpenHeroes(_base.Heroes);
        }

        public void OnRecruitButton()
        {
            if (_phase != Phase.Day) return;
            _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
            Select(-1);
            _heroUi.OpenRecruit(_base.Heroes);
        }

        public void OnRecruit(int count)
        {
            var block = _base.Recruit(count, _pullResults);
            if (block == PullBlock.NotEnoughTickets)
            {
                _ctx.Feedback.Sfx.Play(SfxId.HeroHurt, 0f);
                Toast("모집권이 부족합니다 — 밤을 버티면 별 개수만큼 받습니다", UiKit.Bad);
                return;
            }
            if (block != PullBlock.None) return;

            _heroUi.ShowResults(_pullResults);
            bool legendary = _pullResults.Exists(r => r.Rarity == HeroRarity.Legendary);
            bool epic = _pullResults.Exists(r => r.Rarity == HeroRarity.Epic);
            if (legendary)
            {
                _ctx.Feedback.Sfx.Play(SfxId.Victory, 0f);
                _ctx.Feedback.Shake(0.35f);
            }
            else _ctx.Feedback.Sfx.Play(epic ? SfxId.Victory : SfxId.Click, 0f);
        }

        public void OnPromote(string heroId)
        {
            if (_base.PromoteHero(heroId) != PromoteBlock.None) return;
            _ctx.Feedback.Sfx.Play(SfxId.Victory, 0f);
            Toast($"{_ctx.Data.Hero(heroId).Name} ★{_base.Heroes.Stars(heroId)} 승급!", new Color(1f, 0.85f, 0.4f));
        }

        public void OnAssign(HeroSlot slot, string heroId)
        {
            if (_base.AssignHero(slot, heroId) != AssignBlock.None) return;
            _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
            string name = _ctx.Data.Hero(heroId).Name;
            string where = slot == HeroSlot.Leader ? "밤 출전 영웅" : slot == HeroSlot.Manager ? "기지 관리자" : "정찰대장";
            Toast($"{name} → {where}", new Color(0.6f, 0.85f, 1f));
        }

        private NightSetup BuildNightSetup(BaseState state)
        {
            var heroes = _base.Heroes;
            var leader = heroes.Leader;
            return new NightSetup(state.Night, state.HqLevel, state.LevelOf(BasePlots.Clinic),
                new[] { state.LevelOf(BasePlots.TowerA), state.LevelOf(BasePlots.TowerB) },
                leader.Name, heroes.CombatStats(leader.Id), HeroUi.ParseHex(leader.ColorHex, new Color(0.2f, 0.42f, 0.78f)),
                heroes.ScoutBonus);
        }

        // ------------------------------------------------------------------ frame

        public void Tick(float dt)
        {
            _input.Update();

            // The base economy runs in real time in every phase (including during the night).
            _base.Tick();
            var state = _base.State;
            _view.Sync(state);
            _view.Tick(dt);

            switch (_phase)
            {
                case Phase.Day:
                    if (_heroUi != null && _heroUi.IsOpen)
                    {
                        _heroUi.Refresh(_base.Heroes);
                        _heroUi.Tick(Time.unscaledDeltaTime);
                    }
                    else
                    {
                        HandleDayInput();
                        EmitProductionFloats(dt, state);
                    }
                    _hud.Tick(state, _base.Now);
                    break;

                case Phase.Dusk:
                    _transition += dt;
                    SkyLighting.DayNight(_ctx.Sun, _ctx.CameraRig.Camera, _transition / TransitionSeconds);
                    if (_transition >= TransitionSeconds)
                    {
                        _phase = Phase.Night;
                        _battle.BeginNight(BuildNightSetup(state));
                    }
                    break;

                case Phase.Night:
                    _battle.Tick(dt);
                    break;

                case Phase.Dawn:
                    _transition += dt;
                    SkyLighting.DayNight(_ctx.Sun, _ctx.CameraRig.Camera, 1f - _transition / TransitionSeconds);
                    if (_transition >= TransitionSeconds) EnterDay();
                    _hud.Tick(state, _base.Now);
                    break;
            }

            _ctx.Feedback.Tick();
        }

        private void EnterDay()
        {
            _phase = Phase.Day;
            _ctx.Feedback.ResetTime();
            _ctx.Hud.SetBattleVisible(false);
            var s = _base.State;
            _ctx.Hq.SetLevel(s.HqLevel);
            _ctx.Hq.ResetState(_ctx.Config.HqMaxHp * _ctx.Data.Defense.HqHpMultiplier(s.HqLevel)); // also restores a collapsed HQ
            _ctx.Hero.ResetState();
            _hud.SetVisible(true);
            Select(-1);
        }

        private void OnReturnedToBase(NightOutcome outcome)
        {
            int nightBefore = _base.State.Night;
            _base.ApplyNight(outcome.Victory, outcome.Reward, outcome.Kills);
            _phase = Phase.Dawn;
            _transition = 0f;
            _hud.SetVisible(true);
            Select(-1);
            Toast(outcome.Victory
                    ? $"NIGHT {nightBefore} 생존! 보상을 창고에 넣었습니다"
                    : "본부가 무너졌습니다. 보상 일부만 회수했습니다",
                outcome.Victory ? new Color(1f, 0.85f, 0.4f) : UiKit.Bad);
        }

        // ------------------------------------------------------------------ input

        private void HandleDayInput()
        {
            if (!_input.ReleasedThisFrame || _input.PressStartedOverUi || _input.OverUi) return;
            if (!_ctx.CameraRig.ScreenToGround(_input.Position, out var ground)) return;

            int id = MapLayout.PlotAt(ground);
            if (id == _selected) return;
            Select(id);
            if (id >= 0) _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
        }

        private void Select(int id)
        {
            _selected = id;
            _view.Select(id);
            _hud?.Select(id);
        }

        // ------------------------------------------------------------------ events → feedback

        private void OnUpgradeStarted(BuildingUpgradeStarted e)
        {
            _ctx.Feedback.Sfx.Play(SfxId.TowerShot, 0f);
            _ctx.Feedback.Chunks(MapLayout.PlotCenter(e.BuildingId) + Vector3.up * 0.5f, 10,
                new Color(0.6f, 0.55f, 0.45f), new Color(0.35f, 0.32f, 0.3f), 3f, 0.14f);
            _view.Punch(e.BuildingId);
        }

        private void OnUpgradeCompleted(BuildingUpgradeCompleted e)
        {
            var def = _ctx.Data.Building(BaseView.TypeOf(e.BuildingId));
            _view.Punch(e.BuildingId);
            _ctx.Feedback.Sfx.Play(SfxId.Victory, 0f);
            _ctx.Feedback.Fire(MapLayout.PlotCenter(e.BuildingId), 18, 1.2f, 2.5f, 0.2f);
            Toast(e.NewLevel == 1 ? $"{def.Name} 건설 완료!" : $"{def.Name} Lv.{e.NewLevel} 완료!", new Color(1f, 0.85f, 0.4f));
        }

        private void EmitProductionFloats(float dt, BaseState s)
        {
            _floatTimer += dt;
            if (_floatTimer < ProductionFloatInterval) return;
            _floatTimer = 0f;

            int cap = s.StorageCap;
            foreach (var b in s.Buildings)
            {
                var def = _ctx.Data.Building(b.Type);
                if (!def.IsProducer || b.Level <= 0) continue;
                if (s.GetAmount(def.Produces) >= cap) continue;
                double amount = s.EffectiveRatePerHour(b) * ProductionFloatInterval / 3600.0;
                if (amount < 0.5) continue;
                _hud.Float(_view.LabelAnchor(b.Id) + Vector3.up * 0.4f,
                    $"+{amount:0} {BaseHud.ResourceName(def.Produces)}", BaseHud.ResourceColor(def.Produces));
            }
        }

        private void Toast(string message, Color color) => _hud?.Toast(message, color);
    }
}
