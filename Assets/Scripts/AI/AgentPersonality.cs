// -----------------------------------------------------------------------------
//  NEBULA NINE - NPC personality model.
//
//  Thirteen traits in [0,1] drive every downstream system: how far an agent sees,
//  how long it remembers, how strongly evidence moves its suspicion, how often it
//  speaks, how convincing it is, whether it bandwagons, how well it lies.
//
//  Traits come from an archetype (so personalities read as *characters*, not as
//  random noise) plus per-agent jitter, then get scaled by the match difficulty.
//  The module is deliberately standalone: tuning the AI never touches gameplay.
// -----------------------------------------------------------------------------

using UnityEngine;
using Nebula.Core;

namespace Nebula.AI
{
    public enum Archetype
    {
        Analyst,        // careful, quiet, reasons well
        Firebrand,      // aggressive, accuses fast
        Friendly,       // trusting, sociable, forgiving
        Detective,      // gathers a lot of evidence, high logic
        Manipulator,    // superb liar and persuader
        Paranoid,       // suspects nearly everybody
        Follower,       // goes with the majority
        Wanderer,       // task focused, oblivious
        Veteran,        // strategic, patient, hard to fool
        Loudmouth,      // talks constantly, low signal
    }

    public class AgentPersonality
    {
        public Archetype Archetype;
        public string Nickname;

        // --- the thirteen traits -------------------------------------------
        public float Intelligence;      // quality of inference
        public float Memory;            // how long observations stay reliable
        public float Observation;       // sight range / chance to notice
        public float Logic;             // weighting of hard evidence over vibes
        public float Caution;           // avoids risk, stays in groups
        public float Courage;           // willing to go alone, confront
        public float Trust;             // baseline benefit of the doubt
        public float Deceit;            // willingness and skill at lying
        public float Aggression;        // how eagerly it accuses
        public float Sociability;       // how often it speaks
        public float Persuasion;        // how much its words move others
        public float ContradictionSense;// spots inconsistent statements
        public float Strategy;          // long horizon planning

        // --- derived ---------------------------------------------------------
        public float MemoryHalfLife => Mathf.Lerp(28f, 190f, Memory);
        public float VisionScale => Mathf.Lerp(0.78f, 1.16f, Observation);
        public float NoticeChance => Mathf.Lerp(0.55f, 0.995f, Observation);
        public float SpeakUrgency => Mathf.Lerp(0.15f, 1f, Sociability * 0.7f + Aggression * 0.3f);
        public float EvidenceWeight => Mathf.Lerp(0.45f, 1.35f, Logic * 0.65f + Intelligence * 0.35f);
        public float RumourWeight => Mathf.Lerp(0.9f, 0.18f, Logic);
        public float BandwagonBias => Mathf.Lerp(0.05f, 0.85f, (1f - Courage) * 0.5f + (1f - Intelligence) * 0.25f + Sociability * 0.25f);
        public float LieQuality => Mathf.Lerp(0.15f, 0.98f, Deceit * 0.6f + Persuasion * 0.4f);
        public float GroupPreference => Mathf.Lerp(0.05f, 0.95f, Caution * 0.6f + Sociability * 0.4f);
        public float MistakeChance => Mathf.Lerp(0.34f, 0.03f, Intelligence);

        public static AgentPersonality Generate(NebulaRandom rng, Difficulty difficulty, string nickname)
        {
            var archetype = (Archetype)rng.NextInt(System.Enum.GetValues(typeof(Archetype)).Length);
            return Generate(rng, difficulty, nickname, archetype);
        }

        public static AgentPersonality Generate(NebulaRandom rng, Difficulty difficulty, string nickname, Archetype archetype)
        {
            var p = new AgentPersonality { Archetype = archetype, Nickname = nickname };
            float j = 0.13f;   // jitter

            switch (archetype)
            {
                case Archetype.Analyst:
                    p.Set(rng, j, intel: .78f, mem: .80f, obs: .72f, logic: .88f, caution: .80f, courage: .38f,
                        trust: .48f, deceit: .35f, aggression: .22f, social: .30f, persuade: .55f, contra: .82f, strat: .70f);
                    break;
                case Archetype.Firebrand:
                    p.Set(rng, j, intel: .48f, mem: .45f, obs: .55f, logic: .38f, caution: .25f, courage: .85f,
                        trust: .22f, deceit: .45f, aggression: .93f, social: .82f, persuade: .62f, contra: .40f, strat: .32f);
                    break;
                case Archetype.Friendly:
                    p.Set(rng, j, intel: .50f, mem: .50f, obs: .52f, logic: .45f, caution: .45f, courage: .50f,
                        trust: .88f, deceit: .20f, aggression: .18f, social: .88f, persuade: .58f, contra: .32f, strat: .35f);
                    break;
                case Archetype.Detective:
                    p.Set(rng, j, intel: .92f, mem: .90f, obs: .88f, logic: .92f, caution: .55f, courage: .62f,
                        trust: .40f, deceit: .30f, aggression: .45f, social: .58f, persuade: .74f, contra: .90f, strat: .84f);
                    break;
                case Archetype.Manipulator:
                    p.Set(rng, j, intel: .80f, mem: .70f, obs: .70f, logic: .68f, caution: .62f, courage: .72f,
                        trust: .35f, deceit: .96f, aggression: .48f, social: .80f, persuade: .94f, contra: .66f, strat: .86f);
                    break;
                case Archetype.Paranoid:
                    p.Set(rng, j, intel: .58f, mem: .72f, obs: .78f, logic: .40f, caution: .88f, courage: .30f,
                        trust: .08f, deceit: .40f, aggression: .70f, social: .60f, persuade: .40f, contra: .55f, strat: .40f);
                    break;
                case Archetype.Follower:
                    p.Set(rng, j, intel: .42f, mem: .40f, obs: .45f, logic: .35f, caution: .62f, courage: .22f,
                        trust: .70f, deceit: .28f, aggression: .25f, social: .55f, persuade: .30f, contra: .25f, strat: .25f);
                    break;
                case Archetype.Wanderer:
                    p.Set(rng, j, intel: .50f, mem: .35f, obs: .34f, logic: .48f, caution: .30f, courage: .70f,
                        trust: .60f, deceit: .30f, aggression: .20f, social: .25f, persuade: .35f, contra: .28f, strat: .45f);
                    break;
                case Archetype.Veteran:
                    p.Set(rng, j, intel: .86f, mem: .82f, obs: .74f, logic: .80f, caution: .70f, courage: .74f,
                        trust: .38f, deceit: .58f, aggression: .40f, social: .48f, persuade: .78f, contra: .78f, strat: .94f);
                    break;
                default: // Loudmouth
                    p.Set(rng, j, intel: .44f, mem: .42f, obs: .48f, logic: .32f, caution: .35f, courage: .68f,
                        trust: .45f, deceit: .52f, aggression: .62f, social: .96f, persuade: .55f, contra: .34f, strat: .28f);
                    break;
            }

            p.ApplyDifficulty(difficulty, rng);
            return p;
        }

        private void Set(NebulaRandom rng, float jitter, float intel, float mem, float obs, float logic, float caution,
            float courage, float trust, float deceit, float aggression, float social, float persuade, float contra, float strat)
        {
            float J() => rng.Range(-jitter, jitter);
            Intelligence = Mathf.Clamp01(intel + J());
            Memory = Mathf.Clamp01(mem + J());
            Observation = Mathf.Clamp01(obs + J());
            Logic = Mathf.Clamp01(logic + J());
            Caution = Mathf.Clamp01(caution + J());
            Courage = Mathf.Clamp01(courage + J());
            Trust = Mathf.Clamp01(trust + J());
            Deceit = Mathf.Clamp01(deceit + J());
            Aggression = Mathf.Clamp01(aggression + J());
            Sociability = Mathf.Clamp01(social + J());
            Persuasion = Mathf.Clamp01(persuade + J());
            ContradictionSense = Mathf.Clamp01(contra + J());
            Strategy = Mathf.Clamp01(strat + J());
        }

        /// <summary>
        /// Difficulty does not replace the personality, it bends the "skill" traits.
        /// Even at Master the agent keeps a real mistake chance, so it can be fooled.
        /// </summary>
        private void ApplyDifficulty(Difficulty d, NebulaRandom rng)
        {
            float skill;
            switch (d)
            {
                case Difficulty.Easy: skill = -0.32f; break;
                case Difficulty.Normal: skill = -0.12f; break;
                case Difficulty.Hard: skill = 0.05f; break;
                case Difficulty.Expert: skill = 0.20f; break;
                default: skill = 0.33f; break;
            }

            Intelligence = Mathf.Clamp01(Intelligence + skill);
            Memory = Mathf.Clamp01(Memory + skill * 0.85f);
            Observation = Mathf.Clamp01(Observation + skill * 0.75f);
            Logic = Mathf.Clamp01(Logic + skill);
            ContradictionSense = Mathf.Clamp01(ContradictionSense + skill * 0.9f);
            Strategy = Mathf.Clamp01(Strategy + skill);
            Persuasion = Mathf.Clamp01(Persuasion + skill * 0.4f);
            Deceit = Mathf.Clamp01(Deceit + skill * 0.5f);
        }

        public string ArchetypeName
        {
            get
            {
                switch (Archetype)
                {
                    case Archetype.Analyst: return "аналитик";
                    case Archetype.Firebrand: return "заводила";
                    case Archetype.Friendly: return "добряк";
                    case Archetype.Detective: return "следователь";
                    case Archetype.Manipulator: return "манипулятор";
                    case Archetype.Paranoid: return "параноик";
                    case Archetype.Follower: return "конформист";
                    case Archetype.Wanderer: return "работяга";
                    case Archetype.Veteran: return "ветеран";
                    default: return "болтун";
                }
            }
        }

        public string Describe()
        {
            string talk = Sociability > 0.7f ? "много говорит" : Sociability < 0.35f ? "молчаливый" : "говорит по делу";
            string think = Intelligence > 0.75f ? "отлично анализирует" : Intelligence < 0.4f ? "думает поверхностно" : "рассуждает средне";
            string faith = Trust > 0.7f ? "доверчивый" : Trust < 0.3f ? "недоверчивый" : "осторожен в оценках";
            return $"{ArchetypeName}: {talk}, {think}, {faith}";
        }
    }
}
