using System.Collections.Generic;
using UnityEngine;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// The single base map for M0: grid layout, walls, gates, ruins and spawn edges,
    /// plus world ↔ cell conversion. Owns the shared flow field toward the HQ.
    /// </summary>
    public sealed class MapLayout
    {
        public const int Cols = 28;
        public const int Rows = 48;
        public const float CellSize = 1f;

        /// <summary>World position of the corner of cell (0,0).</summary>
        public static readonly Vector3 Origin = new Vector3(-14f, 0f, -14f);

        public static float MinX => Origin.x;
        public static float MaxX => Origin.x + Cols * CellSize;
        public static float MinZ => Origin.z;
        public static float MaxZ => Origin.z + Rows * CellSize;
        public static Vector3 Center => new Vector3((MinX + MaxX) * 0.5f, 0f, (MinZ + MaxZ) * 0.5f);

        // HQ: 4x4 cells centred on world (0,0).
        public static readonly RectInt HqCells = new RectInt(12, 12, 4, 4);
        // Wall ring perimeter (inclusive cell bounds).
        public static readonly RectInt WallRing = new RectInt(6, 8, 16, 16); // cells x 6..21, y 8..23
        public static readonly RectInt[] Gates =
        {
            new RectInt(12, 23, 4, 1), // north gate, 4 wide
            new RectInt(21, 14, 1, 3), // east gate
            new RectInt(6, 14, 1, 3),  // west gate
        };

        // Ruins and wrecks outside the wall; they shape the zombie routes.
        public static readonly RectInt[] Buildings =
        {
            new RectInt(1, 30, 4, 3),
            new RectInt(22, 29, 4, 3),
            new RectInt(9, 37, 3, 3),
            new RectInt(17, 41, 3, 3),
            new RectInt(2, 41, 3, 2),
            new RectInt(1, 18, 2, 4),
            new RectInt(25, 17, 2, 4),
            new RectInt(4, 2, 3, 3),
            new RectInt(21, 3, 3, 3),
        };

        /// <summary>Building plots by BasePlots id (cell rects). Index = plot id.</summary>
        public static readonly RectInt[] Plots =
        {
            HqCells,                    // 0 Headquarters
            new RectInt(7, 9, 3, 3),    // 1 Farm
            new RectInt(18, 9, 3, 3),   // 2 FuelPump
            new RectInt(7, 19, 3, 3),   // 3 Workshop
            new RectInt(12, 9, 4, 2),   // 4 Shelter
            new RectInt(18, 19, 3, 3),  // 5 Clinic
            new RectInt(10, 19, 2, 2),  // 6 Tower A
            new RectInt(16, 19, 2, 2),  // 7 Tower B
        };

        public static readonly RectInt[] Cars =
        {
            new RectInt(16, 33, 2, 1),
            new RectInt(8, 28, 1, 2),
            new RectInt(23, 38, 2, 1),
            new RectInt(13, 44, 2, 1),
            new RectInt(3, 26, 2, 1),
        };

        public FlowField Field { get; }
        public List<Vector2Int> WallCells { get; } = new List<Vector2Int>();
        public Bounds HqBounds { get; }

        public MapLayout()
        {
            Field = new FlowField(Cols, Rows);

            for (int x = WallRing.xMin; x < WallRing.xMax; x++)
            {
                for (int y = WallRing.yMin; y < WallRing.yMax; y++)
                {
                    bool perimeter = x == WallRing.xMin || x == WallRing.xMax - 1 || y == WallRing.yMin || y == WallRing.yMax - 1;
                    if (!perimeter || IsGate(x, y)) continue;
                    WallCells.Add(new Vector2Int(x, y));
                    Field.SetCost(x, y, FlowField.BlockedCost);
                }
            }

            foreach (var r in Buildings) Block(r);
            foreach (var r in Cars) Block(r);
            for (int i = 1; i < Plots.Length; i++) Block(Plots[i]); // HQ stays the flow target

            var targets = new List<GridCoord>();
            for (int x = HqCells.xMin; x < HqCells.xMax; x++)
                for (int y = HqCells.yMin; y < HqCells.yMax; y++)
                    targets.Add(new GridCoord(x, y));
            Field.Build(targets);

            Vector3 hqMin = CellCorner(HqCells.xMin, HqCells.yMin);
            Vector3 hqMax = CellCorner(HqCells.xMax, HqCells.yMax);
            HqBounds = new Bounds((hqMin + hqMax) * 0.5f, new Vector3(hqMax.x - hqMin.x, 3f, hqMax.z - hqMin.z));
        }

        private void Block(RectInt r)
        {
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                    Field.SetCost(x, y, FlowField.BlockedCost);
        }

        /// <summary>Plot id under a world position (with a little tap tolerance), or -1.</summary>
        public static int PlotAt(Vector3 p, float tolerance = 0.4f)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Plots.Length; i++)
            {
                var r = Plots[i];
                Vector3 min = CellCorner(r.xMin, r.yMin), max = CellCorner(r.xMax, r.yMax);
                float dx = Mathf.Max(min.x - p.x, 0f, p.x - max.x);
                float dz = Mathf.Max(min.z - p.z, 0f, p.z - max.z);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d <= tolerance && d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }

        public static Vector3 PlotCenter(int id) => RectCenter(Plots[id]);
        public static Vector2 PlotSize(int id) => new Vector2(Plots[id].width * CellSize, Plots[id].height * CellSize);

        public static bool IsGate(int x, int y)
        {
            foreach (var g in Gates)
                if (g.Contains(new Vector2Int(x, y))) return true;
            return false;
        }

        public static Vector3 CellCorner(int x, int y) => Origin + new Vector3(x * CellSize, 0f, y * CellSize);
        public static Vector3 CellCenter(int x, int y) => CellCorner(x, y) + new Vector3(CellSize * 0.5f, 0f, CellSize * 0.5f);

        public static Vector3 RectCenter(RectInt r) =>
            Origin + new Vector3((r.x + r.width * 0.5f) * CellSize, 0f, (r.y + r.height * 0.5f) * CellSize);

        public static void WorldToCell(Vector3 p, out int x, out int y)
        {
            x = Mathf.FloorToInt((p.x - Origin.x) / CellSize);
            y = Mathf.FloorToInt((p.z - Origin.z) / CellSize);
        }

        public static int CellIndexClamped(Vector3 p)
        {
            WorldToCell(p, out int x, out int y);
            x = Mathf.Clamp(x, 0, Cols - 1);
            y = Mathf.Clamp(y, 0, Rows - 1);
            return y * Cols + x;
        }

        public static Vector3 ClampToMap(Vector3 p, float margin = 0.3f)
        {
            p.x = Mathf.Clamp(p.x, MinX + margin, MaxX - margin);
            p.z = Mathf.Clamp(p.z, MinZ + margin, MaxZ - margin);
            p.y = 0f;
            return p;
        }

        public bool IsWalkableForZombie(Vector3 p)
        {
            WorldToCell(p, out int x, out int y);
            return Field.InBounds(x, y) && !Field.IsBlocked(x, y);
        }

        public bool IsWalkableForHero(Vector3 p)
        {
            WorldToCell(p, out int x, out int y);
            if (!Field.InBounds(x, y) || Field.IsBlocked(x, y)) return false;
            return !HqCells.Contains(new Vector2Int(x, y));
        }

        /// <summary>Flow direction at a world position, or zero on the target/unreachable cells.</summary>
        public Vector3 FlowAt(Vector3 p)
        {
            WorldToCell(p, out int x, out int y);
            Field.GetDirection(x, y, out int dx, out int dy);
            if (dx == 0 && dy == 0) return Vector3.zero;
            return new Vector3(dx, 0f, dy).normalized;
        }

        public float DistanceToHq(Vector3 p)
        {
            var b = HqBounds;
            float dx = Mathf.Max(b.min.x - p.x, 0f, p.x - b.max.x);
            float dz = Mathf.Max(b.min.z - p.z, 0f, p.z - b.max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public Vector3 RandomSpawnPoint(SpawnSide side, System.Random rng)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int x, y;
                switch (side)
                {
                    case SpawnSide.East: x = Cols - 1; y = rng.Next(26, Rows); break;
                    case SpawnSide.West: x = 0; y = rng.Next(26, Rows); break;
                    default: x = rng.Next(0, Cols); y = Rows - 1; break;
                }
                if (Field.IsBlocked(x, y)) continue;
                var c = CellCenter(x, y);
                c.x += (float)(rng.NextDouble() - 0.5) * 0.8f;
                c.z += (float)(rng.NextDouble() - 0.5) * 0.8f;
                return c;
            }
            return CellCenter(Cols / 2, Rows - 1);
        }
    }
}
