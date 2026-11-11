using System;
using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Ai;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Field;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Data.Validation;
using Game.Economy;
using Game.Foundation.Math;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>전투 시뮬레이션 규칙 (verification R-16, R-17, R-20, R-6, R-29 …).</summary>
    public class BattleRuleTests
    {
        [Fact(DisplayName = "R-16 같은 능력치 진형 대결: 상성표의 유리한 쪽이 이긴다 (전체 유리 조합, 양쪽 자리 바꿔서)")]
        public void R16()
        {
            var d = Defs;
            var failures = new List<string>();
            int pairs = 0;
            foreach (var a in d.Formations.Keys)
                foreach (var b in d.Formations.Keys)
                {
                    if (!DataValidator.Wins(d, a, b)) continue;
                    pairs++;
                    foreach (bool swap in new[] { false, true })
                    {
                        var ca = Corps("A", swap ? "teamB" : "teamA", a);
                        var cb = Corps("B", swap ? "teamA" : "teamB", b);
                        var r = BattleSimulation.Run(Setup("test_plain", 7, null, ca, cb));
                        if (r.Winner != ca.Team) failures.Add($"{a} > {b} (자리 바꿈 {swap}): 승자 {r.Winner ?? "무승부"}");
                    }
                }
            Assert.True(pairs > 20, "상성 조합이 너무 적습니다.");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact(DisplayName = "R-17 숲에서는 보병이 기병을 이기고, 평지에서는 기병이 이긴다 (지형 전투 보정)")]
        public void R17TerrainFlipsOutcome()
        {
            var forest = BattleSimulation.Run(Setup("test_forest", 1, null, Corps("A", "teamA", "square", "infantry"), Corps("B", "teamB", "square", "cavalry")));
            var plain = BattleSimulation.Run(Setup("test_plain", 1, null, Corps("A", "teamA", "square", "infantry"), Corps("B", "teamB", "square", "cavalry")));
            Assert.Equal("teamA", forest.Winner);
            Assert.Equal("teamB", plain.Winner);
        }

        [Fact(DisplayName = "병종 상성: 창병 > 기병, 궁병 > 창병")]
        public void TroopCounters()
        {
            Assert.Equal("teamA", BattleSimulation.Run(Setup("test_plain", 1, null, Corps("A", "teamA", "square", "spearman"), Corps("B", "teamB", "square", "cavalry"))).Winner);
            Assert.Equal("teamA", BattleSimulation.Run(Setup("test_plain", 1, null, Corps("A", "teamA", "square", "archer"), Corps("B", "teamB", "square", "spearman"))).Winner);
        }

        [Fact(DisplayName = "R-20 대기 중에도 사거리 안의 적은 공격한다")]
        public void R20HoldStillFights()
        {
            var setup = Setup("test_plain", 1, null,
                Corps("A", "teamA", "square", controller: Controllers.Player),
                Corps("B", "teamB", "square"));
            var sim = new BattleSimulation(setup);
            var start = sim.Context.Corps[0].Anchor;
            long bStart = sim.Context.Corps[1].Troops;
            for (int i = 0; i < 600 && !sim.IsOver; i++) sim.Step();
            Assert.Equal(OrderKinds.Hold, sim.Context.Corps[0].Order.Kind);
            Assert.Equal(start, sim.Context.Corps[0].Anchor); // 움직이지 않았다
            Assert.True(sim.Context.Corps[1].Troops < bStart, "대기 중인 군단이 반격하지 않았습니다.");
        }

        [Fact(DisplayName = "R-20 강행 이동은 사거리 안의 적을 공격하되 멈추지 않는다")]
        public void R20ForcedMoveFightsWithoutStopping()
        {
            var setup = Setup("test_plain", 1, null,
                Corps("A", "teamA", "square", controller: Controllers.Player),
                Corps("B", "teamB", "square", stance: AiStanceIds.Defensive));
            var sim = new BattleSimulation(setup);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.MoveTo, X = Fix.FromInt(12), Y = Fix.FromInt(2), Mode = MoveModes.Forced }));
            long bStart = sim.Context.Corps[1].Troops;
            bool fought = false;
            Fix lastY = sim.Context.Corps[0].Anchor.Y;
            for (int i = 0; i < 400 && !sim.IsOver; i++)
            {
                sim.Step();
                var a = sim.Context.Corps[0];
                if (a.Engaged)
                {
                    fought = true;
                    if (a.Status == CorpsStatus.Active && a.Order.Mode == MoveModes.Forced) Assert.True(a.Anchor.Y < lastY, $"강행 중 교전으로 멈췄습니다. tick {sim.Tick} {a.Anchor} {a.Order.Kind} {a.Order.Mode} last {lastY}");
                }
                lastY = a.Anchor.Y;
            }
            Assert.True(fought);
            Assert.True(sim.Context.Corps[1].Troops < bStart);
        }

        [Fact(DisplayName = "기병은 산을 지나지 못해 돌아간다 (지형 이동 규칙)")]
        public void CavalryAvoidsMountain()
        {
            var setup = Setup("test_mountain_pass", 1, null,
                Corps("A", "teamA", "square", "cavalry", controller: Controllers.Player),
                Corps("B", "teamB", "square", stance: AiStanceIds.Defensive));
            var sim = new BattleSimulation(setup);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.MoveTo, X = Fix.FromInt(12), Y = Fix.FromInt(1) }));
            var cavalry = Defs.TroopTypes["cavalry"];
            bool passedMountainRows = false;
            for (int i = 0; i < 600 && !sim.IsOver; i++)
            {
                sim.Step();
                var a = sim.Context.Corps[0];
                Assert.True(sim.Context.Field.CanEnter(Cell.Of(a.Anchor), cavalry), $"기병이 {Cell.Of(a.Anchor)}({sim.Context.Field.TerrainAt(a.Anchor)})에 들어갔습니다.");
                foreach (var u in a.Units) Assert.True(sim.Context.Field.CanEnter(Cell.Of(u.Position), cavalry));
                if (a.Anchor.Y < Fix.FromInt(8)) passedMountainRows = true;
            }
            Assert.True(passedMountainRows, "기병이 산줄기를 지나가지 못했습니다.");
        }

        [Fact(DisplayName = "산은 보병만 들어갈 수 있고, 기병 길은 산을 피한다")]
        public void InfantryCrossesMountain()
        {
            var setup = Setup("test_mountain_pass", 1, null, Corps("A", "teamA", "square"), Corps("B", "teamB", "square"));
            var field = new Battlefield(setup.Map, setup.Defs, true);
            var infantryPath = field.FindPath(new Cell(12, 36), new Cell(12, 3), new[] { Defs.TroopTypes["infantry"] });
            var cavalryPath = field.FindPath(new Cell(12, 36), new Cell(12, 3), new[] { Defs.TroopTypes["cavalry"] });
            var mountain = new Cell(11, 20);
            Assert.True(field.CanEnter(mountain, Defs.TroopTypes["infantry"]));
            Assert.False(field.CanEnter(mountain, Defs.TroopTypes["cavalry"]));
            Assert.DoesNotContain(cavalryPath, c => setup.Map.TerrainAt(c.X, c.Y) == "mountain");
            Assert.Equal(new Cell(12, 3), cavalryPath.Last());
            Assert.Equal(new Cell(12, 3), infantryPath.Last());
        }

        [Fact(DisplayName = "길 찾기: 못 가는 목표면 가장 가까운 칸까지")]
        public void PathToUnreachableStopsNearest()
        {
            var setup = Setup("test_mountain_pass", 1, null, Corps("A", "teamA", "square"), Corps("B", "teamB", "square"));
            var field = new Battlefield(setup.Map, setup.Defs, true);
            var path = field.FindPath(new Cell(3, 20), new Cell(11, 20), new[] { Defs.TroopTypes["cavalry"] });
            Assert.Equal(new Cell(8, 20), path.Last());
        }

        [Fact(DisplayName = "R-6 전투 중 군량 소비 = Σ(병력 × 하루 소비량) × 7 × 경과 분 (교전 없을 때 경제 모듈 식과 같다)")]
        public void R6SupplyMatchesFormula()
        {
            var setup = Setup("test_plain", 1, null,
                Corps("A", "teamA", "square", "cavalry", controller: Controllers.Player),
                Corps("B", "teamB", "square", controller: Controllers.Player));
            var sim = new BattleSimulation(setup);
            long ticksPerMinute = setup.Combat.TicksPerMinute;
            for (long i = 0; i < ticksPerMinute * 2 + 6; i++) sim.Step();
            var a = sim.Context.Corps[0];
            long daily = SupplyCalculator.DailyConsumption(a.Units.Select(u => (u.Troops, u.Troop.SupplyPerDay)));
            Fix minutes = Fix.FromRatio(ticksPerMinute * 2 + 6, ticksPerMinute);
            Assert.Equal(SupplyCalculator.BattleConsumption(setup.Balance, daily, minutes), a.StartSupply - a.Supply);
        }

        [Fact(DisplayName = "군량 소비 스위치를 끄면 군량이 줄지 않는다")]
        public void SupplyFlagOff()
        {
            var r = BattleSimulation.Run(Setup("test_plain", 1, "{\"supplyConsumption\": false}", Corps("A", "teamA", "square"), Corps("B", "teamB", "square")));
            Assert.All(r.Corps, c => Assert.Equal(c.StartSupply, c.RemainingSupply));
        }

        [Fact(DisplayName = "군량이 바닥나면 사기가 떨어져 먼저 퇴각한다")]
        public void SupplyShortageRetreat()
        {
            var r = BattleSimulation.Run(Setup("test_plain", 1, null, Corps("A", "teamA", "square", supply: 2000), Corps("B", "teamB", "square")));
            Assert.Equal("teamB", r.Winner);
            Assert.Equal(0, r.Find("A").RemainingSupply);
            Assert.Equal(CorpsStatus.Retreated, r.Find("A").Status);
        }

        [Fact(DisplayName = "퇴각: 퇴각 방향 끝으로 빠져나가고, 그 팀이 진다")]
        public void RetreatLeavesThroughEdge()
        {
            var setup = Setup("test_plain", 1, null, Corps("A", "teamA", "square", controller: Controllers.Player), Corps("B", "teamB", "square"));
            var sim = new BattleSimulation(setup);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Retreat }));
            var r = sim.RunToEnd();
            Assert.Equal(CorpsStatus.Retreated, r.Find("A").Status);
            Assert.Equal("teamB", r.Winner);
            Assert.True(sim.Context.Corps[0].Anchor.Y >= Fix.FromInt(setup.Map.Height) - setup.Combat.RetreatEdgeMargin);
        }

        [Fact(DisplayName = "대장 군단이 빠지면 다른 군단이 남아도 진다 (패배 조건 commander)")]
        public void CommanderDefeat()
        {
            var a1 = Corps("A1", "teamA", "square", controller: Controllers.Player);
            var a2 = Corps("A2", "teamA", "square", controller: Controllers.Player);
            a2.Role = "vanguard";
            var setup = Setup("test_plain", 1, null, a1, a2, Corps("B", "teamB", "square", stance: AiStanceIds.Defensive));
            var sim = new BattleSimulation(setup);
            sim.Issue(new BattleCommand { CorpsId = "A1", Kind = CommandKinds.Retreat });
            var r = sim.RunToEnd();
            Assert.Equal("teamB", r.Winner);
            Assert.Equal(CorpsStatus.Active, r.Find("A2").Status);
        }

        [Fact(DisplayName = "시간 제한: 남은 병력 비율로 판정 (교전 없으면 무승부)")]
        public void TimeLimitDraw()
        {
            var setup = Setup("test_plain", 1, null,
                Corps("A", "teamA", "square", controller: Controllers.Player),
                Corps("B", "teamB", "square", controller: Controllers.Player));
            setup.MaxMinutes = Fix.FromInt(1);
            var r = BattleSimulation.Run(setup);
            Assert.Equal(BattleEndReasons.TimeLimit, r.EndReason);
            Assert.Null(r.Winner);
            Assert.Equal(setup.Combat.TicksPerMinute, r.Ticks);
        }

        [Fact(DisplayName = "진형 변경 명령: 소부대 배치가 바뀐다")]
        public void ChangeFormation()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square", controller: Controllers.Player), Corps("B", "teamB", "square")));
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.ChangeFormation, FormationId = "column" }));
            sim.Step();
            var a = sim.Context.Corps[0];
            // 규율(R-56)만큼 전환 시간이 걸린다: 그동안은 이전 진형
            Assert.True(a.Reforming);
            Assert.Equal("square", a.Formation.Id);
            while (a.Reforming) sim.Step();
            Assert.Equal("column", a.Formation.Id);
            sim.Step();
            var vanguard = a.Units.First(u => u.Slot == SlotIds.Vanguard);
            Assert.Equal(a.Anchor.Y - Fix.FromInt(4), vanguard.Position.Y); // 북쪽을 보고 4칸 앞
        }
    }

    public class BattleDeterminismTests
    {
        private static BattleSetup TwoVsTwo(ulong seed, string flags = null)
        {
            var a1 = Mixed("A1", "teamA", "fishScale",
                ("vanguard", "spearman", 1000), ("center", "infantry", 1200), ("leftWing", "infantry", 800),
                ("rightWing", "infantry", 800), ("leftGuard", "archer", 600), ("rightGuard", "archer", 600));
            a1.Controller = Controllers.Player;
            var a2 = Corps("A2", "teamA", "craneWing", "cavalry", 700, General("ga2", might: 70, temperament: "brave"));
            a2.Role = "vanguard";
            var b1 = Mixed("B1", "teamB", "circle",
                ("vanguard", "spearman", 900), ("center", "archer", 900), ("leftWing", "spearman", 900),
                ("rightWing", "spearman", 900), ("leftGuard", "archer", 700), ("rightGuard", "archer", 700));
            var b2 = Corps("B2", "teamB", "arrowhead", "cavalry", 800, General("gb2", leadership: 40, might: 75, temperament: "arrogant"));
            b2.Role = "vanguard";
            return Setup("test_plain", seed, flags, a1, a2, b1, b2);
        }

        private static List<BattleCommand> PlayerOrders() => new List<BattleCommand>
        {
            new BattleCommand { Tick = 0, CorpsId = "A1", Kind = CommandKinds.MoveTo, X = Fix.FromInt(8), Y = Fix.FromInt(24), Mode = MoveModes.Normal },
            new BattleCommand { Tick = 150, CorpsId = "A1", Kind = CommandKinds.Attack, TargetCorpsId = "B2" },
            new BattleCommand { Tick = 400, CorpsId = "A1", Kind = CommandKinds.ChangeFormation, FormationId = "square" },
            new BattleCommand { Tick = 401, CorpsId = "A1", Kind = CommandKinds.Advance },
        };

        [Fact(DisplayName = "R-29 같은 설정·시드·명령이면 같은 결과")]
        public void SameInputSameResult()
        {
            var r1 = BattleSimulation.Run(TwoVsTwo(42), PlayerOrders());
            var r2 = BattleSimulation.Run(TwoVsTwo(42), PlayerOrders());
            Assert.Equal(r1.StateHash, r2.StateHash);
            Assert.Equal(r1.Ticks, r2.Ticks);
            Assert.Equal(r1.Winner, r2.Winner);
            Assert.Equal(r1.Corps.Select(c => c.RemainingTroops), r2.Corps.Select(c => c.RemainingTroops));
        }

        [Fact(DisplayName = "R-29 리플레이: 결과의 명령 기록으로 다시 돌리면 같은 결과")]
        public void ReplayFromLog()
        {
            var r1 = BattleSimulation.Run(TwoVsTwo(42), PlayerOrders());
            // 받아들여진 명령만 기록된다 (그 순간 안 보이는 적 공격 등은 거절, R-65 뒤로 B2가 늦게 보인다)
            Assert.True(r1.Commands.Count >= 3, "기록된 명령 " + r1.Commands.Count);
            var r2 = BattleSimulation.Run(TwoVsTwo(42), r1.Commands);
            Assert.Equal(r1.StateHash, r2.StateHash);
        }

        [Fact(DisplayName = "실시간 입력(Issue)과 기록 재생(Run)이 같은 결과")]
        public void LiveInputEqualsReplay()
        {
            var sim = new BattleSimulation(TwoVsTwo(42));
            var orders = PlayerOrders();
            int next = 0;
            while (!sim.IsOver)
            {
                while (next < orders.Count && orders[next].Tick == sim.Tick) sim.Issue(orders[next++]);
                sim.Step();
            }
            Assert.Equal(sim.Result.StateHash, BattleSimulation.Run(TwoVsTwo(42), sim.Result.Commands).StateHash);
        }

        [Fact(DisplayName = "시드가 다르면 (피해 폭 때문에) 결과가 달라진다")]
        public void DifferentSeedDiffers()
        {
            Assert.NotEqual(BattleSimulation.Run(TwoVsTwo(1)).StateHash, BattleSimulation.Run(TwoVsTwo(2)).StateHash);
        }

        [Fact(DisplayName = "받을 수 없는 명령은 거절하고 기록하지 않는다")]
        public void RejectsInvalidCommands()
        {
            var sim = new BattleSimulation(TwoVsTwo(1));
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A2", Kind = CommandKinds.Hold }));          // AI 군단
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A1", Kind = "teleport" }));                  // 등록 안 된 명령
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A1", Kind = CommandKinds.Attack, TargetCorpsId = "A2" })); // 아군
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A1", Kind = CommandKinds.MoveTo, X = Fix.FromInt(-1), Y = Fix.Zero }));
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A1", Kind = CommandKinds.ChangeFormation, FormationId = "nope" }));
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A1", Kind = CommandKinds.Retreat }));
            sim.Step();
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A1", Kind = CommandKinds.Advance }));        // 퇴각 중
            Assert.Single(sim.CommandLog);
        }

        [Fact(DisplayName = "확장: 새 명령을 등록하면 기존 코드 수정 없이 쓸 수 있다")]
        public void CustomCommand()
        {
            var ext = BattleExtensions.Default();
            ext.Commands.Register(new Charge());
            var sim = new BattleSimulation(TwoVsTwo(1, "{\"fogOfWar\": false}"), ext);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A1", Kind = "charge" }));
            sim.Step();
            Assert.Equal(MoveModes.Forced, sim.Context.Corps[0].Order.Mode);
            Assert.Equal(OrderKinds.MoveTo, sim.Context.Corps[0].Order.Kind);
        }

        /// <summary>가장 가까운 적 위치로 강행 돌격 (테스트용 확장 명령).</summary>
        private sealed class Charge : ICommandHandler
        {
            public string Kind => "charge";
            public bool IsValid(BattleContext ctx, CorpsState corps, BattleCommand cmd) => ctx.NearestEnemy(corps) != null;
            public void Apply(BattleContext ctx, CorpsState corps, BattleCommand cmd) =>
                corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.MoveTo, Point = ctx.NearestEnemy(corps).Anchor, Mode = MoveModes.Forced });
        }

        [Fact(DisplayName = "잘못된 전투 설정은 시작 전에 거절한다")]
        public void InvalidSetup()
        {
            Assert.Throws<ArgumentException>(() => new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square"))));
            Assert.Throws<ArgumentException>(() => new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "nope"), Corps("B", "teamB", "square"))));
            Assert.Throws<ArgumentException>(() => new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square", "dragon"), Corps("B", "teamB", "square"))));
            Assert.Throws<ArgumentException>(() => new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square"), Corps("A", "teamB", "square"))));
            Assert.Throws<ArgumentException>(() => new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square"), Corps("B", "teamC", "square"))));
            var bad = Corps("B", "teamB", "square", stance: "berserk");
            Assert.Throws<ArgumentException>(() => new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square"), bad)));
        }
    }

    public class BattleDataTests
    {
        [Theory(DisplayName = "시험용 맵은 데이터 검사를 통과한다")]
        [InlineData("test_plain")]
        [InlineData("test_mountain_pass")]
        [InlineData("test_forest")]
        public void MapsAreValid(string id) => Assert.Empty(DataValidator.ValidateMap(Defs, Map(id)));

        [Fact(DisplayName = "맵 검사: 이동 규칙이 없는 지형, 막힌 시작 위치를 잡는다")]
        public void MapValidationCatchesErrors()
        {
            var doc = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""id"": ""bad"", ""legend"": { ""."": ""lava"", ""~"": ""river"" },
                ""rows"": [ ""..~"", ""..~"" ], ""spawns"": { ""teamA"": { ""x"": 2, ""y"": 0, ""facing"": ""N"" } },
                ""retreatEdges"": { ""teamA"": ""up"" } }");
            var errors = DataValidator.ValidateMap(Defs, MapDef.From(doc));
            Assert.Contains(errors, e => e.Contains("lava"));
            Assert.Contains(errors, e => e.Contains("지나갈 수 없는"));
            Assert.Contains(errors, e => e.Contains("up"));
        }

        [Fact(DisplayName = "진형마다 여섯 칸 위치가 있다")]
        public void FormationSlots()
        {
            foreach (var f in Defs.Formations.Values)
                Assert.All(SlotIds.All, s => Assert.True(f.Slots.ContainsKey(s), $"{f.Id}.{s}"));
        }

        [Fact(DisplayName = "Fix 제곱근은 내림이고 정확하다")]
        public void FixSqrt()
        {
            Assert.Equal(Fix.FromInt(3), Fix.Sqrt(Fix.FromInt(9)));
            Assert.Equal(Fix.Parse("1.4142"), Fix.Sqrt(Fix.FromInt(2)));
            Assert.Equal(Fix.Parse("0.5"), Fix.Sqrt(Fix.Parse("0.25")));
            Assert.Equal(Fix.Zero, Fix.Sqrt(Fix.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fix.Sqrt(Fix.FromInt(-1)));
        }

        [Fact(DisplayName = "전투 수치 덮어쓰기: 스텝 값이 공통 값을 이긴다 (R-18)")]
        public void CombatOverride()
        {
            var c = Defs.ResolveCombat(Newtonsoft.Json.Linq.JObject.Parse("{\"morale\": {\"start\": 80}}"));
            Assert.Equal(Fix.FromInt(80), c.MoraleStart);
            Assert.Equal(Defs.Combat.TickSeconds, c.TickSeconds);
        }
    }
}
