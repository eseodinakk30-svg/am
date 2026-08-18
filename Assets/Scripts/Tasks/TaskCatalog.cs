// -----------------------------------------------------------------------------
//  NEBULA NINE - the 38 task definitions.
//
//  Kinds:  Common   - every crew member gets the same one
//          Short    - single console, quick
//          Long     - single console, slow / demanding
//          MultiStage - two or three consoles in different rooms
//          Visual   - other players can literally see you doing it
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Fx;

namespace Nebula.Tasks
{
    public static class TaskCatalog
    {
        public static readonly List<TaskDefinition> All = new List<TaskDefinition>();
        private static readonly Dictionary<TaskId, TaskDefinition> ById = new Dictionary<TaskId, TaskDefinition>();

        static TaskCatalog()
        {
            // ---------------------------------------------------------- common
            Add(TaskId.SwipeCard, "Провести пропуск", TaskKind.Common, 5f,
                pool: new[] { "command" },
                create: () => new SwipeCardGame());

            Add(TaskId.ResetBreakers, "Сбросить автоматы", TaskKind.Common, 5.5f,
                pool: new[] { "electrical" },
                create: () => new SwitchBankGame(6, true));

            // ---------------------------------------------------------- short
            Add(TaskId.WireLink, "Соединить провода", TaskKind.Short, 4.5f,
                pool: new[] { "electrical", "cafeteria", "storage", "comms", "quarters", "security", "maintenance" },
                create: () => new WireLinkGame(4));

            Add(TaskId.KeypadCode, "Ввести код доступа", TaskKind.Short, 5f,
                pool: new[] { "command", "security", "airlock" },
                create: () => new KeypadGame(5));

            Add(TaskId.CalibrateAntenna, "Калибровка антенны", TaskKind.Short, 5f,
                pool: new[] { "comms" },
                create: () => new SliderTargetGame(3, 0.05f, Art.Accent, new[] { "Азимут", "Наклон", "Усиление" }));

            Add(TaskId.SortCargo, "Сортировка груза", TaskKind.Short, 6f,
                pool: new[] { "storage", "cargo" },
                create: () => new SortGame(6,
                    new[] { "Топливо", "Провизия", "Запчасти" },
                    new[] { new Color(0.95f, 0.62f, 0.25f), new Color(0.45f, 0.85f, 0.5f), new Color(0.42f, 0.66f, 0.95f) }));

            Add(TaskId.CleanFilter, "Очистка фильтра", TaskKind.Short, 6f,
                pool: new[] { "lifesupport", "water" },
                create: () => new DebrisPurgeGame(7));

            Add(TaskId.AlignTelescope, "Настроить телескоп", TaskKind.Short, 5.5f,
                pool: new[] { "observation" },
                create: () => new CrosshairGame(2.4f, 95f, "Наведи телескоп на объект"));

            Add(TaskId.StabilizePower, "Стабилизация энергии", TaskKind.Short, 6f,
                pool: new[] { "electrical", "reactor" },
                create: () => new PressureRegulateGame(4.5f, "Держи ток в зелёной зоне"));

            Add(TaskId.UnlockManifold, "Открыть коллектор", TaskKind.Short, 5f,
                pool: new[] { "engines", "maintenance" },
                create: () => new ValveGame(3));

            Add(TaskId.RebootTerminal, "Перезапуск терминала", TaskKind.Short, 5f,
                pool: new[] { "servers", "command", "security" },
                create: () => new SequenceGame(3, 2, 4, 1, Art.Accent));

            Add(TaskId.DecryptSignal, "Расшифровать сигнал", TaskKind.Short, 6f,
                pool: new[] { "comms", "servers" },
                create: () => new GridAlignGame());

            Add(TaskId.WeighSamples, "Взвесить образцы", TaskKind.Short, 5f,
                pool: new[] { "lab" },
                create: () => new BalanceGame("Левая чаша", "Правая чаша", 0.04f));

            Add(TaskId.LubricateGears, "Смазать механизм", TaskKind.Short, 5.5f,
                pool: new[] { "maintenance", "engines", "dronebay" },
                create: () => new TracePathGame(8, new Color(0.95f, 0.78f, 0.35f), "Пройди по зубьям шестерни"));

            Add(TaskId.ScanManifest, "Сканировать накладную", TaskKind.Short, 4.5f,
                pool: new[] { "cargo", "storage", "airlock" },
                create: () => new SwipeCardGame());

            Add(TaskId.BalanceCoolant, "Баланс охладителя", TaskKind.Short, 6f,
                pool: new[] { "coolant", "reactor" },
                create: () => new BalanceGame("Контур A", "Контур B", 0.05f));

            Add(TaskId.IceMelt, "Разморозить трубы", TaskKind.Short, 6f,
                pool: new[] { "coolant", "water" },
                create: () => new HoldGaugeGame(0.3f, false, 0f, 0f, "ПРОГРЕВ", new Color(0.95f, 0.5f, 0.25f)));

            Add(TaskId.WaterPlants, "Полить растения", TaskKind.Short, 5.5f,
                pool: new[] { "hydro" },
                create: () => new HoldGaugeGame(0.42f, true, 0.72f, 0.16f, "ПОЛИВ", new Color(0.4f, 0.8f, 0.95f)));

            Add(TaskId.CatalogSamples, "Каталог образцов", TaskKind.Short, 7f,
                pool: new[] { "lab", "hydro" },
                create: () => new MemoryPairsGame(5));

            Add(TaskId.SweepFloor, "Убрать мусор", TaskKind.Short, 6f,
                pool: new[] { "cafeteria", "quarters", "corridor_placeholder" },
                create: () => new DebrisPurgeGame(9));

            Add(TaskId.FixWiringLoom, "Починить жгут", TaskKind.Short, 6f,
                pool: new[] { "maintenance", "servers" },
                create: () => new WireLinkGame(5));

            Add(TaskId.NavigateCourse, "Скорректировать курс", TaskKind.Short, 6f,
                pool: new[] { "command" },
                create: () => new CrosshairGame(3f, 130f, "Удержи станцию на курсе"));

            Add(TaskId.RestartServers, "Перезапустить серверы", TaskKind.Short, 6f,
                pool: new[] { "servers" },
                create: () => new SequenceGame(4, 2, 5, 1, new Color(0.45f, 0.9f, 0.6f)));

            Add(TaskId.InventoryCount, "Пересчитать запасы", TaskKind.Short, 5.5f,
                pool: new[] { "storage", "cargo", "quarters" },
                create: () => new CountGame());

            Add(TaskId.TuneRadio, "Настроить радио", TaskKind.Short, 5.5f,
                pool: new[] { "comms", "observation" },
                create: () => new DialTuneGame("Поймай несущую частоту"));

            Add(TaskId.AlignSolar, "Развернуть панели", TaskKind.Short, 5.5f,
                pool: new[] { "observation", "airlock" },
                create: () => new SliderTargetGame(4, 0.055f, new Color(0.98f, 0.8f, 0.3f),
                    new[] { "П1", "П2", "П3", "П4" }, true));

            // ---------------------------------------------------------- long
            Add(TaskId.ReactorAlign, "Настроить реактор", TaskKind.Long, 11f,
                pool: new[] { "reactor" },
                create: () => new SequenceGame(3, 3, 4, 2, new Color(0.98f, 0.55f, 0.3f)));

            Add(TaskId.RepairHull, "Заварить пробоину", TaskKind.Long, 12f,
                pool: new[] { "airlock", "cargo" },
                create: () => new TracePathGame(13, new Color(0.98f, 0.62f, 0.25f), "Веди сварку по шву"));

            Add(TaskId.ChargeCells, "Зарядить батареи", TaskKind.Long, 11f,
                pool: new[] { "electrical", "dronebay" },
                create: () => new HoldGaugeGame(0.26f, true, 0.86f, 0.1f, "ЗАРЯД", new Color(0.4f, 0.9f, 0.95f)));

            Add(TaskId.CalibrateDistributor, "Калибровка распределителя", TaskKind.Long, 12f,
                pool: new[] { "reactor", "electrical" },
                create: () => new PipeRouteGame());

            // ---------------------------------------------------------- multi stage
            Add(TaskId.FuelEngines, "Заправить двигатели", TaskKind.MultiStage, 8f,
                stages: new[] { "storage", "engines" },
                hints: new[] { "Наполнить канистру", "Залить топливо" },
                create: () => new HoldGaugeGame(0.3f, false, 0f, 0f, "НАСОС", new Color(0.95f, 0.72f, 0.3f)));

            Add(TaskId.DataTransfer, "Перенести данные", TaskKind.MultiStage, 8f,
                stages: new[] { "servers", "command" },
                hints: new[] { "Скачать журналы", "Загрузить журналы" },
                create: () => new TransferGame("Передача данных", 6.5f, Art.Accent));

            Add(TaskId.DivertPower, "Перенаправить питание", TaskKind.MultiStage, 6f,
                stages: new[] { "electrical", "quarters" },
                hints: new[] { "Отвести питание", "Принять питание" },
                create: () => new SwitchBankGame(5, false));

            Add(TaskId.PurgeVents, "Продуть вентиляцию", TaskKind.MultiStage, 8f,
                stages: new[] { "lifesupport", "hydro" },
                hints: new[] { "Открыть заслонку", "Продуть контур" },
                create: () => new ValveGame(4));

            // ---------------------------------------------------------- visual
            Add(TaskId.ScanBiometrics, "Медицинское сканирование", TaskKind.Visual, 9f,
                pool: new[] { "medbay" }, visual: true,
                create: () => new ScanHoldGame(8.5f, "Сканирование", new Color(0.45f, 0.9f, 0.95f)));

            Add(TaskId.PrimeShields, "Активировать щиты", TaskKind.Visual, 6f,
                pool: new[] { "command" }, visual: true,
                create: () => new TapTargetsGame(7, 0f, new Color(0.45f, 0.85f, 0.98f), false, "Секции"));

            Add(TaskId.ClearAsteroids, "Отстрел астероидов", TaskKind.Visual, 8f,
                pool: new[] { "observation" }, visual: true,
                create: () => new TapTargetsGame(12, 190f, new Color(0.75f, 0.62f, 0.5f), true, "Астероиды"));

            Add(TaskId.EmptyGarbage, "Сбросить мусор", TaskKind.Visual, 6f,
                stages: new[] { "cafeteria", "airlock" },
                hints: new[] { "Собрать мусор", "Открыть шлюз" }, visual: true,
                create: () => new HoldGaugeGame(0.36f, false, 0f, 0f, "РЫЧАГ", new Color(0.85f, 0.55f, 0.35f)));
        }

        private static void Add(TaskId id, string title, TaskKind kind, float npcSeconds,
            System.Func<MiniGameBase> create, string[] pool = null, string[] stages = null,
            string[] hints = null, bool visual = false)
        {
            var def = new TaskDefinition
            {
                Id = id,
                Title = title,
                Kind = kind,
                NpcSeconds = npcSeconds,
                RoomPool = SanitisePool(pool),
                StageRooms = stages,
                StageHints = hints,
                Visual = visual,
                Create = create,
            };
            All.Add(def);
            ById[id] = def;
        }

        /// <summary>Drops room keys that do not exist in the layout (keeps the catalogue forgiving).</summary>
        private static string[] SanitisePool(string[] pool)
        {
            if (pool == null) return null;
            var list = new List<string>();
            foreach (var key in pool)
                if (Map.StationLayout.Get(key) != null) list.Add(key);
            if (list.Count == 0) list.Add("cafeteria");
            return list.ToArray();
        }

        public static TaskDefinition Get(TaskId id) => ById.TryGetValue(id, out var d) ? d : null;

        public static List<TaskDefinition> OfKind(TaskKind kind)
        {
            var list = new List<TaskDefinition>();
            foreach (var d in All) if (d.Kind == kind) list.Add(d);
            return list;
        }

        /// <summary>Long pool = Long + MultiStage (both are "long" from the lobby settings' point of view).</summary>
        public static List<TaskDefinition> LongPool()
        {
            var list = new List<TaskDefinition>();
            foreach (var d in All)
                if (d.Kind == TaskKind.Long || d.Kind == TaskKind.MultiStage) list.Add(d);
            return list;
        }

        public static List<TaskDefinition> ShortPool(bool includeVisual)
        {
            var list = new List<TaskDefinition>();
            foreach (var d in All)
            {
                if (d.Kind == TaskKind.Short) list.Add(d);
                else if (includeVisual && d.Kind == TaskKind.Visual) list.Add(d);
            }
            return list;
        }

        public static List<TaskDefinition> CommonPool() => OfKind(TaskKind.Common);
    }
}
