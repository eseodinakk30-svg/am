// -----------------------------------------------------------------------------
//  NEBULA NINE - interactive map furniture: doors, vents, elevators, cameras and
//  the room lighting rig that the "lights out" sabotage drives.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Fx;

namespace Nebula.Map
{
    // ---------------------------------------------------------------- doors
    public class DoorController : MonoBehaviour
    {
        public AreaDef Doorway;
        public int OwnerRoomId = -1;

        private Transform _leaf0, _leaf1;
        private float _openAmount = 1f;     // 1 = fully open
        private float _closeTimer;
        private bool _closed;
        private AudioSource _audio;

        public bool IsClosed => _closed;
        public float TimeLeft => _closeTimer;

        public void Init(AreaDef doorway, Transform leafA, Transform leafB)
        {
            Doorway = doorway;
            _leaf0 = leafA;
            _leaf1 = leafB;
            var owner = StationLayout.Get(doorway.OwnerRoom);
            OwnerRoomId = owner?.Id ?? -1;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.minDistance = 4f;
            _audio.maxDistance = 26f;
            _audio.playOnAwake = false;
            _audio.rolloffMode = AudioRolloffMode.Linear;
        }

        public void Close(float duration)
        {
            if (!Doorway.Closable) return;
            _closed = true;
            _closeTimer = Mathf.Max(_closeTimer, duration);
            StationGrid.Instance?.SetBlocked(Doorway, true);
            Audio.SoundBank.PlayAt(_audio, Audio.Sfx.DoorClose);
        }

        public void ForceOpen()
        {
            if (!_closed) return;
            _closed = false;
            _closeTimer = 0f;
            StationGrid.Instance?.SetBlocked(Doorway, false);
            Audio.SoundBank.PlayAt(_audio, Audio.Sfx.DoorOpen);
        }

        private void Update()
        {
            if (_closed)
            {
                _closeTimer -= Time.deltaTime;
                if (_closeTimer <= 0f) ForceOpen();
            }

            float target = _closed ? 0f : 1f;
            if (!Mathf.Approximately(_openAmount, target))
            {
                _openAmount = Mathf.MoveTowards(_openAmount, target, Time.deltaTime * 2.6f);
                Apply();
            }
        }

        private void Apply()
        {
            if (_leaf0 == null || _leaf1 == null) return;
            float slide = Mathf.Lerp(0f, 1f, _openAmount);
            _leaf0.localPosition = new Vector3(-slide * 1.9f, _leaf0.localPosition.y, 0f);
            _leaf1.localPosition = new Vector3(slide * 1.9f, _leaf1.localPosition.y, 0f);
        }
    }

    // ---------------------------------------------------------------- vents
    public class VentPoint : MonoBehaviour
    {
        public VentDef Def;
        public int RoomId = -1;
        private Transform _lid;
        private float _lidAngle;
        private float _targetAngle;
        public float LastUsedTime = -99f;

        public void Init(VentDef def, Transform lid)
        {
            Def = def;
            _lid = lid;
            var room = StationLayout.Get(def.RoomKey);
            RoomId = room?.Id ?? -1;
        }

        public void PlayOpen()
        {
            _targetAngle = -105f;
            LastUsedTime = Time.time;
            CancelInvoke(nameof(PlayClose));
            Invoke(nameof(PlayClose), 0.75f);
        }

        public void PlayClose() => _targetAngle = 0f;

        private void Update()
        {
            if (Mathf.Abs(_lidAngle - _targetAngle) > 0.5f && _lid != null)
            {
                _lidAngle = Mathf.MoveTowards(_lidAngle, _targetAngle, Time.deltaTime * 420f);
                _lid.localRotation = Quaternion.Euler(_lidAngle, 0f, 0f);
            }
        }
    }

    // ---------------------------------------------------------------- elevator
    public class ElevatorPad : MonoBehaviour
    {
        public ElevatorDef Def;
        public bool IsSideA;
        public float Cooldown;

        public Vector3 OtherSideWorld =>
            IsSideA ? StationLayout.CellToWorld(Def.DeckB, Def.CellB) : StationLayout.CellToWorld(Def.DeckA, Def.CellA);

        public DeckId OtherDeck => IsSideA ? Def.DeckB : Def.DeckA;

        private void Update()
        {
            if (Cooldown > 0f) Cooldown -= Time.deltaTime;
        }
    }

    // ---------------------------------------------------------------- cameras
    public class SecurityCameraUnit : MonoBehaviour
    {
        public CameraDef Def;
        public int RoomId = -1;
        public Camera Cam;
        public RenderTexture Target;
        private Transform _indicator;
        private Renderer _indicatorRenderer;
        private float _blink;

        public static bool AnyoneWatching;

        public void Init(CameraDef def, Camera cam, RenderTexture rt, Transform indicator)
        {
            Def = def;
            Cam = cam;
            Target = rt;
            _indicator = indicator;
            _indicatorRenderer = indicator != null ? indicator.GetComponent<Renderer>() : null;
            var room = StationLayout.Get(def.RoomKey);
            RoomId = room?.Id ?? -1;
            SetActive(false);
        }

        public void SetActive(bool on)
        {
            if (Cam != null) Cam.enabled = on;
        }

        private void Update()
        {
            if (_indicatorRenderer == null) return;
            bool on = AnyoneWatching;
            _blink += Time.deltaTime * (on ? 6f : 1.2f);
            float k = on ? (Mathf.Sin(_blink) * 0.5f + 0.5f) : 0.06f;
            var c = on ? Color.Lerp(new Color(0.35f, 0f, 0f), new Color(1f, 0.15f, 0.12f), k)
                       : new Color(0.16f, 0.05f, 0.05f);
            Art.Tint(_indicatorRenderer, c);
        }
    }

    // ---------------------------------------------------------------- lighting
    /// <summary>One per room; the lights sabotage dims every registered rig.</summary>
    public class RoomLightRig : MonoBehaviour
    {
        public int RoomId = -1;
        private readonly List<Light> _lights = new List<Light>();
        private readonly List<float> _baseIntensity = new List<float>();
        private readonly List<Renderer> _emissive = new List<Renderer>();
        private readonly List<Color> _emissiveBase = new List<Color>();
        private float _current = 1f;
        private float _target = 1f;

        public void Register(Light l)
        {
            _lights.Add(l);
            _baseIntensity.Add(l.intensity);
        }

        public void RegisterEmissive(Renderer r, Color baseColor)
        {
            _emissive.Add(r);
            _emissiveBase.Add(baseColor);
        }

        public void SetLevel(float level) => _target = Mathf.Clamp01(level);

        private void Update()
        {
            if (Mathf.Abs(_current - _target) < 0.002f) return;
            _current = Mathf.MoveTowards(_current, _target, Time.deltaTime * 1.9f);
            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] == null) continue;
                _lights[i].intensity = _baseIntensity[i] * _current;
                if (_current < 0.05f) _lights[i].enabled = false;
                else if (!_lights[i].enabled) _lights[i].enabled = true;
            }
            for (int i = 0; i < _emissive.Count; i++)
            {
                if (_emissive[i] == null) continue;
                var c = Color.Lerp(_emissiveBase[i] * 0.12f, _emissiveBase[i], _current);
                Art.Tint(_emissive[i], c);
            }
        }
    }
}
