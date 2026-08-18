// -----------------------------------------------------------------------------
//  NEBULA NINE - mini-games, part 3: card swipe, dial tuning, memory pairs,
//  counting, valve rotation and data transfer.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Fx;
using Nebula.UI;

namespace Nebula.Tasks
{
    // ================================================================= card swipe
    /// <summary>Swipe the ID card at a sane speed - too fast or too slow is rejected.</summary>
    public class SwipeCardGame : MiniGameBase
    {
        private Image _card;
        private Text _readout;
        private bool _dragging;
        private float _startX;
        private float _startTime;
        private int _attempts;

        public override string Instruction => "Проведи картой через считыватель";

        protected override void Build()
        {
            UIKit.Panel(Root, "Reader", new Vector2(0f, 60f), new Vector2(800f, 130f), new Color(0.10f, 0.12f, 0.17f, 0.97f), 16);
            UIKit.Panel(Root, "Slot", new Vector2(0f, 60f), new Vector2(780f, 26f), new Color(0.02f, 0.03f, 0.05f, 1f), 8);
            _readout = UIKit.Label(Root, "Ожидание карты", new Vector2(0f, -80f), new Vector2(800f, 50f), 30,
                TextAnchor.MiddleCenter, Art.TextDim);

            _card = UIKit.Panel(Root, "Card", new Vector2(-300f, 60f), new Vector2(200f, 118f), new Color(0.85f, 0.78f, 0.35f), 12);
            UIKit.Label(_card.transform, "ID", new Vector2(-60f, 28f), new Vector2(80f, 40f), 26, TextAnchor.MiddleCenter, new Color(0.2f, 0.16f, 0.05f), FontStyle.Bold);
            UIKit.Panel(_card.transform, "Chip", new Vector2(52f, -20f), new Vector2(56f, 44f), new Color(0.55f, 0.48f, 0.15f), 6);

            var relay = UIKit.AddPointer(_card.gameObject);
            relay.Down += e =>
            {
                _dragging = true;
                _startTime = Time.unscaledTime;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var l))
                    _startX = l.x;
            };
            relay.Dragged += e =>
            {
                if (!_dragging) return;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var l))
                    _card.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(l.x, -320f, 330f), 60f);
            };
            relay.Up += _ => Release();
            relay.DragEnded += _ => Release();
        }

        private void Release()
        {
            if (!_dragging || IsDone) return;
            _dragging = false;

            float travelled = _card.rectTransform.anchoredPosition.x - _startX;
            float elapsed = Mathf.Max(0.0001f, Time.unscaledTime - _startTime);
            float speed = travelled / elapsed;

            if (travelled < 420f)
            {
                _readout.text = "Карта не проведена до конца";
                _readout.color = Art.AccentWarm;
                Fail();
            }
            else if (speed > 1500f)
            {
                _readout.text = "Слишком быстро";
                _readout.color = Art.Danger;
                Fail();
            }
            else if (speed < 320f)
            {
                _readout.text = "Слишком медленно";
                _readout.color = Art.Danger;
                Fail();
            }
            else
            {
                _readout.text = "Доступ разрешён";
                _readout.color = Art.Good;
                Complete();
                return;
            }

            _attempts++;
            _card.rectTransform.anchoredPosition = new Vector2(-300f, 60f);
        }
    }

    // ================================================================= dial tuning
    /// <summary>Rotate the dial until the received waveform matches the reference.</summary>
    public class DialTuneGame : MiniGameBase
    {
        private readonly string _caption;
        private float _target;
        private float _value;
        private Image _dial;
        private Text _readout;
        private readonly List<Image> _wave = new List<Image>();
        private float _hold;
        private bool _dragging;
        private Vector2 _lastLocal;

        public DialTuneGame(string caption = "Настрой частоту") { _caption = caption; }

        public override string Instruction => _caption;

        protected override void Build()
        {
            _target = Rng.Range(0.18f, 0.82f);
            _value = Rng.Range(0.05f, 0.95f);

            UIKit.Panel(Root, "Scope", new Vector2(0f, 120f), new Vector2(760f, 220f), new Color(0.03f, 0.07f, 0.05f, 0.96f), 14);
            for (int i = 0; i < 48; i++)
            {
                var bar = UIKit.Sprite(Root, "W", Art.SolidSprite(),
                    new Vector2(-360f + i * 15.3f, 120f), new Vector2(8f, 10f), new Color(0.35f, 0.95f, 0.55f, 0.9f));
                _wave.Add(bar);
            }

            var knob = UIKit.Panel(Root, "Dial", new Vector2(0f, -110f), new Vector2(210f, 210f), new Color(0.14f, 0.17f, 0.23f, 0.98f), 110);
            _dial = UIKit.Sprite(knob.transform, "Pointer", Art.SolidSprite(), new Vector2(0f, 58f), new Vector2(12f, 82f), Art.AccentWarm);
            _readout = UIKit.Label(Root, "", new Vector2(0f, -240f), new Vector2(700f, 44f), 26, TextAnchor.MiddleCenter, Art.TextDim);

            var relay = UIKit.AddPointer(knob.gameObject);
            relay.Down += e =>
            {
                _dragging = true;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out _lastLocal);
            };
            relay.Dragged += e =>
            {
                if (!_dragging) return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var l)) return;
                _value = Mathf.Clamp01(_value + (l.x - _lastLocal.x) * 0.0022f);
                _lastLocal = l;
            };
            relay.Up += _ => _dragging = false;
            relay.DragEnded += _ => _dragging = false;
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            float error = Mathf.Abs(_value - _target);
            float noise = Mathf.Clamp01(error * 4.5f);
            float freq = 6f + _value * 24f;

            for (int i = 0; i < _wave.Count; i++)
            {
                float x = i / (float)_wave.Count;
                float clean = Mathf.Sin((x * freq + Time.unscaledTime * 2.2f) * Mathf.PI * 2f);
                float dirty = Rng.Range(-1f, 1f);
                float v = Mathf.Lerp(clean, dirty, noise);
                var rt = _wave[i].rectTransform;
                rt.sizeDelta = new Vector2(8f, 10f + Mathf.Abs(v) * 88f);
                _wave[i].color = Color.Lerp(new Color(0.35f, 0.95f, 0.55f, 0.95f), new Color(0.95f, 0.4f, 0.35f, 0.8f), noise);
            }

            _dial.transform.parent.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(140f, -140f, _value));
            _readout.text = "Частота: " + (88f + _value * 20f).ToString("F1") + " МГц";

            if (error < 0.035f)
            {
                _hold += dt;
                if (_hold > 0.9f) Complete();
            }
            else _hold = 0f;
        }
    }

    // ================================================================= memory pairs
    /// <summary>Catalogue samples by matching pairs of markers.</summary>
    public class MemoryPairsGame : MiniGameBase
    {
        private readonly int _pairs;
        private readonly List<Image> _cards = new List<Image>();
        private readonly List<Image> _faces = new List<Image>();
        private readonly List<int> _values = new List<int>();
        private readonly List<bool> _matched = new List<bool>();
        private int _firstPick = -1, _secondPick = -1;
        private float _flipBackTimer;
        private int _found;

        private static readonly Color[] Palette =
        {
            new Color(0.92f, 0.35f, 0.35f), new Color(0.35f, 0.65f, 0.95f), new Color(0.98f, 0.80f, 0.30f),
            new Color(0.45f, 0.88f, 0.52f), new Color(0.78f, 0.48f, 0.95f), new Color(0.35f, 0.90f, 0.88f),
        };

        public MemoryPairsGame(int pairs = 5) { _pairs = Mathf.Clamp(pairs, 3, 6); }

        public override string Instruction => "Найди совпадающие образцы";

        protected override void Build()
        {
            var deck = new List<int>();
            for (int i = 0; i < _pairs; i++) { deck.Add(i); deck.Add(i); }
            Rng.Shuffle(deck);

            int cols = _pairs > 4 ? 5 : 4;
            int rows = Mathf.CeilToInt(deck.Count / (float)cols);
            float cw = 150f, ch = 130f, gap = 14f;
            float totalW = cols * cw + (cols - 1) * gap;
            float totalH = rows * ch + (rows - 1) * gap;

            for (int i = 0; i < deck.Count; i++)
            {
                int c = i % cols, r = i / cols;
                var pos = new Vector2(-totalW * 0.5f + cw * 0.5f + c * (cw + gap),
                                       totalH * 0.5f - ch * 0.5f - r * (ch + gap));
                var back = UIKit.Panel(Root, "Card" + i, pos, new Vector2(cw, ch), new Color(0.14f, 0.17f, 0.24f, 0.97f), 14);
                var face = UIKit.Panel(back.transform, "Face", Vector2.zero, new Vector2(cw - 20f, ch - 20f), Palette[deck[i] % Palette.Length], 10);
                face.gameObject.SetActive(false);

                _cards.Add(back);
                _faces.Add(face);
                _values.Add(deck[i]);
                _matched.Add(false);

                int index = i;
                var relay = UIKit.AddPointer(back.gameObject);
                relay.Clicked += _ => Pick(index);
            }
        }

        private void Pick(int i)
        {
            if (IsDone || _matched[i] || _flipBackTimer > 0f) return;
            if (i == _firstPick) return;

            _faces[i].gameObject.SetActive(true);
            Audio.SoundBank.Play(Audio.Sfx.UiClick, 0.4f);

            if (_firstPick < 0) { _firstPick = i; return; }

            _secondPick = i;
            if (_values[_firstPick] == _values[_secondPick])
            {
                _matched[_firstPick] = _matched[_secondPick] = true;
                _cards[_firstPick].color = new Color(0.14f, 0.3f, 0.2f, 0.95f);
                _cards[_secondPick].color = new Color(0.14f, 0.3f, 0.2f, 0.95f);
                _firstPick = _secondPick = -1;
                _found++;
                Progress();
                if (_found >= _pairs) Complete();
            }
            else
            {
                _flipBackTimer = 0.65f;
            }
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (_flipBackTimer > 0f)
            {
                _flipBackTimer -= dt;
                if (_flipBackTimer <= 0f)
                {
                    if (_firstPick >= 0) _faces[_firstPick].gameObject.SetActive(false);
                    if (_secondPick >= 0) _faces[_secondPick].gameObject.SetActive(false);
                    _firstPick = _secondPick = -1;
                    Fail();
                }
            }
        }
    }

    // ================================================================= counting
    /// <summary>Count the containers on the shelf and enter the number.</summary>
    public class CountGame : MiniGameBase
    {
        private int _answer;
        private Text _prompt;

        public override string Instruction => "Пересчитай контейнеры и выбери ответ";

        protected override void Build()
        {
            _answer = Rng.Range(7, 20);
            var color = new Color(0.55f, 0.65f, 0.78f);

            var shelf = UIKit.Panel(Root, "Shelf", new Vector2(0f, 90f), new Vector2(840f, 250f), new Color(0.06f, 0.08f, 0.12f, 0.92f), 16);
            for (int i = 0; i < _answer; i++)
            {
                float x = -380f + (i % 10) * 84f;
                float y = 60f - (i / 10) * 96f;
                UIKit.Panel(shelf.transform, "Box", new Vector2(x, y), new Vector2(70f, 74f),
                    Color.Lerp(color, Palette(i), 0.5f), 8);
            }

            _prompt = UIKit.Label(Root, "Сколько контейнеров?", new Vector2(0f, -60f), new Vector2(800f, 44f), 28,
                TextAnchor.MiddleCenter, Art.TextDim);

            var options = new List<int> { _answer, _answer + Rng.Range(1, 4), Mathf.Max(1, _answer - Rng.Range(1, 4)), _answer + Rng.Range(4, 7) };
            Rng.Shuffle(options);
            for (int i = 0; i < options.Count; i++)
            {
                int v = options[i];
                UIKit.Button(Root, v.ToString(), new Vector2(-315f + i * 210f, -160f), new Vector2(180f, 86f),
                    () => Answer(v), Art.PanelSoft, 34, 14);
            }
        }

        private static Color Palette(int i)
        {
            switch (i % 4)
            {
                case 0: return new Color(0.85f, 0.55f, 0.3f);
                case 1: return new Color(0.4f, 0.7f, 0.9f);
                case 2: return new Color(0.5f, 0.8f, 0.5f);
                default: return new Color(0.8f, 0.75f, 0.4f);
            }
        }

        private void Answer(int v)
        {
            if (IsDone) return;
            if (v == _answer)
            {
                _prompt.text = "Верно";
                _prompt.color = Art.Good;
                Complete();
            }
            else
            {
                _prompt.text = "Неверно, пересчитай";
                _prompt.color = Art.Danger;
                Fail();
            }
        }
    }

    // ================================================================= valves
    /// <summary>Rotate each valve until its arrow points at the marker.</summary>
    public class ValveGame : MiniGameBase
    {
        private readonly int _count;
        private readonly List<Image> _valves = new List<Image>();
        private readonly List<float> _angles = new List<float>();
        private readonly List<float> _targets = new List<float>();
        private readonly List<Image> _marks = new List<Image>();
        private int _dragIndex = -1;
        private Vector2 _lastLocal;

        public ValveGame(int count = 3) { _count = Mathf.Clamp(count, 2, 4); }

        public override string Instruction => "Поверни вентили в нужное положение";

        protected override void Build()
        {
            float step = 760f / _count;
            for (int i = 0; i < _count; i++)
            {
                float x = -380f + step * (i + 0.5f);
                var pos = new Vector2(x, 10f);
                var body = UIKit.Panel(Root, "Valve" + i, pos, new Vector2(190f, 190f), new Color(0.13f, 0.16f, 0.22f, 0.97f), 95);

                float target = Rng.Range(0f, 360f);
                _targets.Add(target);
                var mark = UIKit.Sprite(Root, "Mark", Art.SolidSprite(),
                    pos + new Vector2(Mathf.Sin(target * Mathf.Deg2Rad), Mathf.Cos(target * Mathf.Deg2Rad)) * 118f,
                    new Vector2(24f, 24f), Art.Good);
                _marks.Add(mark);

                var handle = UIKit.Sprite(body.transform, "Handle", Art.SolidSprite(), new Vector2(0f, 48f), new Vector2(16f, 90f), Art.AccentWarm);
                _valves.Add(handle);
                _angles.Add(Rng.Range(0f, 360f));
                body.transform.localRotation = Quaternion.Euler(0f, 0f, -_angles[i]);

                int index = i;
                var relay = UIKit.AddPointer(body.gameObject);
                relay.Down += e =>
                {
                    _dragIndex = index;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out _lastLocal);
                };
                relay.Dragged += e =>
                {
                    if (_dragIndex != index) return;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var l)) return;
                    var centre = body.rectTransform.anchoredPosition;
                    float a0 = Mathf.Atan2(_lastLocal.y - centre.y, _lastLocal.x - centre.x) * Mathf.Rad2Deg;
                    float a1 = Mathf.Atan2(l.y - centre.y, l.x - centre.x) * Mathf.Rad2Deg;
                    _angles[index] = Mathf.Repeat(_angles[index] - Mathf.DeltaAngle(a0, a1), 360f);
                    body.transform.localRotation = Quaternion.Euler(0f, 0f, -_angles[index]);
                    _lastLocal = l;
                };
                relay.Up += _ => _dragIndex = -1;
                relay.DragEnded += _ => _dragIndex = -1;
            }
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            bool all = true;
            for (int i = 0; i < _count; i++)
            {
                bool ok = Mathf.Abs(Mathf.DeltaAngle(_angles[i], _targets[i])) < 12f;
                _valves[i].color = ok ? Art.Good : Art.AccentWarm;
                _marks[i].color = ok ? Art.Good : new Color(0.4f, 0.55f, 0.45f);
                if (!ok) all = false;
            }
            if (all) Complete();
        }
    }

    // ================================================================= transfer
    /// <summary>Download / upload with a progress bar and a start button.</summary>
    public class TransferGame : MiniGameBase
    {
        private readonly string _caption;
        private readonly float _duration;
        private readonly Color _color;

        private Image _bar;
        private Text _readout;
        private bool _running;
        private float _time;

        public TransferGame(string caption, float duration = 6.5f, Color? color = null)
        {
            _caption = caption; _duration = duration; _color = color ?? Art.Accent;
        }

        public override string Instruction => _caption;

        protected override void Build()
        {
            UIKit.Panel(Root, "Console", new Vector2(0f, 40f), new Vector2(820f, 260f), new Color(0.05f, 0.07f, 0.11f, 0.95f), 16);
            UIKit.Label(Root, _caption, new Vector2(0f, 130f), new Vector2(760f, 46f), 30, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);
            _bar = UIKit.Bar(Root, new Vector2(0f, 40f), new Vector2(720f, 52f), new Color(0.02f, 0.03f, 0.05f, 1f), _color, 12);
            _readout = UIKit.Label(Root, "Готов к передаче", new Vector2(0f, -30f), new Vector2(760f, 44f), 26,
                TextAnchor.MiddleCenter, Art.TextDim);
            UIKit.Button(Root, "НАЧАТЬ", new Vector2(0f, -180f), new Vector2(300f, 96f), Begin, Art.PanelSoft, 32, 18);
        }

        private void Begin()
        {
            if (_running || IsDone) return;
            _running = true;
            _readout.text = "Передача...";
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (!_running || IsDone) return;
            _time += dt;
            float p = Mathf.Clamp01(_time / _duration);
            _bar.fillAmount = p;
            _readout.text = "Передача... " + Mathf.RoundToInt(p * 100f) + "%";
            if (p >= 1f)
            {
                _readout.text = "Готово";
                _readout.color = Art.Good;
                Complete();
            }
        }
    }
}
