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
        private void BuildRoomDressing(AreaDef area, Transform deckRoot)
        {
            var holder = Child("Room_" + (area.Key ?? area.Id.ToString()), deckRoot).transform;
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

            // crates / machinery hugging the walls
            int crates = rng.Range(3, 7);
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
            var screens = new MeshBuilder();
            var screenColor = Color.Lerp(Art.Accent, area.Tint * 3f, 0.25f);
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
        private void BuildSpawnPoints()
        {
            var cafeteria = StationLayout.Get("cafeteria");
            if (cafeteria == null) return;
            MeetingCenter = StationLayout.CellToWorld(cafeteria.Deck, cafeteria.CenterCell.x, cafeteria.CenterCell.y);

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
