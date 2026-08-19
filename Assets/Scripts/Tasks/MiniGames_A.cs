// -----------------------------------------------------------------------------
//  NEBULA NINE - mini-games, part 1: wiring, keypads, sliders, sequences,
//  hold-gauges and drag sorting.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Fx;
using Nebula.UI;

namespace Nebula.Tasks
{
    // ================================================================= wires
    /// <summary>Drag each coloured terminal on the left to its twin on the right.</summary>
    public class WireLinkGame : MiniGameBase
    {
        private readonly int _count;
        private readonly Color[] _palette =
        {
            new Color(0.92f, 0.29f, 0.31f), new Color(0.33f, 0.62f, 0.95f),
            new Color(0.98f, 0.79f, 0.25f), new Color(0.42f, 0.85f, 0.47f),
            new Color(0.75f, 0.45f, 0.95f),
        };

        private readonly List<Image> _left = new List<Image>();
        private readonly List<Image> _right = new List<Image>();
        private readonly List<int> _rightOrder = new List<int>();
        private readonly List<Image> _wires = new List<Image>();
        private bool[] _linked;
        private int _dragging = -1;
        private Image _rubber;

        public WireLinkGame(int count = 4) { _count = Mathf.Clamp(count, 3, 5); }

        public override string Instruction => "Соедини провода одного цвета";

        protected override void Build()
        {
            _linked = new bool[_count];
            for (int i = 0; i < _count; i++) _rightOrder.Add(i);
            Rng.Shuffle(_rightOrder);

            float step = 420f / Mathf.Max(1, _count - 1);
            float top = 210f;

            for (int i = 0; i < _count; i++)
            {
                var lp = new Vector2(-330f, top - step * i);
                var rp = new Vector2(330f, top - step * i);

                var l = UIKit.Panel(Root, "L" + i, lp, new Vector2(110f, 54f), _palette[i], 10);
                var r = UIKit.Panel(Root, "R" + i, rp, new Vector2(110f, 54f), _palette[_rightOrder[i]], 10);
                _left.Add(l);
                _right.Add(r);

                int index = i;
                var relay = UIKit.AddPointer(l.gameObject);
                relay.Down += _ => { _dragging = index; };
                relay.Dragged += e => DragWire(e);
                relay.DragEnded += e => EndDrag(e);
                relay.Up += e => EndDrag(e);
            }

            _rubber = UIKit.Icon(Root, "Rubber", Art.SolidSprite(), Vector2.zero, new Vector2(10f, 6f), Color.white);
            _rubber.gameObject.SetActive(false);
        }

        private void DragWire(PointerEventData e)
        {
            if (_dragging < 0 || _linked[_dragging]) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var local)) return;
            _rubber.gameObject.SetActive(true);
            _rubber.color = _palette[_dragging];
            StretchBetween(_rubber.rectTransform, _left[_dragging].rectTransform.anchoredPosition, local);
        }

        private void EndDrag(PointerEventData e)
        {
            if (_dragging < 0) { _rubber.gameObject.SetActive(false); return; }
            _rubber.gameObject.SetActive(false);

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var local))
            {
                for (int i = 0; i < _count; i++)
                {
                    if (_rightOrder[i] != _dragging) continue;
                    var target = _right[i].rectTransform.anchoredPosition;
                    if (Vector2.Distance(local, target) < 90f && !_linked[_dragging])
                    {
                        _linked[_dragging] = true;
                        var wire = UIKit.Icon(Root, "Wire", Art.SolidSprite(), Vector2.zero, new Vector2(10f, 8f), _palette[_dragging]);
                        wire.transform.SetSiblingIndex(0);
                        StretchBetween(wire.rectTransform, _left[_dragging].rectTransform.anchoredPosition, target);
                        _wires.Add(wire);
                        Progress();
                    }
                }
            }

            _dragging = -1;
            foreach (var l in _linked) if (!l) return;
            Complete();
        }

        private static void StretchBetween(RectTransform rt, Vector2 a, Vector2 b)
        {
            var delta = b - a;
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.sizeDelta = new Vector2(delta.magnitude, 9f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }

    // ================================================================= keypad
    /// <summary>Type the displayed access code on a numeric pad.</summary>
    public class KeypadGame : MiniGameBase
    {
        private readonly int _digits;
        private string _code = "";
        private string _entry = "";
        private Text _display;
        private Text _target;

        public KeypadGame(int digits = 5) { _digits = Mathf.Clamp(digits, 3, 6); }

        public override string Instruction => "Введи код доступа";

        protected override void Build()
        {
            for (int i = 0; i < _digits; i++) _code += Rng.Range(0, 10).ToString();

            UIKit.Panel(Root, "Screen", new Vector2(0f, 190f), new Vector2(520f, 90f), new Color(0.05f, 0.09f, 0.07f, 0.95f), 12);
            _target = UIKit.Label(Root, _code, new Vector2(0f, 190f), new Vector2(520f, 90f), 52,
                TextAnchor.MiddleCenter, new Color(0.42f, 0.95f, 0.55f), FontStyle.Bold);

            UIKit.Panel(Root, "Entry", new Vector2(0f, 95f), new Vector2(520f, 70f), new Color(0.06f, 0.07f, 0.11f, 0.95f), 12);
            _display = UIKit.Label(Root, "", new Vector2(0f, 95f), new Vector2(520f, 70f), 44,
                TextAnchor.MiddleCenter, Art.Accent, FontStyle.Bold);

            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "<" };
            for (int i = 0; i < keys.Length; i++)
            {
                int col = i % 3, row = i / 3;
                var pos = new Vector2(-150f + col * 150f, 0f - row * 92f);
                string k = keys[i];
                UIKit.Button(Root, k, pos, new Vector2(132f, 78f), () => Press(k),
                    k == "C" ? new Color(0.45f, 0.2f, 0.22f, 0.95f) :
                    k == "<" ? new Color(0.3f, 0.28f, 0.2f, 0.95f) : Art.PanelSoft, 34, 14);
            }
        }

        private void Press(string k)
        {
            if (IsDone) return;
            if (k == "C") _entry = "";
            else if (k == "<") { if (_entry.Length > 0) _entry = _entry.Substring(0, _entry.Length - 1); }
            else if (_entry.Length < _digits) _entry += k;

            _display.text = _entry;

            if (_entry.Length == _digits)
            {
                if (_entry == _code)
                {
                    _display.color = Art.Good;
                    Complete();
                }
                else
                {
                    Fail();
                    _entry = "";
                    _display.text = "";
                    _display.color = Art.Danger;
                }
            }
        }
    }

    // ================================================================= sliders
    /// <summary>Align N sliders onto their target marks (antenna, telescope, distributor...).</summary>
    public class SliderTargetGame : MiniGameBase
    {
        private readonly int _count;
        private readonly float _tolerance;
        private readonly string[] _labels;
        private readonly Color _color;
        private readonly bool _vertical;

        private readonly List<Slider> _sliders = new List<Slider>();
        private readonly List<float> _targets = new List<float>();
        private readonly List<Image> _marks = new List<Image>();
        private float _holdTimer;

        public SliderTargetGame(int count, float tolerance, Color color, string[] labels = null, bool vertical = false)
        {
            _count = Mathf.Clamp(count, 1, 5);
            _tolerance = tolerance;
            _color = color;
            _labels = labels;
            _vertical = vertical;
        }

        public override string Instruction => "Совмести значения с отметками";

        protected override void Build()
        {
            if (_vertical)
            {
                float step = 700f / Mathf.Max(1, _count);
                for (int i = 0; i < _count; i++)
                {
                    float x = -350f + step * (i + 0.5f);
                    var s = UIKit.Slider(Root, new Vector2(x, -20f), new Vector2(58f, 340f), 0f, 1f, Rng.Range(0.05f, 0.95f), null, true);
                    _sliders.Add(s);
                    float target = Rng.Range(0.15f, 0.85f);
                    _targets.Add(target);
                    var mark = UIKit.Icon(Root, "Mark", Art.SolidSprite(),
                        new Vector2(x, -20f - 170f + 340f * target), new Vector2(96f, 7f), _color);
                    _marks.Add(mark);
                    if (_labels != null && i < _labels.Length)
                        UIKit.Label(Root, _labels[i], new Vector2(x, -215f), new Vector2(150f, 40f), 22, TextAnchor.MiddleCenter, Art.TextDim);
                }
            }
            else
            {
                float step = 120f;
                float top = (_count - 1) * step * 0.5f;
                for (int i = 0; i < _count; i++)
                {
                    float y = top - step * i;
                    var s = UIKit.Slider(Root, new Vector2(40f, y), new Vector2(600f, 46f), 0f, 1f, Rng.Range(0.05f, 0.95f), null);
                    _sliders.Add(s);
                    float target = Rng.Range(0.12f, 0.88f);
                    _targets.Add(target);
                    var mark = UIKit.Icon(Root, "Mark", Art.SolidSprite(),
                        new Vector2(40f - 300f + 600f * target, y), new Vector2(7f, 78f), _color);
                    _marks.Add(mark);
                    if (_labels != null && i < _labels.Length)
                        UIKit.Label(Root, _labels[i], new Vector2(-330f, y), new Vector2(190f, 44f), 24, TextAnchor.MiddleRight, Art.TextDim);
                }
            }
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            bool all = true;
            for (int i = 0; i < _sliders.Count; i++)
            {
                bool ok = Mathf.Abs(_sliders[i].value - _targets[i]) <= _tolerance;
                _marks[i].color = ok ? Art.Good : _color;
                if (!ok) all = false;
            }

            if (all)
            {
                _holdTimer += dt;
                if (_holdTimer > 0.45f) Complete();
            }
            else _holdTimer = 0f;
        }
    }

    // ================================================================= sequence
    /// <summary>Watch a flashing sequence, then repeat it (reactor, servers, terminals).</summary>
    public class SequenceGame : MiniGameBase
    {
        private readonly int _cols, _rows, _length, _rounds;
        private readonly Color _color;

        private readonly List<Image> _pads = new List<Image>();
        private readonly List<int> _sequence = new List<int>();
        private int _round;
        private int _inputIndex;
        private int _showIndex;
        private float _timer;
        private bool _showing = true;
        private Text _status;

        public SequenceGame(int cols = 3, int rows = 3, int length = 4, int rounds = 1, Color? color = null)
        {
            _cols = cols; _rows = rows; _length = length; _rounds = rounds;
            _color = color ?? Art.Accent;
        }

        public override string Instruction => "Повтори последовательность";

        protected override void Build()
        {
            float cell = 118f, gap = 16f;
            float w = _cols * cell + (_cols - 1) * gap;
            float h = _rows * cell + (_rows - 1) * gap;

            for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                var pos = new Vector2(-w * 0.5f + cell * 0.5f + c * (cell + gap),
                                       h * 0.5f - cell * 0.5f - r * (cell + gap) - 20f);
                var pad = UIKit.Panel(Root, "Pad", pos, new Vector2(cell, cell), new Color(0.13f, 0.16f, 0.22f, 0.95f), 16);
                int index = _pads.Count;
                var relay = UIKit.AddPointer(pad.gameObject);
                relay.Clicked += _ => OnPad(index);
                _pads.Add(pad);
            }

            _status = UIKit.Label(Root, "Смотри...", new Vector2(0f, h * 0.5f + 62f), new Vector2(700f, 46f), 28,
                TextAnchor.MiddleCenter, Art.AccentWarm);

            NewRound();
        }

        private void NewRound()
        {
            _sequence.Clear();
            int len = _length + _round;
            for (int i = 0; i < len; i++) _sequence.Add(Rng.NextInt(_pads.Count));
            _showing = true;
            _showIndex = -1;
            _inputIndex = 0;
            _timer = 0.5f;
            if (_status != null) _status.text = "Смотри...";
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone || !_showing) return;

            _timer -= dt;
            if (_timer > 0f) return;

            if (_showIndex >= 0 && _showIndex < _sequence.Count)
                SetPad(_sequence[_showIndex], false);

            _showIndex++;
            if (_showIndex >= _sequence.Count)
            {
                _showing = false;
                if (_status != null) _status.text = "Теперь повтори";
                return;
            }

            SetPad(_sequence[_showIndex], true);
            Audio.SoundBank.Play(Audio.Sfx.Beep, 0.4f, 0.85f + _sequence[_showIndex] * 0.06f);
            _timer = 0.52f;
        }

        private void SetPad(int index, bool lit)
        {
            if (index < 0 || index >= _pads.Count) return;
            _pads[index].color = lit ? _color : new Color(0.13f, 0.16f, 0.22f, 0.95f);
        }

        private void OnPad(int index)
        {
            if (IsDone || _showing) return;

            if (_sequence[_inputIndex] == index)
            {
                _inputIndex++;
                Progress();
                var img = _pads[index];
                img.color = Art.Good;
                var tween = UiTween.Ensure(img.gameObject);
                tween.Run(0.22f, t => { if (t >= 1f) img.color = new Color(0.13f, 0.16f, 0.22f, 0.95f); });

                if (_inputIndex >= _sequence.Count)
                {
                    _round++;
                    if (_round >= _rounds) Complete();
                    else NewRound();
                }
            }
            else
            {
                Fail();
                _showing = true;
                _showIndex = -1;
                _inputIndex = 0;
                _timer = 0.6f;
                if (_status != null) _status.text = "Ошибка. Смотри снова...";
            }
        }
    }

    // ================================================================= hold gauge
    /// <summary>
    /// Hold the button to fill a gauge. Two flavours: fill it completely (fuel,
    /// charge) or stop inside a safe band before overload (power cells).
    /// </summary>
    public class HoldGaugeGame : MiniGameBase
    {
        private readonly float _fillRate;
        private readonly bool _mustStopInBand;
        private readonly float _bandCenter, _bandWidth;
        private readonly string _caption;
        private readonly Color _color;

        private Image _fill;
        private Image _band;
        private Text _readout;
        private PointerRelay _relay;
        private float _value;
        private bool _overloaded;

        public HoldGaugeGame(float fillRate = 0.34f, bool mustStopInBand = false, float bandCenter = 0.78f,
            float bandWidth = 0.12f, string caption = "УДЕРЖИВАЙ", Color? color = null)
        {
            _fillRate = fillRate;
            _mustStopInBand = mustStopInBand;
            _bandCenter = bandCenter;
            _bandWidth = bandWidth;
            _caption = caption;
            _color = color ?? Art.AccentWarm;
        }

        public override string Instruction => _mustStopInBand ? "Отпусти в зелёной зоне" : "Держи, пока шкала не заполнится";

        protected override void Build()
        {
            UIKit.Panel(Root, "GaugeBg", new Vector2(0f, 90f), new Vector2(720f, 86f), new Color(0.06f, 0.08f, 0.12f, 0.95f), 14);

            if (_mustStopInBand)
            {
                float x = -360f + 720f * _bandCenter;
                _band = UIKit.Icon(Root, "Band", Art.SolidSprite(), new Vector2(x, 90f),
                    new Vector2(720f * _bandWidth, 86f), new Color(0.25f, 0.75f, 0.4f, 0.45f));
            }

            _fill = UIKit.Bar(Root, new Vector2(0f, 90f), new Vector2(710f, 74f), new Color(0f, 0f, 0f, 0f), _color, 12);
            _readout = UIKit.Label(Root, "0%", new Vector2(0f, 90f), new Vector2(720f, 86f), 34,
                TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

            var btn = UIKit.Panel(Root, "HoldBtn", new Vector2(0f, -110f), new Vector2(320f, 150f), Art.PanelSoft, 24);
            UIKit.Label(btn.transform, _caption, Vector2.zero, new Vector2(320f, 150f), 34, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);
            _relay = UIKit.AddPointer(btn.gameObject);
            _relay.Up += _ => Release();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone || _overloaded) return;

            if (_relay != null && _relay.IsDown)
            {
                _value += _fillRate * dt;
                if (_value >= 1f)
                {
                    _value = 1f;
                    if (!_mustStopInBand) Complete();
                    else
                    {
                        _overloaded = true;
                        Fail();
                        _value = 0f;
                        _overloaded = false;
                    }
                }
            }
            else if (!_mustStopInBand)
            {
                _value = Mathf.Max(0f, _value - dt * 0.16f);
            }

            _fill.fillAmount = _value;
            _readout.text = Mathf.RoundToInt(_value * 100f) + "%";
            if (_mustStopInBand && _band != null)
            {
                bool inBand = Mathf.Abs(_value - _bandCenter) <= _bandWidth * 0.5f;
                _band.color = inBand ? new Color(0.3f, 0.9f, 0.45f, 0.6f) : new Color(0.25f, 0.75f, 0.4f, 0.35f);
            }
        }

        private void Release()
        {
            if (IsDone || !_mustStopInBand) return;
            if (Mathf.Abs(_value - _bandCenter) <= _bandWidth * 0.5f) Complete();
            else { Fail(); _value = 0f; }
        }
    }

    // ================================================================= sorting
    /// <summary>Drag items into the matching bin (cargo, samples, inventory).</summary>
    public class SortGame : MiniGameBase
    {
        private readonly int _itemCount;
        private readonly string[] _binNames;
        private readonly Color[] _binColors;

        private class Item
        {
            public Image Img;
            public int Category;
            public Vector2 Home;
            public bool Placed;
        }

        private readonly List<Item> _items = new List<Item>();
        private readonly List<RectTransform> _bins = new List<RectTransform>();
        private Item _dragging;
        private int _placed;

        public SortGame(int itemCount, string[] binNames, Color[] binColors)
        {
            _itemCount = itemCount;
            _binNames = binNames;
            _binColors = binColors;
        }

        public override string Instruction => "Разложи по контейнерам";

        protected override void Build()
        {
            int bins = _binNames.Length;
            float step = 760f / bins;
            for (int i = 0; i < bins; i++)
            {
                var pos = new Vector2(-380f + step * (i + 0.5f), -160f);
                var bin = UIKit.Panel(Root, "Bin" + i, pos, new Vector2(step - 24f, 170f),
                    new Color(_binColors[i].r * 0.35f, _binColors[i].g * 0.35f, _binColors[i].b * 0.35f, 0.9f), 16);
                UIKit.Label(bin.transform, _binNames[i], new Vector2(0f, -60f), new Vector2(step - 30f, 44f), 22,
                    TextAnchor.MiddleCenter, _binColors[i], FontStyle.Bold);
                _bins.Add(bin.rectTransform);
            }

            for (int i = 0; i < _itemCount; i++)
            {
                int cat = Rng.NextInt(bins);
                var home = new Vector2(Rng.Range(-330f, 330f), Rng.Range(80f, 210f));
                var img = UIKit.Panel(Root, "Item" + i, home, new Vector2(84f, 84f), _binColors[cat], 12);
                var item = new Item { Img = img, Category = cat, Home = home };
                _items.Add(item);

                var relay = UIKit.AddPointer(img.gameObject);
                relay.Down += _ => { if (!item.Placed) _dragging = item; };
                relay.Dragged += e => Drag(e);
                relay.Up += e => Drop(e);
                relay.DragEnded += e => Drop(e);
            }
        }

        private void Drag(PointerEventData e)
        {
            if (_dragging == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var local))
                _dragging.Img.rectTransform.anchoredPosition = local;
        }

        private void Drop(PointerEventData e)
        {
            if (_dragging == null) return;
            var pos = _dragging.Img.rectTransform.anchoredPosition;
            var bin = _bins[_dragging.Category];
            bool inside = Mathf.Abs(pos.x - bin.anchoredPosition.x) < bin.sizeDelta.x * 0.5f + 20f &&
                          Mathf.Abs(pos.y - bin.anchoredPosition.y) < bin.sizeDelta.y * 0.5f + 20f;

            if (inside)
            {
                _dragging.Placed = true;
                _dragging.Img.rectTransform.anchoredPosition = bin.anchoredPosition + new Vector2(Rng.Range(-30f, 30f), Rng.Range(-20f, 20f));
                _dragging.Img.rectTransform.sizeDelta = new Vector2(52f, 52f);
                _placed++;
                Progress();
                if (_placed >= _items.Count) Complete();
            }
            else
            {
                _dragging.Img.rectTransform.anchoredPosition = _dragging.Home;
            }
            _dragging = null;
        }
    }
}
