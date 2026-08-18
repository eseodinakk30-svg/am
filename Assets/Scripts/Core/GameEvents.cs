// -----------------------------------------------------------------------------
//  NEBULA NINE - ground truth event bus.
//
//  IMPORTANT DESIGN RULE:
//  These events describe what *actually* happened in the simulation.  NPC brains
//  are NOT allowed to consume them directly as knowledge - they receive them
//  through Perception, which decides whether that particular agent could have
//  seen/heard it (line of sight, distance, lights, being dead, ...).  That is the
//  single place where "AI cheating" would be introduced, so it is deliberately
//  isolated and easy to audit.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Tasks;

namespace Nebula.Core
{
    public static class GameEvents
    {
        // ---- flow ---------------------------------------------------------
        public static event Action<MatchPhase> PhaseChanged;
        public static event Action MatchStarted;
        public static event Action<WinSide, WinReason> GameOver;
        public static event Action<PlayerState> PlayerSpawned;
        public static event Action RosterChanged;

        // ---- combat -------------------------------------------------------
        public static event Action<PlayerState, PlayerState> Killed;              // killer, victim
        public static event Action<PlayerState, PlayerState> BodyReported;        // reporter, victim
        public static event Action<PlayerState, int> VentEntered;                 // player, ventId
        public static event Action<PlayerState, int> VentExited;

        // ---- meetings -----------------------------------------------------
        public static event Action<PlayerState, PlayerState> MeetingStarted;      // caller, victim (null = emergency)
        public static event Action<PlayerState, int> VoteCast;                    // voter, targetId (-1 skip)
        public static event Action<PlayerState, bool> Ejected;                    // ejected (null = skip), wasInfiltrator
        public static event Action MeetingEnded;
        public static event Action<PlayerState, string, bool> Chat;               // speaker, line, ghostChannel

        // ---- systems ------------------------------------------------------
        public static event Action<SabotageType, int> SabotageStarted;            // type, roomId
        public static event Action<SabotageType> SabotageResolved;
        public static event Action<int, float> DoorsClosed;                       // roomId, duration
        public static event Action<float> VisionFactorChanged;
        public static event Action<PlayerState, TaskInstance, int> TaskStageDone; // player, task, stageIndex
        public static event Action<float> TaskProgressChanged;                    // 0..1 crew task bar
        public static event Action<string, float> Announce;                       // toast text, seconds

        // ------------------------------------------------------------------
        public static void RaisePhaseChanged(MatchPhase p) => PhaseChanged?.Invoke(p);
        public static void RaiseMatchStarted() => MatchStarted?.Invoke();
        public static void RaiseGameOver(WinSide s, WinReason r) => GameOver?.Invoke(s, r);
        public static void RaisePlayerSpawned(PlayerState p) => PlayerSpawned?.Invoke(p);
        public static void RaiseRosterChanged() => RosterChanged?.Invoke();

        public static void RaiseKilled(PlayerState killer, PlayerState victim) => Killed?.Invoke(killer, victim);
        public static void RaiseBodyReported(PlayerState reporter, PlayerState victim) => BodyReported?.Invoke(reporter, victim);
        public static void RaiseVentEntered(PlayerState p, int ventId) => VentEntered?.Invoke(p, ventId);
        public static void RaiseVentExited(PlayerState p, int ventId) => VentExited?.Invoke(p, ventId);

        public static void RaiseMeetingStarted(PlayerState caller, PlayerState victim) => MeetingStarted?.Invoke(caller, victim);
        public static void RaiseVoteCast(PlayerState voter, int targetId) => VoteCast?.Invoke(voter, targetId);
        public static void RaiseEjected(PlayerState p, bool wasInf) => Ejected?.Invoke(p, wasInf);
        public static void RaiseMeetingEnded() => MeetingEnded?.Invoke();
        public static void RaiseChat(PlayerState p, string line, bool ghost) => Chat?.Invoke(p, line, ghost);

        public static void RaiseSabotageStarted(SabotageType t, int room) => SabotageStarted?.Invoke(t, room);
        public static void RaiseSabotageResolved(SabotageType t) => SabotageResolved?.Invoke(t);
        public static void RaiseDoorsClosed(int room, float dur) => DoorsClosed?.Invoke(room, dur);
        public static void RaiseVisionFactorChanged(float f) => VisionFactorChanged?.Invoke(f);
        public static void RaiseTaskStageDone(PlayerState p, TaskInstance t, int stage) => TaskStageDone?.Invoke(p, t, stage);
        public static void RaiseTaskProgressChanged(float v) => TaskProgressChanged?.Invoke(v);
        public static void RaiseAnnounce(string text, float seconds = 3f) => Announce?.Invoke(text, seconds);

        /// <summary>Drop every subscriber - called when a match is torn down.</summary>
        public static void ClearAll()
        {
            PhaseChanged = null; MatchStarted = null; GameOver = null;
            PlayerSpawned = null; RosterChanged = null;
            Killed = null; BodyReported = null; VentEntered = null; VentExited = null;
            MeetingStarted = null; VoteCast = null; Ejected = null; MeetingEnded = null; Chat = null;
            SabotageStarted = null; SabotageResolved = null; DoorsClosed = null;
            VisionFactorChanged = null; TaskStageDone = null; TaskProgressChanged = null; Announce = null;
        }
    }

    /// <summary>
    /// A perceivable happening handed to every agent's Perception filter.
    /// Kept as a class (pooled) because agents copy only what they can perceive.
    /// </summary>
    public class WorldEvent
    {
        public MemoryKind Kind;
        public int ActorId = -1;
        public int TargetId = -1;
        public int RoomId = -1;
        public DeckId Deck;
        public Vector3 Position;
        public float Time;
        public string Text;
        public float Magnitude;      // e.g. loudness, used for hearing checks
        public bool GlobalKnowledge; // sabotages / meetings: everyone knows immediately

        public WorldEvent Set(MemoryKind kind, int actor, int target, int room, DeckId deck, Vector3 pos, float time)
        {
            Kind = kind; ActorId = actor; TargetId = target; RoomId = room;
            Deck = deck; Position = pos; Time = time; Text = null; Magnitude = 1f;
            GlobalKnowledge = false;
            return this;
        }
    }

    /// <summary>Tiny free-list so the per-frame perception stream never allocates.</summary>
    public static class WorldEventPool
    {
        private static readonly Stack<WorldEvent> Pool = new Stack<WorldEvent>(64);

        public static WorldEvent Get() => Pool.Count > 0 ? Pool.Pop() : new WorldEvent();

        public static void Release(WorldEvent e)
        {
            if (e == null) return;
            if (Pool.Count < 256) Pool.Push(e);
        }
    }
}
