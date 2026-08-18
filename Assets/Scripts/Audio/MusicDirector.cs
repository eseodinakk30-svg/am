// -----------------------------------------------------------------------------
//  NEBULA NINE - adaptive procedural score.
//
//  Three synthesised loops (calm / tension / meeting) plus a station drone are
//  crossfaded according to what the match is doing.  Everything is generated at
//  startup on a background-friendly budget (~1.5 MB of PCM).
// -----------------------------------------------------------------------------

using UnityEngine;
using Nebula.Core;

namespace Nebula.Audio
{
    public enum MusicMood
    {
        Menu = 0,
        Calm = 1,
        Tension = 2,
        Meeting = 3,
        Critical = 4,
        Silence = 5,
    }

    public class MusicDirector : MonoBehaviour
    {
        public static MusicDirector Instance { get; private set; }

        private const int Rate = 22050;
        private AudioSource _layerCalm, _layerTension, _layerMeeting, _drone;
        private MusicMood _mood = MusicMood.Menu;
        private float _masterVolume = 0.55f;

        public MusicMood Mood => _mood;

        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Mathf.Clamp01(value);
        }

        public static MusicDirector Create(Transform parent)
        {
            var go = new GameObject("MusicDirector");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<MusicDirector>();
            m.Init();
            Instance = m;
            return m;
        }

        private void Init()
        {
            _layerCalm = MakeSource("Calm", BuildCalmLoop());
            _layerTension = MakeSource("Tension", BuildTensionLoop());
            _layerMeeting = MakeSource("Meeting", BuildMeetingLoop());
            _drone = MakeSource("Drone", BuildDrone());
            _drone.volume = 0.16f;
            SetMood(MusicMood.Menu, true);
        }

        private AudioSource MakeSource(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 0f;
            src.playOnAwake = false;
            src.Play();
            return src;
        }

        public void SetMood(MusicMood mood, bool instant = false)
        {
            _mood = mood;
            if (instant) ApplyTargets(1f);
        }

        private void Update()
        {
            ApplyTargets(Time.unscaledDeltaTime * 1.1f);
        }

        private void ApplyTargets(float lerp)
        {
            float calm = 0f, tension = 0f, meeting = 0f, drone = 0.18f;
            switch (_mood)
            {
                case MusicMood.Menu: calm = 0.75f; drone = 0.10f; break;
                case MusicMood.Calm: calm = 0.42f; drone = 0.22f; break;
                case MusicMood.Tension: calm = 0.12f; tension = 0.6f; drone = 0.25f; break;
                case MusicMood.Critical: tension = 0.95f; drone = 0.3f; break;
                case MusicMood.Meeting: meeting = 0.8f; drone = 0.08f; break;
                case MusicMood.Silence: drone = 0.05f; break;
            }

            float k = Mathf.Clamp01(lerp);
            _layerCalm.volume = Mathf.Lerp(_layerCalm.volume, calm * _masterVolume, k);
            _layerTension.volume = Mathf.Lerp(_layerTension.volume, tension * _masterVolume, k);
            _layerMeeting.volume = Mathf.Lerp(_layerMeeting.volume, meeting * _masterVolume, k);
            _drone.volume = Mathf.Lerp(_drone.volume, drone * _masterVolume, k);
        }

        // ------------------------------------------------------------------ synthesis
        private static AudioClip Build(string name, float seconds, System.Func<float, float, float> gen)
        {
            int samples = Mathf.RoundToInt(seconds * Rate);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Clamp(gen(t, t / seconds), -1f, 1f);
            }
            // ensure a click free loop point
            int fade = Mathf.Min(2000, samples / 8);
            for (int i = 0; i < fade; i++)
            {
                float f = i / (float)fade;
                data[i] *= f;
                data[samples - 1 - i] *= f;
            }
            var clip = AudioClip.Create(name, samples, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Tri(float phase) => Mathf.Abs(Mathf.Repeat(phase, 1f) * 4f - 2f) - 1f;

        private static readonly float[] MinorRoot = { 110f, 130.81f, 98f, 116.54f };   // Am - C - G - Bb feel

        private static AudioClip BuildCalmLoop()
        {
            return Build("mus_calm", 16f, (t, p) =>
            {
                int bar = Mathf.FloorToInt(p * 4f) % 4;
                float root = MinorRoot[bar];
                float pad = Mathf.Sin(t * 2f * Mathf.PI * root) * 0.16f
                          + Mathf.Sin(t * 2f * Mathf.PI * root * 1.5f) * 0.10f
                          + Mathf.Sin(t * 2f * Mathf.PI * root * 2f) * 0.06f;
                pad *= 0.7f + 0.3f * Mathf.Sin(t * 0.6f);

                // sparse arpeggio
                float step = Mathf.Repeat(t * 2f, 1f);
                float arpFreq = root * (bar % 2 == 0 ? 4f : 3f);
                float arp = Tri(t * arpFreq) * Mathf.Pow(1f - step, 4f) * 0.09f;

                return pad + arp;
            });
        }

        private static AudioClip BuildTensionLoop()
        {
            return Build("mus_tension", 12f, (t, p) =>
            {
                float pulse = Mathf.Repeat(t * 2.4f, 1f);
                float bass = Mathf.Sin(t * 2f * Mathf.PI * 55f) * Mathf.Pow(1f - pulse, 2.4f) * 0.28f;
                float drone = Mathf.Sin(t * 2f * Mathf.PI * 82.4f) * 0.09f
                            + Mathf.Sin(t * 2f * Mathf.PI * 87.3f) * 0.07f;   // slight beating = unease
                float tick = Mathf.Repeat(t * 4f, 1f);
                float hat = (Random.value * 2f - 1f) * Mathf.Pow(1f - tick, 22f) * 0.05f;
                float swell = Mathf.Sin(t * 2f * Mathf.PI * 220f) * Mathf.Max(0f, Mathf.Sin(p * Mathf.PI * 2f)) * 0.05f;
                return bass + drone + hat + swell;
            });
        }

        private static AudioClip BuildMeetingLoop()
        {
            return Build("mus_meeting", 10f, (t, p) =>
            {
                float beat = Mathf.Repeat(t * 1.6f, 1f);
                float stab = (Mathf.Sin(t * 2f * Mathf.PI * 147f) + Mathf.Sin(t * 2f * Mathf.PI * 185f)) *
                             Mathf.Pow(1f - beat, 3.2f) * 0.13f;
                float pad = Mathf.Sin(t * 2f * Mathf.PI * 73.4f) * 0.11f;
                float shimmer = Tri(t * 588f) * (0.5f + 0.5f * Mathf.Sin(t * 1.3f)) * 0.025f;
                return stab + pad + shimmer;
            });
        }

        private static AudioClip BuildDrone()
        {
            return Build("mus_drone", 8f, (t, p) =>
            {
                float hum = Mathf.Sin(t * 2f * Mathf.PI * 48f) * 0.16f
                          + Mathf.Sin(t * 2f * Mathf.PI * 96f) * 0.05f;
                float air = (Random.value * 2f - 1f) * 0.02f;
                float sweep = Mathf.Sin(t * 0.35f) * 0.04f * Mathf.Sin(t * 2f * Mathf.PI * 190f);
                return hum + air + sweep;
            });
        }
    }
}
