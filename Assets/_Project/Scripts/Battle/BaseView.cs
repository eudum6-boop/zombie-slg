using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// 3D presentation of the base: a pad per plot, a placeholder model per building type that grows with
    /// level, scaffolding while under construction, and the selection ring. Reads the domain state; never writes it.
    /// </summary>
    public sealed class BaseView
    {
        private sealed class PlotView
        {
            public int Id;
            public BuildingType Type;
            public Transform Root;
            public Transform Model;
            public Transform Empty;
            public Transform Scaffold;
            public int ShownLevel = -1;
            public bool ShownUpgrading;
            public float Punch;
            public float Height = 1.5f;
        }

        private readonly BattleContext _ctx;
        private readonly MaterialLibrary _m;
        private readonly Transform _root;
        private readonly PlotView[] _plots;
        private readonly Transform _selection;
        private readonly Transform[] _selectionBars = new Transform[4];
        private int _selected = -1;
        private float _time;

        public BaseView(BattleContext ctx)
        {
            _ctx = ctx;
            _m = ctx.Materials;
            _root = new GameObject("Base").transform;
            _root.SetParent(ctx.WorldRoot, false);

            _plots = new PlotView[MapLayout.Plots.Length];
            var pad = _m.Lit(new Color(0.36f, 0.35f, 0.32f));
            for (int id = 0; id < _plots.Length; id++)
            {
                var type = TypeOf(id);
                var root = new GameObject("Plot_" + id + "_" + type).transform;
                root.SetParent(_root, false);
                root.position = MapLayout.PlotCenter(id);
                var size = MapLayout.PlotSize(id);

                if (id != BasePlots.Headquarters)
                    _m.Primitive(PrimitiveType.Cube, "Pad", root, new Vector3(0f, 0.02f, 0f), new Vector3(size.x * 0.96f, 0.04f, size.y * 0.96f), pad);

                var pv = new PlotView { Id = id, Type = type, Root = root };
                pv.Empty = BuildEmptyMarker(root, size);
                pv.Scaffold = BuildScaffold(root, size, id == BasePlots.Headquarters ? 3.8f : 2.4f);
                pv.Empty.gameObject.SetActive(false);
                pv.Scaffold.gameObject.SetActive(false);
                _plots[id] = pv;
            }

            // Selection: glowing rectangle outline around the plot.
            _selection = new GameObject("Selection").transform;
            _selection.SetParent(_root, false);
            var glow = _m.Unlit(new Color(0.5f, 1.4f, 2.0f));
            for (int i = 0; i < 4; i++)
            {
                _selectionBars[i] = _m.Primitive(PrimitiveType.Cube, "Edge", _selection, Vector3.zero, Vector3.one, glow).transform;
                NoShadow(_selectionBars[i]);
            }
            _selection.gameObject.SetActive(false);
        }

        public static BuildingType TypeOf(int id)
        {
            foreach (var d in BasePlots.Defaults) if (d.id == id) return d.type;
            return BuildingType.Headquarters;
        }

        /// <summary>World point above a plot, for HUD labels.</summary>
        public Vector3 LabelAnchor(int id) => _plots[id].Root.position + new Vector3(0f, _plots[id].Height + 0.6f, 0f);

        public void Select(int id)
        {
            _selected = id;
            if (id < 0) { _selection.gameObject.SetActive(false); return; }
            var size = MapLayout.PlotSize(id) + new Vector2(0.5f, 0.5f);
            float hx = size.x * 0.5f, hz = size.y * 0.5f, w = 0.12f;
            _selectionBars[0].localPosition = new Vector3(0f, 0.06f, -hz); _selectionBars[0].localScale = new Vector3(size.x + w, 0.05f, w);
            _selectionBars[1].localPosition = new Vector3(0f, 0.06f, hz);  _selectionBars[1].localScale = new Vector3(size.x + w, 0.05f, w);
            _selectionBars[2].localPosition = new Vector3(-hx, 0.06f, 0f); _selectionBars[2].localScale = new Vector3(w, 0.05f, size.y + w);
            _selectionBars[3].localPosition = new Vector3(hx, 0.06f, 0f);  _selectionBars[3].localScale = new Vector3(w, 0.05f, size.y + w);
            _selection.position = _plots[id].Root.position;
            _selection.localScale = Vector3.one;
            _selection.gameObject.SetActive(true);
        }

        public void Punch(int id)
        {
            if (id < 0 || id >= _plots.Length) return;
            _plots[id].Punch = 1f;
        }

        /// <summary>Reconciles visuals with the domain state. Cheap when nothing changed.</summary>
        public void Sync(BaseState state)
        {
            for (int id = 0; id < _plots.Length; id++)
            {
                var b = state.Get(id);
                if (b == null) continue;
                var pv = _plots[id];

                if (pv.ShownLevel != b.Level)
                {
                    pv.ShownLevel = b.Level;
                    RebuildModel(pv, b.Level);
                }
                if (pv.ShownUpgrading != b.Upgrading)
                {
                    pv.ShownUpgrading = b.Upgrading;
                    pv.Scaffold.gameObject.SetActive(b.Upgrading);
                }
            }

            _ctx.Hq.SetLevel(state.HqLevel);
            for (int i = 0; i < _ctx.Towers.Count; i++)
            {
                int id = i == 0 ? BasePlots.TowerA : BasePlots.TowerB;
                int lv = state.LevelOf(id);
                if (_ctx.Towers[i].Level != lv) _ctx.Towers[i].SetLevel(lv);
            }
        }

        public void Tick(float dt)
        {
            _time += dt;
            foreach (var pv in _plots)
            {
                if (pv.Punch > 0f)
                {
                    pv.Punch = Mathf.Max(0f, pv.Punch - dt * 2.5f);
                    float k = Mathf.Sin(pv.Punch * Mathf.PI * 3f) * pv.Punch * 0.25f;
                    pv.Root.localScale = new Vector3(1f - k * 0.5f, 1f + k, 1f - k * 0.5f);
                }
                if (pv.Empty.gameObject.activeSelf)
                {
                    var plus = pv.Empty.Find("Plus");
                    if (plus != null)
                    {
                        plus.localPosition = new Vector3(0f, 0.9f + Mathf.Sin(_time * 2.5f + pv.Id) * 0.15f, 0f);
                        plus.localRotation = Quaternion.Euler(0f, _time * 60f, 0f);
                    }
                }
                if (pv.Scaffold.gameObject.activeSelf)
                {
                    var crane = pv.Scaffold.Find("Beam");
                    if (crane != null) crane.localRotation = Quaternion.Euler(0f, Mathf.Sin(_time * 1.3f + pv.Id) * 35f, 0f);
                }
            }

            if (_selected >= 0)
            {
                float pulse = 1f + Mathf.Sin(_time * 5f) * 0.025f;
                _selection.localScale = new Vector3(pulse, 1f, pulse);
            }
        }

        // ------------------------------------------------------------------ models

        private void RebuildModel(PlotView pv, int level)
        {
            if (pv.Model != null) Object.Destroy(pv.Model.gameObject);
            pv.Model = null;

            bool empty = level <= 0;
            pv.Empty.gameObject.SetActive(empty);
            pv.Height = empty ? 1.2f : 1.5f;

            // HQ and towers have their own battle objects; only add level trim here.
            if (pv.Type == BuildingType.Headquarters) { pv.Height = 3.2f + 0.25f * (level - 1); return; }
            if (pv.Type == BuildingType.GunTower) { pv.Height = 2.2f; return; }
            if (empty) return;

            var model = new GameObject("Model").transform;
            model.SetParent(pv.Root, false);
            pv.Model = model;
            float s = 1f + 0.12f * (level - 1);
            var size = MapLayout.PlotSize(pv.Id);

            switch (pv.Type)
            {
                case BuildingType.Farm: pv.Height = BuildFarm(model, size, s, level); break;
                case BuildingType.FuelPump: pv.Height = BuildFuelPump(model, s); break;
                case BuildingType.Workshop: pv.Height = BuildWorkshop(model, s); break;
                case BuildingType.Shelter: pv.Height = BuildShelter(model, size, s); break;
                case BuildingType.Clinic: pv.Height = BuildClinic(model, s); break;
                case BuildingType.Barracks: pv.Height = BuildBarracks(model, size, s); break;
                case BuildingType.ResearchLab: pv.Height = BuildLab(model, size, s); break;
            }

            // Level pips: one small light per level on the pad edge.
            var pip = _m.Unlit(new Color(1.6f, 1.1f, 0.3f));
            for (int i = 0; i < Mathf.Min(level, 10); i++)
            {
                float x = -size.x * 0.42f + i * 0.22f;
                var p = _m.Primitive(PrimitiveType.Cube, "Pip", model, new Vector3(x, 0.07f, -size.y * 0.44f), new Vector3(0.12f, 0.06f, 0.12f), pip);
                NoShadow(p.transform);
            }
        }

        private float BuildFarm(Transform t, Vector2 size, float s, int level)
        {
            _m.Primitive(PrimitiveType.Cube, "Soil", t, new Vector3(0f, 0.08f, 0f), new Vector3(size.x * 0.85f, 0.1f, size.y * 0.85f), _m.Lit(new Color(0.33f, 0.24f, 0.16f)));
            var crop = _m.Lit(new Color(0.35f, 0.62f, 0.25f));
            int rows = 3 + Mathf.Min(level / 3, 2);
            for (int r = 0; r < rows; r++)
            {
                float z = -size.y * 0.32f + r * (size.y * 0.64f / Mathf.Max(1, rows - 1));
                _m.Primitive(PrimitiveType.Cube, "Crop", t, new Vector3(-0.3f, 0.25f * s, z), new Vector3(size.x * 0.55f, 0.3f * s, 0.25f), crop);
            }
            _m.Primitive(PrimitiveType.Cube, "Shed", t, new Vector3(size.x * 0.3f, 0.5f * s, size.y * 0.25f), new Vector3(0.7f, 1f * s, 0.8f), _m.Lit(new Color(0.55f, 0.35f, 0.22f)));
            return 1.2f * s;
        }

        private float BuildFuelPump(Transform t, float s)
        {
            var rust = _m.Lit(new Color(0.62f, 0.36f, 0.18f));
            var steel = _m.Lit(new Color(0.42f, 0.44f, 0.46f));
            _m.Primitive(PrimitiveType.Cylinder, "Tank", t, new Vector3(-0.35f, 0.75f * s, 0.1f), new Vector3(1.5f, 0.75f * s, 1.5f), rust);
            _m.Primitive(PrimitiveType.Cylinder, "TankCap", t, new Vector3(-0.35f, 1.52f * s, 0.1f), new Vector3(1.1f, 0.05f, 1.1f), steel);
            _m.Primitive(PrimitiveType.Cube, "Pump", t, new Vector3(0.8f, 0.6f * s, -0.6f), new Vector3(0.6f, 1.2f * s, 0.6f), steel);
            _m.Primitive(PrimitiveType.Cube, "Pipe", t, new Vector3(0.3f, 0.35f, -0.35f), new Vector3(1.0f, 0.15f, 0.15f), steel);
            var lamp = _m.Primitive(PrimitiveType.Cube, "Lamp", t, new Vector3(0.8f, 1.25f * s, -0.6f), new Vector3(0.18f, 0.18f, 0.18f), _m.Unlit(new Color(2.5f, 0.6f, 0.2f)));
            NoShadow(lamp.transform);
            return 1.6f * s;
        }

        private float BuildWorkshop(Transform t, float s)
        {
            var wall = _m.Lit(new Color(0.38f, 0.42f, 0.48f));
            var roof = _m.Lit(new Color(0.25f, 0.27f, 0.30f));
            _m.Primitive(PrimitiveType.Cube, "Hall", t, new Vector3(0f, 0.7f * s, 0.1f), new Vector3(2.4f, 1.4f * s, 2.0f), wall);
            _m.Primitive(PrimitiveType.Cube, "Roof", t, new Vector3(0f, 1.45f * s, 0.1f), new Vector3(2.6f, 0.12f, 2.2f), roof);
            _m.Primitive(PrimitiveType.Cylinder, "Chimney", t, new Vector3(0.75f, 1.9f * s, 0.6f), new Vector3(0.3f, 0.5f * s, 0.3f), roof);
            _m.Primitive(PrimitiveType.Cube, "Door", t, new Vector3(0f, 0.45f, -0.92f), new Vector3(0.9f, 0.9f, 0.05f), _m.Lit(new Color(0.75f, 0.55f, 0.15f)));
            return 2.2f * s;
        }

        private float BuildShelter(Transform t, Vector2 size, float s)
        {
            var canvas = _m.Lit(new Color(0.40f, 0.44f, 0.30f));
            _m.Primitive(PrimitiveType.Cube, "Body", t, new Vector3(0f, 0.45f * s, 0f), new Vector3(size.x * 0.85f, 0.9f * s, size.y * 0.7f), canvas);
            var roof = _m.Primitive(PrimitiveType.Cube, "Roof", t, new Vector3(0f, 0.95f * s, 0f), new Vector3(size.x * 0.88f, 0.55f, 0.55f), _m.Lit(new Color(0.32f, 0.35f, 0.24f)));
            roof.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            var window = _m.Emissive(new Color(0.9f, 0.8f, 0.5f), new Color(1.6f, 1.1f, 0.4f));
            for (int i = -1; i <= 1; i++)
                _m.Primitive(PrimitiveType.Cube, "Window", t, new Vector3(i * 1.0f, 0.5f * s, -size.y * 0.36f), new Vector3(0.35f, 0.25f, 0.04f), window);
            return 1.4f * s;
        }

        private float BuildClinic(Transform t, float s)
        {
            var white = _m.Lit(new Color(0.85f, 0.86f, 0.84f));
            var red = _m.Lit(new Color(0.8f, 0.15f, 0.15f));
            _m.Primitive(PrimitiveType.Cube, "Body", t, new Vector3(0f, 0.65f * s, 0f), new Vector3(2.2f, 1.3f * s, 2.2f), white);
            _m.Primitive(PrimitiveType.Cube, "CrossH", t, new Vector3(0f, 1.33f * s, 0f), new Vector3(1.1f, 0.06f, 0.35f), red);
            _m.Primitive(PrimitiveType.Cube, "CrossV", t, new Vector3(0f, 1.33f * s, 0f), new Vector3(0.35f, 0.06f, 1.1f), red);
            return 1.6f * s;
        }

        private float BuildBarracks(Transform t, Vector2 size, float s)
        {
            var olive = _m.Lit(new Color(0.33f, 0.38f, 0.26f));
            var sand = _m.Lit(new Color(0.62f, 0.55f, 0.40f));
            _m.Primitive(PrimitiveType.Cube, "Hut", t, new Vector3(-0.3f, 0.5f * s, 0.15f), new Vector3(size.x * 0.6f, 1.0f * s, size.y * 0.6f), olive);
            var roof = _m.Primitive(PrimitiveType.Cylinder, "Roof", t, new Vector3(-0.3f, 1.0f * s, 0.15f), new Vector3(size.y * 0.6f, size.x * 0.3f, size.y * 0.6f), olive);
            roof.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            _m.Primitive(PrimitiveType.Cube, "Sandbags", t, new Vector3(0f, 0.18f, -size.y * 0.38f), new Vector3(size.x * 0.85f, 0.36f, 0.3f), sand);
            _m.Primitive(PrimitiveType.Cylinder, "Flagpole", t, new Vector3(size.x * 0.38f, 0.9f * s, 0.3f), new Vector3(0.06f, 0.9f * s, 0.06f), _m.Lit(new Color(0.3f, 0.3f, 0.3f)));
            var flag = _m.Primitive(PrimitiveType.Cube, "Flag", t, new Vector3(size.x * 0.38f + 0.25f, 1.6f * s, 0.3f), new Vector3(0.5f, 0.3f, 0.03f), _m.Lit(new Color(0.75f, 0.3f, 0.12f)));
            NoShadow(flag.transform);
            return 1.9f * s;
        }

        private float BuildLab(Transform t, Vector2 size, float s)
        {
            var wall = _m.Lit(new Color(0.78f, 0.80f, 0.82f));
            var dark = _m.Lit(new Color(0.22f, 0.24f, 0.28f));
            _m.Primitive(PrimitiveType.Cube, "Body", t, new Vector3(0f, 0.55f * s, 0.1f), new Vector3(size.x * 0.8f, 1.1f * s, size.y * 0.7f), wall);
            _m.Primitive(PrimitiveType.Sphere, "Dome", t, new Vector3(-0.4f, 1.1f * s, 0.1f), new Vector3(1.0f, 0.8f, 1.0f) * s, wall);
            _m.Primitive(PrimitiveType.Cylinder, "Mast", t, new Vector3(0.8f, 1.5f * s, 0.2f), new Vector3(0.06f, 0.45f * s, 0.06f), dark);
            var light = _m.Primitive(PrimitiveType.Sphere, "Beacon", t, new Vector3(0.8f, 1.98f * s, 0.2f), Vector3.one * 0.16f, _m.Unlit(new Color(0.4f, 1.6f, 2.2f)));
            NoShadow(light.transform);
            var window = _m.Emissive(new Color(0.6f, 0.85f, 1f), new Color(0.5f, 1.2f, 1.8f));
            _m.Primitive(PrimitiveType.Cube, "Window", t, new Vector3(0.3f, 0.6f * s, -size.y * 0.25f - 0.01f), new Vector3(0.8f, 0.3f, 0.04f), window);
            return 2.1f * s;
        }

        private Transform BuildEmptyMarker(Transform parent, Vector2 size)
        {
            var root = new GameObject("Empty").transform;
            root.SetParent(parent, false);
            var post = _m.Lit(new Color(0.85f, 0.6f, 0.1f));
            float hx = size.x * 0.42f, hz = size.y * 0.42f;
            foreach (var c in new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(-hx, hz), new Vector2(hx, hz) })
                _m.Primitive(PrimitiveType.Cube, "Stake", root, new Vector3(c.x, 0.25f, c.y), new Vector3(0.1f, 0.5f, 0.1f), post);

            var plus = new GameObject("Plus").transform;
            plus.SetParent(root, false);
            var glow = _m.Unlit(new Color(1.6f, 1.0f, 0.3f));
            var a = _m.Primitive(PrimitiveType.Cube, "H", plus, Vector3.zero, new Vector3(0.7f, 0.18f, 0.18f), glow);
            var b = _m.Primitive(PrimitiveType.Cube, "V", plus, Vector3.zero, new Vector3(0.18f, 0.7f, 0.18f), glow);
            NoShadow(a.transform);
            NoShadow(b.transform);
            return root;
        }

        private Transform BuildScaffold(Transform parent, Vector2 size, float height)
        {
            var root = new GameObject("Scaffold").transform;
            root.SetParent(parent, false);
            var pole = _m.Lit(new Color(0.9f, 0.72f, 0.15f));
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            foreach (var c in new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(-hx, hz), new Vector2(hx, hz) })
                _m.Primitive(PrimitiveType.Cube, "Pole", root, new Vector3(c.x, height * 0.5f, c.y), new Vector3(0.09f, height, 0.09f), pole);
            for (int level = 1; level <= 2; level++)
            {
                float y = height * level / 2.2f;
                _m.Primitive(PrimitiveType.Cube, "RailF", root, new Vector3(0f, y, -hz), new Vector3(size.x, 0.07f, 0.07f), pole);
                _m.Primitive(PrimitiveType.Cube, "RailB", root, new Vector3(0f, y, hz), new Vector3(size.x, 0.07f, 0.07f), pole);
                _m.Primitive(PrimitiveType.Cube, "RailL", root, new Vector3(-hx, y, 0f), new Vector3(0.07f, 0.07f, size.y), pole);
                _m.Primitive(PrimitiveType.Cube, "RailR", root, new Vector3(hx, y, 0f), new Vector3(0.07f, 0.07f, size.y), pole);
            }
            // A small swinging crane beam sells "work in progress".
            var beam = new GameObject("Beam").transform;
            beam.SetParent(root, false);
            beam.localPosition = new Vector3(-hx, height + 0.1f, -hz);
            _m.Primitive(PrimitiveType.Cube, "Arm", beam, new Vector3(size.x * 0.5f, 0f, 0f), new Vector3(size.x, 0.1f, 0.1f), _m.Lit(new Color(0.8f, 0.3f, 0.1f)));
            return root;
        }

        private static void NoShadow(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            if (r == null) return;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
