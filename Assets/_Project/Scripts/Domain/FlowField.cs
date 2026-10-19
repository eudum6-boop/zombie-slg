using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    /// <summary>Integer cell coordinate on the battle grid (X = column, Y = row).</summary>
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public readonly int X;
        public readonly int Y;

        public GridCoord(int x, int y) { X = x; Y = y; }

        public bool Equals(GridCoord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X},{Y})";
    }

    /// <summary>
    /// Single-target flow field. Every zombie on the map shares one field toward the base,
    /// so pathfinding cost is paid once per map change instead of once per agent.
    /// Pure C# so it can be unit-tested and reused by server code.
    /// </summary>
    public sealed class FlowField
    {
        public const byte BlockedCost = 255;
        public const int Unreachable = int.MaxValue;

        private const int StraightStep = 10;
        private const int DiagonalStep = 14;

        // 8 neighbours: E, W, N, S, NE, NW, SE, SW
        private static readonly int[] NeighbourDx = { 1, -1, 0, 0, 1, -1, 1, -1 };
        private static readonly int[] NeighbourDy = { 0, 0, 1, -1, 1, 1, -1, -1 };

        private readonly byte[] _cost;
        private readonly int[] _integration;
        private readonly sbyte[] _dirX;
        private readonly sbyte[] _dirY;
        private readonly MinHeap _open = new MinHeap(256);

        public int Width { get; }
        public int Height { get; }
        public int Version { get; private set; }

        public FlowField(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            int n = width * height;
            _cost = new byte[n];
            _integration = new int[n];
            _dirX = new sbyte[n];
            _dirY = new sbyte[n];
            for (int i = 0; i < n; i++) { _cost[i] = 1; _integration[i] = Unreachable; }
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public int Index(int x, int y) => y * Width + x;

        public void SetCost(int x, int y, byte cost)
        {
            if (!InBounds(x, y)) return;
            _cost[Index(x, y)] = cost == 0 ? (byte)1 : cost;
        }

        public byte GetCost(int x, int y) => InBounds(x, y) ? _cost[Index(x, y)] : BlockedCost;
        public bool IsBlocked(int x, int y) => GetCost(x, y) == BlockedCost;

        public int GetIntegration(int x, int y) => InBounds(x, y) ? _integration[Index(x, y)] : Unreachable;

        /// <summary>Direction to step toward the target. (0,0) on a target cell or an unreachable/blocked cell.</summary>
        public void GetDirection(int x, int y, out int dx, out int dy)
        {
            if (!InBounds(x, y)) { dx = 0; dy = 0; return; }
            int i = Index(x, y);
            dx = _dirX[i];
            dy = _dirY[i];
        }

        /// <summary>Dijkstra from every target cell, then each cell points at its cheapest neighbour.</summary>
        public void Build(IReadOnlyList<GridCoord> targets)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));

            int n = Width * Height;
            for (int i = 0; i < n; i++) { _integration[i] = Unreachable; _dirX[i] = 0; _dirY[i] = 0; }
            _open.Clear();

            for (int t = 0; t < targets.Count; t++)
            {
                var c = targets[t];
                if (!InBounds(c.X, c.Y) || IsBlocked(c.X, c.Y)) continue;
                int idx = Index(c.X, c.Y);
                _integration[idx] = 0;
                _open.Push(0, idx);
            }

            while (_open.Count > 0)
            {
                _open.Pop(out int dist, out int idx);
                if (dist > _integration[idx]) continue; // stale entry

                int x = idx % Width;
                int y = idx / Width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + NeighbourDx[k];
                    int ny = y + NeighbourDy[k];
                    if (!CanStep(x, y, nx, ny, k)) continue;

                    int nIdx = Index(nx, ny);
                    int step = (k < 4 ? StraightStep : DiagonalStep) * _cost[nIdx];
                    int nd = dist + step;
                    if (nd < _integration[nIdx])
                    {
                        _integration[nIdx] = nd;
                        _open.Push(nd, nIdx);
                    }
                }
            }

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int idx = Index(x, y);
                    int best = _integration[idx];
                    if (best == 0 || best == Unreachable) continue;

                    int bestK = -1;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + NeighbourDx[k];
                        int ny = y + NeighbourDy[k];
                        if (!CanStep(x, y, nx, ny, k)) continue;
                        int v = _integration[Index(nx, ny)];
                        if (v < best) { best = v; bestK = k; }
                    }

                    if (bestK >= 0)
                    {
                        _dirX[idx] = (sbyte)NeighbourDx[bestK];
                        _dirY[idx] = (sbyte)NeighbourDy[bestK];
                    }
                }
            }

            Version++;
        }

        // Diagonal moves are only allowed when both orthogonal cells are open (no corner cutting through walls).
        private bool CanStep(int x, int y, int nx, int ny, int k)
        {
            if (!InBounds(nx, ny) || IsBlocked(nx, ny)) return false;
            if (k < 4) return true;
            return !IsBlocked(nx, y) && !IsBlocked(x, ny);
        }

        /// <summary>Allocation-free binary min-heap keyed by distance.</summary>
        private sealed class MinHeap
        {
            private long[] _items;
            public int Count { get; private set; }

            public MinHeap(int capacity) { _items = new long[capacity]; }

            public void Clear() => Count = 0;

            public void Push(int priority, int value)
            {
                if (Count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
                long item = ((long)priority << 32) | (uint)value;
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (_items[parent] <= item) break;
                    _items[i] = _items[parent];
                    i = parent;
                }
                _items[i] = item;
            }

            public void Pop(out int priority, out int value)
            {
                long top = _items[0];
                priority = (int)(top >> 32);
                value = (int)(top & 0xFFFFFFFF);

                long last = _items[--Count];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && _items[child + 1] < _items[child]) child++;
                    if (_items[child] >= last) break;
                    _items[i] = _items[child];
                    i = child;
                }
                if (Count > 0) _items[i] = last;
            }
        }
    }
}
