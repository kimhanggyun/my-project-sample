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
            .Register(new DefensiveStance());
    }

    internal static class AiOrders
    {
        /// <summary>지금 공격 중인 대상이 가장 가까운 보이는 적이면 유지, 아니면 그 적. 보이는 적이 없으면 진군.</summary>
        public static void AttackNearest(BattleContext ctx, CorpsState corps)
        {
            var current = corps.Order.Kind == OrderKinds.Attack ? ctx.FindCorps(corps.Order.TargetCorpsId) : null;
            var nearest = ctx.NearestEnemy(corps);
            if (nearest == null)
            {
                // 보이는 적이 없으면 진군 (마지막 목격 위치·적 시작 위치로, 보이면 그때 공격)
                if (corps.Order.Kind != OrderKinds.Advance) corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Advance, Mode = MoveModes.AttackMove });
                return;
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
