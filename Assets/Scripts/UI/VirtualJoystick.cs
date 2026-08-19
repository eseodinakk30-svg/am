// -----------------------------------------------------------------------------
//  NEBULA NINE - floating virtual stick.
//  Anywhere in the left half of the screen becomes the stick origin on touch down,
//  which is what mobile players expect; the knob is clamped to the ring.
// -----------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Fx;

namespace Nebula.UI
{
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public Vector2 Value { get; private set; }
        public float Radius = 130f;

        private RectTransform _self;
        private RectTransform _ring;
        private RectTransform _knob;
        private Image _ringImage;
        private Image _knobImage;
        private Canvas _canvas;
        private int _pointerId = -99;
        private Vector2 _origin;

        public static VirtualJoystick Create(Transform parent, bool leftHanded)
        {
            var rt = UIKit.Anchored(parent, "JoystickArea",
                leftHanded ? new Vector2(1f, 0f) : new Vector2(0f, 0f),
                Vector2.zero, Vector2.zero);
            rt.anchorMin = leftHanded ? new Vector2(0.5f, 0f) : new Vector2(0f, 0f);
            rt.anchorMax = leftHanded ? new Vector2(1f, 1f) : new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f);
            img.raycastTarget = true;

            var stick = rt.gameObject.AddComponent<VirtualJoystick>();
            stick.Build();
            return stick;
        }

        private void Build()
        {
            _self = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();

            _ring = UIKit.Node(transform, "Ring", Vector2.zero, new Vector2(Radius * 2f, Radius * 2f));
            _ringImage = _ring.gameObject.AddComponent<Image>();
            _ringImage.sprite = Art.Circle(192, 16f);
            _ringImage.color = new Color(1f, 1f, 1f, 0.16f);
            _ringImage.raycastTarget = false;

            _knob = UIKit.Node(_ring, "Knob", Vector2.zero, new Vector2(Radius * 0.85f, Radius * 0.85f));
            _knobImage = _knob.gameObject.AddComponent<Image>();
            _knobImage.sprite = Art.Circle(128);
            _knobImage.color = new Color(1f, 1f, 1f, 0.30f);
            _knobImage.raycastTarget = false;

            SetVisible(false);
        }

        private void SetVisible(bool on)
        {
            if (_ringImage != null) _ringImage.enabled = on;
            if (_knobImage != null) _knobImage.enabled = on;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointerId != -99) return;
            _pointerId = e.pointerId;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_self, e.position, Cam(e), out var local))
            {
                _origin = local;
                _ring.anchoredPosition = local;
                _knob.anchoredPosition = Vector2.zero;
                SetVisible(true);
            }
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointerId) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_self, e.position, Cam(e), out var local)) return;
            var delta = local - _origin;
            var clamped = Vector2.ClampMagnitude(delta, Radius);
            _knob.anchoredPosition = clamped;
            Value = clamped / Radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointerId) return;
            _pointerId = -99;
            Value = Vector2.zero;
            _knob.anchoredPosition = Vector2.zero;
            SetVisible(false);
        }

        private Camera Cam(PointerEventData e)
        {
            if (_canvas == null) return null;
            return _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        }
    }
}
