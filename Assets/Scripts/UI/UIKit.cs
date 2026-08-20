// -----------------------------------------------------------------------------
//  NEBULA NINE - procedural uGUI toolkit.
//
//  Every screen in the game is built from these helpers: panels, labels, buttons,
//  sliders, draggable nodes, gauges.  No prefabs, no TextMeshPro asset import -
//  the project runs straight after cloning.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Fx;

namespace Nebula.UI
{
    public static class UIKit
    {
        public static readonly Vector2 DesignResolution = new Vector2(1600f, 900f);

        // ------------------------------------------------------------------ core
        public static RectTransform Node(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name, float margin = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
            return rt;
        }

        /// <summary>Anchored to a corner/edge: anchor (0..1, 0..1), offset in pixels from that anchor.</summary>
        public static RectTransform Anchored(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>
        /// Область, заданная долями родителя, а не пикселями от центра. Экраны
        /// телефонов сильно шире 16:9, и разметка «столько-то пикселей влево от
        /// середины» оставляла по краям пустые поля, а на узких — наползала.
        /// </summary>
        public static RectTransform Region(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float pad = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        /// <summary>Растянутый по горизонтали элемент внутри карточки: слева и справа отступы в пикселях.</summary>
        public static RectTransform Row(Transform parent, string name, float left, float right, float centreY, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-right, 0f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, centreY);
            return rt;
        }

        /// <summary>Текст, растянутый по ширине родителя. Ширина известна сразу, поэтому
        /// перенос строк считается верно — это важно для чата.</summary>
        public static Text RowLabel(Transform parent, string text, float left, float right, float centreY, float height,
            int fontSize = 22, TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var rt = Row(parent, "Label", left, right, centreY, height);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Art.UiFont;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = anchor;
            t.text = text;
            t.color = color ?? Art.TextMain;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = true;
            return t;
        }

        public static Image Panel(Transform parent, string name, Vector2 pos, Vector2 size, Color color, int radius = 18)
        {
            var rt = Node(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = radius > 0 ? Art.RoundedRect(radius, 64) : Art.SolidSprite();
            img.type = radius > 0 ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            return img;
        }

        public static Image PanelStretch(Transform parent, string name, Color color, int radius = 18, float margin = 0f)
        {
            var rt = Stretch(parent, name, margin);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = radius > 0 ? Art.RoundedRect(radius, 64) : Art.SolidSprite();
            img.type = radius > 0 ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            return img;
        }

        public static Image Icon(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Node(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize = 26,
            TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var rt = Node(parent, "Label", pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Art.UiFont;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = anchor;
            t.text = text;
            t.color = color ?? Art.TextMain;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = true;
            return t;
        }

        public static UnityEngine.UI.Button Button(Transform parent, string caption, Vector2 pos, Vector2 size, Action onClick,
            Color? bg = null, int fontSize = 28, int radius = 16)
        {
            var img = Panel(parent, "Button_" + caption, pos, size, bg ?? Art.PanelSoft, radius);
            var btn = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.78f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            colors.fadeDuration = 0.07f;
            btn.colors = colors;

            if (!string.IsNullOrEmpty(caption))
                Label(img.transform, caption, Vector2.zero, size, fontSize, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            if (onClick != null)
            {
                btn.onClick.AddListener(() =>
                {
                    Audio.SoundBank.Play(Audio.Sfx.UiClick, 0.55f);
                    onClick();
                });
            }
            return btn;
        }

        public static Image CircleButton(Transform parent, string caption, Vector2 anchor, Vector2 offset, float diameter,
            Color color, Action onClick, out Text label)
        {
            var rt = Anchored(parent, "CircleButton_" + caption, anchor, offset, new Vector2(diameter, diameter));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Art.Circle(128);
            img.color = color;

            var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            if (onClick != null)
            {
                btn.onClick.AddListener(() =>
                {
                    Audio.SoundBank.Play(Audio.Sfx.UiClick, 0.5f);
                    onClick();
                });
            }

            label = Label(rt, caption, Vector2.zero, new Vector2(diameter, diameter * 0.5f),
                Mathf.RoundToInt(diameter * 0.19f), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            return img;
        }

        /// <summary>Horizontal fill bar. Returns the fill image (use fillAmount).</summary>
        /// <summary>Полоска, занимающая готовый прямоугольник разметки.</summary>
        public static Image BarIn(RectTransform rt, Color bg, Color fill, int radius = 10)
        {
            var back = PanelStretch(rt, "BarBg", bg, radius);
            back.raycastTarget = false;
            var fillRt = Stretch(rt, "BarFill");
            var img = fillRt.gameObject.AddComponent<Image>();
            img.sprite = Art.RoundedRect(radius, 64);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.fillAmount = 0f;
            img.color = fill;
            img.raycastTarget = false;
            return img;
        }

        public static Image Bar(Transform parent, Vector2 pos, Vector2 size, Color bg, Color fill, int radius = 10)
        {
            Panel(parent, "BarBg", pos, size, bg, radius);
            var rt = Node(parent, "BarFill", pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Art.RoundedRect(radius, 64);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.fillAmount = 0f;
            img.color = fill;
            img.raycastTarget = false;
            return img;
        }

        public static UnityEngine.UI.Slider Slider(Transform parent, Vector2 pos, Vector2 size, float min, float max, float value,
            Action<float> onChanged, bool vertical = false)
        {
            var rt = Node(parent, "Slider", pos, size);
            var slider = rt.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.direction = vertical ? UnityEngine.UI.Slider.Direction.BottomToTop : UnityEngine.UI.Slider.Direction.LeftToRight;

            var bg = Panel(rt, "Bg", Vector2.zero, size, new Color(0.06f, 0.08f, 0.12f, 0.9f), 12);
            bg.raycastTarget = true;

            var fillArea = Stretch(rt, "FillArea", 6f);
            var fillRt = Stretch(fillArea, "Fill", 0f);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = Art.RoundedRect(10, 48);
            fill.type = Image.Type.Sliced;
            fill.color = Art.Accent;

            var handleArea = Stretch(rt, "HandleArea", 4f);
            var handleRt = Node(handleArea, "Handle", Vector2.zero,
                vertical ? new Vector2(size.x + 12f, 46f) : new Vector2(46f, size.y + 12f));
            var handle = handleRt.gameObject.AddComponent<Image>();
            handle.sprite = Art.RoundedRect(14, 48);
            handle.type = Image.Type.Sliced;
            handle.color = Art.TextMain;

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        public static UnityEngine.UI.Toggle Toggle(Transform parent, string caption, Vector2 pos, Vector2 size, bool value, Action<bool> onChanged)
        {
            var rt = Node(parent, "Toggle", pos, size);
            var toggle = rt.gameObject.AddComponent<UnityEngine.UI.Toggle>();

            var box = Panel(rt, "Box", new Vector2(-size.x * 0.5f + 26f, 0f), new Vector2(40f, 40f),
                new Color(0.08f, 0.10f, 0.14f, 0.95f), 10);
            toggle.targetGraphic = box;

            var check = Icon(box.transform, "Check", Art.RoundedRect(8, 32), Vector2.zero, new Vector2(24f, 24f), Art.Accent);
            toggle.graphic = check;
            toggle.isOn = value;
            Label(rt, caption, new Vector2(24f, 0f), new Vector2(size.x - 60f, size.y), 24, TextAnchor.MiddleLeft);
            if (onChanged != null) toggle.onValueChanged.AddListener(v => onChanged(v));
            return toggle;
        }

        /// <summary>Кнопка поверх готового прямоугольника — когда положение задаёт разметка, а не пиксели.</summary>
        public static UnityEngine.UI.Button ButtonIn(RectTransform rt, string caption, Action onClick,
            Color? bg = null, int fontSize = 28, int radius = 16)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = radius > 0 ? Art.RoundedRect(radius, 64) : Art.SolidSprite();
            img.type = radius > 0 ? Image.Type.Sliced : Image.Type.Simple;
            img.color = bg ?? Art.PanelSoft;

            var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.78f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            colors.fadeDuration = 0.07f;
            btn.colors = colors;

            if (!string.IsNullOrEmpty(caption))
            {
                var label = Stretch(rt, "Caption", 6f);
                var t = label.gameObject.AddComponent<Text>();
                t.font = Art.UiFont;
                t.fontSize = fontSize;
                t.fontStyle = FontStyle.Bold;
                t.alignment = TextAnchor.MiddleCenter;
                t.text = caption;
                t.color = Art.TextMain;
                t.raycastTarget = false;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }

            if (onClick != null)
            {
                btn.onClick.AddListener(() =>
                {
                    Audio.SoundBank.Play(Audio.Sfx.UiClick, 0.55f);
                    onClick();
                });
            }
            return btn;
        }

        /// <summary>Прокрутка, занимающая весь родительский прямоугольник.</summary>
        public static ScrollRect ScrollViewStretch(Transform parent, string name, out RectTransform content)
        {
            var root = Stretch(parent, name);
            var mask = root.gameObject.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.001f);
            root.gameObject.AddComponent<RectMask2D>();

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            content = Stretch(root, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, content.offsetMin.y);
            content.offsetMax = new Vector2(0f, content.offsetMax.y);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 10f);

            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
            scroll.scrollSensitivity = 32f;
            scroll.viewport = root;
            return scroll;
        }

        public static ScrollRect ScrollView(Transform parent, string name, Vector2 pos, Vector2 size, out RectTransform content)
        {
            var root = Node(parent, name, pos, size);
            var mask = root.gameObject.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.001f);
            root.gameObject.AddComponent<RectMask2D>();

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            content = Node(root, "Content", Vector2.zero, size);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, size.y);

            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
            scroll.scrollSensitivity = 32f;
            scroll.viewport = root;
            return scroll;
        }

        // ------------------------------------------------------------------ interaction
        public static PointerRelay AddPointer(GameObject go)
        {
            var relay = go.GetComponent<PointerRelay>();
            if (relay == null) relay = go.AddComponent<PointerRelay>();
            var graphic = go.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastTarget = true;
            return relay;
        }

        public static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            c.a = a;
            g.color = c;
        }

        public static CanvasGroup Group(GameObject go)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }
    }

    /// <summary>Pointer callbacks for arbitrary UI graphics (drag puzzles, hold buttons).</summary>
    public class PointerRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler,
        IBeginDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action<PointerEventData> Down;
        public Action<PointerEventData> Up;
        public Action<PointerEventData> Dragged;
        public Action<PointerEventData> DragBegun;
        public Action<PointerEventData> DragEnded;
        public Action<PointerEventData> Entered;
        public Action<PointerEventData> Exited;
        public Action<PointerEventData> Clicked;

        public bool IsDown { get; private set; }

        public void OnPointerDown(PointerEventData e) { IsDown = true; Down?.Invoke(e); }
        public void OnPointerUp(PointerEventData e) { IsDown = false; Up?.Invoke(e); }
        public void OnDrag(PointerEventData e) => Dragged?.Invoke(e);
        public void OnBeginDrag(PointerEventData e) => DragBegun?.Invoke(e);
        public void OnEndDrag(PointerEventData e) => DragEnded?.Invoke(e);
        public void OnPointerEnter(PointerEventData e) => Entered?.Invoke(e);
        public void OnPointerExit(PointerEventData e) => Exited?.Invoke(e);
        public void OnPointerClick(PointerEventData e) => Clicked?.Invoke(e);
    }

    /// <summary>Simple frame-driven tween helpers used by menus and toasts.</summary>
    public class UiTween : MonoBehaviour
    {
        private readonly List<Action<float>> _steps = new List<Action<float>>();
        private readonly List<float> _durations = new List<float>();
        private readonly List<float> _elapsed = new List<float>();

        public static UiTween Ensure(GameObject go)
        {
            var t = go.GetComponent<UiTween>();
            return t != null ? t : go.AddComponent<UiTween>();
        }

        public void Run(float duration, Action<float> step)
        {
            _steps.Add(step);
            _durations.Add(Mathf.Max(0.0001f, duration));
            _elapsed.Add(0f);
        }

        private void Update()
        {
            for (int i = _steps.Count - 1; i >= 0; i--)
            {
                _elapsed[i] += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_elapsed[i] / _durations[i]);
                _steps[i]?.Invoke(t);
                if (t >= 1f)
                {
                    _steps.RemoveAt(i);
                    _durations.RemoveAt(i);
                    _elapsed.RemoveAt(i);
                }
            }
        }
    }
}
