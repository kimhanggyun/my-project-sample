using Game.Foundation.Math;
using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;

namespace Game.Battle.Model.Ai
{
    public static class AiStanceIds
    {
        /// <summary>가장 가까운 적을 찾아 공격.</summary>
        public const string Aggressive = "aggressive";
        /// <summary>제자리 대기, 감지 거리 안에 적이 오면 공격.</summary>
        public const string Defensive = "defensive";
        /// <summary>숨는 지형에서 복병을 치고 기다림 (복병 스킬이 있을 때).</summary>
        public const string Ambusher = "ambusher";
    }

    /// <summary>AI 군단의 판단 (정해진 틱마다). 새 성향은 구현을 추가하고 등록한다.</summary>
    public interface IAiStance
    {
        string Id { get; }
        void Think(BattleContext ctx, CorpsState corps);
    }

    public sealed class AiRegistry
    {
        private readonly Dictionary<string, IAiStance> _stances = new Dictionary<string, IAiStance>();

        public AiRegistry Register(IAiStance stance)
        {
            _stances[stance.Id] = stance;
            return this;
        }

        public bool Contains(string id) => id != null && _stances.ContainsKey(id);
        public IAiStance Get(string id) => _stances[id];

        public static AiRegistry WithBuiltIns() => new AiRegistry()
            .Register(new AggressiveStance())
            .Register(new DefensiveStance())
            .Register(new AmbusherStance());
    }

    /// <summary>
    /// 사령관 협공 (ai.md 4장·7장, R-75): 난이도가 coordinate인 AI 군단에만.
    /// 아직 붙지 않은 같은 편 공격형 군단 중 가장 먼 군단보다 coordinateGapCells 넘게 적에 가까우면 기다린다.
    /// 붙으면 닿는 거리(aiEngageCells × 1.5) 안에서 같은 편이 이미 싸우는 적을 함께 친다.
    /// </summary>
    internal static class CommanderAi
    {
        private static Fix DistanceToEnemy(BattleContext ctx, CorpsState c)
        {
            var e = ctx.NearestEnemy(c);
            return e == null ? Fix.FromInt(-1) : (e.Anchor - c.Anchor).Length;
        }

        public static bool ShouldWait(BattleContext ctx, CorpsState corps)
        {
            Fix mine = DistanceToEnemy(ctx, corps);
            if (mine < Fix.Zero) return false;
            Fix farthest = mine;
            foreach (var c in ctx.Corps)
            {
                if (c == corps || c.Team != corps.Team || !c.InBattle || !c.AiInCharge || c.AiStance != AiStanceIds.Aggressive) continue;
                if (c.Engaged || c.MeleeContact) continue;
                Fix d = DistanceToEnemy(ctx, c);
                if (d > farthest) farthest = d;
            }
            return farthest - mine > ctx.Defs.Ai.CoordinateGapCells;
        }

        public static CorpsState FocusTarget(BattleContext ctx, CorpsState corps, CorpsState nearest)
        {
            Fix reach = ctx.Combat.AiEngageCells * Fix.FromRatio(3, 2);
            CorpsState best = null;
            Fix bestD = Fix.Zero;
            foreach (var ally in ctx.Corps)
            {
                if (ally == corps || ally.Team != corps.Team || !ally.InBattle || ally.Order.Kind != OrderKinds.Attack) continue;
                var t = ctx.FindCorps(ally.Order.TargetCorpsId);
                if (t == null || !t.InBattle || !ctx.IsVisibleTo(corps.Team, t)) continue;
                Fix d = Vec.DistanceSquared(t.Anchor, corps.Anchor);
                if (d > reach * reach) continue;
                if (best == null || d < bestD) { best = t; bestD = d; }
            }
            return best ?? nearest;
        }
    }

    internal static class AiOrders
    {
        /// <summary>
        /// 가장 가까운 보이는 적이 aiEngageCells 안이면 그 적을 공격 (지금 대상이면 유지).
        /// 멀거나 안 보이면 자기 줄로 곧게 진군 (R-65: 가운데로 모이지 않는다). 줄 끝(전장 끝)에 닿았는데도 멀면 예전처럼 그 적·목격 위치로.
        /// </summary>
        public static void AttackNearest(BattleContext ctx, CorpsState corps)
        {
            var current = corps.Order.Kind == OrderKinds.Attack ? ctx.FindCorps(corps.Order.TargetCorpsId) : null;
            var nearest = ctx.NearestEnemy(corps);
            var engage = ctx.Combat.AiEngageCells;
            bool close = nearest != null && Vec.DistanceSquared(nearest.Anchor, corps.Anchor) <= engage * engage;
            bool coordinate = ctx.Defs.Ai.Difficulties.TryGetValue(corps.AiDifficulty ?? "", out var diff) && diff.Coordinate;
            if (coordinate && close) nearest = CommanderAi.FocusTarget(ctx, corps, nearest); // 같은 편이 싸우는 적을 함께 (R-75)
            if (!close && coordinate && CommanderAi.ShouldWait(ctx, corps))
            {
                // 협공 (R-75): 혼자 앞서 나가지 않고 뒤처진 같은 편을 기다린다
                if (corps.Order.Kind != OrderKinds.Hold) corps.AssignOrder(new CorpsOrder());
                return;
            }
            if (!close)
            {
                bool atEnd = Vec.DistanceSquared(ctx.LaneEnd(corps), corps.Anchor) <= Fix.FromInt(1);
                if (!atEnd)
                {
                    if (corps.Order.Kind != OrderKinds.Advance || !corps.Order.Lane)
                        corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Advance, Mode = MoveModes.AttackMove, Lane = true });
                    return;
                }
                if (nearest == null)
                {
                    // 줄 끝인데 보이는 적이 없으면: 마지막 목격 위치·적 시작 위치로 (보이면 그때 공격)
                    if (corps.Order.Kind != OrderKinds.Advance || corps.Order.Lane) corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Advance, Mode = MoveModes.AttackMove });
                    return;
                }
            }
            if (current != null && current.InBattle && current == nearest) return;
            corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Attack, TargetCorpsId = nearest.Id, Mode = MoveModes.AttackMove });
        }
    }

    internal sealed class AggressiveStance : IAiStance
    {
        public string Id => AiStanceIds.Aggressive;
        public void Think(BattleContext ctx, CorpsState corps) => AiOrders.AttackNearest(ctx, corps);
    }

    internal sealed class DefensiveStance : IAiStance
    {
        public string Id => AiStanceIds.Defensive;
        public void Think(BattleContext ctx, CorpsState corps)
        {
            var nearest = ctx.NearestEnemy(corps);
            var reach = ctx.Combat.PerceptionCells;
            if (nearest != null && Vec.DistanceSquared(nearest.Anchor, corps.Anchor) <= reach * reach)
                AiOrders.AttackNearest(ctx, corps);
            else if (corps.BeingHit && nearest != null)
                AiOrders.AttackNearest(ctx, corps);
            else if (corps.Order.Kind != OrderKinds.Hold)
                corps.AssignOrder(new CorpsOrder());
        }
    }
}
