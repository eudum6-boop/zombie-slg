using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZombieSLG.Application;
using ZombieSLG.Domain;
using ZombieSLG.Infrastructure;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Composition root. Drop this on an empty GameObject in an empty scene (or use the menu ZombieSLG ▸ 2)
    /// and press Play — it builds the map, camera, lights, units, HUDs and wires the layers together:
    /// Infrastructure (clock, save) → Application (BaseService, EventBus) → Presentation (views, GameFlow).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class BattleBootstrap : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private int _zombiePrewarm = 300;
        [SerializeField] private int _stressTestCount = 200;
        [SerializeField] private bool _showFlowFieldGizmos;

        private BattleContext _ctx;
        /// <summary>Set by the editor menu "진행 데이터 초기화" so the next run doesn't restore the old cloud save.</summary>
        public const string SkipCloudPullKey = "zslg.cloud.skip_pull";

        private BaseService _baseService;
        private GameFlow _flow;
        private BaseHud _baseHud;
        private UgsCloudSave _cloud;
        private CloudSyncService _sync;
        private SyncedClock _clock;
        private RankingService _ranking;
        private ServerRewardService _rewards;
        private LocalSaveRepository _localRepo;
        private readonly UgsServerTime _serverTime = new UgsServerTime();
        private bool _started;

        private void Awake()
        {
            UnityEngine.Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            WarnIf2DRenderer();

            // Services that don't need game data: clock, local save, cloud connection.
            // Game clock: server time once synced, device clock while offline (see SyncedClock).
            var device = new SystemClock();
            _clock = new SyncedClock(() => device.Now, () => MonotonicClock.Seconds);
            _localRepo = new LocalSaveRepository();
            _cloud = new UgsCloudSave();
            _sync = new CloudSyncService(_cloud, _localRepo, _clock, msg => Debug.Log("[ZombieSLG][클라우드] " + msg));
            _ranking = new RankingService(new UgsLeaderboard(_cloud), msg => Debug.LogWarning("[ZombieSLG][순위] " + msg));

            // Plain dark screen while connecting (SetupCameraAndLighting reuses this camera later).
            if (Camera.main == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
                camGo.AddComponent<AudioListener>();
            }
        }

        /// <summary>Builds the world, systems and UI. Runs after the server balance values are known.</summary>
        private void BuildGame(GameData data)
        {
            var config = BattleConfig.From(data.Balance);
            config.ZombiePrewarm = _zombiePrewarm;
            config.StressTestCount = _stressTestCount;

            var ctx = new BattleContext { Data = data, Config = config };
            _ctx = ctx;

            ctx.WorldRoot = new GameObject("World").transform;
            ctx.Materials = new MaterialLibrary();
            ctx.Map = new MapLayout();
            MapBuilder.Build(ctx.Map, ctx.Materials, ctx.WorldRoot);

            SetupCameraAndLighting(ctx);
            EnsureEventSystem();

            ctx.Feedback = new Feedback(ctx.WorldRoot, ctx.Materials, ctx.CameraRig);

            var hqGo = new GameObject("Headquarters");
            hqGo.transform.SetParent(ctx.WorldRoot, false);
            hqGo.transform.position = ctx.Map.HqBounds.center - new Vector3(0f, ctx.Map.HqBounds.center.y, 0f);
            ctx.Hq = hqGo.AddComponent<HeadquartersView>();
            ctx.Hq.Init(ctx);

            ctx.Zombies = new ZombieManager(ctx);
            int pre = Mathf.Max(0, config.ZombiePrewarm);
            ctx.Zombies.Prewarm(pre, pre / 3, pre / 10);

            var heroGo = new GameObject("Hero");
            heroGo.transform.SetParent(ctx.WorldRoot, false);
            ctx.Hero = heroGo.AddComponent<HeroController>();
            ctx.Hero.Init(ctx, new Vector3(0f, 0f, 4.5f));
            ctx.Defenders.Add(ctx.Hero);

            // Gate squads, just inside each gate (order matches SquadMath.Split: north gets the remainder first).
            foreach (var (name, post, facing) in MapLayout.SquadPosts)
            {
                var squad = new SquadController(ctx, name, post, facing);
                ctx.Squads.Add(squad);
                ctx.Defenders.Add(squad);
            }

            foreach (var pos in new[] { MapLayout.PlotCenter(BasePlots.TowerA), MapLayout.PlotCenter(BasePlots.TowerB) })
            {
                var towerGo = new GameObject("MachineGunTower");
                towerGo.transform.SetParent(ctx.WorldRoot, false);
                towerGo.transform.position = pos;
                var tower = towerGo.AddComponent<TowerController>();
                tower.Init(ctx);
                ctx.Towers.Add(tower);
            }

            ctx.Skill = new FirebombSkill(ctx);

            var input = new PointerInput();
            var battle = new BattleController(ctx, input);
            ctx.Hud = new BattleHud(ctx, transform,
                onStart: null,
                onSkill: battle.OnSkillButton,
                onStress: battle.OnStressTest,
                onResultButton: battle.OnResultButton);

            // Layers: infrastructure → application → presentation.
            _baseService = new BaseService(_clock, _localRepo, new EventBus(), data);
            _baseService.Saved += _sync.MarkDirty;
            _rewards = new ServerRewardService(new UgsServerRewards(_cloud), _baseService,
                msg => Debug.LogWarning("[ZombieSLG][서버 보상] " + msg));
            var baseView = new BaseView(ctx);
            _flow = new GameFlow(ctx, _baseService, baseView, battle, input);
            var baseHud = new BaseHud(ctx, baseView, transform,
                onNight: _flow.OnNightButton,
                onUpgrade: _flow.OnUpgrade,
                onWorkers: _flow.OnWorkers,
                onClosePanel: _flow.OnClosePanel,
                onHeroes: _flow.OnHeroesButton,
                onRecruit: _flow.OnRecruitButton,
                onAction: _flow.OnBuildingAction,
                onRanking: _flow.OnRankingButton);
            _flow.AttachHud(baseHud);
            _flow.AttachHeroUi(new HeroUi(ctx, baseHud.Root,
                onPromote: _flow.OnPromote,
                onAssign: _flow.OnAssign,
                onRecruit: _flow.OnRecruit));
            _flow.AttachArmyUi(new ArmyUi(ctx, baseHud.Root, onTrain: _flow.OnTrain, onHeal: _flow.OnHeal));
            _flow.AttachResearchUi(new ResearchUi(ctx, baseHud.Root, onStart: _flow.OnResearch));
            _flow.AttachRanking(_ranking, new RankingUi(baseHud.Root));
            _flow.AttachServerRewards(_rewards);
            _baseHud = baseHud;
            baseHud.SetVisible(false); // shown by GameFlow once the save is loaded
        }

        /// <summary>
        /// Server first (sign in, pull the newer save), then the game. Offline or slow network falls back
        /// to the local save after the timeout, so the game always starts.
        /// </summary>
        private async void Start()
        {
            var asset = GameDataAsset.LoadDefault();
            if (asset == null)
            {
                Debug.LogError("[ZombieSLG] GameData가 없습니다. 메뉴 ZombieSLG ▸ 데이터 테이블 가져오기 를 실행하세요.");
                enabled = false;
                return;
            }
            bool skipPull = PlayerPrefs.GetInt(SkipCloudPullKey, 0) == 1;

            bool online = await _cloud.ConnectAsync();
            bool serverTime = online && await _clock.TrySyncAsync(_serverTime, 8000);
            if (online && !serverTime) // first Cloud Code call can be slow right after sign-in; try once more
                serverTime = await _clock.TrySyncAsync(_serverTime, 8000);
            if (this == null) return;

            // Balance: shipped CSV values, patched by Remote Config (or its cached copy when offline).
            string overrides = UgsRemoteConfig.Cached;
            bool freshConfig = false;
            if (online) (overrides, freshConfig) = await UgsRemoteConfig.FetchBalanceAsync();
            if (this == null) return;
            var data = LoadGameData(asset, overrides, out int applied);
            if (data == null)
            {
                enabled = false;
                return;
            }
            if (applied > 0) Debug.Log($"[ZombieSLG] 서버 밸런스 값 {applied}개 적용{(freshConfig ? "" : " (저장된 값)")}");
            BuildGame(data);

            if (online && !serverTime)
                Debug.LogWarning($"[ZombieSLG] 서버 시간을 받지 못했습니다 ({_clock.LastSyncError}) — 기기 시계를 사용합니다.");
            else if (serverTime)
                Debug.Log($"[ZombieSLG] 서버 시간 사용 (기기 시계 차이 {_clock.DeviceOffset:+0;-0}초)");

            var source = SyncSource.Offline;
            if (online && !skipPull) source = await _sync.PullAsync();
            else if (online) source = SyncSource.Local;
            if (this == null) return; // play mode stopped while waiting

            if (skipPull)
            {
                PlayerPrefs.DeleteKey(SkipCloudPullKey);
                PlayerPrefs.Save();
            }

            bool tampered = serverTime && ClampFutureSave();

            _flow.Start();
            _started = true;

            switch (source)
            {
                case SyncSource.Cloud: _flow.Notify("클라우드 세이브를 불러왔습니다", new Color(0.5f, 0.85f, 1f)); break;
                case SyncSource.Local: _flow.Notify("클라우드 저장 연결됨", new Color(0.5f, 0.85f, 1f)); break;
                default: _flow.Notify("오프라인 모드 — 기기에만 저장됩니다", new Color(1f, 0.8f, 0.4f)); break;
            }
            if (tampered) _flow.Notify("기기 시간이 서버와 달라 서버 시간 기준으로 맞췄습니다", new Color(1f, 0.6f, 0.4f));
            else if (online && !serverTime) _flow.Notify("서버 시간을 받지 못해 기기 시간을 사용합니다", new Color(1f, 0.8f, 0.4f));
            if (online) _ = _sync.PushNowAsync(); // upload the freshly loaded/migrated save
            if (online) _ = _ranking.ReportAsync(_baseService.State.Data.BestNight); // scores earned offline

            // Server-authoritative tickets/gacha (Cloud Code module "ZombieServer").
            if (online)
            {
                var ledger = await _rewards.SyncAsync();
                if (this == null) return;
                if (ledger != null)
                    Debug.Log($"[ZombieSLG] 서버 보상 모듈 연결 (모집권 {ledger.Tickets}, 서버 NIGHT {ledger.Night}{(ledger.Imported ? ", 첫 연결 — 기존 진행 가져옴" : "")})");
                else
                    _flow.Notify("서버 보상 모듈에 연결하지 못했습니다 — 모집을 할 수 없습니다", new Color(1f, 0.8f, 0.4f));
            }
        }

        /// <summary>
        /// A save ticked in the future means the device clock was pushed forward while offline.
        /// Pull it back to server time so no more progress accrues for the fake hours.
        /// </summary>
        private bool ClampFutureSave()
        {
            var save = _localRepo.Load();
            double now = _clock.Now;
            if (save == null || !SyncedClock.LooksTampered(save.LastTick, now)) return false;
            Debug.LogWarning($"[ZombieSLG] 세이브 시각이 서버보다 {save.LastTick - now:0}초 앞서 있어 서버 시간으로 맞춥니다.");
            save.LastTick = now;
            _localRepo.Save(save);
            return true;
        }

        /// <summary>The monotonic timer may stop while the app is suspended, so re-read server time on resume.</summary>
        private async void ResyncAfterResume()
        {
            if (!_cloud.IsAvailable) return;
            bool ok = await _clock.TrySyncAsync(_serverTime);
            if (!ok) _clock.Unsync(); // device clock, never backwards
        }

        private void Update()
        {
            // _flow is null after a script reload during Play (Unity keeps the bool, drops the objects).
            if (!_started || _flow == null) return;
            _flow.Tick(Time.deltaTime);
            _sync.Tick();
        }

        private static GameData LoadGameData(GameDataAsset asset, string overridesJson, out int applied)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var data = RemoteBalance.Build(asset.Tables, overridesJson, errors, warnings, out applied);
            foreach (var w in warnings) Debug.LogWarning("[ZombieSLG][서버 밸런스] " + w);
            if (data == null)
            {
                foreach (var e in errors) Debug.LogError("[ZombieSLG][데이터] " + e);
                Debug.LogError("[ZombieSLG] 데이터 테이블에 오류가 있어 시작할 수 없습니다.");
            }
            return data;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_started) return;
            if (!paused)
            {
                ResyncAfterResume();
                return;
            }
            _baseService.Save();
            _ = _sync.PushNowAsync(); // mobile: the app may be killed while in the background
        }

        private void OnApplicationQuit()
        {
            if (!_started) return;
            _baseService.Save();
            _ = _sync.PushNowAsync(); // best effort; the next launch uploads anyway
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
            if (_started) _baseService.Save();
        }

        private static void SetupCameraAndLighting(BattleContext ctx)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var rig = cam.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Distance = 40f;
            rig.Init(cam, MapLayout.Center, (MapLayout.MaxX - MapLayout.MinX) * 0.5f, (MapLayout.MaxZ - MapLayout.MinZ) * 0.5f);
            ctx.CameraRig = rig;

            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;

            var sunGo = new GameObject("Moonlight");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sunGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            ctx.Sun = sun;

            // Post-processing ties the placeholder primitives into one look (GDD: style unity over fidelity).
            var volGo = new GameObject("PostProcessing");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.33f);
            vignette.smoothness.Override(0.5f);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(-12f);
            color.contrast.Override(14f);
            color.postExposure.Override(0.15f);
            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);
            volume.sharedProfile = profile;
        }

        private static void WarnIf2DRenderer()
        {
            // Renderer2D lives in a separate URP 2D assembly, so compare by type name instead of referencing it.
            try
            {
                if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.scriptableRenderer?.GetType().Name == "Renderer2D")
                {
                    Debug.LogWarning("[ZombieSLG] 현재 URP 렌더러가 2D Renderer입니다. 3D 조명/그림자가 보이지 않습니다. " +
                                     "메뉴 ZombieSLG ▸ 1. 3D 렌더러(URP Universal) 설정 을 먼저 실행하세요.");
                }
            }
            catch (System.Exception)
            {
                // Diagnostic only.
            }
        }

        private static void EnsureEventSystem()
        {
            var existing = FindAnyObjectByType<EventSystem>();
            if (existing != null)
            {
                if (existing.GetComponent<InputSystemUIInputModule>() == null)
                {
                    var legacy = existing.GetComponent<BaseInputModule>();
                    if (legacy != null) Destroy(legacy);
                    existing.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
                }
                return;
            }
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private void OnDrawGizmos()
        {
            if (_ctx == null || _ctx.Map == null || !_showFlowFieldGizmos) return;
            var field = _ctx.Map.Field;
            for (int y = 0; y < field.Height; y++)
            {
                for (int x = 0; x < field.Width; x++)
                {
                    Vector3 c = MapLayout.CellCenter(x, y) + Vector3.up * 0.05f;
                    if (field.IsBlocked(x, y))
                    {
                        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
                        Gizmos.DrawCube(c, new Vector3(0.9f, 0.05f, 0.9f));
                        continue;
                    }
                    field.GetDirection(x, y, out int dx, out int dy);
                    Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.8f);
                    Vector3 d = new Vector3(dx, 0f, dy).normalized * 0.4f;
                    Gizmos.DrawLine(c - d, c + d);
                    Gizmos.DrawSphere(c + d, 0.06f);
                }
            }
        }
    }
}
