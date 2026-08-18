// -----------------------------------------------------------------------------
//  NEBULA NINE - alibi bookkeeping and contradiction detection.
//
//  Every statement made in a meeting ("I was in the Lab with Cobalt") becomes an
//  AlibiClaim.  Each agent keeps its OWN board, because whether a claim looks
//  like a lie depends on what that particular agent remembers and on how good it
//  is at spotting inconsistencies.
//
//  Three checks run on every new claim:
//    1. claim vs. my own memory      - "no, I saw you in the Reactor"
//    2. claim vs. the named companion's claim - mutual stories must match
//    3. claim vs. third party sightings      - somebody else already placed you
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;

namespace Nebula.AI
{
    public class AlibiClaim
    {
        public int SpeakerId;
        public int SubjectId;      // who the claim is about (usually the speaker)
        public int RoomId;
        public int CompanionId = -1;
        public float AboutTime;    // the moment the claim refers to
        public float SaidAt;
        public bool IsVouch;       // "I saw <subject> there" rather than "I was there"
        public bool KnownLie;      // set by the speaker's own brain when it lies deliberately
    }

    public struct AlibiConflict
    {
        public int SuspectId;
        public int RoomId;
        public int ClaimedRoomId;
        public float Strength;
        public float Time;
        public int WitnessId;
    }

    public class AlibiBoard
    {
        private readonly NpcBrain _brain;
        public readonly List<AlibiClaim> Claims = new List<AlibiClaim>();
        public readonly List<AlibiConflict> Contradictions = new List<AlibiConflict>();
        public readonly List<AlibiConflict> Corroborations = new List<AlibiConflict>();

        /// <summary>Claims this agent has already publicly challenged (so it does not repeat itself).</summary>
        private readonly HashSet<int> _challenged = new HashSet<int>();

        public AlibiBoard(NpcBrain brain)
        {
            _brain = brain;
        }

        public void ClearForMeeting()
        {
            Claims.Clear();
            _challenged.Clear();
            // contradictions persist across meetings - a caught lie stays remembered,
            // but their strength is halved so old lies fade behind fresh evidence
            for (int i = Contradictions.Count - 1; i >= 0; i--)
            {
                var c = Contradictions[i];
                c.Strength *= 0.55f;
                if (c.Strength < 0.15f) Contradictions.RemoveAt(i);
                else Contradictions[i] = c;
            }
            Corroborations.Clear();
        }

        public void ResetAll()
        {
            Claims.Clear();
            Contradictions.Clear();
            Corroborations.Clear();
            _challenged.Clear();
        }

        public AlibiClaim ClaimOf(int speakerId)
        {
            for (int i = Claims.Count - 1; i >= 0; i--)
                if (Claims[i].SpeakerId == speakerId && !Claims[i].IsVouch) return Claims[i];
            return null;
        }

        // ==================================================================
        //  registering a heard claim
        // ==================================================================
        public void Register(AlibiClaim claim)
        {
            if (claim == null) return;
            Claims.Add(claim);
            if (_brain?.Owner != null && claim.SpeakerId == _brain.Owner.Id) return;   // do not audit yourself

            var personality = _brain.Personality;
            var memory = _brain.Memory;
            float detect = Mathf.Lerp(0.25f, 1f, personality.ContradictionSense);

            // ---- 1. against my own memory --------------------------------
            int believedRoom = memory.BelievedRoomAt(claim.SubjectId, claim.AboutTime, out float confidence);
            if (believedRoom >= 0 && confidence > 0.35f)
            {
                if (believedRoom == claim.RoomId)
                {
                    AddCorroboration(claim.SubjectId, claim.RoomId, confidence * 0.9f, claim.AboutTime, _brain.Owner.Id);
                }
                else
                {
                    float dist = Map.StationGrid.Instance != null
                        ? Map.StationGrid.Instance.RoomDistance(believedRoom, claim.RoomId)
                        : 40f;
                    if (dist > 18f && _brain.Rng.Chance(detect))
                    {
                        AddContradiction(claim.SubjectId, believedRoom, claim.RoomId,
                            confidence * Mathf.Clamp01(dist / 60f) * detect, claim.AboutTime, _brain.Owner.Id);
                    }
                }
            }

            // ---- 2. against the named companion's story -------------------
            if (claim.CompanionId >= 0)
            {
                var companionClaim = ClaimOf(claim.CompanionId);
                if (companionClaim != null && Mathf.Abs(companionClaim.AboutTime - claim.AboutTime) < 25f)
                {
                    if (companionClaim.RoomId == claim.RoomId)
                    {
                        AddCorroboration(claim.SubjectId, claim.RoomId, 0.85f, claim.AboutTime, claim.CompanionId);
                        AddCorroboration(claim.CompanionId, claim.RoomId, 0.85f, claim.AboutTime, claim.SubjectId);
                    }
                    else if (_brain.Rng.Chance(detect))
                    {
                        // somebody is lying; the agent cannot tell who, so both take a hit,
                        // the speaker slightly more (they made the specific claim)
                        AddContradiction(claim.SubjectId, companionClaim.RoomId, claim.RoomId, 0.75f * detect, claim.AboutTime, claim.CompanionId);
                        AddContradiction(claim.CompanionId, claim.RoomId, companionClaim.RoomId, 0.5f * detect, claim.AboutTime, claim.SubjectId);
                    }
                }
            }

            // ---- 3. against third party statements ------------------------
            for (int i = 0; i < Claims.Count - 1; i++)
            {
                var other = Claims[i];
                if (other.SubjectId != claim.SubjectId) continue;
                if (other.SpeakerId == claim.SpeakerId) continue;
                if (Mathf.Abs(other.AboutTime - claim.AboutTime) > 22f) continue;

                if (other.RoomId == claim.RoomId)
                    AddCorroboration(claim.SubjectId, claim.RoomId, 0.7f, claim.AboutTime, other.SpeakerId);
                else if (_brain.Rng.Chance(detect * 0.9f))
                    AddContradiction(claim.SubjectId, other.RoomId, claim.RoomId, 0.65f * detect, claim.AboutTime, other.SpeakerId);
            }
        }

        private void AddContradiction(int suspect, int seenRoom, int claimedRoom, float strength, float time, int witness)
        {
            if (strength <= 0.05f) return;
            for (int i = 0; i < Contradictions.Count; i++)
            {
                var c = Contradictions[i];
                if (c.SuspectId == suspect && Mathf.Abs(c.Time - time) < 12f)
                {
                    c.Strength = Mathf.Min(2.2f, c.Strength + strength * 0.5f);
                    Contradictions[i] = c;
                    return;
                }
            }
            Contradictions.Add(new AlibiConflict
            {
                SuspectId = suspect, RoomId = seenRoom, ClaimedRoomId = claimedRoom,
                Strength = strength, Time = time, WitnessId = witness,
            });
        }

        private void AddCorroboration(int suspect, int room, float strength, float time, int witness)
        {
            if (strength <= 0.05f) return;
            for (int i = 0; i < Corroborations.Count; i++)
            {
                var c = Corroborations[i];
                if (c.SuspectId == suspect && Mathf.Abs(c.Time - time) < 12f)
                {
                    c.Strength = Mathf.Min(1.8f, c.Strength + strength * 0.4f);
                    Corroborations[i] = c;
                    return;
                }
            }
            Corroborations.Add(new AlibiConflict
            {
                SuspectId = suspect, RoomId = room, ClaimedRoomId = room,
                Strength = strength, Time = time, WitnessId = witness,
            });
        }

        // ==================================================================
        //  dialogue support
        // ==================================================================
        /// <summary>Is there a fresh contradiction this agent has not called out yet?</summary>
        public bool TryGetChallenge(out AlibiConflict conflict)
        {
            conflict = default;
            float best = 0.34f;
            bool found = false;
            for (int i = 0; i < Contradictions.Count; i++)
            {
                var c = Contradictions[i];
                if (_challenged.Contains(c.SuspectId)) continue;
                if (c.Strength <= best) continue;
                best = c.Strength;
                conflict = c;
                found = true;
            }
            return found;
        }

        public void MarkChallenged(int suspectId) => _challenged.Add(suspectId);

        public bool HasCorroborationFor(int playerId)
        {
            for (int i = 0; i < Corroborations.Count; i++)
                if (Corroborations[i].SuspectId == playerId && Corroborations[i].Strength > 0.4f) return true;
            return false;
        }

        public float ContradictionStrength(int playerId)
        {
            float total = 0f;
            for (int i = 0; i < Contradictions.Count; i++)
                if (Contradictions[i].SuspectId == playerId) total += Contradictions[i].Strength;
            return total;
        }

        /// <summary>True when somebody publicly claimed to have been with me and it is false.</summary>
        public bool SomebodyMisquotedMe(int selfId, out AlibiClaim claim)
        {
            claim = null;
            var memory = _brain.Memory;
            for (int i = 0; i < Claims.Count; i++)
            {
                var c = Claims[i];
                if (c.SpeakerId == selfId) continue;
                if (c.CompanionId != selfId && c.SubjectId != selfId) continue;
                int myRoom = memory.MyRoomAt(c.AboutTime, selfId);
                if (myRoom >= 0 && myRoom != c.RoomId)
                {
                    claim = c;
                    return true;
                }
            }
            return false;
        }
    }
}
