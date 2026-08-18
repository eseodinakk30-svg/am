// -----------------------------------------------------------------------------
//  NEBULA NINE - mini-games, part 2: tap targets, path tracing, switch banks,
//  balancing, crosshair tracking and timed scans.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Fx;
using Nebula.UI;

namespace Nebula.Tasks
{
    // ================================================================= tap targets
    /// <summary>Shoot down / prime / purge - tap the moving markers before they escape.</summary>
    public class TapTargetsGame : MiniGameBase
    {
        private readonly int _needed;
        private readonly float _speed;
        private readonly Color _color;
        private readonly bool _moving;
        private readonly string _caption;

        private class Target
        {
            public Image Img;
            public Vector2 Velocity;
            public float Life;
        }

        private readonly List<Target> _targets = new List<Target>();
        private int _hits;
        private Text _counter;
        private float _spawnTimer;

        public TapTargetsGame(int needed = 8, float speed = 160f, Color? color = null, bool moving = true, string caption = "Цели")
        {
            _needed = needed; _speed = speed; _color = color ?? Art.Danger; _moving = moving; _caption = caption;
        }

        public override string Instruction => "Уничтожь все цели";

        protected override void Build()
        {
            UIKit.PanelStretch(Root, "Field", new Color(0.04f, 0.06f, 0.10f, 0.85f), 20, 8f);
            _counter = UIKit.Label(Root, _caption + ": 0/" + _needed, new Vector2(0f, 235f), new Vector2(600f, 44f), 28,
                TextAnchor.MiddleCenter, Art.TextDim);
            for (int i = 0; i < 4; i++) SpawnTarget();
        }

        private void SpawnTarget()
        {
            var pos = new Vector2(Rng.Range(-390f, 390f), Rng.Range(-190f, 175f));
            var img = UIKit.Sprite(Root, "Target", Art.Circle(96, _moving ? 0f : 14f), pos, new Vector2(78f, 78f), _color);
            img.raycastTarget = true;
            var t = new Target
            {
                Img = img,
                Velocity = _moving ? Rng.InsideUnitCircle().normalized * _speed : Vector2.zero,
                Life = Rng.Range(3.5f, 6.5f),
            };
            var relay = UIKit.AddPointer(img.gameObject);
            relay.Down += _ => Hit(t);
            _targets.Add(t);
        }

        private void Hit(Target t)
        {
            if (IsDone || t.Img == null) return;
            _hits++;
            Progress();
            Object.Destroy(t.Img.gameObject);
            t.Img = null;
            _counter.text = _caption + ": " + _hits + "/" + _needed;
            if (_hits >= _needed) Complete();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var t = _targets[i];
                if (t.Img == null) { _targets.RemoveAt(i); continue; }
                if (_moving)
                {
                    var p = t.Img.rectTransform.anchoredPosition + t.Velocity * dt;
                    if (Mathf.Abs(p.x) > 400f) t.Velocity.x = -t.Velocity.x;
                    if (p.y > 190f || p.y < -200f) t.Velocity.y = -t.Velocity.y;
                    t.Img.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(p.x, -400f, 400f), Mathf.Clamp(p.y, -200f, 190f));
                }
                t.Life -= dt;
                if (t.Life <= 0f)
                {
                    Object.Destroy(t.Img.gameObject);
                    t.Img = null;
                    _targets.RemoveAt(i);
                }
            }

            _spawnTimer -= dt;
            if (_spawnTimer <= 0f && _targets.Count < 5 && _hits < _needed)
            {
                _spawnTimer = 0.45f;
                SpawnTarget();
            }
        }
    }

    // ================================================================= trace path
    /// <summary>Drag the tool along the marked path without skipping nodes.</summary>
    public class TracePathGame : MiniGameBase
    {
        private readonly int _nodes;
        private readonly Color _color;
        private readonly string _caption;

        private readonly List<Image> _dots = new List<Image>();
        private readonly List<Vector2> _points = new List<Vector2>();
        private int _next;
        private Image _tool;
        private bool _dragging;

        public TracePathGame(int nodes = 9, Color? color = null, string caption = "Проведи инструментом")
        {
            _nodes = Mathf.Clamp(nodes, 4, 16);
            _color = color ?? Art.AccentWarm;
            _caption = caption;
        }

        public override string Instruction => _caption;

        protected override void Build()
        {
            var field = UIKit.PanelStretch(Root, "Field", new Color(0.05f, 0.07f, 0.11f, 0.9f), 20, 8f);
            field.raycastTarget = true;

            float x = -380f;
            float step = 760f / (_nodes - 1);
            float phase = Rng.Range(0f, 6.28f);
            for (int i = 0; i < _nodes; i++)
            {
                float y = Mathf.Sin(phase + i * 0.8f) * 130f + Rng.Range(-25f, 25f);
                var p = new Vector2(x + step * i, y);
                _points.Add(p);
                var dot = UIKit.Sprite(Root, "Node", Art.Circle(64, 10f), p, new Vector2(56f, 56f),
                    i == 0 ? Art.Good : new Color(_color.r, _color.g, _color.b, 0.45f));
                _dots.Add(dot);
            }

            for (int i = 0; i < _points.Count - 1; i++)
            {
                var a = _points[i];
                var b = _points[i + 1];
                var line = UIKit.Sprite(Root, "Line", Art.SolidSprite(), (a + b) * 0.5f,
                    new Vector2(Vector2.Distance(a, b), 6f), new Color(_color.r, _color.g, _color.b, 0.22f));
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                line.transform.SetSiblingIndex(1);
            }

            _tool = UIKit.Sprite(Root, "Tool", Art.Circle(64), _points[0], new Vector2(46f, 46f), Art.Accent);

            var relay = UIKit.AddPointer(field.gameObject);
            relay.Down += e => { _dragging = true; Move(e); };
            relay.Dragged += Move;
            relay.Up += _ => _dragging = false;
            relay.DragEnded += _ => _dragging = false;
        }

        private void Move(PointerEventData e)
        {
            if (IsDone || !_dragging) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var local)) return;
            _tool.rectTransform.anchoredPosition = local;

            if (_next < _points.Count && Vector2.Distance(local, _points[_next]) < 52f)
            {
                _dots[_next].color = Art.Good;
                _next++;
                Progress();
                if (_next < _points.Count) _dots[_next].color = new Color(_color.r, _color.g, _color.b, 0.95f);
                if (_next >= _points.Count) Complete();
            }
        }
    }

    // ================================================================= switch bank
    /// <summary>
    /// Flip every breaker up. Some pairs are cross wired: flipping one drops its
    /// neighbour, so the order matters.
    /// </summary>
    public class SwitchBankGame : MiniGameBase
    {
        private readonly int _count;
        private readonly bool _crossWired;

        private readonly List<Image> _switches = new List<Image>();
        private readonly List<Image> _knobs = new List<Image>();
        private bool[] _on;
        private int[] _linked;

        public SwitchBankGame(int count = 5, bool crossWired = false)
        {
            _count = Mathf.Clamp(count, 3, 8);
            _crossWired = crossWired;
        }

        public override string Instruction => "Подними все рубильники";

        protected override void Build()
        {
            _on = new bool[_count];
            _linked = new int[_count];
            for (int i = 0; i < _count; i++) _linked[i] = -1;

            if (_crossWired)
            {
                for (int i = 0; i < _count - 1; i += 2)
                {
                    if (Rng.Chance(0.65f)) { _linked[i] = i + 1; _linked[i + 1] = i; }
                }
            }

            float step = 780f / _count;
            for (int i = 0; i < _count; i++)
            {
                float x = -390f + step * (i + 0.5f);
                var body = UIKit.Panel(Root, "Sw" + i, new Vector2(x, 0f), new Vector2(step - 26f, 300f),
                    new Color(0.10f, 0.12f, 0.17f, 0.95f), 14);
                var knob = UIKit.Panel(body.transform, "Knob", new Vector2(0f, -95f), new Vector2(step - 52f, 92f),
                    Art.Danger, 12);
                UIKit.Label(body.transform, (i + 1).ToString(), new Vector2(0f, 128f), new Vector2(60f, 40f), 24,
                    TextAnchor.MiddleCenter, Art.TextDim);
                _switches.Add(body);
                _knobs.Add(knob);

                int index = i;
                var relay = UIKit.AddPointer(body.gameObject);
                relay.Clicked += _ => Flip(index);
            }
        }

        private void Flip(int i)
        {
            if (IsDone) return;
            _on[i] = !_on[i];
            if (_linked[i] >= 0 && _on[i]) _on[_linked[i]] = false;
            Audio.SoundBank.Play(Audio.Sfx.Switch, 0.6f);
            Refresh();

            foreach (var b in _on) if (!b) return;
            Complete();
        }

        private void Refresh()
        {
            for (int i = 0; i < _count; i++)
            {
                _knobs[i].rectTransform.anchoredPosition = new Vector2(0f, _on[i] ? 95f : -95f);
                _knobs[i].color = _on[i] ? Art.Good : Art.Danger;
            }
        }
    }

    // ================================================================= balance
    /// <summary>Two inputs that have to add up to the requested value (coolant, scales).</summary>
    public class BalanceGame : MiniGameBase
    {
        private readonly string _leftLabel, _rightLabel;
        private readonly float _tolerance;

        private Slider _a, _b;
        private float _target;
        private Text _readout;
        private Image _needle;
        private float _hold;

        public BalanceGame(string leftLabel = "Контур A", string rightLabel = "Контур B", float tolerance = 0.05f)
        {
            _leftLabel = leftLabel; _rightLabel = rightLabel; _tolerance = tolerance;
        }

        public override string Instruction => "Сбалансируй систему";

        protected override void Build()
        {
            _target = Rng.Range(0.35f, 0.75f);

            UIKit.Label(Root, _leftLabel, new Vector2(-250f, 190f), new Vector2(300f, 40f), 24, TextAnchor.MiddleCenter, Art.TextDim);
            UIKit.Label(Root, _rightLabel, new Vector2(250f, 190f), new Vector2(300f, 40f), 24, TextAnchor.MiddleCenter, Art.TextDim);

            _a = UIKit.Slider(Root, new Vector2(-250f, 30f), new Vector2(66f, 260f), 0f, 1f, Rng.Range(0.1f, 0.9f), null, true);
            _b = UIKit.Slider(Root, new Vector2(250f, 30f), new Vector2(66f, 260f), 0f, 1f, Rng.Range(0.1f, 0.9f), null, true);

            UIKit.Panel(Root, "GaugeBg", new Vector2(0f, 30f), new Vector2(300f, 300f), new Color(0.06f, 0.08f, 0.12f, 0.95f), 150);
            UIKit.Sprite(Root, "TargetMark", Art.SolidSprite(),
                new Vector2(Mathf.Cos(Mathf.PI * (1f - _target)) * 105f, 30f + Mathf.Sin(Mathf.PI * (1f - _target)) * 105f),
                new Vector2(22f, 22f), Art.Good);
            _needle = UIKit.Sprite(Root, "Needle", Art.SolidSprite(), new Vector2(0f, 30f), new Vector2(8f, 120f), Art.AccentWarm);

            _readout = UIKit.Label(Root, "", new Vector2(0f, -140f), new Vector2(700f, 44f), 26, TextAnchor.MiddleCenter, Art.TextDim);
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            float mix = (_a.value + _b.value) * 0.5f;
            float angle = Mathf.Lerp(90f, -90f, mix);
            _needle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            _needle.rectTransform.anchoredPosition = new Vector2(0f, 30f) +
                (Quaternion.Euler(0f, 0f, angle) * Vector3.up * 60f).ToVector2();

            bool ok = Mathf.Abs(mix - _target) <= _tolerance;
            _needle.color = ok ? Art.Good : Art.AccentWarm;
            _readout.text = ok ? "Стабильно" : "Цель: " + Mathf.RoundToInt(_target * 100f) + "%   Текущее: " + Mathf.RoundToInt(mix * 100f) + "%";

            if (ok)
            {
                _hold += dt;
                if (_hold > 1.1f) Complete();
            }
            else _hold = 0f;
        }
    }

    // ================================================================= crosshair
    /// <summary>Keep the crosshair on a drifting marker for a few seconds.</summary>
    public class CrosshairGame : MiniGameBase
    {
        private readonly float _holdNeeded;
        private readonly float _driftSpeed;
        private readonly string _caption;

        private Image _target, _cross, _progressBar;
        private Vector2 _targetPos, _targetVel;
        private float _hold;
        private bool _dragging;

        public CrosshairGame(float holdNeeded = 2.6f, float driftSpeed = 105f, string caption = "Удержи цель")
        {
            _holdNeeded = holdNeeded; _driftSpeed = driftSpeed; _caption = caption;
        }

        public override string Instruction => _caption;

        protected override void Build()
        {
            var field = UIKit.PanelStretch(Root, "Field", new Color(0.03f, 0.05f, 0.09f, 0.9f), 20, 8f);
            field.raycastTarget = true;

            _targetPos = new Vector2(Rng.Range(-250f, 250f), Rng.Range(-120f, 120f));
            _targetVel = Rng.InsideUnitCircle().normalized * _driftSpeed;

            _target = UIKit.Sprite(Root, "Marker", Art.Circle(96, 0f), _targetPos, new Vector2(80f, 80f),
                new Color(0.95f, 0.42f, 0.3f, 0.85f));
            _cross = UIKit.Sprite(Root, "Cross", Art.Circle(96, 10f), Vector2.zero, new Vector2(120f, 120f), Art.Accent);
            _progressBar = UIKit.Bar(Root, new Vector2(0f, -215f), new Vector2(600f, 26f),
                new Color(0f, 0f, 0f, 0.5f), Art.Good, 8);

            var relay = UIKit.AddPointer(field.gameObject);
            relay.Down += e => { _dragging = true; Move(e); };
            relay.Dragged += Move;
            relay.Up += _ => _dragging = false;
            relay.DragEnded += _ => _dragging = false;
        }

        private void Move(PointerEventData e)
        {
            if (!_dragging) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var local))
                _cross.rectTransform.anchoredPosition = local;
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            _targetPos += _targetVel * dt;
            if (Mathf.Abs(_targetPos.x) > 340f) { _targetVel.x = -_targetVel.x; _targetPos.x = Mathf.Clamp(_targetPos.x, -340f, 340f); }
            if (Mathf.Abs(_targetPos.y) > 160f) { _targetVel.y = -_targetVel.y; _targetPos.y = Mathf.Clamp(_targetPos.y, -160f, 160f); }
            _targetVel += Rng.InsideUnitCircle() * (_driftSpeed * 0.6f * dt);
            _targetVel = Vector2.ClampMagnitude(_targetVel, _driftSpeed * 1.5f);
            _target.rectTransform.anchoredPosition = _targetPos;

            bool locked = Vector2.Distance(_cross.rectTransform.anchoredPosition, _targetPos) < 58f;
            _cross.color = locked ? Art.Good : Art.Accent;
            _hold = locked ? _hold + dt : Mathf.Max(0f, _hold - dt * 0.7f);
            _progressBar.fillAmount = Mathf.Clamp01(_hold / _holdNeeded);
            if (_hold >= _holdNeeded) Complete();
        }
    }

    // ================================================================= scan
    /// <summary>Stand still while the console scans you - the classic "visual" task.</summary>
    public class ScanHoldGame : MiniGameBase
    {
        private readonly float _duration;
        private readonly string _caption;
        private readonly Color _color;

        private float _timer;
        private Image _sweep;
        private Image _ring;
        private Text _readout;

        public ScanHoldGame(float duration = 6f, string caption = "Сканирование", Color? color = null)
        {
            _duration = duration; _caption = caption; _color = color ?? Art.Accent;
        }

        public override string Instruction => _caption;

        protected override void Build()
        {
            UIKit.PanelStretch(Root, "Field", new Color(0.04f, 0.07f, 0.10f, 0.9f), 20, 8f);
            _ring = UIKit.Sprite(Root, "Ring", Art.Circle(256, 12f), new Vector2(0f, 20f), new Vector2(300f, 300f),
                new Color(_color.r, _color.g, _color.b, 0.55f));
            _sweep = UIKit.Sprite(Root, "Sweep", Art.SolidSprite(), new Vector2(0f, 20f), new Vector2(620f, 8f),
                new Color(_color.r, _color.g, _color.b, 0.9f));
            UIKit.Sprite(Root, "Body", Art.Circle(160), new Vector2(0f, 20f), new Vector2(120f, 120f),
                new Color(1f, 1f, 1f, 0.14f));
            _readout = UIKit.Label(Root, "", new Vector2(0f, -190f), new Vector2(700f, 46f), 30,
                TextAnchor.MiddleCenter, Art.TextDim);
            Audio.SoundBank.Play(Audio.Sfx.Scan, 0.5f);
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            _timer += dt;
            float p = Mathf.Clamp01(_timer / _duration);
            _sweep.rectTransform.anchoredPosition = new Vector2(0f, 20f + Mathf.Sin(_timer * 3.1f) * 145f);
            _ring.rectTransform.localScale = Vector3.one * (1f + Mathf.Sin(_timer * 4f) * 0.04f);
            _readout.text = _caption + "... " + Mathf.RoundToInt(p * 100f) + "%";
            if (p >= 1f) Complete();
        }
    }

    internal static class VectorExtensions
    {
        public static Vector2 ToVector2(this Vector3 v) => new Vector2(v.x, v.y);
    }
}
