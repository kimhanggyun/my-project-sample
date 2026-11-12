using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Vision
{
    /// <summary>
    /// 한 팀의 시야 (battle.md 2.10). 아군 전체 시야를 합친 것 하나를 팀 전체(플레이어 화면·AI)가 함께 쓴다.
    /// 칸 상태 3단계: 미탐색 / 탐색함(지금은 안 보임) / 지금 보임.
    /// </summary>
    public sealed class TeamVision
    {
        public string Team { get; internal set; }
        public bool[] VisibleCells { get; internal set; }
        public bool[] ExploredCells { get; internal set; }
        /// <summary>지금 보이는 적 군단 id.</summary>
        public HashSet<string> VisibleCorps { get; } = new HashSet<string>();
        /// <summary>적 군단 id → 마지막으로 본 위치 (흐린 표시, AI 진군 목표).</summary>
        public Dictionary<string, Vec> LastSeen { get; } = new Dictionary<string, Vec>();
    }

    /// <summary>간파로 밝힌 범위. 그동안 숨은 부대까지 보이고, 숨은 부대는 들킨다.</summary>
    public sealed class RevealZone
    {
        public string Team { get; internal set; }
        public Vec Center { get; internal set; }
        public Fix Radius { get; internal set; }
        /// <summary>간파를 쓴 군단 (없으면 null).</summary>
        public string SourceCorpsId { get; internal set; }
        public long TicksLeft { get; internal set; }

        public bool Covers(Vec p) => Vec.DistanceSquared(p, Center) <= Radius * Radius;
    }

    /// <summary>
    /// 시야 계산 (일정 틱마다). 소부대 시야 = 기준 + (시야 능력치 − 50) × 계수 + (장수 지략 − 50) × 계수 + 높이 보너스
    /// − 숨는 지형 안 감소. 숨는 지형(숲 등) 안의 적은 가까이(concealedSeenCells) 가야 보인다.
    /// 은신한 군단은 간파 범위 안에서만 보이고, 보이는 순간 은신이 풀린다. 규칙 스위치 fogOfWar가 꺼지면 모두 보인다.
    /// </summary>
    internal static class VisionSystem
    {
        public const string FogOfWarFlag = "fogOfWar";
        public const string FogMemoryFlag = "fogMemory";
        private static readonly Fix Fifty = Fix.FromInt(50);

        public static void Init(BattleContext ctx)
        {
            int n = ctx.Field.Width * ctx.Field.Height;
            bool memory = ctx.IsEnabled(FogMemoryFlag);
            foreach (var team in ctx.Teams)
            {
                var v = new TeamVision { Team = team, VisibleCells = new bool[n], ExploredCells = new bool[n] };
                if (memory && ctx.Setup.InitialExplored.TryGetValue(team, out var initial) && initial != null)
                    for (int i = 0; i < n && i < initial.Length; i++) v.ExploredCells[i] = initial[i];
                ctx.Vision[team] = v;
            }
            Update(ctx);
        }

        public static void Step(BattleContext ctx)
        {
            for (int i = ctx.Reveals.Count - 1; i >= 0; i--)
                if (--ctx.Reveals[i].TicksLeft <= 0) ctx.Reveals.RemoveAt(i);
            if (ctx.Tick % ctx.Combat.VisionUpdateEveryTicks == 0) Update(ctx);
        }

        public static void Update(BattleContext ctx)
        {
            bool fog = ctx.IsEnabled(FogOfWarFlag);
            foreach (var v in ctx.Vision.Values)
            {
                System.Array.Clear(v.VisibleCells, 0, v.VisibleCells.Length);
                v.VisibleCorps.Clear();
                if (!fog)
                {
                    for (int i = 0; i < v.VisibleCells.Length; i++) { v.VisibleCells[i] = true; v.ExploredCells[i] = true; }
                    foreach (var c in ctx.Corps)
                        if (c.Team != v.Team && c.InBattle) { v.VisibleCorps.Add(c.Id); v.LastSeen[c.Id] = c.Anchor; }
                    continue;
                }
                var observers = new List<(Vec pos, Fix radius)>();
                foreach (var c in ctx.Corps)
                {
                    if (c.Team != v.Team || !c.InBattle || c.HasStatus(StatusIds.Discord)) continue; // 이간: 그 군단의 눈은 같은 편에 보태지 않는다 (R-82)
                    foreach (var u in c.Units)
                    {
                        if (!u.Alive) continue;
                        Fix r = Radius(ctx, c, u);
                        observers.Add((u.Position, r));
                        MarkCircle(ctx, v, u.Position, r);
                    }
                }
                foreach (var z in ctx.Reveals)
                    if (z.Team == v.Team) MarkCircle(ctx, v, z.Center, z.Radius);
                // 망루 (R-78): 차지한 팀이 그 자리에서 본다
                foreach (var o in ctx.Objects)
                    if (o.Setup.Type == Victory.MapObjectSetup.Watchtower && o.Owner == v.Team)
                    {
                        observers.Add((o.Center, o.Setup.Value));
                        MarkCircle(ctx, v, o.Center, o.Setup.Value);
                    }

                foreach (var e in ctx.Corps)
                {
                    if (e.Team == v.Team || !e.InBattle) continue;
                    if (SeenBy(ctx, v.Team, e, observers))
                    {
                        v.VisibleCorps.Add(e.Id);
                        v.LastSeen[e.Id] = e.Anchor;
                    }
                }
            }
        }

        /// <summary>적 군단 e가 보이는가. 은신이면 간파 범위로만 (보이면 은신이 풀린다).</summary>
        private static bool SeenBy(BattleContext ctx, string team, CorpsState e, List<(Vec pos, Fix radius)> observers)
        {
            foreach (var z in ctx.Reveals)
            {
                if (z.Team != team) continue;
                foreach (var u in e.Units)
                    if (u.Alive && z.Covers(u.Position))
                    {
                        if ((e.Hidden || e.HasStatus(StatusIds.AmbushSetup)) && z.SourceCorpsId != null)
                            Events.EventLog.Add(ctx, Events.EventKinds.HiddenFound, z.SourceCorpsId, e.Id);
                        if (e.Hidden) Skills.StealthSystem.Expose(e);
                        e.RemoveStatus(StatusIds.AmbushSetup);
                        return true;
                    }
            }
            if (e.Hidden) return false;
            Fix concealed = ctx.Combat.VisionConcealedSeenCells;
            foreach (var u in e.Units)
            {
                if (!u.Alive) continue;
                bool inCover = ctx.Field.RuleAt(Cell.Of(u.Position)).Conceals;
                foreach (var o in observers)
                {
                    Fix reach = inCover ? Fix.Min(o.radius, concealed) : o.radius;
                    if (Vec.DistanceSquared(o.pos, u.Position) <= reach * reach) return true;
                }
            }
            return false;
        }

        public static Fix Radius(BattleContext ctx, CorpsState c, SubUnitState u)
        {
            var k = ctx.Combat;
            Fix r = k.VisionCellsAtStat50
                    + (u.Troop.Stats.Get("vision") - Fifty) * k.VisionCellsPerStatPoint
                    + (c.Setup.General.Intellect - Fifty) * k.VisionIntellectCellsPerPoint;
            var rule = ctx.Field.RuleAt(Cell.Of(u.Position));
            r += k.VisionCellsPerHeightLevel * rule.Height;
            if (rule.Conceals) r -= k.VisionInsideConcealPenaltyCells;
            r += ctx.Environment.Sum(x => x.VisionCells); // 비·안개·밤
            return Fix.Max(Fix.OneValue, r);
        }

        private static void MarkCircle(BattleContext ctx, TeamVision v, Vec center, Fix radius)
        {
            int w = ctx.Field.Width, h = ctx.Field.Height;
            int r = (int)radius.CeilToLong();
            var c = Cell.Of(center);
            Fix r2 = radius * radius;
            for (int y = c.Y - r; y <= c.Y + r; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (x < 0 || x >= w) continue;
                    if (Vec.DistanceSquared(new Cell(x, y).Center, center) > r2) continue;
                    v.VisibleCells[y * w + x] = true;
                    v.ExploredCells[y * w + x] = true;
                }
            }
        }
    }
}

namespace Game.Battle.Model.Vision
{
    internal static class VisibleCorpsOrder
    {
        /// <summary>HashSet은 순서가 정해져 있지 않으므로 군단 순서로 돌려준다 (지문 계산용).</summary>
        public static IEnumerable<string> OrderedIds(this HashSet<string> set, BattleContext ctx)
        {
            foreach (var c in ctx.Corps) if (set.Contains(c.Id)) yield return c.Id;
        }
    }
}
