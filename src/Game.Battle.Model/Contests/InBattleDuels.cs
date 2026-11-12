using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Skills;
using Game.Data.Defs;
using Game.Foundation.Math;
using Game.Foundation.Random;

namespace Game.Battle.Model.Contests
{
    /// <summary>전투 중 일기토 하나 (battle.md 2.7.5, R-71). 결과가 날 때까지 두 군단은 서로 싸우지 않고 그 자리에 선다.</summary>
    public sealed class DuelState
    {
        public string ContestId { get; internal set; }
        /// <summary>건 쪽 군단 (대결의 A).</summary>
        public string CorpsA { get; internal set; }
        /// <summary>받은 쪽 군단 (대결의 B).</summary>
        public string CorpsB { get; internal set; }
        public long StartTick { get; internal set; }
        /// <summary>이 틱까지 선택 기록(duelChoices)이 오지 않으면 양쪽 모두 AI로 끝낸다.</summary>
        public long DeadlineTick { get; internal set; }
        /// <summary>이 대결만의 난수 시드: 화면이 미리 보여 주는 라운드와 전투가 내는 결과가 같다.</summary>
        public ulong Seed { get; internal set; }
        public ContestResult Result { get; internal set; }
    }

    /// <summary>
    /// 전투 중 일기토 (스킬 "일기토 신청", battle.md 2.7.5·skills.md, R-71).
    /// 신청 → 받는 장수가 성향 확률로 응함/거절 (거절하면 그 군단 사기↓). 응하면 두 군단은 서로 교전을 멈추고 제자리, 명령을 받지 않는다.
    /// 플레이어가 고른 행동은 명령 duelChoices 하나로 들어오고(기록·리플레이·서버 검증 그대로), 기한까지 안 오면 AI가 고른다.
    /// 결과의 전투 영향은 전투 직전 대결과 같다 (사기, 쓰러지면 혼란 뒤 도망·중상·포로).
    /// </summary>
    public static class InBattleDuels
    {
        /// <summary>틱마다: 기한이 지났거나 한쪽이 전장을 떠난 대결을 끝낸다.</summary>
        internal static void Step(BattleContext ctx)
        {
            for (int i = ctx.Duels.Count - 1; i >= 0; i--)
            {
                var d = ctx.Duels[i];
                if (d.Result != null) continue;
                var a = ctx.FindCorps(d.CorpsA);
                var b = ctx.FindCorps(d.CorpsB);
                if (!a.InBattle || !b.InBattle) { End(ctx, d, a, b); continue; } // 대결 없이 풀림
                if (ctx.Tick >= d.DeadlineTick) Resolve(ctx, d, null, true);
            }
        }

        /// <summary>대기 중인 대결 (그 군단이 끼어 있는).</summary>
        public static DuelState PendingFor(BattleContext ctx, string corpsId)
        {
            foreach (var d in ctx.Duels)
                if (d.Result == null && (d.CorpsA == corpsId || d.CorpsB == corpsId)) return d;
            return null;
        }

        /// <summary>
        /// 이 대결을 끝까지 계산한다 (전투에 반영하지 않음). playerChoices = 한쪽(playerIsA)의 기록, 모자라면 AI.
        /// 앞 k라운드는 앞 k개 기록에만 달린다 (기록이 있으면 그 쪽은 난수를 쓰지 않는다) → 화면이 한 라운드씩 보여 줄 수 있다.
        /// </summary>
        public static ContestResult Simulate(DefinitionSet defs, BattleSetup setup, DuelState d, IReadOnlyList<string> playerChoices, bool playerIsA)
        {
            var def = defs.Contests[d.ContestId];
            var a = Fighter(defs, def, Corps(setup, d.CorpsA));
            var b = Fighter(defs, def, Corps(setup, d.CorpsB));
            var rng = new SeededRandom(d.Seed);
            var session = new ContestSession(def, a, b, rng);
            var ai = new AiContestChooser(defs.Ai, rng);
            IContestChooser mine = new RecordedChooser(playerChoices ?? new List<string>(), ai);
            return ContestRunner.Run(session, playerIsA ? mine : ai, playerIsA ? ai : mine);
        }

        /// <summary>신청을 받아 응함/거절을 정한다 (스킬 효과 challenge).</summary>
        internal static void Challenge(SkillCast cast, SkillPart p)
        {
            var ctx = cast.Battle;
            var a = cast.Caster;
            var b = cast.TargetCorps;
            string contestId = p.Has("contest") ? p.String("contest") : "duel";
            if (b == null || !b.InBattle || a.Setup.General == null || b.Setup.General == null) return;
            if (!ctx.Defs.Contests.TryGetValue(contestId, out var def) || !ctx.IsEnabled(def.Flag)) return;
            if (a.DuelWith != null || b.DuelWith != null) return;
            // 응할 확률: 성향별 (없으면 기본값). 거부 성향이면 늘 거절
            Fix chance = p.FixOr("acceptChance", Fix.FromDecimal(0.5m));
            string key = "accept." + (b.Setup.General.Temperament ?? "");
            if (p.Has(key)) chance = p.Fix(key);
            foreach (var t in def.RefuseTemperaments) if (t == b.Setup.General.Temperament) chance = Fix.Zero;
            if (!ctx.Random.Chance(chance))
            {
                b.Morale = Fix.Clamp(b.Morale + p.FixOr("refuseMorale", Fix.Zero), Fix.Zero, Fix.Hundred);
                Events.EventLog.Add(ctx, Events.EventKinds.DuelRefused, b.Id, a.Id);
                return;
            }
            var d = new DuelState
            {
                ContestId = contestId,
                CorpsA = a.Id,
                CorpsB = b.Id,
                StartTick = ctx.Tick,
                DeadlineTick = ctx.Tick + SkillRules.Ticks(ctx, p.FixOr("decideSec", Fix.FromInt(30))),
                Seed = unchecked(ctx.Setup.Seed * 0x9E3779B97F4A7C15UL + (ulong)ctx.Tick * 0xBF58476D1CE4E5B9UL + (ulong)a.Index * 31UL + (ulong)b.Index),
            };
            ctx.Duels.Add(d);
            foreach (var c in new[] { a, b })
            {
                c.DuelWith = c == a ? b.Id : a.Id;
                c.Queue.Clear();
                c.PendingSkill = null;
                c.AssignOrder(new CorpsOrder()); // 제자리
            }
            Events.EventLog.Add(ctx, Events.EventKinds.DuelStarted, a.Id, b.Id);
        }

        /// <summary>결과를 내고 전투에 반영한다. choices = 플레이어 쪽 기록 (없으면 양쪽 AI).</summary>
        internal static void Resolve(BattleContext ctx, DuelState d, IReadOnlyList<string> choices, bool playerIsA)
        {
            var a = ctx.FindCorps(d.CorpsA);
            var b = ctx.FindCorps(d.CorpsB);
            var def = ctx.Defs.Contests[d.ContestId];
            d.Result = Simulate(ctx.Defs, ctx.Setup, d, choices, playerIsA);
            PreBattleContests.Apply(ctx, def, d.Result, a, b, ctx.Injuries);
            End(ctx, d, a, b);
            Events.EventLog.Add(ctx, Events.EventKinds.DuelEnded, a.Id, d.Result.End + ":" + (d.Result.Winner ?? d.Result.Retreater ?? ""));
        }

        private static void End(BattleContext ctx, DuelState d, CorpsState a, CorpsState b)
        {
            if (d.Result == null) d.Result = new ContestResult();
            a.DuelWith = null;
            b.DuelWith = null;
        }

        private static ContestFighter Fighter(DefinitionSet defs, ContestDef def, CorpsSetup c)
        {
            var f = new ContestFighter(c.General.Id, c.General.Stat(def.Stat), c.General.Temperament);
            f.DamagePct = Combat.TraitEffects.ContestDamagePct(defs, c.General.Traits);
            return f;
        }

        private static CorpsSetup Corps(BattleSetup setup, string id)
        {
            foreach (var c in setup.Corps) if (c.Id == id) return c;
            throw new System.ArgumentException("없는 군단 " + id);
        }
    }

    /// <summary>스킬 효과 "challenge": 일기토 신청 (R-71).</summary>
    internal sealed class ChallengeEffect : ISkillEffect
    {
        public string Type => "challenge";
        public void Apply(SkillCast c, SkillPart p) => InBattleDuels.Challenge(c, p);
    }

    /// <summary>스킬 조건 "targetCanDuel": 대상 군단에 장수가 있고, 두 군단 모두 다른 대결 중이 아니다.</summary>
    internal sealed class TargetCanDuel : ISkillCondition
    {
        public string Type => "targetCanDuel";
        public bool IsMet(SkillCast c, SkillPart p) =>
            c.TargetCorps != null && c.TargetCorps.Setup.General != null && c.Caster.Setup.General != null
            && c.TargetCorps.DuelWith == null && c.Caster.DuelWith == null;
    }

    /// <summary>명령 duelChoices (R-71): 플레이어 쪽 일기토 선택 기록 (Mode = 행동 id를 쉼표로). 대기 중인 대결이 있어야 한다.</summary>
    internal sealed class DuelChoicesHandler : ICommandHandler
    {
        public string Kind => CommandKinds.DuelChoices;

        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) => InBattleDuels.PendingFor(ctx, corps.Id) != null;

        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd)
        {
            var d = InBattleDuels.PendingFor(ctx, corps.Id);
            var choices = new List<string>();
            if (!string.IsNullOrEmpty(cmd.Mode))
                foreach (var s in cmd.Mode.Split(',')) if (s.Length > 0) choices.Add(s);
            InBattleDuels.Resolve(ctx, d, choices, d.CorpsA == corps.Id);
        }
    }
}
