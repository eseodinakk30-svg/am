// -----------------------------------------------------------------------------
//  NEBULA NINE - комната ожидания.
//
//  Матч больше не начинается сразу из меню: сначала все оказываются в столовой,
//  где можно походить, посмотреть на остальных и подойти к ноутбуку. Ноутбук
//  открывает консоль: внешность слева, правила справа. Хост жмёт СТАРТ.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Gameplay;
using Nebula.Map;

namespace Nebula.UI
{
    public class LobbyScreen : MonoBehaviour
    {
        public const float LaptopRange = 3.2f;

        private RectTransform _root;
        private MatchManager _match;
        private Text _title;
        private Text _roster;
        private RectTransform _rosterPanel;
        private RectTransform _rosterBody;
        private Text _rosterMark;
        private bool _rosterHidden;
        private Text _hint;
        private UnityEngine.UI.Button _startButton;
        private Transform _laptop;

        public System.Action OnStart;
        public System.Action OnOpenConsole;

        public Vector3 LaptopPosition { get; private set; }
        public DeckId LaptopDeck { get; private set; }

        public static LobbyScreen Create(Transform parent, MatchManager match)
        {
            var rt = UIKit.Stretch(parent, "Lobby");
            var screen = rt.gameObject.AddComponent<LobbyScreen>();
            screen._root = rt;
            screen._match = match;
            screen.Build();
            rt.gameObject.SetActive(false);
            return screen;
        }

        private void Build()
        {
            var top = UIKit.Anchored(_root, "Banner", new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(860f, 96f));
            UIKit.PanelStretch(top, "Bg", new Color(0.05f, 0.08f, 0.13f, 0.86f), 18);
            _title = UIKit.Label(top, "КОМНАТА", Vector2.zero, new Vector2(820f, 80f), 34,
                                 TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            // Список тех, кто в комнате, сворачивается по шапке — он занимал
            // всю левую сторону и закрывал обзор.
            _rosterPanel = UIKit.Anchored(_root, "Roster", new Vector2(0f, 1f), new Vector2(20f, -140f), new Vector2(360f, 500f));
            UIKit.PanelStretch(_rosterPanel, "Bg", new Color(0.04f, 0.06f, 0.10f, 0.82f), 16);

            var head = UIKit.Row(_rosterPanel, "Head", 6f, 6f, 0f, 44f);
            head.anchorMin = new Vector2(0f, 1f);
            head.anchorMax = new Vector2(1f, 1f);
            head.pivot = new Vector2(0.5f, 1f);
            head.anchoredPosition = new Vector2(0f, -6f);
            UIKit.ButtonIn(head, "", ToggleRoster, new Color(0.09f, 0.13f, 0.20f, 0.92f), 20, 10);
            UIKit.RowLabel(head, "В КОМНАТЕ", 14f, 50f, 0f, 38f, 22,
                           TextAnchor.MiddleLeft, Art.TextMain, FontStyle.Bold);
            _rosterMark = UIKit.RowLabel(head, "▲", 0f, 14f, 0f, 38f, 22,
                                         TextAnchor.MiddleRight, Art.TextDim, FontStyle.Bold);

            _rosterBody = UIKit.Region(_rosterPanel, "Body", Vector2.zero, Vector2.one, 0f);
            _rosterBody.offsetMax = new Vector2(-10f, -54f);
            _rosterBody.offsetMin = new Vector2(12f, 10f);
            _roster = _rosterBody.gameObject.AddComponent<Text>();
            _roster.font = Art.UiFont;
            _roster.fontSize = 23;
            _roster.color = Art.TextMain;
            _roster.alignment = TextAnchor.UpperLeft;
            _roster.horizontalOverflow = HorizontalWrapMode.Wrap;
            _roster.verticalOverflow = VerticalWrapMode.Truncate;
            _roster.raycastTarget = false;

            _hint = UIKit.Label(_root, "", new Vector2(0f, -300f), new Vector2(900f, 60f), 26,
                                TextAnchor.MiddleCenter, new Color(0.78f, 0.84f, 0.92f));

            var startImg = UIKit.CircleButton(_root, "СТАРТ", new Vector2(0.5f, 0f), new Vector2(0f, 150f), 190f,
                                              new Color(0.22f, 0.68f, 0.38f, 0.95f), () => OnStart?.Invoke(), out _);
            _startButton = startImg.GetComponent<UnityEngine.UI.Button>();
        }

        private void ToggleRoster()
        {
            _rosterHidden = !_rosterHidden;
            _rosterBody.gameObject.SetActive(!_rosterHidden);
            _rosterPanel.sizeDelta = new Vector2(360f, _rosterHidden ? 56f : 500f);
            if (_rosterMark != null) _rosterMark.text = _rosterHidden ? "▼" : "▲";
        }

        /// <summary>Ставит ноутбук у стола в столовой и включает экран лобби.</summary>
        public void Show(Transform worldRoot)
        {
            _root.gameObject.SetActive(true);
            EnsureLaptop(worldRoot);
            Refresh();
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
            if (_laptop != null) _laptop.gameObject.SetActive(false);
        }

        private void EnsureLaptop(Transform worldRoot)
        {
            var cafe = StationLayout.Get("cafeteria");
            var centre = cafe != null
                ? StationLayout.CellToWorld(cafe.Deck, cafe.CenterCell.x, cafe.CenterCell.y)
                : Vector3.zero;
            LaptopDeck = cafe != null ? cafe.Deck : DeckId.Upper;
            LaptopPosition = centre;

            if (_laptop != null)
            {
                _laptop.gameObject.SetActive(true);
                _laptop.position = centre;
                return;
            }

            var go = new GameObject("LobbyLaptop");
            go.transform.SetParent(worldRoot, false);
            go.transform.position = centre;
            _laptop = go.transform;

            // подставка
            var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(baseGo.GetComponent<Collider>());
            baseGo.name = "Desk";
            baseGo.transform.SetParent(go.transform, false);
            baseGo.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            baseGo.transform.localScale = new Vector3(2.2f, 0.9f, 1.4f);
            baseGo.GetComponent<Renderer>().sharedMaterial = Art.Lit(new Color(0.16f, 0.19f, 0.26f), 0.1f, 0.4f);

            // крышка с подсветкой — её видно издалека, к ней и идут
            var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(lid.GetComponent<Collider>());
            lid.name = "Screen";
            lid.transform.SetParent(go.transform, false);
            lid.transform.localPosition = new Vector3(0f, 1.15f, -0.3f);
            lid.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
            lid.transform.localScale = new Vector3(1.5f, 0.9f, 0.08f);
            lid.GetComponent<Renderer>().sharedMaterial = Art.Lit(new Color(0.32f, 0.78f, 0.92f), 0f, 0.5f, 1.6f);
        }

        public bool PlayerNearLaptop(PlayerState p)
        {
            if (p == null || _laptop == null || !_laptop.gameObject.activeSelf) return false;
            if (p.Deck != LaptopDeck) return false;
            var a = p.Position; a.y = 0f;
            var b = LaptopPosition; b.y = 0f;
            return (a - b).sqrMagnitude <= LaptopRange * LaptopRange;
        }

        private float _timer;

        private void Update()
        {
            if (_match == null) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.3f;
            Refresh();
        }

        private void Refresh()
        {
            var code = Net.NetworkService.Instance != null ? Net.NetworkService.Instance.RoomCodeValue : "";
            _title.text = string.IsNullOrEmpty(code)
                ? "КОМНАТА  ·  " + _match.Players.Count + " участников"
                : "КОД " + code + "  ·  " + _match.Players.Count + " участников";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _match.Players.Count && i < 15; i++)
            {
                var p = _match.Players[i];
                if (p == null) continue;
                sb.Append(p.IsLocal ? "▶ " : "  ");
                sb.AppendLine(p.Label);
            }
            _roster.text = sb.ToString();

            bool near = PlayerNearLaptop(_match.Local);
            _hint.text = near
                ? "Нажми ДЕЙСТВИЕ — откроется консоль комнаты"
                : "Подойди к ноутбуку в центре столовой, чтобы сменить облик и правила";
            _hint.color = near ? new Color(0.55f, 0.92f, 0.65f) : new Color(0.72f, 0.78f, 0.86f);

            if (_startButton != null) _startButton.interactable = true;
        }
    }
}
