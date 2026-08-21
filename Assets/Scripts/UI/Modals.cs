// -----------------------------------------------------------------------------
//  NEBULA NINE - modal panels: task console, sabotage menu, security cameras,
//  admin table and the full screen map.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Gameplay;
using Nebula.Map;
using Nebula.Tasks;

namespace Nebula.UI
{
    public abstract class ModalBase : MonoBehaviour
    {
        protected RectTransform Root;
        protected RectTransform Panel;
        protected Text TitleLabel;
        public bool IsOpen => gameObject.activeSelf;

        protected void BuildFrame(Transform parent, string title, Vector2 size, System.Action onClose)
        {
            Root = UIKit.Stretch(parent, GetType().Name);
            var dim = UIKit.PanelStretch(Root, "Dim", new Color(0f, 0f, 0f, 0.72f), 0);
            dim.raycastTarget = true;

            Panel = UIKit.Node(Root, "Panel", Vector2.zero, size);
            UIKit.PanelStretch(Panel, "Bg", new Color(0.08f, 0.10f, 0.15f, 0.99f), 22);
            TitleLabel = UIKit.Label(Panel, title, new Vector2(0f, size.y * 0.5f - 44f), new Vector2(size.x - 180f, 52f), 32,
                TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);
            UIKit.Button(Panel, "ЗАКРЫТЬ", new Vector2(size.x * 0.5f - 92f, size.y * 0.5f - 44f), new Vector2(150f, 62f),
                onClose, new Color(0.35f, 0.16f, 0.18f, 0.95f), 22, 14);
        }

        public virtual void Close()
        {
            gameObject.SetActive(false);
        }
    }

    // ================================================================= task console
    public class TaskWindow : ModalBase
    {
        private RectTransform _content;
        private Text _hint;
        private MiniGameBase _game;
        private TaskInstance _task;
        private PlayerController _player;
        private NebulaRandom _rng;
        private bool _finished;

        public static TaskWindow Create(Transform parent, PlayerController player)
        {
            var go = new GameObject("TaskWindow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var win = go.AddComponent<TaskWindow>();
            win._player = player;
            win._rng = new NebulaRandom(Random.Range(1, int.MaxValue));
            win.Build();
            go.SetActive(false);
            return win;
        }

        private void Build()
        {
            BuildFrame(transform, "", new Vector2(1180f, 700f), Close);
            _hint = UIKit.Label(Panel, "", new Vector2(0f, 290f), new Vector2(900f, 40f), 24,
                TextAnchor.MiddleCenter, Art.TextDim);
            _content = UIKit.Node(Panel, "Content", new Vector2(0f, -30f), new Vector2(1080f, 560f));
        }

        public void Open(TaskInstance task)
        {
            if (task == null || task.IsComplete) return;
            _task = task;
            _finished = false;
            gameObject.SetActive(true);

            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);

            _game = task.Definition.Create != null ? task.Definition.Create() : null;
            if (_game == null) { Close(); return; }

            _game.Setup(_content, task, task.StagesCompleted, _rng, OnGameComplete);
            _hint.text = task.CurrentHint + "  —  " + _game.Instruction;
            if (TitleLabel != null) TitleLabel.text = task.Definition.Title;
        }

        private void OnGameComplete()
        {
            if (_finished) return;
            _finished = true;
            _player.CompleteTaskStage(_task);
            Close();
        }

        private void Update()
        {
            if (_game == null || _finished) return;
            _game.Tick(Time.unscaledDeltaTime);
        }

        public override void Close()
        {
            _game?.Dispose();
            _game = null;
            _task = null;
            base.Close();
        }
    }

    // ================================================================= sabotage
    public class SabotageMenu : ModalBase
    {
        private MatchManager _match;
        private RectTransform _list;
        private RectTransform _doorList;

        public static SabotageMenu Create(Transform parent, MatchManager match)
        {
            var go = new GameObject("SabotageMenu", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var menu = go.AddComponent<SabotageMenu>();
            menu._match = match;
            menu.BuildUi();
            go.SetActive(false);
            return menu;
        }

        private void BuildUi()
        {
            BuildFrame(transform, "САБОТАЖ", new Vector2(1000f, 620f), Close);
            _list = UIKit.Node(Panel, "List", new Vector2(0f, -10f), new Vector2(940f, 460f));
            _doorList = UIKit.Node(Panel, "Doors", new Vector2(0f, -10f), new Vector2(940f, 460f));
            _doorList.gameObject.SetActive(false);
        }

        public void Open()
        {
            gameObject.SetActive(true);
            _doorList.gameObject.SetActive(false);
            _list.gameObject.SetActive(true);
            Rebuild();
        }

        private void Rebuild()
        {
            for (int i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            var options = _match.Sabotage.AvailableSabotages();

            if (options.Count == 0)
            {
                UIKit.Label(_list, "Саботаж перезаряжается...", Vector2.zero, new Vector2(900f, 60f), 26,
                    TextAnchor.MiddleCenter, Art.TextDim);
                return;
            }

            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                int col = i % 2, row = i / 2;
                var pos = new Vector2(-230f + col * 460f, 170f - row * 110f);
                UIKit.Button(_list, _match.Sabotage.DisplayName(option), pos, new Vector2(430f, 92f), () =>
                {
                    if (option == SabotageType.Doors) ShowDoorPicker();
                    else
                    {
                        if (PlayerController.Instance != null) PlayerController.Instance.TriggerSabotage(option, -1);
                        else _match.Sabotage.Trigger(option);
                        Close();
                    }
                }, new Color(0.36f, 0.14f, 0.28f, 0.95f), 24, 16);
            }
        }

        private void ShowDoorPicker()
        {
            _list.gameObject.SetActive(false);
            _doorList.gameObject.SetActive(true);
            for (int i = _doorList.childCount - 1; i >= 0; i--) Destroy(_doorList.GetChild(i).gameObject);

            var rooms = new List<AreaDef>();
            foreach (var door in StationView.Instance.Doors)
            {
                var owner = StationLayout.Get(door.OwnerRoomId);
                if (owner != null && !rooms.Contains(owner)) rooms.Add(owner);
            }

            for (int i = 0; i < rooms.Count && i < 15; i++)
            {
                var room = rooms[i];
                int col = i % 3, row = i / 3;
                var pos = new Vector2(-300f + col * 300f, 180f - row * 84f);
                UIKit.Button(_doorList, room.Name, pos, new Vector2(280f, 70f), () =>
                {
                    if (PlayerController.Instance != null) PlayerController.Instance.TriggerSabotage(SabotageType.Doors, room.Id);
                    else _match.Sabotage.Trigger(SabotageType.Doors, room.Id);
                    Close();
                }, new Color(0.30f, 0.20f, 0.14f, 0.95f), 20, 12);
            }
        }
    }

    // ================================================================= cameras
    public class CamerasView : ModalBase
    {
        private const int FeedsPerPage = 4;

        private readonly List<RawImage> _feeds = new List<RawImage>();
        private readonly List<Text> _feedNames = new List<Text>();
        private readonly List<SecurityCameraUnit> _live = new List<SecurityCameraUnit>();
        private MatchManager _match;
        private Text _pageLabel;
        private int _page;

        public static CamerasView Create(Transform parent, MatchManager match)
        {
            var go = new GameObject("CamerasView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<CamerasView>();
            view._match = match;
            view.BuildUi();
            go.SetActive(false);
            return view;
        }

        private void BuildUi()
        {
            BuildFrame(transform, "ПОСТ НАБЛЮДЕНИЯ", new Vector2(1180f, 760f), Close);

            for (int i = 0; i < FeedsPerPage; i++)
            {
                int col = i % 2, row = i / 2;
                var pos = new Vector2(-270f + col * 540f, 160f - row * 280f);
                var frame = UIKit.Panel(Panel, "Feed" + i, pos, new Vector2(520f, 260f), new Color(0.02f, 0.03f, 0.05f, 1f), 10);

                var rt = UIKit.Node(frame.transform, "Raw", Vector2.zero, new Vector2(510f, 250f));
                var raw = rt.gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                _feeds.Add(raw);

                _feedNames.Add(UIKit.Label(frame.transform, "", new Vector2(0f, -108f),
                    new Vector2(500f, 34f), 20, TextAnchor.MiddleCenter, Art.Accent));
            }

            // страницы: на станции камер больше, чем экранов на посту
            UIKit.Button(Panel, "<", new Vector2(-250f, -300f), new Vector2(110f, 66f), () => Flip(-1),
                new Color(0.16f, 0.22f, 0.30f, 0.95f), 30, 12);
            _pageLabel = UIKit.Label(Panel, "", new Vector2(0f, -300f), new Vector2(320f, 40f), 22,
                TextAnchor.MiddleCenter, Art.TextDim);
            UIKit.Button(Panel, ">", new Vector2(250f, -300f), new Vector2(110f, 66f), () => Flip(1),
                new Color(0.16f, 0.22f, 0.30f, 0.95f), 30, 12);
        }

        private static List<SecurityCameraUnit> AllCameras()
        {
            return StationView.Instance != null ? StationView.Instance.Cameras : null;
        }

        private int PageCount()
        {
            var all = AllCameras();
            int count = all != null ? all.Count : 0;
            return Mathf.Max(1, Mathf.CeilToInt(count / (float)FeedsPerPage));
        }

        private void Flip(int delta)
        {
            int pages = PageCount();
            _page = ((_page + delta) % pages + pages) % pages;
            ApplyPage();
        }

        /// <summary>Привязывает четыре экрана к камерам текущей страницы.</summary>
        private void ApplyPage()
        {
            var all = AllCameras();
            _live.Clear();

            for (int i = 0; i < _feeds.Count; i++)
            {
                int index = _page * FeedsPerPage + i;
                bool has = all != null && index < all.Count;
                _feeds[i].enabled = has;
                _feeds[i].texture = has ? all[index].Target : null;
                _feedNames[i].text = has ? StationLayout.NameOf(all[index].RoomId) : "— нет сигнала —";
                if (has) _live.Add(all[index]);
            }

            if (_pageLabel != null) _pageLabel.text = "КАМЕРЫ " + (_page + 1) + " / " + PageCount();
            if (IsOpen) SetCameras(true);
        }

        public void Open()
        {
            gameObject.SetActive(true);
            if (_page >= PageCount()) _page = 0;
            ApplyPage();
            SetCameras(true);
        }

        public override void Close()
        {
            SetCameras(false);
            base.Close();
        }

        private void SetCameras(bool on)
        {
            SecurityCameraUnit.AnyoneWatching = on;
            var view = StationView.Instance;
            if (view == null) return;
            // питание дают только тем камерам, чью картинку панель действительно
            // показывает: остальные рендерили бы в текстуру, которую никто не видит
            foreach (var cam in view.Cameras) cam.SetActive(on && _live.Contains(cam));
        }

        private void Update()
        {
            // stepping away from the console closes the feed
            if (!IsOpen || _match == null || _match.Local == null) return;
            var security = StationLayout.Get("security");
            if (security != null && _match.Local.RoomId != security.Id) Close();
        }
    }

    // ================================================================= admin table
    public class AdminView : ModalBase
    {
        private MatchManager _match;
        private readonly List<Text> _counts = new List<Text>();
        private readonly List<AreaDef> _rooms = new List<AreaDef>();
        private float _timer;

        public static AdminView Create(Transform parent, MatchManager match)
        {
            var go = new GameObject("AdminView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<AdminView>();
            view._match = match;
            view.BuildUi();
            go.SetActive(false);
            return view;
        }

        private void BuildUi()
        {
            BuildFrame(transform, "АДМИН-КАРТА", new Vector2(1240f, 720f), Close);
            var mapHolder = UIKit.Node(Panel, "Map", new Vector2(0f, -20f), new Vector2(1150f, 600f));
            var minimap = MinimapView.Create(mapHolder, _match, Vector2.zero, new Vector2(1150f, 600f), true);

            foreach (var area in StationLayout.AllRooms())
            {
                _rooms.Add(area);
                float w = 1150f - 16f, h = 600f - 16f;
                var pos = new Vector2((area.CenterCell.x / StationLayout.GridW - 0.5f) * w,
                                      (area.CenterCell.y / StationLayout.GridH - 0.5f) * h);
                var label = UIKit.Label(mapHolder, "", pos + new Vector2(0f, 20f), new Vector2(90f, 34f), 26,
                    TextAnchor.MiddleCenter, Art.AccentWarm, FontStyle.Bold);
                _counts.Add(label);
            }
        }

        public void Open()
        {
            gameObject.SetActive(true);
            Refresh();
        }

        private void Update()
        {
            if (!IsOpen) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f) { _timer = 0.5f; Refresh(); }

            var command = StationLayout.Get("command");
            if (command != null && _match.Local != null && _match.Local.RoomId != command.Id) Close();
            if (_match.Sabotage != null && _match.Sabotage.CommsDown) Close();
        }

        private void Refresh()
        {
            for (int i = 0; i < _rooms.Count; i++)
            {
                int count = 0;
                var area = _rooms[i];
                foreach (var p in _match.Players)
                {
                    if (!p.IsAlive || p.InVent) continue;
                    if (p.Deck != area.Deck) continue;
                    if (p.RoomId == area.Id) count++;
                }
                _counts[i].text = count > 0 ? count.ToString() : "";
            }
        }
    }

    // ================================================================= big map
    public class BigMapView : ModalBase
    {
        public static BigMapView Create(Transform parent, MatchManager match)
        {
            var go = new GameObject("BigMapView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<BigMapView>();
            view.BuildFrame(view.transform, "КАРТА СТАНЦИИ", new Vector2(1300f, 760f), view.Close);
            MinimapView.Create(view.Panel, match, new Vector2(0f, -20f), new Vector2(1200f, 620f), true);
            go.SetActive(false);
            return view;
        }

        public void Open() => gameObject.SetActive(true);
    }
}
