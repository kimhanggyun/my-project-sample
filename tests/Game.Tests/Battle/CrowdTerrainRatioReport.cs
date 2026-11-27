using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Game.Battle.Model.Crowd;
using Game.Data.Defs;
using Game.Data.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Game.Tests.Battle
{
    /// <summary>
    /// 측정 전용 (2026-11-27, battle-crowd.md "맵별 대조 측정"): R-154 병종 비율(평원 기준)이 샘플전투2 맵 · 지형에서 어떻게 달라지는지 표로 낸다.
    /// 2026-11-26 사용자: 맵 · 지형 · 공성 수치는 이 비율에 맞춰 조절하지 않는다 — 그래서 여기는 아무것도 단언하지 않고 표만 찍는다.
    /// 평소 dotnet test에서는 건너뜀. 돌리려면: <c>CROWD_REPORT=1 dotnet test tests/Game.Tests --filter "FullyQualifiedName~CrowdTerrainRatioReport" --logger "console;verbosity=detailed"</c>
    /// </summary>
    public class CrowdTerrainRatioReport
    {
        private readonly ITestOutputHelper _out;
        public CrowdTerrainRatioReport(ITestOutputHelper o) { _out = o; }

        private const int Seeds = 12;

        /// <summary>CROWD_REPORT=1일 때만 도는 Fact (측정은 오래 걸림, 결과를 단언하지 않음).</summary>
        public sealed class ReportFactAttribute : FactAttribute
        {
            public ReportFactAttribute()
            {
                if (Environment.GetEnvironmentVariable("CROWD_REPORT") != "1") Skip = "측정 전용 — CROWD_REPORT=1 일 때만 (battle-crowd.md 맵별 대조 측정)";
            }
        }

        private static readonly DefinitionSet Defs = TestData.Real();
        private static readonly CrowdRules Rules = CrowdRules.Load(new FileDataSource(TestData.DataRoot));

        /// <summary>조합: R-154 Targets 6개 + 같은 수 상성 3개.</summary>
        private static readonly (string a, int na, string b, int nb, string label)[] Pairs =
            CrowdTroopRatioTests.Targets.Select(t => (t.a, t.na, t.b, t.nb, $"{Short(t.a)} {t.na} : {Short(t.b)} {t.nb}"))
            .Concat(new[] { ("spearman", "cavalry"), ("cavalry", "archer"), ("archer", "spearman") }.Select(p => (p.Item1, 100, p.Item2, 100, $"{Short(p.Item1)} 100 : {Short(p.Item2)} 100")))
            .ToArray();

        private static string Short(string troop) => troop switch
        {
            "spearman" => "창", "cavalry" => "기", "archer" => "궁", "infantry" => "보", _ => troop,
        };

        /// <summary>지형 하나로 덮은 48 × 80 맵 (샘플전투2 맵과 같은 크기 · 시작 자리). 섞인 맵과 따로 지형 효과만 보려고.</summary>
        private static MapDef Uniform(string terrain)
        {
            var rows = new JArray();
            for (int y = 0; y < 80; y++) rows.Add(new string('x', 48));
            return MapDef.From(new JObject
            {
                ["id"] = "uniform_" + terrain,
                ["battleType"] = "field",
                ["legend"] = new JObject { ["x"] = terrain },
                ["rows"] = rows,
                ["spawns"] = new JObject
                {
                    ["teamA"] = new JObject { ["x"] = 24, ["y"] = 72, ["facing"] = "N" },
                    ["teamB"] = new JObject { ["x"] = 24, ["y"] = 8, ["facing"] = "S" },
                },
                ["retreatEdges"] = new JObject { ["teamA"] = "south", ["teamB"] = "north" },
            });
        }

        private static MapDef Map(string id) => MapDef.Load(new FileDataSource(TestData.DataRoot), id);

        private static CrowdTerrain TerrainOf(MapDef map) =>
            map == null ? null : new CrowdTerrain(map, Defs.TerrainRules, Defs.TerrainCombat, CrowdTerrain.CellSizeFor(map, Rules.MapFieldWidth));

        /// <summary>한 판 결과.</summary>
        private struct Game1 { public bool AAlly; public int Winner; public int ATeam; public int ALost, BLost; public string End; }

        /// <summary>CrowdTroopRatioTests.Fight와 같은 판 (스킬 없음 · 서로 진군 · 책사 장수 · 장수 안 쓰러짐), 맵만 고름. 판마다 지형을 새로 만들어 판끼리 나눠 쓰는 것이 없게 (병렬).</summary>
        private static Game1 One(MapDef map, string a, int na, string b, int nb, int k)
        {
            bool aAlly = k % 2 == 0;
            var ua = new CrowdUnitSpec { Row = 0, Col = 1, Troop = a, Count = na, Commander = true, General = "strategist" };
            var ub = new CrowdUnitSpec { Row = 0, Col = 1, Troop = b, Count = nb, Commander = true, General = "strategist" };
            var battle = new CrowdBattle(Rules, new CrowdSetup
            {
                AllyUnits = new List<CrowdUnitSpec> { aAlly ? ua : ub },
                EnemyUnits = new List<CrowdUnitSpec> { aAlly ? ub : ua },
                AllyAdvance = true, EnemyAdvance = true, Seed = (ulong)(101 + k), Terrain = TerrainOf(map),
            }, Defs.TroopTypes, Defs.TroopCounters);
            foreach (var army in battle.Armies) { army.General.MaxHp = 1000000; army.General.Hp = 1000000; }
            for (int t = 0; t < 20000 && !battle.IsOver; t++) battle.Step();
            int aTeam = aAlly ? CrowdBattle.Ally : CrowdBattle.Enemy;
            return new Game1
            {
                AAlly = aAlly, Winner = battle.Winner, ATeam = aTeam, End = battle.EndReason,
                ALost = na - Math.Min(na, battle.AliveCount(aTeam)),
                BLost = nb - Math.Min(nb, battle.AliveCount(1 - aTeam)),
            };
        }

        private sealed class Cell
        {
            public int Wins, Games, Draws, WinsSouth, GamesSouth, WinsNorth, GamesNorth;
            public double ALost, BLost;
        }

        private static Cell Measure(MapDef map, string a, int na, string b, int nb)
        {
            var games = new Game1[Seeds];
            Parallel.For(0, Seeds, k => games[k] = One(map, a, na, b, nb, k));
            var c = new Cell { Games = Seeds };
            foreach (var g in games)
            {
                bool win = g.Winner == g.ATeam;
                if (win) c.Wins++;
                if (g.Winner < 0) c.Draws++;
                // 적은 쪽(a)이 남쪽(teamA = Ally)에 섰는지 북쪽에 섰는지 (고지대 등 한쪽이 높은 맵)
                if (g.AAlly) { c.GamesSouth++; if (win) c.WinsSouth++; } else { c.GamesNorth++; if (win) c.WinsNorth++; }
                c.ALost += g.ALost; c.BLost += g.BLost;
            }
            c.ALost /= Seeds; c.BLost /= Seeds;
            return c;
        }

        /// <summary>대상: 평지(맵 없음 = R-154 기준) · 샘플전투2 맵 전부 · 한 지형으로 덮은 맵.</summary>
        private static readonly (string label, Func<MapDef> map)[] Fields =
        {
            ("평지 (맵 없음, R-154 기준)", () => null),
            ("crowd_plain 평원", () => Map("crowd_plain")),
            ("crowd_forest 숲길", () => Map("crowd_forest")),
            ("crowd_highland 고지전", () => Map("crowd_highland")),
            ("crowd_river 큰 강", () => Map("crowd_river")),
            ("crowd_marsh 늪지", () => Map("crowd_marsh")),
            ("crowd_gorge 협곡", () => Map("crowd_gorge")),
            ("crowd_pass 산길", () => Map("crowd_pass")),
            ("전체 숲", () => Uniform("forest")),
            ("전체 언덕", () => Uniform("hill")),
            ("전체 풀숲", () => Uniform("grass")),
            ("전체 습지", () => Uniform("marsh")),
        };

        [ReportFact(DisplayName = "측정: 맵 · 지형별 병종 비율 (R-154 조합 9개 × 시드 12판, 단언 없음)")]
        public void TerrainRatios()
        {
            var sb = new StringBuilder();
            sb.Append("| 맵 · 지형 |");
            foreach (var p in Pairs) sb.Append(' ').Append(p.label).Append(" |");
            sb.AppendLine();
            sb.Append("|---|").Append(string.Concat(Enumerable.Repeat("---|", Pairs.Length))).AppendLine();
            Cell[] baseRow = null;
            var detail = new StringBuilder();
            foreach (var f in Fields)
            {
                var map = f.map();
                var row = Pairs.Select(p => Measure(map, p.a, p.na, p.b, p.nb)).ToArray();
                if (baseRow == null) baseRow = row;
                sb.Append("| ").Append(f.label).Append(" |");
                for (int i = 0; i < row.Length; i++)
                {
                    var c = row[i];
                    bool far = Math.Abs(c.Wins - baseRow[i].Wins) >= 4; // 평원 대비 크게 달라짐 (12판 중 4판 이상)
                    string w = far ? $"**{c.Wins}**" : c.Wins.ToString();
                    sb.Append(' ').Append(w).Append($" ({c.ALost:0}/{c.BLost:0})");
                    if (c.Draws > 0) sb.Append($" 무{c.Draws}");
                    sb.Append(" |");
                    detail.AppendLine($"DETAIL {f.label} | {Pairs[i].label} | wins {c.Wins}/{c.Games} (south {c.WinsSouth}/{c.GamesSouth}, north {c.WinsNorth}/{c.GamesNorth}) draws {c.Draws} | lost {c.ALost:0.0}/{c.BLost:0.0}");
                }
                sb.AppendLine();
                _out.WriteLine("ROW " + f.label + " done");
            }
            _out.WriteLine("TABLE\n" + sb);
            _out.WriteLine(detail.ToString());
        }

        // ---- 공성 (공격 · 수비 승률만) ----

        /// <summary>공성 한 판: 우리(공격, 남쪽) a병종 na명 · 적(수비, 성 안, 적 AI가 성벽 · 문 막기를 고름) b병종 nb명. 공격 쪽은 진군만 (공성 명령 없음).</summary>
        private static int SiegeOne(MapDef map, string a, int na, string b, int nb, int k, out string end)
        {
            var battle = new CrowdBattle(Rules, new CrowdSetup
            {
                AllyUnits = new List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Troop = a, Count = na, Commander = true, General = "strategist" } },
                EnemyUnits = new List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Troop = b, Count = nb, Commander = true, General = "strategist" } },
                AllyAdvance = true, EnemyAdvance = true, Seed = (ulong)(201 + k), Terrain = TerrainOf(map),
            }, Defs.TroopTypes, Defs.TroopCounters);
            battle.UseSkillData(Defs.Skills);
            foreach (var army in battle.Armies) { army.General.MaxHp = 1000000; army.General.Hp = 1000000; }
            for (int t = 0; t < 20000 && !battle.IsOver; t++) battle.Step();
            end = battle.EndReason;
            return battle.Winner;
        }

        /// <summary>FullAssaultCanBreakTheGate와 같은 편성 · 명령 (공격 보 120 · 창 90 · 기 60 · 궁 90, 충차 · 사다리 · 돌입 대기 · 성벽 쏘기 / 수비 보 · 창 · 궁 기본 수, 적 AI).</summary>
        private static int FullAssaultOne(MapDef map, int k, out string end)
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
            var b = new CrowdBattle(Rules, new CrowdSetup { AllyUnits = au, EnemyUnits = eu, AllyAdvance = false, EnemyAdvance = true, Seed = (ulong)(31 + k), Terrain = TerrainOf(map) }, Defs.TroopTypes, Defs.TroopCounters);
            b.UseSkillData(Defs.Skills);
            int Id(string place) { foreach (var x in b.TeamArmies(CrowdBattle.Ally)) if (x.Place == place) return x.Id; return -1; }
            b.OrderSiege(Id("front_center"), SiegeTasks.Ram);
            b.OrderSiege(Id("front_left"), SiegeTasks.Ladder);
            b.OrderSiege(Id("front_right"), SiegeTasks.WaitBreach);
            b.OrderSiege(Id("middle_center"), SiegeTasks.ShootWall);
            for (int t = 0; t < 20000 && !b.IsOver; t++) b.Step();
            end = b.EndReason + (b.BreachTick >= 0 ? " (breach " + b.BreachTick + ")" : "");
            return b.Winner;
        }

        /// <summary>공격 쪽 승리 수, 그중 시간이 다 돼 병력이 많아서 이긴 판 (⏱), 비김 (무).</summary>
        private static string SiegeCell(int[] res, string[] ends)
        {
            int att = 0, attTime = 0, draws = 0;
            for (int k = 0; k < res.Length; k++)
            {
                if (res[k] == CrowdBattle.Ally) { att++; if (ends[k] == "time") attTime++; }
                else if (res[k] < 0) draws++;
            }
            string s = att.ToString();
            if (attTime > 0) s += attTime == att ? "⏱" : $" (⏱{attTime})";
            if (draws > 0) s += $" 무{draws}";
            return s;
        }

        private static readonly string[] SiegeMaps = { "crowd_siege", "crowd_siege_plain", "crowd_siege_river", "crowd_siege_mountain" };

        [ReportFact(DisplayName = "측정: 공성 맵 공격 · 수비 승률 (조합 9개 양방향 + FullAssault 편성, 시드 12판, 단언 없음)")]
        public void SiegeRatios()
        {
            var sb = new StringBuilder();
            sb.Append("| 공성 맵 | 공격 : 수비 |");
            foreach (var p in Pairs) sb.Append(' ').Append(p.label).Append(" |");
            sb.AppendLine(" 전면 공격 편성 |");
            sb.Append("|---|---|").Append(string.Concat(Enumerable.Repeat("---|", Pairs.Length + 1))).AppendLine();
            var detail = new StringBuilder();
            foreach (var id in SiegeMaps)
            {
                var map = Map(id);
                foreach (bool smallAttacks in new[] { true, false })
                {
                    sb.Append("| ").Append(id).Append(" | ").Append(smallAttacks ? "왼쪽 병종이 공격" : "오른쪽 병종이 공격").Append(" |");
                    foreach (var p in Pairs)
                    {
                        var res = new int[Seeds]; var ends = new string[Seeds];
                        Parallel.For(0, Seeds, k =>
                        {
                            res[k] = smallAttacks ? SiegeOne(map, p.a, p.na, p.b, p.nb, k, out ends[k]) : SiegeOne(map, p.b, p.nb, p.a, p.na, k, out ends[k]);
                        });
                        sb.Append(' ').Append(SiegeCell(res, ends)).Append(" |");
                        int att = res.Count(w => w == CrowdBattle.Ally), draws = res.Count(w => w < 0);
                        detail.AppendLine($"SIEGE {id} | {(smallAttacks ? "small attacks" : "big attacks")} | {p.label} | attacker wins {att}/{Seeds} draws {draws} | {string.Join(",", ends.GroupBy(e => e).Select(g => g.Key + "×" + g.Count()))}");
                    }
                    if (smallAttacks)
                    {
                        var res = new int[Seeds]; var ends = new string[Seeds];
                        Parallel.For(0, Seeds, k => res[k] = FullAssaultOne(map, k, out ends[k]));
                        sb.Append(' ').Append(SiegeCell(res, ends)).Append(" |");
                        int att = res.Count(w => w == CrowdBattle.Ally), draws = res.Count(w => w < 0);
                        detail.AppendLine($"ASSAULT {id} | attacker wins {att}/{Seeds} draws {draws} | {string.Join(",", ends.GroupBy(e => e).Select(g => g.Key + "×" + g.Count()))}");
                    }
                    else sb.Append(" — |");
                    sb.AppendLine();
                }
                _out.WriteLine("SIEGE ROW " + id + " done");
            }
            _out.WriteLine("SIEGE TABLE\n" + sb);
            _out.WriteLine(detail.ToString());
        }

        // ---- 진형 (R-157 진형 길이 뒤) ----

        private static readonly string[] EnemyFormations = { CrowdFormations.Line, CrowdFormations.Thick, CrowdFormations.Thicker, CrowdFormations.Wedge, CrowdFormations.Vee };

        /// <summary>VeeHurtsThickBlockMoreThanLine과 같은 판 (맵 없는 평지 · 기본 설정 = 보병 200 대 200, 우리 진군 · 적 대기): 500틱에 적 손실, 끝까지 돌려 승패.</summary>
        private static (int enemyLost500, int allyLost500, int winner) FormationOne(string ally, string enemy, ulong seed)
        {
            var b = new CrowdBattle(Rules, new CrowdSetup { AllyFormation = ally, EnemyFormation = enemy, Seed = seed });
            int t = 0;
            for (; t < 500 && !b.IsOver; t++) b.Step();
            int e500 = 200 - b.AliveCount(CrowdBattle.Enemy), a500 = 200 - b.AliveCount(CrowdBattle.Ally);
            for (; t < 20000 && !b.IsOver; t++) b.Step();
            return (e500, a500, b.Winner);
        }

        [ReportFact(DisplayName = "측정: 진형 × 적 진형 (보병 200 대 200 평지, 500틱 손실 · 끝까지 승리, 시드 12판, 단언 없음)")]
        public void FormationMatrix()
        {
            // VeeHurtsThickBlockMoreThanLine과 같은 시드 1~3 합 (테스트 출력과 맞춰 보기)
            int vee = 0, line = 0;
            for (ulong seed = 1; seed <= 3; seed++) { vee += FormationOne(CrowdFormations.Vee, CrowdFormations.Thick, seed).enemyLost500; line += FormationOne(CrowdFormations.Line, CrowdFormations.Thick, seed).enemyLost500; }
            _out.WriteLine($"CHECK vee→thick {vee}, line→thick {line} (seeds 1-3, 500 ticks)");
            var sb = new StringBuilder();
            sb.Append("| 우리 진형 (진군) \\ 적 진형 (대기) |");
            foreach (var e in EnemyFormations) sb.Append(' ').Append(e).Append(" |");
            sb.AppendLine();
            sb.Append("|---|").Append(string.Concat(Enumerable.Repeat("---|", EnemyFormations.Length))).AppendLine();
            foreach (var a in CrowdFormations.All)
            {
                sb.Append("| ").Append(a).Append(" |");
                foreach (var e in EnemyFormations)
                {
                    var r = new (int enemyLost500, int allyLost500, int winner)[Seeds];
                    Parallel.For(0, Seeds, k => r[k] = FormationOne(a, e, (ulong)(1 + k)));
                    double el = r.Average(x => x.enemyLost500), al = r.Average(x => x.allyLost500);
                    int wins = r.Count(x => x.winner == CrowdBattle.Ally), draws = r.Count(x => x.winner < 0);
                    sb.Append($" {el:0} / {al:0} · {wins}").Append(draws > 0 ? $" 무{draws}" : "").Append(" |");
                }
                sb.AppendLine();
                _out.WriteLine("FORMATION ROW " + a + " done");
            }
            _out.WriteLine("FORMATION TABLE\n" + sb);
        }

        [ReportFact(DisplayName = "측정: 병렬로 돌려도 같은 결과 (판끼리 나눠 쓰는 것이 없음)")]
        public void ParallelMatchesSequential()
        {
            var map = Map("crowd_forest");
            var seq = Enumerable.Range(0, 4).Select(k => One(map, "infantry", 90, "archer", 100, k)).ToArray();
            var par = new Game1[4];
            Parallel.For(0, 4, k => par[k] = One(map, "infantry", 90, "archer", 100, k));
            for (int k = 0; k < 4; k++)
            {
                Assert.Equal(seq[k].Winner, par[k].Winner);
                Assert.Equal(seq[k].ALost, par[k].ALost);
                Assert.Equal(seq[k].BLost, par[k].BLost);
            }
        }
    }
}
