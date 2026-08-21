// -----------------------------------------------------------------------------
//  NEBULA NINE - консоль комнаты: то, что открывается с ноутбука в столовой.
//  Слева облик, справа правила матча. Всё сохраняется сразу, без кнопки «ОК».
// -----------------------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Fx;

namespace Nebula.UI
{
    public class LobbyConsole : ModalBase
    {
        private Action _onChanged;

        public static LobbyConsole Create(Transform parent, Action onChanged)
        {
            var go = new GameObject("LobbyConsole", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<LobbyConsole>();
            view._onChanged = onChanged;
            view.BuildUi();
            go.SetActive(false);
            return view;
        }

        private void BuildUi()
        {
            BuildFrame(transform, "КОНСОЛЬ КОМНАТЫ", new Vector2(1420f, 800f), Close);

            var profile = GameSettings.Profile;
            var rules = GameSettings.Match;

            // ---------------------------------------------------------- облик
            UIKit.Label(Panel, "ОБЛИК", new Vector2(-350f, 300f), new Vector2(560f, 40f), 28,
                        TextAnchor.MiddleLeft, Art.AccentWarm, FontStyle.Bold);

            Cycler(Panel, "Цвет", new Vector2(-350f, 240f), ColorBank.SuitNames.Length,
                   () => profile.ColorIndex, v => profile.ColorIndex = v, i => ColorBank.SuitNames[i]);
            Cycler(Panel, "Костюм", new Vector2(-350f, 172f), CosmeticBank.Outfits.Length,
                   () => profile.OutfitIndex, v => profile.OutfitIndex = v, i => CosmeticBank.Outfits[i]);
            Cycler(Panel, "Шапка", new Vector2(-350f, 104f), CosmeticBank.Hats.Length,
                   () => profile.HatIndex, v => profile.HatIndex = v, i => CosmeticBank.Hats[i]);
            Cycler(Panel, "Аксессуар", new Vector2(-350f, 36f), CosmeticBank.Accessories.Length,
                   () => profile.AccessoryIndex, v => profile.AccessoryIndex = v, i => CosmeticBank.Accessories[i]);
            Cycler(Panel, "Эффект", new Vector2(-350f, -32f), CosmeticBank.Trails.Length,
                   () => profile.TrailIndex, v => profile.TrailIndex = v, i => CosmeticBank.Trails[i]);

            UIKit.Label(Panel, "Облик применится к следующему матчу.", new Vector2(-350f, -104f),
                        new Vector2(560f, 40f), 20, TextAnchor.MiddleLeft, Art.TextDim);

            // ---------------------------------------------------------- правила
            // Ролей стало много, и в фиксированную колонку они больше не влезают:
            // список правил прокручивается.
            UIKit.Label(Panel, "ПРАВИЛА", new Vector2(350f, 340f), new Vector2(560f, 40f), 28,
                        TextAnchor.MiddleLeft, Art.AccentWarm, FontStyle.Bold);

            var rulesArea = UIKit.Node(Panel, "RulesArea", new Vector2(350f, -20f), new Vector2(600f, 660f));
            UIKit.ScrollViewStretch(rulesArea, "RulesScroll", out var rules_content);

            float rowY = -34f;
            const float Pitch = 60f;
            Vector2 NextRow()
            {
                var pos = new Vector2(0f, rowY);
                rowY -= Pitch;
                return pos;
            }
            void Header(string text)
            {
                UIKit.Label(rules_content, text, new Vector2(-40f, rowY + 6f), new Vector2(420f, 34f), 21,
                            TextAnchor.MiddleLeft, Art.Accent, FontStyle.Bold);
                rowY -= 44f;
            }

            IntRow(rules_content, "Участников", NextRow(), 4, 15,
                   () => rules.PlayerCount, v => rules.PlayerCount = v);
            IntRow(rules_content, "Диверсантов", NextRow(), 1, 3,
                   () => rules.InfiltratorCount, v => rules.InfiltratorCount = v);
            Cycler(rules_content, "Моя роль", NextRow(), 3,
                   () => (int)rules.MyRole, v => rules.MyRole = (RoleWish)v,
                   i => i == 0 ? "как повезёт" : i == 1 ? "всегда экипаж" : "всегда диверсант");

            Header("РОЛИ ЭКИПАЖА");
            IntRow(rules_content, "Учёных", NextRow(), 0, 3,
                   () => rules.ScientistCount, v => rules.ScientistCount = v);
            IntRow(rules_content, "Инженеров", NextRow(), 0, 3,
                   () => rules.EngineerCount, v => rules.EngineerCount = v);
            IntRow(rules_content, "Следопытов", NextRow(), 0, 2,
                   () => rules.TrackerCount, v => rules.TrackerCount = v);
            IntRow(rules_content, "Ангелов-хранителей", NextRow(), 0, 2,
                   () => rules.GuardianCount, v => rules.GuardianCount = v);
            IntRow(rules_content, "Шумовиков", NextRow(), 0, 2,
                   () => rules.NoisemakerCount, v => rules.NoisemakerCount = v);

            Header("РОЛИ ДИВЕРСАНТОВ");
            IntRow(rules_content, "Оборотней", NextRow(), 0, 2,
                   () => rules.ShapeshifterCount, v => rules.ShapeshifterCount = v);
            IntRow(rules_content, "Фантомов", NextRow(), 0, 2,
                   () => rules.PhantomCount, v => rules.PhantomCount = v);

            Header("МАТЧ");
            FloatRow(rules_content, "Перезарядка убийства", NextRow(), 10f, 60f, 1f,
                     () => rules.KillCooldown, v => rules.KillCooldown = v, "с");
            FloatRow(rules_content, "Скорость", NextRow(), 3.5f, 9f, 0.1f,
                     () => rules.MoveSpeed, v => rules.MoveSpeed = v, "");
            IntRow(rules_content, "Коротких заданий", NextRow(), 0, 8,
                   () => rules.ShortTasks, v => rules.ShortTasks = v);
            IntRow(rules_content, "Долгих заданий", NextRow(), 0, 6,
                   () => rules.LongTasks, v => rules.LongTasks = v);
            IntRow(rules_content, "Общих заданий", NextRow(), 0, 4,
                   () => rules.CommonTasks, v => rules.CommonTasks = v);

            // высота содержимого = сколько строк реально выложили
            rules_content.sizeDelta = new Vector2(rules_content.sizeDelta.x, Mathf.Abs(rowY) + 40f);

            Cycler(Panel, "Сложность ИИ", new Vector2(-350f, -180f), 5,
                   () => (int)rules.AiDifficulty, v => rules.AiDifficulty = (Difficulty)v,
                   i => DifficultyName(i));
        }

        private static string DifficultyName(int i)
        {
            switch (i)
            {
                case 0: return "новичок";
                case 1: return "обычная";
                case 2: return "высокая";
                case 3: return "жёсткая";
                default: return "мастер";
            }
        }

        public void Open()
        {
            gameObject.SetActive(true);
        }

        public override void Close()
        {
            GameSettings.Match.Validate();
            GameSettings.Save();
            _onChanged?.Invoke();
            base.Close();
        }

        // ------------------------------------------------------------------ строки
        private void Cycler(Transform host, string label, Vector2 pos, int count, Func<int> get, Action<int> set, Func<int, string> nameOf)
        {
            if (count <= 0) return;
            UIKit.Label(host, label, pos + new Vector2(-260f, 0f), new Vector2(250f, 40f), 22,
                        TextAnchor.MiddleLeft, Art.TextDim);
            var value = UIKit.Label(host, nameOf(Mathf.Abs(get()) % count), pos + new Vector2(60f, 0f),
                                    new Vector2(240f, 40f), 22, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            void Step(int delta)
            {
                int v = (Mathf.Abs(get()) + delta + count) % count;
                set(v);
                value.text = nameOf(v);
                GameSettings.Save();
            }

            UIKit.Button(host, "◀", pos + new Vector2(-70f, 0f), new Vector2(56f, 48f), () => Step(-1), Art.PanelSoft, 22, 12);
            UIKit.Button(host, "▶", pos + new Vector2(190f, 0f), new Vector2(56f, 48f), () => Step(1), Art.PanelSoft, 22, 12);
        }

        private void IntRow(Transform host, string label, Vector2 pos, int min, int max, Func<int> get, Action<int> set)
        {
            UIKit.Label(host, label, pos + new Vector2(-260f, 0f), new Vector2(250f, 40f), 22,
                        TextAnchor.MiddleLeft, Art.TextDim);
            var value = UIKit.Label(host, get().ToString(), pos + new Vector2(60f, 0f),
                                    new Vector2(240f, 40f), 22, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            void Step(int delta)
            {
                int v = Mathf.Clamp(get() + delta, min, max);
                set(v);
                value.text = v.ToString();
                GameSettings.Save();
            }

            UIKit.Button(host, "−", pos + new Vector2(-70f, 0f), new Vector2(56f, 48f), () => Step(-1), Art.PanelSoft, 24, 12);
            UIKit.Button(host, "+", pos + new Vector2(190f, 0f), new Vector2(56f, 48f), () => Step(1), Art.PanelSoft, 24, 12);
        }

        private void FloatRow(Transform host, string label, Vector2 pos, float min, float max, float step,
                              Func<float> get, Action<float> set, string suffix)
        {
            UIKit.Label(host, label, pos + new Vector2(-260f, 0f), new Vector2(250f, 40f), 22,
                        TextAnchor.MiddleLeft, Art.TextDim);
            var value = UIKit.Label(host, Format(get(), suffix), pos + new Vector2(60f, 0f),
                                    new Vector2(240f, 40f), 22, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            void Step(float delta)
            {
                float v = Mathf.Clamp(get() + delta, min, max);
                set(v);
                value.text = Format(v, suffix);
                GameSettings.Save();
            }

            UIKit.Button(host, "−", pos + new Vector2(-70f, 0f), new Vector2(56f, 48f), () => Step(-step), Art.PanelSoft, 24, 12);
            UIKit.Button(host, "+", pos + new Vector2(190f, 0f), new Vector2(56f, 48f), () => Step(step), Art.PanelSoft, 24, 12);
        }

        private static string Format(float v, string suffix)
        {
            string body = Mathf.Abs(v - Mathf.Round(v)) < 0.05f
                ? Mathf.RoundToInt(v).ToString()
                : v.ToString("0.0");
            return string.IsNullOrEmpty(suffix) ? body : body + " " + suffix;
        }
    }
}
