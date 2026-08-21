// -----------------------------------------------------------------------------
//  NEBULA NINE - in-game HUD.
//
//  Landscape mobile layout: floating stick on the left, action cluster bottom
//  right, objectives top left, minimap top right, alerts across the top.
//  Buttons are 130-180 px on a 1600x900 design canvas, i.e. comfortably
//  thumb-sized on a phone.
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
    public class HudController : MonoBehaviour
    {
        private MatchManager _match;
        private PlayerController _player;
        private RectTransform _root;

        private VirtualJoystick _stick;
        private Image _useButton, _killButton, _reportButton, _ventButton, _sabotageButton, _mapButton, _emergencyButton;
        private Text _useLabel, _killLabel, _reportLabel, _ventLabel, _sabotageLabel, _emergencyLabel;
        private Image _killCooldownRing;
        private Text _taskListText, _roleText, _alertText, _toastText, _timerText;
        private Image _progressBar, _alertPanel, _toastPanel, _repairPanel, _repairFill;
        private Text _repairLabel;
        private MinimapView _minimap;
        private RectTransform _ventPanel;
        private RectTransform _taskPanel;
        private RectTransform _taskBody;
        private Text _collapseMark;
        private RectTransform _questArrow;
        private Image _questArrowImage;
        private bool _tasksCollapsed;
        private Image _roleButton;          // «ЖИЗНИ» у учёного, «ОБЛИК» у оборотня
        private Text _roleButtonLabel;
        private float _toastTimer;
        private float _roleRevealTimer;

        public static HudController Create(Transform parent, MatchManager match, PlayerController player)
        {
            var rt = UIKit.Stretch(parent, "HUD");
            var hud = rt.gameObject.AddComponent<HudController>();
            hud._root = rt;
            hud._match = match;
            hud._player = player;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            bool leftHanded = GameSettings.Profile.LeftHandedUi;
            _stick = VirtualJoystick.Create(_root, leftHanded);

            float sx = leftHanded ? -1f : 1f;
            var anchor = leftHanded ? new Vector2(0f, 0f) : new Vector2(1f, 0f);

            _useButton = UIKit.CircleButton(_root, "ДЕЙСТВИЕ", anchor, new Vector2(sx * -150f, 150f), 180f,
                new Color(0.24f, 0.62f, 0.85f, 0.92f), () => _player.DoUse(), out _useLabel);
            var useRelay = UIKit.AddPointer(_useButton.gameObject);
            useRelay.Down += _ => _player.UseHeld = true;
            useRelay.Dragged += _ => _player.UseHeld = true;

            _reportButton = UIKit.CircleButton(_root, "ДОКЛАД", anchor, new Vector2(sx * -150f, 350f), 130f,
                new Color(0.85f, 0.72f, 0.25f, 0.92f), () => _player.DoReport(), out _reportLabel);

            _killButton = UIKit.CircleButton(_root, "УБИТЬ", anchor, new Vector2(sx * -330f, 170f), 150f,
                new Color(0.86f, 0.24f, 0.26f, 0.92f), () => _player.DoKill(), out _killLabel);
            _killCooldownRing = UIKit.Icon(_killButton.transform, "Cd", Art.Circle(160), Vector2.zero,
                new Vector2(150f, 150f), new Color(0f, 0f, 0f, 0.55f));
            _killCooldownRing.type = Image.Type.Filled;
            _killCooldownRing.fillMethod = Image.FillMethod.Radial360;
            _killCooldownRing.fillOrigin = 2;
            _killCooldownRing.fillAmount = 0f;

            _ventButton = UIKit.CircleButton(_root, "ВЕНТ", anchor, new Vector2(sx * -330f, 350f), 120f,
                new Color(0.65f, 0.35f, 0.20f, 0.92f), () => _player.DoVent(), out _ventLabel);

            _sabotageButton = UIKit.CircleButton(_root, "САБОТАЖ", anchor, new Vector2(sx * -150f, 520f), 120f,
                new Color(0.55f, 0.20f, 0.45f, 0.92f), () => _player.OpenSabotage(), out _sabotageLabel);

            _emergencyButton = UIKit.CircleButton(_root, "СБОР", anchor, new Vector2(sx * -480f, 160f), 120f,
                new Color(0.90f, 0.42f, 0.18f, 0.92f), () => _player.DoEmergency(), out _emergencyLabel);

            // Одна кнопка на профессию: у учёного это показатели жизни, у оборотня —
            // выбор облика. Обычному экипажу и рядовому диверсанту она не показывается.
            _roleButton = UIKit.CircleButton(_root, "", anchor, new Vector2(sx * -480f, 330f), 120f,
                new Color(0.28f, 0.55f, 0.48f, 0.92f), () => _player.UseRoleAbility(), out _roleButtonLabel);

            // ---- objectives ------------------------------------------------
            // Список заданий сворачивается по шапке: он закрывал левую треть
            // экрана и мешал смотреть, что происходит вокруг.
            _taskPanel = UIKit.Anchored(_root, "Tasks", new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(460f, 372f));
            UIKit.PanelStretch(_taskPanel, "Bg", new Color(0.04f, 0.06f, 0.10f, 0.82f), 14);

            var headerRt = UIKit.Row(_taskPanel, "Header", 6f, 6f, 0f, 46f);
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -6f);
            UIKit.ButtonIn(headerRt, "", ToggleTasks, new Color(0.09f, 0.13f, 0.20f, 0.92f), 20, 10);

            _roleText = UIKit.RowLabel(headerRt, "", 14f, 54f, 0f, 40f, 23,
                TextAnchor.MiddleLeft, Art.TextMain, FontStyle.Bold);
            _collapseMark = UIKit.RowLabel(headerRt, "▲", 0f, 14f, 0f, 40f, 22,
                TextAnchor.MiddleRight, Art.TextDim, FontStyle.Bold);

            _taskBody = UIKit.Region(_taskPanel, "Body", new Vector2(0f, 0f), new Vector2(1f, 1f), 0f);
            _taskBody.offsetMax = new Vector2(-6f, -56f);
            _taskBody.offsetMin = new Vector2(6f, 8f);

            var barRt = UIKit.Row(_taskBody, "Bar", 8f, 8f, 0f, 18f);
            barRt.anchorMin = new Vector2(0f, 1f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(0.5f, 1f);
            barRt.anchoredPosition = new Vector2(0f, -4f);
            _progressBar = UIKit.BarIn(barRt, new Color(0f, 0f, 0f, 0.5f), Art.Good, 6);

            var listRt = UIKit.Region(_taskBody, "List", new Vector2(0f, 0f), new Vector2(1f, 1f), 0f);
            listRt.offsetMax = new Vector2(-8f, -30f);
            listRt.offsetMin = new Vector2(8f, 4f);
            _taskListText = listRt.gameObject.AddComponent<Text>();
            _taskListText.font = Art.UiFont;
            _taskListText.fontSize = 21;
            _taskListText.color = Art.TextMain;
            _taskListText.alignment = TextAnchor.UpperLeft;
            _taskListText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _taskListText.verticalOverflow = VerticalWrapMode.Truncate;
            _taskListText.supportRichText = true;
            _taskListText.raycastTarget = false;

            // ---- minimap ---------------------------------------------------
            _minimap = MinimapView.Create(_root, _match, Vector2.zero, new Vector2(430f, 275f), true);
            var mm = (RectTransform)_minimap.transform;
            mm.anchorMin = mm.anchorMax = new Vector2(1f, 1f);
            mm.pivot = new Vector2(1f, 1f);
            mm.anchoredPosition = new Vector2(-24f, -24f);

            // Миникарта занимает сверху 275 единиц, а кнопка стояла на -270 с
            // якорем в правом верхнем углу — то есть налезала на её нижний край.
            _mapButton = UIKit.CircleButton(_root, "КАРТА", new Vector2(1f, 1f), new Vector2(-24f, -315f), 96f,
                new Color(0.2f, 0.24f, 0.32f, 0.9f), () => UiRoot.Instance?.ToggleBigMap(), out _);

            // ---- alerts ----------------------------------------------------
            // Баннер и всплывашка шириной 900 и 760 стояли по центру и на телефоне
            // залезали слева на список заданий, справа на миникарту. Растягиваем их
            // между боковыми панелями, а не задаём ширину числом.
            _alertPanel = TopBanner("Alert", -20f, 74f, new Color(0.55f, 0.10f, 0.12f, 0.92f),
                                    out _alertText, 26, Color.white, FontStyle.Bold);
            _toastPanel = TopBanner("Toast", -160f, 62f, new Color(0.06f, 0.08f, 0.13f, 0.9f),
                                    out _toastText, 24, Art.TextMain, FontStyle.Normal);

            _timerText = UIKit.Label(_root, "", Vector2.zero, new Vector2(400f, 50f), 30, TextAnchor.MiddleCenter, Art.AccentWarm, FontStyle.Bold);
            var tr = _timerText.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.anchoredPosition = new Vector2(0f, -104f);

            // ---- repair progress ------------------------------------------
            var repair = UIKit.Anchored(_root, "Repair", new Vector2(0.5f, 0f), new Vector2(0f, 300f), new Vector2(560f, 70f));
            _repairPanel = UIKit.PanelStretch(repair, "Bg", new Color(0.05f, 0.08f, 0.12f, 0.9f), 12);
            // Полоса и надпись «УДЕРЖИВАЙ ДЕЙСТВИЕ» были соседями фона, а гасили
            // именно фон — поэтому они висели на экране весь матч.
            _repairFill = UIKit.Bar(_repairPanel.transform, Vector2.zero, new Vector2(540f, 42f), new Color(0f, 0f, 0f, 0.4f), Art.Accent, 8);
            _repairFill.rectTransform.anchorMin = _repairFill.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _repairLabel = UIKit.Label(_repairPanel.transform, "УДЕРЖИВАЙ ДЕЙСТВИЕ", Vector2.zero, new Vector2(540f, 60f), 22,
                TextAnchor.MiddleCenter, Color.white);
            _repairLabel.rectTransform.anchorMin = _repairLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _repairPanel.gameObject.SetActive(false);

            // ---- vent hop panel -------------------------------------------
            _ventPanel = UIKit.Anchored(_root, "VentHops", new Vector2(0.5f, 0f), new Vector2(0f, 380f), new Vector2(900f, 90f));
            _ventPanel.gameObject.SetActive(false);

            // ---- указатель на задание ---------------------------------------
            // Станция стала большой, и без стрелки к ближайшей цели новичок
            // просто наматывает круги. При активном саботаже стрелка ведёт к нему.
            _questArrow = UIKit.Anchored(_root, "Quest", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 54f));
            _questArrowImage = _questArrow.gameObject.AddComponent<Image>();
            _questArrowImage.sprite = Art.Triangle(64);
            _questArrowImage.color = new Color(0.98f, 0.82f, 0.28f, 0.85f);
            _questArrowImage.raycastTarget = false;
            _questArrow.gameObject.SetActive(false);

            GameEvents.Announce += ShowToast;
            GameEvents.PhaseChanged += OnPhase;
        }

        /// <summary>
        /// Полоса под верхней кромкой, зажатая между списком заданий слева и
        /// миникартой справа. Ширина считается от краёв экрана, поэтому на любом
        /// соотношении сторон полоса не накрывает боковые панели.
        /// </summary>
        private Image TopBanner(string name, float y, float height, Color bg,
                                out Text label, int fontSize, Color textColor, FontStyle style)
        {
            const float LeftClear = 500f;    // список заданий: 20 + 460 + зазор
            const float RightClear = 478f;   // миникарта: 24 + 430 + зазор

            var rt = UIKit.Node(_root, name, Vector2.zero, new Vector2(0f, height));
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(LeftClear, 0f);
            rt.offsetMax = new Vector2(-RightClear, 0f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);

            var panel = UIKit.PanelStretch(rt, "Bg", bg, 14);

            // Текст обязан быть ребёнком фона, а не его соседом: панель гасят через
            // SetActive на фоне, и надпись-сосед оставалась висеть на экране.
            var trt = UIKit.Node(panel.transform, "Text", Vector2.zero, Vector2.zero);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 4f);
            trt.offsetMax = new Vector2(-14f, -4f);
            label = trt.gameObject.AddComponent<Text>();
            label.font = Art.UiFont;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = textColor;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = fontSize;
            label.raycastTarget = false;

            panel.gameObject.SetActive(false);
            return panel;
        }

        private void OnDestroy()
        {
            GameEvents.Announce -= ShowToast;
            GameEvents.PhaseChanged -= OnPhase;
        }

        private void OnPhase(MatchPhase phase)
        {
            bool inRound = phase == MatchPhase.Roaming || phase == MatchPhase.RoleReveal;
            bool lobby = phase == MatchPhase.Lobby;

            // На собрании HUD прятался только частично: список заданий, миникарта,
            // джойстик и подсказки просвечивали сквозь экран голосования и лезли
            // поверх карточек. Убираем целиком — там он не нужен.
            bool meeting = phase == MatchPhase.MeetingIntro || phase == MatchPhase.Discussion
                           || phase == MatchPhase.Voting || phase == MatchPhase.VoteResult
                           || phase == MatchPhase.Ejection || phase == MatchPhase.GameOver;
            _root.gameObject.SetActive(!meeting);
            if (meeting)
            {
                if (_toastPanel != null) _toastPanel.gameObject.SetActive(false);
                if (_repairPanel != null) _repairPanel.gameObject.SetActive(false);
                _toastTimer = 0f;
                return;
            }

            // В лобби из всего управления нужны только стик и «ДЕЙСТВИЕ» для ноутбука.
            foreach (var img in new[] { _killButton, _reportButton, _ventButton, _sabotageButton, _emergencyButton, _roleButton })
                if (img != null) img.gameObject.SetActive(inRound);
            if (_useButton != null) _useButton.gameObject.SetActive(inRound || lobby);

            // список заданий и полоска прогресса в комнате ожидания ни к чему
            if (_roleText != null) _roleText.transform.parent.gameObject.SetActive(!lobby);
            if (_minimap != null) _minimap.gameObject.SetActive(!lobby);

            if (phase == MatchPhase.RoleReveal) _roleRevealTimer = 4f;
        }

        private void ToggleTasks()
        {
            _tasksCollapsed = !_tasksCollapsed;
            _taskBody.gameObject.SetActive(!_tasksCollapsed);
            _taskPanel.sizeDelta = new Vector2(460f, _tasksCollapsed ? 58f : 372f);
            if (_collapseMark != null) _collapseMark.text = _tasksCollapsed ? "▼" : "▲";
        }

        public void ShowToast(string text, float seconds)
        {
            if (_toastPanel == null) return;
            _toastPanel.gameObject.SetActive(true);
            _toastText.text = text;
            _toastTimer = seconds;
        }

        // ==================================================================
        private void Update()
        {
            if (_match == null || _player == null) return;
            var local = _match.Local;
            if (local == null) return;

            _player.StickInput = _stick != null ? _stick.Value : Vector2.zero;

            UpdateButtons(local);
            UpdateObjectives(local);
            UpdateAlerts();
            UpdateRepair(local);
            UpdateVentPanel(local);
            UpdateQuestArrow(local);

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.deltaTime;
                if (_toastTimer <= 0f) _toastPanel.gameObject.SetActive(false);
            }
        }

        private void UpdateButtons(PlayerState local)
        {
            bool impostor = local.Role == Role.Infiltrator;
            bool alive = local.IsAlive;
            bool vents = local.CanUseVents;   // диверсанты и инженер

            _killButton.gameObject.SetActive(impostor && alive);
            _ventButton.gameObject.SetActive(vents && alive);
            _sabotageButton.gameObject.SetActive(impostor && alive);

            if (impostor && alive)
            {
                bool canKill = _player.CanKill(out _);
                SetInteractable(_killButton, canKill);
                float cd = local.KillCooldown;
                _killCooldownRing.fillAmount = _match.Settings.KillCooldown > 0.01f
                    ? Mathf.Clamp01(cd / _match.Settings.KillCooldown) : 0f;
                _killLabel.text = cd > 0.05f ? Mathf.CeilToInt(cd).ToString() : "УБИТЬ";
                SetInteractable(_sabotageButton, _player.CanSabotage());
            }

            if (vents && alive)
            {
                SetInteractable(_ventButton, _player.CanVent());
                _ventLabel.text = local.InVent ? "ВЫЙТИ" : "ВЕНТ";
            }

            UpdateRoleButton(local, alive);

            bool canReport = _player.CanReport(out _);
            _reportButton.gameObject.SetActive(alive);
            SetInteractable(_reportButton, canReport);

            bool canEmergency = _player.CanEmergency();
            _emergencyButton.gameObject.SetActive(alive);
            SetInteractable(_emergencyButton, canEmergency);
            _emergencyLabel.text = local.EmergenciesUsed >= _match.Settings.EmergencyMeetingsPerPlayer ? "0" : "СБОР";

            // Подпись кнопки действия обязана повторять порядок PlayerController.DoUse,
            // иначе она обещает одно, а нажатие делает другое: на посту наблюдения
            // рядом с консолью задания надпись говорила «ЗАДАНИЕ», а открывались камеры.
            string useLabel = "ДЕЙСТВИЕ";
            bool useEnabled = false;
            var sab = _match.Sabotage;
            var security = StationLayout.Get("security");
            var command = StationLayout.Get("command");
            var comms = StationLayout.Get("comms");
            bool ghostTasks = local.IsGhost && _match.Settings != null && _match.Settings.GhostsDoTasks;
            var lift = StationView.Instance != null && !local.InVent
                ? StationView.Instance.NearestElevator(local.Position, local.Deck, 3.2f) : null;

            if (_match.Phase == MatchPhase.Lobby)
            {
                // в комнате ожидания кнопка открывает консоль у ноутбука; без этой
                // ветки она оставалась серой, а подсказка звала на неё нажать
                bool nearLaptop = _player.IsNearLobbyLaptop != null && _player.IsNearLobbyLaptop();
                useLabel = "КОНСОЛЬ";
                useEnabled = nearLaptop;
            }
            else if (sab != null && sab.IsActive && sab.PanelIndexNear(local.Position, local.Deck) >= 0)
            {
                // ремонт идёт удержанием той же кнопки
                useLabel = "ЧИНИТЬ";
                useEnabled = alive;
            }
            else if (lift != null && lift.Cooldown <= 0f && alive)
            {
                useLabel = "ЛИФТ";
                useEnabled = true;
            }
            else if (alive && security != null && local.RoomId == security.Id)
            {
                useLabel = "КАМЕРЫ";
                useEnabled = true;
            }
            else if (alive && comms != null && local.RoomId == comms.Id
                     && (sab == null || !sab.CommsDown)
                     && (_match.Tasks == null || _match.Tasks.FindTaskInRange(local, 2.4f) == null))
            {
                useLabel = "ЖУРНАЛ";
                useEnabled = true;
            }
            else if (alive && command != null && local.RoomId == command.Id
                     && (sab == null || !sab.CommsDown)
                     && (_match.Tasks == null || _match.Tasks.FindTaskInRange(local, 2.4f) == null))
            {
                useLabel = "АДМИН";
                useEnabled = true;
            }
            else if (!local.InVent && (alive || ghostTasks) && _match.Tasks != null
                     && _match.Tasks.FindTaskInRange(local) != null)
            {
                useLabel = "ЗАДАНИЕ";
                useEnabled = true;
            }
            _useLabel.text = useLabel;
            SetInteractable(_useButton, useEnabled);
        }

        private static void SetInteractable(Image button, bool on)
        {
            if (button == null) return;
            var btn = button.GetComponent<Button>();
            if (btn != null) btn.interactable = on;
            var c = button.color;
            c.a = on ? 0.94f : 0.34f;
            button.color = c;
        }

        /// <summary>
        /// Стрелка к ближайшей цели. Камера смотрит сверху без рыскания, поэтому
        /// мировые X и Z ложатся прямо на экранные вправо и вверх — направление
        /// считается без проекции.
        /// </summary>
        private void UpdateQuestArrow(PlayerState local)
        {
            if (_questArrow == null) return;

            if (_match.Phase != MatchPhase.Roaming || !local.IsAlive || local.InVent)
            {
                _questArrow.gameObject.SetActive(false);
                return;
            }

            bool sabotage = false;
            if (!TryFindGuidance(local, out var target, out sabotage))
            {
                _questArrow.gameObject.SetActive(false);
                return;
            }

            var d = target - local.Position;
            d.y = 0f;
            if (d.sqrMagnitude < 9f)          // уже пришли — стрелка только мешает
            {
                _questArrow.gameObject.SetActive(false);
                return;
            }

            d.Normalize();
            _questArrow.gameObject.SetActive(true);
            _questArrow.anchoredPosition = new Vector2(d.x, d.z) * 190f;
            _questArrow.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            _questArrowImage.color = sabotage
                ? new Color(0.96f, 0.32f, 0.30f, 0.9f)
                : new Color(0.98f, 0.82f, 0.28f, 0.85f);
        }

        /// <summary>Куда вести игрока: сперва критический саботаж, потом ближайшее задание.</summary>
        private bool TryFindGuidance(PlayerState local, out Vector3 target, out bool sabotage)
        {
            target = Vector3.zero;
            sabotage = false;

            var sab = _match.Sabotage;
            if (sab != null && sab.IsActive && sab.Panels.Count > 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < sab.Panels.Count; i++)
                {
                    if (sab.Panels[i].Done) continue;
                    if (sab.Panels[i].Deck != local.Deck) continue;
                    float dist = (sab.Panels[i].Position - local.Position).sqrMagnitude;
                    if (dist < best) { best = dist; target = sab.Panels[i].Position; sabotage = true; }
                }
                if (sabotage) return true;
            }

            // предателю задания не нужны — у него свои дела
            if (local.Role == Role.Infiltrator) return false;

            // при потерянной связи список заданий скрыт — стрелка не должна его выдавать
            if (sab != null && sab.CommsDown) return false;

            var tasks = _match.Tasks;
            if (tasks == null) return false;

            float nearest = float.MaxValue;
            bool found = false;
            bool otherDeck = false;
            foreach (var task in local.Tasks)
            {
                if (task == null || task.IsComplete) continue;
                var area = StationLayout.Get(task.CurrentRoomId);
                if (area == null) continue;
                if (area.Deck != local.Deck) { otherDeck = true; continue; }
                // ведём к самой консоли, а не к центру комнаты: станции стоят по
                // периметру, и центр может быть в полутора десятках метров от них
                var pos = tasks.NextStationFor(task);
                float dist = (pos - local.Position).sqrMagnitude;
                if (dist < nearest) { nearest = dist; target = pos; found = true; }
            }
            if (found) return true;

            // все оставшиеся задания на другой палубе — ведём к ближайшему лифту,
            // иначе стрелка просто пропадает и игрок не понимает, куда идти
            if (otherDeck) return TryFindElevator(local, out target);
            return false;
        }

        /// <summary>Ближайший лифт на палубе игрока — переход между палубами.</summary>
        private bool TryFindElevator(PlayerState local, out Vector3 target)
        {
            target = Vector3.zero;
            var view = StationView.Instance;
            if (view == null) return false;
            var pad = view.NearestElevator(local.Position, local.Deck, 500f);
            if (pad == null) return false;
            target = pad.transform.position;
            return true;
        }

        private void UpdateRoleButton(PlayerState local, bool alive)
        {
            if (_roleButton == null) return;

            bool scientist = local.Special == SpecialRole.Scientist;
            bool shifter = local.Special == SpecialRole.Shapeshifter;
            bool show = alive && (scientist || shifter);
            _roleButton.gameObject.SetActive(show);
            if (!show) return;

            if (scientist)
            {
                _roleButton.color = new Color(0.28f, 0.55f, 0.48f, 0.92f);
                _roleButtonLabel.text = "ЖИЗНИ";
                SetInteractable(_roleButton, local.VitalsCharge > 0.05f);
            }
            else
            {
                bool disguised = local.DisguisedAs >= 0;
                _roleButton.color = disguised
                    ? new Color(0.62f, 0.30f, 0.34f, 0.92f)
                    : new Color(0.48f, 0.22f, 0.52f, 0.92f);
                _roleButtonLabel.text = disguised
                    ? "СНЯТЬ " + Mathf.CeilToInt(local.ShapeshiftLeft)
                    : local.ShapeshiftCooldown > 0.05f
                        ? Mathf.CeilToInt(local.ShapeshiftCooldown).ToString()
                        : "ОБЛИК";
                // пока облик надет, кнопка обязана оставаться нажимаемой — иначе
                // снять его досрочно нельзя, а CanShapeshift в этот момент ложна
                SetInteractable(_roleButton, disguised || _match.CanShapeshift(local));
            }
        }

        private void UpdateObjectives(PlayerState local)
        {
            if (_roleText == null) return;

            bool impostor = local.Role == Role.Infiltrator;
            string tint = impostor ? "#EA4B4F" : "#5ED27E";
            string roleName = "<color=" + tint + ">" + MatchManager.RoleTitle(local) + "</color>";
            string status = local.IsGhost ? " (призрак)" : "";
            if (local.DisguisedAs >= 0) status += " — в чужом облике";
            _roleText.text = roleName + status;

            _progressBar.fillAmount = _match.Tasks != null ? _match.Tasks.CrewProgress : 0f;

            bool commsDown = _match.Sabotage != null && _match.Sabotage.CommsDown;
            if (commsDown)
            {
                _taskListText.text = "<color=#EA4B4F>СВЯЗЬ ПОТЕРЯНА\nСписок заданий недоступен</color>";
                return;
            }

            var sb = new System.Text.StringBuilder();
            if (impostor) sb.AppendLine("<color=#B9C2D6>Изображай работу:</color>");
            foreach (var task in local.Tasks)
            {
                sb.AppendLine(task.DisplayLine);
                if (sb.Length > 900) break;
            }
            _taskListText.text = sb.ToString();
        }

        private void UpdateAlerts()
        {
            var sab = _match.Sabotage;
            bool show = sab != null && sab.IsActive;
            if (_roleRevealTimer > 0f)
            {
                _roleRevealTimer -= Time.deltaTime;
                _alertPanel.gameObject.SetActive(true);
                var local = _match.Local;
                bool imp = local != null && local.Role == Role.Infiltrator;
                _alertPanel.color = imp ? new Color(0.55f, 0.10f, 0.12f, 0.94f) : new Color(0.10f, 0.35f, 0.45f, 0.94f);
                _alertText.text = MatchManager.RoleTitle(local) + " — " + MatchManager.RoleHint(local);
                _timerText.text = "";
                return;
            }

            _alertPanel.gameObject.SetActive(show);
            if (show)
            {
                _alertPanel.color = sab.IsCritical
                    ? new Color(0.62f, 0.10f, 0.12f, 0.92f)
                    : new Color(0.45f, 0.30f, 0.08f, 0.92f);
                _alertText.text = sab.ActiveDescription();
                _timerText.text = sab.IsCritical ? MathX.TimeString(sab.TimeLeft) : "";
            }
            else _timerText.text = "";
        }

        private void UpdateRepair(PlayerState local)
        {
            var sab = _match.Sabotage;
            if (sab == null || !sab.IsActive)
            {
                _repairPanel.gameObject.SetActive(false);
                return;
            }

            int panel = sab.PanelIndexNear(local.Position, local.Deck);
            if (panel < 0)
            {
                _repairPanel.gameObject.SetActive(false);
                return;
            }

            _repairPanel.gameObject.SetActive(true);
            _repairFill.fillAmount = sab.Panels[panel].HoldProgress;
            _repairLabel.text = sab.Mode == RepairMode.BothSimultaneous
                ? "УДЕРЖИВАЙ — нужен второй человек на другой панели"
                : "УДЕРЖИВАЙ ДЕЙСТВИЕ";
        }

        private int _ventPanelVentId = int.MinValue;

        private void UpdateVentPanel(PlayerState local)
        {
            bool show = local.InVent && local.CanUseVents;
            if (_ventPanel.gameObject.activeSelf != show) _ventPanel.gameObject.SetActive(show);
            if (!show) { _ventPanelVentId = int.MinValue; return; }

            var targets = _player.VentTargets();

            // Раньше кнопки пересобирались только при смене их количества. Внутри
            // одной сети вентиляции количество всегда одно и то же (все венты сети,
            // кроме текущего), поэтому после первого же перехода кнопки оставались
            // от прошлого венте: в списке был тот, где ты уже сидишь, и не было того,
            // откуда пришёл. Ключ — идентификатор текущего венте.
            if (_ventPanelVentId == local.VentId && _ventPanel.childCount == targets.Count) return;
            _ventPanelVentId = local.VentId;

            // сначала отвязываем, потом удаляем: Destroy отложен до конца кадра,
            // и childCount иначе остаётся прежним
            for (int i = _ventPanel.childCount - 1; i >= 0; i--)
            {
                var child = _ventPanel.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            float step = 240f;
            float start = -(targets.Count - 1) * step * 0.5f;
            for (int i = 0; i < targets.Count; i++)
            {
                var vent = targets[i];
                string name = StationLayout.NameOf(vent.RoomId);
                UIKit.Button(_ventPanel, name, new Vector2(start + i * step, 0f), new Vector2(step - 16f, 76f),
                    () => _player.HopVent(vent), new Color(0.35f, 0.22f, 0.16f, 0.94f), 22, 14);
            }
        }
    }
}
