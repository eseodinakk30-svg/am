// -----------------------------------------------------------------------------
//  NEBULA NINE - карточка роли в начале матча.
//
//  Раньше роль сообщалась узкой полоской поверх HUD, и половина игроков её
//  просто не замечала — особенно те, кому выпал диверсант. Теперь это отдельный
//  полноэкранный кадр: сторона, профессия, что делать и, для диверсантов,
//  кто с ними заодно.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Gameplay;

namespace Nebula.UI
{
    public class RoleRevealScreen : MonoBehaviour
    {
        private RectTransform _root;
        private Image _backdrop;
        private Image _card;
        private Text _side;
        private Text _title;
        private Text _hint;
        private Text _mates;
        private CanvasGroup _group;
        private float _timer;

        public static RoleRevealScreen Create(Transform parent)
        {
            var rt = UIKit.Stretch(parent, "RoleReveal");
            var screen = rt.gameObject.AddComponent<RoleRevealScreen>();
            screen._root = rt;
            screen.Build();
            rt.gameObject.SetActive(false);
            return screen;
        }

        private void Build()
        {
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;

            _backdrop = UIKit.Panel(_root, "Backdrop", Vector2.zero, new Vector2(4000f, 4000f),
                                    new Color(0.02f, 0.03f, 0.05f, 0.93f), 0);

            _card = UIKit.Panel(_root, "Card", Vector2.zero, new Vector2(980f, 520f),
                                new Color(0.09f, 0.11f, 0.16f, 0.97f), 34);

            _side = UIKit.Label(_card.transform, "", new Vector2(0f, 186f), new Vector2(900f, 60f), 34,
                                TextAnchor.MiddleCenter, new Color(0.72f, 0.78f, 0.88f), FontStyle.Normal);

            _title = UIKit.Label(_card.transform, "", new Vector2(0f, 92f), new Vector2(920f, 130f), 104,
                                 TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

            _hint = UIKit.Label(_card.transform, "", new Vector2(0f, -34f), new Vector2(860f, 130f), 32,
                                TextAnchor.UpperCenter, new Color(0.86f, 0.90f, 0.96f));

            _mates = UIKit.Label(_card.transform, "", new Vector2(0f, -178f), new Vector2(860f, 110f), 30,
                                 TextAnchor.UpperCenter, new Color(0.98f, 0.55f, 0.55f));
        }

        /// <summary>Показывает карточку на указанное число секунд.</summary>
        public void Show(PlayerState local, IReadOnlyList<PlayerState> everyone, float seconds)
        {
            if (local == null) return;

            bool impostor = local.Role == Role.Infiltrator;
            _side.text = impostor ? "ТЫ ПРОТИВ ВСЕХ" : "ТЫ ЗА ЭКИПАЖ";
            _title.text = MatchManager.RoleTitle(local);
            _title.color = impostor ? new Color(0.98f, 0.35f, 0.36f) : new Color(0.45f, 0.86f, 0.98f);
            _card.color = impostor ? new Color(0.20f, 0.06f, 0.08f, 0.97f) : new Color(0.06f, 0.13f, 0.19f, 0.97f);
            _hint.text = MatchManager.RoleHint(local);

            _mates.text = impostor ? Teammates(local, everyone) : "";

            _timer = Mathf.Max(0.6f, seconds);
            _root.gameObject.SetActive(true);
            _group.alpha = 0f;
        }

        private static string Teammates(PlayerState local, IReadOnlyList<PlayerState> everyone)
        {
            if (everyone == null) return "";
            var names = new List<string>();
            for (int i = 0; i < everyone.Count; i++)
            {
                var p = everyone[i];
                if (p == null || p == local || p.Role != Role.Infiltrator) continue;
                names.Add(p.Name);
            }
            if (names.Count == 0) return "Ты один. Никто не прикроет.";

            var sb = new StringBuilder(names.Count == 1 ? "С тобой заодно: " : "С тобой заодно: ");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(i == names.Count - 1 ? " и " : ", ");
                sb.Append(names[i]);
            }
            return sb.ToString();
        }

        public void Hide()
        {
            _timer = 0f;
            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;

            // проявление в начале и уход в конце, чтобы кадр не моргал
            _group.alpha = Mathf.Lerp(_group.alpha, _timer > 0.45f ? 1f : 0f, Time.deltaTime * 9f);
            if (_timer <= 0f)
            {
                _group.alpha = 0f;
                _root.gameObject.SetActive(false);
            }
        }
    }
}
