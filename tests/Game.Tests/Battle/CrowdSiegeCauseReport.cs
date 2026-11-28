using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Game.Battle.Model.Crowd;
using Game.Battle.Model.Geometry;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Math;
using Xunit;
using Xunit.Abstractions;

namespace Game.Tests.Battle
{
    /// <summary>
    /// 측정 전용 (2026-11-27, battle-crowd.md 3.33 "공성 원인 측정"): 전면 공격 편성 (FullAssaultCanBreakTheGate와 같은 편성 · 명령)을
    /// 공성 맵 4개 × 시드 101 ~ 112로 돌려 판마다 성문 · 총대장 · 손실을 적는다. 단언 없음, CROWD_REPORT=1일 때만.
    /// #307 전 코드에서도 그대로 컴파일되게 (그때 있던 API만 씀) — 원인 나눠 재기는 이 파일을 옛 코드 · 되돌린 코드 위에서 같이 돌려 비교.
    /// 돌리기: <c>CROWD_REPORT=1 dotnet test tests/Game.Tests --filter "FullyQualifiedName~CrowdSiegeCauseReport" --logger "console;verbosity=detailed"</c>
    /// </summary>
    public class CrowdSiegeCauseReport
    {
        private readonly ITestOutputHelper _out;
        public CrowdSiegeCauseReport(ITestOutputHelper o) { _out = o; }

        public sealed class ReportFactAttribute : FactAttribute
        {
            public ReportFactAttribute()
            {
                if (Environment.GetEnvironmentVariable("CROWD_REPORT") != "1") Skip = "측정 전용 — CROWD_REPORT=1 일 때만 (battle-crowd.md 3.33 공성 원인 측정)";
            }
        }

        private static readonly DefinitionSet Defs = TestData.Real();
        private static readonly CrowdRules Rules = CrowdRules.Load(new FileDataSource(TestData.DataRoot));
        private static readonly string[] Maps = { "crowd_siege", "crowd_siege_plain", "crowd_siege_river", "crowd_siege_mountain" };
        private const int FirstSeed = 101, Seeds = 12;
        /// <summary>성문 "앞에 붙은" 병사 = 성문 가운데에서 이 거리 안 (전장 칸 4개 = 맵 칸 4개 × 1.25).</summary>
        private static readonly Fix GateReach = Fix.FromInt(5);

        public sealed class Game1
        {
            public string Map; public int Seed;
            public int Winner, BreachTick = -1, EndTick; public string End;
            public int CmdFledTick = -1, CmdFledAlive = -1; public double CmdFledMorale = -1;
            public int AtGate = -1, Bracing = -1, GateIndex = -1; // 성문 열리기 직전 틱 (열린 성문)
            public int AllyStart, AllyAtBreach = -1, AllyEnd, EnemyStart, EnemyAtBreach = -1, EnemyEnd;
            public string FieldSize;
        }

        private static CrowdTerrain TerrainOf(string id)
        {
            var map = MapDef.Load(new FileDataSource(TestData.DataRoot), id);
            return new CrowdTerrain(map, Defs.TerrainRules, Defs.TerrainCombat, CrowdTerrain.CellSizeFor(map, Rules.MapFieldWidth));
        }

        /// <summary>FullAssaultCanBreakTheGate와 같은 편성 · 명령 (공격 보 120 · 창 90 · 기 60 · 궁 90, 충차 · 사다리 · 돌입 대기 · 성벽 쏘기 / 수비 보 · 창 · 궁 기본 수, 적 AI).</summary>
        public static Game1 Assault(string mapId, int seed)
        {
            var au = new List<CrowdUnitSpec>
            {
                new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 120, Commander = true, General = "warrior" },
                new CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman", Count = 90 },
                new CrowdUnitSpec { Row = 0, Col = 2, Troop = "cavalry", Count = 60 },
                new CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer", Count = 90, General = "strategist" },
            };
            var eu = new List<CrowdUnitSpec>
            {
                new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true },
                new CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman" },
                new CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer" },
            };
            var b = new CrowdBattle(Rules, new CrowdSetup { AllyUnits = au, EnemyUnits = eu, AllyAdvance = false, EnemyAdvance = true, Seed = (ulong)seed, Terrain = TerrainOf(mapId) }, Defs.TroopTypes, Defs.TroopCounters);
            b.UseSkillData(Defs.Skills);
            int Id(string place) { foreach (var x in b.TeamArmies(CrowdBattle.Ally)) if (x.Place == place) return x.Id; return -1; }
            b.OrderSiege(Id("front_center"), SiegeTasks.Ram);
            b.OrderSiege(Id("front_left"), SiegeTasks.Ladder);
            b.OrderSiege(Id("front_right"), SiegeTasks.WaitBreach);
            b.OrderSiege(Id("middle_center"), SiegeTasks.ShootWall);
            var g = new Game1 { Map = mapId, Seed = seed, AllyStart = b.AliveCount(CrowdBattle.Ally), EnemyStart = b.AliveCount(CrowdBattle.Enemy), FieldSize = b.Rules.FieldWidth + "x" + b.Rules.FieldDepth };
            var cmd = b.Commander(CrowdBattle.Ally);
            // 성문마다 (평지 성곽은 앞 · 옆 둘): 열리기 직전 틱의 문 앞 공격 · 문 막기 수
            var lastAtGate = new int[b.Gates.Count]; var lastBracing = new int[b.Gates.Count];
            for (int t = 0; t < 20000 && !b.IsOver; t++)
            {
                for (int i = 0; i < b.Gates.Count; i++)
                    if (!b.Gates[i].Open) { lastAtGate[i] = Near(b, CrowdBattle.Ally, b.Gates[i], false); lastBracing[i] = Near(b, CrowdBattle.Enemy, b.Gates[i], true); }
                b.Step();
                if (g.BreachTick < 0 && b.BreachTick >= 0)
                {
                    int gi = 0;
                    for (int i = 0; i < b.Gates.Count; i++) if (b.Gates[i].Open) { gi = i; break; }
                    g.BreachTick = b.BreachTick; g.AtGate = lastAtGate[gi]; g.Bracing = lastBracing[gi]; g.GateIndex = gi;
                    g.AllyAtBreach = b.AliveCount(CrowdBattle.Ally); g.EnemyAtBreach = b.AliveCount(CrowdBattle.Enemy);
                }
                if (g.CmdFledTick < 0 && cmd.General.FledTick >= 0)
                {
                    g.CmdFledTick = cmd.General.FledTick; g.CmdFledAlive = b.AliveCount(CrowdBattle.Ally);
                    double m = 0; int n = 0;
                    foreach (var a in b.TeamArmies(CrowdBattle.Ally)) { m += double.Parse(a.Morale.ToString(), System.Globalization.CultureInfo.InvariantCulture); n++; }
                    g.CmdFledMorale = n > 0 ? m / n : -1;
                }
            }
            if (g.BreachTick < 0) { g.AtGate = lastAtGate.Max(); g.Bracing = lastBracing.Max(); } // 안 열리면 끝날 때 가장 붐빈 성문
            g.Winner = b.Winner; g.End = b.EndReason; g.EndTick = b.Tick;
            g.AllyEnd = b.AliveCount(CrowdBattle.Ally); g.EnemyEnd = b.AliveCount(CrowdBattle.Enemy);
            return g;
        }

        /// <summary>편 team의 살아 있는 병사 가운데 성문 가운데에서 GateReach 안, 성 안쪽(inside) / 바깥쪽에 선 수.</summary>
        private static int Near(CrowdBattle b, int team, CrowdGate gate, bool inside)
        {
            int n = 0;
            Fix r2 = GateReach * GateReach;
            foreach (var a in b.TeamArmies(team))
                foreach (var s in a.Soldiers)
                {
                    if (!s.Alive || Vec.DistanceSquared(s.Pos, gate.Center) > r2) continue;
                    Fix dot = (s.Pos.X - gate.Center.X) * gate.Inward.X + (s.Pos.Y - gate.Center.Y) * gate.Inward.Y;
                    if (inside ? dot > Fix.Zero : dot <= Fix.Zero) n++;
                }
            return n;
        }

        [ReportFact(DisplayName = "측정: 공성 원인 — 전면 공격 편성 판마다 (공성 맵 4 × 시드 101~112, 단언 없음)")]
        public void AssaultDetails()
        {
            string variant = Environment.GetEnvironmentVariable("CROWD_VARIANT") ?? "B";
            var all = new List<Game1>();
            foreach (var m in Maps)
            {
                var games = new Game1[Seeds];
                Parallel.For(0, Seeds, k => games[k] = Assault(m, FirstSeed + k));
                all.AddRange(games);
            }
            foreach (var g in all)
                _out.WriteLine($"GAME {variant} | {g.Map} | {g.Seed} | win {(g.Winner == CrowdBattle.Ally ? "A" : g.Winner == CrowdBattle.Enemy ? "D" : "-")} | breach {g.BreachTick} | end {g.EndTick} {g.End} | cmdFled {g.CmdFledTick} alive {g.CmdFledAlive} morale {g.CmdFledMorale:0.0} | gate#{g.GateIndex} atGate {g.AtGate} bracing {g.Bracing} | ally {g.AllyStart}->{g.AllyAtBreach}->{g.AllyEnd} | enemy {g.EnemyStart}->{g.EnemyAtBreach}->{g.EnemyEnd} | field {g.FieldSize}");
            var sb = new StringBuilder();
            foreach (var m in Maps)
            {
                var gs = all.Where(x => x.Map == m).ToList();
                var br = gs.Where(x => x.BreachTick >= 0).ToList();
                var fled = gs.Where(x => x.CmdFledTick >= 0).ToList();
                sb.AppendLine($"SUMMARY {variant} | {m} | 공격 승 {gs.Count(x => x.Winner == CrowdBattle.Ally)}/{Seeds} | 성문 열림 {br.Count}/{Seeds}"
                    + (br.Count > 0 ? $" (틱 {br.Min(x => x.BreachTick)}~{br.Max(x => x.BreachTick)}, 평균 {br.Average(x => x.BreachTick):0})" : "")
                    + $" | 공격 총대장 달아남 {fled.Count}" + (fled.Count > 0 ? $" (틱 평균 {fled.Average(x => x.CmdFledTick):0}, 그때 공격 병력 {fled.Average(x => x.CmdFledAlive):0} · 사기 {fled.Average(x => x.CmdFledMorale):0})" : "")
                    + $" | 문 앞 공격 {gs.Average(x => x.AtGate):0.0} · 문 막기 {gs.Average(x => x.Bracing):0.0}"
                    + $" | 공격 손실 열리기 전 {br.DefaultIfEmpty().Average(x => x == null ? 0 : x.AllyStart - x.AllyAtBreach):0} · 뒤 {br.DefaultIfEmpty().Average(x => x == null ? 0 : x.AllyAtBreach - x.AllyEnd):0}"
                    + $" | 끝 {string.Join(",", gs.GroupBy(x => x.End).Select(q => q.Key + "×" + q.Count()))} | 전장 {gs[0].FieldSize}");
            }
            _out.WriteLine(sb.ToString());
        }
    }
}
