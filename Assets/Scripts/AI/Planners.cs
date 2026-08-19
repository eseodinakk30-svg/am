// -----------------------------------------------------------------------------
//  NEBULA NINE - utility planners.
//
//  CrewPlanner and ImpostorPlanner score a handful of candidate goals every slow
//  tick and pick the best one for this particular personality.  Nothing is
//  scripted: two agents with the same role in the same situation will often do
//  different things because caution/courage/strategy weigh the options
//  differently.
//
//  The infiltrator's kill evaluation only ever uses information the agent could
//  actually have: players it can currently see, rooms it remembers people going
//  to, and whether the security camera in this room has its light on.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Gameplay;
using Nebula.Map;
using Nebula.Tasks;

namespace Nebula.AI
{
    public enum GoalKind
    {
        Idle,
        DoTask,
        FakeTask,
        GoTo,
        Follow,
        Shadow,
        FixSabotage,
        CheckCameras,
        CheckAdmin,
        Hunt,
        VentEscape,
        Sabotage,
        Regroup,
        Flee,
        CallEmergency,
        ReportBody,
    }

    public struct NpcGoal
    {
        public GoalKind Kind;
        public Vector3 Position;
        public DeckId Deck;
        public int TargetId;
        public int RoomId;
        public TaskInstance Task;
        public float Expiry;
        public int PanelIndex;

        public static NpcGoal Idle => new NpcGoal { Kind = GoalKind.Idle, TargetId = -1, RoomId = -1, PanelIndex = -1 };
    }

    // ==================================================================
    //  crew
    // ==================================================================
    public class CrewPlanner
    {
        private readonly NpcBrain _brain;
        private float _lastCameraCheck = -99f;
        private float _lastAdminCheck = -99f;

        public CrewPlanner(NpcBrain brain) { _brain = brain; }

        public NpcGoal Plan()
        {
            var match = _brain.Match;
            var self = _brain.Owner;
            var p = _brain.Personality;
            if (match == null || self == null) return NpcGoal.Idle;
            float now = match.MatchTime;

            var best = NpcGoal.Idle;
            float bestScore = 0.05f;

            // ---- repair an active sabotage ------------------------------
            var sab = match.Sabotage;
            if (sab != null && sab.IsActive && sab.Panels.Count > 0)
            {
                int panel = ChoosePanel(sab, self);
                if (panel >= 0)
                {
                    float urgency = sab.IsCritical ? 3.2f : 1.15f;
                    // cautious agents rush criticals, brave ones sometimes keep tasking
                    urgency *= Mathf.Lerp(0.75f, 1.35f, p.Caution);
                    float dist = Vector3.Distance(self.Position, sab.Panels[panel].Position);
                    float score = urgency * Mathf.Clamp01(1f - dist / 260f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new NpcGoal
                        {
                            Kind = GoalKind.FixSabotage,
                            Position = sab.Panels[panel].Position,
                            Deck = sab.Panels[panel].Deck,
                            RoomId = sab.Panels[panel].RoomId,
                            TargetId = -1,
                            PanelIndex = panel,
                            Expiry = now + 45f,
                        };
                    }
                }
            }

            // ---- do the next task ---------------------------------------
            var task = PickTask(self);
            if (task != null)
            {
                var pos = match.Tasks.NextStationFor(task);
                var area = StationLayout.Get(task.CurrentRoomId);
                float dist = Vector3.Distance(self.Position, pos);
                float score = 1.05f * Mathf.Clamp01(1f - dist / 300f) + 0.25f;
                // an agent that feels safe focuses on tasks
                score *= Mathf.Lerp(1.25f, 0.8f, _brain.Fear);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new NpcGoal
                    {
                        Kind = GoalKind.DoTask,
                        Position = pos,
                        Deck = area?.Deck ?? self.Deck,
                        RoomId = task.CurrentRoomId,
                        Task = task,
                        TargetId = -1,
                        PanelIndex = -1,
                        Expiry = now + 70f,
                    };
                }
            }

            // ---- stick with somebody -------------------------------------
            int buddy = PickBuddy();
            if (buddy >= 0)
            {
                var other = match.PlayerById(buddy);
                if (other != null)
                {
                    float score = p.GroupPreference * (0.55f + _brain.Fear * 0.9f);
                    if (match.Sabotage != null && match.Sabotage.LightsDown) score += 0.5f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new NpcGoal
                        {
                            Kind = GoalKind.Follow,
                            Position = other.Position,
                            Deck = other.Deck,
                            TargetId = buddy,
                            RoomId = other.RoomId,
                            PanelIndex = -1,
                            Expiry = now + 16f,
                        };
                    }
                }
            }

            // ---- deliberately shadow the prime suspect --------------------
            int suspect = _brain.Suspicion.MostSuspicious(out float suspicion);
            if (suspect >= 0 && suspicion > 0.55f && p.Strategy > 0.55f && p.Courage > 0.4f)
            {
                var target = match.PlayerById(suspect);
                if (target != null && target.IsAlive)
                {
                    float score = 0.75f * suspicion * p.Strategy * p.Courage;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new NpcGoal
                        {
                            Kind = GoalKind.Shadow,
                            Position = target.Position,
                            Deck = target.Deck,
                            TargetId = suspect,
                            RoomId = target.RoomId,
                            PanelIndex = -1,
                            Expiry = now + 22f,
                        };
                    }
                }
            }

            // ---- check the security cameras -------------------------------
            if (now - _lastCameraCheck > 55f && p.Caution + p.Strategy > 0.95f)
            {
                var security = StationLayout.Get("security");
                if (security != null)
                {
                    float score = 0.62f * p.Strategy;
                    if (_brain.Suspicion.Ranked().Count > 0 && suspicion > 0.5f) score += 0.2f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        _lastCameraCheck = now;
                        best = new NpcGoal
                        {
                            Kind = GoalKind.CheckCameras,
                            Position = StationLayout.AreaCenterWorld(security.Id),
                            Deck = security.Deck,
                            RoomId = security.Id,
                            TargetId = -1,
                            PanelIndex = -1,
                            Expiry = now + 30f,
                        };
                    }
                }
            }

            // ---- glance at the admin table --------------------------------
            if (now - _lastAdminCheck > 70f && p.Intelligence > 0.6f)
            {
                var command = StationLayout.Get("command");
                if (command != null)
                {
                    float score = 0.5f * p.Intelligence;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        _lastAdminCheck = now;
                        best = new NpcGoal
                        {
                            Kind = GoalKind.CheckAdmin,
                            Position = StationLayout.AreaCenterWorld(command.Id),
                            Deck = command.Deck,
                            RoomId = command.Id,
                            TargetId = -1,
                            PanelIndex = -1,
                            Expiry = now + 26f,
                        };
                    }
                }
            }

            // ---- nothing better: patrol ----------------------------------
            if (best.Kind == GoalKind.Idle)
            {
                var rooms = StationLayout.RoomsOfDeck(self.Deck);
                if (rooms.Count > 0)
                {
                    var room = rooms[_brain.Rng.NextInt(rooms.Count)];
                    best = new NpcGoal
                    {
                        Kind = GoalKind.GoTo,
                        Position = StationGrid.Instance.RandomPointInArea(room.Id, _brain.Rng),
                        Deck = room.Deck,
                        RoomId = room.Id,
                        TargetId = -1,
                        PanelIndex = -1,
                        Expiry = now + 25f,
                    };
                }
            }

            return best;
        }

        private int ChoosePanel(SabotageSystem sab, PlayerState self)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < sab.Panels.Count; i++)
            {
                if (sab.Panels[i].Done) continue;
                // for two-panel repairs, prefer the one nobody else is heading to
                float dist = Vector3.Distance(self.Position, sab.Panels[i].Position);
                if (sab.Panels[i].HeldBy >= 0 && sab.Panels[i].HeldBy != self.Id) dist += 80f;
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        private TaskInstance PickTask(PlayerState self)
        {
            TaskInstance best = null;
            float bestScore = float.MinValue;
            var match = _brain.Match;
            foreach (var t in self.Tasks)
            {
                if (t.IsComplete) continue;
                var pos = match.Tasks.NextStationFor(t);
                float dist = Vector3.Distance(self.Position, pos);
                var area = StationLayout.Get(t.CurrentRoomId);
                if (area != null && area.Deck != self.Deck) dist += 45f;
                float score = -dist;
                // strategic agents like finishing long tasks early
                if (t.Definition.Kind == TaskKind.MultiStage) score += 22f * _brain.Personality.Strategy;
                if (t.Definition.Visual) score += 30f * _brain.Personality.Strategy; // proves innocence
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        private int PickBuddy()
        {
            var match = _brain.Match;
            var self = _brain.Owner;
            int best = -1;
            float bestScore = 0.1f;
            foreach (int id in _brain.Sense.VisibleNow)
            {
                var other = match.PlayerById(id);
                if (other == null || !other.IsAlive) continue;
                float suspicion = _brain.Suspicion.Probability(id);
                float trust = _brain.Suspicion.Trust(id);
                // prefer people you trust, avoid the ones you fear
                float score = (1f - suspicion) + trust * 0.3f;
                if (suspicion > 0.62f) score -= 1.2f;
                if (score > bestScore) { bestScore = score; best = id; }
            }
            return best;
        }
    }

    // ==================================================================
    //  infiltrator
    // ==================================================================
    public class ImpostorPlanner
    {
        private readonly NpcBrain _brain;
        private float _lastSabotage = -99f;
        private float _nextKillWindowCheck;

        public ImpostorPlanner(NpcBrain brain) { _brain = brain; }

        public NpcGoal Plan()
        {
            var match = _brain.Match;
            var self = _brain.Owner;
            var p = _brain.Personality;
            if (match == null || self == null) return NpcGoal.Idle;
            float now = match.MatchTime;

            var best = NpcGoal.Idle;
            float bestScore = 0.05f;
            float selfHeat = _brain.SelfHeat;

            // ---- sabotage -------------------------------------------------
            if (match.Sabotage != null && match.Sabotage.Cooldown <= 0f && !match.Sabotage.IsActive)
            {
                var choice = ChooseSabotage(out float sabScore);
                if (choice != SabotageType.None)
                {
                    float score = sabScore * Mathf.Lerp(0.7f, 1.4f, p.Strategy);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new NpcGoal
                        {
                            Kind = GoalKind.Sabotage,
                            TargetId = (int)choice,
                            RoomId = -1,
                            PanelIndex = -1,
                            Position = self.Position,
                            Deck = self.Deck,
                            Expiry = now + 6f,
                        };
                    }
                }
            }

            // ---- hunt -----------------------------------------------------
            if (self.KillCooldown <= 4f)
            {
                int prey = ChoosePrey(out float preyScore, out Vector3 preyPos, out DeckId preyDeck);
                if (prey >= 0)
                {
                    float score = preyScore * Mathf.Lerp(0.65f, 1.5f, p.Aggression * 0.5f + p.Courage * 0.5f);
                    score *= Mathf.Lerp(1.25f, 0.55f, selfHeat);   // heat makes them careful
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new NpcGoal
                        {
                            Kind = GoalKind.Hunt,
                            Position = preyPos,
                            Deck = preyDeck,
                            TargetId = prey,
                            RoomId = match.PlayerById(prey)?.RoomId ?? -1,
                            PanelIndex = -1,
                            Expiry = now + 20f,
                        };
                    }
                }
            }

            // ---- build an alibi by hanging around people ------------------
            if (selfHeat > 0.45f || self.KillCooldown > 12f)
            {
                int buddy = ChooseAlibiPartner();
                if (buddy >= 0)
                {
                    var other = match.PlayerById(buddy);
                    float score = (0.55f + selfHeat * 0.8f) * Mathf.Lerp(0.6f, 1.3f, p.Strategy);
                    if (other != null && score > bestScore)
                    {
                        bestScore = score;
                        best = new NpcGoal
                        {
                            Kind = GoalKind.Follow,
                            Position = other.Position,
                            Deck = other.Deck,
                            TargetId = buddy,
                            RoomId = other.RoomId,
                            PanelIndex = -1,
                            Expiry = now + 18f,
                        };
                    }
                }
            }

            // ---- pretend to do a task ------------------------------------
            var fake = PickFakeTask();
            if (fake != null)
            {
                var pos = match.Tasks.NextStationFor(fake);
                float dist = Vector3.Distance(self.Position, pos);
                float score = 0.65f * Mathf.Clamp01(1f - dist / 260f) + selfHeat * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    var area = StationLayout.Get(fake.CurrentRoomId);
                    best = new NpcGoal
                    {
                        Kind = GoalKind.FakeTask,
                        Position = pos,
                        Deck = area?.Deck ?? self.Deck,
                        RoomId = fake.CurrentRoomId,
                        Task = fake,
                        TargetId = -1,
                        PanelIndex = -1,
                        Expiry = now + 30f,
                    };
                }
            }

            // ---- fallback: reposition towards a busy area -----------------
            if (best.Kind == GoalKind.Idle)
            {
                var rooms = StationLayout.RoomsOfDeck(self.Deck);
                var room = rooms[_brain.Rng.NextInt(rooms.Count)];
                best = new NpcGoal
                {
                    Kind = GoalKind.GoTo,
                    Position = StationGrid.Instance.RandomPointInArea(room.Id, _brain.Rng),
                    Deck = room.Deck,
                    RoomId = room.Id,
                    TargetId = -1,
                    PanelIndex = -1,
                    Expiry = now + 22f,
                };
            }

            return best;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// Scores every visible crew member as a kill target.  Only information the
        /// agent could plausibly have is used: who it can see, how far help is, how
        /// close a vent or exit is, and whether the room's camera light is on.
        /// </summary>
        public int ChoosePrey(out float score, out Vector3 position, out DeckId deck)
        {
            var match = _brain.Match;
            var self = _brain.Owner;
            var p = _brain.Personality;
            score = 0f;
            position = self.Position;
            deck = self.Deck;

            int best = -1;
            float bestScore = 0.18f;

            foreach (var target in match.Players)
            {
                if (target.Id == self.Id || !target.IsAlive) continue;
                if (target.Role == Role.Infiltrator) continue;
                if (target.InVent) continue;

                bool visible = match.CanSeePlayer(self, target);
                float distance = Vector3.Distance(self.Position, target.Position);
                if (!visible && distance > 26f) continue;
                if (target.Deck != self.Deck) continue;

                // --- witnesses -------------------------------------------
                int witnesses = 0;
                float nearestWitness = 999f;
                foreach (var w in match.Players)
                {
                    if (w.Id == self.Id || w.Id == target.Id || !w.IsAlive) continue;
                    if (w.Role == Role.Infiltrator) continue;
                    if (!match.CanSeePlayer(self, w)) continue;      // only people I can see count
                    witnesses++;
                    nearestWitness = Mathf.Min(nearestWitness, Vector3.Distance(target.Position, w.Position));
                }

                // people I cannot see but recently placed nearby still worry me
                float ghostRisk = 0f;
                foreach (var w in match.Players)
                {
                    if (w.Id == self.Id || w.Id == target.Id || !w.IsAlive) continue;
                    if (w.Role == Role.Infiltrator) continue;
                    if (match.CanSeePlayer(self, w)) continue;
                    float unseen = _brain.Memory.TimeUnaccountedFor(w.Id, match.MatchTime);
                    int lastRoom = _brain.Memory.LastSeenRoom(w.Id);
                    if (unseen < 12f && lastRoom == target.RoomId) ghostRisk += 0.55f;
                    else if (unseen < 20f && lastRoom >= 0 && StationGrid.Instance != null &&
                             StationGrid.Instance.RoomDistance(lastRoom, target.RoomId) < 25f) ghostRisk += 0.22f;
                }

                if (witnesses > 0) continue;   // never kill in front of somebody you can see

                // --- escape quality --------------------------------------
                var view = StationView.Instance;
                float ventDistance = 999f;
                if (view != null)
                {
                    var vent = view.NearestVent(target.Position, target.Deck, 40f);
                    if (vent != null) ventDistance = Vector3.Distance(target.Position, vent.transform.position);
                }

                // --- camera coverage --------------------------------------
                float cameraRisk = 0f;
                if (view != null)
                {
                    foreach (var cam in view.Cameras)
                    {
                        if (cam.RoomId != target.RoomId) continue;
                        cameraRisk = SecurityCameraUnit.AnyoneWatching ? 1.1f : 0.28f;
                    }
                }

                // --- assemble --------------------------------------------
                float isolation = Mathf.Clamp01(nearestWitness / 30f);
                float proximity = Mathf.Clamp01(1f - distance / 24f);
                float escape = Mathf.Clamp01(1f - ventDistance / 40f);
                float lightsBonus = match.Sabotage != null && match.Sabotage.LightsDown ? 0.45f : 0f;
                float commsBonus = match.Sabotage != null && match.Sabotage.CommsDown ? 0.15f : 0f;

                float s = proximity * 1.1f + isolation * 0.9f + escape * 0.55f + lightsBonus + commsBonus;
                s -= ghostRisk * Mathf.Lerp(0.6f, 1.9f, p.Caution);
                s -= cameraRisk * Mathf.Lerp(0.4f, 1.6f, p.Strategy);
                s -= _brain.Suspicion.Probability(target.Id) * 0.4f;    // do not kill the guy everyone suspects
                s -= _brain.SelfHeat * 0.7f;

                // a strategic infiltrator loves killing where somebody else will be blamed
                int frameCandidate = FrameCandidateNear(target.RoomId);
                if (frameCandidate >= 0) s += 0.5f * p.Strategy;

                if (self.KillCooldown > 0.1f) s -= 0.35f;   // approach but do not commit yet

                if (s > bestScore)
                {
                    bestScore = s;
                    best = target.Id;
                    position = target.Position;
                    deck = target.Deck;
                }
            }

            score = bestScore;
            return best;
        }

        /// <summary>Somebody the agent last saw near this room and could pin the kill on.</summary>
        private int FrameCandidateNear(int roomId)
        {
            var match = _brain.Match;
            var self = _brain.Owner;
            foreach (var pl in match.Players)
            {
                if (pl.Id == self.Id || !pl.IsAlive || pl.Role == Role.Infiltrator) continue;
                int last = _brain.Memory.LastSeenRoom(pl.Id);
                if (last < 0) continue;
                float unseen = _brain.Memory.TimeUnaccountedFor(pl.Id, match.MatchTime);
                if (unseen > 30f) continue;
                if (last == roomId) return pl.Id;
                if (StationGrid.Instance != null && StationGrid.Instance.RoomDistance(last, roomId) < 18f) return pl.Id;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        public SabotageType ChooseSabotage(out float score)
        {
            var match = _brain.Match;
            var self = _brain.Owner;
            var p = _brain.Personality;
            score = 0f;
            var options = match.Sabotage.AvailableSabotages();
            if (options.Count == 0) return SabotageType.None;

            SabotageType best = SabotageType.None;
            float bestScore = 0.22f;
            float taskProgress = match.Tasks != null ? match.Tasks.CrewProgress : 0f;

            foreach (var option in options)
            {
                float s = 0f;
                switch (option)
                {
                    case SabotageType.Lights:
                        // best when people are spread out and I am ready to kill
                        s = 0.85f + (self.KillCooldown < 8f ? 0.55f : 0f);
                        s -= _brain.SelfHeat * 0.2f;
                        break;

                    case SabotageType.Reactor:
                    case SabotageType.Oxygen:
                    case SabotageType.Coolant:
                        // criticals drag everybody to fixed points: great for splitting
                        // the herd, and mandatory when the crew is about to win on tasks
                        s = 0.55f + taskProgress * 1.5f;
                        if (match.AliveCount() <= 5) s += 0.35f;
                        s += p.Strategy * 0.3f;
                        break;

                    case SabotageType.Comms:
                        s = 0.4f + (1f - taskProgress) * 0.35f;
                        break;

                    case SabotageType.Engines:
                        s = 0.35f;
                        break;

                    case SabotageType.Doors:
                        s = 0.3f + (self.KillCooldown < 3f ? 0.4f : 0f);
                        break;
                }

                s *= _brain.Rng.Range(0.85f, 1.15f);
                if (s > bestScore) { bestScore = s; best = option; }
            }

            score = bestScore;
            return best;
        }

        private int ChooseAlibiPartner()
        {
            var match = _brain.Match;
            int best = -1;
            float bestScore = 0.1f;
            foreach (int id in _brain.Sense.VisibleNow)
            {
                var other = match.PlayerById(id);
                if (other == null || !other.IsAlive || other.Role == Role.Infiltrator) continue;
                // an infiltrator wants a credible, talkative witness
                var otherBrain = other.Brain as NpcBrain;
                float credibility = otherBrain != null ? otherBrain.Personality.Sociability : 0.7f;
                float score = credibility + (1f - _brain.Suspicion.Probability(id)) * 0.4f;
                if (score > bestScore) { bestScore = score; best = id; }
            }
            return best;
        }

        private TaskInstance PickFakeTask()
        {
            var self = _brain.Owner;
            var match = _brain.Match;
            TaskInstance best = null;
            float bestDist = float.MaxValue;
            foreach (var t in self.Tasks)
            {
                if (t.IsComplete) continue;
                var pos = match.Tasks.NextStationFor(t);
                float d = Vector3.Distance(self.Position, pos);
                var area = StationLayout.Get(t.CurrentRoomId);
                if (area != null && area.Deck != self.Deck) d += 60f;
                // never fake a visual task in front of witnesses - that is a giveaway
                if (t.Definition.Visual) d += 200f;
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best;
        }
    }
}
