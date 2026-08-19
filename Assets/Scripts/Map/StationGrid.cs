// -----------------------------------------------------------------------------
//  NEBULA NINE - navigation / visibility grid.
//
//  Derived entirely from StationLayout.  Provides:
//    * walkability + area lookup per cell (per deck)
//    * A* with 8-way movement, corner-cut prevention and string-pulled smoothing
//    * grid line-of-sight (used for NPC perception and for player vision cones)
//    * a room level adjacency graph with all-pairs distances, which the AI uses to
//      reason about "could that player have got from A to B in that time?"
//
//  No physics raycasts are involved, so a full 15-agent perception pass costs a
//  few dozen microseconds on a mid range phone.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;

namespace Nebula.Map
{
    public class StationGrid
    {
        public static StationGrid Instance { get; private set; }

        public const int Decks = 2;
        private readonly int _w = StationLayout.GridW;
        private readonly int _h = StationLayout.GridH;

        private readonly bool[][] _walkable = new bool[Decks][];
        private readonly bool[][] _blocked = new bool[Decks][];   // dynamic (closed doors)
        private readonly short[][] _area = new short[Decks][];

        // --- A* scratch ---
        private readonly float[] _g;
        private readonly float[] _f;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly byte[] _closed;
        private int _searchStamp;
        private MinHeap _open;

        // --- room graph ---
        private List<int> _roomIds;                    // area ids of type Room/Corridor
        private Dictionary<int, int> _roomIndex;       // areaId -> dense index
        private float[,] _roomDist;                    // metres, walking
        private List<int>[] _roomNeighbours;

        public StationGrid()
        {
            for (int d = 0; d < Decks; d++)
            {
                _walkable[d] = new bool[_w * _h];
                _blocked[d] = new bool[_w * _h];
                _area[d] = new short[_w * _h];
                for (int i = 0; i < _area[d].Length; i++) _area[d][i] = -1;
            }

            _g = new float[_w * _h];
            _f = new float[_w * _h];
            _parent = new int[_w * _h];
            _stamp = new int[_w * _h];
            _closed = new byte[_w * _h];
            _open = new MinHeap(_w * _h / 4);

            Rasterise();
            BuildRoomGraph();
            Instance = this;
        }

        public int Width => _w;
        public int Height => _h;

        // ------------------------------------------------------------------ build
        private void Rasterise()
        {
            // Rooms and corridors first, doorways last so they overwrite the wall cells.
            foreach (var a in StationLayout.Areas)
            {
                if (a.Type == AreaType.Doorway) continue;
                Fill(a);
            }
            foreach (var a in StationLayout.Areas)
            {
                if (a.Type != AreaType.Doorway) continue;
                Fill(a);
            }
        }

        private void Fill(AreaDef a)
        {
            int d = (int)a.Deck;
            for (int z = a.Rect.yMin; z < a.Rect.yMax; z++)
            {
                for (int x = a.Rect.xMin; x < a.Rect.xMax; x++)
                {
                    if (x < 0 || z < 0 || x >= _w || z >= _h) continue;
                    int i = z * _w + x;
                    _walkable[d][i] = true;
                    _area[d][i] = (short)a.Id;
                }
            }
        }

        // ------------------------------------------------------------------ queries
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < _w && z < _h;

        public bool Walkable(DeckId deck, int x, int z)
        {
            if (!InBounds(x, z)) return false;
            int i = z * _w + x;
            int d = (int)deck;
            return _walkable[d][i] && !_blocked[d][i];
        }

        public bool WalkableStatic(DeckId deck, int x, int z)
        {
            if (!InBounds(x, z)) return false;
            return _walkable[(int)deck][z * _w + x];
        }

        public int AreaAt(DeckId deck, int x, int z)
        {
            if (!InBounds(x, z)) return -1;
            return _area[(int)deck][z * _w + x];
        }

        public int AreaAtWorld(Vector3 world)
        {
            var deck = StationLayout.DeckOfWorld(world);
            var c = StationLayout.WorldToCell(world);
            return AreaAt(deck, c.x, c.y);
        }

        /// <summary>Resolves doorway cells to the room they belong to.</summary>
        public int RoomAtWorld(Vector3 world)
        {
            int id = AreaAtWorld(world);
            var a = StationLayout.Get(id);
            if (a != null && a.Type == AreaType.Doorway)
            {
                var owner = StationLayout.Get(a.OwnerRoom);
                if (owner != null) return owner.Id;
            }
            return id;
        }

        public bool WalkableWorld(Vector3 world)
        {
            var deck = StationLayout.DeckOfWorld(world);
            var c = StationLayout.WorldToCell(world);
            return Walkable(deck, c.x, c.y);
        }

        public void SetBlocked(AreaDef doorway, bool blocked)
        {
            if (doorway == null) return;
            int d = (int)doorway.Deck;
            for (int z = doorway.Rect.yMin; z < doorway.Rect.yMax; z++)
            for (int x = doorway.Rect.xMin; x < doorway.Rect.xMax; x++)
            {
                if (!InBounds(x, z)) continue;
                _blocked[d][z * _w + x] = blocked;
            }
        }

        public Vector3 NearestWalkable(DeckId deck, Vector3 world)
        {
            var c = StationLayout.WorldToCell(world);
            if (Walkable(deck, c.x, c.y)) return world;
            for (int r = 1; r < 24; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                    if (Walkable(deck, c.x + dx, c.y + dz))
                        return StationLayout.CellToWorld(deck, c.x + dx, c.y + dz);
                }
            }
            return world;
        }

        // ------------------------------------------------------------------ line of sight
        /// <summary>Supercover grid line test; returns false when any solid cell blocks the ray.</summary>
        public bool LineOfSight(DeckId deck, Vector3 a, Vector3 b)
        {
            var ca = StationLayout.WorldToCell(a);
            var cb = StationLayout.WorldToCell(b);
            return LineOfSightCells(deck, ca.x, ca.y, cb.x, cb.y);
        }

        public bool LineOfSightCells(DeckId deck, int x0, int z0, int x1, int z1)
        {
            int dx = Mathf.Abs(x1 - x0), dz = Mathf.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1, sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;
            int guard = dx + dz + 4;

            while (guard-- > 0)
            {
                if (!Walkable(deck, x0, z0)) return false;
                if (x0 == x1 && z0 == z1) return true;
                int e2 = err * 2;
                if (e2 > -dz)
                {
                    // moving in x: also require the shared corner to be open
                    err -= dz;
                    x0 += sx;
                    if (e2 < dx && !Walkable(deck, x0, z0) && !Walkable(deck, x0 - sx, z0 + sz)) return false;
                }
                if (e2 < dx)
                {
                    err += dx;
                    z0 += sz;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ A*
        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] DCost = { 1f, 1f, 1f, 1f, 1.41421f, 1.41421f, 1.41421f, 1.41421f };

        /// <summary>
        /// Finds a smoothed path. Returns false when unreachable (the output list then
        /// holds a single "best effort" waypoint towards the goal).
        /// </summary>
        public bool FindPath(DeckId deck, Vector3 from, Vector3 to, List<Vector3> result, int maxNodes = 6000)
        {
            result.Clear();
            var ca = StationLayout.WorldToCell(from);
            var cb = StationLayout.WorldToCell(to);

            if (!Walkable(deck, cb.x, cb.y))
            {
                var fixedTo = NearestWalkable(deck, to);
                cb = StationLayout.WorldToCell(fixedTo);
            }
            if (!Walkable(deck, ca.x, ca.y))
            {
                var fixedFrom = NearestWalkable(deck, from);
                ca = StationLayout.WorldToCell(fixedFrom);
            }

            int start = ca.y * _w + ca.x;
            int goal = cb.y * _w + cb.x;
            if (start == goal)
            {
                result.Add(to);
                return true;
            }

            _searchStamp++;
            _open.Clear();
            Touch(start);
            _g[start] = 0f;
            _f[start] = Heuristic(ca.x, ca.y, cb.x, cb.y);
            _open.Push(start, _f[start]);

            int expanded = 0;
            bool found = false;

            while (_open.Count > 0)
            {
                int cur = _open.Pop();
                if (_closed[cur] == 1) continue;
                _closed[cur] = 1;
                if (cur == goal) { found = true; break; }
                if (++expanded > maxNodes) break;

                int cx = cur % _w, cz = cur / _w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], nz = cz + DZ[k];
                    if (!Walkable(deck, nx, nz)) continue;
                    if (k >= 4)
                    {
                        // no diagonal squeezing between two walls
                        if (!Walkable(deck, cx + DX[k], cz) || !Walkable(deck, cx, cz + DZ[k])) continue;
                    }
                    int ni = nz * _w + nx;
                    Touch(ni);
                    if (_closed[ni] == 1) continue;
                    float ng = _g[cur] + DCost[k];
                    if (ng < _g[ni] - 0.0001f)
                    {
                        _g[ni] = ng;
                        _parent[ni] = cur;
                        _f[ni] = ng + Heuristic(nx, nz, cb.x, cb.y);
                        _open.Push(ni, _f[ni]);
                    }
                }
            }

            if (!found)
            {
                result.Add(NearestWalkable(deck, to));
                return false;
            }

            // reconstruct
            var raw = ListPool.GetInt();
            int node = goal;
            int guard = 0;
            while (node != start && guard++ < 20000)
            {
                raw.Add(node);
                node = _parent[node];
            }
            raw.Add(start);
            raw.Reverse();

            // String pull, forward scan: extend the segment while line of sight holds.
            // The obvious backwards search is O(n^2) line-of-sight traces per path,
            // which is far too expensive with a dozen agents re-pathing on a phone.
            int idx = 0;
            while (idx < raw.Count - 1)
            {
                int ax = raw[idx] % _w, az = raw[idx] / _w;
                int best = idx + 1;
                for (int j = idx + 2; j < raw.Count; j++)
                {
                    if (!LineOfSightCells(deck, ax, az, raw[j] % _w, raw[j] / _w)) break;
                    best = j;
                }
                int px = raw[best] % _w, pz = raw[best] / _w;
                result.Add(StationLayout.CellToWorld(deck, px, pz));
                idx = best;
            }

            if (result.Count > 0) result[result.Count - 1] = to;
            else result.Add(to);
            ListPool.Release(raw);
            return true;
        }

        private void Touch(int i)
        {
            if (_stamp[i] != _searchStamp)
            {
                _stamp[i] = _searchStamp;
                _g[i] = float.MaxValue;
                _f[i] = float.MaxValue;
                _parent[i] = -1;
                _closed[i] = 0;
            }
        }

        private static float Heuristic(int ax, int az, int bx, int bz)
        {
            int dx = Mathf.Abs(ax - bx), dz = Mathf.Abs(az - bz);
            int min = Mathf.Min(dx, dz);
            return (dx + dz) + (1.41421f - 2f) * min;
        }

        // ------------------------------------------------------------------ room graph
        private void BuildRoomGraph()
        {
            _roomIds = new List<int>();
            _roomIndex = new Dictionary<int, int>();
            foreach (var a in StationLayout.Areas)
            {
                if (a.Type == AreaType.Doorway) continue;
                _roomIndex[a.Id] = _roomIds.Count;
                _roomIds.Add(a.Id);
            }

            int n = _roomIds.Count;
            _roomDist = new float[n, n];
            _roomNeighbours = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                _roomNeighbours[i] = new List<int>();
                for (int j = 0; j < n; j++) _roomDist[i, j] = i == j ? 0f : 100000f;
            }

            // adjacency: two areas are connected when their cells touch (doorways bridge walls)
            for (int d = 0; d < Decks; d++)
            for (int z = 0; z < _h; z++)
            for (int x = 0; x < _w; x++)
            {
                if (!_walkable[d][z * _w + x]) continue;
                int a = ResolveRoom(_area[d][z * _w + x]);
                if (a < 0) continue;
                TryLink(d, a, x + 1, z);
                TryLink(d, a, x, z + 1);
            }

            // elevators bridge decks
            foreach (var e in StationLayout.Elevators)
            {
                int a = ResolveRoom(AreaAt(e.DeckA, e.CellA.x, e.CellA.y));
                int b = ResolveRoom(AreaAt(e.DeckB, e.CellB.x, e.CellB.y));
                if (a < 0 || b < 0) continue;
                Connect(a, b, 6f);
            }

            // Floyd-Warshall over ~30 nodes: instant, and gives the AI a travel-time oracle.
            for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
            {
                float ik = _roomDist[i, k];
                if (ik > 90000f) continue;
                for (int j = 0; j < n; j++)
                {
                    float v = ik + _roomDist[k, j];
                    if (v < _roomDist[i, j]) _roomDist[i, j] = v;
                }
            }
        }

        private int ResolveRoom(int areaId)
        {
            var a = StationLayout.Get(areaId);
            if (a == null) return -1;
            if (a.Type == AreaType.Doorway)
            {
                var owner = StationLayout.Get(a.OwnerRoom);
                return owner?.Id ?? -1;
            }
            return a.Id;
        }

        private void TryLink(int deck, int areaA, int x, int z)
        {
            if (!InBounds(x, z)) return;
            if (!_walkable[deck][z * _w + x]) return;
            int b = ResolveRoom(_area[deck][z * _w + x]);
            if (b < 0 || b == areaA) return;
            Connect(areaA, b, EuclideanBetween(areaA, b));
        }

        private float EuclideanBetween(int areaA, int areaB)
        {
            var a = StationLayout.Get(areaA);
            var b = StationLayout.Get(areaB);
            if (a == null || b == null) return 10f;
            return Vector2.Distance(a.CenterCell, b.CenterCell) * StationLayout.CellSize;
        }

        private void Connect(int areaA, int areaB, float cost)
        {
            if (!_roomIndex.TryGetValue(areaA, out int ia)) return;
            if (!_roomIndex.TryGetValue(areaB, out int ib)) return;
            if (cost < _roomDist[ia, ib])
            {
                _roomDist[ia, ib] = cost;
                _roomDist[ib, ia] = cost;
            }
            if (!_roomNeighbours[ia].Contains(ib)) _roomNeighbours[ia].Add(ib);
            if (!_roomNeighbours[ib].Contains(ia)) _roomNeighbours[ib].Add(ia);
        }

        /// <summary>Walking distance in metres between two areas (rooms or corridors).</summary>
        public float RoomDistance(int areaA, int areaB)
        {
            if (areaA == areaB) return 0f;
            if (_roomIndex == null) return 40f;
            if (!_roomIndex.TryGetValue(ResolveRoom(areaA), out int ia)) return 60f;
            if (!_roomIndex.TryGetValue(ResolveRoom(areaB), out int ib)) return 60f;
            return _roomDist[ia, ib];
        }

        public IReadOnlyList<int> Neighbours(int areaId)
        {
            if (_roomIndex != null && _roomIndex.TryGetValue(ResolveRoom(areaId), out int i))
            {
                var list = new List<int>(_roomNeighbours[i].Count);
                foreach (var idx in _roomNeighbours[i]) list.Add(_roomIds[idx]);
                return list;
            }
            return new List<int>();
        }

        // ------------------------------------------------------------------ sampling
        public Vector3 RandomPointInArea(int areaId, NebulaRandom rng, float inset = 1.5f)
        {
            var a = StationLayout.Get(areaId);
            if (a == null) return Vector3.zero;
            for (int i = 0; i < 24; i++)
            {
                float x = rng.Range(a.Rect.xMin + inset, a.Rect.xMax - inset);
                float z = rng.Range(a.Rect.yMin + inset, a.Rect.yMax - inset);
                int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
                if (Walkable(a.Deck, cx, cz)) return StationLayout.CellToWorld(a.Deck, x, z);
            }
            return StationLayout.CellToWorld(a.Deck, a.CenterCell.x, a.CenterCell.y);
        }

        /// <summary>Deterministic "nice looking" station point inside a room (for consoles).</summary>
        public Vector3 StationPoint(int areaId, int slot, out float yaw)
        {
            var a = StationLayout.Get(areaId);
            yaw = 0f;
            if (a == null) return Vector3.zero;

            int perimeter = Mathf.Max(4, (a.Rect.width + a.Rect.height) * 2 - 8);
            int step = Mathf.Max(3, perimeter / 8);
            int t = (slot * step + slot * 5 + 2) % perimeter;

            int w = a.Rect.width - 3, h = a.Rect.height - 3;
            if (w < 1) w = 1;
            if (h < 1) h = 1;

            float cx, cz;
            if (t < w) { cx = a.Rect.xMin + 1.5f + t; cz = a.Rect.yMin + 1.6f; yaw = 0f; }
            else if (t < w + h) { cx = a.Rect.xMax - 1.6f; cz = a.Rect.yMin + 1.5f + (t - w); yaw = 270f; }
            else if (t < w + h + w) { cx = a.Rect.xMax - 1.5f - (t - w - h); cz = a.Rect.yMax - 1.6f; yaw = 180f; }
            else { cx = a.Rect.xMin + 1.6f; cz = a.Rect.yMax - 1.5f - (t - w - h - w); yaw = 90f; }

            cx = Mathf.Clamp(cx, a.Rect.xMin + 1f, a.Rect.xMax - 1f);
            cz = Mathf.Clamp(cz, a.Rect.yMin + 1f, a.Rect.yMax - 1f);
            return StationLayout.CellToWorld(a.Deck, cx, cz);
        }
    }

    /// <summary>Tiny binary heap specialised for int payloads.</summary>
    public class MinHeap
    {
        private int[] _items;
        private float[] _keys;
        public int Count { get; private set; }

        public MinHeap(int capacity)
        {
            capacity = Mathf.Max(16, capacity);
            _items = new int[capacity];
            _keys = new float[capacity];
        }

        public void Clear() => Count = 0;

        public void Push(int item, float key)
        {
            if (Count == _items.Length)
            {
                System.Array.Resize(ref _items, Count * 2);
                System.Array.Resize(ref _keys, Count * 2);
            }
            int i = Count++;
            _items[i] = item;
            _keys[i] = key;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_keys[p] <= _keys[i]) break;
                Swap(p, i);
                i = p;
            }
        }

        public int Pop()
        {
            int top = _items[0];
            Count--;
            if (Count > 0)
            {
                _items[0] = _items[Count];
                _keys[0] = _keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < Count && _keys[l] < _keys[m]) m = l;
                    if (r < Count && _keys[r] < _keys[m]) m = r;
                    if (m == i) break;
                    Swap(m, i);
                    i = m;
                }
            }
            return top;
        }

        private void Swap(int a, int b)
        {
            (_items[a], _items[b]) = (_items[b], _items[a]);
            (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
        }
    }

    public static class ListPool
    {
        private static readonly Stack<List<int>> IntPool = new Stack<List<int>>();

        public static List<int> GetInt()
        {
            var l = IntPool.Count > 0 ? IntPool.Pop() : new List<int>(256);
            l.Clear();
            return l;
        }

        public static void Release(List<int> l)
        {
            if (l == null) return;
            l.Clear();
            if (IntPool.Count < 16) IntPool.Push(l);
        }
    }
}
