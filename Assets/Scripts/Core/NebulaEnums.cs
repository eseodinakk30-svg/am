// -----------------------------------------------------------------------------
//  NEBULA NINE - shared enumerations.
// -----------------------------------------------------------------------------

namespace Nebula.Core
{
    /// <summary>Secret allegiance handed out at the start of a match.</summary>
    public enum Role
    {
        Crew = 0,
        Infiltrator = 1,
    }

    public enum LifeState
    {
        Alive = 0,
        Murdered = 1,   // ghost, body still on the floor / already reported
        Ejected = 2,    // voted out
        Disconnected = 3,
    }

    /// <summary>High level state machine of a match.</summary>
    public enum MatchPhase
    {
        Lobby = 0,
        RoleReveal = 1,
        Roaming = 2,
        MeetingIntro = 3,
        Discussion = 4,
        Voting = 5,
        VoteResult = 6,
        Ejection = 7,
        GameOver = 8,
    }

    public enum WinSide
    {
        None = 0,
        Crew = 1,
        Infiltrators = 2,
    }

    public enum WinReason
    {
        None = 0,
        TasksComplete = 1,
        AllInfiltratorsEjected = 2,
        InfiltratorsReachedParity = 3,
        SabotageTimeout = 4,
        EveryoneLeft = 5,
    }

    public enum SabotageType
    {
        None = 0,
        Lights = 1,
        Reactor = 2,
        Oxygen = 3,
        Comms = 4,
        Doors = 5,
        Engines = 6,
        Coolant = 7,
    }

    /// <summary>How a sabotage has to be dealt with.</summary>
    public enum SabotageSeverity
    {
        /// <summary>Annoying but not lethal (lights, comms).</summary>
        Systemic = 0,
        /// <summary>Counts down to a loss for the crew (reactor, oxygen, coolant).</summary>
        Critical = 1,
        /// <summary>Instant, no repair needed (doors).</summary>
        Instant = 2,
    }

    public enum TaskKind
    {
        Short = 0,
        Long = 1,
        Visual = 2,
        MultiStage = 3,
        Common = 4,
    }

    public enum Difficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Expert = 3,
        Master = 4,
    }

    public enum QualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
    }

    public enum DeckId
    {
        Upper = 0,
        Lower = 1,
    }

    /// <summary>What an NPC wants to express during a meeting.</summary>
    public enum SpeechIntent
    {
        None = 0,
        Accuse = 1,
        Defend = 2,          // defend yourself
        Vouch = 3,           // defend somebody else
        ClaimAlibi = 4,
        Question = 5,
        Answer = 6,
        DemandEvidence = 7,
        Corroborate = 8,
        Contradict = 9,
        ReportContext = 10,  // "I found the body in X"
        ChangeMind = 11,
        Agree = 12,
        Doubt = 13,
        Deflect = 14,
        TaskClaim = 15,
        SabotageNote = 16,
        VentCall = 17,
        Silence = 18,
        Greeting = 19,
        VoteCall = 20,
        SkipCall = 21,
    }

    /// <summary>Everything an NPC can store in its episodic memory.</summary>
    public enum MemoryKind
    {
        Sighting = 0,          // I saw player P in room R
        SelfLocation = 1,      // I was in room R
        RoomEntry = 2,
        RoomExit = 3,
        TaskObserved = 4,      // P was interacting with a task station
        VisualTaskProof = 5,   // P completed a visually verifiable task
        BodySeen = 6,
        BodyReported = 7,
        KillWitnessed = 8,
        VentWitnessed = 9,
        SabotageStarted = 10,
        SabotageFixed = 11,
        DoorsClosed = 12,
        Accusation = 13,
        Defense = 14,
        AlibiClaim = 15,
        Vote = 16,
        Ejection = 17,
        MeetingCalled = 18,
        LostSight = 19,
        Alone = 20,
        Contradiction = 21,
    }

    public enum NetRole
    {
        Offline = 0,
        Host = 1,
        Client = 2,
    }

    public enum LobbyVisibility
    {
        Private = 0,
        Public = 1,
    }
}
