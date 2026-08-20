// -----------------------------------------------------------------------------
//  NEBULA NINE - meeting screen: discussion, chat, voting and the ejection cut.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Gameplay;

namespace Nebula.UI
{
    public class MeetingScreen : MonoBehaviour
    {
        private MatchManager _match;
        private RectTransform _root;
        private Text _title, _timer, _phaseLabel;
        private RectTransform _grid;
        private RectTransform _chatContent;
        private ScrollRect _chatScroll;
        private InputField _chatInput;
        private Button _skipButton;
        private Image _ejectPanel;
        private Text _ejectText;
        private RectTransform _ejectActor;

        private class Slot
        {
            public PlayerState Player;
            public Image Card;
            public Image Chip;
            public Text Name;
            public Button VoteButton;
            public RectTransform VoteMarks;
            public Image DeadOverlay;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private int _chatCount;
        private float _refresh;

        public static MeetingScreen Create(Transform parent, MatchManager match)
        {
            var rt = UIKit.Stretch(parent, "MeetingScreen");
            var screen = rt.gameObject.AddComponent<MeetingScreen>();
            screen._root = rt;
            screen._match = match;
            screen.Build();
            rt.gameObject.SetActive(false);
            return screen;
        }

        private void Build()
        {
            UIKit.PanelStretch(_root, "Bg", new Color(0.03f, 0.045f, 0.075f, 0.97f), 0);

            // Шапка, сетка и чат теперь живут в долях экрана: на широком телефоне
            // они растягиваются, а не оставляют по краям пустые поля.
            var header = UIKit.Region(_root, "Header", new Vector2(0f, 0.88f), new Vector2(1f, 1f), 18f);
            _title = UIKit.RowLabel(header, "СОБРАНИЕ", 40f, 260f, 8f, 60f, 40,
                TextAnchor.MiddleLeft, Art.TextMain, FontStyle.Bold);
            _phaseLabel = UIKit.RowLabel(header, "", 40f, 260f, -34f, 34f, 22,
                TextAnchor.MiddleLeft, Art.TextDim);
            _timer = UIKit.RowLabel(header, "", 0f, 40f, 0f, 60f, 44,
                TextAnchor.MiddleRight, Art.AccentWarm, FontStyle.Bold);

            _grid = UIKit.Region(_root, "Grid", new Vector2(0f, 0.13f), new Vector2(0.60f, 0.87f), 14f);

            // ---- chat ----
            var chatPanel = UIKit.Region(_root, "Chat", new Vector2(0.60f, 0.02f), new Vector2(1f, 0.87f), 14f);
            UIKit.PanelStretch(chatPanel, "Bg", new Color(0.06f, 0.08f, 0.13f, 0.94f), 16);
            var chatView = UIKit.Region(chatPanel, "View", new Vector2(0f, 0.14f), new Vector2(1f, 1f), 10f);
            _chatScroll = UIKit.ScrollViewStretch(chatView, "Scroll", out _chatContent);

            var inputBar = UIKit.Region(chatPanel, "InputBar", new Vector2(0f, 0f), new Vector2(1f, 0.14f), 10f);
            var inputRt = UIKit.Row(inputBar, "Input", 0f, 96f, 0f, 62f);
            var inputBg = inputRt.gameObject.AddComponent<Image>();
            inputBg.sprite = Art.RoundedRect(12, 48);
            inputBg.type = Image.Type.Sliced;
            inputBg.color = new Color(0.10f, 0.13f, 0.19f, 0.98f);

            _chatInput = inputRt.gameObject.AddComponent<InputField>();
            var textRt = UIKit.Stretch(inputRt, "Text", 12f);
            var inputText = textRt.gameObject.AddComponent<Text>();
            inputText.font = Art.UiFont;
            inputText.fontSize = 22;
            inputText.color = Art.TextMain;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;
            _chatInput.textComponent = inputText;

            var placeholderRt = UIKit.Stretch(inputRt, "Placeholder", 12f);
            var placeholder = placeholderRt.gameObject.AddComponent<Text>();
            placeholder.font = Art.UiFont;
            placeholder.fontSize = 22;
            placeholder.color = new Color(0.45f, 0.5f, 0.6f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.text = "Написать сообщение...";
            _chatInput.placeholder = placeholder;
            _chatInput.characterLimit = 90;
            _chatInput.onEndEdit.AddListener(OnChatSubmit);

            var sendRt = UIKit.Anchored(inputBar, "Send", new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(84f, 62f));
            sendRt.pivot = new Vector2(1f, 0.5f);
            UIKit.ButtonIn(sendRt, "▶", () => OnChatSubmit(_chatInput.text), Art.PanelSoft, 26, 12);

            var footer = UIKit.Region(_root, "Footer", new Vector2(0f, 0f), new Vector2(0.60f, 0.13f), 14f);
            _skipButton = UIKit.ButtonIn(UIKit.Row(footer, "Skip", 40f, 40f, 0f, 74f),
                "ПРОПУСТИТЬ ГОЛОСОВАНИЕ", () => CastVote(-1), new Color(0.24f, 0.27f, 0.36f, 0.96f), 24, 16);

            // ---- ejection overlay ----
            var eject = UIKit.Stretch(_root, "Eject");
            _ejectPanel = UIKit.PanelStretch(eject, "Bg", new Color(0.01f, 0.015f, 0.03f, 0.98f), 0);
            _ejectActor = UIKit.Node(eject, "Actor", new Vector2(-500f, 0f), new Vector2(120f, 120f));
            var body = _ejectActor.gameObject.AddComponent<Image>();
            body.sprite = Art.Circle(128);
            body.color = Color.white;
            _ejectText = UIKit.Label(eject, "", new Vector2(0f, -240f), new Vector2(1300f, 90f), 40,
                TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);
            _ejectPanel.gameObject.SetActive(false);

            GameEvents.PhaseChanged += OnPhase;
            GameEvents.Chat += OnChatLine;
            GameEvents.Ejected += OnEjected;
        }

        private void OnDestroy()
        {
            GameEvents.PhaseChanged -= OnPhase;
            GameEvents.Chat -= OnChatLine;
            GameEvents.Ejected -= OnEjected;
        }

        // ==================================================================
        private void OnPhase(MatchPhase phase)
        {
            bool show = phase == MatchPhase.MeetingIntro || phase == MatchPhase.Discussion ||
                        phase == MatchPhase.Voting || phase == MatchPhase.VoteResult || phase == MatchPhase.Ejection;
            _root.gameObject.SetActive(show);
            if (!show) return;

            if (phase == MatchPhase.MeetingIntro)
            {
                BuildGrid();
                ClearChat();
                var meeting = _match.Meeting;
                _title.text = meeting.IsEmergency
                    ? "ЭКСТРЕННОЕ СОБРАНИЕ"
                    : "НАЙДЕНО ТЕЛО: " + (meeting.BodyVictim != null ? meeting.BodyVictim.ColorName : "?");
                _ejectPanel.gameObject.SetActive(false);
            }

            _phaseLabel.text = phase switch
            {
                MatchPhase.MeetingIntro => "Все собираются...",
                MatchPhase.Discussion => "Обсуждение",
                MatchPhase.Voting => "Голосование",
                MatchPhase.VoteResult => "Подсчёт голосов",
                MatchPhase.Ejection => "",
                _ => "",
            };

            bool votingOpen = phase == MatchPhase.Voting;
            _skipButton.gameObject.SetActive(votingOpen && _match.Local != null && _match.Local.IsAlive);
            foreach (var slot in _slots)
                if (slot.VoteButton != null) slot.VoteButton.gameObject.SetActive(votingOpen && CanVote(slot));

            if (phase == MatchPhase.VoteResult) ShowVoteResults();
        }

        private bool CanVote(Slot slot)
        {
            var local = _match.Local;
            return local != null && local.IsAlive && slot.Player.IsAlive && !_match.Meeting.Votes.ContainsKey(local.Id);
        }

        private void BuildGrid()
        {
            for (int i = _grid.childCount - 1; i >= 0; i--) Destroy(_grid.GetChild(i).gameObject);
            _slots.Clear();

            int count = _match.Players.Count;
            int cols = count > 8 ? 2 : 1;
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));

            for (int i = 0; i < count; i++)
            {
                var player = _match.Players[i];
                int col = i % cols, row = i / cols;

                // Карточка занимает свою клетку в долях сетки: ширина подстраивается
                // под экран, и текст внутри больше не наползает на соседей.
                var cell = UIKit.Region(_grid, "Slot" + i,
                    new Vector2(col / (float)cols, 1f - (row + 1f) / rows),
                    new Vector2((col + 1f) / cols, 1f - row / (float)rows), 6f);
                var card = UIKit.PanelStretch(cell, "Bg", new Color(0.10f, 0.13f, 0.19f, 0.96f), 14);

                var chipRt = UIKit.Anchored(cell, "Chip", new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(54f, 54f));
                chipRt.pivot = new Vector2(0f, 0.5f);
                var chip = chipRt.gameObject.AddComponent<Image>();
                chip.sprite = Art.Circle(96);
                chip.color = player.Color;
                chip.raycastTarget = false;

                // справа держим место под кнопку голоса и метки, слева — под фишку
                var name = UIKit.RowLabel(cell, player.Label, 82f, 250f, 13f, 32, 24,
                    TextAnchor.MiddleLeft, Art.TextMain, FontStyle.Bold);
                UIKit.RowLabel(cell, player.ColorName, 82f, 250f, -16f, 26, 18,
                    TextAnchor.MiddleLeft, Art.TextDim);

                var voteMarks = UIKit.Anchored(cell, "Marks", new Vector2(1f, 1f), new Vector2(-14f, -8f), new Vector2(210f, 26f));
                voteMarks.pivot = new Vector2(1f, 1f);

                Button voteButton = null;
                if (player.IsAlive)
                {
                    int targetId = player.Id;
                    var vrt = UIKit.Anchored(cell, "Vote", new Vector2(1f, 0.5f), new Vector2(-14f, -8f), new Vector2(112f, 48f));
                    vrt.pivot = new Vector2(1f, 0.5f);
                    voteButton = UIKit.ButtonIn(vrt, "ГОЛОС", () => CastVote(targetId),
                        new Color(0.24f, 0.42f, 0.62f, 0.96f), 18, 12);
                    voteButton.gameObject.SetActive(false);
                }

                Image deadOverlay = null;
                if (!player.IsAlive)
                {
                    deadOverlay = UIKit.PanelStretch(cell, "Dead", new Color(0f, 0f, 0f, 0.55f), 14);
                    // «ВЫБЫЛ» стоял ровно по центру карточки и ложился поверх имени
                    var drt = UIKit.Anchored(cell, "DeadLabel", new Vector2(1f, 0.5f), new Vector2(-14f, -8f), new Vector2(112f, 34f));
                    drt.pivot = new Vector2(1f, 0.5f);
                    var dt = drt.gameObject.AddComponent<Text>();
                    dt.font = Art.UiFont;
                    dt.fontSize = 20;
                    dt.fontStyle = FontStyle.Bold;
                    dt.alignment = TextAnchor.MiddleCenter;
                    dt.color = Art.Danger;
                    dt.text = "ВЫБЫЛ";
                    dt.raycastTarget = false;
                }

                _slots.Add(new Slot
                {
                    Player = player, Card = card, Chip = chip, Name = name,
                    VoteButton = voteButton, VoteMarks = voteMarks, DeadOverlay = deadOverlay,
                });
            }
        }

        private void CastVote(int targetId)
        {
            var local = _match.Local;
            if (local == null || !local.IsAlive) return;
            if (_match.Meeting.Votes.ContainsKey(local.Id)) return;
            if (PlayerController.Instance != null) PlayerController.Instance.SubmitVote(targetId);
            else if (!_match.CastVote(local, targetId)) return;
            foreach (var slot in _slots)
                if (slot.VoteButton != null) slot.VoteButton.gameObject.SetActive(false);
            _skipButton.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_root.gameObject.activeSelf) return;
            _timer.text = MathX.TimeString(_match.PhaseTimer);

            _refresh -= Time.deltaTime;
            if (_refresh > 0f) return;
            _refresh = 0.25f;

            // live vote markers
            if (_match.Phase == MatchPhase.Voting || _match.Phase == MatchPhase.VoteResult)
            {
                var counts = new Dictionary<int, int>();
                foreach (var kv in _match.Meeting.Votes)
                {
                    if (!counts.ContainsKey(kv.Value)) counts[kv.Value] = 0;
                    counts[kv.Value]++;
                }

                foreach (var slot in _slots)
                {
                    int n = counts.TryGetValue(slot.Player.Id, out int c) ? c : 0;
                    RefreshMarks(slot, n);
                }
            }
        }

        private void RefreshMarks(Slot slot, int votes)
        {
            if (slot.VoteMarks.childCount == votes) return;
            for (int i = slot.VoteMarks.childCount - 1; i >= 0; i--) Destroy(slot.VoteMarks.GetChild(i).gameObject);
            for (int i = 0; i < votes && i < 8; i++)
            {
                UIKit.Icon(slot.VoteMarks, "V", Art.Circle(32), new Vector2(-90f + i * 24f, 0f),
                    new Vector2(20f, 20f), _match.Settings.AnonymousVotes ? Art.TextDim : Art.AccentWarm);
            }
        }

        private void ShowVoteResults()
        {
            if (_match.Settings.AnonymousVotes) return;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _match.Meeting.Votes)
            {
                var voter = _match.PlayerById(kv.Key);
                var target = kv.Value < 0 ? null : _match.PlayerById(kv.Value);
                if (voter == null) continue;
                sb.Append(voter.ColorName).Append(" → ").Append(target != null ? target.ColorName : "пропуск").Append("   ");
            }
            AppendChat(null, sb.ToString(), true);
        }

        // ==================================================================
        private void OnChatSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var local = _match.Local;
            if (local == null) return;
            if (PlayerController.Instance != null) PlayerController.Instance.SubmitChat(text.Trim());
            else
            {
                bool ghost = local.IsGhost;
                _match.AddChat(local, text.Trim(), SpeechIntent.None, ghost);
                if (!ghost) _match.Debate?.OnPlayerChat(local, text.Trim());
            }
            _chatInput.text = "";
            _chatInput.ActivateInputField();
        }

        private void OnChatLine(PlayerState speaker, string text, bool ghost)
        {
            var local = _match.Local;
            if (ghost && (local == null || !local.IsGhost)) return;
            AppendChat(speaker, text, false);
        }

        private void ClearChat()
        {
            for (int i = _chatContent.childCount - 1; i >= 0; i--) Destroy(_chatContent.GetChild(i).gameObject);
            _chatCount = 0;
            _chatContent.sizeDelta = new Vector2(0f, 10f);
        }

        private void AppendChat(PlayerState speaker, string text, bool system)
        {
            string line = system
                ? $"<color=#8892A6>{text}</color>"
                : $"<color=#{ColorUtility.ToHtmlStringRGB(speaker != null ? speaker.Color : Color.white)}>{(speaker != null ? speaker.ColorName : "—")}</color>: {text}";

            // Раньше строка создавалась шириной 500 при растянутых по горизонтали
            // якорях: фактическая ширина выходила «ширина чата + 500», текст
            // помещался в одну строку, высота считалась по ней — а потом ширина
            // ужималась, текст переносился на две строки и наползал на следующую.
            // Теперь ширина выставляется до замера.
            var rt = UIKit.Node(_chatContent, "Line", Vector2.zero, new Vector2(0f, 30f));
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-16f, 30f);
            rt.anchoredPosition = new Vector2(0f, -_chatContent.sizeDelta.y);

            var label = rt.gameObject.AddComponent<Text>();
            label.font = Art.UiFont;
            label.fontSize = 21;
            label.color = Art.TextMain;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.text = line;

            float height = Mathf.Max(30f, label.preferredHeight + 10f);
            rt.sizeDelta = new Vector2(-16f, height);
            _chatContent.sizeDelta = new Vector2(0f, _chatContent.sizeDelta.y + height);
            _chatCount++;

            TrimChat();

            Canvas.ForceUpdateCanvases();
            if (_chatScroll != null) _chatScroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>За долгое собрание строк набирается много; держим ленту в разумных пределах.</summary>
        private void TrimChat()
        {
            const int maxLines = 70;
            while (_chatContent.childCount > maxLines)
            {
                var oldest = (RectTransform)_chatContent.GetChild(0);
                float h = oldest.sizeDelta.y;
                // сначала отцепляем, потом удаляем: Destroy отложен до конца кадра,
                // и childCount упал бы не сразу — цикл крутился бы вечно
                oldest.SetParent(null, false);
                Destroy(oldest.gameObject);
                _chatContent.sizeDelta = new Vector2(0f, Mathf.Max(0f, _chatContent.sizeDelta.y - h));
                for (int i = 0; i < _chatContent.childCount; i++)
                {
                    var child = (RectTransform)_chatContent.GetChild(i);
                    child.anchoredPosition = new Vector2(child.anchoredPosition.x, child.anchoredPosition.y + h);
                }
                _chatCount--;
            }
        }

        // ==================================================================
        private void OnEjected(PlayerState player, bool wasInfiltrator)
        {
            _ejectPanel.gameObject.SetActive(true);
            var img = _ejectActor.GetComponent<Image>();

            if (player == null)
            {
                img.color = new Color(1f, 1f, 1f, 0f);
                _ejectText.text = "Никто не был изгнан (пропуск голосования)";
                return;
            }

            img.color = player.Color;
            _ejectActor.anchoredPosition = new Vector2(-620f, 0f);
            var tween = UiTween.Ensure(gameObject);
            tween.Run(4.5f, t =>
            {
                _ejectActor.anchoredPosition = new Vector2(Mathf.Lerp(-620f, 720f, t), Mathf.Sin(t * 6f) * 40f);
                _ejectActor.localRotation = Quaternion.Euler(0f, 0f, t * 720f);
                _ejectActor.localScale = Vector3.one * Mathf.Lerp(1.4f, 0.5f, t);
            });

            string verdict = _match.Settings.ConfirmEjects
                ? (wasInfiltrator ? " был <color=#EA4B4F>ПРЕДАТЕЛЕМ</color>" : " не был предателем")
                : "";
            int left = 0;
            foreach (var p in _match.Players) if (p.IsAlive && p.Role == Role.Infiltrator) left++;
            string remaining = _match.Settings.ConfirmEjects && left > 0
                ? $"\nОсталось предателей: {left}" : "";
            _ejectText.text = player.Label + " выброшен в космос." + verdict + remaining;
        }
    }
}
