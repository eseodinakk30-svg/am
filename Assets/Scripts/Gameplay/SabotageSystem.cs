// -----------------------------------------------------------------------------
//  NEBULA NINE - sabotage rules.
//
//  Seven sabotages with three different repair shapes:
//    * SinglePanel      - one console, one person (lights, comms)
//    * BothSimultaneous - two consoles held at the same time (reactor, coolant)
//    * BothSequential   - two consoles, any order, before the timer (oxygen, engines)
//  Doors are instant and need no repair.
//
//  Everything a saboteur can trigger is exposed as data so the AI can reason
//  about which sabotage splits the crew best right now.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Map;

namespace Nebula.Gameplay
{
    public enum RepairMode
    {
        None = 0,
        SinglePanel = 1,
        BothSimultaneous = 2,
        BothSequential = 3,
    }

    public class SabotagePanel
    {
        public int RoomId;
        public Vector3 Position;
        public DeckId Deck;
        public bool Done;
        public float HoldProgress;
        public int HeldBy = -1;
        public bool HeldThisFrame;
    }

    public class SabotageSystem
    {
        public SabotageType Active { get; private set; } = SabotageType.None;
        public SabotageSeverity Severity { get; private set; }
        public RepairMode Mode { get; private set; }
        public float TimeLeft { get; private set; }
        public float TotalTime { get; private set; }
        public int TargetRoomId { get; private set; } = -1;
        public float Cooldown { get; private set; }
        public readonly List<SabotagePanel> Panels = new List<SabotagePanel>();

        public bool IsActive => Active != SabotageType.None;
        public bool IsCritical => IsActive && Severity == SabotageSeverity.Critical;

        private MatchSettings _settings;
        private StationView _view;
        private float _doorTimer;

        public void Init(MatchSettings settings, StationView view)
        {
            _settings = settings;
            _view = view;
            Active = SabotageType.None;
            Cooldown = settings.SabotageCooldown * 0.5f;
            Panels.Clear();
        }

        public void ResetForRound()
        {
            Resolve(false);
            Cooldown = _settings != null ? _settings.SabotageCooldown * 0.5f : 12f;
        }

        // ------------------------------------------------------------------ availability
        public bool CanSabotage(SabotageType type)
        {
            if (Cooldown > 0f) return false;
            if (type == SabotageType.Doors) return _doorTimer <= 0f;
            return !IsActive;
        }

        public List<SabotageType> AvailableSabotages()
        {
            var list = new List<SabotageType>();
            if (IsActive)
            {
                if (_doorTimer <= 0f && Cooldown <= 0f) list.Add(SabotageType.Doors);
                return list;
            }
            if (Cooldown > 0f) return list;

            list.Add(SabotageType.Lights);
            list.Add(SabotageType.Reactor);
            list.Add(SabotageType.Oxygen);
            list.Add(SabotageType.Comms);
            list.Add(SabotageType.Engines);
            list.Add(SabotageType.Coolant);
            if (_doorTimer <= 0f) list.Add(SabotageType.Doors);
            return list;
        }

        // ------------------------------------------------------------------ start
        public bool Trigger(SabotageType type, int doorRoomId = -1)
        {
            if (!CanSabotage(type)) return false;

            if (type == SabotageType.Doors)
            {
                if (doorRoomId < 0) return false;
                _view?.CloseDoorsOfRoom(doorRoomId, _settings.DoorCloseDuration);
                _doorTimer = _settings.DoorCloseDuration + 8f;
                Cooldown = 10f;
                GameEvents.RaiseDoorsClosed(doorRoomId, _settings.DoorCloseDuration);
                GameEvents.RaiseSabotageStarted(SabotageType.Doors, doorRoomId);
                Audio.SoundBank.Play(Audio.Sfx.DoorClose, 0.7f);
                return true;
            }

            Active = type;
            Panels.Clear();
            TargetRoomId = -1;

            switch (type)
            {
                case SabotageType.Lights:
                    Severity = SabotageSeverity.Systemic;
                    Mode = RepairMode.SinglePanel;
                    TotalTime = TimeLeft = 0f;
                    AddPanel("electrical", 5);
                    _view?.SetAllLights(0.06f);
                    GameEvents.RaiseVisionFactorChanged(_settings.LightsOutVision / _settings.CrewVision);
                    break;

                case SabotageType.Comms:
                    Severity = SabotageSeverity.Systemic;
                    Mode = RepairMode.SinglePanel;
                    TotalTime = TimeLeft = 0f;
                    AddPanel("comms", 5);
                    break;

                case SabotageType.Reactor:
                    Severity = SabotageSeverity.Critical;
                    Mode = RepairMode.BothSimultaneous;
                    TotalTime = TimeLeft = _settings.ReactorMeltdownTime;
                    AddPanel("reactor", 1);
                    AddPanel("reactor", 5);
                    break;

                case SabotageType.Coolant:
                    Severity = SabotageSeverity.Critical;
                    Mode = RepairMode.BothSimultaneous;
                    TotalTime = TimeLeft = _settings.CoolantOverloadTime;
                    AddPanel("coolant", 2);
                    AddPanel("engines", 6);
                    break;

                case SabotageType.Oxygen:
                    Severity = SabotageSeverity.Critical;
                    Mode = RepairMode.BothSequential;
                    TotalTime = TimeLeft = _settings.OxygenDepletionTime;
                    AddPanel("lifesupport", 3);
                    AddPanel("cafeteria", 7);
                    break;

                case SabotageType.Engines:
                    Severity = SabotageSeverity.Systemic;
                    Mode = RepairMode.BothSequential;
                    TotalTime = TimeLeft = 0f;
                    AddPanel("engines", 2);
                    AddPanel("dronebay", 4);
                    break;
            }

            Cooldown = _settings.SabotageCooldown;
            GameEvents.RaiseSabotageStarted(type, TargetRoomId);
            Audio.SoundBank.Play(Audio.Sfx.SabotageStart, 0.8f);
            if (Severity == SabotageSeverity.Critical) Audio.SoundBank.Play(Audio.Sfx.Alarm, 0.6f);
            return true;
        }

        private void AddPanel(string roomKey, int slot)
        {
            var area = StationLayout.Get(roomKey);
            if (area == null) return;
            var grid = StationGrid.Instance;
            var pos = grid != null ? grid.StationPoint(area.Id, slot, out _) : Vector3.zero;
            Panels.Add(new SabotagePanel { RoomId = area.Id, Position = pos, Deck = area.Deck });
        }

        // ------------------------------------------------------------------ repair
        /// <summary>Called every frame by whoever is holding a repair console.</summary>
        public void HoldPanel(int panelIndex, int playerId)
        {
            if (!IsActive || panelIndex < 0 || panelIndex >= Panels.Count) return;
            var panel = Panels[panelIndex];
            panel.HeldThisFrame = true;
            panel.HeldBy = playerId;
        }

        public int PanelIndexNear(Vector3 position, DeckId deck, float range = 3.4f)
        {
            if (!IsActive) return -1;
            float best = range * range;
            int bestIndex = -1;
            for (int i = 0; i < Panels.Count; i++)
            {
                if (Panels[i].Deck != deck) continue;
                if (Panels[i].Done) continue;
                float d = (Panels[i].Position - position).sqrMagnitude;
                if (d < best) { best = d; bestIndex = i; }
            }
            return bestIndex;
        }

        public void Update(float dt)
        {
            if (Cooldown > 0f) Cooldown -= dt;
            if (_doorTimer > 0f) _doorTimer -= dt;
            if (!IsActive) return;

            switch (Mode)
            {
                case RepairMode.SinglePanel:
                {
                    var p = Panels[0];
                    if (p.HeldThisFrame)
                    {
                        p.HoldProgress += dt * (Active == SabotageType.Lights ? 0.55f * _settings.LightsRepairFactor : 0.5f);
                        if (p.HoldProgress >= 1f) { Resolve(true); return; }
                    }
                    else p.HoldProgress = Mathf.Max(0f, p.HoldProgress - dt * 0.25f);
                    break;
                }

                case RepairMode.BothSimultaneous:
                {
                    bool bothHeld = Panels.Count >= 2 && Panels[0].HeldThisFrame && Panels[1].HeldThisFrame &&
                                    Panels[0].HeldBy != Panels[1].HeldBy;
                    if (bothHeld)
                    {
                        Panels[0].HoldProgress += dt * 0.55f;
                        Panels[1].HoldProgress = Panels[0].HoldProgress;
                        if (Panels[0].HoldProgress >= 1f) { Resolve(true); return; }
                    }
                    else
                    {
                        foreach (var p in Panels) p.HoldProgress = Mathf.Max(0f, p.HoldProgress - dt * 0.6f);
                    }
                    break;
                }

                case RepairMode.BothSequential:
                {
                    bool all = true;
                    foreach (var p in Panels)
                    {
                        if (p.Done) continue;
                        if (p.HeldThisFrame)
                        {
                            p.HoldProgress += dt * 0.7f;
                            if (p.HoldProgress >= 1f)
                            {
                                p.Done = true;
                                Audio.SoundBank.Play(Audio.Sfx.Repair, 0.7f);
                            }
                        }
                        else p.HoldProgress = Mathf.Max(0f, p.HoldProgress - dt * 0.3f);
                        if (!p.Done) all = false;
                    }
                    if (all) { Resolve(true); return; }
                    break;
                }
            }

            foreach (var p in Panels) p.HeldThisFrame = false;

            if (Severity == SabotageSeverity.Critical)
            {
                TimeLeft -= dt;
                if (TimeLeft <= 0f) TimeLeft = 0f;
            }
        }

        public bool CriticalExpired => IsCritical && TimeLeft <= 0f;

        public void Resolve(bool announce = true)
        {
            if (Active == SabotageType.None) return;
            var was = Active;

            if (was == SabotageType.Lights)
            {
                _view?.SetAllLights(1f);
                GameEvents.RaiseVisionFactorChanged(1f);
            }

            Active = SabotageType.None;
            Severity = SabotageSeverity.Systemic;
            Mode = RepairMode.None;
            TimeLeft = 0f;
            TargetRoomId = -1;
            Panels.Clear();

            if (announce)
            {
                GameEvents.RaiseSabotageResolved(was);
                Audio.SoundBank.Play(Audio.Sfx.Repair, 0.8f);
            }
        }

        // ------------------------------------------------------------------ info
        public string DisplayName(SabotageType type)
        {
            switch (type)
            {
                case SabotageType.Lights: return "Отключить свет";
                case SabotageType.Reactor: return "Разрушить реактор";
                case SabotageType.Oxygen: return "Отключить кислород";
                case SabotageType.Comms: return "Заглушить связь";
                case SabotageType.Doors: return "Заблокировать двери";
                case SabotageType.Engines: return "Заглушить двигатели";
                case SabotageType.Coolant: return "Перегрев охлаждения";
                default: return "-";
            }
        }

        public string ActiveDescription()
        {
            switch (Active)
            {
                case SabotageType.Lights: return "АВАРИЯ ОСВЕЩЕНИЯ — почини щит в Электрощитовой";
                case SabotageType.Reactor: return "РЕАКТОР НЕСТАБИЛЕН — двое к панелям реактора";
                case SabotageType.Oxygen: return "УТЕЧКА КИСЛОРОДА — Жизнеобеспечение и Столовая";
                case SabotageType.Comms: return "СВЯЗЬ ЗАГЛУШЕНА — почини узел связи";
                case SabotageType.Engines: return "ДВИГАТЕЛИ ОСТАНОВЛЕНЫ — Двигательный отсек и Ангар дронов";
                case SabotageType.Coolant: return "ПЕРЕГРЕВ — Холодильная установка и Двигательный отсек";
                default: return "";
            }
        }

        /// <summary>Comms sabotage blackout: task list and admin table are hidden.</summary>
        public bool CommsDown => Active == SabotageType.Comms;
        public bool LightsDown => Active == SabotageType.Lights;
        public bool EnginesDown => Active == SabotageType.Engines;
    }
}
