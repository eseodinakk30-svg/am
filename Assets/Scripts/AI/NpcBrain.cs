// -----------------------------------------------------------------------------
//  NEBULA NINE - the NPC brain.
//
//  Pipeline per agent:
//      sense -> remember -> evaluate -> hypothesise -> plan -> act
//      and in meetings: speak -> listen -> re-evaluate -> vote
//
//  TickFast  (every frame, all agents)  : perception + goal execution
//  TickSlow  (round robin, few/frame)   : memory decay, suspicion re-evaluation,
//                                         re-planning
//
//  The brain never reads Role, Tasks or positions of anybody it has not perceived;
//  all such access goes through Perception / AgentMemory.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Characters;
using Nebula.Core;
using Nebula.Gameplay;
using Nebula.Map;
using Nebula.Tasks;

namespace Nebula.AI
{
    public class NpcBrain
    {
        public PlayerState Owner { get; private set; }
        public MatchManager Match { get; private set; }
        /// <summary>Насколько агенту сейчас не терпится ответить (0..2).</summary>
        public float SpeakUrge => _urgeToSpeak;

        public AgentPersonality Personality { get; private set; }
        public AgentMemory Memory { get; private set; }
        public Perception Sense { get; private set; }
        public SuspicionModel Suspicion { get; private set; }
        public AlibiBoard Alibis { get; private set; }
        public NebulaRandom Rng { get; private set; }
        public NpcNavigator Nav { get; private set; }

        private CrewPlanner _crewPlanner;
        private ImpostorPlanner _impostorPlanner;
        private NpcGoal _goal = NpcGoal.Idle;
        private float _goalTimer;
        private float _workTimer;
        private bool _working;

        /// <summary>0..1 sense of personal danger (crew) - drives grouping.</summary>
        public float Fear { get; private set; }

        /// <summary>0..1 estimate of how suspicious the agent looks to others.</summary>
        public float SelfHeat { get; private set; }

        // --- meeting scratch ---------------------------------------------
        private readonly HashSet<int> _accusedThisMeeting = new HashSet<int>();
        private readonly List<int> _accusersOfMe = new List<int>();
        private int _pendingQuestionFrom = -1;
        private float _urgeToSpeak;          // подскакивает, когда к агенту обратились
        private bool _saidAlibi;
        private bool _saidGreeting;
        private bool _needDefense;
        private AlibiClaim _myClaim;
        private int _mindChangedTo = -1;
        private float _lastSpeechTime;
        private int _committedVote = -2;
        private readonly Dictionary<int, int> _adminSnapshot = new Dictionary<int, int>();
        private float _adminSnapshotTime = -99f;
        private int _pendingReportVictim = -1;
        private float _reportDelay;
        private float _postKillPanic;
        private int _lastMeetingNumber = -1;

        public string PersonalityLabel => Personality != null ? Personality.Describe() : "";

        // ==================================================================
        public void Init(PlayerState owner, MatchManager match, Difficulty difficulty, NebulaRandom rng)
        {
            Owner = owner;
            Match = match;
            Rng = rng;
            Personality = AgentPersonality.Generate(rng, difficulty, owner.Label);
            Memory = new AgentMemory(Personality);
            Sense = new Perception(this);
            Sense.Bind(match);
            Suspicion = new SuspicionModel(this);
            Alibis = new AlibiBoard(this);
            Nav = new NpcNavigator { Wander = Mathf.Lerp(0.05f, 0.22f, 1f - Personality.Caution) };
            _crewPlanner = new CrewPlanner(this);
            _impostorPlanner = new ImpostorPlanner(this);
        }

        public void OnRoundStart()
        {
            _goal = NpcGoal.Idle;
            _goalTimer = 0f;
            _working = false;
            Nav.Stop();
            Sense.Reset();
            _postKillPanic = 0f;
        }

        // ==================================================================
        //  fast tick: perceive + act
        // ==================================================================
        public void TickFast(float dt)
        {
            if (Owner == null || Match == null) return;
            var actor = Owner.View as Actor;
            if (actor == null) return;

            if (Match.Phase != MatchPhase.Roaming)
            {
                actor.Motor.SetInput(Vector2.zero);
                return;
            }

            if (Owner.IsGhost)
            {
                TickGhost(dt, actor);
                return;
            }

            Sense.Sense(dt);

            if (_reportDelay > 0f)
            {
                _reportDelay -= dt;
                if (_reportDelay <= 0f && _pendingReportVictim >= 0)
                {
                    var victim = Match.PlayerById(_pendingReportVictim);
                    if (victim != null && victim.HasUnreportedBody)
                    {
                        // a body is usually spotted from across the room: walk up to it first
                        _goal = new NpcGoal
                        {
                            Kind = GoalKind.ReportBody,
                            Position = victim.BodyPosition,
                            Deck = victim.BodyDeck,
                            TargetId = victim.Id,
                            RoomId = victim.BodyRoomId,
                            PanelIndex = -1,
                            Expiry = Match.MatchTime + 30f,
                        };
                        _goalTimer = 30f;
                        ApplyGoalDestination();
                    }
                    else _pendingReportVictim = -1;
                }
            }

            if (_postKillPanic > 0f) _postKillPanic -= dt;

            ExecuteGoal(dt, actor);
        }

        private void TickGhost(float dt, Actor actor)
        {
            // ghosts keep doing their tasks (crew) or just drift (infiltrators)
            if (Owner.Role == Role.Crew && Match.Settings.GhostsDoTasks)
            {
                var task = NextIncompleteTask();
                if (task != null)
                {
                    var pos = Match.Tasks.NextStationFor(task);
                    var area = StationLayout.Get(task.CurrentRoomId);
                    if (Nav.Arrived || Vector3.Distance(Nav.Destination, pos) > 3f)
                        Nav.SetDestination(pos, area?.Deck ?? Owner.Deck, Owner);

                    if (Vector3.Distance(Owner.Position, pos) < 2.4f)
                    {
                        _workTimer -= dt;
                        if (_workTimer <= 0f)
                        {
                            _workTimer = task.Definition.NpcSeconds;
                            Match.Tasks.CompleteStage(Owner, task, Match.Players);
                        }
                    }
                }
            }
            actor.Motor.SetInput(Nav.Tick(dt, Owner));
        }

        // ==================================================================
        //  slow tick: think
        // ==================================================================
        public void TickSlow(float dt)
        {
            if (Owner == null || Match == null || Owner.IsGhost) return;
            if (Match.Phase != MatchPhase.Roaming) return;

            Memory.Decay(dt);
            Suspicion.Evaluate();
            UpdateEmotion(dt);

            _goalTimer -= dt;
            bool goalDone = _goal.Kind == GoalKind.Idle || _goalTimer <= 0f ||
                            (_goal.Expiry > 0f && Match.MatchTime > _goal.Expiry);

            // a hunt or repair goal is re-evaluated aggressively; a task goal is sticky
            if (_goal.Kind == GoalKind.Hunt || _goal.Kind == GoalKind.FixSabotage || _goal.Kind == GoalKind.Follow)
                goalDone = true;

            // reporting a body always takes priority until it is done or the body is gone
            if (_goal.Kind == GoalKind.ReportBody)
            {
                var pending = Match.PlayerById(_goal.TargetId);
                goalDone = pending == null || !pending.HasUnreportedBody;
            }

            // An agent standing at a console must not be interrupted by the routine
            // re-plan, otherwise the work timer restarts every few seconds and the
            // task never completes.  Only a critical sabotage overrides it.
            bool busy = _working && (_goal.Kind == GoalKind.DoTask || _goal.Kind == GoalKind.FakeTask);
            bool critical = Match.Sabotage != null && Match.Sabotage.IsCritical;
            if (busy && !critical) goalDone = false;

            if (goalDone)
            {
                var previous = _goal;
                _goal = Owner.Role == Role.Infiltrator ? _impostorPlanner.Plan() : _crewPlanner.Plan();
                _goalTimer = _goal.Kind == GoalKind.DoTask || _goal.Kind == GoalKind.FakeTask
                    ? Rng.Range(7f, 11f)
                    : Rng.Range(2.2f, 4.5f);
                if (previous.Task == null || previous.Task != _goal.Task) _working = false;
                ApplyGoalDestination();
            }

            ConsiderEmergencyMeeting();
        }

        private void UpdateEmotion(float dt)
        {
            float targetFear = 0f;
            if (Match.Sabotage != null && Match.Sabotage.LightsDown) targetFear += 0.4f;
            if (Sense.VisibleNow.Count == 0) targetFear += 0.25f;
            if (Memory.CountOfKind(MemoryKind.BodySeen) > 0) targetFear += 0.3f;
            int suspect = Suspicion.MostSuspicious(out float p);
            if (suspect >= 0 && p > 0.6f && Sense.VisibleNow.Contains(suspect)) targetFear += 0.45f;
            targetFear *= Mathf.Lerp(1.35f, 0.45f, Personality.Courage);
            Fear = MathX.DecayTo(Fear, Mathf.Clamp01(targetFear), 0.9f, dt);

            if (Owner.Role == Role.Infiltrator)
            {
                float heat = 0f;
                heat += Mathf.Min(1f, _accusersOfMe.Count * 0.3f);
                heat += Memory.CountOfKind(MemoryKind.BodySeen) > 0 ? 0.15f : 0f;
                heat += _postKillPanic > 0f ? 0.35f : 0f;
                if (Alibis != null) heat += Mathf.Min(0.5f, Alibis.ContradictionStrength(Owner.Id) * 0.4f);
                SelfHeat = MathX.DecayTo(SelfHeat, Mathf.Clamp01(heat), 0.4f, dt);
            }
        }

        private void ConsiderEmergencyMeeting()
        {
            if (Owner.Role != Role.Crew) return;
            if (Owner.EmergenciesUsed >= Match.Settings.EmergencyMeetingsPerPlayer) return;
            if (Match.Sabotage != null && Match.Sabotage.IsCritical) return;

            int suspect = Suspicion.MostSuspicious(out float p);
            bool hardProof = suspect >= 0 && Memory.HasHardEvidence(suspect);
            if (!hardProof) return;
            if (!Rng.Chance(0.5f + Personality.Courage * 0.4f)) return;

            var cafeteria = StationLayout.Get("cafeteria");
            if (cafeteria == null) return;

            if (Owner.RoomId == cafeteria.Id)
            {
                Match.TryEmergency(Owner);
            }
            else
            {
                _goal = new NpcGoal
                {
                    Kind = GoalKind.CallEmergency,
                    Position = StationLayout.AreaCenterWorld(cafeteria.Id),
                    Deck = cafeteria.Deck,
                    RoomId = cafeteria.Id,
                    TargetId = -1,
                    PanelIndex = -1,
                    Expiry = Match.MatchTime + 60f,
                };
                _goalTimer = 12f;
                ApplyGoalDestination();
            }
        }

        // ==================================================================
        //  goal execution
        // ==================================================================
        private void ApplyGoalDestination()
        {
            if (_goal.Kind == GoalKind.Idle || _goal.Kind == GoalKind.Sabotage) return;
            Nav.SetDestination(_goal.Position, _goal.Deck, Owner);
        }

        private void ExecuteGoal(float dt, Actor actor)
        {
            switch (_goal.Kind)
            {
                case GoalKind.Sabotage:
                    if (Match.Sabotage.Trigger((SabotageType)_goal.TargetId, PickDoorRoom()))
                        _goal = NpcGoal.Idle;
                    else _goal = NpcGoal.Idle;
                    actor.Motor.SetInput(Vector2.zero);
                    return;

                case GoalKind.Hunt:
                    ExecuteHunt(dt, actor);
                    return;

                case GoalKind.Follow:
                case GoalKind.Shadow:
                {
                    var target = Match.PlayerById(_goal.TargetId);
                    if (target != null && target.IsAlive)
                    {
                        float keep = _goal.Kind == GoalKind.Shadow ? 6.5f : 3.4f;
                        float d = Vector3.Distance(Owner.Position, target.Position);
                        if (d > keep)
                        {
                            Nav.SetDestination(target.Position, target.Deck, Owner);
                            actor.Motor.SetInput(Nav.Tick(dt, Owner));
                        }
                        else actor.Motor.SetInput(Vector2.zero);
                        return;
                    }
                    _goal = NpcGoal.Idle;
                    break;
                }

                case GoalKind.FixSabotage:
                {
                    var sab = Match.Sabotage;
                    if (sab == null || !sab.IsActive) { _goal = NpcGoal.Idle; break; }
                    if (_goal.PanelIndex < 0 || _goal.PanelIndex >= sab.Panels.Count) { _goal = NpcGoal.Idle; break; }
                    var panel = sab.Panels[_goal.PanelIndex];
                    float d = Vector3.Distance(Owner.Position, panel.Position);
                    if (d < 2.6f)
                    {
                        actor.Motor.SetInput(Vector2.zero);
                        actor.SetActivity(AnimState.Repair, 0.6f);
                        sab.HoldPanel(_goal.PanelIndex, Owner.Id);
                        return;
                    }
                    break;
                }

                case GoalKind.DoTask:
                case GoalKind.FakeTask:
                {
                    var task = _goal.Task;
                    if (task == null || task.IsComplete) { _goal = NpcGoal.Idle; break; }
                    float d = Vector3.Distance(Owner.Position, _goal.Position);
                    if (d < 2.4f)
                    {
                        actor.Motor.SetInput(Vector2.zero);
                        actor.SetActivity(task.Definition.Visual && _goal.Kind == GoalKind.DoTask
                            ? AnimState.Use : AnimState.Work, 0.6f);

                        if (!_working)
                        {
                            _working = true;
                            _workTimer = task.Definition.NpcSeconds * Rng.Range(0.85f, 1.25f);
                        }

                        _workTimer -= dt;
                        if (_workTimer <= 0f)
                        {
                            _working = false;
                            if (_goal.Kind == GoalKind.DoTask)
                            {
                                Match.Tasks.CompleteStage(Owner, task, Match.Players);
                                actor.PlaySound(Audio.Sfx.TaskComplete, 0.4f);
                            }
                            else
                            {
                                // pretending: mark nothing, just walk away like a busy crewmate
                                actor.PlaySound(Audio.Sfx.TaskTick, 0.3f);
                            }
                            _goal = NpcGoal.Idle;
                        }
                        return;
                    }
                    break;
                }

                case GoalKind.CheckCameras:
                {
                    float d = Vector3.Distance(Owner.Position, _goal.Position);
                    if (d < 3.4f)
                    {
                        actor.Motor.SetInput(Vector2.zero);
                        actor.SetActivity(AnimState.Use, 0.6f);
                        WatchCameras(dt);
                        return;
                    }
                    break;
                }

                case GoalKind.CheckAdmin:
                {
                    float d = Vector3.Distance(Owner.Position, _goal.Position);
                    if (d < 3.4f)
                    {
                        actor.Motor.SetInput(Vector2.zero);
                        actor.SetActivity(AnimState.Use, 0.6f);
                        ReadAdminTable();
                        return;
                    }
                    break;
                }

                case GoalKind.CallEmergency:
                {
                    var cafeteria = StationLayout.Get("cafeteria");
                    if (cafeteria != null && Owner.RoomId == cafeteria.Id)
                    {
                        Match.TryEmergency(Owner);
                        _goal = NpcGoal.Idle;
                        actor.Motor.SetInput(Vector2.zero);
                        return;
                    }
                    break;
                }

                case GoalKind.ReportBody:
                {
                    var victim = Match.PlayerById(_goal.TargetId);
                    if (victim == null || !victim.HasUnreportedBody)
                    {
                        _pendingReportVictim = -1;
                        _goal = NpcGoal.Idle;
                        break;
                    }
                    if (Vector3.Distance(Owner.Position, victim.BodyPosition) <= Match.Settings.ReportRange)
                    {
                        actor.Motor.SetInput(Vector2.zero);
                        _pendingReportVictim = -1;
                        _goal = NpcGoal.Idle;
                        Match.TryReport(Owner, victim);
                        return;
                    }
                    break;
                }

                case GoalKind.VentEscape:
                    ExecuteVentEscape(dt, actor);
                    return;
            }

            actor.Motor.SetInput(Nav.Tick(dt, Owner));
            if (Nav.Arrived && _goal.Kind == GoalKind.GoTo) _goal = NpcGoal.Idle;
        }

        private int PickDoorRoom()
        {
            // close doors on the room with the most crew (or on the room I just left)
            int best = -1, bestCount = 0;
            foreach (var area in StationLayout.AllRooms())
            {
                int count = 0;
                foreach (int id in Sense.VisibleNow)
                {
                    var p = Match.PlayerById(id);
                    if (p != null && p.RoomId == area.Id) count++;
                }
                if (count > bestCount) { bestCount = count; best = area.Id; }
            }
            return best >= 0 ? best : Owner.RoomId;
        }

        private void ExecuteHunt(float dt, Actor actor)
        {
            var target = Match.PlayerById(_goal.TargetId);
            if (target == null || !target.IsAlive)
            {
                _goal = NpcGoal.Idle;
                return;
            }

            float d = Vector3.Distance(Owner.Position, target.Position);
            if (d <= Match.Settings.KillRange && Owner.KillCooldown <= 0f)
            {
                // final safety check right before committing
                if (!AnyVisibleWitness(target))
                {
                    if (Match.TryKill(Owner, target))
                    {
                        _postKillPanic = 6f;
                        OnAfterKill(target);
                        return;
                    }
                }
                else
                {
                    // abort: walk away casually
                    _goal = NpcGoal.Idle;
                    return;
                }
            }

            Nav.SetDestination(target.Position, target.Deck, Owner);
            actor.Motor.SetInput(Nav.Tick(dt, Owner));
        }

        /// <summary>
        /// A witness is somebody I can see who ALSO has line of sight to the spot in
        /// question - a crewmate standing behind a wall from my victim is not a risk,
        /// and treating them as one made kills essentially impossible.
        /// </summary>
        private bool AnyVisibleWitness(PlayerState exclude)
        {
            var spot = exclude != null ? exclude.Position : Owner.Position;
            var deck = exclude != null ? exclude.Deck : Owner.Deck;
            foreach (var p in Match.Players)
            {
                if (p.Id == Owner.Id || (exclude != null && p.Id == exclude.Id)) continue;
                if (!p.IsAlive || p.Role == Role.Infiltrator) continue;
                if (!Match.CanSeePlayer(Owner, p)) continue;
                if (Match.CanSee(p, spot, deck)) return true;
            }
            return false;
        }

        private void OnAfterKill(PlayerState victim)
        {
            var view = StationView.Instance;
            if (view == null) return;

            var vent = view.NearestVent(Owner.Position, Owner.Deck, 12f);
            bool wantsVent = vent != null && Rng.Chance(0.35f + Personality.Strategy * 0.45f);

            if (wantsVent)
            {
                _goal = new NpcGoal
                {
                    Kind = GoalKind.VentEscape,
                    Position = vent.transform.position,
                    Deck = vent.Def.Deck,
                    TargetId = vent.Def.Id,
                    RoomId = vent.RoomId,
                    PanelIndex = -1,
                    Expiry = Match.MatchTime + 20f,
                };
                _goalTimer = 12f;
                Nav.SetDestination(vent.transform.position, vent.Def.Deck, Owner);
            }
            else
            {
                // walk away and look busy somewhere else
                var rooms = StationLayout.RoomsOfDeck(Owner.Deck);
                AreaDef far = null;
                float bestDist = 0f;
                foreach (var r in rooms)
                {
                    float dist = StationGrid.Instance.RoomDistance(Owner.RoomId, r.Id);
                    if (dist > bestDist && dist < 90f) { bestDist = dist; far = r; }
                }
                if (far != null)
                {
                    _goal = new NpcGoal
                    {
                        Kind = GoalKind.GoTo,
                        Position = StationGrid.Instance.RandomPointInArea(far.Id, Rng),
                        Deck = far.Deck,
                        RoomId = far.Id,
                        TargetId = -1,
                        PanelIndex = -1,
                        Expiry = Match.MatchTime + 30f,
                    };
                    _goalTimer = 10f;
                    ApplyGoalDestination();
                }
            }
        }

        private void ExecuteVentEscape(float dt, Actor actor)
        {
            var view = StationView.Instance;
            if (view == null) { _goal = NpcGoal.Idle; return; }

            if (!Owner.InVent)
            {
                var vent = view.VentById(_goal.TargetId);
                if (vent == null) { _goal = NpcGoal.Idle; return; }
                if (Vector3.Distance(Owner.Position, vent.transform.position) < 2.2f)
                {
                    if (!AnyVisibleWitness(null)) actor.EnterVent(vent);
                    else _goal = NpcGoal.Idle;
                    return;
                }
                actor.Motor.SetInput(Nav.Tick(dt, Owner));
                return;
            }

            // inside the network: hop away then surface when the coast is clear
            _workTimer -= dt;
            if (_workTimer > 0f) return;
            _workTimer = 1.6f;

            var connected = Match.ConnectedVents(Owner);
            if (connected.Count > 0 && Rng.Chance(0.7f))
            {
                var next = connected[Rng.NextInt(connected.Count)];
                actor.MoveThroughVent(next);
                Owner.RoomId = next.RoomId;
                return;
            }

            if (!AnyVisibleWitness(null))
            {
                var current = view.VentById(Owner.VentId);
                actor.ExitVent(current);
                _goal = NpcGoal.Idle;
            }
        }

        private void WatchCameras(float dt)
        {
            // отмечаемся каждый кадр — признак сам погаснет, когда агент уйдёт
            SecurityCameraUnit.ReportWatching();
            var view = StationView.Instance;
            if (view == null) return;

            _workTimer -= dt;
            if (_workTimer > 0f) return;
            _workTimer = 1.1f;

            foreach (var cam in view.Cameras)
            {
                foreach (var p in Match.Players)
                {
                    if (!p.IsAlive || p.Id == Owner.Id) continue;
                    if (p.IsPhantomHidden) continue;   // фантома не берёт и камера
                    if (p.RoomId != cam.RoomId) continue;
                    // на записи виден облик, а не носитель — как и при личной встрече
                    int seen = p.DisguisedAs >= 0 ? p.DisguisedAs : p.Id;
                    // camera footage is grainy: lower confidence than seeing in person
                    Memory.Add(MemoryKind.Sighting, seen, -1, p.RoomId, p.Deck, Match.MatchTime, 0.6f);
                }
            }

            if (Match.MatchTime - _adminSnapshotTime > 22f) _adminSnapshotTime = Match.MatchTime;
        }

        private void ReadAdminTable()
        {
            // the admin table only reveals HOW MANY people are in each room
            _adminSnapshot.Clear();
            foreach (var p in Match.Players)
            {
                if (!p.IsAlive) continue;
                if (!_adminSnapshot.ContainsKey(p.RoomId)) _adminSnapshot[p.RoomId] = 0;
                _adminSnapshot[p.RoomId]++;
            }
            _adminSnapshotTime = Match.MatchTime;
        }

        private TaskInstance NextIncompleteTask()
        {
            foreach (var t in Owner.Tasks) if (!t.IsComplete) return t;
            return null;
        }

        // ==================================================================
        //  perception callbacks
        // ==================================================================
        public void OnBodySpotted(PlayerState victim)
        {
            if (victim == null || Owner.IsGhost || !Owner.IsAlive) return;
            if (_pendingReportVictim == victim.Id) return;

            bool report;
            if (Owner.Role == Role.Crew)
            {
                report = true;
            }
            else
            {
                // a self report is a tool: use it when it buys credibility
                bool wasSeenNearby = Sense.VisibleNow.Count > 0;
                report = wasSeenNearby || (victim.KilledById != Owner.Id && Rng.Chance(0.4f + Personality.Strategy * 0.3f));
                if (victim.KilledById == Owner.Id && !wasSeenNearby) report = Rng.Chance(0.12f);
            }

            if (!report) return;
            _pendingReportVictim = victim.Id;
            _reportDelay = Rng.Range(0.35f, 1.2f) * Mathf.Lerp(1.5f, 0.6f, Personality.Courage);
        }

        public void OnKillWitnessed(PlayerState killer, PlayerState victim)
        {
            Suspicion.AddTrust(killer.Id, -3f);
            Fear = 1f;
            if (Owner.Role != Role.Crew) return;
            // run for the nearest crowd, then report
            _pendingReportVictim = victim.Id;
            _reportDelay = Rng.Range(0.2f, 0.8f);
        }

        public void OnVentWitnessed(PlayerState player)
        {
            if (player == null) return;
            bool engineersInPlay = Match != null && Match.Settings != null && Match.Settings.EngineerCount > 0;
            Suspicion.AddTrust(player.Id, engineersInPlay ? -0.9f : -2.2f);
        }

        public void OnSabotageHeard(SabotageType type, int roomId)
        {
            // force a re-plan so the agent reacts immediately
            _goalTimer = 0f;
            if (Owner.Role == Role.Crew) Fear = Mathf.Max(Fear, 0.35f);
        }

        // ==================================================================
        //  meetings
        // ==================================================================
        public void OnMeetingStart(MeetingState meeting)
        {
            if (meeting.MeetingNumber != _lastMeetingNumber)
            {
                _lastMeetingNumber = meeting.MeetingNumber;
                Alibis.ClearForMeeting();
            }

            _accusedThisMeeting.Clear();
            _accusersOfMe.Clear();
            _pendingQuestionFrom = -1;
            _urgeToSpeak = 0f;
            _saidAlibi = false;
            _saidGreeting = false;
            _needDefense = false;
            _myClaim = null;
            _mindChangedTo = -1;
            _committedVote = -2;
            _lastSpeechTime = -99f;

            Memory.Decay(0.5f);
            Suspicion.Evaluate();

            if (meeting.Caller != null) Sense.OnMeetingCalled(meeting.Caller);
        }

        public void OnMeetingEnd()
        {
            Suspicion.Evaluate();
            _goal = NpcGoal.Idle;
            _goalTimer = 0f;
            Nav.Stop();
        }

        // ------------------------------------------------------------------ speaking
        public SpeechAct ProposeSpeech(MeetingState meeting)
        {
            if (Owner == null || !Owner.IsAlive) return SpeechAct.None;
            float now = Time.time;

            // К агенту только что обратились — пауза и молчаливость отступают,
            // иначе на прямой вопрос игроку никто не отвечал.
            bool addressed = _urgeToSpeak > 0.9f;

            // собственная пауза: даже самый разговорчивый не строчит очередями
            float pause = 7.5f - Personality.Sociability * 3.2f;
            if (addressed) pause *= 0.3f;
            if (now - _lastSpeechTime < pause) return SpeechAct.None;

            // very quiet personalities often just stay silent
            float silence = Mathf.Clamp01(0.55f - Personality.Sociability * 0.5f);
            if (addressed) silence *= 0.25f;
            if (Rng.Chance(silence)) return SpeechAct.None;

            var act = SpeechAct.None;
            act.AboutTime = LastRelevantTime(meeting);

            // ---- 1. hard evidence outranks everything ---------------------
            int ventSuspect = FindHardEvidence(MemoryKind.VentWitnessed, out int ventRoom);
            if (ventSuspect >= 0 && !_accusedThisMeeting.Contains(ventSuspect))
            {
                act.Intent = SpeechIntent.VentCall;
                act.TargetId = ventSuspect;
                act.RoomId = ventRoom;
                // с инженерами в правилах вентиляция перестаёт быть уликой
                // железной: агент всё равно поднимет тему, но не как приговор
                bool engineersInPlay = Match != null && Match.Settings != null && Match.Settings.EngineerCount > 0;
                act.Priority = engineersInPlay ? 2.6f : 3.4f;
                act.Confidence = engineersInPlay ? 0.72f : 0.98f;
                return act;
            }

            int killSuspect = FindHardEvidence(MemoryKind.KillWitnessed, out int killRoom);
            if (killSuspect >= 0 && !_accusedThisMeeting.Contains(killSuspect))
            {
                act.Intent = SpeechIntent.Accuse;
                act.TargetId = killSuspect;
                act.RoomId = killRoom;
                act.Why = SuspicionModel.ReasonText(EvidenceKind.SawKill, killRoom);
                act.Priority = 3.3f;
                act.Confidence = 0.98f;
                return act;
            }

            // ---- 2. answer a direct question -----------------------------
            if (_pendingQuestionFrom >= 0)
            {
                int asker = _pendingQuestionFrom;
                _pendingQuestionFrom = -1;
                act.Intent = SpeechIntent.Answer;
                act.TargetId = asker;
                var claim = BuildOwnClaim();
                act.RoomId = claim.RoomId;
                act.Room2Id = PickNeighbourRoom(claim.RoomId);
                act.OtherId = claim.CompanionId;
                act.IsLie = claim.KnownLie;
                act.Priority = 2.35f;
                act.Confidence = 0.8f;
                return act;
            }

            // ---- 3. defend yourself --------------------------------------
            if (_needDefense)
            {
                _needDefense = false;
                var claim = BuildOwnClaim();
                bool deflect = Owner.Role == Role.Infiltrator && Personality.Deceit > 0.6f && Rng.Chance(0.42f);
                if (deflect)
                {
                    int scapegoat = PickScapegoat();
                    if (scapegoat >= 0)
                    {
                        act.Intent = SpeechIntent.Deflect;
                        act.TargetId = scapegoat;
                        act.RoomId = Memory.LastSeenRoom(scapegoat);
                        act.Priority = 2.3f;
                        act.Confidence = 0.7f;
                        return act;
                    }
                }
                act.Intent = SpeechIntent.Defend;
                act.TargetId = _accusersOfMe.Count > 0 ? _accusersOfMe[_accusersOfMe.Count - 1] : -1;
                act.RoomId = claim.RoomId;
                act.OtherId = claim.CompanionId;
                act.IsLie = claim.KnownLie;
                act.Priority = 2.25f;
                act.Confidence = 0.85f;
                return act;
            }

            // ---- 4. call out a contradiction -----------------------------
            if (Alibis.TryGetChallenge(out var conflict) && conflict.SuspectId != Owner.Id)
            {
                Alibis.MarkChallenged(conflict.SuspectId);
                act.Intent = SpeechIntent.Contradict;
                act.TargetId = conflict.SuspectId;
                act.RoomId = conflict.ClaimedRoomId;
                act.Room2Id = conflict.RoomId;
                act.Priority = 1.85f * Personality.ContradictionSense;
                act.Confidence = Mathf.Clamp01(conflict.Strength);
                return act;
            }

            // ---- 5. state your alibi -------------------------------------
            if (!_saidAlibi && (Rng.Chance(0.55f + Personality.Sociability * 0.35f) || _accusersOfMe.Count > 0))
            {
                var claim = BuildOwnClaim();
                act.Intent = SpeechIntent.ClaimAlibi;
                act.TargetId = Owner.Id;
                act.RoomId = claim.RoomId;
                act.Room2Id = PickNeighbourRoom(claim.RoomId);
                act.OtherId = claim.CompanionId;
                act.IsLie = claim.KnownLie;
                act.Priority = 1.45f;
                act.Confidence = 0.85f;
                return act;
            }

            // ---- 6. accuse the top suspect -------------------------------
            int suspect = PickAccusationTarget(out float probability, out EvidenceKind reason, out int reasonRoom);
            if (suspect >= 0 && !_accusedThisMeeting.Contains(suspect))
            {
                float threshold = Mathf.Lerp(0.42f, 0.66f, Personality.Caution);
                if (probability > threshold)
                {
                    act.Intent = SpeechIntent.Accuse;
                    act.TargetId = suspect;
                    act.RoomId = reasonRoom;
                    act.Why = SuspicionModel.ReasonText(reason, reasonRoom);
                    act.Priority = 1.15f + probability * 1.1f + Personality.Aggression * 0.6f;
                    act.Confidence = probability;
                    return act;
                }
            }

            // ---- 7. vouch for somebody you can clear ---------------------
            int cleared = FindSomebodyToVouchFor();
            if (cleared >= 0)
            {
                act.Intent = SpeechIntent.Vouch;
                act.TargetId = cleared;
                act.RoomId = Memory.LastSeenRoom(cleared);
                act.Priority = 1.05f + Personality.Trust * 0.4f;
                act.Confidence = 0.8f;
                return act;
            }

            // ---- 8. greeting / question / filler -------------------------
            if (!_saidGreeting && meeting.Chat.Count < 3)
            {
                _saidGreeting = true;
                act.Intent = SpeechIntent.Greeting;
                act.Priority = 0.75f * Personality.Sociability;
                return act;
            }

            int quiet = FindQuietPlayer(meeting);
            if (quiet >= 0 && Rng.Chance(0.4f + Personality.Aggression * 0.3f))
            {
                act.Intent = SpeechIntent.Question;
                act.TargetId = quiet;
                act.RoomId = Memory.LastSeenRoom(quiet);
                act.Priority = 0.85f;
                act.Confidence = 0.5f;
                return act;
            }

            if (Match.Sabotage != null && Memory.CountOfKind(MemoryKind.SabotageStarted) > 0 && Rng.Chance(0.25f))
            {
                act.Intent = SpeechIntent.SabotageNote;
                act.TargetId = -1;
                act.RoomId = Memory.Records.Count > 0 ? Memory.LastSeenRoom(Owner.Id) : -1;
                act.Priority = 0.6f;
                return act;
            }

            if (Rng.Chance(0.35f * Personality.Sociability))
            {
                act.Intent = Rng.Chance(0.5f) ? SpeechIntent.Agree : SpeechIntent.Doubt;
                act.Priority = 0.42f;
                return act;
            }

            return SpeechAct.None;
        }

        public void OnSpoke(SpeechAct act)
        {
            _lastSpeechTime = Time.time;
            _urgeToSpeak = 0f;   // высказался — очередь снова общая

            switch (act.Intent)
            {
                case SpeechIntent.Accuse:
                case SpeechIntent.VentCall:
                case SpeechIntent.Deflect:
                    if (act.TargetId >= 0) _accusedThisMeeting.Add(act.TargetId);
                    break;
                case SpeechIntent.ClaimAlibi:
                case SpeechIntent.Answer:
                case SpeechIntent.Defend:
                    _saidAlibi = true;
                    break;
            }
        }

        // ------------------------------------------------------------------ listening
        public void HearSpeech(PlayerState speaker, SpeechAct act, string text)
        {
            if (speaker == null || Owner == null) return;
            if (!Owner.IsAlive && !Owner.IsGhost) return;

            switch (act.Intent)
            {
                case SpeechIntent.Accuse:
                case SpeechIntent.VentCall:
                case SpeechIntent.Deflect:
                    Memory.RememberAccusation(speaker.Id, act.TargetId, Match.MatchTime);
                    if (act.TargetId == Owner.Id)
                    {
                        if (!_accusersOfMe.Contains(speaker.Id)) _accusersOfMe.Add(speaker.Id);
                        _needDefense = true;
                        Suspicion.AddTrust(speaker.Id, -0.35f);
                    }
                    else
                    {
                        // an accusation from somebody I trust moves me a little
                        float credibility = Mathf.Clamp01(0.35f + Suspicion.Trust(speaker.Id) * 0.2f);
                        var speakerBrain = speaker.Brain as NpcBrain;
                        if (speakerBrain != null) credibility *= Mathf.Lerp(0.6f, 1.4f, speakerBrain.Personality.Persuasion);
                        float move = credibility * Personality.RumourWeight * (act.Intent == SpeechIntent.VentCall ? 2.2f : 0.8f);
                        Suspicion.AddTrust(act.TargetId, -move * 0.6f);
                    }
                    break;

                case SpeechIntent.ClaimAlibi:
                case SpeechIntent.Answer:
                case SpeechIntent.Defend:
                {
                    var claim = new AlibiClaim
                    {
                        SpeakerId = speaker.Id,
                        SubjectId = speaker.Id,
                        RoomId = act.RoomId,
                        CompanionId = act.OtherId,
                        AboutTime = act.AboutTime > 0f ? act.AboutTime : Match.MatchTime,
                        SaidAt = Match.MatchTime,
                        KnownLie = false,
                    };
                    Alibis.Register(claim);
                    break;
                }

                case SpeechIntent.Vouch:
                case SpeechIntent.Corroborate:
                {
                    Memory.RememberDefense(speaker.Id, act.TargetId, Match.MatchTime);
                    var claim = new AlibiClaim
                    {
                        SpeakerId = speaker.Id,
                        SubjectId = act.TargetId,
                        RoomId = act.RoomId,
                        CompanionId = speaker.Id,
                        AboutTime = act.AboutTime > 0f ? act.AboutTime : Match.MatchTime,
                        SaidAt = Match.MatchTime,
                        IsVouch = true,
                    };
                    Alibis.Register(claim);
                    if (act.TargetId == Owner.Id) Suspicion.AddTrust(speaker.Id, 0.5f);
                    break;
                }

                case SpeechIntent.Contradict:
                    if (act.TargetId == Owner.Id)
                    {
                        _needDefense = true;
                        if (!_accusersOfMe.Contains(speaker.Id)) _accusersOfMe.Add(speaker.Id);
                    }
                    break;

                case SpeechIntent.Question:
                    if (act.TargetId == Owner.Id) _pendingQuestionFrom = speaker.Id;
                    break;

                case SpeechIntent.Doubt:
                case SpeechIntent.SkipCall:
                    // Обычная реплика без явного обвинения раньше не давала вообще
                    // ничего — игроку казалось, что он пишет в стену. Теперь она
                    // втягивает агента в разговор. Доверие тут не трогаем: Doubt —
                    // это разбор всего, что не распозналось как обвинение, и любое
                    // нейтральное упоминание цвета роняло бы человеку репутацию.
                    if (act.TargetId == Owner.Id) _pendingQuestionFrom = speaker.Id;
                    break;
            }

            // Кто-то заговорил напрямую с агентом — ему есть что ответить, и
            // он должен захотеть высказаться раньше очереди.
            if (act.TargetId == Owner.Id) _urgeToSpeak = Mathf.Max(_urgeToSpeak, 1.6f);
            else if (speaker.IsLocal) _urgeToSpeak = Mathf.Max(_urgeToSpeak, 0.5f);

            Suspicion.Evaluate();
        }

        // ------------------------------------------------------------------ claims
        private AlibiClaim BuildOwnClaim()
        {
            if (_myClaim != null) return _myClaim;   // stay consistent with yourself

            var claim = new AlibiClaim
            {
                SpeakerId = Owner.Id,
                SubjectId = Owner.Id,
                AboutTime = Match.MatchTime - 18f,
                SaidAt = Match.MatchTime,
            };

            int trueRoom = Memory.MyRoomAt(claim.AboutTime, Owner.Id);
            if (trueRoom < 0) trueRoom = Owner.RoomId;

            int companion = FindCompanionAt(claim.AboutTime);

            bool shouldLie = false;
            if (Owner.Role == Role.Infiltrator)
            {
                // lie only when the truth is dangerous - a good liar mixes truth in
                int crimeRoom = LastCrimeRoom();
                bool truthIsDamning = crimeRoom >= 0 && StationGrid.Instance != null &&
                                      StationGrid.Instance.RoomDistance(trueRoom, crimeRoom) < 20f;
                shouldLie = truthIsDamning || SelfHeat > 0.55f;
                if (shouldLie && !Rng.Chance(Personality.LieQuality * 0.85f + 0.15f))
                    shouldLie = Rng.Chance(0.6f);   // poor liars still lie, just badly
            }

            if (shouldLie)
            {
                claim.RoomId = PickBelievableLieRoom(trueRoom);
                claim.KnownLie = true;
                // a skilled liar names a companion only if that companion cannot deny it
                claim.CompanionId = Personality.LieQuality > 0.7f ? -1 : companion;
            }
            else
            {
                claim.RoomId = trueRoom;
                claim.CompanionId = companion;
            }

            _myClaim = claim;
            return claim;
        }

        private int FindCompanionAt(float time)
        {
            var records = Memory.Records;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var r = records[i];
                if (r.Kind != MemoryKind.Sighting) continue;
                if (Mathf.Abs(r.Time - time) > 14f) continue;
                if (r.SecondaryId != Owner.Id) continue;
                var p = Match.PlayerById(r.SubjectId);
                if (p != null && p.IsAlive) return r.SubjectId;
            }
            return -1;
        }

        private int LastCrimeRoom()
        {
            var records = Memory.Records;
            for (int i = records.Count - 1; i >= 0; i--)
                if (records[i].Kind == MemoryKind.BodySeen || records[i].Kind == MemoryKind.BodyReported)
                    return records[i].RoomId;
            return Match.Meeting.BodyVictim != null ? Match.Meeting.BodyVictim.BodyRoomId : -1;
        }

        /// <summary>
        /// Picks a room to lie about: far from the crime scene, plausible for the
        /// agent's known route, and ideally one nobody can disprove.
        /// </summary>
        private int PickBelievableLieRoom(int trueRoom)
        {
            var rooms = StationLayout.AllRooms();
            int crimeRoom = LastCrimeRoom();
            int best = trueRoom;
            float bestScore = float.MinValue;

            foreach (var room in rooms)
            {
                if (room.Id == crimeRoom) continue;
                float score = 0f;
                var grid = StationGrid.Instance;
                if (grid != null && crimeRoom >= 0) score += Mathf.Min(60f, grid.RoomDistance(room.Id, crimeRoom)) * 0.05f;

                // it must be reachable from where people last saw me
                if (grid != null) score -= Mathf.Min(80f, grid.RoomDistance(room.Id, trueRoom)) * 0.03f;

                // rooms where I actually have a task are much more believable
                foreach (var t in Owner.Tasks)
                    if (!t.IsComplete && t.CurrentRoomId == room.Id) score += 1.4f;

                // avoid rooms where somebody was, if I am a skilled liar
                foreach (var p in Match.Players)
                {
                    if (p.Id == Owner.Id || !p.IsAlive) continue;
                    if (Memory.LastSeenRoom(p.Id) == room.Id)
                        score -= Personality.LieQuality * 1.6f;
                }

                score += Rng.Range(-0.4f, 0.4f) * (1f - Personality.LieQuality);
                if (score > bestScore) { bestScore = score; best = room.Id; }
            }
            return best;
        }

        private int PickNeighbourRoom(int roomId)
        {
            var grid = StationGrid.Instance;
            if (grid == null) return roomId;
            var neighbours = grid.Neighbours(roomId);
            if (neighbours.Count == 0) return roomId;
            return neighbours[Rng.NextInt(neighbours.Count)];
        }

        private float LastRelevantTime(MeetingState meeting)
        {
            if (meeting?.BodyVictim != null && meeting.BodyVictim.DeathTime > 0f) return meeting.BodyVictim.DeathTime;
            return Mathf.Max(0f, Match.MatchTime - 20f);
        }

        // ------------------------------------------------------------------ selection helpers
        private int FindHardEvidence(MemoryKind kind, out int roomId)
        {
            roomId = -1;
            var records = Memory.Records;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].Kind != kind) continue;
                var p = Match.PlayerById(records[i].SubjectId);
                if (p == null || !p.IsAlive) continue;
                roomId = records[i].RoomId;
                return records[i].SubjectId;
            }
            return -1;
        }

        private int PickAccusationTarget(out float probability, out EvidenceKind reason, out int reasonRoom)
        {
            probability = 0f;
            reason = EvidenceKind.None;
            reasonRoom = -1;

            var ranked = Suspicion.Ranked();
            if (ranked.Count == 0) return -1;

            // an infiltrator accuses the crewmate the group is most likely to buy,
            // never a fellow infiltrator (unless that partner is already doomed)
            if (Owner.Role == Role.Infiltrator)
            {
                foreach (var kv in ranked)
                {
                    var p = Match.PlayerById(kv.Key);
                    if (p == null || !p.IsAlive) continue;
                    if (p.Role == Role.Infiltrator)
                    {
                        // betray a partner only when it saves the operation
                        bool partnerDoomed = Memory.TimesAccused(p.Id) >= 3;
                        if (!(partnerDoomed && Personality.Strategy > 0.7f && Rng.Chance(0.35f))) continue;
                    }
                    probability = Mathf.Max(kv.Value.Probability, 0.55f);
                    reason = kv.Value.TopReason != EvidenceKind.None ? kv.Value.TopReason : EvidenceKind.AlwaysAlone;
                    reasonRoom = kv.Value.TopRoom >= 0 ? kv.Value.TopRoom : Memory.LastSeenRoom(p.Id);
                    return kv.Key;
                }
                return -1;
            }

            foreach (var kv in ranked)
            {
                var p = Match.PlayerById(kv.Key);
                if (p == null || !p.IsAlive) continue;
                probability = kv.Value.Probability;
                reason = kv.Value.TopReason;
                reasonRoom = kv.Value.TopRoom;
                return kv.Key;
            }
            return -1;
        }

        private int PickScapegoat()
        {
            var ranked = Suspicion.Ranked();
            foreach (var kv in ranked)
            {
                var p = Match.PlayerById(kv.Key);
                if (p == null || !p.IsAlive) continue;
                if (p.Role == Role.Infiltrator && Owner.Role == Role.Infiltrator) continue;
                if (_accusersOfMe.Contains(kv.Key)) return kv.Key;   // best: turn it on my accuser
            }
            foreach (var kv in ranked)
            {
                var p = Match.PlayerById(kv.Key);
                if (p == null || !p.IsAlive) continue;
                if (p.Role == Role.Infiltrator && Owner.Role == Role.Infiltrator) continue;
                return kv.Key;
            }
            return -1;
        }

        private int FindSomebodyToVouchFor()
        {
            foreach (var p in Match.Players)
            {
                if (p.Id == Owner.Id || !p.IsAlive) continue;
                if (_accusedThisMeeting.Contains(p.Id)) continue;
                if (Memory.SawVisualProof(p.Id)) return p.Id;
                if (Suspicion.Probability(p.Id) < 0.16f && Memory.TimesAccused(p.Id) > 0 && Rng.Chance(0.5f)) return p.Id;
            }
            return -1;
        }

        private int FindQuietPlayer(MeetingState meeting)
        {
            var spoke = new HashSet<int>();
            foreach (var line in meeting.Chat) spoke.Add(line.SpeakerId);
            foreach (var p in Match.Players)
            {
                if (p.Id == Owner.Id || !p.IsAlive) continue;
                if (!spoke.Contains(p.Id)) return p.Id;
            }
            return -1;
        }

        // ------------------------------------------------------------------ voting
        public int DecideVote(Dictionary<int, int> tally)
        {
            if (_committedVote != -2 && Rng.Chance(0.75f)) return _committedVote;

            int decision;
            if (Owner.Role == Role.Infiltrator) decision = DecideVoteAsInfiltrator(tally);
            else decision = DecideVoteAsCrew(tally);

            _committedVote = decision;
            return decision;
        }

        private int DecideVoteAsCrew(Dictionary<int, int> tally)
        {
            int suspect = -1;
            float probability = 0f;
            foreach (var kv in Suspicion.Ranked())
            {
                var p = Match.PlayerById(kv.Key);
                if (p == null || !p.IsAlive) continue;
                suspect = kv.Key;
                probability = kv.Value.Probability;
                break;
            }

            // hard proof: vote no matter what the room thinks
            if (suspect >= 0 && Memory.HasHardEvidence(suspect)) return suspect;

            // bandwagon: follow a forming majority
            int leader = -1, leaderVotes = 0;
            foreach (var kv in tally)
                if (kv.Value > leaderVotes) { leaderVotes = kv.Value; leader = kv.Key; }

            if (leader >= 0 && leaderVotes >= 2 && leader != Owner.Id)
            {
                float pull = Personality.BandwagonBias;
                if (leader >= 0 && Suspicion.Probability(leader) > 0.35f) pull += 0.2f;
                if (Rng.Chance(pull))
                {
                    if (leader != suspect) _mindChangedTo = leader;
                    return leader;
                }
            }

            float threshold = Mathf.Lerp(0.45f, 0.72f, Personality.Caution);
            if (suspect < 0 || probability < threshold)
                return Rng.Chance(0.82f) ? -1 : suspect;    // skip

            // even a Master agent misreads the room sometimes
            if (Rng.Chance(Personality.MistakeChance * 0.55f))
            {
                var ranked = Suspicion.Ranked();
                if (ranked.Count > 1)
                {
                    var alt = ranked[Mathf.Min(ranked.Count - 1, 1 + Rng.NextInt(2))];
                    var p = Match.PlayerById(alt.Key);
                    if (p != null && p.IsAlive) return alt.Key;
                }
            }

            return suspect;
        }

        private int DecideVoteAsInfiltrator(Dictionary<int, int> tally)
        {
            int leader = -1, leaderVotes = 0;
            foreach (var kv in tally)
                if (kv.Value > leaderVotes) { leaderVotes = kv.Value; leader = kv.Key; }

            var leaderPlayer = Match.PlayerById(leader);
            bool leaderIsPartner = leaderPlayer != null && leaderPlayer.Role == Role.Infiltrator;
            bool leaderIsMe = leader == Owner.Id;

            int aliveCrew = 0, aliveInf = 0;
            foreach (var p in Match.Players)
            {
                if (!p.IsAlive) continue;
                if (p.Role == Role.Infiltrator) aliveInf++;
                else aliveCrew++;
            }

            // being the vote leader is fatal: push hard onto somebody else
            if (leaderIsMe || _accusersOfMe.Count >= 2)
            {
                int scapegoat = PickScapegoat();
                if (scapegoat >= 0) return scapegoat;
            }

            // a partner about to be ejected: sell them out to buy trust, but only if
            // the operation survives it
            if (leaderIsPartner && leaderVotes >= Mathf.CeilToInt(aliveCrew * 0.5f))
            {
                bool survivable = aliveInf > 1 || aliveCrew <= 3;
                if (survivable && Personality.Strategy > 0.6f && Rng.Chance(0.55f)) return leader;
                return -1;   // otherwise quietly skip
            }

            // ride the wave onto a crewmate
            if (leader >= 0 && !leaderIsPartner && !leaderIsMe && leaderVotes >= 1) return leader;

            // no wave yet: pick the crewmate that is most dangerous to us
            int best = -1;
            float bestScore = 0f;
            foreach (var p in Match.Players)
            {
                if (!p.IsAlive || p.Role == Role.Infiltrator || p.Id == Owner.Id) continue;
                var brain = p.Brain as NpcBrain;
                float threat = brain != null
                    ? brain.Personality.Intelligence * 0.5f + brain.Personality.Persuasion * 0.5f
                    : 0.75f;                                   // treat the human as dangerous
                if (_accusersOfMe.Contains(p.Id)) threat += 0.6f;
                threat += Suspicion.Probability(p.Id) * 0.3f;
                if (threat > bestScore) { bestScore = threat; best = p.Id; }
            }

            return Rng.Chance(0.75f) ? best : -1;
        }
    }
}
