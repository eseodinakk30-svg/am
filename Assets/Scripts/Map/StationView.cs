// -----------------------------------------------------------------------------
//  NEBULA NINE - builds the visible station from StationLayout.
//
//  Output per deck:
//    * one floor mesh per palette tint (5 renderers, SRP batched)
//    * one merged wall mesh, one object holding merged box colliders
//    * per-room point lights + emissive strips (driven by the lights sabotage)
//    * doors, vents, elevators, security cameras, decorative props
//
//  Nothing here is authored by hand, so the map can be re-laid-out purely by
//  editing StationLayout.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Fx;

namespace Nebula.Map
{
    public class StationView : MonoBehaviour
    {
        public static StationView Instance { get; private set; }

        public StationGrid Grid { get; private set; }
        public readonly List<DoorController> Doors = new List<DoorController>();
        public readonly List<VentPoint> Vents = new List<VentPoint>();
        public readonly List<ElevatorPad> Elevators = new List<ElevatorPad>();
        public readonly List<SecurityCameraUnit> Cameras = new List<SecurityCameraUnit>();
        public readonly Dictionary<int, RoomLightRig> RoomLights = new Dictionary<int, RoomLightRig>();
        public readonly List<Vector3> SpawnPoints = new List<Vector3>();
        public Vector3 MeetingCenter { get; private set; }

        private readonly Dictionary<int, List<VentPoint>> _ventNetworks = new Dictionary<int, List<VentPoint>>();
        private Transform _root;

        private const int WallLayer = 8;

        public void Build()
        {
            Instance = this;
            Grid = new StationGrid();
            _root = transform;

            for (int d = 0; d < StationGrid.Decks; d++)
                BuildDeck((DeckId)d);

            BuildDoors();
            BuildVents();
            BuildElevators();
            BuildCameras();
            BuildSpawnPoints();
            BuildBackdrop();
        }

        // ------------------------------------------------------------------ helpers
        private static Vector3 Corner(DeckId deck, float x, float z)
        {
            return new Vector3(
                (x - StationLayout.GridW * 0.5f) * StationLayout.CellSize,
                deck == DeckId.Upper ? 0f : -StationLayout.DeckHeight,
                (z - StationLayout.GridH * 0.5f) * StationLayout.CellSize);
        }

        private static float DeckY(DeckId deck) => deck == DeckId.Upper ? 0f : -StationLayout.DeckHeight;

        private GameObject Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool shadows = true)
        {
            var go = Child(name, parent);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        // ------------------------------------------------------------------ deck
        private void BuildDeck(DeckId deck)
        {
            var deckRoot = Child("Deck_" + deck, _root).transform;

            // ---- floors, grouped by tint so each group is a single draw call ----
            var groups = new Dictionary<int, MeshBuilder>();
            var groupColor = new Dictionary<int, Color>();

            foreach (var area in StationLayout.Areas)
            {
                if (area.Deck != deck) continue;
                var tint = area.Tint;
                int key = (Mathf.RoundToInt(tint.r * 255) << 16) | (Mathf.RoundToInt(tint.g * 255) << 8) | Mathf.RoundToInt(tint.b * 255);
                if (!groups.TryGetValue(key, out var mb))
                {
                    mb = new MeshBuilder();
                    groups[key] = mb;
                    groupColor[key] = tint;
                }

                var a = Corner(deck, area.Rect.xMin, area.Rect.yMin);
                var b = Corner(deck, area.Rect.xMax, area.Rect.yMax);
                mb.AddFloor(a.x, a.z, b.x, b.z, DeckY(deck), tint);
            }

            foreach (var kv in groups)
            {
                var mesh = kv.Value.Build("Floor_" + deck + "_" + kv.Key);
                MeshObject("Floor_" + kv.Key, deckRoot, mesh, Art.Lit(groupColor[kv.Key], 0.05f, 0.28f), false);
            }

            // ---- walls ----
            BuildWalls(deck, deckRoot);

            // ---- room dressing ----
            foreach (var area in StationLayout.Areas)
            {
                if (area.Deck != deck) continue;
                if (area.Type == AreaType.Doorway) continue;
                BuildRoomDressing(area, deckRoot);
            }
        }

        private void BuildWalls(DeckId deck, Transform deckRoot)
        {
            var mb = new MeshBuilder();
            var colliderRoot = Child("WallColliders_" + deck, deckRoot);
            colliderRoot.layer = WallLayer;
            colliderRoot.isStatic = true;

            var trimBuilder = new MeshBuilder();
            float y = DeckY(deck);
            float h = StationLayout.WallHeight;
            var wallColor = new Color(0.128f, 0.145f, 0.196f);
            var trimColor = new Color(0.28f, 0.60f, 0.72f);

            int w = StationLayout.GridW, gh = StationLayout.GridH;

            // horizontal edges (walls that run along X)
            for (int z = 0; z < gh; z++)
            {
                for (int dir = 0; dir < 2; dir++)
                {
                    int x = 0;
                    while (x < w)
                    {
                        if (NeedsHorizontalWall(deck, x, z, dir))
                        {
                            int start = x;
                            while (x < w && NeedsHorizontalWall(deck, x, z, dir)) x++;
                            float zEdge = dir == 0 ? z + 1 : z;
                            var p0 = Corner(deck, start, zEdge);
                            var p1 = Corner(deck, x, zEdge);
                            mb.AddWall(p0.x, p0.z, p1.x, p1.z, y, h, wallColor);
                            trimBuilder.AddWall(p0.x, p0.z, p1.x, p1.z, y + h - 0.14f, 0.14f, trimColor);
                            AddBoxCollider(colliderRoot, new Vector3((p0.x + p1.x) * 0.5f, y + h * 0.5f, p0.z),
                                new Vector3(Mathf.Abs(p1.x - p0.x), h, 0.5f));
                        }
                        else x++;
                    }
                }
            }

            // vertical edges (walls that run along Z)
            for (int x = 0; x < w; x++)
            {
                for (int dir = 0; dir < 2; dir++)
                {
                    int z = 0;
                    while (z < gh)
                    {
                        if (NeedsVerticalWall(deck, x, z, dir))
                        {
                            int start = z;
                            while (z < gh && NeedsVerticalWall(deck, x, z, dir)) z++;
                            float xEdge = dir == 0 ? x + 1 : x;
                            var p0 = Corner(deck, xEdge, start);
                            var p1 = Corner(deck, xEdge, z);
                            mb.AddWall(p0.x, p0.z, p1.x, p1.z, y, h, wallColor);
                            trimBuilder.AddWall(p0.x, p0.z, p1.x, p1.z, y + h - 0.14f, 0.14f, trimColor);
                            AddBoxCollider(colliderRoot, new Vector3(p0.x, y + h * 0.5f, (p0.z + p1.z) * 0.5f),
                                new Vector3(0.5f, h, Mathf.Abs(p1.z - p0.z)));
                        }
                        else z++;
                    }
                }
            }

            MeshObject("Walls", deckRoot, mb.Build("Walls_" + deck), Art.Lit(wallColor, 0.15f, 0.30f));
            MeshObject("WallTrim", deckRoot, trimBuilder.Build("Trim_" + deck), Art.Lit(trimColor, 0f, 0.6f, 1.4f), false);
        }

        private bool NeedsHorizontalWall(DeckId deck, int x, int z, int dir)
        {
            if (!Grid.WalkableStatic(deck, x, z)) return false;
            int nz = dir == 0 ? z + 1 : z - 1;
            return !Grid.WalkableStatic(deck, x, nz);
        }

        private bool NeedsVerticalWall(DeckId deck, int x, int z, int dir)
        {
            if (!Grid.WalkableStatic(deck, x, z)) return false;
            int nx = dir == 0 ? x + 1 : x - 1;
            return !Grid.WalkableStatic(deck, nx, z);
        }

        private void AddBoxCollider(GameObject host, Vector3 center, Vector3 size)
        {
            var bc = host.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
        }

        // ------------------------------------------------------------------ dressing
        /// <summary>
        /// Табличка с названием комнаты под потолком. Станция большая и вся из
        /// одинаковых серых отсеков — без подписей понять, где ты находишься,
        /// можно было только по миникарте.
        /// </summary>
        private void BuildRoomSign(AreaDef area, Transform holder)
        {
            if (area.Type != AreaType.Room || string.IsNullOrEmpty(area.Name)) return;

            var go = Child("Sign", holder);
            go.transform.position = StationLayout.CellToWorld(area.Deck, area.CenterCell.x, area.CenterCell.y)
                                    + Vector3.up * (StationLayout.WallHeight - 0.6f);
            // камера смотрит сверху под фиксированным наклоном — разворачиваем табличку под него
            go.transform.rotation = Quaternion.Euler(62f, 0f, 0f);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(520f, 90f);
            rt.localScale = Vector3.one * 0.016f;

            var textGo = Child("Text", go.transform);
            var text = textGo.AddComponent<Text>();
            text.font = Art.UiFont;
            text.fontSize = 46;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = area.Name.ToUpperInvariant();
            text.color = new Color(0.78f, 0.88f, 1f, 0.42f);
            text.raycastTarget = false;
            var trt = text.rectTransform;
            trt.sizeDelta = new Vector2(520f, 90f);
            trt.anchoredPosition = Vector2.zero;
        }

        private void BuildRoomDressing(AreaDef area, Transform deckRoot)
        {
            var holder = Child("Room_" + (area.Key ?? area.Id.ToString()), deckRoot).transform;
            BuildRoomSign(area, holder);
            var rng = new NebulaRandom(area.Id * 7919 + 13);
            float y = DeckY(area.Deck);

            // ---- lighting rig ----
            var rig = holder.gameObject.AddComponent<RoomLightRig>();
            rig.RoomId = area.Id;
            RoomLights[area.Id] = rig;

            int lightsX = Mathf.Clamp(area.Rect.width / 12, 1, 3);
            int lightsZ = Mathf.Clamp(area.Rect.height / 12, 1, 3);
            for (int ix = 0; ix < lightsX; ix++)
            for (int iz = 0; iz < lightsZ; iz++)
            {
                float fx = area.Rect.xMin + area.Rect.width * (ix + 0.5f) / lightsX;
                float fz = area.Rect.yMin + area.Rect.height * (iz + 0.5f) / lightsZ;
                var go = Child("Light", holder);
                go.transform.position = StationLayout.CellToWorld(area.Deck, fx, fz) + Vector3.up * 4.2f;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = area.Type == AreaType.Corridor ? 12f : 17f;
                l.intensity = area.Type == AreaType.Corridor ? 1.35f : 1.75f;
                l.color = Color.Lerp(new Color(0.78f, 0.86f, 1f), area.Tint + new Color(0.5f, 0.5f, 0.5f), 0.35f);
                l.shadows = LightShadows.None;   // point light shadows are expensive on mobile
                l.renderMode = LightRenderMode.Auto;
                rig.Register(l);
            }

            // ---- emissive ceiling strips ----
            var strips = new MeshBuilder();
            var stripColor = Color.Lerp(new Color(0.55f, 0.78f, 0.95f), area.Tint * 2.4f, 0.35f);
            int stripCount = Mathf.Clamp(area.Rect.width / 8, 1, 4);
            for (int i = 0; i < stripCount; i++)
            {
                float fx = area.Rect.xMin + area.Rect.width * (i + 0.5f) / stripCount;
                var a = Corner(area.Deck, fx - 0.35f, area.Rect.yMin + 1.5f);
                var b = Corner(area.Deck, fx + 0.35f, area.Rect.yMax - 1.5f);
                strips.AddFloor(a.x, a.z, b.x, b.z, y + StationLayout.WallHeight - 0.05f, stripColor, false);
            }
            var stripGo = MeshObject("Strips", holder, strips.Build("Strips_" + area.Id), Art.Lit(stripColor, 0f, 0.7f, 2.2f), false);
            rig.RegisterEmissive(stripGo.GetComponent<Renderer>(), stripColor);

            if (area.Type == AreaType.Corridor) return;

            // ---- floor decals + low props (visual only, they never block movement) ----
            var props = new MeshBuilder();
            var propColor = Color.Lerp(area.Tint, new Color(0.35f, 0.39f, 0.47f), 0.6f);

            // painted hazard border
            float bx0 = area.Rect.xMin + 1f, bz0 = area.Rect.yMin + 1f;
            float bx1 = area.Rect.xMax - 1f, bz1 = area.Rect.yMax - 1f;
            var c0 = Corner(area.Deck, bx0, bz0);
            var c1 = Corner(area.Deck, bx1, bz1);
            var borderColor = Color.Lerp(area.Tint, Art.AccentWarm, 0.22f);
            props.AddFloor(c0.x, c0.z, c1.x, c0.z + 0.25f, y + 0.02f, borderColor);
            props.AddFloor(c0.x, c1.z - 0.25f, c1.x, c1.z, y + 0.02f, borderColor);
            props.AddFloor(c0.x, c0.z, c0.x + 0.25f, c1.z, y + 0.02f, borderColor);
            props.AddFloor(c1.x - 0.25f, c0.z, c1.x, c1.z, y + 0.02f, borderColor);

            // ---- узнаваемая начинка комнаты ----
            var glow = new MeshBuilder();
            var glowColor = Color.Lerp(Art.Accent, area.Tint * 3f, 0.25f);
            bool landmark = BuildLandmark(area, props, glow, propColor, y);

            // crates / machinery hugging the walls
            int crates = landmark ? rng.Range(1, 4) : rng.Range(3, 7);
            for (int i = 0; i < crates; i++)
            {
                bool alongX = rng.Chance(0.5f);
                float px, pz;
                if (alongX)
                {
                    px = rng.Range(area.Rect.xMin + 2f, area.Rect.xMax - 2f);
                    pz = rng.Chance(0.5f) ? area.Rect.yMin + 1.2f : area.Rect.yMax - 1.2f;
                }
                else
                {
                    px = rng.Chance(0.5f) ? area.Rect.xMin + 1.2f : area.Rect.xMax - 1.2f;
                    pz = rng.Range(area.Rect.yMin + 2f, area.Rect.yMax - 2f);
                }
                var p = StationLayout.CellToWorld(area.Deck, px, pz);
                float sx = rng.Range(0.9f, 1.9f), sz = rng.Range(0.9f, 1.9f), sy = rng.Range(0.5f, 1.25f);
                props.AddBox(new Vector3(p.x, y + sy * 0.5f, p.z), new Vector3(sx, sy, sz), propColor);
            }

            MeshObject("Props", holder, props.Build("Props_" + area.Id), Art.Lit(propColor, 0.1f, 0.35f));

            // ---- wall consoles with emissive screens ----
            var screens = glow;
            var screenColor = glowColor;
            int consoles = Mathf.Clamp(area.Rect.width / 9, 1, 3);
            for (int i = 0; i < consoles; i++)
            {
                var p = Grid.StationPoint(area.Id, i * 2 + 1, out float yaw);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var fwd = rot * Vector3.forward;
                var right = rot * Vector3.right;
                var center = p + Vector3.up * 1.55f;
                var a = center - right * 0.85f - fwd * 0.02f;
                var b = center + right * 0.85f - fwd * 0.02f;
                screens.AddWall(a.x, a.z, b.x, b.z, center.y - 0.45f, 0.9f, screenColor);
            }
            var screenGo = MeshObject("Screens", holder, screens.Build("Screens_" + area.Id), Art.Lit(screenColor, 0f, 0.85f, 2.6f), false);
            rig.RegisterEmissive(screenGo.GetComponent<Renderer>(), screenColor);
        }


        /// <summary>
        /// Точки комнаты, которые декор обязан обходить: дверные проёмы, венты,
        /// лифты, консоли заданий и центр столовой с кнопкой сбора.
        /// Возвращает (x, z, радиус) в мировых координатах.
        /// </summary>
        private List<Vector3> CollectKeepOut(AreaDef area)
        {
            var list = new List<Vector3>(16);

            void Add(Vector3 world, float radius) => list.Add(new Vector3(world.x, world.z, radius));

            foreach (var other in StationLayout.Areas)
            {
                if (other.Type != AreaType.Doorway) continue;
                if (other.Deck != area.Deck) continue;
                if (other.OwnerRoom != area.Key) continue;
                Add(StationLayout.CellToWorld(area.Deck, other.CenterCell.x, other.CenterCell.y), 3.6f);
            }

            foreach (var vent in StationLayout.Vents)
            {
                if (vent.Deck != area.Deck || vent.RoomKey != area.Key) continue;
                Add(StationLayout.CellToWorld(area.Deck, vent.Cell), 2.6f);
            }

            foreach (var lift in StationLayout.Elevators)
            {
                if (lift.DeckA == area.Deck && area.Rect.Contains(lift.CellA))
                    Add(StationLayout.CellToWorld(area.Deck, lift.CellA), 3.2f);
                if (lift.DeckB == area.Deck && area.Rect.Contains(lift.CellB))
                    Add(StationLayout.CellToWorld(area.Deck, lift.CellB), 3.2f);
            }

            for (int slot = 0; slot < 8; slot++)
                Add(Grid.StationPoint(area.Id, slot, out _), 2.1f);

            // столовая: кнопка экстренного сбора и ноутбук старта в лобби
            if (area.Key == "cafeteria")
                Add(StationLayout.CellToWorld(area.Deck, area.CenterCell.x, area.CenterCell.y), 4.4f);

            return list;
        }

        // ------------------------------------------------------------------ landmarks
        /// <summary>
        /// Крупная узнаваемая начинка отсека. Раньше все комнаты были одинаковыми
        /// серыми коробками с ящиками вдоль стен — по картинке нельзя было понять,
        /// где ты. Теперь у каждого отсека свой силуэт: реактор с колонной, склад
        /// со стеллажами, оранжерея с грядками и так далее.
        /// Геометрия строго декоративная: коллайдеров нет, сетка проходимости
        /// не меняется, и NPC ходят ровно там же, где ходили.
        /// </summary>
        private bool BuildLandmark(AreaDef area, MeshBuilder solid, MeshBuilder glow, Color body, float y)
        {
            if (area.Type != AreaType.Room || string.IsNullOrEmpty(area.Key)) return false;

            // нормированные координаты: 0..1 внутри прямоугольника комнаты
            Vector3 P(float u, float v) => StationLayout.CellToWorld(
                area.Deck,
                area.Rect.xMin + area.Rect.width * u,
                area.Rect.yMin + area.Rect.height * v);

            float W = area.Rect.width * StationLayout.CellSize;
            float H = area.Rect.height * StationLayout.CellSize;
            float ceiling = y + StationLayout.WallHeight;
            var dark = Color.Lerp(body, Color.black, 0.25f);

            var keepOut = CollectKeepOut(area);

            // Декорация не должна вырастать в дверном проёме, на венте, на консоли
            // задания или на месте кнопки сбора — иначе персонаж проходит сквозь неё.
            bool Free(Vector3 p, float radius)
            {
                for (int i = 0; i < keepOut.Count; i++)
                {
                    var k = keepOut[i];
                    float dx = p.x - k.x, dz = p.z - k.y;
                    float rr = radius + k.z;
                    if (dx * dx + dz * dz < rr * rr) return false;
                }
                return true;
            }

            // — вспомогательные заготовки —
            void Box(float u, float v, float sx, float sy, float sz, Color c)
            {
                var p = P(u, v);
                if (!Free(p, Mathf.Max(sx, sz) * 0.5f)) return;
                solid.AddBox(new Vector3(p.x, y + sy * 0.5f, p.z), new Vector3(sx, sy, sz), c);
            }
            void Screen(float u, float v, float sx, float sy, float sz)
            {
                var p = P(u, v);
                if (!Free(p, Mathf.Max(sx, sz) * 0.5f)) return;
                glow.AddBox(new Vector3(p.x, y + sy * 0.5f + 0.9f, p.z), new Vector3(sx, sy, sz), Color.white);
            }
            void Pillar(float u, float v, float r, float h, Color c)
            {
                var p = P(u, v);
                if (!Free(p, r)) return;
                solid.AddCylinder(new Vector3(p.x, y, p.z), r, h, 10, c);
            }
            void Lamp(float u, float v, float r, float h)
            {
                var p = P(u, v);
                if (!Free(p, r)) return;
                glow.AddCylinder(new Vector3(p.x, y + 0.05f, p.z), r, h, 10, Color.white);
            }
            void Pipe(float u0, float v0, float h0, float u1, float v1, float h1, float t, Color c)
            {
                var a = P(u0, v0); var b = P(u1, v1);
                // трубу ниже роста тоже нельзя ставить в проходе, а под потолком — можно
                float low = Mathf.Min(h0, h1);
                if (low < 2.2f && (!Free(a, t) || !Free(b, t))) return;
                solid.AddBeam(new Vector3(a.x, y + h0, a.z), new Vector3(b.x, y + h1, b.z), t, c);
            }
            // ряд одинаковых блоков вдоль оси X
            void RowX(int n, float v, float sx, float sy, float sz, Color c)
            {
                for (int i = 0; i < n; i++) Box((i + 0.5f) / n, v, sx, sy, sz, c);
            }

            switch (area.Key)
            {
                case "reactor":
                    // колонна активной зоны с кольцами и стяжками к потолку
                    Pillar(0.5f, 0.5f, 2.4f, 2.6f, dark);
                    Lamp(0.5f, 0.5f, 1.7f, 3.1f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i / 6f * Mathf.PI * 2f;
                        float u = 0.5f + Mathf.Cos(a) * 0.32f, v = 0.5f + Mathf.Sin(a) * 0.32f;
                        Pillar(u, v, 0.55f, 2.2f, body);
                        Pipe(u, v, 2.2f, 0.5f, 0.5f, ceiling - y - 0.4f, 0.28f, dark);
                    }
                    return true;

                case "engines":
                    // две тяговые гондолы с соплами
                    for (int i = 0; i < 2; i++)
                    {
                        float v = i == 0 ? 0.26f : 0.74f;
                        Box(0.42f, v, W * 0.46f, 2.6f, 3.4f, body);
                        Box(0.72f, v, W * 0.12f, 1.9f, 2.2f, dark);
                        Screen(0.18f, v, 1.4f, 1.1f, 0.3f);
                        Lamp(0.80f, v, 0.9f, 1.6f);
                    }
                    Pipe(0.05f, 0.26f, 2.6f, 0.05f, 0.74f, 2.6f, 0.35f, dark);
                    return true;

                case "medbay":
                    // три койки и сканирующее кольцо
                    for (int i = 0; i < 3; i++)
                    {
                        float v = 0.22f + i * 0.28f;
                        Box(0.30f, v, 2.6f, 0.8f, 1.3f, body);
                        Box(0.30f, v, 2.7f, 0.12f, 1.5f, dark);
                    }
                    Pillar(0.74f, 0.5f, 1.7f, 0.25f, dark);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i / 3f * Mathf.PI * 2f;
                        Pipe(0.74f + Mathf.Cos(a) * 0.08f, 0.5f + Mathf.Sin(a) * 0.12f, 0.25f,
                             0.74f, 0.5f, 2.8f, 0.22f, body);
                    }
                    Lamp(0.74f, 0.5f, 1.4f, 0.16f);
                    return true;

                case "lab":
                case "archive":
                    // ряды столов / стеллажей с подсветкой полок
                    for (int i = 0; i < 3; i++)
                    {
                        float v = 0.22f + i * 0.28f;
                        Box(0.5f, v, W * 0.62f, 1.0f, 1.2f, body);
                        Screen(0.5f, v, W * 0.58f, 0.14f, 0.9f);
                    }
                    return true;

                case "hydro":
                case "filtration":
                    // грядки и лампы досветки
                    for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        float u = 0.26f + i * 0.24f, v = 0.3f + j * 0.4f;
                        Box(u, v, 3.4f, 0.9f, 3.0f, body);
                        Pillar(u, v, 0.7f, 1.8f, new Color(0.24f, 0.45f, 0.22f));
                        Lamp(u, v, 0.5f, 0.12f);
                    }
                    return true;

                case "comms":
                case "relay":
                    // тарелка на поворотном основании
                    Pillar(0.5f, 0.45f, 1.5f, 1.1f, dark);
                    Pipe(0.5f, 0.45f, 1.1f, 0.5f, 0.62f, 2.6f, 0.5f, body);
                    var dish = P(0.5f, 0.62f);
                    glow.AddCylinder(new Vector3(dish.x, y + 2.6f, dish.z), 2.2f, 0.22f, 12, Color.white);
                    RowX(3, 0.12f, 1.6f, 1.4f, 1.0f, body);
                    return true;

                case "command":
                    // дугой стоящие пульты и большой экран у стены
                    for (int i = 0; i < 5; i++)
                    {
                        float t = (i + 0.5f) / 5f;
                        float u = 0.22f + t * 0.56f;
                        float v = 0.42f - Mathf.Sin(t * Mathf.PI) * 0.14f;
                        Box(u, v, 2.2f, 1.1f, 1.4f, body);
                        Screen(u, v, 1.9f, 0.9f, 0.25f);
                    }
                    Screen(0.5f, 0.9f, W * 0.5f, 2.0f, 0.3f);
                    return true;

                case "security":
                    // стена мониторов и кресло оператора
                    for (int i = 0; i < 4; i++)
                    {
                        float u = 0.2f + i * 0.2f;
                        Screen(u, 0.86f, 2.0f, 1.4f, 0.25f);
                    }
                    Box(0.5f, 0.68f, W * 0.55f, 1.0f, 1.2f, body);
                    Box(0.5f, 0.5f, 1.1f, 1.3f, 1.1f, dark);
                    return true;

                case "cafeteria":
                    // столы кольцом вокруг кнопки экстренного сбора
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i / 6f * Mathf.PI * 2f + 0.4f;
                        float u = 0.5f + Mathf.Cos(a) * 0.30f;
                        float v = 0.5f + Mathf.Sin(a) * 0.30f;
                        Pillar(u, v, 1.5f, 0.95f, body);
                        for (int k = 0; k < 3; k++)
                        {
                            float b = k / 3f * Mathf.PI * 2f;
                            Pillar(u + Mathf.Cos(b) * 0.075f, v + Mathf.Sin(b) * 0.105f, 0.42f, 0.55f, dark);
                        }
                    }
                    return true;

                case "quarters":
                    // двухъярусные койки вдоль обеих стен
                    for (int i = 0; i < 4; i++)
                    {
                        float u = 0.16f + i * 0.23f;
                        Box(u, 0.14f, 2.0f, 0.75f, 3.0f, body);
                        Box(u, 0.14f, 1.9f, 0.30f, 2.9f, dark);
                        Box(u, 0.86f, 2.0f, 0.75f, 3.0f, body);
                        Box(u, 0.86f, 1.9f, 0.30f, 2.9f, dark);
                    }
                    return true;

                case "observation":
                    // телескоп у панорамного окна
                    Pillar(0.5f, 0.36f, 1.3f, 0.9f, dark);
                    Pipe(0.5f, 0.36f, 0.9f, 0.5f, 0.72f, 3.0f, 0.85f, body);
                    Screen(0.5f, 0.94f, W * 0.6f, 1.8f, 0.2f);
                    RowX(2, 0.16f, 2.4f, 0.8f, 1.2f, body);
                    return true;

                case "electrical":
                case "battery":
                    // шкафы автоматики и висящие жгуты
                    for (int i = 0; i < 4; i++)
                    {
                        float u = 0.16f + i * 0.23f;
                        Box(u, 0.8f, 2.0f, 2.4f, 1.1f, body);
                        Screen(u, 0.8f, 1.6f, 1.0f, 0.2f);
                        Pipe(u, 0.8f, 2.4f, u, 0.55f, 1.9f, 0.18f, dark);
                    }
                    RowX(3, 0.24f, 1.8f, 1.2f, 1.6f, dark);
                    return true;

                case "storage":
                case "cargo":
                    // стеллажи в два ряда и штабель контейнеров
                    for (int i = 0; i < 4; i++)
                    {
                        float u = 0.16f + i * 0.23f;
                        Box(u, 0.24f, 2.2f, 2.6f, 1.4f, body);
                        Box(u, 0.76f, 2.2f, 2.6f, 1.4f, body);
                    }
                    Box(0.5f, 0.5f, 3.0f, 1.6f, 3.0f, dark);
                    Box(0.5f, 0.5f, 2.2f, 2.6f, 2.2f, body);
                    return true;

                case "airlock":
                case "dronebay":
                    // шлюзовые ворота и разметка площадки
                    Screen(0.5f, 0.95f, W * 0.42f, 2.4f, 0.25f);
                    Pillar(0.28f, 0.4f, 1.6f, 0.2f, dark);
                    Pillar(0.72f, 0.4f, 1.6f, 0.2f, dark);
                    Box(0.28f, 0.4f, 1.6f, 0.7f, 2.2f, body);
                    Box(0.72f, 0.4f, 1.6f, 0.7f, 2.2f, body);
                    Pipe(0.1f, 0.95f, 2.6f, 0.9f, 0.95f, 2.6f, 0.4f, dark);
                    return true;

                case "lifesupport":
                case "water":
                case "coolant":
                    // баллоны и обвязка труб
                    for (int i = 0; i < 3; i++)
                    {
                        float u = 0.24f + i * 0.26f;
                        Pillar(u, 0.62f, 1.5f, 2.7f, body);
                        Lamp(u, 0.62f, 1.1f, 0.14f);
                        Pipe(u, 0.62f, 2.7f, u, 0.24f, 1.4f, 0.3f, dark);
                    }
                    Pipe(0.14f, 0.24f, 1.4f, 0.86f, 0.24f, 1.4f, 0.34f, dark);
                    return true;

                case "servers":
                    // ряды стоек с моргающими панелями
                    for (int i = 0; i < 5; i++)
                    {
                        float u = 0.14f + i * 0.18f;
                        Box(u, 0.35f, 1.5f, 2.5f, H * 0.34f, dark);
                        Screen(u, 0.35f, 1.1f, 1.6f, 0.2f);
                        Box(u, 0.72f, 1.5f, 2.5f, H * 0.24f, dark);
                    }
                    return true;

                case "maintenance":
                case "reprocessing":
                    // магистральные трубы под потолком и вентили
                    for (int i = 0; i < 3; i++)
                    {
                        float v = 0.24f + i * 0.26f;
                        Pipe(0.06f, v, 2.6f, 0.94f, v, 2.6f, 0.45f, dark);
                        Pipe(0.3f + i * 0.2f, v, 2.6f, 0.3f + i * 0.2f, v, 1.1f, 0.3f, body);
                        Box(0.3f + i * 0.2f, v, 1.6f, 1.1f, 1.6f, body);
                    }
                    return true;

                case "armory":
                case "scrap":
                    // оружейные шкафы / стойки с разобранным железом
                    for (int i = 0; i < 5; i++)
                    {
                        float u = (i + 0.5f) / 5f;
                        Box(u, 0.8f, 1.7f, 2.3f, 1.0f, body);
                        Screen(u, 0.8f, 1.2f, 0.5f, 0.2f);
                    }
                    Box(0.5f, 0.3f, W * 0.4f, 1.0f, 1.6f, dark);
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ doors
        private void BuildDoors()
        {
            var root = Child("Doors", _root).transform;
            foreach (var area in StationLayout.Areas)
            {
                if (area.Type != AreaType.Doorway || !area.Closable) continue;

                var center = StationLayout.CellToWorld(area.Deck, area.CenterCell.x, area.CenterCell.y);
                bool alongX = area.Rect.width >= area.Rect.height;
                float span = (alongX ? area.Rect.width : area.Rect.height) * StationLayout.CellSize;

                var go = Child("Door_" + area.OwnerRoom + "_" + area.Id, root);
                go.transform.position = center;
                go.transform.rotation = Quaternion.Euler(0f, alongX ? 0f : 90f, 0f);

                var leafA = MakeDoorLeaf(go.transform, -1f, span);
                var leafB = MakeDoorLeaf(go.transform, 1f, span);

                var dc = go.AddComponent<DoorController>();
                dc.Init(area, leafA, leafB);
                Doors.Add(dc);
            }
        }

        private Transform MakeDoorLeaf(Transform parent, float side, float span)
        {
            var go = Child(side < 0 ? "LeafL" : "LeafR", parent);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = Art.Cube;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Art.Lit(new Color(0.72f, 0.36f, 0.18f), 0.4f, 0.55f, 0.35f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(span * 0.5f + 0.1f, StationLayout.WallHeight * 0.92f, 0.35f);
            go.transform.localPosition = new Vector3(side * (span * 0.25f), StationLayout.WallHeight * 0.46f, 0f);
            // start open (slid away)
            go.transform.localPosition += new Vector3(side * 1.9f, 0f, 0f);
            return go.transform;
        }

        // ------------------------------------------------------------------ vents
        private void BuildVents()
        {
            var root = Child("Vents", _root).transform;
            foreach (var def in StationLayout.Vents)
            {
                var pos = StationLayout.CellToWorld(def.Deck, def.Cell);
                var go = Child("Vent_" + def.Id, root);
                go.transform.position = pos;

                var frame = Child("Frame", go.transform);
                var fmf = frame.AddComponent<MeshFilter>();
                fmf.sharedMesh = Art.Cube;
                var fmr = frame.AddComponent<MeshRenderer>();
                fmr.sharedMaterial = Art.Lit(new Color(0.16f, 0.18f, 0.22f), 0.5f, 0.4f);
                fmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                frame.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                frame.transform.localScale = new Vector3(1.9f, 0.12f, 1.9f);

                var lid = Child("Lid", go.transform);
                var lmf = lid.AddComponent<MeshFilter>();
                lmf.sharedMesh = Art.Cube;
                var lmr = lid.AddComponent<MeshRenderer>();
                lmr.sharedMaterial = Art.Lit(new Color(0.42f, 0.46f, 0.52f), 0.7f, 0.5f);
                lmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lid.transform.localPosition = new Vector3(0f, 0.16f, -0.85f);
                lid.transform.localScale = new Vector3(1.7f, 0.1f, 1.7f);

                var vp = go.AddComponent<VentPoint>();
                vp.Init(def, lid.transform);
                Vents.Add(vp);

                if (!_ventNetworks.TryGetValue(def.Network, out var list))
                {
                    list = new List<VentPoint>();
                    _ventNetworks[def.Network] = list;
                }
                list.Add(vp);
            }
        }

        public List<VentPoint> VentsInNetwork(int network)
        {
            return _ventNetworks.TryGetValue(network, out var l) ? l : new List<VentPoint>();
        }

        public VentPoint NearestVent(Vector3 pos, DeckId deck, float maxDistance)
        {
            VentPoint best = null;
            float bestD = maxDistance * maxDistance;
            foreach (var v in Vents)
            {
                if (v.Def.Deck != deck) continue;
                float d = (v.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = v; }
            }
            return best;
        }

        public VentPoint VentById(int id)
        {
            foreach (var v in Vents) if (v.Def.Id == id) return v;
            return null;
        }

        // ------------------------------------------------------------------ elevators
        private void BuildElevators()
        {
            var root = Child("Elevators", _root).transform;
            foreach (var def in StationLayout.Elevators)
            {
                Elevators.Add(MakePad(root, def, true));
                Elevators.Add(MakePad(root, def, false));
            }
        }

        private ElevatorPad MakePad(Transform root, ElevatorDef def, bool sideA)
        {
            var deck = sideA ? def.DeckA : def.DeckB;
            var cell = sideA ? def.CellA : def.CellB;
            var pos = StationLayout.CellToWorld(deck, cell);

            var go = Child("Elevator_" + def.Id + (sideA ? "_A" : "_B"), root);
            go.transform.position = pos;

            var disc = Child("Pad", go.transform);
            var mf = disc.AddComponent<MeshFilter>();
            mf.sharedMesh = Art.Cylinder;
            var mr = disc.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Art.Lit(new Color(0.25f, 0.65f, 0.85f), 0.2f, 0.7f, 1.6f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            disc.transform.localScale = new Vector3(3.4f, 0.06f, 3.4f);
            disc.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            var pad = go.AddComponent<ElevatorPad>();
            pad.Def = def;
            pad.IsSideA = sideA;
            return pad;
        }

        // ------------------------------------------------------------------ cameras
        private void BuildCameras()
        {
            var root = Child("SecurityCameras", _root).transform;
            foreach (var def in StationLayout.Cameras)
            {
                var room = StationLayout.Get(def.RoomKey);
                if (room == null) continue;
                var pos = StationLayout.CellToWorld(room.Deck, def.Cell) + Vector3.up * 5.5f;

                var go = Child("Cam_" + def.RoomKey, root);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(58f, def.Yaw, 0f);

                var body = Child("Body", go.transform);
                var mf = body.AddComponent<MeshFilter>();
                mf.sharedMesh = Art.Cube;
                var mr = body.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Art.Lit(new Color(0.2f, 0.22f, 0.26f), 0.4f, 0.4f);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                body.transform.localScale = new Vector3(0.7f, 0.4f, 1.1f);

                var led = Child("Led", go.transform);
                var lmf = led.AddComponent<MeshFilter>();
                lmf.sharedMesh = Art.Sphere;
                var lmr = led.AddComponent<MeshRenderer>();
                lmr.sharedMaterial = new Material(Art.Lit(new Color(0.2f, 0.05f, 0.05f), 0f, 0.5f, 1.5f));
                lmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                led.transform.localScale = Vector3.one * 0.28f;
                led.transform.localPosition = new Vector3(0f, 0f, 0.6f);

                var camGo = Child("Render", go.transform);
                camGo.transform.localPosition = Vector3.zero;
                camGo.transform.localRotation = Quaternion.identity;
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 62f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 60f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.02f, 0.03f, 0.05f);
                cam.depth = -5;

                var rt = new RenderTexture(320, 180, 16, RenderTextureFormat.Default)
                {
                    name = "CamRT_" + def.RoomKey,
                    filterMode = FilterMode.Bilinear,
                    antiAliasing = 1,
                };
                cam.targetTexture = rt;

                var unit = go.AddComponent<SecurityCameraUnit>();
                unit.Init(def, cam, rt, led.transform);
                Cameras.Add(unit);
            }
        }

        // ------------------------------------------------------------------ spawns
        /// <summary>
        /// Физическая кнопка сбора посреди столовой. Собрание и раньше можно было
        /// созвать только отсюда, но узнать об этом было неоткуда — кнопка на
        /// экране просто оставалась серой.
        /// </summary>
        private void BuildEmergencyButton(Vector3 centre)
        {
            var holder = Child("EmergencyButton", transform).transform;
            // ровно в центре столовой в лобби стоит ноутбук старта — ставим кнопку
            // рядом на том же столе, иначе две модели прорастают друг в друга
            holder.position = centre + new Vector3(0f, 0f, 1.9f);

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(pedestal.GetComponent<Collider>());
            pedestal.name = "Pedestal";
            pedestal.transform.SetParent(holder, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            pedestal.transform.localScale = new Vector3(1.7f, 0.5f, 1.7f);
            pedestal.GetComponent<Renderer>().sharedMaterial =
                Art.Lit(new Color(0.20f, 0.23f, 0.30f), 0.15f, 0.4f);

            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dome.GetComponent<Collider>());
            dome.name = "Dome";
            dome.transform.SetParent(holder, false);
            dome.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            dome.transform.localScale = new Vector3(1.1f, 0.7f, 1.1f);
            dome.GetComponent<Renderer>().sharedMaterial =
                Art.Lit(new Color(0.86f, 0.20f, 0.22f), 0f, 0.6f, 1.4f);
        }

        private void BuildSpawnPoints()
        {
            var cafeteria = StationLayout.Get("cafeteria");
            if (cafeteria == null) return;
            MeetingCenter = StationLayout.CellToWorld(cafeteria.Deck, cafeteria.CenterCell.x, cafeteria.CenterCell.y);

            BuildEmergencyButton(MeetingCenter);

            for (int i = 0; i < 15; i++)
            {
                float angle = i / 15f * Mathf.PI * 2f;
                var p = MeetingCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 7.2f;
                SpawnPoints.Add(Grid.NearestWalkable(DeckId.Upper, p));
            }

            // decorative meeting table
            var table = Child("MeetingTable", _root);
            table.transform.position = MeetingCenter;
            var mf = table.AddComponent<MeshFilter>();
            mf.sharedMesh = Art.Cylinder;
            var mr = table.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Art.Lit(new Color(0.30f, 0.34f, 0.42f), 0.3f, 0.5f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            table.transform.localScale = new Vector3(6.5f, 0.35f, 6.5f);
            table.transform.position += Vector3.up * 0.3f;
        }

        // ------------------------------------------------------------------ backdrop
        private void BuildBackdrop()
        {
            var go = Child("SpaceBackdrop", _root);
            go.transform.position = new Vector3(0f, -140f, 0f);
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = Vector3.one * 900f;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = Art.Quad;
            var mr = go.AddComponent<MeshRenderer>();
            // клонируем образец, а не создаём из шейдера: иначе нужные варианты
            // шейдера не попадут в сборку плеера
            var mat = new Material(Art.UnlitTemplate) { name = "NB_Space" };
            var tex = Art.StarField(512, 90210);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            Art.SetColor(mat, Color.white);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ------------------------------------------------------------------ services
        public void SetAllLights(float level)
        {
            foreach (var kv in RoomLights) kv.Value.SetLevel(level);
        }

        public void CloseDoorsOfRoom(int roomId, float duration)
        {
            foreach (var d in Doors)
                if (d.OwnerRoomId == roomId) d.Close(duration);
        }

        public void OpenAllDoors()
        {
            foreach (var d in Doors) d.ForceOpen();
        }

        public DoorController NearestDoor(Vector3 pos, float maxDistance)
        {
            DoorController best = null;
            float bestD = maxDistance * maxDistance;
            foreach (var d in Doors)
            {
                float dist = (d.transform.position - pos).sqrMagnitude;
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }

        public ElevatorPad NearestElevator(Vector3 pos, DeckId deck, float maxDistance)
        {
            ElevatorPad best = null;
            float bestD = maxDistance * maxDistance;
            foreach (var e in Elevators)
            {
                var padDeck = e.IsSideA ? e.Def.DeckA : e.Def.DeckB;
                if (padDeck != deck) continue;
                float d = (e.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }
    }
}
