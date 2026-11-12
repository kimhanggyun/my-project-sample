using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Victory;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Skills
{
    /// <summary>
    /// 수공 (floodAttack, R-84): 내 군단이 차지한 둑 반경 안에서 쓴다 (조건 atOwnDam). 전조(물소리) 뒤 둑이 터진다.
    /// 잠기는 원 안의 칸 중 높이 0이고 지나갈 수 있던 칸(강·건물·절벽 제외) → flood 지형 floodSec 동안, 그 뒤 mud 지형 mudSec 동안, 그 뒤 원래 지형.
    /// 터지는 순간 원 안의 모든 소부대(양쪽 다)가 병력 lossPct% 손실·confuseSec 혼란, 원 안의 불은 꺼진다.
    /// </summary>
    internal sealed class FloodAttackEffect : ISkillEffect
    {
        public string Type => "floodAttack";
        public void Apply(SkillCast c, SkillPart p)
        {
            var ctx = c.Battle;
            var dam = Floods.DamAt(ctx, c.Caster);
            if (dam == null) return;
            dam.Broken = true;
            var center = new Vec(dam.Setup.FloodX, dam.Setup.FloodY);
            Fix r = dam.Setup.FloodRadius;
            string flood = p.String("flood"), mud = p.String("mud");
            var wave = new FloodWave
            {
                MudTick = ctx.Tick + SkillRules.Ticks(ctx, p.Fix("floodSec")),
                DryTick = ctx.Tick + SkillRules.Ticks(ctx, p.Fix("floodSec") + p.Fix("mudSec")),
                Flood = flood,
                Mud = mud,
            };
            int ri = (int)r.CeilToLong();
            var mid = Cell.Of(center);
            for (int y = mid.Y - ri; y <= mid.Y + ri; y++)
                for (int x = mid.X - ri; x <= mid.X + ri; x++)
                {
                    var cell = new Cell(x, y);
                    if (!ctx.Field.Map.InBounds(x, y) || Vec.DistanceSquared(cell.Center, center) > r * r) continue;
                    var rule = ctx.Field.RuleAt(cell);
                    if (rule.Height > 0 || rule.BlocksEveryone) continue;
                    wave.Cells.Add((cell, ctx.Field.TerrainAt(cell)));
                    ctx.Field.SetTerrain(cell, flood);
                    ctx.Environment.Burning.Remove(y * ctx.Field.Width + x); // 불 끔
                }
            ctx.Floods.Add(wave);
            long ticks = SkillRules.Ticks(ctx, p.FixOr("confuseSec", Fix.Zero));
            Fix pct = p.FixOr("lossPct", Fix.Zero);
            foreach (var k in ctx.Corps)
            {
                if (!k.InBattle) continue;
                bool hit = false;
                foreach (var u in k.Units)
                {
                    if (!u.Alive || !wave.Covers(Cell.Of(u.Position))) continue;
                    u.PendingLoss += Fix.FromInt(u.Troops) * pct / Fix.Hundred;
                    if (ticks > 0) Events.Confusion.Apply(ctx, u, ticks);
                    hit = true;
                }
                if (hit) { k.BeingHit = true; Events.EventLog.Add(ctx, Events.EventKinds.Flooded, k.Id, c.Caster.Id); }
                k.ClearPath();
            }
            Events.EventLog.Add(ctx, Events.EventKinds.DamBroken, c.Caster.Id, dam.Setup.Id);
        }
    }

    /// <summary>조건 "atOwnDam": 내 군단 중심이 내 팀이 차지한 터지지 않은 둑 반경 안.</summary>
    internal sealed class AtOwnDam : ISkillCondition
    {
        public string Type => "atOwnDam";
        public bool IsMet(SkillCast c, SkillPart p) => Floods.DamAt(c.Battle, c.Caster) != null;
    }

    /// <summary>한 번 터진 물 (지형을 되돌릴 때 쓴다).</summary>
    public sealed class FloodWave
    {
        public List<(Cell cell, string original)> Cells { get; } = new List<(Cell, string)>();
        public long MudTick { get; internal set; }
        public long DryTick { get; internal set; }
        internal string Flood { get; set; }
        internal string Mud { get; set; }
        internal bool Muddy { get; set; }
        public bool Covers(Cell c)
        {
            foreach (var (cell, _) in Cells) if (cell.Equals(c)) return true;
            return false;
        }
    }

    public static class Floods
    {
        /// <summary>군단이 서 있는, 그 팀이 차지했고 아직 안 터진 둑.</summary>
        public static MapObjectState DamAt(BattleContext ctx, CorpsState c)
        {
            foreach (var o in ctx.Objects)
                if (o.Setup.Type == MapObjectSetup.Dam && !o.Broken && o.Owner == c.Team
                    && Vec.DistanceSquared(o.Center, c.Anchor) <= o.Setup.Radius * o.Setup.Radius) return o;
            return null;
        }

        /// <summary>틱마다: 물 → 진흙 → 원래 지형.</summary>
        internal static void Step(BattleContext ctx)
        {
            for (int i = ctx.Floods.Count - 1; i >= 0; i--)
            {
                var w = ctx.Floods[i];
                if (!w.Muddy && ctx.Tick >= w.MudTick)
                {
                    w.Muddy = true;
                    foreach (var (cell, _) in w.Cells) ctx.Field.SetTerrain(cell, w.Mud);
                    foreach (var k in ctx.Corps) k.ClearPath();
                }
                if (ctx.Tick >= w.DryTick)
                {
                    foreach (var (cell, original) in w.Cells) ctx.Field.SetTerrain(cell, original);
                    foreach (var k in ctx.Corps) k.ClearPath();
                    ctx.Floods.RemoveAt(i);
                }
            }
        }
    }
}
