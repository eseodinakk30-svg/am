// -----------------------------------------------------------------------------
//  NEBULA NINE - deterministic random helpers.
//
//  A match is seeded once; every subsystem (roles, task assignment, NPC
//  personalities, patrol decisions) pulls from its own stream so that a change
//  in one system does not shuffle the others.  Seeds are shared over the network
//  so host and clients agree on cosmetic randomness.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Core
{
    public class NebulaRandom
    {
        private uint _state;

        public NebulaRandom(int seed)
        {
            _state = (uint)(seed == 0 ? Environment.TickCount : seed);
            if (_state == 0) _state = 0x9E3779B9;
        }

        /// <summary>xorshift32 - fast, deterministic, plenty good for gameplay.</summary>
        public uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) return 0;
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        public float Value01() => (NextUInt() & 0xFFFFFF) / (float)0x1000000;

        public float Range(float min, float max) => min + Value01() * (max - min);

        public bool Chance(float probability) => Value01() < probability;

        /// <summary>Approximately gaussian value in [0,1] centred on <paramref name="mean"/>.</summary>
        public float Gaussian01(float mean, float spread)
        {
            float sum = (Value01() + Value01() + Value01()) / 3f;      // central limit-ish
            float v = mean + (sum - 0.5f) * 2f * spread;
            return Mathf.Clamp01(v);
        }

        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[NextInt(list.Count)];
        }

        public void Shuffle<T>(IList<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Weighted pick; weights need not be normalised. Returns -1 on empty input.</summary>
        public int PickWeighted(IList<float> weights)
        {
            if (weights == null || weights.Count == 0) return -1;
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += Mathf.Max(0f, weights[i]);
            if (total <= 0.0001f) return NextInt(weights.Count);
            float roll = Value01() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= Mathf.Max(0f, weights[i]);
                if (roll <= 0f) return i;
            }
            return weights.Count - 1;
        }

        public Vector2 InsideUnitCircle()
        {
            for (int i = 0; i < 8; i++)
            {
                var v = new Vector2(Range(-1f, 1f), Range(-1f, 1f));
                if (v.sqrMagnitude <= 1f) return v;
            }
            return Vector2.zero;
        }
    }

    public static class MathX
    {
        /// <summary>Logistic squash, used all over the suspicion model.</summary>
        public static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));

        /// <summary>Inverse logistic; converts a probability into log-odds.</summary>
        public static float Logit(float p)
        {
            p = Mathf.Clamp(p, 0.0005f, 0.9995f);
            return Mathf.Log(p / (1f - p));
        }

        public static float Remap(float v, float a, float b, float x, float y)
        {
            if (Mathf.Abs(b - a) < 1e-6f) return x;
            return x + (Mathf.Clamp(v, Mathf.Min(a, b), Mathf.Max(a, b)) - a) * (y - x) / (b - a);
        }

        public static float DecayTo(float current, float target, float rate, float dt)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));
        }

        public static string TimeString(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.CeilToInt(seconds);
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }
    }
}
