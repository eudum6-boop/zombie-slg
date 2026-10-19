using UnityEngine;

namespace ZombieSLG.Battle
{
    /// <summary>Builds the placeholder low-poly environment from <see cref="MapLayout"/>. Swapped for asset-pack art later.</summary>
    public static class MapBuilder
    {
        public static void Build(MapLayout map, MaterialLibrary m, Transform parent)
        {
            var root = new GameObject("Environment").transform;
            root.SetParent(parent, false);

            float w = MapLayout.Cols * MapLayout.CellSize;
            float d = MapLayout.Rows * MapLayout.CellSize;
            Vector3 c = MapLayout.Center;

            // Ground: asphalt city block with a wider apron so the edges don't show.
            m.Primitive(PrimitiveType.Cube, "Ground", root, new Vector3(c.x, -0.5f, c.z), new Vector3(w + 30f, 1f, d + 30f),
                m.Lit(new Color(0.17f, 0.18f, 0.19f)));

            // Base compound floor.
            var ring = MapLayout.WallRing;
            Vector3 rc = MapLayout.RectCenter(ring);
            m.Primitive(PrimitiveType.Cube, "BaseFloor", root, new Vector3(rc.x, -0.49f, rc.z), new Vector3(ring.width, 1f, ring.height),
                m.Lit(new Color(0.30f, 0.29f, 0.26f)));

            // Road from the north gate to the map edge + lane dashes.
            var road = m.Lit(new Color(0.22f, 0.22f, 0.23f));
            float roadStart = MapLayout.CellCorner(0, ring.yMax).z;
            float roadLen = MapLayout.MaxZ - roadStart + 8f;
            m.Primitive(PrimitiveType.Cube, "Road", root, new Vector3(0f, -0.495f, roadStart + roadLen * 0.5f), new Vector3(5f, 1f, roadLen), road);
            var dash = m.Lit(new Color(0.75f, 0.65f, 0.25f));
            for (float z = roadStart + 1f; z < MapLayout.MaxZ + 6f; z += 2.5f)
                m.Primitive(PrimitiveType.Cube, "Dash", root, new Vector3(0f, 0.005f, z), new Vector3(0.18f, 0.01f, 1.1f), dash);

            // Walls: concrete barriers with darker caps; posts beside every gate.
            var concrete = m.Lit(new Color(0.58f, 0.57f, 0.53f));
            var cap = m.Lit(new Color(0.40f, 0.39f, 0.36f));
            var walls = new GameObject("Walls").transform;
            walls.SetParent(root, false);
            foreach (var cell in map.WallCells)
            {
                Vector3 p = MapLayout.CellCenter(cell.x, cell.y);
                m.Primitive(PrimitiveType.Cube, "Wall", walls, p + new Vector3(0f, 0.55f, 0f), new Vector3(0.98f, 1.1f, 0.98f), concrete);
                m.Primitive(PrimitiveType.Cube, "Cap", walls, p + new Vector3(0f, 1.15f, 0f), new Vector3(1.0f, 0.12f, 1.0f), cap);
            }

            var hazard = m.Lit(new Color(0.85f, 0.6f, 0.1f));
            foreach (var g in MapLayout.Gates)
            {
                bool horizontal = g.width > g.height;
                Vector3 a = horizontal ? MapLayout.CellCenter(g.xMin - 1, g.y) : MapLayout.CellCenter(g.x, g.yMin - 1);
                Vector3 b = horizontal ? MapLayout.CellCenter(g.xMax, g.y) : MapLayout.CellCenter(g.x, g.yMax);
                m.Primitive(PrimitiveType.Cube, "GatePost", root, a + new Vector3(0f, 0.9f, 0f), new Vector3(1.05f, 1.8f, 1.05f), hazard);
                m.Primitive(PrimitiveType.Cube, "GatePost", root, b + new Vector3(0f, 0.9f, 0f), new Vector3(1.05f, 1.8f, 1.05f), hazard);

                var lamp = new GameObject("GateLamp").AddComponent<Light>();
                lamp.transform.SetParent(root, false);
                lamp.transform.position = MapLayout.RectCenter(g) + new Vector3(0f, 2.5f, 0f);
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.7f, 0.4f);
                lamp.range = 7f;
                lamp.intensity = 6f;
                lamp.shadows = LightShadows.None;
            }

            // Ruined buildings (random heights, deterministic seed).
            var rng = new System.Random(7);
            Color[] ruinColors =
            {
                new Color(0.32f, 0.30f, 0.29f),
                new Color(0.38f, 0.33f, 0.28f),
                new Color(0.27f, 0.29f, 0.31f),
            };
            foreach (var r in MapLayout.Buildings)
            {
                float h = 1.5f + (float)rng.NextDouble() * 3f;
                Vector3 p = MapLayout.RectCenter(r);
                var col = ruinColors[rng.Next(ruinColors.Length)];
                m.Primitive(PrimitiveType.Cube, "Ruin", root, p + new Vector3(0f, h * 0.5f, 0f), new Vector3(r.width * 0.96f, h, r.height * 0.96f), m.Lit(col));
                // Broken top slab, slightly rotated.
                var slab = m.Primitive(PrimitiveType.Cube, "Slab", root, p + new Vector3(0.2f, h + 0.1f, -0.1f),
                    new Vector3(r.width * 0.6f, 0.2f, r.height * 0.5f), m.Lit(col * 0.8f));
                slab.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 40f - 20f, 8f);
            }

            // Wrecked cars.
            Color[] carColors =
            {
                new Color(0.45f, 0.18f, 0.16f),
                new Color(0.20f, 0.28f, 0.40f),
                new Color(0.55f, 0.52f, 0.45f),
            };
            foreach (var r in MapLayout.Cars)
            {
                Vector3 p = MapLayout.RectCenter(r);
                bool alongX = r.width > r.height;
                var col = carColors[rng.Next(carColors.Length)];
                var car = new GameObject("Car").transform;
                car.SetParent(root, false);
                car.position = p;
                car.rotation = Quaternion.Euler(0f, (alongX ? 90f : 0f) + (float)rng.NextDouble() * 20f - 10f, 0f);
                m.Primitive(PrimitiveType.Cube, "Body", car, new Vector3(0f, 0.4f, 0f), new Vector3(0.9f, 0.5f, 1.8f), m.Lit(col));
                m.Primitive(PrimitiveType.Cube, "Cabin", car, new Vector3(0f, 0.8f, -0.1f), new Vector3(0.8f, 0.35f, 0.9f), m.Lit(col * 0.7f));
            }
        }
    }
}
