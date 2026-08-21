// -----------------------------------------------------------------------------
//  NEBULA NINE - suspicion as accumulated evidence, not as a dice roll.
//
//  Each agent keeps a log-odds score per player.  Every piece of evidence in
//  memory contributes a weight (positive = more likely an infiltrator), scaled by
//  the record's confidence and by the agent's own traits (a paranoid agent starts
//  from a harsher prior, a logical agent leans on hard evidence and discounts
//  hearsay, a trusting agent forgives circumstantial proximity).
//
//  Probability = sigmoid(logOdds).  The strongest contributing term is kept so
//  the dialogue system can say *why* it suspects somebody, in concrete terms.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Gameplay;

namespace Nebula.AI
{
    public enum EvidenceKind
    {
        None,
        SawKill,
        SawVent,
        AloneWithVictim,
        NearCrimeScene,
        UnaccountedFor,
        SelfReport,
        Contradiction,
        DefendedInfiltrator,
        PushedInnocent,
        NearSabotage,
        AlwaysAlone,
        VotePattern,
        Corroborated,
        VisualTask,
        WithMe,
        TaskSeen,
        Cleared,
        Silent,
    }

    public struct SuspicionEntry
    {
        public float LogOdds;
        public EvidenceKind TopReason;
        public float TopWeight;
        public int TopRoom;
        public float TopTime;
        public int TopWitness;

        public float Probability => MathX.Sigmoid(LogOdds);
    }

    public class SuspicionModel
    {
        private readonly NpcBrain _brain;
        private readonly Dictionary<int, SuspicionEntry> _entries = new Dictionary<int, SuspicionEntry>();
        private readonly Dictionary<int, float> _trust = new Dictionary<int, float>();

        public SuspicionModel(NpcBrain brain)
        {
            _brain = brain;
        }

        public IReadOnlyDictionary<int, SuspicionEntry> Entries => _entries;

        public void Reset()
        {
            _entries.Clear();
            _trust.Clear();
        }

        public float Probability(int playerId) => _entries.TryGetValue(playerId, out var e) ? e.Probability : BasePrior();

        public SuspicionEntry Entry(int playerId) => _entries.TryGetValue(playerId, out var e) ? e : default;

        /// <summary>Social trust built up over the match (independent of hard evidence).</summary>
        public float Trust(int playerId) => _trust.TryGetValue(playerId, out var t) ? t : 0f;

        public void AddTrust(int playerId, float delta)
        {
            _trust[playerId] = Mathf.Clamp(Trust(playerId) + delta, -3f, 3f);
        }

        private float BasePrior()
        {
            var match = _brain.Match;
            if (match == null) return -1.6f;
            int alive = Mathf.Max(2, match.AliveCount());
            int inf = Mathf.Max(1, match.Settings.InfiltratorCount);
            float p = Mathf.Clamp(inf / (float)alive, 0.05f, 0.45f);
            return MathX.Logit(p);
        }

        // ==================================================================
        //  full re-evaluation from memory
        // ==================================================================
        public void Evaluate()
        {
            var match = _brain.Match;
            if (match == null) return;
            var self = _brain.Owner;
            var personality = _brain.Personality;
            var memory = _brain.Memory;
            float now = match.MatchTime;

            float prior = BasePrior();
            // paranoid agents start everybody higher, trusting agents lower
            prior += Mathf.Lerp(0.9f, -0.9f, personality.Trust);

            var scratch = new Dictionary<int, SuspicionEntry>();
            foreach (var p in match.Players)
            {
                if (p.Id == self.Id) continue;
                scratch[p.Id] = new SuspicionEntry { LogOdds = prior, TopReason = EvidenceKind.None };
            }

            var records = memory.Records;

            // Правила лобби — общедоступная информация, а не подсмотренная роль:
            // если в матче есть инженеры, то нырок в вентиляцию сам по себе уже
            // не приговор. Без этой поправки инженера выкидывали в первое же
            // собрание за использование собственного умения.
            float ventWeight = match.Settings != null && match.Settings.EngineerCount > 0 ? 2.1f : 5.2f;

            // --- crime scenes: where and roughly when bodies turned up -------
            var crimeScenes = new List<MemoryRecord>();
            for (int i = 0; i < records.Count; i++)
                if (records[i].Kind == MemoryKind.BodySeen || records[i].Kind == MemoryKind.BodyReported)
                    crimeScenes.Add(records[i]);

            // --- direct evidence -------------------------------------------
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                switch (r.Kind)
                {
                    case MemoryKind.KillWitnessed:
                        Bump(scratch, r.SubjectId, 6.4f * r.Confidence, EvidenceKind.SawKill, r);
                        break;

                    case MemoryKind.VentWitnessed:
                        Bump(scratch, r.SubjectId, ventWeight * r.Confidence, EvidenceKind.SawVent, r);
                        break;

                    case MemoryKind.VisualTaskProof:
                        Bump(scratch, r.SubjectId, -2.6f * r.Confidence * personality.EvidenceWeight, EvidenceKind.VisualTask, r);
                        break;

                    case MemoryKind.Alone:
                        // "I was alone in a room with X" - matters only if X later turns out near a body
                        Bump(scratch, r.SubjectId, 0.10f * r.Confidence, EvidenceKind.AlwaysAlone, r);
                        break;

                    case MemoryKind.TaskObserved:
                        Bump(scratch, r.SubjectId, -0.16f * r.Confidence * personality.RumourWeight, EvidenceKind.TaskSeen, r);
                        break;

                    case MemoryKind.Sighting:
                        if (r.SecondaryId == self.Id)
                            Bump(scratch, r.SubjectId, -0.09f * r.Confidence, EvidenceKind.WithMe, r);
                        break;

                    case MemoryKind.Ejection:
                        if (r.Extra == 0) PunishPushers(scratch, r.SubjectId, personality);   // ejected a crewmate
                        break;
                }
            }

            // --- proximity to each crime scene ------------------------------
            foreach (var scene in crimeScenes)
            {
                float windowStart = scene.Time - 34f;
                float windowEnd = scene.Time + 2f;

                foreach (var kv in new List<int>(scratch.Keys))
                {
                    int room = memory.BelievedRoomAt(kv, scene.Time - 6f, out float conf);
                    if (room < 0) continue;

                    if (room == scene.RoomId)
                    {
                        Bump(scratch, kv, 1.35f * conf * personality.EvidenceWeight, EvidenceKind.NearCrimeScene,
                            new MemoryRecord { RoomId = scene.RoomId, Time = scene.Time, SubjectId = kv });
                    }
                    else
                    {
                        float dist = Map.StationGrid.Instance != null
                            ? Map.StationGrid.Instance.RoomDistance(room, scene.RoomId)
                            : 50f;
                        if (dist < 22f)
                            Bump(scratch, kv, 0.42f * conf * personality.EvidenceWeight, EvidenceKind.NearCrimeScene,
                                new MemoryRecord { RoomId = room, Time = scene.Time, SubjectId = kv });
                        else if (dist > 55f)
                            Bump(scratch, kv, -0.55f * conf * personality.EvidenceWeight, EvidenceKind.Cleared,
                                new MemoryRecord { RoomId = room, Time = scene.Time, SubjectId = kv });
                    }
                }

                // whoever was alone with the victim shortly before is a prime suspect
                for (int i = 0; i < records.Count; i++)
                {
                    var r = records[i];
                    if (r.Kind != MemoryKind.Sighting) continue;
                    if (r.Time < windowStart || r.Time > windowEnd) continue;
                    if (r.SecondaryId != scene.SubjectId) continue;
                    if (r.SubjectId == scene.SubjectId) continue;
                    Bump(scratch, r.SubjectId, 1.15f * r.Confidence * personality.EvidenceWeight,
                        EvidenceKind.AloneWithVictim, r);
                }

                // self reporting is mildly suspicious, strongly so for paranoid agents
                for (int i = 0; i < records.Count; i++)
                {
                    var r = records[i];
                    if (r.Kind != MemoryKind.BodyReported) continue;
                    if (r.SecondaryId != scene.SubjectId) continue;
                    float w = Mathf.Lerp(0.12f, 0.75f, 1f - personality.Trust);
                    Bump(scratch, r.SubjectId, w, EvidenceKind.SelfReport, r);
                }
            }

            // --- кто отмалчивается ------------------------------------------
            // Живой участник, не сказавший ни слова за собрание, вызывает вопросы —
            // ровно как у людей. Без этого игрок, который просто не пишет в чат,
            // не набирал подозрений вовсе, и его никогда не обвиняли.
            var meeting = match.Meeting;
            if (meeting != null && meeting.Chat != null && meeting.Chat.Count >= 6)
            {
                var spoke = new HashSet<int>();
                for (int i = 0; i < meeting.Chat.Count; i++) spoke.Add(meeting.Chat[i].SpeakerId);

                foreach (var kv in new List<int>(scratch.Keys))
                {
                    if (kv == self.Id || spoke.Contains(kv)) continue;
                    var quiet = match != null ? match.PlayerById(kv) : null;
                    if (quiet == null || !quiet.IsAlive) continue;
                    Bump(scratch, kv, 0.55f * Mathf.Lerp(0.4f, 1.3f, 1f - personality.Trust),
                         EvidenceKind.Silent, new MemoryRecord { SubjectId = kv, Time = now, RoomId = -1 });
                }
            }

            // --- who has been off the radar --------------------------------
            foreach (var kv in new List<int>(scratch.Keys))
            {
                float unseen = memory.TimeUnaccountedFor(kv, now);
                if (unseen > 45f && crimeScenes.Count > 0)
                    Bump(scratch, kv, Mathf.Min(0.8f, (unseen - 45f) / 90f) * personality.Caution,
                        EvidenceKind.UnaccountedFor, new MemoryRecord { SubjectId = kv, Time = now, RoomId = -1 });

                // sabotages that happened while nobody could vouch for them
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].Kind != MemoryKind.SabotageStarted) continue;
                    int room = memory.BelievedRoomAt(kv, records[i].Time, out float conf);
                    if (room < 0)
                        Bump(scratch, kv, 0.16f * personality.Caution, EvidenceKind.NearSabotage, records[i]);
                }
            }

            // --- statements made during meetings ---------------------------
            var board = _brain.Alibis;
            if (board != null)
            {
                foreach (var contradiction in board.Contradictions)
                {
                    float weight = 1.25f * personality.ContradictionSense * contradiction.Strength;
                    Bump(scratch, contradiction.SuspectId, weight, EvidenceKind.Contradiction,
                        new MemoryRecord { SubjectId = contradiction.SuspectId, RoomId = contradiction.RoomId, Time = contradiction.Time });
                }
                foreach (var corroboration in board.Corroborations)
                {
                    float weight = -1.05f * Mathf.Lerp(0.5f, 1.15f, personality.Logic) * corroboration.Strength;
                    Bump(scratch, corroboration.SuspectId, weight, EvidenceKind.Corroborated,
                        new MemoryRecord { SubjectId = corroboration.SuspectId, RoomId = corroboration.RoomId, Time = corroboration.Time });
                }
            }

            // --- social signals --------------------------------------------
            foreach (var kv in new List<int>(scratch.Keys))
            {
                int defendedInf = 0;
                for (int i = 0; i < records.Count; i++)
                {
                    var r = records[i];
                    if (r.Kind == MemoryKind.Defense && r.SubjectId == kv)
                    {
                        // did the defended player turn out to be an infiltrator?
                        for (int j = 0; j < records.Count; j++)
                        {
                            if (records[j].Kind == MemoryKind.Ejection && records[j].SubjectId == r.SecondaryId && records[j].Extra == 1)
                                defendedInf++;
                        }
                    }
                }
                if (defendedInf > 0)
                    Bump(scratch, kv, 0.85f * defendedInf * personality.Logic, EvidenceKind.DefendedInfiltrator,
                        new MemoryRecord { SubjectId = kv, Time = now, RoomId = -1 });

                // aggressive players who accuse everybody get a small penalty from
                // logical agents (noise), and a small bonus from followers (leadership)
                int accusations = memory.TimesAccuser(kv);
                if (accusations >= 3)
                {
                    float w = Mathf.Lerp(-0.25f, 0.45f, personality.Logic);
                    Bump(scratch, kv, w, EvidenceKind.PushedInnocent, new MemoryRecord { SubjectId = kv, Time = now, RoomId = -1 });
                }

                // accumulated personal trust
                float trust = Trust(kv);
                if (Mathf.Abs(trust) > 0.01f)
                {
                    var e = scratch[kv];
                    e.LogOdds -= trust * 0.55f;
                    scratch[kv] = e;
                }
            }

            // --- imperfection: agents are not solvers -----------------------
            float noise = personality.MistakeChance;
            foreach (var key in new List<int>(scratch.Keys))
            {
                var e = scratch[key];
                e.LogOdds += _brain.Rng.Range(-1f, 1f) * noise * 1.5f;
                e.LogOdds = Mathf.Clamp(e.LogOdds, -8f, 9f);
                scratch[key] = e;
            }

            // dead and ejected players do not matter any more
            foreach (var p in match.Players)
            {
                if (p.IsAlive) continue;
                scratch.Remove(p.Id);
            }

            _entries.Clear();
            foreach (var kv in scratch) _entries[kv.Key] = kv.Value;
        }

        private void PunishPushers(Dictionary<int, SuspicionEntry> scratch, int ejectedId, AgentPersonality personality)
        {
            // an ejected crewmate means whoever pushed hardest for it looks worse
            var memory = _brain.Memory;
            foreach (var key in new List<int>(scratch.Keys))
            {
                if (memory.LastVoteOf(key) != ejectedId) continue;
                Bump(scratch, key, 0.5f * personality.Logic, EvidenceKind.VotePattern,
                    new MemoryRecord { SubjectId = key, Time = 0f, RoomId = -1 });
            }
        }

        private static void Bump(Dictionary<int, SuspicionEntry> scratch, int playerId, float weight,
            EvidenceKind kind, MemoryRecord source)
        {
            if (!scratch.TryGetValue(playerId, out var e)) return;
            e.LogOdds += weight;
            if (Mathf.Abs(weight) > Mathf.Abs(e.TopWeight))
            {
                e.TopWeight = weight;
                e.TopReason = kind;
                e.TopRoom = source.RoomId;
                e.TopTime = source.Time;
                e.TopWitness = source.SecondaryId;
            }
            scratch[playerId] = e;
        }

        // ==================================================================
        //  ranking helpers used by voting and dialogue
        // ==================================================================
        public int MostSuspicious(out float probability, int exclude = -1)
        {
            int best = -1;
            probability = 0f;
            foreach (var kv in _entries)
            {
                if (kv.Key == exclude) continue;
                float p = kv.Value.Probability;
                if (p > probability) { probability = p; best = kv.Key; }
            }
            return best;
        }

        public int LeastSuspicious(out float probability)
        {
            int best = -1;
            probability = 1f;
            foreach (var kv in _entries)
            {
                float p = kv.Value.Probability;
                if (p < probability) { probability = p; best = kv.Key; }
            }
            return best;
        }

        public List<KeyValuePair<int, SuspicionEntry>> Ranked()
        {
            var list = new List<KeyValuePair<int, SuspicionEntry>>(_entries);
            list.Sort((a, b) => b.Value.Probability.CompareTo(a.Value.Probability));
            return list;
        }

        /// <summary>Human readable reason, used verbatim by the dialogue generator.</summary>
        public static string ReasonText(EvidenceKind kind, int roomId)
        {
            string room = roomId >= 0 ? Map.StationLayout.NameOf(roomId) : "";
            switch (kind)
            {
                case EvidenceKind.SawKill: return $"я видел убийство в {room}";
                case EvidenceKind.SawVent: return $"я видел, как он лез в вентиляцию в {room}";
                case EvidenceKind.AloneWithVictim: return "он последний был рядом с погибшим";
                case EvidenceKind.NearCrimeScene: return $"он крутился возле {room}";
                case EvidenceKind.UnaccountedFor: return "его вообще никто не видел";
                case EvidenceKind.SelfReport: return "он сам нашёл тело — удобно";
                case EvidenceKind.Contradiction: return "его слова не сходятся";
                case EvidenceKind.DefendedInfiltrator: return "он выгораживал предателя";
                case EvidenceKind.PushedInnocent: return "он давит на всех подряд";
                case EvidenceKind.NearSabotage: return "во время саботажа его не было видно";
                case EvidenceKind.AlwaysAlone: return "он постоянно ходит один";
                case EvidenceKind.VotePattern: return "он голосовал за невиновного";
                case EvidenceKind.Corroborated: return "его подтвердили";
                case EvidenceKind.VisualTask: return "я видел, как он делал визуальное задание";
                case EvidenceKind.WithMe: return "он был со мной";
                case EvidenceKind.TaskSeen: return "он работал у консоли";
                case EvidenceKind.Cleared: return "он был далеко";
                case EvidenceKind.Silent: return "он отмалчивается всё собрание";
                default: return "просто ощущение";
            }
        }
    }
}
