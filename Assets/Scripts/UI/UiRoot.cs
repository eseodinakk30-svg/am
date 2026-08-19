// -----------------------------------------------------------------------------
//  NEBULA NINE - UI root: canvas, event system, screen switching.
//
//  Owns every screen and decides which one is visible for the current match
//  phase.  Also the place where a match is started, restarted and torn down.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Gameplay;
using Nebula.Map;
using Nebula.Net;

namespace Nebula.UI
{
    public class UiRoot : MonoBehaviour
    {
        public static UiRoot Instance { get; private set; }

        public Canvas Canvas { get; private set; }
        public RectTransform SafeArea { get; private set; }

        private MatchManager _match;
        private PlayerController _player;
        private CameraRig _camera;
        private NetworkService _net;
        private NetBridge _bridge;

        private MainMenuScreen _menu;
        private HudController _hud;
        private MeetingScreen _meeting;
        private GameOverScreen _gameOver;
        private TaskWindow _taskWindow;
        private SabotageMenu _sabotageMenu;
        private CamerasView _cameras;
        private AdminView _admin;
        private BigMapView _bigMap;
        private RectTransform _gameLayer;

        private bool _matchRunning;

        public static UiRoot Create(Transform parent, MatchManager match, PlayerController player,
            CameraRig camera, NetworkService net, NetBridge bridge)
        {
            var go = new GameObject("UiRoot");
            go.transform.SetParent(parent, false);
            var root = go.AddComponent<UiRoot>();
            root._match = match;
            root._player = player;
            root._camera = camera;
            root._net = net;
            root._bridge = bridge;
            root.Build();
            Instance = root;
            return root;
        }

        // ==================================================================
        private void Build()
        {
            // ---- canvas ----
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.pixelPerfect = false;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = UIKit.DesignResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // ---- event system ----
            if (EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.transform.SetParent(transform, false);
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }

            // ---- safe area ----
            SafeArea = UIKit.Stretch(canvasGo.transform, "SafeArea");
            SafeArea.gameObject.AddComponent<SafeAreaFitter>();

            // ---- layers ----
            _gameLayer = UIKit.Stretch(SafeArea, "GameLayer");
            _gameLayer.gameObject.SetActive(false);

            var vision = _match.gameObject.AddComponent<VisionController>();
            vision.Setup(_match, _gameLayer);

            _hud = HudController.Create(_gameLayer, _match, _player);
            _meeting = MeetingScreen.Create(_gameLayer, _match);
            _taskWindow = TaskWindow.Create(_gameLayer, _player);
            _sabotageMenu = SabotageMenu.Create(_gameLayer, _match);
            _cameras = CamerasView.Create(_gameLayer, _match);
            _admin = AdminView.Create(_gameLayer, _match);
            _bigMap = BigMapView.Create(_gameLayer, _match);

            _gameOver = GameOverScreen.Create(SafeArea, _match);
            _gameOver.OnPlayAgain = Restart;
            _gameOver.OnBackToMenu = BackToMenu;

            _menu = MainMenuScreen.Create(SafeArea);
            _menu.OnStartSolo = StartSolo;
            _menu.OnHostGame = HostGame;
            _menu.OnJoinGame = JoinGame;

            _player.OnRequestTaskWindow = task => _taskWindow.Open(task);
            _player.OnRequestSabotageMenu = () => _sabotageMenu.Open();
            _player.OnRequestCameras = () => _cameras.Open();
            _player.OnRequestAdmin = () => _admin.Open();

            if (_bridge != null) _bridge.OnRemoteRoster = OnRemoteRoster;

            GameEvents.PhaseChanged += OnPhaseChanged;
            ShowMenu(true);
        }

        private void OnDestroy()
        {
            GameEvents.PhaseChanged -= OnPhaseChanged;
        }

        private void OnPhaseChanged(MatchPhase phase)
        {
            if (phase == MatchPhase.GameOver) return;
            if (phase != MatchPhase.Roaming)
            {
                if (_taskWindow.IsOpen) _taskWindow.Close();
                if (_sabotageMenu.IsOpen) _sabotageMenu.Close();
                if (_cameras.IsOpen) _cameras.Close();
                if (_admin.IsOpen) _admin.Close();
                if (_bigMap.IsOpen) _bigMap.Close();
            }
        }

        public void ToggleBigMap()
        {
            if (_bigMap.IsOpen) _bigMap.Close();
            else _bigMap.Open();
        }

        private void ShowMenu(bool on)
        {
            _menu.Show(on);
            _gameLayer.gameObject.SetActive(!on);
            if (on) _gameOver.Hide();
        }

        // ==================================================================
        //  match lifecycle
        // ==================================================================
        public void StartSolo(int humanSeats)
        {
            GameSettings.Save();
            TeardownMatch();

            _match.Station = StationView.Instance;
            _match.StartMatch(GameSettings.Match, GameSettings.Profile.DisplayName, humanSeats);
            AfterMatchStarted();
        }

        private void HostGame()
        {
            if (_net == null)
            {
                _menu.SetStatus("Сеть недоступна на этом устройстве");
                return;
            }

            if (!_net.StartHost(GameSettings.Match.PlayerCount, LobbyVisibility.Public))
            {
                _menu.SetStatus("Не удалось открыть порт: " + _net.LastError);
                return;
            }

            _menu.SetStatus("Комната создана. Код: " + _net.RoomCodeValue + " — матч начнётся сразу, свободные места займут NPC.");
            StartSolo(1);
            _bridge?.SendRoster();
        }

        private void JoinGame(string code)
        {
            if (_net == null)
            {
                _menu.SetStatus("Сеть недоступна на этом устройстве");
                return;
            }

            code = RoomCode.Normalise(code);
            if (!string.IsNullOrEmpty(code) && !RoomCode.IsValid(code))
            {
                _menu.SetStatus("Код комнаты состоит из 6 символов");
                return;
            }

            if (!_net.StartClient(code))
            {
                _menu.SetStatus("Не удалось открыть сокет: " + _net.LastError);
                return;
            }
            _menu.SetStatus("Поиск комнаты " + (string.IsNullOrEmpty(code) ? "в локальной сети" : code) + "...");
        }

        private void OnRemoteRoster(MatchSettings settings, List<PlayerState> roster, int localId)
        {
            TeardownMatch();
            _match.Station = StationView.Instance;
            _match.StartRemoteMatch(settings, roster, localId);
            AfterMatchStarted();
            _menu.SetStatus("");
        }

        private void AfterMatchStarted()
        {
            _player.Bind(_match);
            _matchRunning = true;
            ShowMenu(false);

            var localActor = _match.Local?.View as Characters.Actor;
            if (localActor != null) _camera.SetTarget(localActor.transform);

            _match.Tasks?.RefreshMarkers(_match.Local);
            Audio.MusicDirector.Instance?.SetMood(Audio.MusicMood.Calm);
        }

        private void Restart()
        {
            _gameOver.Hide();
            StartSolo(1);
        }

        private void BackToMenu()
        {
            TeardownMatch();
            _net?.Shutdown();
            ShowMenu(true);
            Audio.MusicDirector.Instance?.SetMood(Audio.MusicMood.Menu);
        }

        private void TeardownMatch()
        {
            if (!_matchRunning) return;
            _matchRunning = false;
            _match.Teardown();
            StationView.Instance?.OpenAllDoors();
            StationView.Instance?.SetAllLights(1f);
        }
    }

    /// <summary>Keeps the UI inside the notch-safe region on modern phones.</summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rt;
        private Rect _last;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            if (Screen.safeArea != _last) Apply();
        }

        private void Apply()
        {
            if (_rt == null) return;
            _last = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;

            var min = _last.position;
            var max = _last.position + _last.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
