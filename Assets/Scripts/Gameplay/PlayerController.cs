// -----------------------------------------------------------------------------
//  NEBULA NINE - local player input and interaction verbs.
//
//  Movement comes from the on-screen stick (or WASD in the editor).  Every action
//  button on the HUD calls into here, and here alone decides whether the action is
//  currently legal - the HUD only reflects that state.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Characters;
using Nebula.Core;
using Nebula.Map;
using Nebula.Tasks;

namespace Nebula.Gameplay
{
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        public Vector2 StickInput;
        public bool UseHeld;
        /// <summary>Repair console the player is currently holding (-1 = none); streamed to the host.</summary>
        public int HeldPanelIndex { get; private set; } = -1;

        private static bool IsClient =>
            Net.NetBridge.Instance != null && Net.NetBridge.Instance.Active && !Net.NetBridge.Instance.IsHost;

        private MatchManager _match;
        private PlayerState _self;
        private Actor _actor;
        private float _ventHopCooldown;

        public System.Action<TaskInstance> OnRequestTaskWindow;
        public System.Action OnRequestCameras;
        public System.Action OnRequestAdmin;
        public System.Action OnRequestSabotageMenu;

        public PlayerState Self => _self;

        private void Awake() => Instance = this;

        public void Bind(MatchManager match)
        {
            _match = match;
            _self = match.Local;
            _actor = _self?.View as Actor;
        }

        private void Update()
        {
            if (_match == null || _self == null) return;
            if (_actor == null) _actor = _self.View as Actor;
            if (_actor == null) return;

            if (_ventHopCooldown > 0f) _ventHopCooldown -= Time.deltaTime;

            var input = StickInput;
#if UNITY_EDITOR || UNITY_STANDALONE
            var keys = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (keys.sqrMagnitude > 0.01f) input = keys;
            if (Input.GetKeyDown(KeyCode.E)) DoUse();
            if (Input.GetKeyDown(KeyCode.Q)) DoKill();
            if (Input.GetKeyDown(KeyCode.R)) DoReport();
            if (Input.GetKeyDown(KeyCode.F)) DoVent();
            UseHeld |= Input.GetKey(KeyCode.E);
#endif

            bool canMove = _match.Phase == MatchPhase.Roaming && !_self.InVent;
            _actor.Motor.SetInput(canMove ? input : Vector2.zero);

            if (UseHeld) TickHoldInteraction();
        }

        private void LateUpdate()
        {
            UseHeld = false;
            HeldPanelIndex = -1;
        }

        // ------------------------------------------------------------------ interactions
        private void TickHoldInteraction()
        {
            var sab = _match.Sabotage;
            if (sab == null || !sab.IsActive || !_self.IsAlive) return;
            int panel = sab.PanelIndexNear(_self.Position, _self.Deck);
            if (panel < 0) return;
            HeldPanelIndex = panel;
            if (!IsClient) sab.HoldPanel(panel, _self.Id);
            _actor.SetActivity(AnimState.Repair, 0.4f);
        }

        public void DoUse()
        {
            if (_match.Phase != MatchPhase.Roaming) return;

            // 1. elevator
            var view = StationView.Instance;
            if (view != null && !_self.InVent)
            {
                var pad = view.NearestElevator(_self.Position, _self.Deck, 3.2f);
                if (pad != null && pad.Cooldown <= 0f)
                {
                    _match.TryElevator(_self);
                    return;
                }
            }

            // 2. security cameras / admin table
            var security = StationLayout.Get("security");
            if (security != null && _self.RoomId == security.Id && _self.IsAlive)
            {
                OnRequestCameras?.Invoke();
                return;
            }
            var command = StationLayout.Get("command");
            if (command != null && _self.RoomId == command.Id && _self.IsAlive &&
                (_match.Sabotage == null || !_match.Sabotage.CommsDown))
            {
                // only when not standing on a task console
                var nearTask = _match.Tasks.FindTaskInRange(_self, 2.4f);
                if (nearTask == null)
                {
                    OnRequestAdmin?.Invoke();
                    return;
                }
            }

            // 3. task console
            if (!_self.InVent && (_self.IsAlive || (_self.IsGhost && _match.Settings.GhostsDoTasks)))
            {
                var task = _match.Tasks.FindTaskInRange(_self);
                if (task != null)
                {
                    if (_self.Role == Role.Infiltrator)
                    {
                        // infiltrators can open the console but never complete anything
                        _actor.SetActivity(AnimState.Work, 2.5f);
                        GameEvents.RaiseAnnounce("Ты только делаешь вид, что работаешь", 1.6f);
                        return;
                    }
                    OnRequestTaskWindow?.Invoke(task);
                    return;
                }
            }
        }

        public bool CanKill(out PlayerState target)
        {
            target = _match != null ? _match.FindKillTarget(_self) : null;
            return target != null && _match.CanKill(_self, target);
        }

        public void DoKill()
        {
            if (!CanKill(out var target)) return;
            if (IsClient) Net.NetBridge.Instance.RequestKill(target.Id);
            else _match.TryKill(_self, target);
        }

        public bool CanReport(out PlayerState victim)
        {
            victim = _match != null ? _match.FindReportableBody(_self) : null;
            return victim != null;
        }

        public void DoReport()
        {
            if (!CanReport(out var victim)) return;
            if (IsClient) Net.NetBridge.Instance.RequestReport(victim.Id);
            else _match.TryReport(_self, victim);
        }

        public bool CanEmergency() => _match != null && _match.CanCallEmergency(_self);

        public void DoEmergency()
        {
            if (!CanEmergency()) return;
            if (IsClient) Net.NetBridge.Instance.RequestEmergency();
            else _match.TryEmergency(_self);
        }

        public bool CanVent()
        {
            if (_match == null || _self == null || _self.Role != Role.Infiltrator || !_self.IsAlive) return false;
            if (_self.InVent) return true;
            var view = StationView.Instance;
            return view != null && view.NearestVent(_self.Position, _self.Deck, 2.6f) != null;
        }

        public void DoVent()
        {
            if (!CanVent()) return;
            if (IsClient) Net.NetBridge.Instance.RequestVent();
            else _match.TryUseVent(_self, out _);
        }

        public List<VentPoint> VentTargets() => _match != null ? _match.ConnectedVents(_self) : new List<VentPoint>();

        public void HopVent(VentPoint vent)
        {
            if (vent == null || !_self.InVent || _ventHopCooldown > 0f) return;
            _ventHopCooldown = 0.45f;
            _actor.MoveThroughVent(vent);
            _self.RoomId = vent.RoomId;
            _self.Deck = vent.Def.Deck;
        }

        public bool CanSabotage() => _match != null && _self != null && _self.Role == Role.Infiltrator &&
                                     _self.IsAlive && _match.Phase == MatchPhase.Roaming;

        public void OpenSabotage() => OnRequestSabotageMenu?.Invoke();

        public void CompleteTaskStage(TaskInstance task)
        {
            if (task == null || _self == null) return;
            if (IsClient)
            {
                Net.NetBridge.Instance.RequestTaskStage(task.Definition.Id);
                return;
            }
            _match.Tasks.CompleteStage(_self, task, _match.Players);
            _match.Tasks.RefreshMarkers(_self);
            _match.CheckWinConditions();
        }

        /// <summary>Sabotage requests go through the host in multiplayer.</summary>
        public void TriggerSabotage(SabotageType type, int roomId)
        {
            if (!CanSabotage()) return;
            if (IsClient) Net.NetBridge.Instance.RequestSabotage(type, roomId);
            else _match.Sabotage.Trigger(type, roomId);
        }

        /// <summary>Votes go through the host in multiplayer.</summary>
        public void SubmitVote(int targetId)
        {
            if (_self == null || !_self.IsAlive) return;
            if (IsClient) Net.NetBridge.Instance.RequestVote(targetId);
            else _match.CastVote(_self, targetId);
        }

        /// <summary>Chat goes through the host in multiplayer.</summary>
        public void SubmitChat(string text)
        {
            if (_self == null || string.IsNullOrWhiteSpace(text)) return;
            if (IsClient) { Net.NetBridge.Instance.RequestChat(text); return; }
            _match.AddChat(_self, text, SpeechIntent.None, _self.IsGhost);
            if (!_self.IsGhost) _match.Debate?.OnPlayerChat(_self, text);
        }
    }
}
