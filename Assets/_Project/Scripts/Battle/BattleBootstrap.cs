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
        private BaseService _baseService;
        private GameFlow _flow;

        private void Awake()
        {
            UnityEngine.Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            WarnIf2DRenderer();

            // All numbers come from the CSV tables (Data/Tables → Resources/GameData.asset).
            var data = LoadGameData();
            if (data == null)
            {
                enabled = false;
                return;
            }
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
            _baseService = new BaseService(new SystemClock(), new LocalSaveRepository(), new EventBus(), data);
            var baseView = new BaseView(ctx);
            _flow = new GameFlow(ctx, _baseService, baseView, battle, input);
            var baseHud = new BaseHud(ctx, baseView, transform,
                onNight: _flow.OnNightButton,
                onUpgrade: _flow.OnUpgrade,
                onWorkers: _flow.OnWorkers,
                onClosePanel: _flow.OnClosePanel);
            _flow.AttachHud(baseHud);
            _flow.Start();
        }

        private void Update()
        {
            _flow?.Tick(Time.deltaTime);
        }

        private static GameData LoadGameData()
        {
            var asset = GameDataAsset.LoadDefault();
            if (asset == null)
            {
                Debug.LogError("[ZombieSLG] GameData가 없습니다. 메뉴 ZombieSLG ▸ 데이터 테이블 가져오기 를 실행하세요.");
                return null;
            }
            var errors = new List<string>();
            var data = asset.Build(errors);
            if (data == null)
            {
                foreach (var e in errors) Debug.LogError("[ZombieSLG][데이터] " + e);
                Debug.LogError("[ZombieSLG] 데이터 테이블에 오류가 있어 시작할 수 없습니다.");
            }
            return data;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) _baseService?.Save();
        }

        private void OnApplicationQuit()
        {
            _baseService?.Save();
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
            _baseService?.Save();
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
