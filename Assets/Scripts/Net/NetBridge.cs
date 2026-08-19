// -----------------------------------------------------------------------------
//  NEBULA NINE - gameplay <-> network glue.
//
//  Host: watches the local (authoritative) MatchManager, mirrors everything to the
//  peers and validates their requests.  A client can only ever *ask* to kill,
//  report, vote or complete a task; the host applies the same rule checks it uses
//  for its own player, which is what keeps the match cheat resistant.
//
//  Client: keeps a mirrored MatchManager driven purely by host messages, moves its
//  own character locally for responsiveness and streams its position up.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Characters;
using Nebula.Core;
using Nebula.Gameplay;
using Nebula.Map;
using Nebula.Tasks;

namespace Nebula.Net
{
    public class NetBridge : MonoBehaviour
    {
        public static NetBridge Instance { get; private set; }

        private NetworkService _net;
        private MatchManager _match;
        private float _snapshotTimer;
        private float _inputTimer;
        private readonly Dictionary<int, Vector3> _remoteTargets = new Dictionary<int, Vector3>();

        public bool Active => _net != null && _net.Role != NetRole.Offline;
        public bool IsHost => _net != null && _net.Role == NetRole.Host;

        public System.Action<MatchSettings, List<PlayerState>, int> OnRemoteRoster;

        public void Bind(NetworkService net, MatchManager match)
        {
            Instance = this;
            _net = net;
            _match = match;

            _net.OnMessage += HandleMessage;
            _net.OnPeerJoined += OnPeerJoined;
            _net.OnPeerLost += OnPeerLost;

            GameEvents.PhaseChanged += OnPhaseChanged;
            GameEvents.Killed += OnKilled;
            GameEvents.MeetingStarted += OnMeetingStarted;
            GameEvents.VoteCast += OnVoteCast;
            GameEvents.Ejected += OnEjected;
            GameEvents.TaskStageDone += OnTaskDone;
            GameEvents.SabotageStarted += OnSabotageStarted;
            GameEvents.SabotageResolved += OnSabotageResolved;
            GameEvents.Chat += OnChat;
            GameEvents.GameOver += OnGameOver;
            GameEvents.VentEntered += OnVentEntered;
            GameEvents.VentExited += OnVentExited;
        }

        private void OnDestroy()
        {
            if (_net != null)
            {
                _net.OnMessage -= HandleMessage;
                _net.OnPeerJoined -= OnPeerJoined;
                _net.OnPeerLost -= OnPeerLost;
            }
            GameEvents.PhaseChanged -= OnPhaseChanged;
            GameEvents.Killed -= OnKilled;
            GameEvents.MeetingStarted -= OnMeetingStarted;
            GameEvents.VoteCast -= OnVoteCast;
            GameEvents.Ejected -= OnEjected;
            GameEvents.TaskStageDone -= OnTaskDone;
            GameEvents.SabotageStarted -= OnSabotageStarted;
            GameEvents.SabotageResolved -= OnSabotageResolved;
            GameEvents.Chat -= OnChat;
            GameEvents.GameOver -= OnGameOver;
            GameEvents.VentEntered -= OnVentEntered;
            GameEvents.VentExited -= OnVentExited;
        }

        // ==================================================================
        //  host: outgoing state
        // ==================================================================
        private void Update()
        {
            if (!Active || _match == null || _match.Players.Count == 0) return;

            if (IsHost)
            {
                _snapshotTimer -= Time.deltaTime;
                if (_snapshotTimer <= 0f)
                {
                    _snapshotTimer = 1f / 15f;
                    SendSnapshot();
                }
            }
            else
            {
                _inputTimer -= Time.deltaTime;
                if (_inputTimer <= 0f)
                {
                    _inputTimer = 1f / 15f;
                    SendInput();
                }
                InterpolateRemotes();
            }
        }

        private void SendSnapshot()
        {
            var w = new NetWriter(NetMsg.Snapshot);
            w.Int(_match.Players.Count);
            w.Float(_match.MatchTime);
            foreach (var p in _match.Players)
            {
                w.Int(p.Id);
                w.Vec(p.Position);
                w.Byte((byte)p.Life);
                w.Byte((byte)p.Deck);
                w.Bool(p.InVent);
                w.Int(p.RoomId);
            }
            _net.SendToAll(w);
        }

        private void SendInput()
        {
            var local = _match.Local;
            if (local == null) return;
            var w = new NetWriter(NetMsg.InputState);
            w.Str(_net.LocalToken);
            w.Vec(local.Position);
            w.Byte((byte)local.Deck);
            int panel = PlayerController.Instance != null ? PlayerController.Instance.HeldPanelIndex : -1;
            w.Byte((byte)Mathf.Clamp(panel + 1, 0, 255));
            _net.SendToHost(w);
        }

        private void InterpolateRemotes()
        {
            foreach (var kv in _remoteTargets)
            {
                var player = _match.PlayerById(kv.Key);
                if (player == null || player.IsLocal) continue;
                var actor = player.View as Actor;
                if (actor == null) continue;
                var current = actor.transform.position;
                var target = kv.Value;
                if ((current - target).sqrMagnitude > 64f) actor.Motor.Teleport(target);
                else actor.transform.position = Vector3.Lerp(current, target, Time.deltaTime * 12f);
            }
        }

        public void SendRoster()
        {
            if (!IsHost) return;
            var s = _match.Settings;
            var w = new NetWriter(NetMsg.Roster, _net.NextSequence());
            w.Int(s.PlayerCount).Int(s.InfiltratorCount).Float(s.MoveSpeed).Float(s.CrewVision)
             .Float(s.InfiltratorVision).Float(s.KillCooldown).Float(s.KillRange)
             .Float(s.DiscussionTime).Float(s.VotingTime).Int(s.CommonTasks).Int(s.LongTasks)
             .Int(s.ShortTasks).Bool(s.ConfirmEjects).Bool(s.AnonymousVotes);

            w.Int(_match.Players.Count);
            foreach (var p in _match.Players)
            {
                w.Int(p.Id).Str(p.Name).Int(p.ColorIndex).Int(p.HatIndex).Int(p.OutfitIndex)
                 .Int(p.AccessoryIndex).Int(p.TrailIndex).Bool(p.IsBot);
            }
            _net.SendToAll(w, true);
        }

        // ---- event mirroring ------------------------------------------------
        private void OnPhaseChanged(MatchPhase phase)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.PhaseChange, _net.NextSequence())
                .Byte((byte)phase).Float(_match.PhaseTimer), true);
        }

        private void OnKilled(PlayerState killer, PlayerState victim)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventKill, _net.NextSequence())
                .Int(killer.Id).Int(victim.Id).Vec(victim.BodyPosition).Int(victim.BodyRoomId), true);
        }

        private void OnMeetingStarted(PlayerState caller, PlayerState victim)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventMeeting, _net.NextSequence())
                .Int(caller?.Id ?? -1).Int(victim?.Id ?? -1).Bool(victim == null), true);
        }

        private void OnVoteCast(PlayerState voter, int targetId)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventVote, _net.NextSequence())
                .Int(voter.Id).Int(targetId), true);
        }

        private void OnEjected(PlayerState player, bool wasInfiltrator)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventEject, _net.NextSequence())
                .Int(player?.Id ?? -1).Bool(wasInfiltrator), true);
        }

        private void OnTaskDone(PlayerState player, TaskInstance task, int stage)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventTask, _net.NextSequence())
                .Int(player.Id).Int((int)task.Definition.Id).Int(stage)
                .Float(_match.Tasks.CrewProgress), true);
        }

        private void OnSabotageStarted(SabotageType type, int roomId)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventSabotage, _net.NextSequence())
                .Byte((byte)type).Int(roomId), true);
        }

        private void OnSabotageResolved(SabotageType type)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventSabotageEnd, _net.NextSequence()).Byte((byte)type), true);
        }

        private void OnChat(PlayerState speaker, string line, bool ghost)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventChat, _net.NextSequence())
                .Int(speaker?.Id ?? -1).Str(line).Bool(ghost), true);
        }

        private void OnGameOver(WinSide side, WinReason reason)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventGameOver, _net.NextSequence())
                .Byte((byte)side).Byte((byte)reason), true);
        }

        private void OnVentEntered(PlayerState p, int ventId)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventVent, _net.NextSequence()).Int(p.Id).Int(ventId).Bool(true), true);
        }

        private void OnVentExited(PlayerState p, int ventId)
        {
            if (!IsHost) return;
            _net.SendToAll(new NetWriter(NetMsg.EventVent, _net.NextSequence()).Int(p.Id).Int(ventId).Bool(false), true);
        }

        // ==================================================================
        //  incoming
        // ==================================================================
        private void HandleMessage(NetReader reader, string endpoint)
        {
            if (_match == null) return;

            if (IsHost) HandleHostMessage(reader, endpoint);
            else HandleClientMessage(reader);
        }

        private void HandleHostMessage(NetReader r, string endpoint)
        {
            var peer = _net.PeerOf(endpoint);
            var player = peer != null ? _match.PlayerById(peer.PlayerId) : null;

            switch (r.Type)
            {
                case NetMsg.InputState:
                {
                    r.Str();
                    var pos = r.Vec();
                    r.Byte();
                    int heldPanel = r.Byte() - 1;
                    if (player == null || !player.IsAlive) return;
                    if (heldPanel >= 0) _match.Sabotage?.HoldPanel(heldPanel, player.Id);
                    var actor = player.View as Actor;
                    if (actor == null) return;
                    // anti-cheat: reject teleports beyond what the speed allows
                    float maxStep = _match.Settings.MoveSpeed * 0.5f + 2.5f;
                    if ((pos - player.Position).sqrMagnitude > maxStep * maxStep) return;
                    actor.Motor.Teleport(pos);
                    return;
                }

                case NetMsg.RequestKill:
                {
                    int victimId = r.Int();
                    var victim = _match.PlayerById(victimId);
                    if (player != null && victim != null) _match.TryKill(player, victim);
                    return;
                }

                case NetMsg.RequestReport:
                {
                    int victimId = r.Int();
                    var victim = _match.PlayerById(victimId);
                    if (player != null && victim != null) _match.TryReport(player, victim);
                    return;
                }

                case NetMsg.RequestEmergency:
                    if (player != null) _match.TryEmergency(player);
                    return;

                case NetMsg.RequestTaskStage:
                {
                    int taskId = r.Int();
                    if (player == null || player.Role == Role.Infiltrator) return;
                    foreach (var t in player.Tasks)
                    {
                        if ((int)t.Definition.Id != taskId || t.IsComplete) continue;
                        // the host verifies the player is actually standing at that console
                        var pos = _match.Tasks.NextStationFor(t);
                        if ((pos - player.Position).sqrMagnitude > 25f) return;
                        _match.Tasks.CompleteStage(player, t, _match.Players);
                        _match.CheckWinConditions();
                        return;
                    }
                    return;
                }

                case NetMsg.RequestVote:
                {
                    int target = r.Int();
                    if (player != null) _match.CastVote(player, target);
                    return;
                }

                case NetMsg.RequestSabotage:
                {
                    var type = (SabotageType)r.Byte();
                    int roomId = r.Int();
                    if (player != null && player.Role == Role.Infiltrator && player.IsAlive)
                        _match.Sabotage.Trigger(type, roomId);
                    return;
                }

                case NetMsg.RequestVent:
                    if (player != null) _match.TryUseVent(player, out _);
                    return;

                case NetMsg.RequestChat:
                {
                    string text = r.Str();
                    if (player == null || string.IsNullOrWhiteSpace(text)) return;
                    if (text.Length > 120) text = text.Substring(0, 120);
                    _match.AddChat(player, text, SpeechIntent.None, player.IsGhost);
                    if (!player.IsGhost) _match.Debate?.OnPlayerChat(player, text);
                    return;
                }
            }
        }

        private void HandleClientMessage(NetReader r)
        {
            switch (r.Type)
            {
                case NetMsg.Roster:
                {
                    var settings = new MatchSettings
                    {
                        PlayerCount = r.Int(),
                        InfiltratorCount = r.Int(),
                        MoveSpeed = r.Float(),
                        CrewVision = r.Float(),
                        InfiltratorVision = r.Float(),
                        KillCooldown = r.Float(),
                        KillRange = r.Float(),
                        DiscussionTime = r.Float(),
                        VotingTime = r.Float(),
                        CommonTasks = r.Int(),
                        LongTasks = r.Int(),
                        ShortTasks = r.Int(),
                        ConfirmEjects = r.Bool(),
                        AnonymousVotes = r.Bool(),
                    };

                    int count = r.Int();
                    var roster = new List<PlayerState>();
                    for (int i = 0; i < count; i++)
                    {
                        var p = new PlayerState
                        {
                            Id = r.Int(),
                            Name = r.Str(),
                            ColorIndex = r.Int(),
                            HatIndex = r.Int(),
                            OutfitIndex = r.Int(),
                            AccessoryIndex = r.Int(),
                            TrailIndex = r.Int(),
                            IsBot = r.Bool(),
                        };
                        roster.Add(p);
                    }
                    OnRemoteRoster?.Invoke(settings, roster, LocalIdFromRoster(roster));
                    return;
                }

                case NetMsg.Snapshot:
                {
                    int count = r.Int();
                    r.Float();
                    for (int i = 0; i < count; i++)
                    {
                        int id = r.Int();
                        var pos = r.Vec();
                        var life = (LifeState)r.Byte();
                        var deck = (DeckId)r.Byte();
                        bool inVent = r.Bool();
                        int roomId = r.Int();

                        var p = _match.PlayerById(id);
                        if (p == null) continue;
                        p.Life = life;
                        p.Deck = deck;
                        p.InVent = inVent;
                        p.RoomId = roomId;
                        if (!p.IsLocal) _remoteTargets[id] = pos;
                    }
                    return;
                }

                case NetMsg.PhaseChange:
                {
                    var phase = (MatchPhase)r.Byte();
                    float timer = r.Float();
                    _match.ApplyRemotePhase(phase, timer);
                    return;
                }

                case NetMsg.EventKill:
                {
                    int killerId = r.Int(), victimId = r.Int();
                    var pos = r.Vec();
                    int room = r.Int();
                    _match.ApplyRemoteKill(killerId, victimId, pos, room);
                    return;
                }

                case NetMsg.EventMeeting:
                {
                    int callerId = r.Int(), victimId = r.Int();
                    bool emergency = r.Bool();
                    _match.ApplyRemoteMeeting(callerId, victimId, emergency);
                    return;
                }

                case NetMsg.EventVote:
                {
                    int voterId = r.Int(), target = r.Int();
                    _match.ApplyRemoteVote(voterId, target);
                    return;
                }

                case NetMsg.EventEject:
                {
                    int id = r.Int();
                    bool wasInf = r.Bool();
                    _match.ApplyRemoteEject(id, wasInf);
                    return;
                }

                case NetMsg.EventTask:
                {
                    int playerId = r.Int();
                    r.Int();
                    r.Int();
                    float progress = r.Float();
                    _match.ApplyRemoteTaskProgress(playerId, progress);
                    return;
                }

                case NetMsg.EventSabotage:
                {
                    var type = (SabotageType)r.Byte();
                    int roomId = r.Int();
                    _match.Sabotage.Trigger(type, roomId);
                    return;
                }

                case NetMsg.EventSabotageEnd:
                    _match.Sabotage.Resolve();
                    return;

                case NetMsg.EventChat:
                {
                    int speakerId = r.Int();
                    string text = r.Str();
                    bool ghost = r.Bool();
                    var speaker = _match.PlayerById(speakerId);
                    _match.AddChat(speaker, text, SpeechIntent.None, ghost);
                    return;
                }

                case NetMsg.EventGameOver:
                {
                    var side = (WinSide)r.Byte();
                    var reason = (WinReason)r.Byte();
                    _match.Finish(side, reason);
                    return;
                }

                case NetMsg.EventVent:
                {
                    int id = r.Int();
                    int ventId = r.Int();
                    bool entering = r.Bool();
                    var p = _match.PlayerById(id);
                    if (p?.View is Actor a && StationView.Instance != null)
                    {
                        var vent = StationView.Instance.VentById(ventId);
                        if (entering) a.EnterVent(vent);
                        else a.ExitVent(vent);
                    }
                    return;
                }
            }
        }

        private int LocalIdFromRoster(List<PlayerState> roster)
        {
            var name = GameSettings.Profile.DisplayName;
            foreach (var p in roster)
                if (!p.IsBot && p.Name == name) return p.Id;
            foreach (var p in roster)
                if (!p.IsBot) return p.Id;
            return 0;
        }

        // ==================================================================
        //  peers
        // ==================================================================
        private void OnPeerJoined(NetPeer peer)
        {
            if (!IsHost || _match == null) return;

            if (peer.PlayerId < 0)
            {
                // hand the newcomer a bot's seat so the roster size stays constant
                foreach (var p in _match.Players)
                {
                    if (!p.IsBot || !p.Connected) continue;
                    if (!p.IsAlive) continue;
                    peer.PlayerId = p.Id;
                    p.IsBot = false;
                    p.Brain = null;
                    break;
                }
            }
            else
            {
                var p = _match.PlayerById(peer.PlayerId);
                if (p != null) p.Connected = true;
            }

            SendRoster();
            GameEvents.RaiseAnnounce("Игрок подключился", 2.5f);
        }

        private void OnPeerLost(NetPeer peer)
        {
            if (!IsHost || _match == null) return;
            var p = _match.PlayerById(peer.PlayerId);
            if (p == null) return;
            p.Connected = false;
            // a dropped human is taken over by an NPC so the match can continue
            if (p.IsAlive && p.Brain == null)
            {
                var brain = new AI.NpcBrain();
                brain.Init(p, _match, _match.Settings.AiDifficulty, new NebulaRandom(p.Id * 7717 + 3));
                p.Brain = brain;
                p.IsBot = true;
            }
            GameEvents.RaiseAnnounce("Игрок отключился — управление перешло к NPC", 3f);
        }

        // ==================================================================
        //  client requests
        // ==================================================================
        public void RequestKill(int victimId) =>
            _net.SendToHost(new NetWriter(NetMsg.RequestKill, _net.NextSequence()).Int(victimId), true);

        public void RequestReport(int victimId) =>
            _net.SendToHost(new NetWriter(NetMsg.RequestReport, _net.NextSequence()).Int(victimId), true);

        public void RequestEmergency() =>
            _net.SendToHost(new NetWriter(NetMsg.RequestEmergency, _net.NextSequence()), true);

        public void RequestTaskStage(TaskId task) =>
            _net.SendToHost(new NetWriter(NetMsg.RequestTaskStage, _net.NextSequence()).Int((int)task), true);

        public void RequestVote(int targetId) =>
            _net.SendToHost(new NetWriter(NetMsg.RequestVote, _net.NextSequence()).Int(targetId), true);

        public void RequestSabotage(SabotageType type, int roomId) =>
            _net.SendToHost(new NetWriter(NetMsg.RequestSabotage, _net.NextSequence()).Byte((byte)type).Int(roomId), true);

        public void RequestVent() =>
            _net.SendToHost(new NetWriter(NetMsg.RequestVent, _net.NextSequence()), true);

        public void RequestChat(string text) =>
            _net.SendToHost(new NetWriter(NetMsg.RequestChat, _net.NextSequence()).Str(text), true);
    }
}
