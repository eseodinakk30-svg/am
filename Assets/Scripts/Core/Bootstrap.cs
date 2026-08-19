// -----------------------------------------------------------------------------
//  NEBULA NINE - entry point.
//
//  The Boot scene is intentionally empty: everything is constructed here at
//  runtime, which keeps the repository free of binary assets and makes the whole
//  game reproducible from code.  Press Play on Assets/Scenes/Boot.unity.
// -----------------------------------------------------------------------------

using UnityEngine;
using Nebula.Audio;
using Nebula.AI;
using Nebula.Gameplay;
using Nebula.Map;
using Nebula.Net;
using Nebula.UI;

namespace Nebula.Core
{
    public class Bootstrap : MonoBehaviour
    {
        public static Bootstrap Instance { get; private set; }

        private StationView _station;
        private MatchManager _match;
        private PlayerController _player;
        private CameraRig _camera;
        private NetworkService _net;
        private NetBridge _bridge;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoStart()
        {
            if (Instance != null) return;
            var go = new GameObject("NebulaNine");
            DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();
        }

        private void Awake()
        {
            Instance = this;
            ApplyPlatformSettings();
            BuildWorld();
        }

        // ------------------------------------------------------------------
        private void ApplyPlatformSettings()
        {
            var profile = GameSettings.Profile;
            if (!PlayerPrefs.HasKey("nebula.quality.autodetected"))
            {
                profile.Quality = QualityManager.Autodetect();
                PlayerPrefs.SetInt("nebula.quality.autodetected", 1);
                GameSettings.Save();
            }

            QualityManager.Apply(profile.Quality);
            SoundBank.SfxVolume = profile.SfxVolume;
            Application.targetFrameRate = profile.TargetFps;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Physics.defaultSolverIterations = 4;
            Time.fixedDeltaTime = 1f / 50f;
        }

        private void BuildWorld()
        {
            // ---- lighting -------------------------------------------------
            var sunGo = new GameObject("KeyLight");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(58f, 32f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.62f, 0.72f, 0.92f);
            sun.intensity = 0.85f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.10f, 0.12f, 0.18f);
            RenderSettings.ambientEquatorColor = new Color(0.07f, 0.08f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.035f, 0.05f);
            RenderSettings.fog = false;

            // ---- station --------------------------------------------------
            var stationGo = new GameObject("Station");
            stationGo.transform.SetParent(transform, false);
            _station = stationGo.AddComponent<StationView>();
            _station.Build();

            // ---- systems --------------------------------------------------
            var matchGo = new GameObject("Match");
            matchGo.transform.SetParent(transform, false);
            _match = matchGo.AddComponent<MatchManager>();
            _match.Station = _station;

            var hub = matchGo.AddComponent<PerceptionHub>();
            hub.Bind(_match);

            _player = matchGo.AddComponent<PlayerController>();
            matchGo.AddComponent<PerformanceTuner>();

            var culler = stationGo.AddComponent<LightCuller>();
            culler.Collect(stationGo.transform, null);

            var camGo = new GameObject("CameraRig");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<CameraRig>();
            _camera.Setup(_match);

            var netGo = new GameObject("Network");
            netGo.transform.SetParent(transform, false);
            _net = netGo.AddComponent<NetworkService>();
            _bridge = netGo.AddComponent<NetBridge>();
            _bridge.Bind(_net, _match);

            MusicDirector.Create(transform).MasterVolume = GameSettings.Profile.MusicVolume;
            MusicDirector.Instance.SetMood(MusicMood.Menu, true);

            // ---- interface ------------------------------------------------
            UiRoot.Create(transform, _match, _player, _camera, _net, _bridge);

            // start passive LAN discovery so the menu can list rooms
            _net.StartDiscovery();

            Debug.Log("[Nebula Nine] Готово. Станция N-9 построена: " +
                      StationLayout.Areas.Count + " зон, " + StationLayout.Vents.Count + " вентиляций, " +
                      Tasks.TaskCatalog.All.Count + " типов заданий.");
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) GameSettings.Save();
        }

        private void OnApplicationQuit()
        {
            GameSettings.Save();
            _net?.Shutdown();
        }
    }
}
