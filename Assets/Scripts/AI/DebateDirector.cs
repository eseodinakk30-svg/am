// -----------------------------------------------------------------------------
//  NEBULA NINE - meeting conversation director.
//
//  Every alive NPC proposes what it *wants* to say (an intent plus the concrete
//  evidence behind it, with a priority).  The director runs an auction each beat:
//  the most urgent speaker gets the floor, the sentence is rendered, and then all
//  agents hear it - which registers alibi claims, updates trust, arms replies and
//  can flip somebody's mind mid meeting.
//
//  Voting is driven from here too: agents commit at their own pace, watch the
//  tally, and the bandwagon-prone ones change their minds when the room moves.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Gameplay;

namespace Nebula.AI
{
    public struct SpeechAct
    {
        public SpeechIntent Intent;
        public int TargetId;
        public int OtherId;
        public int RoomId;
        public int Room2Id;
        public string Why;
        public float Priority;
        public float Confidence;
        public bool IsLie;
        public float AboutTime;

        public static SpeechAct None => new SpeechAct { Intent = SpeechIntent.None, TargetId = -1, OtherId = -1, RoomId = -1, Room2Id = -1 };
    }

    public class DebateDirector
    {
        private MatchManager _match;
        private MeetingState _meeting;
        private NebulaRandom _rng;

        private float _beatTimer;
        private int _lastSpeaker = -1;
        private int _linesThisMeeting;
        private readonly Dictionary<int, float> _voteDelay = new Dictionary<int, float>();
        private readonly HashSet<int> _voted = new HashSet<int>();
        private bool _votingActive;
        private float _sinceVotingStart;

        public int LinesSpoken => _linesThisMeeting;

        public void Begin(MatchManager match, MeetingState meeting)
        {
            _match = match;
            _meeting = meeting;
            _rng = new NebulaRandom((int)(Time.time * 1000f) ^ meeting.MeetingNumber * 7919);
            _beatTimer = 1.4f;
            _lastSpeaker = -1;
            _linesThisMeeting = 0;
            _voteDelay.Clear();
            _voted.Clear();
            _votingActive = false;
            _sinceVotingStart = 0f;

            // the reporter frames the meeting
            if (meeting.BodyVictim != null && meeting.Caller != null)
            {
                var ctx = new DialogueContext
                {
                    Personality = BrainOf(meeting.Caller)?.Personality ?? AgentPersonality.Generate(_rng, Difficulty.Normal, "x"),
                    Rng = _rng,
                    Target = meeting.BodyVictim.ColorName,
                    Room = Map.StationLayout.NameOf(meeting.BodyVictim.BodyRoomId),
                };
                if (meeting.Caller.IsBot)
                    Speak(meeting.Caller, SpeechIntent.ReportContext, DialogueBank.Line(SpeechIntent.ReportContext, ctx), -1);
            }
        }

        public void BeginDiscussion()
        {
            _beatTimer = 0.8f;
        }

        public void BeginVoting()
        {
            _votingActive = true;
            _sinceVotingStart = 0f;
            float window = Mathf.Max(4f, _match.Settings.VotingTime * 0.72f);

            foreach (var p in _match.Players)
            {
                if (!p.IsAlive || !p.IsBot) continue;
                var brain = BrainOf(p);
                float eagerness = brain != null ? brain.Personality.Aggression * 0.5f + brain.Personality.Courage * 0.5f : 0.5f;
                // confident, aggressive agents vote early; cautious ones wait to see the room
                _voteDelay[p.Id] = Mathf.Lerp(window * 0.75f, 1.2f, eagerness) + _rng.Range(0f, window * 0.25f);
            }
        }

        private NpcBrain BrainOf(PlayerState p) => p?.Brain as NpcBrain;

        // ==================================================================
        public void Tick(float dt)
        {
            if (_match == null || _meeting == null) return;

            TickConversation(dt);
            if (_votingActive) TickVoting(dt);
        }

        private void TickConversation(float dt)
        {
            _beatTimer -= dt;
            if (_beatTimer > 0f) return;

            // Реплика раз в две секунды читалась как спам: пока разберёшь одну,
            // прилетают ещё три. Живые люди пишут заметно медленнее, да и успеть
            // прочитать надо. К концу обсуждения темп сам ускоряется — как перед
            // закрытием голосования.
            float urgency = _meeting != null && _match.PhaseTimer > 0f
                ? Mathf.InverseLerp(18f, 4f, _match.PhaseTimer) : 0f;
            float pace = _match.Phase == MatchPhase.Voting ? 4.6f : 4.0f;
            pace = Mathf.Lerp(pace, pace * 0.62f, Mathf.Clamp01(urgency));
            _beatTimer = pace + _rng.Range(-0.8f, 1.6f);

            var best = SpeechAct.None;
            PlayerState bestSpeaker = null;
            float bestScore = 0.16f;

            foreach (var p in _match.Players)
            {
                if (!p.IsAlive || !p.IsBot) continue;
                if (p.Id == _lastSpeaker) continue;
                var brain = BrainOf(p);
                if (brain == null) continue;

                var act = brain.ProposeSpeech(_meeting);
                if (act.Intent == SpeechIntent.None) continue;

                float score = act.Priority * Mathf.Lerp(0.55f, 1.35f, brain.Personality.SpeakUrgency);
                score *= _rng.Range(0.82f, 1.18f);
                // к кому обратились — тот и отвечает: без этого прямой вопрос
                // игрока тонул в общей очереди
                score *= 1f + brain.SpeakUrge;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = act;
                    bestSpeaker = p;
                }
            }

            if (bestSpeaker == null)
            {
                // ghosts gossip on their own channel so the meeting never feels dead
                if (_rng.Chance(0.3f)) GhostChatter();
                return;
            }

            var brainSpeaking = BrainOf(bestSpeaker);
            string text = Render(brainSpeaking, best);
            Speak(bestSpeaker, best.Intent, text, best.TargetId);
            brainSpeaking.OnSpoke(best);

            // broadcast to everybody who can hear (i.e. everyone in the meeting)
            foreach (var p in _match.Players)
            {
                var listener = BrainOf(p);
                if (listener == null || p.Id == bestSpeaker.Id) continue;
                listener.HearSpeech(bestSpeaker, best, text);
            }
        }

        private string Render(NpcBrain brain, SpeechAct act)
        {
            var target = _match.PlayerById(act.TargetId);
            var other = _match.PlayerById(act.OtherId);
            var ctx = new DialogueContext
            {
                Personality = brain.Personality,
                Rng = brain.Rng,
                Target = target != null ? target.ColorName : "кто-то",
                Other = other != null ? other.ColorName : null,
                Room = act.RoomId >= 0 ? Map.StationLayout.NameOf(act.RoomId) : "коридоре",
                Room2 = act.Room2Id >= 0 ? Map.StationLayout.NameOf(act.Room2Id) : "другом отсеке",
                Why = act.Why,
                Confidence = act.Confidence,
                Number = Mathf.Max(1, Mathf.RoundToInt(act.Priority * 3f)),
            };
            return DialogueBank.Line(act.Intent, ctx);
        }

        private void Speak(PlayerState speaker, SpeechIntent intent, string text, int targetId)
        {
            if (speaker == null || string.IsNullOrEmpty(text)) return;
            _match.AddChat(speaker, text, intent);
            _lastSpeaker = speaker.Id;
            _linesThisMeeting++;
        }

        private void GhostChatter()
        {
            var ghosts = new List<PlayerState>();
            foreach (var p in _match.Players)
                if (p.IsGhost && p.IsBot) ghosts.Add(p);
            if (ghosts.Count == 0) return;
            var g = ghosts[_rng.NextInt(ghosts.Count)];
            var brain = BrainOf(g);
            if (brain == null) return;
            _match.AddChat(g, DialogueBank.GhostLine(brain.Rng), SpeechIntent.None, true);
        }

        // ==================================================================
        //  voting
        // ==================================================================
        private void TickVoting(float dt)
        {
            _sinceVotingStart += dt;

            foreach (var p in _match.Players)
            {
                if (!p.IsAlive || !p.IsBot) continue;
                if (_voted.Contains(p.Id)) continue;
                if (!_voteDelay.TryGetValue(p.Id, out float delay)) continue;
                if (_sinceVotingStart < delay) continue;

                var brain = BrainOf(p);
                if (brain == null) continue;

                int target = brain.DecideVote(CurrentTally());
                if (_match.CastVote(p, target))
                {
                    _voted.Add(p.Id);
                    AnnounceVote(p, brain, target);
                }
            }
        }

        private void AnnounceVote(PlayerState voter, NpcBrain brain, int target)
        {
            // only outspoken agents narrate their vote
            if (!brain.Rng.Chance(brain.Personality.Sociability * 0.55f)) return;
            var act = new SpeechAct
            {
                Intent = target < 0 ? SpeechIntent.SkipCall : SpeechIntent.VoteCall,
                TargetId = target,
                OtherId = -1,
                RoomId = -1,
                Room2Id = -1,
                Priority = 0.4f,
                Confidence = 0.8f,
            };
            Speak(voter, act.Intent, Render(brain, act), target);
        }

        /// <summary>Public tally so bandwagon logic can react to the room.</summary>
        public Dictionary<int, int> CurrentTally()
        {
            var tally = new Dictionary<int, int>();
            foreach (var kv in _meeting.Votes)
            {
                if (!tally.ContainsKey(kv.Value)) tally[kv.Value] = 0;
                tally[kv.Value]++;
            }
            return tally;
        }

        /// <summary>Called when the human player types something in the meeting chat.</summary>
        public void OnPlayerChat(PlayerState speaker, string text)
        {
            if (_match == null || speaker == null || string.IsNullOrWhiteSpace(text)) return;
            var act = ParsePlayerLine(speaker, text);
            foreach (var p in _match.Players)
            {
                var listener = BrainOf(p);
                if (listener == null || p.Id == speaker.Id) continue;
                listener.HearSpeech(speaker, act, text);
            }

            // Реплика игрока раньше только меняла внутреннее состояние агентов, а
            // очередь высказываний шла своим темпом раз в четыре секунды — со
            // стороны выходило, что игроку никто не отвечает. Теперь его сообщение
            // сдвигает очередь: кто-то отзовётся через секунду-полторы.
            _beatTimer = Mathf.Min(_beatTimer, _rng.Range(0.9f, 1.9f));

            // и тот, к кому обратились, получает право голоса вне очереди
            if (act.TargetId >= 0 && _lastSpeaker == act.TargetId) _lastSpeaker = -1;
        }

        /// <summary>
        /// Very small natural language understanding: NPCs react to colour names,
        /// room names and a handful of keywords the player is likely to use.
        /// </summary>
        private SpeechAct ParsePlayerLine(PlayerState speaker, string text)
        {
            var act = SpeechAct.None;
            act.Confidence = 0.6f;
            act.Priority = 0.5f;
            string lower = text.ToLowerInvariant();

            // find a mentioned player by colour name
            foreach (var p in _match.Players)
            {
                if (p.Id == speaker.Id) continue;
                if (lower.Contains(p.ColorName.ToLowerInvariant()) ||
                    (!string.IsNullOrEmpty(p.Name) && lower.Contains(p.Name.ToLowerInvariant())))
                {
                    act.TargetId = p.Id;
                    break;
                }
            }

            // find a mentioned room
            foreach (var area in Map.StationLayout.AllRooms())
            {
                if (area.Name != null && lower.Contains(area.Name.ToLowerInvariant()))
                {
                    act.RoomId = area.Id;
                    break;
                }
            }

            bool accuses = lower.Contains("предател") || lower.Contains("это ") || lower.Contains("голос") ||
                           lower.Contains("вент") || lower.Contains("убил") || lower.Contains("подозр");
            bool defends = lower.Contains("я был") || lower.Contains("я в ") || lower.Contains("не я") ||
                           lower.Contains("алиби");
            bool vouches = lower.Contains("со мной") || lower.Contains("подтвержд") || lower.Contains("чист");
            bool skip = lower.Contains("скип") || lower.Contains("пропуск");

            if (lower.Contains("вент") && act.TargetId >= 0) act.Intent = SpeechIntent.VentCall;
            else if (accuses && act.TargetId >= 0) act.Intent = SpeechIntent.Accuse;
            else if (vouches && act.TargetId >= 0) act.Intent = SpeechIntent.Vouch;
            else if (defends) act.Intent = SpeechIntent.ClaimAlibi;
            else if (skip) act.Intent = SpeechIntent.SkipCall;
            else if (act.TargetId >= 0 && lower.Contains("?")) act.Intent = SpeechIntent.Question;
            else act.Intent = SpeechIntent.Doubt;

            if (act.Intent == SpeechIntent.ClaimAlibi && act.RoomId < 0) act.RoomId = speaker.RoomId;
            act.AboutTime = _match.MatchTime;
            return act;
        }
    }
}
