// -----------------------------------------------------------------------------
//  NEBULA NINE - task data model.
//
//  A TaskDefinition is static data (title, kind, which rooms, how long an NPC
//  needs).  A TaskInstance is the per-player runtime copy that tracks progress
//  through the stages.  Mini-games are pure UI objects built on demand; NPCs run
//  the same definitions without ever instantiating one.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;
using Nebula.Core;

namespace Nebula.Tasks
{
    public enum TaskId
    {
        WireLink, KeypadCode, CalibrateAntenna, SortCargo, CleanFilter, AlignTelescope,
        StabilizePower, UnlockManifold, RebootTerminal, DecryptSignal, WeighSamples,
        LubricateGears, ScanManifest, BalanceCoolant, IceMelt, WaterPlants, CatalogSamples,
        SweepFloor, FixWiringLoom, NavigateCourse, RestartServers, InventoryCount, TuneRadio,
        AlignSolar, FuelEngines, DataTransfer, DivertPower, ReactorAlign, RepairHull,
        ChargeCells, PurgeVents, CalibrateDistributor, ScanBiometrics, PrimeShields,
        EmptyGarbage, ClearAsteroids, SwipeCard, ResetBreakers,
    }

    public class TaskDefinition
    {
        public TaskId Id;
        public string Title;
        public TaskKind Kind;
        public bool Visual;
        /// <summary>Seconds an NPC spends at the console per stage (humans play the mini-game).</summary>
        public float NpcSeconds = 4.5f;
        /// <summary>Candidate rooms for a single stage task (one is picked per player).</summary>
        public string[] RoomPool;
        /// <summary>Explicit room order for a multi-stage task.</summary>
        public string[] StageRooms;
        public string[] StageHints;
        public Func<MiniGameBase> Create;

        public int Stages => StageRooms != null && StageRooms.Length > 0 ? StageRooms.Length : 1;
        public bool IsMultiStage => Stages > 1;
    }

    public class TaskInstance
    {
        public TaskDefinition Definition;
        public int StagesCompleted;
        public int[] StageRoomIds;
        public bool Fake;    // an infiltrator "doing" this task never actually completes it

        public bool IsComplete => StagesCompleted >= Definition.Stages;

        public int CurrentRoomId =>
            IsComplete ? StageRoomIds[StageRoomIds.Length - 1]
                       : StageRoomIds[Mathf.Clamp(StagesCompleted, 0, StageRoomIds.Length - 1)];

        public string CurrentHint
        {
            get
            {
                if (Definition.StageHints == null || Definition.StageHints.Length == 0) return Definition.Title;
                int i = Mathf.Clamp(StagesCompleted, 0, Definition.StageHints.Length - 1);
                return Definition.StageHints[i];
            }
        }

        public string DisplayLine
        {
            get
            {
                string room = Map.StationLayout.NameOf(CurrentRoomId);
                string label = Definition.IsMultiStage
                    ? $"{Definition.Title} ({StagesCompleted}/{Definition.Stages})"
                    : Definition.Title;
                return IsComplete ? $"<color=#5ED27E>{label}</color>" : $"{room}: {label}";
            }
        }
    }

    /// <summary>Contract every mini-game implements.</summary>
    public abstract class MiniGameBase
    {
        protected RectTransform Root;
        protected TaskInstance Task;
        protected int Stage;
        protected NebulaRandom Rng;
        private Action _onComplete;
        private float _completeDelay = -1f;

        public bool IsDone { get; private set; }
        public virtual string Instruction => "";

        public void Setup(RectTransform root, TaskInstance task, int stage, NebulaRandom rng, Action onComplete)
        {
            Root = root;
            Task = task;
            Stage = stage;
            Rng = rng;
            _onComplete = onComplete;
            Build();
        }

        protected abstract void Build();

        public virtual void Tick(float dt)
        {
            if (_completeDelay > 0f)
            {
                _completeDelay -= dt;
                if (_completeDelay <= 0f)
                {
                    _completeDelay = -1f;
                    _onComplete?.Invoke();
                }
            }
        }

        /// <summary>Call when the puzzle is solved; the window closes after a short beat.</summary>
        protected void Complete(float delay = 0.65f)
        {
            if (IsDone) return;
            IsDone = true;
            Audio.SoundBank.Play(Audio.Sfx.TaskComplete, 0.8f);
            _completeDelay = delay;
        }

        protected void Progress()
        {
            Audio.SoundBank.Play(Audio.Sfx.TaskTick, 0.5f);
        }

        protected void Fail()
        {
            Audio.SoundBank.Play(Audio.Sfx.UiError, 0.6f);
        }

        public virtual void Dispose() { }
    }
}
