// -----------------------------------------------------------------------------
//  NEBULA NINE - episodic memory of one NPC.
//
//  Everything the agent believes about the match lives here, and *only* things it
//  actually perceived get in (Perception is the gate).  Records carry a
//  confidence that decays with an agent specific half life, so a forgetful NPC
//  genuinely misremembers who was where instead of being artificially nerfed.
//
//  The memory also keeps social records - who accused whom, who defended whom,
//  who voted for whom - because those drive the meeting behaviour just as much as
//  physical sightings do.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;

namespace Nebula.AI
{
    public struct MemoryRecord
    {
        public MemoryKind Kind;
        public int SubjectId;      // the player the record is about
        public int SecondaryId;    // companion / target / accuser
        public int RoomId;
        public DeckId Deck;
        public float Time;
        public float Confidence;
        public int Extra;          // sabotage type, vote target, stage index...

        public bool IsAbout(int playerId) => SubjectId == playerId || SecondaryId == playerId;
    }

    public class AgentMemory
    {
        private readonly List<MemoryRecord> _records = new List<MemoryRecord>(192);
        private readonly int _capacity;
        private readonly float _halfLife;

        /// <summary>Fast lookup: last confident sighting per player.</summary>
        private readonly Dictionary<int, MemoryRecord> _lastSeen = new Dictionary<int, MemoryRecord>();

        public IReadOnlyList<MemoryRecord> Records => _records;

        public AgentMemory(AgentPersonality personality)
        {
            _capacity = Mathf.RoundToInt(Mathf.Lerp(48f, 220f, personality.Memory));
            _halfLife = personality.MemoryHalfLife;
        }

        public void Clear()
        {
            _records.Clear();
            _lastSeen.Clear();
        }

        public void Add(MemoryKind kind, int subject, int secondary, int roomId, DeckId deck, float time,
            float confidence = 1f, int extra = 0)
        {
            var rec = new MemoryRecord
            {
                Kind = kind,
                SubjectId = subject,
                SecondaryId = secondary,
                RoomId = roomId,
                Deck = deck,
                Time = time,
                Confidence = Mathf.Clamp01(confidence),
                Extra = extra,
            };

            _records.Add(rec);
            if (kind == MemoryKind.Sighting || kind == MemoryKind.RoomEntry || kind == MemoryKind.TaskObserved ||
                kind == MemoryKind.VisualTaskProof)
            {
                _lastSeen[subject] = rec;
            }

            if (_records.Count > _capacity) TrimOldest();
        }

        private void TrimOldest()
        {
            // drop the least useful record: oldest with the lowest confidence,
            // but never drop hard evidence (kills, vents, bodies)
            int worst = -1;
            float worstScore = float.MaxValue;
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r.Kind == MemoryKind.KillWitnessed || r.Kind == MemoryKind.VentWitnessed ||
                    r.Kind == MemoryKind.BodySeen || r.Kind == MemoryKind.Ejection) continue;
                float score = r.Confidence * 100f + r.Time;
                if (score < worstScore) { worstScore = score; worst = i; }
            }
            if (worst < 0) worst = 0;
            _records.RemoveAt(worst);
        }

        /// <summary>Confidence decay - called on the slow tick.</summary>
        public void Decay(float dt)
        {
            if (_records.Count == 0) return;
            float factor = Mathf.Exp(-0.693f * dt / Mathf.Max(1f, _halfLife));
            for (int i = _records.Count - 1; i >= 0; i--)
            {
                var r = _records[i];
                // hard evidence fades much more slowly
                bool hard = r.Kind == MemoryKind.KillWitnessed || r.Kind == MemoryKind.VentWitnessed ||
                            r.Kind == MemoryKind.BodySeen || r.Kind == MemoryKind.Ejection ||
                            r.Kind == MemoryKind.VisualTaskProof;
                r.Confidence *= hard ? Mathf.Sqrt(factor) : factor;
                if (r.Confidence < 0.06f && !hard) { _records.RemoveAt(i); continue; }
                _records[i] = r;
            }
        }

        // ------------------------------------------------------------------ queries
        public bool TryLastSeen(int playerId, out MemoryRecord record) => _lastSeen.TryGetValue(playerId, out record);

        public float LastSeenTime(int playerId)
        {
            return _lastSeen.TryGetValue(playerId, out var r) ? r.Time : -999f;
        }

        public int LastSeenRoom(int playerId)
        {
            return _lastSeen.TryGetValue(playerId, out var r) ? r.RoomId : -1;
        }

        /// <summary>How long the agent has had no idea where somebody is.</summary>
        public float TimeUnaccountedFor(int playerId, float now)
        {
            float t = LastSeenTime(playerId);
            return t < -900f ? 999f : now - t;
        }

        public List<int> PlayersSeenNear(int roomId, float fromTime, float toTime)
        {
            var result = new List<int>();
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r.Time < fromTime || r.Time > toTime) continue;
                if (r.RoomId != roomId) continue;
                if (r.Kind != MemoryKind.Sighting && r.Kind != MemoryKind.RoomEntry && r.Kind != MemoryKind.TaskObserved) continue;
                if (!result.Contains(r.SubjectId)) result.Add(r.SubjectId);
            }
            return result;
        }

        public bool WasWithMe(int playerId, float fromTime, float toTime, int selfId)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r.Kind != MemoryKind.Sighting) continue;
                if (r.SubjectId != playerId) continue;
                if (r.Time < fromTime || r.Time > toTime) continue;
                if (r.SecondaryId == selfId || r.SecondaryId < 0) return true;
            }
            return false;
        }

        public int CountOfKind(MemoryKind kind, int subjectId = -1)
        {
            int n = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].Kind != kind) continue;
                if (subjectId >= 0 && _records[i].SubjectId != subjectId) continue;
                n++;
            }
            return n;
        }

        public bool HasHardEvidence(int playerId)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r.SubjectId != playerId) continue;
                if (r.Kind == MemoryKind.KillWitnessed || r.Kind == MemoryKind.VentWitnessed) return true;
            }
            return false;
        }

        public bool SawVisualProof(int playerId)
        {
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].Kind == MemoryKind.VisualTaskProof && _records[i].SubjectId == playerId) return true;
            return false;
        }

        /// <summary>Records used when the agent explains itself in a meeting.</summary>
        public List<MemoryRecord> RecentAbout(int playerId, float since, int max = 8)
        {
            var list = new List<MemoryRecord>();
            for (int i = _records.Count - 1; i >= 0 && list.Count < max; i--)
            {
                if (_records[i].Time < since) break;
                if (_records[i].IsAbout(playerId)) list.Add(_records[i]);
            }
            return list;
        }

        public List<MemoryRecord> SelfTrail(int selfId, float since, int max = 10)
        {
            var list = new List<MemoryRecord>();
            for (int i = _records.Count - 1; i >= 0 && list.Count < max; i--)
            {
                if (_records[i].Time < since) break;
                if (_records[i].Kind == MemoryKind.SelfLocation) list.Add(_records[i]);
            }
            return list;
        }

        /// <summary>Where was I at time t (best effort, used to verify other people's claims about me).</summary>
        public int MyRoomAt(float time, int selfId)
        {
            int best = -1;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].Kind != MemoryKind.SelfLocation) continue;
                float d = Mathf.Abs(_records[i].Time - time);
                if (d < bestDelta) { bestDelta = d; best = _records[i].RoomId; }
            }
            return bestDelta < 22f ? best : -1;
        }

        /// <summary>Where do I believe that player was at time t?</summary>
        public int BelievedRoomAt(int playerId, float time, out float confidence)
        {
            int best = -1;
            float bestDelta = float.MaxValue;
            confidence = 0f;
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r.SubjectId != playerId) continue;
                if (r.Kind != MemoryKind.Sighting && r.Kind != MemoryKind.RoomEntry &&
                    r.Kind != MemoryKind.TaskObserved && r.Kind != MemoryKind.VisualTaskProof) continue;
                float d = Mathf.Abs(r.Time - time);
                if (d < bestDelta)
                {
                    bestDelta = d;
                    best = r.RoomId;
                    confidence = r.Confidence * Mathf.Clamp01(1f - d / 25f);
                }
            }
            return bestDelta < 25f ? best : -1;
        }

        public void RememberAccusation(int accuser, int target, float time)
            => Add(MemoryKind.Accusation, accuser, target, -1, DeckId.Upper, time);

        public void RememberDefense(int defender, int target, float time)
            => Add(MemoryKind.Defense, defender, target, -1, DeckId.Upper, time);

        public void RememberVote(int voter, int target, float time)
            => Add(MemoryKind.Vote, voter, target, -1, DeckId.Upper, time, 1f, target);

        public int TimesAccused(int targetId)
        {
            int n = 0;
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].Kind == MemoryKind.Accusation && _records[i].SecondaryId == targetId) n++;
            return n;
        }

        public int TimesAccuser(int accuserId)
        {
            int n = 0;
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].Kind == MemoryKind.Accusation && _records[i].SubjectId == accuserId) n++;
            return n;
        }

        public int TimesDefended(int defenderId, int targetId)
        {
            int n = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r.Kind == MemoryKind.Defense && r.SubjectId == defenderId && r.SecondaryId == targetId) n++;
            }
            return n;
        }

        public int LastVoteOf(int voterId)
        {
            for (int i = _records.Count - 1; i >= 0; i--)
                if (_records[i].Kind == MemoryKind.Vote && _records[i].SubjectId == voterId) return _records[i].Extra;
            return -2;
        }
    }
}
