// -----------------------------------------------------------------------------
//  NEBULA NINE - NPC speech generation.
//
//  Lines are assembled from intent specific template banks with slot filling
//  ({t} target colour, {r} room, {o} third party, {why} the concrete reason from
//  the suspicion model, {n} a number).  Templates are grouped by tone so an
//  aggressive agent and a cautious one never sound the same, and the same intent
//  never produces the same sentence twice in a row.
//
//  The point is not prose quality - it is that every sentence is *derived from
//  the agent's actual evidence*, so reading the chat lets you reconstruct what
//  each NPC believes and why.
// -----------------------------------------------------------------------------

using UnityEngine;
using Nebula.Core;

namespace Nebula.AI
{
    public enum Tone
    {
        Calm,
        Aggressive,
        Friendly,
        Paranoid,
        Cold,
    }

    public struct DialogueContext
    {
        public AgentPersonality Personality;
        public NebulaRandom Rng;
        public string Target;     // {t}
        public string Other;      // {o}
        public string Room;       // {r}
        public string Room2;      // {r2}
        public string Why;        // {why}
        public int Number;        // {n}
        public float Confidence;
    }

    public static class DialogueBank
    {
        public static Tone ToneOf(AgentPersonality p)
        {
            if (p.Aggression > 0.72f) return Tone.Aggressive;
            if (p.Trust < 0.25f) return Tone.Paranoid;
            if (p.Trust > 0.7f && p.Sociability > 0.6f) return Tone.Friendly;
            if (p.Logic > 0.75f && p.Sociability < 0.5f) return Tone.Cold;
            return Tone.Calm;
        }

        // ------------------------------------------------------------------ banks
        private static readonly string[] AccuseCalm =
        {
            "Мне не нравится {t}: {why}.",
            "Я бы посмотрел на {t}. {why}.",
            "{t} выглядит подозрительно — {why}.",
            "У меня вопросы к {t}, {why}.",
            "Если честно, {t} самый странный сейчас: {why}.",
        };

        private static readonly string[] AccuseAggressive =
        {
            "Это {t}, тут и думать нечего. {why}!",
            "{t}, объясняйся. {why}.",
            "Голосуем {t}. {why}, всё очевидно.",
            "Хватит тянуть — {t} предатель. {why}.",
            "{t} палится всю игру. {why}!",
        };

        private static readonly string[] AccuseParanoid =
        {
            "Я весь раунд слежу за {t}. {why}.",
            "Никому не верю, но {t} хуже всех: {why}.",
            "{t} слишком тихо себя ведёт. {why}.",
            "Скажу прямо: {t} меня пугает. {why}.",
        };

        private static readonly string[] AccuseFriendly =
        {
            "Ребят, не хочу никого обижать, но {t}... {why}.",
            "Извини, {t}, но {why}. Объясни, пожалуйста.",
            "Я не уверен, но {t} стоит проверить: {why}.",
        };

        private static readonly string[] AccuseCold =
        {
            "Факт: {why}. Вывод: {t}.",
            "{t}. Основание — {why}.",
            "По совокупности данных подозреваю {t}: {why}.",
        };

        private static readonly string[] DefendSelf =
        {
            "Это не я. Я был в {r}.",
            "Серьёзно? Я всё время в {r} задание делал.",
            "У меня алиби: {r}, там и был.",
            "Проверьте мои задания, я из {r} не выходил.",
            "Я в {r}, могу описать что там чинил.",
        };

        private static readonly string[] DefendSelfAggressive =
        {
            "Не переводи стрелки. Я был в {r}.",
            "Классика: обвинить первого встречного. Я в {r} был.",
            "Ты просто хочешь снять с себя внимание. {r}, весь раунд.",
        };

        private static readonly string[] ClaimAlibi =
        {
            "Я был в {r}.",
            "Я в {r}, делал задание.",
            "Шёл через {r} в {r2}.",
            "С самого начала в {r}.",
            "{r}, потом собирался в {r2}.",
        };

        private static readonly string[] ClaimAlibiWithCompanion =
        {
            "Я был в {r} вместе с {o}.",
            "{o} может подтвердить, мы были в {r}.",
            "Мы с {o} в {r} были, никто не выходил.",
            "Я всё время рядом с {o} в {r}.",
        };

        private static readonly string[] Vouch =
        {
            "{t} чист, он был со мной в {r}.",
            "Я за {t} ручаюсь — вместе шли через {r}.",
            "Не {t}. Я его видел в {r}.",
            "{t} точно не мог, он у меня на глазах был.",
        };

        private static readonly string[] Corroborate =
        {
            "Подтверждаю, {t} был в {r}.",
            "Да, я тоже видел {t} в {r}.",
            "Так и есть, {t} там был.",
        };

        private static readonly string[] Contradict =
        {
            "Стоп. {t} говорит про {r}, а я видел его в {r2}.",
            "Это ложь. {t} был в {r2}, а не в {r}.",
            "{t}, ты только что соврал. {r2}, не {r}.",
            "Не сходится: {t} утверждает {r}, но я видел его в {r2}.",
            "У {t} история не бьётся: {r} против {r2}.",
        };

        private static readonly string[] Question =
        {
            "{t}, где ты был?",
            "{t}, с кем ты был?",
            "Кто-нибудь видел {t}?",
            "{t}, что ты делал в {r}?",
            "Кто был рядом с {r}?",
            "{t}, почему ты один ходишь?",
        };

        private static readonly string[] Answer =
        {
            "Я был в {r}, потом пошёл в {r2}.",
            "В {r}. Один, да, но задания там.",
            "Рядом со мной был {o}.",
            "Я в {r} чинил, потом услышал сбор.",
        };

        private static readonly string[] DemandEvidence =
        {
            "Докажи. Что конкретно ты видел?",
            "Слова без доказательств. Кто ещё видел?",
            "Мне нужны факты, а не ощущения.",
            "Кто может это подтвердить?",
        };

        private static readonly string[] ReportContext =
        {
            "Тело в {r}. Нашёл только что.",
            "{t} мёртв, {r}.",
            "Труп в {r}, рядом никого не было.",
            "Нашёл {t} в {r}, кто там был?",
        };

        private static readonly string[] ChangeMind =
        {
            "Ладно, я передумал. Не {t}.",
            "Согласен, {t} скорее чист.",
            "Беру слова назад про {t}.",
            "Хорошо, тогда {t} — не он.",
        };

        private static readonly string[] Agree =
        {
            "Согласен.",
            "Да, я тоже так думаю.",
            "Поддерживаю.",
            "Логично.",
            "Я с ним.",
        };

        private static readonly string[] Doubt =
        {
            "Не уверен.",
            "Слабое основание, если честно.",
            "Мне это не нравится, но доказательств нет.",
            "Может быть. А может и нет.",
        };

        private static readonly string[] Deflect =
        {
            "Давайте лучше про {t}, он куда подозрительнее.",
            "Мы тратим время. Кто следил за {r}?",
            "Не в ту сторону смотрите.",
            "Меня обсуждать бессмысленно, посмотрите на {t}.",
        };

        private static readonly string[] TaskClaim =
        {
            "Я делал {why} в {r}.",
            "У меня осталось {n} задания.",
            "Закончил задание в {r}, шёл дальше.",
            "Проверьте прогресс — я работаю.",
        };

        private static readonly string[] SabotageNote =
        {
            "Кто чинил {r}? Я туда бежал.",
            "Во время саботажа я был в {r}.",
            "Саботаж выгоден тому, кто был далеко от починки.",
            "Кто НЕ пришёл чинить — тот и предатель.",
        };

        private static readonly string[] VentCall =
        {
            "{t} ВЕНТ! Я видел своими глазами, {r}.",
            "{t} вылез из вентиляции в {r}. Это конец разговора.",
            "Голосуем {t}, он в венте был. {r}.",
        };

        private static readonly string[] VoteCall =
        {
            "Голосуем {t}.",
            "Я за {t}.",
            "Всё, кидаем на {t}.",
            "{t}, без вариантов.",
        };

        private static readonly string[] SkipCall =
        {
            "Пропускаем, информации нет.",
            "Скип. Нельзя выкидывать наугад.",
            "Лучше пропустить и посмотреть следующий раунд.",
            "Скип, иначе выкинем своего.",
        };

        private static readonly string[] Greeting =
        {
            "Что у нас есть?",
            "Рассказывайте, кто где был.",
            "Давайте по порядку.",
            "Тихо. Кто что видел?",
            "Ну и кто на этот раз?",
        };

        private static readonly string[] Silence =
        {
            "...",
            "Молчу, слушаю.",
            "Пока ничего не скажу.",
        };

        private static readonly string[] GhostChatter =
        {
            "Ну вы даёте...",
            "Я же говорил.",
            "Ах если бы вы знали.",
            "Смотреть больно.",
        };

        // ------------------------------------------------------------------ api
        public static string Line(SpeechIntent intent, DialogueContext ctx)
        {
            var tone = ToneOf(ctx.Personality);
            string[] bank;

            switch (intent)
            {
                case SpeechIntent.Accuse:
                    switch (tone)
                    {
                        case Tone.Aggressive: bank = AccuseAggressive; break;
                        case Tone.Paranoid: bank = AccuseParanoid; break;
                        case Tone.Friendly: bank = AccuseFriendly; break;
                        case Tone.Cold: bank = AccuseCold; break;
                        default: bank = AccuseCalm; break;
                    }
                    break;
                case SpeechIntent.Defend:
                    bank = tone == Tone.Aggressive ? DefendSelfAggressive : DefendSelf;
                    break;
                case SpeechIntent.ClaimAlibi:
                    bank = string.IsNullOrEmpty(ctx.Other) ? ClaimAlibi : ClaimAlibiWithCompanion;
                    break;
                case SpeechIntent.Vouch: bank = Vouch; break;
                case SpeechIntent.Corroborate: bank = Corroborate; break;
                case SpeechIntent.Contradict: bank = Contradict; break;
                case SpeechIntent.Question: bank = Question; break;
                case SpeechIntent.Answer: bank = Answer; break;
                case SpeechIntent.DemandEvidence: bank = DemandEvidence; break;
                case SpeechIntent.ReportContext: bank = ReportContext; break;
                case SpeechIntent.ChangeMind: bank = ChangeMind; break;
                case SpeechIntent.Agree: bank = Agree; break;
                case SpeechIntent.Doubt: bank = Doubt; break;
                case SpeechIntent.Deflect: bank = Deflect; break;
                case SpeechIntent.TaskClaim: bank = TaskClaim; break;
                case SpeechIntent.SabotageNote: bank = SabotageNote; break;
                case SpeechIntent.VentCall: bank = VentCall; break;
                case SpeechIntent.VoteCall: bank = VoteCall; break;
                case SpeechIntent.SkipCall: bank = SkipCall; break;
                case SpeechIntent.Greeting: bank = Greeting; break;
                default: bank = Silence; break;
            }

            string template = bank[ctx.Rng != null ? ctx.Rng.NextInt(bank.Length) : Random.Range(0, bank.Length)];
            return Fill(template, ctx);
        }

        public static string GhostLine(NebulaRandom rng)
        {
            return GhostChatter[rng.NextInt(GhostChatter.Length)];
        }

        private static string Fill(string template, DialogueContext ctx)
        {
            var s = template;
            s = s.Replace("{t}", ctx.Target ?? "кто-то");
            s = s.Replace("{o}", ctx.Other ?? "кто-то");
            s = s.Replace("{r2}", ctx.Room2 ?? "другом отсеке");
            s = s.Replace("{r}", ctx.Room ?? "коридоре");
            s = s.Replace("{why}", ctx.Why ?? "странно себя ведёт");
            s = s.Replace("{n}", ctx.Number.ToString());

            // low confidence softens the statement
            if (ctx.Confidence > 0f && ctx.Confidence < 0.42f && ctx.Rng != null && ctx.Rng.Chance(0.45f))
            {
                string[] hedges = { "Возможно, ", "Не уверен, но ", "Кажется, ", "Скорее всего, " };
                s = hedges[ctx.Rng.NextInt(hedges.Length)] + char.ToLowerInvariant(s[0]) + s.Substring(1);
            }
            return s;
        }
    }
}
