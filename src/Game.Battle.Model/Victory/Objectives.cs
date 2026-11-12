using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Victory
{
    /// <summary>맵 오브젝트·목표 (battle.md 6장, R-78). Type: depot(보급소) · watchtower(망루) · zone(점령 구역).</summary>
    public sealed class MapObjectSetup
    {
        public const string Depot = "depot";
        public const string Watchtower = "watchtower";
        public const string Zone = "zone";

        public string Id { get; set; }
        public string Type { get; set; }
        public Fix X { get; set; }
        public Fix Y { get; set; }
        public Fix Radius { get; set; }
        /// <summary>보급소: 1초에 채우는 군량. 망루: 시야 칸.</summary>
        public Fix Value { get; set; }
        /// <summary>점령 구역: 차지해야 하는 팀.</summary>
        public string Team { get; set; }
        /// <summary>점령 구역: 혼자 차지해야 하는 초.</summary>
        public Fix Seconds { get; set; }
    }

    /// <summary>버티기 (R-78): Team이 Seconds까지 전장에 군단을 남기면 다른 팀이 진다.</summary>
    public sealed class HoldOutSetup
    {
        public string Team { get; set; }
        public Fix Seconds { get; set; }
    }

    public sealed class MapObjectState
    {
        public MapObjectSetup Setup { get; internal set; }
        /// <summary>지금 차지한 팀 (아무도 안 왔으면 null). 다툼 중이면 앞 주인 그대로.</summary>
        public string Owner { get; internal set; }
        /// <summary>점령 구역: 목표 팀이 혼자 차지한 틱 (다른 팀이 들어오거나 비우면 0으로).</summary>
        public long HeldTicks { get; internal set; }
        public Vec Center => new Vec(Setup.X, Setup.Y);
    }

    /// <summary>
    /// 틱마다 (R-78): 반경 안에 군단 중심이 있는 팀이 하나뿐이면 그 팀이 차지한다 (아무도 없거나 다투면 주인 그대로).
    /// 보급소: 주인 팀의 반경 안 군단 군량을 1초에 Value씩 (가져온 양까지) 채운다. 망루: 주인 팀이 그 자리에서 Value칸을 본다 (VisionSystem).
    /// 점령 구역: 목표 팀만 있으면 HeldTicks 증가, 아니면 0.
    /// </summary>
    internal static class ObjectiveSystem
    {
        public static void Init(BattleContext ctx)
        {
            if (ctx.Setup.Objects == null) return;
            foreach (var o in ctx.Setup.Objects) ctx.Objects.Add(new MapObjectState { Setup = o });
        }

        public static void Step(BattleContext ctx)
        {
            var dt = ctx.Combat.TickSeconds;
            foreach (var o in ctx.Objects)
            {
                string only = null;
                bool contested = false, any = false;
                Fix r2 = o.Setup.Radius * o.Setup.Radius;
                foreach (var c in ctx.Corps)
                {
                    if (!c.InBattle || Vec.DistanceSquared(c.Anchor, o.Center) > r2) continue;
                    if (!any) { only = c.Team; any = true; }
                    else if (only != c.Team) contested = true;
                }
                if (any && !contested) o.Owner = only;
                if (o.Setup.Type == MapObjectSetup.Zone)
                    o.HeldTicks = any && !contested && only == o.Setup.Team ? o.HeldTicks + 1 : 0;
                if (o.Setup.Type == MapObjectSetup.Depot && o.Owner != null)
                    foreach (var c in ctx.Corps)
                    {
                        if (c.Team != o.Owner || !c.InBattle || Vec.DistanceSquared(c.Anchor, o.Center) > r2) continue;
                        // 군량 = 가져온 양 − 누적 소비 (SupplySystem)라서 누적 소비를 그만큼 되돌린다 (가져온 양까지)
                        Fix add = o.Setup.Value * dt;
                        long back = (add * Fix.FromInt(6000) / dt).FloorToLong();
                        c.SupplyAccumulator = System.Math.Max(0, c.SupplyAccumulator - back);
                        long consumed = (Fix.FromInt(c.SupplyAccumulator) * dt / 6000).FloorToLong();
                        c.Supply = System.Math.Max(0, c.StartSupply - consumed);
                    }
            }
        }

        public static long ZoneTicks(BattleContext ctx, MapObjectState o) => (o.Setup.Seconds / ctx.Combat.TickSeconds).CeilToLong();
    }

    /// <summary>군량 소진 (R-78): 전장에 있는 그 팀 군단 모두 군량이 0 (가져온 군량이 있었던 군단만 센다).</summary>
    internal sealed class SupplyOutCondition : IDefeatCondition
    {
        public string Id => VictoryConditionIds.SupplyOut;
        public bool IsDefeated(BattleContext ctx, string team)
        {
            bool any = false;
            foreach (var c in ctx.Corps)
            {
                if (c.Team != team || !c.InBattle || c.StartSupply <= 0) continue;
                any = true;
                if (c.Supply > 0) return false;
            }
            return any;
        }
    }

    /// <summary>버티기 (R-78): 정한 팀이 아니고, 시간이 다 됐는데 정한 팀이 전장에 남아 있으면 진다.</summary>
    internal sealed class HoldOutCondition : IDefeatCondition
    {
        public string Id => VictoryConditionIds.HoldOut;
        public bool IsDefeated(BattleContext ctx, string team)
        {
            var h = ctx.Setup.HoldOut;
            if (h == null || h.Team == team) return false;
            if (ctx.Combat.TickSeconds * ctx.Tick < h.Seconds) return false;
            foreach (var c in ctx.Corps) if (c.Team == h.Team && c.InBattle) return true;
            return false;
        }
    }

    /// <summary>구역 점령 (R-78): 점령 구역의 목표 팀이 아니고, 목표 팀이 그 구역을 Seconds 동안 혼자 차지하면 진다.</summary>
    internal sealed class ZoneCondition : IDefeatCondition
    {
        public string Id => VictoryConditionIds.Zone;
        public bool IsDefeated(BattleContext ctx, string team)
        {
            foreach (var o in ctx.Objects)
                if (o.Setup.Type == MapObjectSetup.Zone && o.Setup.Team != team && o.HeldTicks >= ObjectiveSystem.ZoneTicks(ctx, o)) return true;
            return false;
        }
    }
}
