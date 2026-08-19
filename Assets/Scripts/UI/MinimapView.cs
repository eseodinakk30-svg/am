// -----------------------------------------------------------------------------
//  NEBULA NINE - minimap / full station map.
//
//  Rooms are drawn straight from StationLayout, so the map can never drift out of
//  sync with the level.  Overlays: your own marker, task objectives, the active
//  sabotage, doors, and (for infiltrators) the vent network.
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
    public class MinimapView : MonoBehaviour
    {
        private RectTransform _root;
        private RectTransform _layer;
        private MatchManager _match;
        private DeckId _deck = DeckId.Upper;
        private Vector2 _size;
        private bool _showLabels;

        private readonly List<Image> _taskMarkers = new List<Image>();
        private readonly List<Image> _ventMarkers = new List<Image>();
        private Image _selfMarker;
        private Image _sabotageMarker;
        private Text _deckLabel;
        private float _refreshTimer;

        public static MinimapView Create(Transform parent, MatchManager match, Vector2 pos, Vector2 size, bool labels)
        {
            var rt = UIKit.Node(parent, "Minimap", pos, size);
            var view = rt.gameObject.AddComponent<MinimapView>();
            view._root = rt;
            view._match = match;
            view._size = size;
            view._showLabels = labels;
            view.Build();
            return view;
        }

        private void Build()
        {
            UIKit.PanelStretch(_root, "Bg", new Color(0.04f, 0.06f, 0.10f, 0.82f), 14);
            _layer = UIKit.Stretch(_root, "Layer", 8f);
            _deckLabel = UIKit.Label(_root, "", new Vector2(0f, _size.y * 0.5f - 16f), new Vector2(_size.x, 28f), 18,
                TextAnchor.MiddleCenter, Art.TextDim);
            Redraw();
        }

        public void SetDeck(DeckId deck)
        {
            if (_deck == deck) return;
            _deck = deck;
            Redraw();
        }

        private Vector2 CellToLocal(float cellX, float cellZ)
        {
            float w = _size.x - 16f, h = _size.y - 16f;
            return new Vector2((cellX / StationLayout.GridW - 0.5f) * w, (cellZ / StationLayout.GridH - 0.5f) * h);
        }

        private Vector2 SizeOf(RectInt rect)
        {
            float w = _size.x - 16f, h = _size.y - 16f;
            return new Vector2(rect.width / (float)StationLayout.GridW * w, rect.height / (float)StationLayout.GridH * h);
        }

        private void Redraw()
        {
            for (int i = _layer.childCount - 1; i >= 0; i--) Destroy(_layer.GetChild(i).gameObject);
            _taskMarkers.Clear();
            _ventMarkers.Clear();

            foreach (var area in StationLayout.Areas)
            {
                if (area.Deck != _deck) continue;
                var center = CellToLocal(area.Rect.xMin + area.Rect.width * 0.5f, area.Rect.yMin + area.Rect.height * 0.5f);
                var size = SizeOf(area.Rect);
                var color = area.Type == AreaType.Room
                    ? new Color(area.Tint.r + 0.10f, area.Tint.g + 0.12f, area.Tint.b + 0.16f, 0.95f)
                    : new Color(0.22f, 0.26f, 0.34f, 0.85f);
                if (area.Type == AreaType.Doorway) color = new Color(0.42f, 0.52f, 0.62f, 0.9f);

                var img = UIKit.Panel(_layer, area.Key ?? "a", center, size, color, area.Type == AreaType.Room ? 6 : 3);
                img.raycastTarget = false;

                if (_showLabels && area.Type == AreaType.Room && size.x > 42f)
                {
                    var label = UIKit.Label(_layer, area.Name, center, new Vector2(size.x, 22f), 15,
                        TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f, 0.85f));
                    label.raycastTarget = false;
                }
            }

            _selfMarker = UIKit.Sprite(_layer, "Self", Art.Circle(48), Vector2.zero, new Vector2(14f, 14f), Color.white);
            _sabotageMarker = UIKit.Sprite(_layer, "Sabotage", Art.Circle(48, 6f), Vector2.zero, new Vector2(26f, 26f), Art.Danger);
            _sabotageMarker.gameObject.SetActive(false);

            if (_deckLabel != null)
                _deckLabel.text = _deck == DeckId.Upper ? "ВЕРХНЯЯ ПАЛУБА" : "НИЖНЯЯ ПАЛУБА";
        }

        private void Update()
        {
            if (_match == null || _match.Local == null) return;

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = 0.15f;

            var local = _match.Local;
            SetDeck(local.Deck);

            var cell = StationLayout.WorldToCell(local.Position);
            _selfMarker.rectTransform.anchoredPosition = CellToLocal(cell.x, cell.y);
            _selfMarker.color = local.Color;

            // task objectives
            int index = 0;
            foreach (var task in local.Tasks)
            {
                if (task.IsComplete) continue;
                var area = StationLayout.Get(task.CurrentRoomId);
                if (area == null || area.Deck != _deck) continue;

                Image marker;
                if (index < _taskMarkers.Count) marker = _taskMarkers[index];
                else
                {
                    marker = UIKit.Sprite(_layer, "Task", Art.Circle(48, 5f), Vector2.zero, new Vector2(18f, 18f),
                        new Color(0.98f, 0.82f, 0.28f));
                    _taskMarkers.Add(marker);
                }
                marker.gameObject.SetActive(true);
                marker.rectTransform.anchoredPosition = CellToLocal(area.CenterCell.x, area.CenterCell.y);
                index++;
            }
            for (int i = index; i < _taskMarkers.Count; i++) _taskMarkers[i].gameObject.SetActive(false);

            // vents for infiltrators
            if (local.Role == Role.Infiltrator)
            {
                int v = 0;
                foreach (var vent in StationLayout.Vents)
                {
                    if (vent.Deck != _deck) continue;
                    Image marker;
                    if (v < _ventMarkers.Count) marker = _ventMarkers[v];
                    else
                    {
                        marker = UIKit.Sprite(_layer, "Vent", Art.RoundedRect(4, 16), Vector2.zero, new Vector2(11f, 11f),
                            new Color(0.95f, 0.45f, 0.35f, 0.9f));
                        _ventMarkers.Add(marker);
                    }
                    marker.gameObject.SetActive(true);
                    marker.rectTransform.anchoredPosition = CellToLocal(vent.Cell.x, vent.Cell.y);
                    v++;
                }
                for (int i = v; i < _ventMarkers.Count; i++) _ventMarkers[i].gameObject.SetActive(false);
            }

            // sabotage marker
            var sab = _match.Sabotage;
            if (sab != null && sab.IsActive && sab.Panels.Count > 0)
            {
                var panel = sab.Panels[0];
                var area = StationLayout.Get(panel.RoomId);
                bool onDeck = area != null && area.Deck == _deck;
                _sabotageMarker.gameObject.SetActive(onDeck);
                if (onDeck) _sabotageMarker.rectTransform.anchoredPosition = CellToLocal(area.CenterCell.x, area.CenterCell.y);
            }
            else _sabotageMarker.gameObject.SetActive(false);
        }
    }
}
