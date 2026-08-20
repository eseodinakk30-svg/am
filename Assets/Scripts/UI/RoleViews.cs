// -----------------------------------------------------------------------------
//  NEBULA NINE - окна профессий: показатели жизни у Учёного и выбор облика
//  у Оборотня. Оба живут по тем же правилам, что и остальные модальные окна.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Gameplay;

namespace Nebula.UI
{
    // ============================================================ показатели жизни
    public class VitalsView : ModalBase
    {
        private MatchManager _match;
        private readonly List<Text> _names = new List<Text>();
        private readonly List<Image> _lamps = new List<Image>();
        private readonly List<Image> _cells = new List<Image>();
        private Text _charge;
        private float _timer;

        public static VitalsView Create(Transform parent, MatchManager match)
        {
            var go = new GameObject("VitalsView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<VitalsView>();
            view._match = match;
            view.BuildUi();
            go.SetActive(false);
            return view;
        }

        private void BuildUi()
        {
            BuildFrame(transform, "ПОКАЗАТЕЛИ ЖИЗНИ", new Vector2(1180f, 720f), Close);

            _charge = UIKit.Label(Panel, "", new Vector2(-430f, 300f), new Vector2(300f, 40f), 24,
                                  TextAnchor.MiddleLeft, new Color(0.55f, 0.86f, 0.72f));

            // сетка на пятнадцать мест — по числу возможных участников
            for (int i = 0; i < 15; i++)
            {
                int col = i % 3, row = i / 3;
                var pos = new Vector2(-360f + col * 360f, 200f - row * 108f);
                var cell = UIKit.Panel(Panel, "Cell", pos, new Vector2(330f, 92f),
                                       new Color(0.06f, 0.08f, 0.12f, 0.96f), 16);
                _cells.Add(cell);

                var lamp = UIKit.Panel(cell.transform, "Lamp", new Vector2(-128f, 0f), new Vector2(34f, 34f),
                                       new Color(0.35f, 0.85f, 0.45f), 17);
                _lamps.Add(lamp);

                var name = UIKit.Label(cell.transform, "", new Vector2(22f, 0f), new Vector2(270f, 44f), 26,
                                       TextAnchor.MiddleLeft, Art.TextMain);
                _names.Add(name);
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

            var local = _match != null ? _match.Local : null;
            if (local == null) { Close(); return; }

            // заряд тратится, пока окно открыто, — подсматривать бесконечно нельзя
            float total = Mathf.Max(1f, _match.Settings.ScientistVitalsSeconds);
            local.VitalsCharge = Mathf.Max(0f, local.VitalsCharge - Time.unscaledDeltaTime / total);
            if (local.VitalsCharge <= 0f) { Close(); return; }

            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f) { _timer = 0.35f; Refresh(); }
        }

        private void Refresh()
        {
            var local = _match.Local;
            if (local != null)
                _charge.text = "Заряд: " + Mathf.CeilToInt(local.VitalsCharge * 100f) + "%";

            var players = _match.Players;
            for (int i = 0; i < _cells.Count; i++)
            {
                bool has = i < players.Count;
                _cells[i].gameObject.SetActive(has);
                if (!has) continue;

                var p = players[i];
                _names[i].text = p.Name;
                bool alive = p.IsAlive;
                _lamps[i].color = alive ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.88f, 0.26f, 0.28f);
                _names[i].color = alive ? Art.TextMain : new Color(0.72f, 0.42f, 0.44f);
            }
        }
    }

    // ============================================================ выбор облика
    public class ShapeshiftView : ModalBase
    {
        private MatchManager _match;
        private readonly List<UnityEngine.UI.Button> _buttons = new List<UnityEngine.UI.Button>();
        private readonly List<Text> _labels = new List<Text>();
        private readonly List<PlayerState> _targets = new List<PlayerState>();

        public static ShapeshiftView Create(Transform parent, MatchManager match)
        {
            var go = new GameObject("ShapeshiftView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<ShapeshiftView>();
            view._match = match;
            view.BuildUi();
            go.SetActive(false);
            return view;
        }

        private void BuildUi()
        {
            BuildFrame(transform, "ПРИНЯТЬ ОБЛИК", new Vector2(1180f, 720f), Close);

            for (int i = 0; i < 14; i++)
            {
                int col = i % 2, row = i / 2;
                var pos = new Vector2(-280f + col * 560f, 220f - row * 82f);
                int index = i;
                var btn = UIKit.Button(Panel, "", pos, new Vector2(520f, 68f),
                                       () => Pick(index), new Color(0.22f, 0.13f, 0.26f, 0.95f), 26, 14);
                _buttons.Add(btn);
                _labels.Add(btn.GetComponentInChildren<Text>());
            }
        }

        public void Open()
        {
            var local = _match != null ? _match.Local : null;
            if (local == null || local.Special != SpecialRole.Shapeshifter) return;

            _targets.Clear();
            foreach (var p in _match.Players)
            {
                if (p == null || p == local || !p.IsAlive) continue;
                if (p.Role == Role.Infiltrator) continue;   // прятаться за своим же нет смысла
                _targets.Add(p);
            }

            for (int i = 0; i < _buttons.Count; i++)
            {
                bool has = i < _targets.Count;
                _buttons[i].gameObject.SetActive(has);
                if (has) _labels[i].text = _targets[i].Name;
            }

            gameObject.SetActive(true);
        }

        private void Pick(int index)
        {
            var local = _match != null ? _match.Local : null;
            if (local == null || index < 0 || index >= _targets.Count) { Close(); return; }
            _match.BeginShapeshift(local, _targets[index]);
            Close();
        }
    }
}
