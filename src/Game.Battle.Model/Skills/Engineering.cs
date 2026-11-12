using System.Collections.Generic;
using Game.Battle.Model.Combat;
using Game.Battle.Model.Damage;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Skills
{
    // 공병·투석기 스킬 (skills.md 3차, R-83): 목책·다리 파괴. 집중 포격은 areaDamage(siege) 조합.

    /// <summary>목책 하나 (R-83). ReadyTick부터 효과, 불이 붙은 칸에 걸리면 무너진다.</summary>
    public sealed class Palisade
    {
        public string Id { get; internal set; }
        public string Team { get; internal set; }
        public Vec Point { get; internal set; }
        public Fix Radius { get; internal set; }
        public long ReadyTick { get; internal set; }
        internal Fix DefensePp { get; set; }
        internal Fix MountedPp { get; set; }
        public bool Ready(BattleContext ctx) => ctx.Tick >= ReadyTick;
        public bool Covers(Vec p) => Vec.DistanceSquared(p, Point) <= Radius * Radius;
    }

    /// <summary>목책 (palisade): 지점에 setupSec 뒤 서는 즉석 방어물. 안에 선 같은 편 소부대는 받는 피해 −defensePp, 기병(mounted)이 치면 그 공격 + mountedPp(음수).</summary>
    internal sealed class PalisadeEffect : ISkillEffect
    {
        public string Type => "palisade";
        public void Apply(SkillCast c, SkillPart p)
        {
            c.Battle.Palisades.Add(new Palisade
            {
                Id = "palisade." + c.Caster.Id + "." + c.Battle.Tick,
                Team = c.Caster.Team,
                Point = c.Point,
                Radius = c.Skill.Radius,
                ReadyTick = c.Battle.Tick + SkillRules.Ticks(c.Battle, p.FixOr("setupSec", Fix.Zero)),
                DefensePp = p.FixOr("defensePp", Fix.Zero),
                MountedPp = p.FixOr("mountedPp", Fix.Zero),
            });
            Events.EventLog.Add(c.Battle, Events.EventKinds.PalisadeBuilt, c.Caster.Id);
        }
    }

    /// <summary>다리 파괴 (destroyBridge): 반경 안의 from 지형(다리)을 to 지형(강)으로. 작업 시간은 스킬 telegraphSec.</summary>
    internal sealed class DestroyBridgeEffect : ISkillEffect
    {
        public string Type => "destroyBridge";
        public void Apply(SkillCast c, SkillPart p)
        {
            var ctx = c.Battle;
            string from = p.String("from"), to = p.String("to");
            Fix r = c.Skill.Radius;
            int ri = (int)r.CeilToLong();
            var center = Cell.Of(c.Point);
            int n = 0;
            for (int y = center.Y - ri; y <= center.Y + ri; y++)
                for (int x = center.X - ri; x <= center.X + ri; x++)
                {
                    var cell = new Cell(x, y);
                    if (!ctx.Field.Map.InBounds(x, y) || ctx.Field.TerrainAt(cell) != from) continue;
                    if (Vec.DistanceSquared(cell.Center, c.Point) > (r + Fix.FromRatio(1, 2)) * (r + Fix.FromRatio(1, 2))) continue;
                    ctx.Field.SetTerrain(cell, to);
                    n++;
                }
            if (n > 0)
            {
                foreach (var k in ctx.Corps) k.ClearPath(); // 길을 다시 찾게
                Events.EventLog.Add(ctx, Events.EventKinds.BridgeDestroyed, c.Caster.Id);
            }
        }
    }

    /// <summary>조건 "targetTerrain": 대상 지점 칸이 그 지형 (다리 파괴 = bridge).</summary>
    internal sealed class TargetTerrain : ISkillCondition
    {
        public string Type => "targetTerrain";
        public bool IsMet(SkillCast c, SkillPart p) => c.Battle.Field.TerrainAt(Cell.Of(c.Point)) == p.String("terrain");
    }

    /// <summary>목책이 불타면 무너진다 (R-83).</summary>
    internal static class PalisadeSystem
    {
        public static void Step(BattleContext ctx)
        {
            for (int i = ctx.Palisades.Count - 1; i >= 0; i--)
                if (ctx.Environment.IsBurning(Cell.Of(ctx.Palisades[i].Point)))
                {
                    ctx.Palisades.RemoveAt(i);
                    Events.EventLog.Add(ctx, Events.EventKinds.PalisadeBurned, null);
                }
        }
    }

    /// <summary>피해 보정 (R-83): 막는 쪽 소부대가 같은 편 목책 안이면 −defensePp, 치는 쪽이 기병이면 + mountedPp.</summary>
    internal sealed class PalisadeSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            foreach (var p in a.Battle.Palisades)
            {
                if (p.Team != a.DefenderCorps.Team || !p.Ready(a.Battle) || !p.Covers(a.Defender.Position)) continue;
                Fix pp = Fix.Zero - p.DefensePp;
                if (a.Attacker.Troop.HasTag("mounted")) pp += p.MountedPp;
                into.Add(new DamageModifier("palisade", pp));
                return; // 겹쳐도 한 번
            }
        }
    }
}
