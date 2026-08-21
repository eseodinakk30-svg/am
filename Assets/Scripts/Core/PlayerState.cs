// -----------------------------------------------------------------------------
//  NEBULA NINE - authoritative per participant state.
//
//  One instance exists per seat in the match (human or NPC).  Views (Actor) and
//  brains (NpcBrain) hang off it, never the other way round: the simulation can
//  run head-less for tests and for the dedicated-host case.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Tasks;

namespace Nebula.Core
{
    public class PlayerState
    {
        public int Id;
        public string Name = "";
        public int ColorIndex;
        public int HatIndex;
        public int OutfitIndex;
        public int AccessoryIndex;
        public int TrailIndex;

        public bool IsBot;
        public bool IsLocal;
        public bool Connected = true;

        public Role Role = Role.Crew;
        public SpecialRole Special = SpecialRole.None;

        /// <summary>Кем сейчас выглядит оборотень; -1 — своим собственным обликом.</summary>
        public int DisguisedAs = -1;
        public float ShapeshiftLeft;
        public float ShapeshiftCooldown;
        public float VitalsCharge = 1f;

        // --- следопыт ---
        /// <summary>За кем ведётся метка; -1 — ни за кем.</summary>
        public int TrackedId = -1;
        public float TrackLeft;
        public float TrackCooldown;

        // --- ангел-хранитель ---
        /// <summary>Кого сейчас прикрывает щит; -1 — никого.</summary>
        public int ShieldedId = -1;
        public float ShieldLeft;
        public float ShieldCooldown;
        /// <summary>Щит на самом игроке: убийство по нему срывается.</summary>
        public float ProtectedLeft;

        // --- фантом ---
        public float PhantomLeft;
        public float PhantomCooldown;
        public bool IsPhantomHidden => PhantomLeft > 0f;

        public bool IsImpostor => Role == Role.Infiltrator;
        public bool CanUseVents => Role == Role.Infiltrator || Special == SpecialRole.Engineer;
        public LifeState Life = LifeState.Alive;

        public Vector3 Position;
        public Vector3 Velocity;
        public int RoomId = -1;
        public DeckId Deck = DeckId.Upper;
        public bool InVent;
        public int VentId = -1;

        public float KillCooldown;
        public int EmergenciesUsed;
        public float LastMovedTime;

        public readonly List<TaskInstance> Tasks = new List<TaskInstance>();

        /// <summary>Set when the corpse is on the floor and not yet reported.</summary>
        public bool HasUnreportedBody;
        public Vector3 BodyPosition;
        public int BodyRoomId = -1;
        public DeckId BodyDeck = DeckId.Upper;
        public float DeathTime = -1f;
        public int KilledById = -1;

        /// <summary>Runtime links - may be null on a head-less host.</summary>
        public object View;      // Nebula.Characters.Actor
        public object Brain;     // Nebula.AI.NpcBrain

        public bool IsAlive => Life == LifeState.Alive;
        public bool IsGhost => Life == LifeState.Murdered || Life == LifeState.Ejected;
        public bool IsInfiltrator => Role == Role.Infiltrator;
        public bool CountsForWin => Life == LifeState.Alive;

        public string ColorName => ColorBank.NameOf(ColorIndex);
        public Color Color => ColorBank.Get(ColorIndex);

        /// <summary>Name used in chat / vote UI ("kova_7 (Cobalt)").</summary>
        public string Label => string.IsNullOrEmpty(Name) ? ColorName : Name;

        public int TasksTotal
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Tasks.Count; i++) n += Tasks[i].Definition.Stages;
                return n;
            }
        }

        public int TasksDone
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Tasks.Count; i++) n += Tasks[i].StagesCompleted;
                return n;
            }
        }

        public bool AllTasksDone => TasksTotal > 0 && TasksDone >= TasksTotal;

        public void ResetForNewMatch()
        {
            Role = Role.Crew;
            Special = SpecialRole.None;
            DisguisedAs = -1;
            ShapeshiftLeft = 0f;
            ShapeshiftCooldown = 0f;
            VitalsCharge = 1f;
            TrackedId = -1;
            TrackLeft = 0f;
            TrackCooldown = 0f;
            ShieldedId = -1;
            ShieldLeft = 0f;
            ShieldCooldown = 0f;
            ProtectedLeft = 0f;
            PhantomLeft = 0f;
            PhantomCooldown = 0f;
            Life = LifeState.Alive;
            Tasks.Clear();
            KillCooldown = 0f;
            EmergenciesUsed = 0;
            InVent = false;
            VentId = -1;
            HasUnreportedBody = false;
            BodyRoomId = -1;
            DeathTime = -1f;
            KilledById = -1;
        }
    }
}
