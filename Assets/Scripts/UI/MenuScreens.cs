// -----------------------------------------------------------------------------
//  NEBULA NINE - main menu, lobby settings, character customisation, network
//  panel and the end-of-match report.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.AI;
using Nebula.Characters;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Gameplay;
using Nebula.Net;

namespace Nebula.UI
{
    public class MainMenuScreen : MonoBehaviour
    {
        private RectTransform _root;
        private RectTransform _tabHost;
        private readonly Dictionary<string, RectTransform> _tabs = new Dictionary<string, RectTransform>();
        private Camera _previewCamera;
        private CharacterVisual _preview;
        private Transform _previewRig;
        private RawImage _previewImage;
        private Text _statusLabel;
        private InputField _nameField;
        private InputField _codeField;

        public System.Action<int> OnStartSolo;      // human seats
        public System.Action OnHostGame;
        public System.Action<string> OnJoinGame;

        public static MainMenuScreen Create(Transform parent)
        {
            var rt = UIKit.Stretch(parent, "MainMenu");
            var menu = rt.gameObject.AddComponent<MainMenuScreen>();
            menu._root = rt;
            menu.Build();
            return menu;
        }

        public void Show(bool on)
        {
            _root.gameObject.SetActive(on);
            if (_previewCamera != null) _previewCamera.enabled = on;
        }

        // ==================================================================
        private void Build()
        {
            UIKit.PanelStretch(_root, "Bg", new Color(0.035f, 0.045f, 0.075f, 1f), 0);

            // star field backdrop
            var bgTex = Art.StarField(512, 4242);
            var bgRt = UIKit.Stretch(_root, "Stars");
            var bgImg = bgRt.gameObject.AddComponent<RawImage>();
            bgImg.texture = bgTex;
            bgImg.color = new Color(1f, 1f, 1f, 0.55f);
            bgImg.raycastTarget = false;

            UIKit.Label(_root, "NEBULA NINE", new Vector2(0f, 360f), new Vector2(1200f, 90f), 76,
                TextAnchor.MiddleCenter, Art.Accent, FontStyle.Bold);
            UIKit.Label(_root, "станция N-9 · найди предателя, пока он не нашёл тебя",
                new Vector2(0f, 296f), new Vector2(1200f, 40f), 24, TextAnchor.MiddleCenter, Art.TextDim);

            // ---- tab bar ----
            string[] tabNames = { "ИГРА", "ПРАВИЛА", "ПЕРСОНАЖ", "СЕТЬ", "НАСТРОЙКИ" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                string key = tabNames[i];
                UIKit.Button(_root, key, new Vector2(-620f + i * 310f, 218f), new Vector2(290f, 62f),
                    () => ShowTab(key), new Color(0.12f, 0.16f, 0.24f, 0.95f), 22, 14);
            }

            _tabHost = UIKit.Node(_root, "TabHost", new Vector2(0f, -70f), new Vector2(1450f, 520f));
            BuildPlayTab();
            BuildRulesTab();
            BuildCharacterTab();
            BuildNetworkTab();
            BuildSettingsTab();
            ShowTab("ИГРА");

            _statusLabel = UIKit.Label(_root, "", new Vector2(0f, -400f), new Vector2(1300f, 40f), 22,
                TextAnchor.MiddleCenter, Art.AccentWarm);
        }

        private RectTransform NewTab(string key)
        {
            var rt = UIKit.Node(_tabHost, "Tab_" + key, Vector2.zero, new Vector2(1450f, 520f));
            UIKit.PanelStretch(rt, "Bg", new Color(0.06f, 0.08f, 0.13f, 0.88f), 20);
            _tabs[key] = rt;
            rt.gameObject.SetActive(false);
            return rt;
        }

        private void ShowTab(string key)
        {
            foreach (var kv in _tabs) kv.Value.gameObject.SetActive(kv.Key == key);
        }

        // ------------------------------------------------------------------ play
        private void BuildPlayTab()
        {
            var tab = NewTab("ИГРА");

            UIKit.Label(tab, "ОДИН ПРОТИВ AI", new Vector2(0f, 190f), new Vector2(1200f, 50f), 34,
                TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);
            UIKit.Label(tab, "Ты и до 14 NPC. Каждый бот имеет собственный характер, память и логику:\n" +
                             "они наблюдают, запоминают, спорят, врут и голосуют самостоятельно.",
                new Vector2(0f, 120f), new Vector2(1200f, 70f), 22, TextAnchor.MiddleCenter, Art.TextDim);

            UIKit.Button(tab, "НАЧАТЬ МАТЧ", new Vector2(0f, 10f), new Vector2(460f, 96f),
                () => OnStartSolo?.Invoke(1), new Color(0.20f, 0.55f, 0.75f, 0.96f), 32, 20);

            var difficultyLabel = UIKit.Label(tab, "", new Vector2(0f, -90f), new Vector2(1200f, 40f), 24,
                TextAnchor.MiddleCenter, Art.AccentWarm);
            void RefreshDifficulty() =>
                difficultyLabel.text = "Сложность AI: " + DifficultyName(GameSettings.Match.AiDifficulty);
            RefreshDifficulty();

            string[] diff = { "Easy", "Normal", "Hard", "Expert", "Master" };
            for (int i = 0; i < diff.Length; i++)
            {
                int index = i;
                UIKit.Button(tab, diff[i], new Vector2(-520f + i * 260f, -160f), new Vector2(240f, 66f), () =>
                {
                    GameSettings.Match.AiDifficulty = (Difficulty)index;
                    GameSettings.Save();
                    RefreshDifficulty();
                }, new Color(0.16f, 0.20f, 0.28f, 0.95f), 22, 14);
            }

            UIKit.Label(tab, "Master анализирует огромный объём информации — но всё равно может ошибиться.",
                new Vector2(0f, -220f), new Vector2(1300f, 36f), 20, TextAnchor.MiddleCenter, Art.TextDim);
        }

        private static string DifficultyName(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return "Easy — слабая логика";
                case Difficulty.Normal: return "Normal — обычный игрок";
                case Difficulty.Hard: return "Hard — внимательный игрок";
                case Difficulty.Expert: return "Expert — сильная дедукция";
                default: return "Master — невероятно умный игрок";
            }
        }

        // ------------------------------------------------------------------ rules
        private void BuildRulesTab()
        {
            var tab = NewTab("ПРАВИЛА");
            var m = GameSettings.Match;

            AddIntSlider(tab, "Участников", new Vector2(-340f, 180f), 4, 15, m.PlayerCount, v => { m.PlayerCount = v; m.Validate(); });
            AddIntSlider(tab, "Предателей", new Vector2(-340f, 100f), 1, 3, m.InfiltratorCount, v => { m.InfiltratorCount = v; m.Validate(); });
            AddIntSlider(tab, "Коротких заданий", new Vector2(-340f, 20f), 0, 10, m.ShortTasks, v => m.ShortTasks = v);
            AddIntSlider(tab, "Длинных заданий", new Vector2(-340f, -60f), 0, 6, m.LongTasks, v => m.LongTasks = v);
            AddIntSlider(tab, "Общих заданий", new Vector2(-340f, -140f), 0, 2, m.CommonTasks, v => m.CommonTasks = v);

            AddFloatSlider(tab, "Перезарядка убийства", new Vector2(360f, 180f), 10f, 60f, m.KillCooldown, v => m.KillCooldown = v, "с");
            AddFloatSlider(tab, "Скорость", new Vector2(360f, 100f), 3f, 10f, m.MoveSpeed, v => m.MoveSpeed = v, "");
            AddFloatSlider(tab, "Обзор экипажа", new Vector2(360f, 20f), 7f, 22f, m.CrewVision, v => m.CrewVision = v, "м");
            AddFloatSlider(tab, "Время обсуждения", new Vector2(360f, -60f), 10f, 90f, m.DiscussionTime, v => m.DiscussionTime = v, "с");
            AddFloatSlider(tab, "Время голосования", new Vector2(360f, -140f), 15f, 120f, m.VotingTime, v => m.VotingTime = v, "с");

            UIKit.Toggle(tab, "Подтверждать роль изгнанного", new Vector2(-340f, -215f), new Vector2(560f, 46f),
                m.ConfirmEjects, v => m.ConfirmEjects = v);
            UIKit.Toggle(tab, "Анонимное голосование", new Vector2(360f, -215f), new Vector2(560f, 46f),
                m.AnonymousVotes, v => m.AnonymousVotes = v);
        }

        private void AddIntSlider(Transform parent, string label, Vector2 pos, int min, int max, int value, System.Action<int> apply)
        {
            var text = UIKit.Label(parent, $"{label}: {value}", pos + new Vector2(0f, 26f), new Vector2(560f, 34f), 22,
                TextAnchor.MiddleLeft, Art.TextMain);
            UIKit.Slider(parent, pos - new Vector2(0f, 8f), new Vector2(560f, 30f), min, max, value, v =>
            {
                int iv = Mathf.RoundToInt(v);
                apply(iv);
                GameSettings.Save();
                text.text = $"{label}: {iv}";
            });
        }

        private void AddFloatSlider(Transform parent, string label, Vector2 pos, float min, float max, float value,
            System.Action<float> apply, string unit)
        {
            var text = UIKit.Label(parent, $"{label}: {value:0.#}{unit}", pos + new Vector2(0f, 26f), new Vector2(560f, 34f), 22,
                TextAnchor.MiddleLeft, Art.TextMain);
            UIKit.Slider(parent, pos - new Vector2(0f, 8f), new Vector2(560f, 30f), min, max, value, v =>
            {
                apply(v);
                GameSettings.Save();
                text.text = $"{label}: {v:0.#}{unit}";
            });
        }

        // ------------------------------------------------------------------ character
        private void BuildCharacterTab()
        {
            var tab = NewTab("ПЕРСОНАЖ");
            var profile = GameSettings.Profile;

            BuildPreview();
            var previewRt = UIKit.Node(tab, "Preview", new Vector2(-480f, 0f), new Vector2(340f, 440f));
            UIKit.PanelStretch(previewRt, "Bg", new Color(0.03f, 0.05f, 0.09f, 0.95f), 18);
            _previewImage = UIKit.Node(previewRt, "Raw", Vector2.zero, new Vector2(320f, 420f)).gameObject.AddComponent<RawImage>();
            _previewImage.texture = _previewCamera.targetTexture;
            _previewImage.raycastTarget = false;

            UIKit.Label(tab, "Имя", new Vector2(-40f, 190f), new Vector2(200f, 36f), 22, TextAnchor.MiddleLeft, Art.TextDim);
            var nameRt = UIKit.Node(tab, "NameField", new Vector2(180f, 190f), new Vector2(520f, 58f));
            var nameBg = nameRt.gameObject.AddComponent<Image>();
            nameBg.sprite = Art.RoundedRect(12, 48);
            nameBg.type = Image.Type.Sliced;
            nameBg.color = new Color(0.10f, 0.13f, 0.19f, 0.98f);
            _nameField = nameRt.gameObject.AddComponent<InputField>();
            var nameTextRt = UIKit.Stretch(nameRt, "Text", 12f);
            var nameText = nameTextRt.gameObject.AddComponent<Text>();
            nameText.font = Art.UiFont;
            nameText.fontSize = 24;
            nameText.color = Art.TextMain;
            nameText.alignment = TextAnchor.MiddleLeft;
            _nameField.textComponent = nameText;
            _nameField.characterLimit = 14;
            _nameField.text = profile.DisplayName;
            _nameField.onEndEdit.AddListener(v =>
            {
                profile.DisplayName = string.IsNullOrWhiteSpace(v) ? NameBank.RandomHumanName() : v.Trim();
                GameSettings.Save();
                RebuildPreview();
            });

            AddCycler(tab, "Цвет", new Vector2(-40f, 110f), ColorBank.SuitNames.Length,
                () => profile.ColorIndex, v => profile.ColorIndex = v, i => ColorBank.SuitNames[i]);
            AddCycler(tab, "Костюм", new Vector2(-40f, 40f), CosmeticBank.Outfits.Length,
                () => profile.OutfitIndex, v => profile.OutfitIndex = v, i => CosmeticBank.Outfits[i]);
            AddCycler(tab, "Головной убор", new Vector2(-40f, -30f), CosmeticBank.Hats.Length,
                () => profile.HatIndex, v => profile.HatIndex = v, i => CosmeticBank.Hats[i]);
            AddCycler(tab, "Аксессуар", new Vector2(-40f, -100f), CosmeticBank.Accessories.Length,
                () => profile.AccessoryIndex, v => profile.AccessoryIndex = v, i => CosmeticBank.Accessories[i]);
            AddCycler(tab, "Эффект", new Vector2(-40f, -170f), CosmeticBank.Trails.Length,
                () => profile.TrailIndex, v => profile.TrailIndex = v, i => CosmeticBank.Trails[i]);
        }

        private void AddCycler(Transform parent, string label, Vector2 pos, int count,
            System.Func<int> get, System.Action<int> set, System.Func<int, string> nameOf)
        {
            UIKit.Label(parent, label, pos, new Vector2(240f, 36f), 22, TextAnchor.MiddleLeft, Art.TextDim);
            var value = UIKit.Label(parent, nameOf(Mathf.Abs(get()) % count), pos + new Vector2(370f, 0f),
                new Vector2(360f, 36f), 22, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            void Step(int delta)
            {
                int v = (Mathf.Abs(get()) + delta + count) % count;
                set(v);
                GameSettings.Save();
                value.text = nameOf(v);
                RebuildPreview();
            }

            UIKit.Button(parent, "◀", pos + new Vector2(210f, 0f), new Vector2(64f, 52f), () => Step(-1), Art.PanelSoft, 22, 12);
            UIKit.Button(parent, "▶", pos + new Vector2(530f, 0f), new Vector2(64f, 52f), () => Step(1), Art.PanelSoft, 22, 12);
        }

        private void BuildPreview()
        {
            if (_previewCamera != null) return;

            var root = new GameObject("CharacterPreview");
            root.transform.position = new Vector3(0f, 400f, 0f);
            _previewRig = root.transform;

            var camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.35f, -4.2f);
            camGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            _previewCamera = camGo.AddComponent<Camera>();
            _previewCamera.clearFlags = CameraClearFlags.SolidColor;
            _previewCamera.backgroundColor = new Color(0.04f, 0.06f, 0.10f);
            _previewCamera.fieldOfView = 34f;
            _previewCamera.nearClipPlane = 0.2f;
            _previewCamera.farClipPlane = 30f;
            _previewCamera.targetTexture = new RenderTexture(320, 420, 16) { name = "PreviewRT" };
            _previewCamera.depth = -3;

            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(2f, 3f, -3f);
            lightGo.transform.localRotation = Quaternion.Euler(35f, 160f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = new Color(0.95f, 0.97f, 1f);

            RebuildPreview();
        }

        private void RebuildPreview()
        {
            if (_previewRig == null) return;
            if (_preview != null) Destroy(_preview.gameObject);

            var go = new GameObject("PreviewCharacter");
            go.transform.SetParent(_previewRig, false);
            go.transform.localPosition = Vector3.zero;
            _preview = go.AddComponent<CharacterVisual>();
            var profile = GameSettings.Profile;
            _preview.Build(profile.ColorIndex, profile.HatIndex, profile.OutfitIndex,
                profile.AccessoryIndex, profile.TrailIndex, profile.DisplayName);
            _preview.SetTagVisible(false);
        }

        private void Update()
        {
            if (_preview != null && _root.gameObject.activeSelf)
                _preview.transform.Rotate(Vector3.up, Time.unscaledDeltaTime * 28f, Space.Self);
        }

        // ------------------------------------------------------------------ network
        private void BuildNetworkTab()
        {
            var tab = NewTab("СЕТЬ");

            UIKit.Label(tab, "СЕТЕВАЯ ИГРА", new Vector2(0f, 200f), new Vector2(1200f, 50f), 32,
                TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);
            UIKit.Label(tab,
                "Хост запускает матч и раздаёт код комнаты. Игроки в одной сети видят открытые комнаты автоматически,\n" +
                "либо подключаются по коду. Свободные места занимают NPC. Хост — источник истины: убийства, задания\n" +
                "и голоса подтверждаются им, поэтому клиент не может «наколдовать» себе результат.",
                new Vector2(0f, 130f), new Vector2(1300f, 100f), 21, TextAnchor.MiddleCenter, Art.TextDim);

            UIKit.Button(tab, "СОЗДАТЬ КОМНАТУ", new Vector2(-330f, 20f), new Vector2(420f, 88f),
                () => OnHostGame?.Invoke(), new Color(0.20f, 0.55f, 0.75f, 0.96f), 26, 18);

            var codeRt = UIKit.Node(tab, "CodeField", new Vector2(330f, 55f), new Vector2(420f, 66f));
            var codeBg = codeRt.gameObject.AddComponent<Image>();
            codeBg.sprite = Art.RoundedRect(12, 48);
            codeBg.type = Image.Type.Sliced;
            codeBg.color = new Color(0.10f, 0.13f, 0.19f, 0.98f);
            _codeField = codeRt.gameObject.AddComponent<InputField>();
            var codeTextRt = UIKit.Stretch(codeRt, "Text", 12f);
            var codeText = codeTextRt.gameObject.AddComponent<Text>();
            codeText.font = Art.UiFont;
            codeText.fontSize = 28;
            codeText.color = Art.TextMain;
            codeText.alignment = TextAnchor.MiddleCenter;
            _codeField.textComponent = codeText;
            _codeField.characterLimit = 6;
            var codePlaceholderRt = UIKit.Stretch(codeRt, "Placeholder", 12f);
            var codePlaceholder = codePlaceholderRt.gameObject.AddComponent<Text>();
            codePlaceholder.font = Art.UiFont;
            codePlaceholder.fontSize = 26;
            codePlaceholder.color = new Color(0.45f, 0.5f, 0.6f);
            codePlaceholder.alignment = TextAnchor.MiddleCenter;
            codePlaceholder.text = "КОД КОМНАТЫ";
            _codeField.placeholder = codePlaceholder;

            UIKit.Button(tab, "ПОДКЛЮЧИТЬСЯ", new Vector2(330f, -40f), new Vector2(420f, 76f),
                () => OnJoinGame?.Invoke(_codeField.text), new Color(0.24f, 0.42f, 0.62f, 0.96f), 24, 16);

            var lanList = UIKit.Node(tab, "LanList", new Vector2(0f, -170f), new Vector2(1300f, 120f));
            UIKit.Label(lanList, "Найденные комнаты в локальной сети:", new Vector2(0f, 40f), new Vector2(1200f, 32f), 20,
                TextAnchor.MiddleCenter, Art.TextDim);
            var lanText = UIKit.Label(lanList, "поиск...", new Vector2(0f, 0f), new Vector2(1200f, 60f), 22,
                TextAnchor.MiddleCenter, Art.Accent);

            var refresher = lanList.gameObject.AddComponent<LanListRefresher>();
            refresher.Label = lanText;
        }

        // ------------------------------------------------------------------ settings
        private void BuildSettingsTab()
        {
            var tab = NewTab("НАСТРОЙКИ");
            var profile = GameSettings.Profile;

            UIKit.Label(tab, "Качество графики", new Vector2(-340f, 190f), new Vector2(500f, 36f), 24,
                TextAnchor.MiddleLeft, Art.TextMain);
            string[] tiers = { "Low", "Medium", "High", "Ultra" };
            for (int i = 0; i < tiers.Length; i++)
            {
                int index = i;
                UIKit.Button(tab, tiers[i], new Vector2(-500f + i * 200f, 130f), new Vector2(180f, 62f), () =>
                {
                    profile.Quality = (QualityTier)index;
                    GameSettings.Save();
                    QualityManager.Apply(profile.Quality);
                }, new Color(0.16f, 0.20f, 0.28f, 0.95f), 22, 14);
            }

            AddFloatSlider(tab, "Музыка", new Vector2(-340f, 40f), 0f, 1f, profile.MusicVolume, v =>
            {
                profile.MusicVolume = v;
                if (Audio.MusicDirector.Instance != null) Audio.MusicDirector.Instance.MasterVolume = v;
            }, "");

            AddFloatSlider(tab, "Звуки", new Vector2(-340f, -40f), 0f, 1f, profile.SfxVolume, v =>
            {
                profile.SfxVolume = v;
                Audio.SoundBank.SfxVolume = v;
            }, "");

            AddIntSlider(tab, "Целевой FPS", new Vector2(-340f, -120f), 30, 120, profile.TargetFps, v =>
            {
                profile.TargetFps = v;
                Application.targetFrameRate = v;
            });

            UIKit.Toggle(tab, "Левосторонний интерфейс", new Vector2(360f, 130f), new Vector2(560f, 46f),
                profile.LeftHandedUi, v => { profile.LeftHandedUi = v; GameSettings.Save(); });
            UIKit.Toggle(tab, "Тряска камеры", new Vector2(360f, 60f), new Vector2(560f, 46f),
                profile.ScreenShake, v => { profile.ScreenShake = v; GameSettings.Save(); });

            UIKit.Label(tab,
                "Игра рендерится на URP и рассчитана на Android: пресеты качества меняют разрешение рендера,\n" +
                "тени, MSAA и лимит доп. источников света.",
                new Vector2(360f, -60f), new Vector2(660f, 90f), 20, TextAnchor.UpperLeft, Art.TextDim);
        }

        public void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }
    }

    /// <summary>Polls the LAN discovery service and prints what it found.</summary>
    public class LanListRefresher : MonoBehaviour
    {
        public Text Label;
        private float _timer;

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 1f;
            if (Label == null) return;

            var rooms = NetworkService.Instance != null ? NetworkService.Instance.DiscoveredRooms : null;
            if (rooms == null || rooms.Count == 0)
            {
                Label.text = "комнаты не найдены — создай свою или введи код";
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var room in rooms)
                sb.Append(room.Code).Append(" (").Append(room.Players).Append("/").Append(room.Capacity).Append(")   ");
            Label.text = sb.ToString();
        }
    }

    // ==================================================================
    public class GameOverScreen : MonoBehaviour
    {
        private RectTransform _root;
        private MatchManager _match;
        private Text _title, _reason;
        private RectTransform _roster;
        public System.Action OnPlayAgain;
        public System.Action OnBackToMenu;

        public static GameOverScreen Create(Transform parent, MatchManager match)
        {
            var rt = UIKit.Stretch(parent, "GameOver");
            var screen = rt.gameObject.AddComponent<GameOverScreen>();
            screen._root = rt;
            screen._match = match;
            screen.Build();
            rt.gameObject.SetActive(false);
            return screen;
        }

        private void Build()
        {
            UIKit.PanelStretch(_root, "Bg", new Color(0.02f, 0.03f, 0.05f, 0.97f), 0);
            _title = UIKit.Label(_root, "", new Vector2(0f, 350f), new Vector2(1300f, 100f), 68,
                TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);
            _reason = UIKit.Label(_root, "", new Vector2(0f, 282f), new Vector2(1300f, 46f), 26,
                TextAnchor.MiddleCenter, Art.TextDim);
            _roster = UIKit.Node(_root, "Roster", new Vector2(0f, -20f), new Vector2(1400f, 520f));

            UIKit.Button(_root, "ИГРАТЬ СНОВА", new Vector2(-230f, -370f), new Vector2(380f, 84f),
                () => OnPlayAgain?.Invoke(), new Color(0.20f, 0.55f, 0.75f, 0.96f), 26, 18);
            UIKit.Button(_root, "В МЕНЮ", new Vector2(230f, -370f), new Vector2(380f, 84f),
                () => OnBackToMenu?.Invoke(), new Color(0.24f, 0.27f, 0.36f, 0.96f), 26, 18);

            GameEvents.GameOver += Show;
        }

        private void OnDestroy() => GameEvents.GameOver -= Show;

        private void Show(WinSide side, WinReason reason)
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();

            bool crewWon = side == WinSide.Crew;
            _title.text = crewWon ? "<color=#5ED27E>ЭКИПАЖ ПОБЕДИЛ</color>" : "<color=#EA4B4F>ПРЕДАТЕЛИ ПОБЕДИЛИ</color>";
            _reason.text = ReasonText(reason);

            for (int i = _roster.childCount - 1; i >= 0; i--) Destroy(_roster.GetChild(i).gameObject);

            int count = _match.Players.Count;
            int cols = 3;
            int rows = Mathf.CeilToInt(count / (float)cols);
            float cw = 1400f / cols, ch = Mathf.Min(96f, 520f / Mathf.Max(1, rows));

            for (int i = 0; i < count; i++)
            {
                var p = _match.Players[i];
                int col = i % cols, row = i / cols;
                var pos = new Vector2(-700f + cw * (col + 0.5f), 240f - ch * (row + 0.5f));
                var card = UIKit.Panel(_roster, "P" + i, pos, new Vector2(cw - 16f, ch - 8f),
                    p.Role == Role.Infiltrator ? new Color(0.25f, 0.08f, 0.10f, 0.95f) : new Color(0.09f, 0.12f, 0.18f, 0.95f), 12);

                UIKit.Icon(card.transform, "Chip", Art.Circle(64), new Vector2(-cw * 0.5f + 42f, 0f),
                    new Vector2(46f, 46f), p.Color);

                string role = p.Role == Role.Infiltrator ? "<color=#EA4B4F>предатель</color>" : "экипаж";
                string status = p.Life == LifeState.Ejected ? " · изгнан"
                    : p.Life == LifeState.Murdered ? " · убит" : "";
                UIKit.Label(card.transform, $"{p.Label} — {role}{status}",
                    new Vector2(-cw * 0.5f + 90f, 16f), new Vector2(cw - 140f, 30f), 20, TextAnchor.MiddleLeft, Art.TextMain);

                string persona = p.Brain is NpcBrain brain ? brain.PersonalityLabel : "живой игрок";
                UIKit.Label(card.transform, persona,
                    new Vector2(-cw * 0.5f + 90f, -14f), new Vector2(cw - 140f, 28f), 17, TextAnchor.MiddleLeft, Art.TextDim);
            }
        }

        private static string ReasonText(WinReason reason)
        {
            switch (reason)
            {
                case WinReason.TasksComplete: return "Экипаж выполнил все задания";
                case WinReason.AllInfiltratorsEjected: return "Все предатели были изгнаны";
                case WinReason.InfiltratorsReachedParity: return "Предателей стало столько же, сколько экипажа";
                case WinReason.SabotageTimeout: return "Критический саботаж не был устранён вовремя";
                default: return "";
            }
        }

        public void Hide() => _root.gameObject.SetActive(false);
    }
}
