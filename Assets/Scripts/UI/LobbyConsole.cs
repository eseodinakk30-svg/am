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

            Cycler("Цвет", new Vector2(-350f, 240f), ColorBank.SuitNames.Length,
                   () => profile.ColorIndex, v => profile.ColorIndex = v, i => ColorBank.SuitNames[i]);
            Cycler("Костюм", new Vector2(-350f, 172f), CosmeticBank.Outfits.Length,
                   () => profile.OutfitIndex, v => profile.OutfitIndex = v, i => CosmeticBank.Outfits[i]);
            Cycler("Шапка", new Vector2(-350f, 104f), CosmeticBank.Hats.Length,
                   () => profile.HatIndex, v => profile.HatIndex = v, i => CosmeticBank.Hats[i]);
            Cycler("Аксессуар", new Vector2(-350f, 36f), CosmeticBank.Accessories.Length,
                   () => profile.AccessoryIndex, v => profile.AccessoryIndex = v, i => CosmeticBank.Accessories[i]);
            Cycler("Эффект", new Vector2(-350f, -32f), CosmeticBank.Trails.Length,
                   () => profile.TrailIndex, v => profile.TrailIndex = v, i => CosmeticBank.Trails[i]);

            UIKit.Label(Panel, "Облик применится к следующему матчу.", new Vector2(-350f, -104f),
                        new Vector2(560f, 40f), 20, TextAnchor.MiddleLeft, Art.TextDim);

            // ---------------------------------------------------------- правила
            UIKit.Label(Panel, "ПРАВИЛА", new Vector2(350f, 300f), new Vector2(560f, 40f), 28,
                        TextAnchor.MiddleLeft, Art.AccentWarm, FontStyle.Bold);

            IntRow("Участников", new Vector2(350f, 240f), 4, 15,
                   () => rules.PlayerCount, v => rules.PlayerCount = v);
            IntRow("Диверсантов", new Vector2(350f, 180f), 1, 3,
                   () => rules.InfiltratorCount, v => rules.InfiltratorCount = v);

            Cycler("Моя роль", new Vector2(350f, 120f), 3,
                   () => (int)rules.MyRole, v => rules.MyRole = (RoleWish)v,
                   i => i == 0 ? "как повезёт" : i == 1 ? "всегда экипаж" : "всегда диверсант");

            IntRow("Учёных", new Vector2(350f, 60f), 0, 3,
                   () => rules.ScientistCount, v => rules.ScientistCount = v);
            IntRow("Инженеров", new Vector2(350f, 0f), 0, 3,
                   () => rules.EngineerCount, v => rules.EngineerCount = v);
            IntRow("Оборотней", new Vector2(350f, -60f), 0, 2,
                   () => rules.ShapeshifterCount, v => rules.ShapeshifterCount = v);

            FloatRow("Перезарядка убийства", new Vector2(350f, -120f), 10f, 60f, 1f,
                     () => rules.KillCooldown, v => rules.KillCooldown = v, "с");
            FloatRow("Скорость", new Vector2(350f, -180f), 3.5f, 9f, 0.1f,
                     () => rules.MoveSpeed, v => rules.MoveSpeed = v, "");
            IntRow("Коротких заданий", new Vector2(350f, -240f), 0, 8,
                   () => rules.ShortTasks, v => rules.ShortTasks = v);
            IntRow("Долгих заданий", new Vector2(350f, -300f), 0, 6,
                   () => rules.LongTasks, v => rules.LongTasks = v);

            Cycler("Сложность ИИ", new Vector2(-350f, -180f), 5,
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
        private void Cycler(string label, Vector2 pos, int count, Func<int> get, Action<int> set, Func<int, string> nameOf)
        {
            if (count <= 0) return;
            UIKit.Label(Panel, label, pos + new Vector2(-260f, 0f), new Vector2(250f, 40f), 22,
                        TextAnchor.MiddleLeft, Art.TextDim);
            var value = UIKit.Label(Panel, nameOf(Mathf.Abs(get()) % count), pos + new Vector2(60f, 0f),
                                    new Vector2(240f, 40f), 22, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            void Step(int delta)
            {
                int v = (Mathf.Abs(get()) + delta + count) % count;
                set(v);
                value.text = nameOf(v);
                GameSettings.Save();
            }

            UIKit.Button(Panel, "◀", pos + new Vector2(-70f, 0f), new Vector2(56f, 48f), () => Step(-1), Art.PanelSoft, 22, 12);
            UIKit.Button(Panel, "▶", pos + new Vector2(190f, 0f), new Vector2(56f, 48f), () => Step(1), Art.PanelSoft, 22, 12);
        }

        private void IntRow(string label, Vector2 pos, int min, int max, Func<int> get, Action<int> set)
        {
            UIKit.Label(Panel, label, pos + new Vector2(-260f, 0f), new Vector2(250f, 40f), 22,
                        TextAnchor.MiddleLeft, Art.TextDim);
            var value = UIKit.Label(Panel, get().ToString(), pos + new Vector2(60f, 0f),
                                    new Vector2(240f, 40f), 22, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            void Step(int delta)
            {
                int v = Mathf.Clamp(get() + delta, min, max);
                set(v);
                value.text = v.ToString();
                GameSettings.Save();
            }

            UIKit.Button(Panel, "−", pos + new Vector2(-70f, 0f), new Vector2(56f, 48f), () => Step(-1), Art.PanelSoft, 24, 12);
            UIKit.Button(Panel, "+", pos + new Vector2(190f, 0f), new Vector2(56f, 48f), () => Step(1), Art.PanelSoft, 24, 12);
        }

        private void FloatRow(string label, Vector2 pos, float min, float max, float step,
                              Func<float> get, Action<float> set, string suffix)
        {
            UIKit.Label(Panel, label, pos + new Vector2(-260f, 0f), new Vector2(250f, 40f), 22,
                        TextAnchor.MiddleLeft, Art.TextDim);
            var value = UIKit.Label(Panel, Format(get(), suffix), pos + new Vector2(60f, 0f),
                                    new Vector2(240f, 40f), 22, TextAnchor.MiddleCenter, Art.TextMain, FontStyle.Bold);

            void Step(float delta)
            {
                float v = Mathf.Clamp(get() + delta, min, max);
                set(v);
                value.text = Format(v, suffix);
                GameSettings.Save();
            }

            UIKit.Button(Panel, "−", pos + new Vector2(-70f, 0f), new Vector2(56f, 48f), () => Step(-step), Art.PanelSoft, 24, 12);
            UIKit.Button(Panel, "+", pos + new Vector2(190f, 0f), new Vector2(56f, 48f), () => Step(step), Art.PanelSoft, 24, 12);
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
