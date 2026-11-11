using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Vision;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Skills
{
    /// <summary>스킬 한 번 사용의 상황. 효과들이 이것만 보고 동작한다.</summary>
    public sealed class SkillCast
    {
        public BattleContext Battle { get; internal set; }
        public CorpsState Caster { get; internal set; }
        public SkillDef Skill { get; internal set; }
        public CorpsState TargetCorps { get; internal set; }
        public Vec Point { get; internal set; }
    }

    /// <summary>스킬 효과 하나 (skills.json effects[].type). 새 효과는 구현을 추가하고 등록한다.</summary>
    public interface ISkillEffect
    {
        string Type { get; }
        void Apply(SkillCast cast, SkillPart part);
    }

    /// <summary>스킬 발동 조건 하나 (skills.json conditions[].type).</summary>
    public interface ISkillCondition
    {
        string Type { get; }
        bool IsMet(SkillCast cast, SkillPart part);
    }

    public sealed class SkillRegistry
    {
        private readonly Dictionary<string, ISkillEffect> _effects = new Dictionary<string, ISkillEffect>();
        private readonly Dictionary<string, ISkillCondition> _conditions = new Dictionary<string, ISkillCondition>();

        public SkillRegistry Register(ISkillEffect e) { _effects[e.Type] = e; return this; }
        public SkillRegistry Register(ISkillCondition c) { _conditions[c.Type] = c; return this; }
        public bool HasEffect(string type) => _effects.ContainsKey(type);
        public bool HasCondition(string type) => _conditions.ContainsKey(type);
        internal ISkillEffect Effect(string type) => _effects[type];
        internal ISkillCondition Condition(string type) => _conditions[type];

        public static SkillRegistry WithBuiltIns() => new SkillRegistry()
            .Register(new RevealEffect())
            .Register(new AmbushEffect())
            .Register(new AreaDamageEffect())
            .Register(new MoraleEffect())
            .Register(new ClearConfusionEffect())
            .Register(new AttackBuffEffect())
            .Register(new ContactMoraleDamageEffect())
            .Register(new TauntEffect())
            .Register(new IgniteEffect())
            .Register(new TargetFlammable())
            .Register(new WindBlowing())
            .Register(new FireAllowedByWeather())
            .Register(new OnConcealingTerrain())
            .Register(new NotEngaged())
            .Register(new CasterHasTag());
    }

    /// <summary>전조가 끝나면 나타날 효과.</summary>
    public sealed class ScheduledEffect
    {
        public long FireTick { get; internal set; }
        public SkillCast Cast { get; internal set; }
    }

    public static class SkillTargets
    {
        public const string Self = "self";
        public const string EnemyCorps = "enemyCorps";
        public const string AllyCorps = "allyCorps";
        public const string Point = "point";
        public const string Area = "area";
    }

    /// <summary>
    /// 스킬 사용 명령 (battle.md 2.8, 4.2). 받을 때 점수·횟수를 낸다 (실패·취소해도 돌려주지 않음, R-12).
    /// 사거리 밖이면 다가간 뒤 쓴다. 다가가는 중 대상을 잃으면 취소.
    /// </summary>
    public sealed class UseSkillHandler : ICommandHandler
    {
        public string Kind => CommandKinds.UseSkill;

        public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd)
        {
            if (cmd.SkillId == null || !ctx.Defs.Skills.TryGetValue(cmd.SkillId, out var skill)) return false;
            if (!corps.KnowsSkill(skill.Id) || !SkillRules.RequirementsMet(corps, skill)) return false;
            var cast = SkillRules.MakeCast(ctx, corps, skill, cmd);
            if (cast == null) return false;
            return corps.SkillPoints.CanUse(skill.Id, Combat.TraitEffects.SkillCost(ctx, corps, skill.Id, skill.Cost), SkillRules.ConditionsMet(cast));
        }

        public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd)
        {
            var skill = ctx.Defs.Skills[cmd.SkillId];
            var cast = SkillRules.MakeCast(ctx, corps, skill, cmd);
            if (!corps.SkillPoints.TryConsume(skill.Id, Combat.TraitEffects.SkillCost(ctx, corps, skill.Id, skill.Cost), SkillRules.ConditionsMet(cast))) return;
            if (SkillRules.InRange(cast))
            {
                SkillRules.Cast(cast);
                return;
            }
            corps.PendingSkill = cmd;
            if (cast.TargetCorps != null)
                corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Attack, TargetCorpsId = cast.TargetCorps.Id });
            else
                corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.MoveTo, Point = cast.Point });
        }
    }

    public static class SkillRules
    {
        public static bool RequirementsMet(CorpsState corps, SkillDef skill)
        {
            if (skill.Requires.Count == 0) return true;
            int met = 0;
            foreach (var r in skill.Requires) if (corps.Setup.General.Stat(r.Stat) >= r.Min) met++;
            return skill.RequiresAny ? met > 0 : met == skill.Requires.Count;
        }

        /// <summary>대상이 올바르면 상황 객체, 아니면 null.</summary>
        internal static SkillCast MakeCast(BattleContext ctx, CorpsState caster, SkillDef skill, BattleCommand cmd)
        {
            var cast = new SkillCast { Battle = ctx, Caster = caster, Skill = skill, Point = caster.Anchor };
            switch (skill.TargetType)
            {
                case SkillTargets.Self:
                    cast.TargetCorps = caster;
                    return cast;
                case SkillTargets.EnemyCorps:
                case SkillTargets.AllyCorps:
                {
                    var t = ctx.FindCorps(cmd.TargetCorpsId);
                    bool enemy = skill.TargetType == SkillTargets.EnemyCorps;
                    if (t == null || !t.InBattle || (t.Team != caster.Team) != enemy) return null;
                    if (enemy && !ctx.IsVisibleTo(caster.Team, t)) return null;
                    cast.TargetCorps = t;
                    cast.Point = t.Anchor;
                    return cast;
                }
                case SkillTargets.Point:
                case SkillTargets.Area:
                    if (!ctx.Field.Map.InBounds((int)cmd.X.FloorToLong(), (int)cmd.Y.FloorToLong())) return null;
                    cast.Point = new Vec(cmd.X, cmd.Y);
                    return cast;
                default:
                    return null;
            }
        }

        internal static bool ConditionsMet(SkillCast cast)
        {
            var reg = cast.Battle.SkillRegistry;
            foreach (var c in cast.Skill.Conditions)
                if (!reg.HasCondition(c.Type) || !reg.Condition(c.Type).IsMet(cast, c)) return false;
            return true;
        }

        internal static bool InRange(SkillCast cast)
        {
            if (cast.Skill.TargetType == SkillTargets.Self || cast.Skill.Range == null) return true;
            Fix r = cast.Skill.Range.Value;
            return Vec.DistanceSquared(cast.Caster.Anchor, cast.Point) <= r * r;
        }

        /// <summary>효과를 낸다 (전조가 있으면 그 뒤에).</summary>
        internal static void Cast(SkillCast cast)
        {
            var ctx = cast.Battle;
            Events.EventLog.Add(ctx, Events.EventKinds.SkillUsed, cast.Caster.Id, cast.Skill.Id);
            long delay = (cast.Skill.TelegraphSec / ctx.Combat.TickSeconds).CeilToLong();
            if (delay <= 0) ApplyEffects(cast);
            else ctx.Scheduled.Add(new ScheduledEffect { FireTick = ctx.Tick + delay, Cast = cast });
        }

        internal static void ApplyEffects(SkillCast cast)
        {
            var reg = cast.Battle.SkillRegistry;
            foreach (var e in cast.Skill.Effects) reg.Effect(e.Type).Apply(cast, e);
        }

        internal static long Ticks(BattleContext ctx, Fix seconds) => (seconds / ctx.Combat.TickSeconds).CeilToLong();
    }

    /// <summary>다가가는 중인 스킬, 전조가 끝난 효과.</summary>
    internal static class SkillSystem
    {
        public static void Step(BattleContext ctx)
        {
            foreach (var corps in ctx.Corps)
            {
                var cmd = corps.PendingSkill;
                if (cmd == null) continue;
                if (!corps.InBattle) { corps.PendingSkill = null; continue; }
                var skill = ctx.Defs.Skills[cmd.SkillId];
                var cast = SkillRules.MakeCast(ctx, corps, skill, cmd);
                if (cast == null) { corps.PendingSkill = null; Commands.OrderQueue.Finish(ctx, corps); continue; } // 대상을 잃음 → 취소, 다음 예약
                if (!SkillRules.InRange(cast)) continue;
                corps.PendingSkill = null;
                if (cast.TargetCorps == null) Commands.OrderQueue.Finish(ctx, corps);
                if (SkillRules.ConditionsMet(cast)) SkillRules.Cast(cast); // 조건이 깨졌으면 실패 (소모는 그대로)
            }
            for (int i = 0; i < ctx.Scheduled.Count; i++)
            {
                var s = ctx.Scheduled[i];
                if (s.FireTick > ctx.Tick) continue;
                ctx.Scheduled.RemoveAt(i--);
                if (s.Cast.Caster.InBattle) SkillRules.ApplyEffects(s.Cast);
            }
        }
    }

    /// <summary>
    /// 복병: 준비 중 움직이거나 적에게 보이면 실패. 준비가 끝나면 은신.
    /// 은신 중 적이 사거리에 들어오면 튀어나와 기습 (기습 보정, 닿은 적 사기↓·일부 혼란, 스위치 ambushConfusion).
    /// 은신 중 움직이면 풀린다.
    /// </summary>
    public static class StealthSystem
    {
        public const string AmbushConfusionFlag = "ambushConfusion";

        internal static void Step(BattleContext ctx)
        {
            foreach (var c in ctx.Corps)
            {
                if (!c.InBattle) { c.Hidden = false; continue; }
                var setup = c.FindStatus(StatusIds.AmbushSetup);
                if (setup != null)
                {
                    if (c.MovedThisTick) { c.RemoveStatus(StatusIds.AmbushSetup); c.AmbushEffect = null; }
                    else if (SeenByEnemy(ctx, c))
                    {
                        // 준비 중에 들킴: 가장 가까운 적 군단이 찾아낸 것으로 기록 (간파로 찾은 것은 시야 쪽에서 이미 기록)
                        if (c.HasStatus(StatusIds.AmbushSetup)) { var finder = NearestEnemy(ctx, c); if (finder != null) Events.EventLog.Add(ctx, Events.EventKinds.HiddenFound, finder.Id, c.Id); }
                        c.RemoveStatus(StatusIds.AmbushSetup);
                        c.AmbushEffect = null;
                    }
                    else if (setup.TicksLeft <= 1) { c.RemoveStatus(StatusIds.AmbushSetup); c.Hidden = true; }
                    continue;
                }
                if (!c.Hidden) continue;
                if (c.MovedThisTick) { Expose(c); continue; }
                if (EnemiesInReach(ctx, c).Count > 0) Spring(ctx, c);
            }
        }

        public static void Expose(CorpsState c)
        {
            c.Hidden = false;
            c.AmbushEffect = null;
        }

        private static CorpsState NearestEnemy(BattleContext ctx, CorpsState c)
        {
            CorpsState best = null;
            Fix bestD = Fix.Zero;
            foreach (var e in ctx.Corps)
            {
                if (e.Team == c.Team || !e.InBattle) continue;
                Fix d = Vec.DistanceSquared(e.Anchor, c.Anchor);
                if (best == null || d < bestD) { best = e; bestD = d; }
            }
            return best;
        }

        private static bool SeenByEnemy(BattleContext ctx, CorpsState c)
        {
            foreach (var team in ctx.Teams)
                if (team != c.Team && ctx.IsVisibleTo(team, c)) return true;
            return false;
        }

        private static List<CorpsState> EnemiesInReach(BattleContext ctx, CorpsState h)
        {
            var list = new List<CorpsState>();
            foreach (var e in ctx.Corps)
            {
                if (e.Team == h.Team || !e.InBattle) continue;
                bool hit = false;
                foreach (var u in h.Units)
                {
                    if (!u.Alive) continue;
                    Fix reach = u.Troop.AttackRange + ctx.Combat.ContactRadius;
                    foreach (var v in e.Units)
                        if (v.Alive && Vec.DistanceSquared(u.Position, v.Position) <= reach * reach) { hit = true; break; }
                    if (hit) break;
                }
                if (hit) list.Add(e);
            }
            return list;
        }

        private static void Spring(BattleContext ctx, CorpsState h)
        {
            var p = h.AmbushEffect;
            var victims = EnemiesInReach(ctx, h);
            Expose(h);
            Events.EventLog.Add(ctx, Events.EventKinds.AmbushSprung, h.Id);
            foreach (var e in victims) Events.EventLog.Add(ctx, Events.EventKinds.Ambushed, e.Id, h.Id);
            if (p == null) return;
            h.AddStatus(new StatusEffect { Id = StatusIds.Surprise, AttackPp = p.Fix("surprisePp"), TicksLeft = SkillRules.Ticks(ctx, p.Fix("surpriseSec")) });
            long confusedTicks = SkillRules.Ticks(ctx, p.Fix("confusionSec"));
            Fix ratio = p.Fix("confusionRatio");
            foreach (var e in victims)
            {
                e.Morale = Fix.Clamp(e.Morale - p.Fix("moraleDamage"), Fix.Zero, Fix.Hundred);
                if (!ctx.IsEnabled(AmbushConfusionFlag)) continue;
                var alive = new List<SubUnitState>();
                foreach (var u in e.Units) if (u.Alive) alive.Add(u);
                long n = (ratio * alive.Count).CeilToLong();
                // 복병에 가까운 소부대부터 (같은 거리면 칸 순서)
                alive.Sort((a, b) =>
                {
                    int cmp = Vec.DistanceSquared(a.Position, h.Anchor).CompareTo(Vec.DistanceSquared(b.Position, h.Anchor));
                    return cmp != 0 ? cmp : e.Units.IndexOf(a).CompareTo(e.Units.IndexOf(b));
                });
                for (int i = 0; i < n && i < alive.Count; i++) Events.Confusion.Apply(ctx, alive[i], confusedTicks);
            }
        }
    }

    /// <summary>상태: 도발 강제, 시간 감소, 혼란 감소, 책략 점수 회복 (R-13).</summary>
    internal static class StatusSystem
    {
        public const string SkillPointRegenFlag = "skillPointRegen";

        /// <summary>이동 전: 도발당한 군단은 건 군단을 공격하러 간다.</summary>
        public static void Enforce(BattleContext ctx)
        {
            foreach (var c in ctx.Corps)
            {
                var t = c.FindStatus(StatusIds.Taunted);
                if (t == null || c.Status != CorpsStatus.Active) continue;
                var src = ctx.FindCorps(t.SourceCorpsId);
                if (src == null || !src.InBattle) { c.RemoveStatus(StatusIds.Taunted); continue; }
                if (c.Order.Kind != OrderKinds.Attack || c.Order.TargetCorpsId != src.Id)
                    c.AssignOrder(new CorpsOrder { Kind = OrderKinds.Attack, TargetCorpsId = src.Id });
            }
        }

        public static void Tick(BattleContext ctx)
        {
            var k = ctx.Combat;
            bool regen = ctx.IsEnabled(SkillPointRegenFlag);
            foreach (var c in ctx.Corps)
            {
                for (int i = c.Statuses.Count - 1; i >= 0; i--)
                {
                    if (--c.Statuses[i].TicksLeft > 0) continue;
                    bool rout = c.Statuses[i].Id == StatusIds.Routing;
                    c.Statuses.RemoveAt(i);
                    if (rout && c.Status == CorpsStatus.Active) Systems.RetreatSystem.BeginRetreat(ctx, c);
                }
                foreach (var u in c.Units) if (u.ConfusedTicks > 0) u.ConfusedTicks--;
                if (c.InBattle)
                    c.SkillPoints.Regenerate(k.SkillPointRegenPerSecond * k.TickSeconds * (c.Resting ? k.RestSkillPointMultiplier : Fix.OneValue), c.Morale, ctx.Balance.MoraleBase, regen);
            }
        }
    }

    // ---- 효과 ----

    /// <summary>간파: 지점 주변을 몇 초간 밝혀 숨은 부대까지 드러낸다. 탐지 능력치(첨병)가 높으면 범위↑.</summary>
    internal sealed class RevealEffect : ISkillEffect
    {
        public string Type => "reveal";
        public void Apply(SkillCast c, SkillPart p)
        {
            Fix best = Fix.FromInt(50);
            foreach (var u in c.Caster.Units) if (u.Alive) best = Fix.Max(best, u.Troop.Stats.Get("detection"));
            Fix radius = p.Fix("radius") + (best - Fix.FromInt(50)) * p.FixOr("detectionRadiusPerPoint", Fix.Zero);
            c.Battle.Reveals.Add(new RevealZone { Team = c.Caster.Team, SourceCorpsId = c.Caster.Id, Center = c.Point, Radius = radius, TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("durationSec")) });
            VisionSystem.Update(c.Battle);
        }
    }

    /// <summary>복병 준비 시작 (제자리 대기). 완성·실패·기습은 StealthSystem.</summary>
    internal sealed class AmbushEffect : ISkillEffect
    {
        public string Type => "ambush";
        public void Apply(SkillCast c, SkillPart p)
        {
            c.Caster.AssignOrder(new CorpsOrder());
            c.Caster.AmbushEffect = p;
            c.Caster.AddStatus(new StatusEffect { Id = StatusIds.AmbushSetup, TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("setupSec")) });
        }
    }

    /// <summary>범위 피해: (시전 군단의 해당 태그 병력 × 계수)를 범위 안 적 소부대에 똑같이 나눈다. 숨은 부대도 맞고 드러난다.</summary>
    internal sealed class AreaDamageEffect : ISkillEffect
    {
        public string Type => "areaDamage";
        public void Apply(SkillCast c, SkillPart p)
        {
            string tag = p.String("sourceTag");
            long source = 0;
            foreach (var u in c.Caster.Units) if (u.Alive && u.Troop.HasTag(tag)) source += u.Troops;
            Fix total = Fix.FromInt(source) * p.Fix("damagePerSourceTroop");
            Fix r2 = c.Skill.Radius * c.Skill.Radius;
            var targets = new List<(CorpsState corps, SubUnitState unit)>();
            foreach (var e in c.Battle.Corps)
            {
                if (e.Team == c.Caster.Team || !e.InBattle) continue;
                foreach (var v in e.Units)
                    if (v.Alive && Vec.DistanceSquared(v.Position, c.Point) <= r2) targets.Add((e, v));
            }
            if (targets.Count == 0) return;
            Fix each = total / targets.Count;
            foreach (var (e, v) in targets)
            {
                v.PendingLoss += each;
                e.BeingHit = true;
                if (e.Hidden) StealthSystem.Expose(e);
            }
        }
    }

    /// <summary>사기 회복: 자기와 반경 안 아군.</summary>
    internal sealed class MoraleEffect : ISkillEffect
    {
        public string Type => "morale";
        public void Apply(SkillCast c, SkillPart p)
        {
            Fix amount = p.Fix("amount");
            Fix r = p.FixOr("allyRadius", Fix.Zero);
            foreach (var a in c.Battle.Corps)
            {
                if (a.Team != c.Caster.Team || !a.InBattle) continue;
                if (a != c.Caster && Vec.DistanceSquared(a.Anchor, c.Caster.Anchor) > r * r) continue;
                a.Morale = Fix.Clamp(a.Morale + amount, Fix.Zero, Fix.Hundred);
            }
        }
    }

    /// <summary>재정비: 혼란 해제.</summary>
    internal sealed class ClearConfusionEffect : ISkillEffect
    {
        public string Type => "clearConfusion";
        public void Apply(SkillCast c, SkillPart p)
        {
            foreach (var u in c.Caster.Units) u.ConfusedTicks = 0;
        }
    }

    /// <summary>공격 보정 버프 (예: 돌격 함성).</summary>
    internal sealed class AttackBuffEffect : ISkillEffect
    {
        public string Type => "attackBuff";
        public void Apply(SkillCast c, SkillPart p) =>
            c.Caster.AddStatus(new StatusEffect
            {
                Id = "buff." + c.Skill.Id,
                AttackPp = p.Fix("pp"),
                TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("durationSec")),
            });
    }

    /// <summary>맞붙은 적 군단 사기 감소.</summary>
    internal sealed class ContactMoraleDamageEffect : ISkillEffect
    {
        public string Type => "contactMoraleDamage";
        public void Apply(SkillCast c, SkillPart p)
        {
            Fix amount = p.Fix("amount");
            foreach (var e in c.Battle.Corps)
            {
                if (e.Team == c.Caster.Team || !e.InBattle) continue;
                bool touch = false;
                foreach (var u in c.Caster.Units)
                {
                    if (!u.Alive) continue;
                    Fix reach = u.Troop.AttackRange + c.Battle.Combat.ContactRadius;
                    foreach (var v in e.Units)
                        if (v.Alive && Vec.DistanceSquared(u.Position, v.Position) <= reach * reach) { touch = true; break; }
                    if (touch) break;
                }
                if (touch) e.Morale = Fix.Clamp(e.Morale - amount, Fix.Zero, Fix.Hundred);
            }
        }
    }

    /// <summary>도발: 대상 군단이 일정 시간 시전 군단을 공격하러 온다 (명령을 받지 않음).</summary>
    internal sealed class TauntEffect : ISkillEffect
    {
        public string Type => "taunt";
        public void Apply(SkillCast c, SkillPart p)
        {
            var t = c.TargetCorps;
            if (t == null || t.Status != CorpsStatus.Active) return;
            t.AddStatus(new StatusEffect { Id = StatusIds.Taunted, SourceCorpsId = c.Caster.Id, TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("durationSec")) });
            t.AssignOrder(new CorpsOrder { Kind = OrderKinds.Attack, TargetCorpsId = c.Caster.Id });
        }
    }

    /// <summary>화계: 지점 칸에 불을 붙인다. 번짐·타기·피해·지형 변화는 EnvironmentSystem.</summary>
    internal sealed class IgniteEffect : ISkillEffect
    {
        public string Type => "ignite";
        public void Apply(SkillCast c, SkillPart p) => Environment.EnvironmentSystem.Ignite(c.Battle, Cell.Of(c.Point), c.Caster.Team);
    }

    // ---- 조건 ----

    /// <summary>지점이 불이 붙는 지형 (숲·수풀).</summary>
    internal sealed class TargetFlammable : ISkillCondition
    {
        public string Type => "targetFlammable";
        public bool IsMet(SkillCast c, SkillPart p) => c.Battle.Field.RuleAt(Cell.Of(c.Point)).Flammable;
    }

    /// <summary>바람이 분다 (스위치 wind가 꺼지면 불지 않는 것으로 본다).</summary>
    internal sealed class WindBlowing : ISkillCondition
    {
        public string Type => "windBlowing";
        public bool IsMet(SkillCast c, SkillPart p) => c.Battle.Environment.WindStrength > Fix.Zero;
    }

    /// <summary>불을 쓸 수 있는 날씨 (비 아님).</summary>
    internal sealed class FireAllowedByWeather : ISkillCondition
    {
        public string Type => "fireAllowedByWeather";
        public bool IsMet(SkillCast c, SkillPart p) => c.Battle.Environment.FireAllowed;
    }

    internal sealed class OnConcealingTerrain : ISkillCondition
    {
        public string Type => "casterOnConcealingTerrain";
        public bool IsMet(SkillCast c, SkillPart p) => c.Battle.Field.RuleAt(Cell.Of(c.Caster.Anchor)).Conceals;
    }

    internal sealed class NotEngaged : ISkillCondition
    {
        public string Type => "notEngaged";
        public bool IsMet(SkillCast c, SkillPart p) => !c.Caster.Engaged && !c.Caster.BeingHit;
    }

    internal sealed class CasterHasTag : ISkillCondition
    {
        public string Type => "casterHasTag";
        public bool IsMet(SkillCast c, SkillPart p)
        {
            string tag = p.String("tag");
            foreach (var u in c.Caster.Units) if (u.Alive && u.Troop.HasTag(tag)) return true;
            return false;
        }
    }
}
