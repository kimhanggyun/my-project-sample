using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Skills;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Ai
{
    /// <summary>
    /// AI가 스킬 하나를 쓸지 점수로 판단한다 (ai.md 3장). 스킬의 첫 효과 종류로 찾는다.
    /// 점수 0이면 안 쓴다. 새 판단은 구현을 추가하고 등록한다.
    /// </summary>
    public interface IAiSkillRule
    {
        string EffectType { get; }
        /// <summary>점수와 내릴 명령 (CorpsId·Kind·SkillId는 채워 줌). 안 쓰면 점수 0.</summary>
        (int score, BattleCommand command) Evaluate(BattleContext ctx, CorpsState corps, SkillDef skill, AiDifficultyDef difficulty);
    }

    public sealed class AiSkillRules
    {
        private readonly Dictionary<string, IAiSkillRule> _rules = new Dictionary<string, IAiSkillRule>();
        public AiSkillRules Register(IAiSkillRule r) { _rules[r.EffectType] = r; return this; }
        public bool TryGet(string type, out IAiSkillRule r) => _rules.TryGetValue(type, out r);

        public static AiSkillRules WithBuiltIns() => new AiSkillRules()
            .Register(new RallyRule())
            .Register(new RegroupRule())
            .Register(new WarCryRule())
            .Register(new ArrowRainRule())
            .Register(new TauntRule())
            .Register(new RevealRule())
            .Register(new FireAttackRule())
            .Register(new ChallengeRule())
            .Register(new ConfuseRule())
            .Register(new FortifyRule())
            .Register(new FocusFireRule())
            .Register(new QuickFormationRule())
            .Register(new TrapRule())
            .Register(new RockfallRule())
            .Register(new DecoyRule())
            .Register(new FeignedRetreatRule())
            .Register(new ForcedMarchRule())
            .Register(new StealthMarchRule())
            .Register(new SpearheadRule())
            .Register(new DiscordRule())
            .Register(new RumorRule())
            .Register(new CounterIntrigueRule())
            .Register(new SupplyRaidRule())
            .Register(new SupplyDropRule())
            .Register(new FirstAidRule())
            .Register(new PalisadeRule())
            .Register(new FloodRule())
            .Register(new FireShipRule())
            .Register(new RammingRule());
    }

    /// <summary>
    /// 군단 AI의 장수 판단 (스킬·진형). AI도 플레이어와 같은 명령 처리기를 거치고 같은 제한(점수·횟수·시야)을 받는다.
    /// 난이도는 판단 빈도·스킬 사용 확률·상성 대응 확률·정찰 여부만 바꾼다 (ai.md 7장).
    /// </summary>
    internal sealed class AiPlanner
    {
        private readonly AiSkillRules _rules;
        private readonly CommandRegistry _commands;

        public AiPlanner(AiSkillRules rules, CommandRegistry commands)
        {
            _rules = rules;
            _commands = commands;
        }

        public void Think(BattleContext ctx, CorpsState corps)
        {
            var diff = ctx.Defs.Ai.Difficulties[corps.AiDifficulty];
            TryCounterFormation(ctx, corps, diff);
            TryUseSkill(ctx, corps, diff);
        }

        private void TryUseSkill(BattleContext ctx, CorpsState corps, AiDifficultyDef diff)
        {
            if (corps.PendingSkill != null || !_commands.TryGet(CommandKinds.UseSkill, out var handler)) return;
            int best = 0;
            BattleCommand bestCmd = null;
            foreach (var skill in corps.KnownSkills)
            {
                if (skill.Effects.Count == 0 || !_rules.TryGet(skill.Effects[0].Type, out var rule)) continue;
                var (score, cmd) = rule.Evaluate(ctx, corps, skill, diff);
                if (score <= best || cmd == null) continue;
                cmd.CorpsId = corps.Id;
                cmd.Kind = CommandKinds.UseSkill;
                cmd.SkillId = skill.Id;
                cmd.Tick = ctx.Tick;
                if (!handler.IsValid(ctx, corps, cmd)) continue;
                best = score;
                bestCmd = cmd;
            }
            if (bestCmd == null || !ctx.Random.Chance(diff.SkillUseChance)) return;
            handler.Apply(ctx, corps, bestCmd);
        }

        /// <summary>가장 가까운 보이는 적의 진형에 불리하면, 아는 진형 중 그 진형에 유리한 것으로 바꾼다.</summary>
        private void TryCounterFormation(BattleContext ctx, CorpsState corps, AiDifficultyDef diff)
        {
            if (diff.CounterFormationChance <= Fix.Zero || corps.Setup.KnownFormations.Count == 0) return;
            var enemy = ctx.NearestEnemy(corps);
            if (enemy == null) return;
            var wins = ctx.Defs.FormationWins;
            string mine = corps.Formation.Id, theirs = enemy.Formation.Id;
            bool losing = wins.TryGetValue(theirs, out var tw) && tw.Contains(mine);
            if (!losing) return;
            string pick = null;
            foreach (var f in corps.Setup.KnownFormations)
                if (wins.TryGetValue(f, out var fw) && fw.Contains(theirs)) { pick = f; break; }
            if (pick == null || !ctx.Random.Chance(diff.CounterFormationChance)) return;
            if (_commands.TryGet(CommandKinds.ChangeFormation, out var h))
            {
                var cmd = new BattleCommand { Tick = ctx.Tick, CorpsId = corps.Id, Kind = CommandKinds.ChangeFormation, FormationId = pick };
                if (h.IsValid(ctx, corps, cmd)) h.Apply(ctx, corps, cmd);
            }
        }
    }

    // ---- 기본 스킬 판단 (1차) ----

    /// <summary>고무: 사기가 시작보다 많이 떨어졌을 때.</summary>
    internal sealed class RallyRule : IAiSkillRule
    {
        public string EffectType => "morale";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d) =>
            c.Morale <= c.StartMorale - ctx.Defs.Ai.RallyMoraleDrop ? (10, new BattleCommand()) : (0, null);
    }

    /// <summary>재정비: 혼란에 빠진 소부대가 있을 때.</summary>
    internal sealed class RegroupRule : IAiSkillRule
    {
        public string EffectType => "clearConfusion";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            foreach (var u in c.Units) if (u.Alive && u.Confused) return (20, new BattleCommand());
            return (0, null);
        }
    }

    /// <summary>돌격 함성: 접전 중이고 아직 버프가 없을 때.</summary>
    internal sealed class WarCryRule : IAiSkillRule
    {
        public string EffectType => "attackBuff";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d) =>
            c.MeleeContact && !c.HasStatus("buff." + s.Id) ? (8, new BattleCommand()) : (0, null);
    }

    /// <summary>화살비: 사거리 안에 보이는 적 군단이 있으면 그 중심에.</summary>
    internal sealed class ArrowRainRule : IAiSkillRule
    {
        public string EffectType => "areaDamage";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            var e = ctx.NearestEnemy(c);
            if (e == null) return (0, null);
            Fix r = s.Range ?? Fix.FromInt(ctx.Field.Width + ctx.Field.Height);
            if (Vec.DistanceSquared(e.Anchor, c.Anchor) > r * r) return (0, null);
            return (6, new BattleCommand { X = e.Anchor.X, Y = e.Anchor.Y });
        }
    }

    /// <summary>도발: 방어형·매복형이 사거리 안의 적을 끌어들일 때.</summary>
    internal sealed class TauntRule : IAiSkillRule
    {
        public string EffectType => "taunt";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.AiStance == AiStanceIds.Aggressive) return (0, null);
            var e = ctx.NearestEnemy(c);
            if (e == null || e.HasStatus(StatusIds.Taunted)) return (0, null);
            Fix r = s.Range ?? Fix.Zero;
            if (Vec.DistanceSquared(e.Anchor, c.Anchor) > r * r) return (0, null);
            return (5, new BattleCommand { TargetCorpsId = e.Id });
        }
    }

    /// <summary>간파 (정찰하는 난이도만): 보이는 적이 없고, 가까이 지금 안 보이는 숨는 지형(숲)이 있으면 그 칸에.</summary>
    internal sealed class RevealRule : IAiSkillRule
    {
        public string EffectType => "reveal";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (!d.Scouting || ctx.NearestEnemy(c) != null) return (0, null);
            Fix reach = ctx.Defs.Ai.RevealSearchCells;
            Fix r2 = reach * reach;
            var here = Cell.Of(c.Anchor);
            int ri = (int)reach.CeilToLong();
            Cell? best = null;
            Fix bestD = Fix.Zero;
            for (int y = here.Y - ri; y <= here.Y + ri; y++)
                for (int x = here.X - ri; x <= here.X + ri; x++)
                {
                    if (!ctx.Field.Map.InBounds(x, y)) continue;
                    var cell = new Cell(x, y);
                    Fix dd = Vec.DistanceSquared(cell.Center, c.Anchor);
                    if (dd > r2 || !ctx.Field.RuleAt(cell).Conceals) continue;
                    // 숲은 시야 칸이어도 숨은 적이 있을 수 있다 → 아직 간파하지 않은 곳만
                    bool revealed = false;
                    foreach (var z in ctx.Reveals) if (z.Team == c.Team && z.Covers(cell.Center)) revealed = true;
                    if (revealed) continue;
                    if (best == null || dd < bestD) { best = cell; bestD = dd; }
                }
            if (best == null) return (0, null);
            var center = best.Value.Center;
            return (4, new BattleCommand { X = center.X, Y = center.Y });
        }
    }

    /// <summary>
    /// 화계 (ai.md 6장 "순풍 + 상대가 숲에 있음"): 보이는 적이 불붙는 지형에 있고 바람이 불면,
    /// 바람이 불어 오는 쪽(적의 바람 위쪽)에 불을 놓아 적 쪽으로 번지게 한다.
    /// </summary>
    internal sealed class FireAttackRule : IAiSkillRule
    {
        public string EffectType => "ignite";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            var env = ctx.Environment;
            if (env.WindStrength <= Fix.Zero || !env.FireAllowed) return (0, null);
            foreach (var e in ctx.Corps)
            {
                if (e.Team == c.Team || !e.InBattle || !ctx.IsVisibleTo(c.Team, e)) continue;
                if (!ctx.Field.RuleAt(Cell.Of(e.Anchor)).Flammable) continue;
                Vec upwind = e.Anchor - env.WindDirection * Fix.FromInt(2);
                Vec point = ctx.Field.RuleAt(Cell.Of(upwind)).Flammable ? upwind : e.Anchor;
                Fix r = s.Range ?? Fix.FromInt(ctx.Field.Width + ctx.Field.Height);
                if (Vec.DistanceSquared(point, c.Anchor) > r * r) continue;
                return (9, new BattleCommand { X = point.X, Y = point.Y });
            }
            return (0, null);
        }
    }

    // ---- 2차 스킬 판단 (R-79) ----

    internal static class AiTargets
    {
        /// <summary>사거리 안의 보이는 적 군단 중 가장 가까운 것 (사거리 없으면 전장 전체).</summary>
        public static CorpsState InRange(BattleContext ctx, CorpsState c, SkillDef s, System.Func<CorpsState, bool> ok = null)
        {
            Fix r = s.Range ?? Fix.FromInt(ctx.Field.Width + ctx.Field.Height);
            CorpsState best = null;
            Fix bestD = Fix.Zero;
            foreach (var e in ctx.Corps)
            {
                if (e.Team == c.Team || !e.InBattle || !ctx.IsVisibleTo(c.Team, e)) continue;
                if (ok != null && !ok(e)) continue;
                Fix d = Vec.DistanceSquared(e.Anchor, c.Anchor);
                if (d > r * r || (best != null && d >= bestD)) continue;
                best = e;
                bestD = d;
            }
            return best;
        }
    }

    /// <summary>일기토 신청 (난이도 challenges만): 두 군이 맞붙은 뒤(내 군단이 접전 중), 내 장수의 대결 능력치가 상대보다 challengeEdge 이상 높을 때만 (질 싸움은 걸지 않는다).</summary>
    internal sealed class ChallengeRule : IAiSkillRule
    {
        public string EffectType => "challenge";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            var me = c.Setup.General;
            if (!d.Challenges || me == null || c.DuelWith != null || !c.MeleeContact) return (0, null);
            string contestId = s.Effects[0].Has("contest") ? s.Effects[0].String("contest") : "duel";
            if (!ctx.Defs.Contests.TryGetValue(contestId, out var def)) return (0, null);
            Fix mine = me.Stat(def.Stat);
            var e = AiTargets.InRange(ctx, c, s, t => t.Setup.General != null && t.DuelWith == null
                && mine >= t.Setup.General.Stat(def.Stat) + ctx.Defs.Ai.ChallengeEdge);
            return e == null ? (0, null) : (7, new BattleCommand { TargetCorpsId = e.Id });
        }
    }

    /// <summary>혼란: 접전 중인 적 군단(같은 편과 붙은)에 — 혼란은 칼이 닿아 있을 때 가장 아프다.</summary>
    internal sealed class ConfuseRule : IAiSkillRule
    {
        public string EffectType => "confuse";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            var e = AiTargets.InRange(ctx, c, s, t => t.MeleeContact);
            return e == null ? (0, null) : (8, new BattleCommand { TargetCorpsId = e.Id });
        }
    }

    /// <summary>철벽: 맞고 있고 사기가 시작보다 rallyMoraleDrop 넘게 떨어졌는데 아직 철벽이 아닐 때.</summary>
    internal sealed class FortifyRule : IAiSkillRule
    {
        public string EffectType => "fortify";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d) =>
            c.BeingHit && !c.HasStatus("fortify." + s.Id) && c.Morale <= c.StartMorale - ctx.Defs.Ai.RallyMoraleDrop ? (9, new BattleCommand()) : (0, null);
    }

    /// <summary>집중 사격: 사거리 안에서 가장 가까운 적에게 (이미 집중 중이면 안 씀).</summary>
    internal sealed class FocusFireRule : IAiSkillRule
    {
        public string EffectType => "focusFire";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.HasStatus(StatusIds.FocusFire)) return (0, null);
            var e = AiTargets.InRange(ctx, c, s);
            return e == null ? (0, null) : (6, new BattleCommand { TargetCorpsId = e.Id });
        }
    }

    /// <summary>진형 급변: 가장 가까운 적의 진형에 밀리고 이길 진형을 알 때 (다음 판단의 상성 대응이 바로 바뀐다).</summary>
    internal sealed class QuickFormationRule : IAiSkillRule
    {
        public string EffectType => "quickFormation";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.HasStatus(StatusIds.QuickFormation) || d.CounterFormationChance <= Fix.Zero) return (0, null);
            var e = ctx.NearestEnemy(c);
            if (e == null) return (0, null);
            var wins = ctx.Defs.FormationWins;
            if (!wins.TryGetValue(e.Formation.Id, out var tw) || !tw.Contains(c.Formation.Id)) return (0, null);
            foreach (var f in c.Setup.KnownFormations)
                if (wins.TryGetValue(f, out var fw) && fw.Contains(e.Formation.Id)) return (5, new BattleCommand());
            return (0, null);
        }
    }

    // ---- 계략 판단 (R-80) ----

    /// <summary>함정: 아직 붙지 않았고 다가오는 적이 보이면, 적과 나 사이(사거리 안)에 놓는다.</summary>
    internal sealed class TrapRule : IAiSkillRule
    {
        public string EffectType => "trap";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.Engaged || c.MeleeContact) return (0, null);
            var e = ctx.NearestEnemy(c);
            if (e == null) return (0, null);
            Fix r = s.Range ?? Fix.FromInt(8);
            Vec gap = e.Anchor - c.Anchor;
            Fix len = gap.Length;
            if (len <= Fix.FromInt(2) || len > r * Fix.FromInt(2)) return (0, null);
            Fix along = Fix.Min(r, len / Fix.FromInt(2));
            var p = c.Anchor + gap * (along / len);
            return (5, new BattleCommand { X = p.X, Y = p.Y });
        }
    }

    /// <summary>낙석: 내가 더 높은 곳에 있고 사거리 안 보이는 적이 아래에 있으면 그 적에게.</summary>
    internal sealed class RockfallRule : IAiSkillRule
    {
        public string EffectType => "rockfall";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            int mine = ctx.Field.RuleAt(Cell.Of(c.Anchor)).Height;
            var e = AiTargets.InRange(ctx, c, s, t => ctx.Field.RuleAt(Cell.Of(t.Anchor)).Height < mine);
            return e == null ? (0, null) : (8, new BattleCommand { X = e.Anchor.X, Y = e.Anchor.Y });
        }
    }

    /// <summary>위장 깃발: 수비형이 아직 적을 못 봤을 때, 내 옆 4칸에 세워 적을 끌어낸다.</summary>
    internal sealed class DecoyRule : IAiSkillRule
    {
        public string EffectType => "decoy";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.AiStance == AiStanceIds.Aggressive || ctx.NearestEnemy(c) != null) return (0, null);
            var f = ctx.Forward(c);
            var side = new Vec(f.Y, Fix.Zero - f.X);
            var p = ctx.ClampToField(c.Anchor + side * Fix.FromInt(4) + f * Fix.FromInt(2));
            return (3, new BattleCommand { X = p.X, Y = p.Y });
        }
    }

    /// <summary>거짓 퇴각: 접전 중이고 사기가 아직 버틸 만하며, 나를 치는 적이 있을 때.</summary>
    internal sealed class FeignedRetreatRule : IAiSkillRule
    {
        public string EffectType => "feignedRetreat";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (!c.MeleeContact || c.Morale < c.StartMorale - ctx.Defs.Ai.RallyMoraleDrop) return (0, null);
            foreach (var e in ctx.Corps)
                if (e.Team != c.Team && e.InBattle && e.Order.Kind == OrderKinds.Attack && e.Order.TargetCorpsId == c.Id) return (6, new BattleCommand());
            return (0, null);
        }
    }

    // ---- 3·4차 판단 (R-82) ----

    /// <summary>강행군: 진군 중이고 보이는 적이 aiEngageCells × 1.5보다 멀 때 (빨리 붙거나 자리를 잡는다).</summary>
    internal sealed class ForcedMarchRule : IAiSkillRule
    {
        public string EffectType => "forcedMarch";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.Engaged || c.MeleeContact || c.HasStatus("buff." + s.Id) || c.Order.Kind == OrderKinds.Hold) return (0, null);
            var e = ctx.NearestEnemy(c);
            Fix far = ctx.Combat.AiEngageCells * Fix.FromRatio(3, 2);
            return e != null && Vec.DistanceSquared(e.Anchor, c.Anchor) <= far * far ? (0, null) : (4, new BattleCommand());
        }
    }

    /// <summary>은밀 행군: 가림막 안에서 아직 적을 못 봤을 때.</summary>
    internal sealed class StealthMarchRule : IAiSkillRule
    {
        public string EffectType => "stealthMarch";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d) =>
            c.Hidden || ctx.NearestEnemy(c) != null ? (0, null) : (3, new BattleCommand());
    }

    /// <summary>선두 돌파: 접전 중이고 사기가 시작 이상일 때, 사거리 안 가장 가까운 적에게.</summary>
    internal sealed class SpearheadRule : IAiSkillRule
    {
        public string EffectType => "spearhead";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (!c.MeleeContact || c.Morale < c.StartMorale) return (0, null);
            var e = AiTargets.InRange(ctx, c, s);
            return e == null ? (0, null) : (7, new BattleCommand { TargetCorpsId = e.Id });
        }
    }

    /// <summary>이간: 사거리 안 적 장수 지략이 나보다 낮을 때 (성공률이 반을 넘음).</summary>
    internal sealed class DiscordRule : IAiSkillRule
    {
        public string EffectType => "discord";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            var me = c.Setup.General;
            if (me == null) return (0, null);
            var e = AiTargets.InRange(ctx, c, s, t => t.Setup.General != null && t.Setup.General.Intellect < me.Intellect && !t.HasStatus(StatusIds.Discord));
            return e == null ? (0, null) : (6, new BattleCommand { TargetCorpsId = e.Id });
        }
    }

    /// <summary>유언비어: 적 군단이 둘 이상 보일 때.</summary>
    internal sealed class RumorRule : IAiSkillRule
    {
        public string EffectType => "rumor";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            int n = 0;
            foreach (var e in ctx.Corps) if (e.Team != c.Team && e.InBattle && ctx.IsVisibleTo(c.Team, e)) n++;
            return n >= 2 ? (5, new BattleCommand()) : (0, null);
        }
    }

    /// <summary>반간: 적이 보이기 시작하면 미리 건다.</summary>
    internal sealed class CounterIntrigueRule : IAiSkillRule
    {
        public string EffectType => "counterIntrigue";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d) =>
            !c.HasStatus(StatusIds.CounterIntrigue) && ctx.NearestEnemy(c) != null ? (3, new BattleCommand()) : (0, null);
    }

    /// <summary>군량 습격: 사거리 안 적 중 군량이 가장 많은 쪽 (가장 가까운 것으로 대신).</summary>
    internal sealed class SupplyRaidRule : IAiSkillRule
    {
        public string EffectType => "supplyRaid";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            var e = AiTargets.InRange(ctx, c, s, t => t.Supply > 0);
            return e == null ? (0, null) : (6, new BattleCommand { TargetCorpsId = e.Id });
        }
    }

    /// <summary>군량 보급: 사거리 안 아군(나 포함) 중 군량이 가져온 양의 40% 밑인 군단.</summary>
    internal sealed class SupplyDropRule : IAiSkillRule
    {
        public string EffectType => "supplyDrop";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            Fix r = s.Range ?? Fix.FromInt(10);
            foreach (var a in ctx.Corps)
            {
                if (a.Team != c.Team || !a.InBattle || a.StartSupply <= 0) continue;
                if (Vec.DistanceSquared(a.Anchor, c.Anchor) > r * r) continue;
                if (a.Supply * 100 < a.StartSupply * 40) return (7, new BattleCommand { TargetCorpsId = a.Id });
            }
            return (0, null);
        }
    }

    /// <summary>응급 치료: 칼이 맞닿아 있지 않고 병력이 처음의 75% 밑일 때.</summary>
    internal sealed class FirstAidRule : IAiSkillRule
    {
        public string EffectType => "firstAid";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.MeleeContact) return (0, null);
            long start = 0;
            foreach (var u in c.Units) start += u.StartTroops;
            return start > 0 && c.Troops * 4 < start * 3 ? (6, new BattleCommand()) : (0, null);
        }
    }

    /// <summary>목책 (R-83): 수비형이 아직 붙지 않았고 보이는 적이 다가오면 내 앞 2칸에.</summary>
    internal sealed class PalisadeRule : IAiSkillRule
    {
        public string EffectType => "palisade";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.AiStance == AiStanceIds.Aggressive || c.Engaged || c.MeleeContact) return (0, null);
            var e = ctx.NearestEnemy(c);
            if (e == null) return (0, null);
            var gap = e.Anchor - c.Anchor;
            if (gap.Length <= Fix.Zero) return (0, null);
            var p = ctx.ClampToField(c.Anchor + gap.Normalized() * Fix.FromInt(2));
            return (5, new BattleCommand { X = p.X, Y = p.Y });
        }
    }

    /// <summary>수공 (R-84): 내가 차지한 둑에 서 있고, 잠길 곳에 적 소부대가 아군보다 많을 때.</summary>
    internal sealed class FloodRule : IAiSkillRule
    {
        public string EffectType => "floodAttack";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            var dam = Floods.DamAt(ctx, c);
            if (dam == null) return (0, null);
            var center = new Vec(dam.Setup.FloodX, dam.Setup.FloodY);
            Fix r2 = dam.Setup.FloodRadius * dam.Setup.FloodRadius;
            int enemy = 0, own = 0;
            foreach (var k in ctx.Corps)
            {
                if (!k.InBattle) continue;
                foreach (var u in k.Units)
                    if (u.Alive && Vec.DistanceSquared(u.Position, center) <= r2) { if (k.Team == c.Team) own++; else enemy++; }
            }
            return enemy > 0 && enemy > own ? (9, new BattleCommand()) : (0, null);
        }
    }

    /// <summary>화선 (R-85): 사거리 안 보이는 적이 내 병력의 절반 이상일 때 그 군단 중심에.</summary>
    internal sealed class FireShipRule : IAiSkillRule
    {
        public string EffectType => "fireShip";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            var e = AiTargets.InRange(ctx, c, s, t => t.Troops * 2 >= c.Troops);
            return e == null ? (0, null) : (7, new BattleCommand { X = e.Anchor.X, Y = e.Anchor.Y });
        }
    }

    /// <summary>충각 돌격 (R-85): 사거리 안 가장 가까운 적에게.</summary>
    internal sealed class RammingRule : IAiSkillRule
    {
        public string EffectType => "ramming";
        public (int, BattleCommand) Evaluate(BattleContext ctx, CorpsState c, SkillDef s, AiDifficultyDef d)
        {
            if (c.HasStatus("buff." + s.Id)) return (0, null);
            var e = AiTargets.InRange(ctx, c, s);
            return e == null ? (0, null) : (7, new BattleCommand { TargetCorpsId = e.Id });
        }
    }

    /// <summary>
    /// 매복형 (ai.md 5장): 숨는 지형에 있으면 복병을 치고 기다린다. 복병이 없거나 실패·기습 뒤에는 공격형처럼 싸운다.
    /// </summary>
    internal sealed class AmbusherStance : IAiStance
    {
        public string Id => AiStanceIds.Ambusher;
        private readonly IAiStance _fallback = new AggressiveStance();

        public void Think(BattleContext ctx, CorpsState corps)
        {
            if (corps.Hidden || corps.HasStatus(StatusIds.AmbushSetup)) return; // 숨어서 기다림
            if (!corps.SkillPoints.CanUse("ambush", Fix.Zero, true) || corps.BeingHit || corps.Engaged)
            {
                _fallback.Think(ctx, corps);
                return;
            }
            var cmd = new BattleCommand { Tick = ctx.Tick, CorpsId = corps.Id, Kind = CommandKinds.UseSkill, SkillId = "ambush" };
            var h = new UseSkillHandler();
            if (h.IsValid(ctx, corps, cmd)) h.Apply(ctx, corps, cmd);
            else _fallback.Think(ctx, corps);
        }
    }
}
