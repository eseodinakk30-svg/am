// -----------------------------------------------------------------------------
//  NEBULA NINE - procedural sound bank.
//
//  Every effect is synthesised at runtime (noise bursts, FM blips, filtered
//  sweeps, metallic clangs) so the project ships without audio files while still
//  having a full, coherent sound palette.  Clips are generated lazily and cached.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Audio
{
    public enum Sfx
    {
        Footstep,
        DoorOpen,
        DoorClose,
        Kill,
        Vent,
        TaskTick,
        TaskComplete,
        Report,
        MeetingBell,
        VoteCast,
        Eject,
        Alarm,
        SabotageStart,
        Repair,
        UiClick,
        UiBack,
        UiError,
        Win,
        Lose,
        GhostWhisper,
        Elevator,
        Scan,
        Beep,
        Emergency,
        Impact,
        Charge,
        Switch,
        Pickup,
    }

    public static class SoundBank
    {
        private const int Rate = 22050;
        private static readonly Dictionary<Sfx, AudioClip> Cache = new Dictionary<Sfx, AudioClip>();
        private static AudioSource _ui2D;
        private static float _sfxVolume = 1f;

        public static float SfxVolume
        {
            get => _sfxVolume;
            set => _sfxVolume = Mathf.Clamp01(value);
        }

        public static AudioSource Ui2D
        {
            get
            {
                if (_ui2D == null)
                {
                    var go = new GameObject("UiAudio");
                    Object.DontDestroyOnLoad(go);
                    _ui2D = go.AddComponent<AudioSource>();
                    _ui2D.spatialBlend = 0f;
                    _ui2D.playOnAwake = false;
                }
                return _ui2D;
            }
        }

        public static AudioClip Get(Sfx sfx)
        {
            if (Cache.TryGetValue(sfx, out var clip) && clip != null) return clip;
            clip = Synthesise(sfx);
            Cache[sfx] = clip;
            return clip;
        }

        /// <summary>Pre-generates every clip so no effect hitches on first use mid-match.</summary>
        public static void Warmup()
        {
            foreach (Sfx sfx in System.Enum.GetValues(typeof(Sfx))) Get(sfx);
        }

        public static void Play(Sfx sfx, float volume = 1f, float pitch = 1f)
        {
            var src = Ui2D;
            src.pitch = pitch;
            src.PlayOneShot(Get(sfx), Mathf.Clamp01(volume) * _sfxVolume);
        }

        public static void PlayAt(AudioSource source, Sfx sfx, float volume = 1f, float pitchJitter = 0.08f)
        {
            if (source == null) return;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.PlayOneShot(Get(sfx), Mathf.Clamp01(volume) * _sfxVolume);
        }

        public static void PlayAtPoint(Sfx sfx, Vector3 position, float volume = 1f)
        {
            AudioSource.PlayClipAtPoint(Get(sfx), position, Mathf.Clamp01(volume) * _sfxVolume);
        }

        // ------------------------------------------------------------------ synthesis
        private static AudioClip Make(string name, float seconds, System.Func<float, float, float> gen)
        {
            int samples = Mathf.Max(16, Mathf.RoundToInt(seconds * Rate));
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)Rate;
                float p = i / (float)samples;
                data[i] = Mathf.Clamp(gen(t, p), -1f, 1f);
            }
            var clip = AudioClip.Create(name, samples, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Noise() => Random.Range(-1f, 1f);

        private static float Env(float p, float attack, float decay)
        {
            if (p < attack) return p / Mathf.Max(attack, 0.0001f);
            float d = (p - attack) / Mathf.Max(1f - attack, 0.0001f);
            return Mathf.Pow(1f - d, decay);
        }

        private static AudioClip Synthesise(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.Footstep:
                    return Make("sfx_step", 0.11f, (t, p) =>
                        Noise() * Env(p, 0.02f, 5f) * 0.35f +
                        Mathf.Sin(t * 2f * Mathf.PI * 120f) * Env(p, 0.01f, 8f) * 0.22f);

                case Sfx.DoorOpen:
                    return Make("sfx_dooropen", 0.55f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (180f + p * 420f)) * Env(p, 0.06f, 2.2f) * 0.3f +
                        Noise() * Env(p, 0.1f, 3f) * 0.16f);

                case Sfx.DoorClose:
                    return Make("sfx_doorclose", 0.5f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (620f - p * 430f)) * Env(p, 0.03f, 2.4f) * 0.32f +
                        Noise() * Env(p, 0.02f, 6f) * 0.2f);

                case Sfx.Kill:
                    return Make("sfx_kill", 0.75f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (900f - p * 780f)) * Env(p, 0.005f, 2.6f) * 0.4f +
                        Noise() * Env(p, 0.01f, 4.2f) * 0.42f +
                        Mathf.Sin(t * 2f * Mathf.PI * 62f) * Env(p, 0.15f, 1.8f) * 0.3f);

                case Sfx.Vent:
                    return Make("sfx_vent", 0.6f, (t, p) =>
                        Noise() * Env(p, 0.05f, 2.4f) * 0.3f * (0.4f + Mathf.Sin(t * 40f) * 0.3f) +
                        Mathf.Sin(t * 2f * Mathf.PI * (260f + Mathf.Sin(p * 24f) * 120f)) * Env(p, 0.04f, 3f) * 0.22f);

                case Sfx.TaskTick:
                    return Make("sfx_tick", 0.07f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * 1400f) * Env(p, 0.01f, 6f) * 0.22f);

                case Sfx.TaskComplete:
                    return Make("sfx_taskdone", 0.55f, (t, p) =>
                    {
                        float f = p < 0.33f ? 660f : p < 0.66f ? 880f : 1320f;
                        return Mathf.Sin(t * 2f * Mathf.PI * f) * Env(p, 0.02f, 1.6f) * 0.3f;
                    });

                case Sfx.Report:
                    return Make("sfx_report", 0.9f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (330f + Mathf.Sin(p * 26f) * 90f)) * Env(p, 0.02f, 1.6f) * 0.35f +
                        Mathf.Sin(t * 2f * Mathf.PI * 110f) * Env(p, 0.01f, 2.4f) * 0.28f);

                case Sfx.MeetingBell:
                    return Make("sfx_bell", 1.4f, (t, p) =>
                        (Mathf.Sin(t * 2f * Mathf.PI * 523f) * 0.5f +
                         Mathf.Sin(t * 2f * Mathf.PI * 784f) * 0.3f +
                         Mathf.Sin(t * 2f * Mathf.PI * 1046f) * 0.2f) * Env(p, 0.01f, 2.2f) * 0.36f);

                case Sfx.VoteCast:
                    return Make("sfx_vote", 0.2f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (520f + p * 260f)) * Env(p, 0.02f, 3.5f) * 0.28f);

                case Sfx.Eject:
                    return Make("sfx_eject", 1.6f, (t, p) =>
                        Noise() * Env(p, 0.08f, 1.4f) * 0.34f * (1f - p * 0.6f) +
                        Mathf.Sin(t * 2f * Mathf.PI * (140f - p * 110f)) * Env(p, 0.05f, 1.2f) * 0.3f);

                case Sfx.Alarm:
                    return Make("sfx_alarm", 1.2f, (t, p) =>
                    {
                        float wob = Mathf.Sin(t * 2f * Mathf.PI * 3.4f) * 0.5f + 0.5f;
                        return Mathf.Sin(t * 2f * Mathf.PI * (520f + wob * 300f)) * 0.3f * Env(p, 0.02f, 0.6f);
                    });

                case Sfx.SabotageStart:
                    return Make("sfx_sabotage", 1.1f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (90f + p * 40f)) * Env(p, 0.04f, 1.1f) * 0.4f +
                        Noise() * Env(p, 0.02f, 2.2f) * 0.2f);

                case Sfx.Repair:
                    return Make("sfx_repair", 0.35f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (240f + p * 620f)) * Env(p, 0.03f, 2.6f) * 0.28f);

                case Sfx.UiClick:
                    return Make("sfx_click", 0.06f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * 1100f) * Env(p, 0.005f, 7f) * 0.2f);

                case Sfx.UiBack:
                    return Make("sfx_back", 0.09f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (700f - p * 300f)) * Env(p, 0.005f, 6f) * 0.2f);

                case Sfx.UiError:
                    return Make("sfx_error", 0.24f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (200f - p * 60f)) * Env(p, 0.01f, 3f) * 0.3f +
                        Noise() * Env(p, 0.01f, 5f) * 0.1f);

                case Sfx.Win:
                    return Make("sfx_win", 1.8f, (t, p) =>
                    {
                        float[] notes = { 523f, 659f, 784f, 1046f };
                        int i = Mathf.Clamp(Mathf.FloorToInt(p * 4f), 0, 3);
                        float local = (p * 4f) % 1f;
                        return Mathf.Sin(t * 2f * Mathf.PI * notes[i]) * Env(local, 0.03f, 1.8f) * 0.3f;
                    });

                case Sfx.Lose:
                    return Make("sfx_lose", 1.8f, (t, p) =>
                    {
                        float[] notes = { 392f, 349f, 294f, 196f };
                        int i = Mathf.Clamp(Mathf.FloorToInt(p * 4f), 0, 3);
                        float local = (p * 4f) % 1f;
                        return Mathf.Sin(t * 2f * Mathf.PI * notes[i]) * Env(local, 0.05f, 1.6f) * 0.3f;
                    });

                case Sfx.GhostWhisper:
                    return Make("sfx_ghost", 1.0f, (t, p) =>
                        Noise() * Env(p, 0.2f, 1.6f) * 0.12f * (0.5f + Mathf.Sin(t * 9f) * 0.5f) +
                        Mathf.Sin(t * 2f * Mathf.PI * (320f + Mathf.Sin(t * 5f) * 40f)) * Env(p, 0.3f, 1.4f) * 0.1f);

                case Sfx.Elevator:
                    return Make("sfx_elevator", 1.1f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (70f + p * 130f)) * Env(p, 0.15f, 1.3f) * 0.3f +
                        Noise() * Env(p, 0.2f, 2f) * 0.08f);

                case Sfx.Scan:
                    return Make("sfx_scan", 1.5f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (400f + Mathf.Repeat(p * 6f, 1f) * 500f)) *
                        Env(Mathf.Repeat(p * 6f, 1f), 0.05f, 2.5f) * 0.16f);

                case Sfx.Beep:
                    return Make("sfx_beep", 0.12f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * 880f) * Env(p, 0.02f, 3f) * 0.22f);

                case Sfx.Emergency:
                    return Make("sfx_emergency", 1.6f, (t, p) =>
                    {
                        float wob = Mathf.Repeat(t * 2.2f, 1f);
                        return Mathf.Sin(t * 2f * Mathf.PI * (420f + wob * 520f)) * 0.33f * Env(p, 0.02f, 0.7f);
                    });

                case Sfx.Impact:
                    return Make("sfx_impact", 0.32f, (t, p) =>
                        Noise() * Env(p, 0.005f, 5f) * 0.4f +
                        Mathf.Sin(t * 2f * Mathf.PI * 70f) * Env(p, 0.01f, 3f) * 0.36f);

                case Sfx.Charge:
                    return Make("sfx_charge", 0.9f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (140f + p * p * 900f)) * Env(p, 0.25f, 1.2f) * 0.24f);

                case Sfx.Switch:
                    return Make("sfx_switch", 0.1f, (t, p) =>
                        Noise() * Env(p, 0.004f, 8f) * 0.24f +
                        Mathf.Sin(t * 2f * Mathf.PI * 520f) * Env(p, 0.006f, 7f) * 0.16f);

                default: // Pickup
                    return Make("sfx_pickup", 0.18f, (t, p) =>
                        Mathf.Sin(t * 2f * Mathf.PI * (620f + p * 520f)) * Env(p, 0.01f, 3.2f) * 0.24f);
            }
        }
    }
}
