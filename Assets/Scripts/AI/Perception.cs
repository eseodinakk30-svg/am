// -----------------------------------------------------------------------------
//  NEBULA NINE - the perception gate.
//
//  This is the ONLY place where world truth becomes agent knowledge.  Everything
//  is filtered through MatchManager.CanSee (same call the player's own rendering
//  uses) plus the agent's observation trait, so an NPC can never learn a role, a
//  kill or a vent hop it had no way of witnessing.  Missed observations are a
//  feature: a distracted agent genuinely does not see things.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Characters;
using Nebula.Core;
using Nebula.Gameplay;
using Nebula.Tasks;

namespace Nebula.AI
{
    public class Perception
    {
        private readonly NpcBrain _brain;
        private MatchManager _match;
        private float _sightTimer;
        private float _selfTimer;
        private int _lastSelfRoom = -1;
        private readonly Dictionary<int, int> _lastKnownRoom = new Dictionary<int, int>();

        /// <summary>Everyone the agent can see right now (refreshed on the fast tick).</summary>
        public readonly List<int> VisibleNow = new List<int>();

        public Perception(NpcBrain brain)
        {
            _brain = brain;
        }

        public void Bind(MatchManager match) => _match = match;

        /// <summary>
        /// Кого агент ВИДИТ, а не кто это на самом деле. Оборотень в чужом облике
        /// запоминается под именем того, чей облик он принял, — иначе умение было
        /// бы чисто косметическим: NPC всё равно обвиняли бы носителя.
        /// </summary>
        private static int Apparent(PlayerState p)
        {
            if (p == null) return -1;
            return p.DisguisedAs >= 0 ? p.DisguisedAs : p.Id;
        }

        /// <summary>Тот же приём для реакций мозга: он тоже обязан работать с обликом.</summary>
        private PlayerState ApparentPlayer(PlayerState p)
        {
            if (p == null || p.DisguisedAs < 0 || _match == null) return p;
            return _match.PlayerById(p.DisguisedAs) ?? p;
        }

        public void Reset()
        {
            VisibleNow.Clear();
            _lastKnownRoom.Clear();
            _lastSelfRoom = -1;
        }

        // ------------------------------------------------------------------ continuous sensing
        public void Sense(float dt)
        {
            if (_match == null) return;
            var self = _brain.Owner;
            if (self == null || self.IsGhost) return;

            _selfTimer -= dt;
            if (_selfTimer <= 0f)
            {
                _selfTimer = 1.6f;
                if (self.RoomId != _lastSelfRoom)
                {
                    _lastSelfRoom = self.RoomId;
                    _brain.Memory.Add(MemoryKind.SelfLocation, self.Id, -1, self.RoomId, self.Deck, _match.MatchTime);
                }
                else
                {
                    _brain.Memory.Add(MemoryKind.SelfLocation, self.Id, -1, self.RoomId, self.Deck, _match.MatchTime, 0.85f);
                }
            }

            _sightTimer -= dt;
            if (_sightTimer > 0f) return;
            _sightTimer = 0.42f;

            VisibleNow.Clear();
            var personality = _brain.Personality;

            foreach (var other in _match.Players)
            {
                if (other == self || !other.IsAlive || other.InVent) continue;
                if (!_match.CanSeePlayer(self, other)) continue;
                VisibleNow.Add(other.Id);
            }

            // record sightings (with the chance of simply not registering it)
            for (int i = 0; i < VisibleNow.Count; i++)
            {
                int id = VisibleNow[i];
                var other = _match.PlayerById(id);
                if (other == null) continue;
                if (!_brain.Rng.Chance(personality.NoticeChance)) continue;

                int seen = Apparent(other);

                float confidence = Mathf.Clamp01(0.65f + personality.Observation * 0.35f);
                if (_match.Sabotage != null && _match.Sabotage.LightsDown) confidence *= 0.68f;

                _brain.Memory.Add(MemoryKind.Sighting, seen, self.Id, other.RoomId, other.Deck, _match.MatchTime, confidence);

                if (!_lastKnownRoom.TryGetValue(seen, out int prev) || prev != other.RoomId)
                {
                    _lastKnownRoom[seen] = other.RoomId;
                    _brain.Memory.Add(MemoryKind.RoomEntry, seen, self.Id, other.RoomId, other.Deck, _match.MatchTime, confidence);
                }

                // third party pairings: "I saw A together with B"
                for (int j = i + 1; j < VisibleNow.Count; j++)
                {
                    var third = _match.PlayerById(VisibleNow[j]);
                    if (third == null || third.RoomId != other.RoomId) continue;
                    int seenThird = Apparent(third);
                    _brain.Memory.Add(MemoryKind.Sighting, seen, seenThird, other.RoomId, other.Deck, _match.MatchTime, confidence * 0.9f);
                    _brain.Memory.Add(MemoryKind.Sighting, seenThird, seen, third.RoomId, third.Deck, _match.MatchTime, confidence * 0.9f);
                }
            }

            // being alone with exactly one person is a memorable, suspicious fact
            if (VisibleNow.Count == 1)
            {
                var other = _match.PlayerById(VisibleNow[0]);
                if (other != null && other.RoomId == self.RoomId)
                    _brain.Memory.Add(MemoryKind.Alone, Apparent(other), self.Id, self.RoomId, self.Deck, _match.MatchTime, 0.9f);
            }

            // corpses in sight
            foreach (var body in _match.Bodies)
            {
                if (body == null || body.Victim == null) continue;
                if (!_match.CanSee(self, body.transform.position, body.Deck)) continue;
                if (_brain.Memory.CountOfKind(MemoryKind.BodySeen, body.Victim.Id) > 0) continue;
                _brain.Memory.Add(MemoryKind.BodySeen, body.Victim.Id, self.Id, body.RoomId, body.Deck, _match.MatchTime);
                _brain.OnBodySpotted(body.Victim);
            }

            // consoles being used near me (weak evidence - infiltrators fake this)
            foreach (int id in VisibleNow)
            {
                var other = _match.PlayerById(id);
                var actor = other?.View as Actor;
                if (actor == null || actor.Visual == null) continue;
                if (actor.Visual.State != AnimState.Work && actor.Visual.State != AnimState.Repair) continue;
                if (!_brain.Rng.Chance(0.35f)) continue;
                _brain.Memory.Add(MemoryKind.TaskObserved, Apparent(other), self.Id, other.RoomId, other.Deck, _match.MatchTime, 0.75f);
            }
        }

        // ------------------------------------------------------------------ discrete events
        public void OnKill(PlayerState killer, PlayerState victim)
        {
            var self = _brain.Owner;
            if (self == null || killer == null || victim == null) return;
            if (self == killer || self == victim) return;

            bool sawKiller = _match.CanSeePlayer(self, killer);
            bool sawVictim = _match.CanSee(self, victim.BodyPosition, victim.BodyDeck);

            if (sawKiller && sawVictim && _brain.Rng.Chance(0.94f))
            {
                _brain.Memory.Add(MemoryKind.KillWitnessed, Apparent(killer), victim.Id, victim.BodyRoomId, victim.BodyDeck, _match.MatchTime);
                _brain.OnKillWitnessed(ApparentPlayer(killer), victim);
            }
            else if (sawVictim)
            {
                _brain.Memory.Add(MemoryKind.BodySeen, victim.Id, self.Id, victim.BodyRoomId, victim.BodyDeck, _match.MatchTime, 0.9f);
                _brain.OnBodySpotted(victim);
            }
        }

        public void OnVent(PlayerState player, int ventId, bool entering)
        {
            var self = _brain.Owner;
            if (self == null || player == null || player == self) return;
            var view = Map.StationView.Instance;
            if (view == null) return;
            var vent = view.VentById(ventId);
            if (vent == null) return;
            if (!_match.CanSee(self, vent.transform.position, vent.Def.Deck)) return;
            if (!_brain.Rng.Chance(0.92f)) return;

            _brain.Memory.Add(MemoryKind.VentWitnessed, Apparent(player), self.Id, vent.RoomId, vent.Def.Deck, _match.MatchTime,
                1f, entering ? 1 : 0);
            _brain.OnVentWitnessed(ApparentPlayer(player));
        }

        public void OnTaskStage(PlayerState player, TaskInstance task, int stage)
        {
            var self = _brain.Owner;
            if (self == null || player == null || player == self || task == null) return;
            if (!_match.CanSeePlayer(self, player)) return;

            if (task.Definition.Visual && !task.Fake)
            {
                // a genuinely visible effect: strong exoneration
                _brain.Memory.Add(MemoryKind.VisualTaskProof, Apparent(player), self.Id, player.RoomId, player.Deck, _match.MatchTime);
            }
            else
            {
                _brain.Memory.Add(MemoryKind.TaskObserved, Apparent(player), self.Id, player.RoomId, player.Deck, _match.MatchTime, 0.7f);
            }
        }

        public void OnSabotage(SabotageType type, int roomId)
        {
            var self = _brain.Owner;
            if (self == null) return;
            // alarms are station wide: everybody knows a sabotage happened, nobody knows who
            _brain.Memory.Add(MemoryKind.SabotageStarted, -1, -1, roomId, self.Deck, _match.MatchTime, 1f, (int)type);
            _brain.OnSabotageHeard(type, roomId);
        }

        public void OnDoorsClosed(int roomId)
        {
            var self = _brain.Owner;
            if (self == null || self.RoomId != roomId) return;
            _brain.Memory.Add(MemoryKind.DoorsClosed, -1, -1, roomId, self.Deck, _match.MatchTime);
        }

        public void OnBodyReported(PlayerState reporter, PlayerState victim)
        {
            if (reporter == null || victim == null) return;
            _brain.Memory.Add(MemoryKind.BodyReported, reporter.Id, victim.Id, victim.BodyRoomId, victim.BodyDeck, _match.MatchTime);
        }

        public void OnMeetingCalled(PlayerState caller)
        {
            if (caller == null) return;
            _brain.Memory.Add(MemoryKind.MeetingCalled, caller.Id, -1, caller.RoomId, caller.Deck, _match.MatchTime);
        }

        public void OnEjection(PlayerState ejected, bool wasInfiltrator)
        {
            if (ejected == null) return;
            _brain.Memory.Add(MemoryKind.Ejection, ejected.Id, -1, -1, DeckId.Upper, _match.MatchTime, 1f, wasInfiltrator ? 1 : 0);
        }
    }

    /// <summary>
    /// Subscribes once to the ground truth event bus and fans events out to every
    /// brain's perception filter.  Keeping this in one place makes it trivial to
    /// audit that no privileged information leaks into the AI.
    /// </summary>
    public class PerceptionHub : MonoBehaviour
    {
        private MatchManager _match;

        public void Bind(MatchManager match) => _match = match;

        private void OnEnable()
        {
            GameEvents.Killed += OnKilled;
            GameEvents.VentEntered += OnVentIn;
            GameEvents.VentExited += OnVentOut;
            GameEvents.TaskStageDone += OnTask;
            GameEvents.SabotageStarted += OnSabotage;
            GameEvents.DoorsClosed += OnDoors;
            GameEvents.BodyReported += OnReported;
            GameEvents.Ejected += OnEjected;
        }

        private void OnDisable()
        {
            GameEvents.Killed -= OnKilled;
            GameEvents.VentEntered -= OnVentIn;
            GameEvents.VentExited -= OnVentOut;
            GameEvents.TaskStageDone -= OnTask;
            GameEvents.SabotageStarted -= OnSabotage;
            GameEvents.DoorsClosed -= OnDoors;
            GameEvents.BodyReported -= OnReported;
            GameEvents.Ejected -= OnEjected;
        }

        private IEnumerable<NpcBrain> Brains()
        {
            if (_match == null) yield break;
            foreach (var b in _match.Brains) yield return b;
        }

        private void OnKilled(PlayerState killer, PlayerState victim)
        {
            foreach (var b in Brains()) b.Sense.OnKill(killer, victim);
        }

        private void OnVentIn(PlayerState p, int ventId)
        {
            foreach (var b in Brains()) b.Sense.OnVent(p, ventId, true);
        }

        private void OnVentOut(PlayerState p, int ventId)
        {
            foreach (var b in Brains()) b.Sense.OnVent(p, ventId, false);
        }

        private void OnTask(PlayerState p, TaskInstance t, int stage)
        {
            foreach (var b in Brains()) b.Sense.OnTaskStage(p, t, stage);
        }

        private void OnSabotage(SabotageType type, int roomId)
        {
            foreach (var b in Brains()) b.Sense.OnSabotage(type, roomId);
        }

        private void OnDoors(int roomId, float duration)
        {
            foreach (var b in Brains()) b.Sense.OnDoorsClosed(roomId);
        }

        private void OnReported(PlayerState reporter, PlayerState victim)
        {
            foreach (var b in Brains()) b.Sense.OnBodyReported(reporter, victim);
        }

        private void OnEjected(PlayerState ejected, bool wasInf)
        {
            foreach (var b in Brains()) b.Sense.OnEjection(ejected, wasInf);
        }
    }
}
