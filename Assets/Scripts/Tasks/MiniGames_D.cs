// -----------------------------------------------------------------------------
//  NEBULA NINE - mini-games, part 4: pressure regulation, spectrum matching,
//  pipe routing, grid alignment and debris purging.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Fx;
using Nebula.UI;

namespace Nebula.Tasks
{
    // ================================================================= pressure
    /// <summary>
    /// The needle constantly drifts; tap the two trim buttons to keep it inside a
    /// safe band that itself wanders. Survive the required time to finish.
    /// </summary>
    public class PressureRegulateGame : MiniGameBase
    {
        private readonly float _holdNeeded;
        private readonly string _caption;

        private float _needle = 0.5f;
        private float _velocity;
        private float _bandCenter = 0.5f;
        private float _bandVel = 0.06f;
        private float _held;
        private float _trim;

        private Image _needleImg, _bandImg, _progress;
        private Text _readout;

        public PressureRegulateGame(float holdNeeded = 5.5f, string caption = "Стабилизируй давление")
        {
            _holdNeeded = holdNeeded; _caption = caption;
        }

        public override string Instruction => _caption;

        protected override void Build()
        {
            UIKit.Panel(Root, "Tube", new Vector2(0f, 30f), new Vector2(160f, 420f), new Color(0.05f, 0.07f, 0.11f, 0.96f), 20);
            _bandImg = UIKit.Sprite(Root, "Band", Art.SolidSprite(), new Vector2(0f, 30f), new Vector2(150f, 96f),
                new Color(0.25f, 0.8f, 0.45f, 0.35f));
            _needleImg = UIKit.Sprite(Root, "Needle", Art.SolidSprite(), new Vector2(0f, 30f), new Vector2(210f, 14f), Art.AccentWarm);

            _progress = UIKit.Bar(Root, new Vector2(0f, -235f), new Vector2(620f, 26f), new Color(0f, 0f, 0f, 0.5f), Art.Good, 8);
            _readout = UIKit.Label(Root, "", new Vector2(0f, 250f), new Vector2(700f, 44f), 26, TextAnchor.MiddleCenter, Art.TextDim);

            MakeTrim("▲  СБРОС", new Vector2(-300f, 60f), -1f);
            MakeTrim("▼  ПОДАЧА", new Vector2(300f, 60f), 1f);
        }

        private void MakeTrim(string caption, Vector2 pos, float sign)
        {
            var img = UIKit.Panel(Root, "Trim", pos, new Vector2(250f, 150f), Art.PanelSoft, 20);
            UIKit.Label(img.transform, caption, Vector2.zero, new Vector2(250f, 150f), 26, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);
            var relay = UIKit.AddPointer(img.gameObject);
            relay.Down += _ => _trim = sign;
            relay.Up += _ => _trim = 0f;
            relay.Exited += _ => { if (!relay.IsDown) _trim = 0f; };
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            _velocity += (_trim * 0.55f - 0.12f * (_needle - 0.5f) + Rng.Range(-0.16f, 0.16f)) * dt;
            _velocity = Mathf.Clamp(_velocity, -0.45f, 0.45f);
            _needle = Mathf.Clamp01(_needle + _velocity * dt);
            if (_needle <= 0f || _needle >= 1f) _velocity = 0f;

            _bandCenter += _bandVel * dt;
            if (_bandCenter < 0.2f || _bandCenter > 0.8f) { _bandVel = -_bandVel; _bandCenter = Mathf.Clamp(_bandCenter, 0.2f, 0.8f); }

            _needleImg.rectTransform.anchoredPosition = new Vector2(0f, 30f + (_needle - 0.5f) * 400f);
            _bandImg.rectTransform.anchoredPosition = new Vector2(0f, 30f + (_bandCenter - 0.5f) * 400f);

            bool inside = Mathf.Abs(_needle - _bandCenter) < 0.11f;
            _needleImg.color = inside ? Art.Good : Art.Danger;
            _bandImg.color = inside ? new Color(0.3f, 0.9f, 0.5f, 0.45f) : new Color(0.3f, 0.8f, 0.45f, 0.25f);

            _held = inside ? _held + dt : Mathf.Max(0f, _held - dt * 1.3f);
            _progress.fillAmount = Mathf.Clamp01(_held / _holdNeeded);
            _readout.text = inside ? "В норме" : "Отклонение!";
            _readout.color = inside ? Art.Good : Art.Danger;

            if (_held >= _holdNeeded) Complete();
        }
    }

    // ================================================================= spectrum
    /// <summary>Match the reference spectrum with three channel sliders.</summary>
    public class SpectrumMatchGame : MiniGameBase
    {
        private Slider _r, _g, _b;
        private Image _sample, _reference;
        private Color _target;
        private Text _readout;
        private float _hold;

        public override string Instruction => "Подбери спектр образца";

        protected override void Build()
        {
            _target = new Color(Rng.Range(0.15f, 0.95f), Rng.Range(0.15f, 0.95f), Rng.Range(0.15f, 0.95f));

            _reference = UIKit.Panel(Root, "Reference", new Vector2(-210f, 110f), new Vector2(300f, 200f), _target, 16);
            UIKit.Label(Root, "Эталон", new Vector2(-210f, -10f), new Vector2(300f, 40f), 24, TextAnchor.MiddleCenter, Art.TextDim);
            _sample = UIKit.Panel(Root, "Sample", new Vector2(210f, 110f), new Vector2(300f, 200f), Color.black, 16);
            UIKit.Label(Root, "Образец", new Vector2(210f, -10f), new Vector2(300f, 40f), 24, TextAnchor.MiddleCenter, Art.TextDim);

            _r = UIKit.Slider(Root, new Vector2(0f, -70f), new Vector2(640f, 40f), 0f, 1f, Rng.Value01(), null);
            _g = UIKit.Slider(Root, new Vector2(0f, -140f), new Vector2(640f, 40f), 0f, 1f, Rng.Value01(), null);
            _b = UIKit.Slider(Root, new Vector2(0f, -210f), new Vector2(640f, 40f), 0f, 1f, Rng.Value01(), null);
            UIKit.Label(Root, "R", new Vector2(-360f, -70f), new Vector2(60f, 40f), 26, TextAnchor.MiddleCenter, new Color(0.95f, 0.4f, 0.4f));
            UIKit.Label(Root, "G", new Vector2(-360f, -140f), new Vector2(60f, 40f), 26, TextAnchor.MiddleCenter, new Color(0.4f, 0.95f, 0.5f));
            UIKit.Label(Root, "B", new Vector2(-360f, -210f), new Vector2(60f, 40f), 26, TextAnchor.MiddleCenter, new Color(0.45f, 0.6f, 0.98f));

            _readout = UIKit.Label(Root, "", new Vector2(0f, 250f), new Vector2(700f, 44f), 26, TextAnchor.MiddleCenter, Art.TextDim);
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (IsDone) return;

            var current = new Color(_r.value, _g.value, _b.value);
            _sample.color = current;

            float error = (Mathf.Abs(current.r - _target.r) + Mathf.Abs(current.g - _target.g) + Mathf.Abs(current.b - _target.b)) / 3f;
            _readout.text = "Отклонение: " + Mathf.RoundToInt(error * 100f) + "%";
            _readout.color = error < 0.06f ? Art.Good : Art.TextDim;

            if (error < 0.06f)
            {
                _hold += dt;
                if (_hold > 0.7f) Complete();
            }
            else _hold = 0f;
        }
    }

    // ================================================================= pipes
    /// <summary>Rotate pipe tiles until coolant flows from the inlet to the outlet.</summary>
    public class PipeRouteGame : MiniGameBase
    {
        private const int Cols = 5;
        private const int Rows = 3;

        // bit mask: 1 = up, 2 = right, 4 = down, 8 = left
        private int[] _tiles;
        private int[] _rotation;
        private Image[] _images;
        private Text _status;

        public override string Instruction => "Собери магистраль от входа к выходу";

        protected override void Build()
        {
            _tiles = new int[Cols * Rows];
            _rotation = new int[Cols * Rows];
            _images = new Image[Cols * Rows];

            // lay a guaranteed solvable snake, then randomise rotations
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    int i = r * Cols + c;
                    int mask = 0;
                    bool leftToRight = r % 2 == 0;
                    if (leftToRight)
                    {
                        if (c > 0) mask |= 8;
                        if (c < Cols - 1) mask |= 2;
                        if (c == Cols - 1 && r < Rows - 1) { mask |= 8; mask |= 4; }
                    }
                    else
                    {
                        if (c < Cols - 1) mask |= 2;
                        if (c > 0) mask |= 8;
                        if (c == 0 && r < Rows - 1) { mask |= 2; mask |= 4; }
                    }
                    if (r > 0)
                    {
                        bool prevEndsRight = (r - 1) % 2 == 0;
                        if ((prevEndsRight && c == Cols - 1) || (!prevEndsRight && c == 0)) mask |= 1;
                    }
                    _tiles[i] = mask == 0 ? 10 : mask;
                }
            }

            float cell = 130f, gap = 12f;
            float w = Cols * cell + (Cols - 1) * gap;
            float h = Rows * cell + (Rows - 1) * gap;

            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int i = r * Cols + c;
                var pos = new Vector2(-w * 0.5f + cell * 0.5f + c * (cell + gap),
                                       h * 0.5f - cell * 0.5f - r * (cell + gap) - 10f);
                var img = UIKit.Panel(Root, "Tile" + i, pos, new Vector2(cell, cell), new Color(0.11f, 0.14f, 0.19f, 0.96f), 12);
                _images[i] = img;
                DrawTile(img.transform, _tiles[i]);
                _rotation[i] = Rng.NextInt(4);
                img.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * _rotation[i]);

                int index = i;
                var relay = UIKit.AddPointer(img.gameObject);
                relay.Clicked += _ => Rotate(index);
            }

            UIKit.Label(Root, "ВХОД", new Vector2(-w * 0.5f - 70f, h * 0.5f - cell * 0.5f - 10f), new Vector2(120f, 40f), 22,
                TextAnchor.MiddleCenter, Art.Good);
            UIKit.Label(Root, "ВЫХОД", new Vector2(w * 0.5f + 76f, -h * 0.5f + cell * 0.5f - 10f), new Vector2(130f, 40f), 22,
                TextAnchor.MiddleCenter, Art.AccentWarm);
            _status = UIKit.Label(Root, "", new Vector2(0f, -h * 0.5f - 70f), new Vector2(700f, 44f), 26,
                TextAnchor.MiddleCenter, Art.TextDim);
        }

        private void DrawTile(Transform parent, int mask)
        {
            var color = new Color(0.45f, 0.75f, 0.95f);
            if ((mask & 1) != 0) UIKit.Sprite(parent, "U", Art.SolidSprite(), new Vector2(0f, 32f), new Vector2(22f, 64f), color);
            if ((mask & 2) != 0) UIKit.Sprite(parent, "R", Art.SolidSprite(), new Vector2(32f, 0f), new Vector2(64f, 22f), color);
            if ((mask & 4) != 0) UIKit.Sprite(parent, "D", Art.SolidSprite(), new Vector2(0f, -32f), new Vector2(22f, 64f), color);
            if ((mask & 8) != 0) UIKit.Sprite(parent, "L", Art.SolidSprite(), new Vector2(-32f, 0f), new Vector2(64f, 22f), color);
            UIKit.Sprite(parent, "Hub", Art.Circle(48), Vector2.zero, new Vector2(30f, 30f), color);
        }

        private void Rotate(int i)
        {
            if (IsDone) return;
            _rotation[i] = (_rotation[i] + 1) % 4;
            _images[i].transform.localRotation = Quaternion.Euler(0f, 0f, -90f * _rotation[i]);
            Audio.SoundBank.Play(Audio.Sfx.UiClick, 0.4f);
            if (Solved())
            {
                _status.text = "Магистраль собрана";
                _status.color = Art.Good;
                Complete();
            }
        }

        private int MaskOf(int i)
        {
            int m = _tiles[i];
            for (int k = 0; k < _rotation[i]; k++)
                m = ((m << 1) | (m >> 3)) & 0xF;
            return m;
        }

        private bool Solved()
        {
            // flood fill from the inlet (left edge of row 0) to the outlet
            var visited = new bool[Cols * Rows];
            var stack = new Stack<int>();
            if ((MaskOf(0) & 8) == 0) return false;
            stack.Push(0);

            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (visited[i]) continue;
                visited[i] = true;
                int c = i % Cols, r = i / Cols;
                int m = MaskOf(i);

                if ((m & 1) != 0 && r > 0 && (MaskOf(i - Cols) & 4) != 0) stack.Push(i - Cols);
                if ((m & 4) != 0 && r < Rows - 1 && (MaskOf(i + Cols) & 1) != 0) stack.Push(i + Cols);
                if ((m & 8) != 0 && c > 0 && (MaskOf(i - 1) & 2) != 0) stack.Push(i - 1);
                if ((m & 2) != 0 && c < Cols - 1 && (MaskOf(i + 1) & 8) != 0) stack.Push(i + 1);
            }

            int exit = (Rows - 1) * Cols + (Cols - 1);
            return visited[exit] && (MaskOf(exit) & 2) != 0;
        }
    }

    // ================================================================= debris
    /// <summary>Drag clogging debris out of the intake filter and into the chute.</summary>
    public class DebrisPurgeGame : MiniGameBase
    {
        private readonly int _count;
        private readonly List<Image> _debris = new List<Image>();
        private Image _chute;
        private Image _dragging;
        private int _cleared;
        private Text _readout;

        public DebrisPurgeGame(int count = 7) { _count = Mathf.Clamp(count, 4, 12); }

        public override string Instruction => "Вытащи мусор из фильтра в шлюз";

        protected override void Build()
        {
            UIKit.Sprite(Root, "Filter", Art.Circle(256, 0f), new Vector2(-190f, 20f), new Vector2(400f, 400f),
                new Color(0.14f, 0.18f, 0.22f, 0.95f));
            UIKit.Sprite(Root, "Grill", Art.Circle(256, 20f), new Vector2(-190f, 20f), new Vector2(400f, 400f),
                new Color(0.35f, 0.45f, 0.55f, 0.7f));

            _chute = UIKit.Panel(Root, "Chute", new Vector2(300f, 20f), new Vector2(260f, 320f),
                new Color(0.28f, 0.16f, 0.12f, 0.92f), 18);
            UIKit.Label(_chute.transform, "ШЛЮЗ", new Vector2(0f, -120f), new Vector2(240f, 44f), 26,
                TextAnchor.MiddleCenter, Art.AccentWarm, FontStyle.Bold);

            for (int i = 0; i < _count; i++)
            {
                var offset = Rng.InsideUnitCircle() * 145f;
                var pos = new Vector2(-190f, 20f) + offset;
                var img = UIKit.Sprite(Root, "Debris" + i, Art.RoundedRect(10, 48),
                    pos, new Vector2(Rng.Range(46f, 78f), Rng.Range(40f, 66f)),
                    Color.Lerp(new Color(0.45f, 0.38f, 0.26f), new Color(0.3f, 0.42f, 0.3f), Rng.Value01()));
                img.raycastTarget = true;
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Rng.Range(0f, 360f));
                _debris.Add(img);

                var relay = UIKit.AddPointer(img.gameObject);
                relay.Down += _ => _dragging = img;
                relay.Dragged += e =>
                {
                    if (_dragging != img) return;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, e.position, e.pressEventCamera, out var l))
                        img.rectTransform.anchoredPosition = l;
                };
                relay.Up += _ => Drop(img);
                relay.DragEnded += _ => Drop(img);
            }

            _readout = UIKit.Label(Root, "Очищено: 0/" + _count, new Vector2(0f, -235f), new Vector2(700f, 44f), 26,
                TextAnchor.MiddleCenter, Art.TextDim);
        }

        private void Drop(Image img)
        {
            if (_dragging != img || IsDone) return;
            _dragging = null;
            var p = img.rectTransform.anchoredPosition;
            var chuteRt = _chute.rectTransform;
            if (Mathf.Abs(p.x - chuteRt.anchoredPosition.x) < chuteRt.sizeDelta.x * 0.5f &&
                Mathf.Abs(p.y - chuteRt.anchoredPosition.y) < chuteRt.sizeDelta.y * 0.5f)
            {
                Object.Destroy(img.gameObject);
                _cleared++;
                Progress();
                _readout.text = "Очищено: " + _cleared + "/" + _count;
                if (_cleared >= _count) Complete();
            }
        }
    }

    // ================================================================= grid align
    /// <summary>Shift the rows until the diagnostic pattern lines up in a column.</summary>
    public class GridAlignGame : MiniGameBase
    {
        private const int Rows = 4;
        private const int Cols = 6;
        private int[] _offset;
        private int[] _marker;
        private readonly List<Image[]> _cells = new List<Image[]>();
        private Text _status;

        public override string Instruction => "Выровняй метки в один столбец";

        protected override void Build()
        {
            _offset = new int[Rows];
            _marker = new int[Rows];
            float cell = 118f, gap = 10f;
            float w = Cols * cell + (Cols - 1) * gap;

            for (int r = 0; r < Rows; r++)
            {
                _marker[r] = Rng.NextInt(Cols);
                _offset[r] = Rng.NextInt(Cols);
                var row = new Image[Cols];
                for (int c = 0; c < Cols; c++)
                {
                    var pos = new Vector2(-w * 0.5f + cell * 0.5f + c * (cell + gap), 170f - r * (cell * 0.72f + gap));
                    row[c] = UIKit.Panel(Root, "C" + r + "_" + c, pos, new Vector2(cell, cell * 0.68f),
                        new Color(0.11f, 0.14f, 0.19f, 0.96f), 10);
                }
                _cells.Add(row);

                int index = r;
                UIKit.Button(Root, "◀", new Vector2(-w * 0.5f - 70f, 170f - r * (cell * 0.72f + gap)), new Vector2(90f, 72f),
                    () => Shift(index, -1), Art.PanelSoft, 30, 12);
                UIKit.Button(Root, "▶", new Vector2(w * 0.5f + 70f, 170f - r * (cell * 0.72f + gap)), new Vector2(90f, 72f),
                    () => Shift(index, 1), Art.PanelSoft, 30, 12);
            }

            _status = UIKit.Label(Root, "", new Vector2(0f, -215f), new Vector2(700f, 44f), 26, TextAnchor.MiddleCenter, Art.TextDim);
            Refresh();
        }

        private void Shift(int row, int dir)
        {
            if (IsDone) return;
            _offset[row] = (_offset[row] + dir + Cols) % Cols;
            Audio.SoundBank.Play(Audio.Sfx.UiClick, 0.4f);
            Refresh();

            int target = (_marker[0] + _offset[0]) % Cols;
            for (int r = 1; r < Rows; r++)
                if ((_marker[r] + _offset[r]) % Cols != target) return;

            _status.text = "Диагностика пройдена";
            _status.color = Art.Good;
            Complete();
        }

        private void Refresh()
        {
            for (int r = 0; r < Rows; r++)
            {
                int lit = (_marker[r] + _offset[r]) % Cols;
                for (int c = 0; c < Cols; c++)
                    _cells[r][c].color = c == lit ? Art.Accent : new Color(0.11f, 0.14f, 0.19f, 0.96f);
            }
        }
    }
}
