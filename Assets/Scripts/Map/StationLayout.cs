// -----------------------------------------------------------------------------
//  NEBULA NINE - "Station N-9" layout description.
//
//  The whole station is described as axis aligned rectangles on a 1 m grid.
//  Rooms and corridors never touch each other directly: they are separated by a
//  one cell thick wall, and connectivity is created by explicit doorway
//  rectangles carved through those walls.  That gives us
//    * exact control over where doors are (and which ones the saboteur can shut),
//    * automatic wall geometry (any walkable cell facing a solid cell gets a wall),
//    * a free navigation grid and a free line-of-sight grid.
//
//  Two decks: the main deck (15 rooms, 2 ring corridors, 4 vertical arteries) and
//  a lower deck (6 rooms) reachable through two elevators.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;

namespace Nebula.Map
{
    public enum AreaType
    {
        Room = 0,
        Corridor = 1,
        Doorway = 2,
    }

    public class AreaDef
    {
        public int Id = -1;
        public string Key;
        public string Name;
        public DeckId Deck;
        public AreaType Type;
        public RectInt Rect;
        /// <summary>For doorways: the room whose door panel this is (door sabotage target).</summary>
        public string OwnerRoom;
        /// <summary>Doorway can be closed by the saboteur.</summary>
        public bool Closable;
        public Color Tint = new Color(0.22f, 0.26f, 0.34f);

        public Vector2 CenterCell => new Vector2(Rect.xMin + Rect.width * 0.5f, Rect.yMin + Rect.height * 0.5f);
        public bool IsRoom => Type == AreaType.Room;
    }

    public class VentDef
    {
        public int Id;
        public DeckId Deck;
        public Vector2Int Cell;
        public string RoomKey;
        public int Network;
    }

    public class ElevatorDef
    {
        public int Id;
        public string Name;
        public DeckId DeckA;
        public Vector2Int CellA;
        public DeckId DeckB;
        public Vector2Int CellB;
    }

    public class CameraDef
    {
        public string RoomKey;
        public Vector2Int Cell;
        public float Yaw;
    }

    public static class StationLayout
    {
        public const int GridW = 128;
        public const int GridH = 80;
        public const float CellSize = 1f;
        public const float DeckHeight = 40f;   // vertical separation between decks (world units)
        public const float WallHeight = 3.2f;

        public static readonly List<AreaDef> Areas = new List<AreaDef>();
        public static readonly List<VentDef> Vents = new List<VentDef>();
        public static readonly List<ElevatorDef> Elevators = new List<ElevatorDef>();
        public static readonly List<CameraDef> Cameras = new List<CameraDef>();
        private static readonly Dictionary<string, AreaDef> ByKey = new Dictionary<string, AreaDef>();

        // Palette used for floors and the minimap.
        private static readonly Color RoomTint = new Color(0.212f, 0.247f, 0.325f);
        private static readonly Color CorridorTint = new Color(0.157f, 0.180f, 0.243f);
        private static readonly Color HotTint = new Color(0.322f, 0.204f, 0.184f);
        private static readonly Color CoolTint = new Color(0.176f, 0.263f, 0.318f);
        private static readonly Color GreenTint = new Color(0.184f, 0.298f, 0.212f);

        static StationLayout()
        {
            BuildUpperDeck();
            BuildLowerDeck();
            BuildVents();
            BuildElevators();
            BuildCameras();

            for (int i = 0; i < Areas.Count; i++)
            {
                Areas[i].Id = i;
                if (!string.IsNullOrEmpty(Areas[i].Key) && !ByKey.ContainsKey(Areas[i].Key))
                    ByKey.Add(Areas[i].Key, Areas[i]);
            }
        }

        // ------------------------------------------------------------------
        private static AreaDef Room(string key, string name, DeckId deck, int x, int z, int w, int h, Color tint)
        {
            var a = new AreaDef
            {
                Key = key, Name = name, Deck = deck, Type = AreaType.Room,
                Rect = new RectInt(x, z, w, h), Tint = tint,
            };
            Areas.Add(a);
            return a;
        }

        private static AreaDef Corridor(string key, string name, DeckId deck, int x, int z, int w, int h)
        {
            var a = new AreaDef
            {
                Key = key, Name = name, Deck = deck, Type = AreaType.Corridor,
                Rect = new RectInt(x, z, w, h), Tint = CorridorTint,
            };
            Areas.Add(a);
            return a;
        }

        private static void Door(DeckId deck, int x, int z, int w, int h, string ownerRoom, bool closable = true)
        {
            Areas.Add(new AreaDef
            {
                Key = null,
                Name = null,
                Deck = deck,
                Type = AreaType.Doorway,
                Rect = new RectInt(x, z, w, h),
                OwnerRoom = ownerRoom,
                Closable = closable,
                Tint = CorridorTint,
            });
        }

        // ------------------------------------------------------------------ upper
        private static void BuildUpperDeck()
        {
            const DeckId D = DeckId.Upper;

            // --- north band (z 58..77) ---
            Room("reactor", "Реактор", D, 2, 58, 24, 20, HotTint);
            Room("medbay", "Медотсек", D, 27, 58, 18, 20, CoolTint);
            Room("lab", "Лаборатория", D, 52, 58, 28, 20, CoolTint);
            Room("hydro", "Оранжерея", D, 81, 58, 20, 20, GreenTint);
            Room("comms", "Связь", D, 108, 58, 18, 20, RoomTint);

            // --- middle band (z 31..50) ---
            Room("command", "Командный центр", D, 2, 31, 24, 20, RoomTint);
            Room("security", "Пост наблюдения", D, 27, 31, 18, 20, RoomTint);
            Room("cafeteria", "Столовая", D, 52, 31, 28, 20, RoomTint);
            Room("quarters", "Жилой сектор", D, 81, 31, 20, 20, RoomTint);
            Room("observation", "Обсерватория", D, 108, 31, 18, 20, CoolTint);

            // --- south band (z 4..23) ---
            Room("electrical", "Электрощитовая", D, 2, 4, 24, 20, HotTint);
            Room("storage", "Склад", D, 27, 4, 18, 20, RoomTint);
            Room("engines", "Двигательный отсек", D, 52, 4, 28, 20, HotTint);
            Room("airlock", "Грузовой шлюз", D, 81, 4, 20, 20, RoomTint);
            Room("lifesupport", "Жизнеобеспечение", D, 108, 4, 18, 20, GreenTint);

            // --- ring corridors ---
            Corridor("cor_north", "Северный коридор", D, 2, 52, 124, 5);
            Corridor("cor_south", "Южный коридор", D, 2, 25, 124, 5);

            // --- vertical arteries (west x46..50, east x102..106) ---
            Corridor("cor_nw", "Западный проход", D, 46, 57, 5, 21);
            Corridor("cor_w", "Западный проход", D, 46, 30, 5, 22);
            Corridor("cor_sw", "Западная шахта", D, 46, 4, 5, 21);
            Corridor("cor_ne", "Восточный проход", D, 102, 57, 5, 21);
            Corridor("cor_e", "Восточный проход", D, 102, 30, 5, 22);
            Corridor("cor_se", "Восточная шахта", D, 102, 4, 5, 21);

            // --- doorways: north band -> north corridor (wall row z = 57) ---
            Door(D, 12, 57, 4, 1, "reactor");
            Door(D, 33, 57, 4, 1, "medbay");
            Door(D, 62, 57, 4, 1, "lab");
            Door(D, 87, 57, 4, 1, "hydro");
            Door(D, 114, 57, 4, 1, "comms");

            // north band -> vertical arteries (wall columns x = 45/51/101/107)
            Door(D, 45, 64, 1, 4, "medbay");
            Door(D, 51, 64, 1, 4, "lab");
            Door(D, 101, 64, 1, 4, "hydro");
            Door(D, 107, 64, 1, 4, "comms");

            // direct room-to-room shortcuts on the north band
            Door(D, 26, 66, 1, 4, "medbay");
            Door(D, 80, 66, 1, 4, "hydro");

            // --- middle band -> north corridor (wall row z = 51) ---
            Door(D, 12, 51, 4, 1, "command");
            Door(D, 33, 51, 4, 1, "security");
            Door(D, 58, 51, 4, 1, "cafeteria");
            Door(D, 71, 51, 4, 1, "cafeteria");
            Door(D, 88, 51, 4, 1, "quarters");
            Door(D, 114, 51, 4, 1, "observation");

            // --- middle band -> south corridor (wall row z = 30) ---
            Door(D, 12, 30, 4, 1, "command");
            Door(D, 33, 30, 4, 1, "security");
            Door(D, 58, 30, 4, 1, "cafeteria");
            Door(D, 71, 30, 4, 1, "cafeteria");
            Door(D, 88, 30, 4, 1, "quarters");
            Door(D, 114, 30, 4, 1, "observation");

            // middle band -> vertical arteries
            Door(D, 45, 38, 1, 4, "security");
            Door(D, 51, 38, 1, 4, "cafeteria");
            Door(D, 101, 38, 1, 4, "quarters");
            Door(D, 107, 38, 1, 4, "observation");
            Door(D, 26, 40, 1, 4, "security");
            Door(D, 80, 40, 1, 4, "quarters");

            // --- south band -> south corridor (wall row z = 24) ---
            Door(D, 12, 24, 4, 1, "electrical");
            Door(D, 33, 24, 4, 1, "storage");
            Door(D, 58, 24, 4, 1, "engines");
            Door(D, 71, 24, 4, 1, "engines");
            Door(D, 88, 24, 4, 1, "airlock");
            Door(D, 114, 24, 4, 1, "lifesupport");

            // south band -> vertical arteries
            Door(D, 45, 12, 1, 4, "storage");
            Door(D, 51, 12, 1, 4, "engines");
            Door(D, 101, 12, 1, 4, "airlock");
            Door(D, 107, 12, 1, 4, "lifesupport");
            Door(D, 26, 14, 1, 4, "electrical");
            Door(D, 80, 14, 1, 4, "airlock");
        }

        // ------------------------------------------------------------------ lower
        private static void BuildLowerDeck()
        {
            const DeckId D = DeckId.Lower;

            Corridor("cor_lower", "Технический коридор", D, 18, 34, 92, 5);

            Room("cargo", "Трюм", D, 20, 40, 26, 20, RoomTint);
            Room("servers", "Серверная", D, 50, 40, 26, 20, CoolTint);
            Room("water", "Водоочистка", D, 80, 40, 26, 20, GreenTint);
            Room("coolant", "Холодильная установка", D, 20, 8, 26, 20, CoolTint);
            Room("maintenance", "Технический уровень", D, 50, 8, 26, 20, RoomTint);
            Room("dronebay", "Ангар дронов", D, 80, 8, 26, 20, HotTint);

            Door(D, 30, 39, 4, 1, "cargo");
            Door(D, 60, 39, 4, 1, "servers");
            Door(D, 90, 39, 4, 1, "water");
            // the south rooms sit six cells below the corridor, so their doorways
            // are short access tunnels rather than single wall openings
            Door(D, 30, 28, 4, 6, "coolant");
            Door(D, 60, 28, 4, 6, "maintenance");
            Door(D, 90, 28, 4, 6, "dronebay");
        }

        // ------------------------------------------------------------------ vents
        private static void BuildVents()
        {
            int id = 0;
            void V(DeckId d, int x, int z, string room, int net)
            {
                Vents.Add(new VentDef { Id = id++, Deck = d, Cell = new Vector2Int(x, z), RoomKey = room, Network = net });
            }

            // network 0 - west industrial loop
            V(DeckId.Upper, 8, 66, "reactor", 0);
            V(DeckId.Upper, 8, 12, "electrical", 0);
            V(DeckId.Upper, 41, 36, "security", 0);

            // network 1 - science loop
            V(DeckId.Upper, 41, 72, "medbay", 1);
            V(DeckId.Upper, 56, 72, "lab", 1);
            V(DeckId.Upper, 56, 34, "cafeteria", 1);

            // network 2 - cargo loop
            V(DeckId.Upper, 75, 8, "engines", 2);
            V(DeckId.Upper, 84, 8, "airlock", 2);
            V(DeckId.Upper, 30, 20, "storage", 2);

            // network 3 - east habitation loop
            V(DeckId.Upper, 97, 62, "hydro", 3);
            V(DeckId.Upper, 122, 62, "comms", 3);
            V(DeckId.Upper, 97, 46, "quarters", 3);
            V(DeckId.Upper, 122, 46, "observation", 3);

            // network 4 - lower deck loop
            V(DeckId.Lower, 24, 44, "cargo", 4);
            V(DeckId.Lower, 54, 44, "servers", 4);
            V(DeckId.Lower, 24, 12, "coolant", 4);
        }

        private static void BuildElevators()
        {
            Elevators.Add(new ElevatorDef
            {
                Id = 0, Name = "Западный лифт",
                DeckA = DeckId.Upper, CellA = new Vector2Int(48, 6),
                DeckB = DeckId.Lower, CellB = new Vector2Int(21, 36),
            });
            Elevators.Add(new ElevatorDef
            {
                Id = 1, Name = "Восточный лифт",
                DeckA = DeckId.Upper, CellA = new Vector2Int(104, 6),
                DeckB = DeckId.Lower, CellB = new Vector2Int(107, 36),
            });
        }

        private static void BuildCameras()
        {
            Cameras.Add(new CameraDef { RoomKey = "cafeteria", Cell = new Vector2Int(66, 48), Yaw = 180f });
            Cameras.Add(new CameraDef { RoomKey = "reactor", Cell = new Vector2Int(14, 75), Yaw = 180f });
            Cameras.Add(new CameraDef { RoomKey = "storage", Cell = new Vector2Int(36, 21), Yaw = 180f });
            Cameras.Add(new CameraDef { RoomKey = "engines", Cell = new Vector2Int(66, 21), Yaw = 180f });
        }

        // ------------------------------------------------------------------ api
        public static AreaDef Get(string key)
        {
            return key != null && ByKey.TryGetValue(key, out var a) ? a : null;
        }

        public static AreaDef Get(int id)
        {
            return id >= 0 && id < Areas.Count ? Areas[id] : null;
        }

        public static string NameOf(int id)
        {
            var a = Get(id);
            if (a == null) return "неизвестно";
            if (a.Type == AreaType.Doorway) return Get(a.OwnerRoom)?.Name ?? "переход";
            return a.Name;
        }

        /// <summary>Rooms only (no corridors, no doorways) - used by tasks and the AI.</summary>
        public static List<AreaDef> RoomsOfDeck(DeckId deck)
        {
            var list = new List<AreaDef>();
            foreach (var a in Areas)
                if (a.Deck == deck && a.Type == AreaType.Room) list.Add(a);
            return list;
        }

        public static List<AreaDef> AllRooms()
        {
            var list = new List<AreaDef>();
            foreach (var a in Areas)
                if (a.Type == AreaType.Room) list.Add(a);
            return list;
        }

        /// <summary>World position of a grid cell centre on the given deck.</summary>
        public static Vector3 CellToWorld(DeckId deck, float cellX, float cellZ)
        {
            return new Vector3(
                (cellX - GridW * 0.5f + 0.5f) * CellSize,
                deck == DeckId.Upper ? 0f : -DeckHeight,
                (cellZ - GridH * 0.5f + 0.5f) * CellSize);
        }

        public static Vector3 CellToWorld(DeckId deck, Vector2Int cell) => CellToWorld(deck, cell.x, cell.y);

        public static Vector2Int WorldToCell(Vector3 world)
        {
            return new Vector2Int(
                Mathf.FloorToInt(world.x / CellSize + GridW * 0.5f),
                Mathf.FloorToInt(world.z / CellSize + GridH * 0.5f));
        }

        public static DeckId DeckOfWorld(Vector3 world) => world.y < -DeckHeight * 0.5f ? DeckId.Lower : DeckId.Upper;

        public static Vector3 AreaCenterWorld(int areaId)
        {
            var a = Get(areaId);
            if (a == null) return Vector3.zero;
            return CellToWorld(a.Deck, a.CenterCell.x, a.CenterCell.y);
        }
    }
}
