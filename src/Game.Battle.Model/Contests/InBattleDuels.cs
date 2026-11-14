using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Skills;
using Game.Data.Defs;
using Game.Foundation.Math;
using Game.Foundation.Random;

namespace Game.Battle.Model.Contests
{
    /// <summary>플레이어 군단이 받은 일기토 신청 (R-79). 답(duelAnswer)이나 기한을 기다린다. 그동안 두 군단은 계속 싸운다.</summary>
    public sealed class DuelOffer
    {
        public string ContestId { get; internal set; }
        /// <summary>건 쪽.</summary>
        public string CorpsA { get; internal set; }
        /// <summary>받은 쪽 (플레이어).</summary>
        public string CorpsB { get; internal set; }
        public long DeadlineTick { get; internal set; }
        internal Fix AcceptChance { get; set; }
        internal Fix RefuseMorale { get; set; }
        internal long DecideTicks { get; set; }
    }

    /// <summary>마주침 일기토 기회 (R-91): 직접 지휘하는 플레이어 군단(거는 쪽)이 신청할지 고르기를 기다린다. 기한까지 답이 없으면 넘어간다.</summary>
    public sealed class DuelChance
    {
        public string ContestId { get; internal set; }
        /// <summary>거는 쪽 (플레이어 군단).</summary>
        public string CorpsA { get; internal set; }
        /// <summary>상대.</summary>
        public string CorpsB { get; internal set; }
        public long DeadlineTick { get; internal set; }
    }

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
            Encounters(ctx);
            // 답이 없는 기회 (R-91): 기한이 지나면 넘어간다. 한쪽이 전장을 떠나면 없던 일로
            for (int i = ctx.DuelChances.Count - 1; i >= 0; i--)
            {
                var ch = ctx.DuelChances[i];
                bool gone = !ctx.FindCorps(ch.CorpsA).InBattle || !ctx.FindCorps(ch.CorpsB).InBattle;
                if (gone || ctx.Tick >= ch.DeadlineTick)
                {
                    ctx.DuelChances.RemoveAt(i);
                    if (!gone) Events.EventLog.Add(ctx, Events.EventKinds.DuelChancePassed, ch.CorpsA, ch.CorpsB);
                }
            }
            // 답이 없는 신청 (R-79): 기한이 지나면 받는 장수의 성향 확률로. 한쪽이 전장을 떠나면 없던 일로
            for (int i = ctx.DuelOffers.Count - 1; i >= 0; i--)
            {
                var o = ctx.DuelOffers[i];
                if (!ctx.FindCorps(o.CorpsA).InBattle || !ctx.FindCorps(o.CorpsB).InBattle) { ctx.DuelOffers.RemoveAt(i); continue; }
                if (ctx.Tick >= o.DeadlineTick) Decide(ctx, o, ctx.Random.Chance(o.AcceptChance));
            }
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

        /// <summary>
        /// 마주침 대결 (R-89): 짝(pairs, 기본 = 양쪽 총대장)인 장수 두 편 군단이 처음 서로 마주치면 (한쪽 팀이 상대를 보고, 거리 encounterCells 안) 그 쌍을 한 번 판정한다.
        /// 판정은 전투 직전 대결과 같은 규칙 (능력치 차이 maxStatDifference 이내, chance 확률, 거부 성향이면 없음).
        /// 한 전투에 maxPerBattle번까지. 일어나면 플레이어 쪽이 받는 쪽(B): 직접 지휘 중이면 신청(duelOffered)으로 묻고 (답이 없으면 응함), 아니면 바로 시작. 군단마다 한 번만.
        /// </summary>
        private static void Encounters(BattleContext ctx)
        {
            var ids = ctx.Setup.EncounterContests;
            if (ids == null || ids.Count == 0) return;
            foreach (var id in ids)
            {
                if (!ctx.Defs.Contests.TryGetValue(id, out var def) || !ctx.IsEnabled(def.Flag)) continue;
                ctx.EncounterCount.TryGetValue(id, out int count);
                if (count >= def.EncounterMaxPerBattle) continue;
                Fix reach2 = def.EncounterCells * def.EncounterCells;
                bool commandersOnly = def.EncounterPairs != "any";
                foreach (var x in ctx.Corps)
                {
                    if (!Ready(ctx, x) || (commandersOnly && x.Setup.Role != CorpsRoles.Commander)) continue;
                    foreach (var y in ctx.Corps)
                    {
                        if (y.Index <= x.Index || y.Team == x.Team || !Ready(ctx, y) || (commandersOnly && y.Setup.Role != CorpsRoles.Commander)) continue;
                        if (Vec.DistanceSquared(x.Anchor, y.Anchor) > reach2) continue;
                        if (!ctx.IsVisibleTo(x.Team, y) && !ctx.IsVisibleTo(y.Team, x)) continue;
                        if (!ctx.EncounterChecked.Add(id + "|" + x.Id + "|" + y.Id)) continue;
                        var gx = x.Setup.General; var gy = y.Setup.General;
                        if (!ContestTrigger.Occurs(def, gx.Stat(def.Stat), gy.Stat(def.Stat), gx.Temperament, gy.Temperament, ctx.Random)) continue;
                        // 거는 쪽 = 무력이 높은 쪽, 같으면 boldOrder에서 앞선 성향, 그것도 같으면 번호가 앞선 쪽 (R-91)
                        var a = Bolder(def, x, y) ? x : y;
                        var b = a == x ? y : x;
                        ctx.EncounterCount[id] = ++count;
                        ctx.EncounterDone.Add(a.Id);
                        ctx.EncounterDone.Add(b.Id);
                        Events.EventLog.Add(ctx, Events.EventKinds.DuelEncounter, a.Id, b.Id);
                        if (a.Controller == Controllers.Player && !a.Delegated)
                        {
                            // 내가 거는 쪽: 신청할지 묻는다 (답이 없으면 넘어감)
                            ctx.DuelChances.Add(new DuelChance { ContestId = id, CorpsA = a.Id, CorpsB = b.Id, DeadlineTick = ctx.Tick + SkillRules.Ticks(ctx, def.EncounterOfferSec) });
                            Events.EventLog.Add(ctx, Events.EventKinds.DuelChance, a.Id, b.Id);
                        }
                        else Offer(ctx, def, a, b, null); // AI(위임 포함)는 건다
                        break; // x는 이제 끝났다
                    }
                    if (count >= def.EncounterMaxPerBattle) break;
                }
            }
        }

        private static bool Ready(BattleContext ctx, CorpsState c) =>
            c.InBattle && c.Setup.General != null && c.DuelWith == null && !ctx.EncounterDone.Contains(c.Id) && !HasOffer(ctx, c.Id);

        /// <summary>x가 y보다 먼저 거는 쪽인가: 대결 능력치 → boldOrder 성향 → 번호.</summary>
        private static bool Bolder(ContestDef def, CorpsState x, CorpsState y)
        {
            Fix sx = x.Setup.General.Stat(def.Stat), sy = y.Setup.General.Stat(def.Stat);
            if (sx != sy) return sx > sy;
            int Rank(string t) { for (int i = 0; i < def.BoldOrder.Count; i++) if (def.BoldOrder[i] == t) return i; return def.BoldOrder.Count; }
            int rx = Rank(x.Setup.General.Temperament), ry = Rank(y.Setup.General.Temperament);
            if (rx != ry) return rx < ry;
            return x.Index < y.Index;
        }

        public static DuelChance ChanceFor(BattleContext ctx, string corpsId)
        {
            foreach (var c in ctx.DuelChances) if (c.CorpsA == corpsId) return c;
            return null;
        }

        /// <summary>기회에 답한다 (명령 duelChance): 신청하면 상대에게 신청, 아니면 넘어간다.</summary>
        internal static void AnswerChance(BattleContext ctx, DuelChance ch, bool challenge)
        {
            ctx.DuelChances.Remove(ch);
            var a = ctx.FindCorps(ch.CorpsA);
            var b = ctx.FindCorps(ch.CorpsB);
            if (!challenge || !a.InBattle || !b.InBattle) { Events.EventLog.Add(ctx, Events.EventKinds.DuelChancePassed, a.Id, b.Id); return; }
            if (ctx.Defs.Contests.TryGetValue(ch.ContestId, out var def)) Offer(ctx, def, a, b, null);
        }

        /// <summary>
        /// a가 b에게 일기토를 건다 (마주침 기회·스킬 공통, R-91). b가 직접 지휘하는 플레이어 군단이면 신청(duelOffered)으로 묻고,
        /// offerSec 안에 답이 없으면 b 장수의 성향 확률로. 그 밖(AI·위임)은 바로 성향 확률로 정한다.
        /// acceptOverride = 스킬 매개변수로 정한 응할 확률 (없으면 대결 정의의 성향표).
        /// </summary>
        internal static void Offer(BattleContext ctx, ContestDef def, CorpsState a, CorpsState b, Fix? acceptOverride, Fix? refuseMoraleOverride = null)
        {
            if (b == null || !b.InBattle || a.Setup.General == null || b.Setup.General == null) return;
            if (a.DuelWith != null || b.DuelWith != null || HasOffer(ctx, a.Id) || HasOffer(ctx, b.Id)) return;
            Fix chance = acceptOverride ?? def.AcceptChance(b.Setup.General.Temperament);
            foreach (var t in def.RefuseTemperaments) if (t == b.Setup.General.Temperament) chance = Fix.Zero;
            var offer = new DuelOffer
            {
                ContestId = def.Id,
                CorpsA = a.Id,
                CorpsB = b.Id,
                AcceptChance = chance,
                RefuseMorale = refuseMoraleOverride ?? def.RefuseMorale,
                DecideTicks = SkillRules.Ticks(ctx, def.EncounterDecideSec),
                DeadlineTick = ctx.Tick + SkillRules.Ticks(ctx, def.EncounterOfferSec),
            };
            if (b.Controller == Controllers.Player && !b.Delegated)
            {
                ctx.DuelOffers.Add(offer);
                Events.EventLog.Add(ctx, Events.EventKinds.DuelOffered, b.Id, a.Id);
                return;
            }
            // 고를 사람이 없는 쪽끼리면 고르는 시간 없이 바로 AI로 (군단을 세워 두지 않음)
            if (!(a.Controller == Controllers.Player && !a.Delegated)) offer.DecideTicks = 0;
            Decide(ctx, offer, ctx.Random.Chance(chance));
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

        /// <summary>대기 중인 신청 (플레이어 군단이 받은 것, R-79).</summary>
        public static DuelOffer OfferTo(BattleContext ctx, string corpsId)
        {
            foreach (var o in ctx.DuelOffers) if (o.CorpsB == corpsId) return o;
            return null;
        }

        internal static bool HasOffer(BattleContext ctx, string corpsId)
        {
            foreach (var o in ctx.DuelOffers) if (o.CorpsA == corpsId || o.CorpsB == corpsId) return true;
            return false;
        }

        /// <summary>
        /// 신청을 받아 응함/거절을 정한다 (스킬 효과 challenge). 받는 쪽이 플레이어가 직접 지휘하는 군단이면 (위임 아님)
        /// 바로 정하지 않고 신청을 남긴다 → 명령 duelAnswer로 응함/거절, offerSec 안에 답이 없으면 성향 확률로 (R-79).
        /// </summary>
        internal static void Challenge(SkillCast cast, SkillPart p)
        {
            var ctx = cast.Battle;
            string contestId = p.Has("contest") ? p.String("contest") : "duel";
            if (!ctx.Defs.Contests.TryGetValue(contestId, out var def) || !ctx.IsEnabled(def.Flag)) return;
            // 스킬 매개변수가 있으면 그것 (예전 데이터), 없으면 대결 정의의 성향표·벌칙 (R-91)
            Fix? chance = null;
            string key = "accept." + (cast.TargetCorps?.Setup.General?.Temperament ?? "");
            if (p.Has(key)) chance = p.Fix(key);
            else if (p.Has("acceptChance")) chance = p.Fix("acceptChance");
            Offer(ctx, def, cast.Caster, cast.TargetCorps, chance, p.Has("refuseMorale") ? p.Fix("refuseMorale") : (Fix?)null);
        }

        /// <summary>신청에 답한다: 응하면 대결 시작, 아니면 받은 쪽 사기 + refuseMorale.</summary>
        internal static void Decide(BattleContext ctx, DuelOffer o, bool accept)
        {
            ctx.DuelOffers.Remove(o);
            var a = ctx.FindCorps(o.CorpsA);
            var b = ctx.FindCorps(o.CorpsB);
            if (!a.InBattle || !b.InBattle || a.DuelWith != null || b.DuelWith != null) return;
            if (!accept)
            {
                // 거절 벌칙 (R-91): 거절한 군단 사기·잠깐 공격 약화, 같은 편 다른 군단 사기
                b.Morale = Fix.Clamp(b.Morale + o.RefuseMorale, Fix.Zero, Fix.Hundred);
                if (ctx.Defs.Contests.TryGetValue(o.ContestId, out var def))
                {
                    if (def.RefuseAttackPp != Fix.Zero && def.RefuseSec > Fix.Zero)
                        b.AddStatus(new StatusEffect { Id = StatusIds.DuelRefused, TicksLeft = SkillRules.Ticks(ctx, def.RefuseSec), AttackPp = def.RefuseAttackPp });
                    if (def.RefuseAllyMorale != Fix.Zero)
                        foreach (var c in ctx.Corps)
                            if (c != b && c.Team == b.Team && c.InBattle) c.Morale = Fix.Clamp(c.Morale + def.RefuseAllyMorale, Fix.Zero, Fix.Hundred);
                }
                Events.EventLog.Add(ctx, Events.EventKinds.DuelRefused, b.Id, a.Id);
                return;
            }
            var d = new DuelState
            {
                ContestId = o.ContestId,
                CorpsA = a.Id,
                CorpsB = b.Id,
                StartTick = ctx.Tick,
                DeadlineTick = ctx.Tick + o.DecideTicks,
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
            && c.TargetCorps.DuelWith == null && c.Caster.DuelWith == null
            && !InBattleDuels.HasOffer(c.Battle, c.TargetCorps.Id) && !InBattleDuels.HasOffer(c.Battle, c.Caster.Id);
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

    /// <summary>명령 duelAnswer (R-79): 받은 일기토 신청에 답한다. Mode "accept" = 응함, 그 밖 = 거절.</summary>
    /// <summary>명령 duelChance (R-91): 마주침 일기토 기회에 신청(challenge) / 넘어가기(pass).</summary>
    internal sealed class DuelChanceHandler : ICommandHandler
    {
        public string Kind => CommandKinds.DuelChance;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) => InBattleDuels.ChanceFor(ctx, corps.Id) != null;
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd) =>
            InBattleDuels.AnswerChance(ctx, InBattleDuels.ChanceFor(ctx, corps.Id), cmd.Mode == "challenge");
    }

    internal sealed class DuelAnswerHandler : ICommandHandler
    {
        public string Kind => CommandKinds.DuelAnswer;
        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) => InBattleDuels.OfferTo(ctx, corps.Id) != null;
        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd) =>
            InBattleDuels.Decide(ctx, InBattleDuels.OfferTo(ctx, corps.Id), cmd.Mode == "accept");
    }
}
