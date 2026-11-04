using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Ai;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Economy;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    internal static class Place
    {
        public static CorpsSetup At(this CorpsSetup c, string x, string y, string facing)
        {
            c.X = Fix.Parse(x); c.Y = Fix.Parse(y); c.Facing = facing;
            return c;
        }

        public static CorpsSetup Player(this CorpsSetup c) { c.Controller = Controllers.Player; return c; }
    }

    /// <summary>전투 중 포로 (battle.md 2.5.1, captivity.md 3장 스위치).</summary>
    public class CaptureTests
    {
        /// <summary>약한 A 군단 하나를 적 4개 군단(병력 적음)이 사방에서 둘러싼다.</summary>
        private static BattleSetup Surrounded(string flags, int might, ulong seed = 3)
        {
            var a = Corps("A", "teamA", "square", troopsPerUnit: 100, general: General("ga", might: might)).Player().At("12.5", "20.5", "N");
            var n = Corps("N", "teamB", "square", troopsPerUnit: 10).Player().At("12.5", "18.5", "S");
            var s = Corps("S", "teamB", "square", troopsPerUnit: 10).Player().At("12.5", "22.5", "N");
            var w = Corps("W", "teamB", "square", troopsPerUnit: 10).Player().At("10.5", "20.5", "E");
            var e = Corps("E", "teamB", "square", troopsPerUnit: 10).Player().At("14.5", "20.5", "W");
            foreach (var c in new[] { n, s, w, e }) c.Role = null;
            return Setup("test_plain", seed, flags ?? "{\"fogOfWar\": false}", a, n, s, w, e);
        }

        private static BattleSimulation RunUntil(BattleSetup setup, int ticks, System.Func<BattleSimulation, bool> stop)
        {
            var sim = new BattleSimulation(setup);
            for (int i = 0; i < ticks && !sim.IsOver && !stop(sim); i++) sim.Step();
            return sim;
        }

        [Fact(DisplayName = "포위된 장수는 포로가 되고 군단은 궤멸한다")]
        public void EncircledCaptured()
        {
            var sim = RunUntil(Surrounded(null, 1), 600, s => s.Context.FindCorps("A").Status == CorpsStatus.Captured);
            Assert.Equal(CorpsStatus.Captured, sim.Context.FindCorps("A").Status);
            var rec = Assert.Single(sim.Context.Captures);
            Assert.Equal("ga", rec.GeneralId);
            Assert.Equal("encircled", rec.Cause);
            Assert.Equal("teamB", rec.CapturedByTeam);
            var r = sim.RunToEnd();
            Assert.Equal("teamB", r.Winner);
            Assert.Single(r.Captures);
        }

        [Fact(DisplayName = "무력이 높으면 포위를 뚫고 탈출할 때가 많다 (탈출하면 경상, 퇴각)")]
        public void HighMightEscapes()
        {
            int Escapes(int might)
            {
                int n = 0;
                for (ulong seed = 1; seed <= 20; seed++)
                {
                    var sim = RunUntil(Surrounded(null, might, seed), 600, x => x.Context.FindCorps("A").Status != CorpsStatus.Active);
                    if (sim.Context.FindCorps("A").Status != CorpsStatus.Retreating) continue;
                    Assert.Equal("light", sim.Context.Injuries["ga"]);
                    Assert.Empty(sim.Context.Captures);
                    n++;
                }
                return n;
            }
            int strong = Escapes(100), weak = Escapes(1);
            Assert.True(strong >= 14 && weak <= 3, $"탈출: 무력 100 → {strong}/20, 무력 1 → {weak}/20");
        }

        [Theory(DisplayName = "포로 스위치(capture 또는 battleCapture)를 끄면 포위돼도 포로가 되지 않는다")]
        [InlineData("{\"fogOfWar\": false, \"capture\": false}")]
        [InlineData("{\"fogOfWar\": false, \"battleCapture\": false}")]
        public void CaptureFlagsOff(string flags)
        {
            var sim = RunUntil(Surrounded(flags, 1), 400, _ => false);
            Assert.NotEqual(CorpsStatus.Captured, sim.Context.FindCorps("A").Status);
            Assert.Empty(sim.Context.Captures);
        }

        [Fact(DisplayName = "퇴각하면 근처 아군 사기가 떨어진다")]
        public void NearbyMoraleDrop()
        {
            var a1 = Corps("A1", "teamA", "square").Player().At("10.5", "30.5", "N");
            var a2 = Corps("A2", "teamA", "square").Player().At("14.5", "30.5", "N");
            var a3 = Corps("A3", "teamA", "square").Player().At("22.5", "38.5", "N");
            a2.Role = null; a3.Role = null;
            var sim = new BattleSimulation(Setup("test_plain", 1, null, a1, a2, a3, Corps("B", "teamB", "square").Player()));
            Fix m2 = sim.Context.FindCorps("A2").Morale, m3 = sim.Context.FindCorps("A3").Morale;
            sim.Issue(new BattleCommand { CorpsId = "A1", Kind = CommandKinds.Retreat });
            sim.Step();
            // 같은 틱의 회복분(초당 0.2 × 0.1초)만큼 오차
            Fix after = sim.Context.FindCorps("A2").Morale, expected = m2 - Defs.Combat.NearbyMoraleDrop;
            Assert.True(after >= expected && after <= expected + Fix.Parse("0.1"), $"{after} (예상 {expected})");
            Assert.Equal(m3, sim.Context.FindCorps("A3").Morale);
        }

        [Fact(DisplayName = "일기토 패배 장수는 포로가 될 수 있고, 세뇌로 진 장수면 등용 보너스가 기록된다")]
        public void DuelCaptureWithBrainwash()
        {
            var a = Corps("A", "teamA", "square", general: General("ga", intellect: 100)).Player();
            var b = Corps("B", "teamB", "square", general: General("gb", intellect: 50)).Player();
            var setup = Setup("test_plain", 4, null, a, b);
            setup.Combat = Defs.ResolveCombat(JObject.Parse("{\"capture\": {\"duelCaptureChance\": 1}}"));
            var brain = new List<string>();
            for (int i = 0; i < 3; i++) brain.AddRange(new[] { "deliberate", "deliberate", "brainwash" });
            setup.Contests.Add(new ContestSetup { ContestId = "debate", CorpsA = "A", CorpsB = "B", ChoicesA = brain, ChoicesB = Enumerable.Repeat("letItPass", 10).ToList() });
            setup.Contests.Add(new ContestSetup { ContestId = "duel", CorpsA = "A", CorpsB = "B", ChoicesA = new List<string> { "bow", "bow" }, ChoicesB = new List<string> { "retreat", "retreat" } });
            var sim = new BattleSimulation(setup);
            Assert.Equal(CorpsStatus.Captured, sim.Context.FindCorps("B").Status);
            var rec = Assert.Single(sim.Context.Captures);
            Assert.Equal("duel", rec.Cause);
            Assert.True(rec.BrainwashBonus);
            Assert.Equal("teamA", sim.RunToEnd().Winner);
        }
    }

    /// <summary>바람·날씨·시간대 (battle.md 2.17).</summary>
    public class WindWeatherTests
    {
        private static BattleSimulation Archers(string windDir, string flags = "{\"fogOfWar\": false}")
        {
            var a = Corps("A", "teamA", "square", "archer").Player().At("12.5", "30.5", "N");
            var b = Corps("B", "teamB", "square").Player().At("12.5", "14.5", "N");
            var setup = Setup("test_plain", 1, flags, a, b);
            if (windDir != null) setup.Wind = new WindSetup { Direction = windDir, Strength = Fix.FromInt(2) };
            return new BattleSimulation(setup);
        }

        private static long Shot(BattleSimulation sim)
        {
            long before = sim.Context.FindCorps("B").Troops;
            for (int i = 0; i < 30; i++) sim.Step();
            return before - sim.Context.FindCorps("B").Troops;
        }

        [Fact(DisplayName = "순풍이면 원거리 사거리가 늘고, 역풍이면 준다 (windRange)")]
        public void WindRange()
        {
            Assert.Equal(0, Shot(Archers(null)));          // 사거리 11 < 거리 약 12.6
            Assert.True(Shot(Archers("N")) > 0);            // 순풍 (A → B 방향) +2
            Assert.Equal(0, Shot(Archers("S")));            // 역풍
            Assert.Equal(0, Shot(Archers("N", "{\"fogOfWar\": false, \"windRange\": false}")));
            Assert.Equal(0, Shot(Archers("N", "{\"fogOfWar\": false, \"wind\": false}")));
        }

        [Fact(DisplayName = "순풍을 받으면 빨리, 역풍을 안으면 느리게 이동한다 (windMove)")]
        public void WindMove()
        {
            Fix Travel(string dir)
            {
                var setup = Setup("test_plain", 1, null, Corps("A", "teamA", "square").Player(), Corps("B", "teamB", "square").Player());
                if (dir != null) setup.Wind = new WindSetup { Direction = dir, Strength = Fix.FromInt(2) };
                var sim = new BattleSimulation(setup);
                var a = sim.Context.FindCorps("A");
                Fix y0 = a.Anchor.Y;
                sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.MoveTo, X = Fix.FromInt(12), Y = Fix.FromInt(20) });
                for (int i = 0; i < 50; i++) sim.Step();
                return y0 - a.Anchor.Y;
            }
            Fix none = Travel(null), tail = Travel("N"), head = Travel("S");
            Assert.True(tail > none && none > head, $"순풍 {tail}, 없음 {none}, 역풍 {head}");
        }

        [Fact(DisplayName = "무더위: 군량 소비 +20% (R-6 식 × 1.2)")]
        public void HeatSupply()
        {
            var setup = Setup("test_plain", 1, null, Corps("A", "teamA", "square").Player(), Corps("B", "teamB", "square").Player());
            setup.Weather = "heat";
            var sim = new BattleSimulation(setup);
            long ticks = setup.Combat.TicksPerMinute * 2;
            for (long i = 0; i < ticks; i++) sim.Step();
            var a = sim.Context.FindCorps("A");
            long daily = SupplyCalculator.DailyConsumption(a.Units.Select(u => (u.Troops, u.Troop.SupplyPerDay)));
            Assert.Equal(daily * setup.Balance.SupplyDaysPerMinute * 2 * 120 / 100, a.StartSupply - a.Supply);
        }

        [Fact(DisplayName = "밤: 시작 사기 감소, 시야 감소. 시간대 스위치를 끄면 영향 없음")]
        public void Night()
        {
            BattleSimulation Make(string time, string flags = null)
            {
                var s = Setup("test_plain", 1, flags, Corps("A", "teamA", "square").Player(), Corps("B", "teamB", "square").Player());
                s.TimeOfDay = time;
                return new BattleSimulation(s);
            }
            var day = Make("day").Context.FindCorps("A").Morale;
            Assert.Equal(day - Fix.FromInt(5), Make("night").Context.FindCorps("A").Morale);
            Assert.Equal(day, Make("night", "{\"timeOfDay\": false}").Context.FindCorps("A").Morale);
            Assert.True(Make("night").Context.Vision["teamA"].ExploredCells.Count(x => x) < Make("day").Context.Vision["teamA"].ExploredCells.Count(x => x));
        }

        [Fact(DisplayName = "날씨 이벤트: 정해진 틱에 날씨·바람이 바뀐다")]
        public void WeatherChange()
        {
            var setup = Setup("test_plain", 1, null, Corps("A", "teamA", "square").Player(), Corps("B", "teamB", "square").Player());
            setup.Changes.Add(new EnvironmentChange { Tick = 20, Weather = "rain", Wind = new WindSetup { Direction = "E", Strength = Fix.FromInt(3) } });
            var sim = new BattleSimulation(setup);
            for (int i = 0; i < 25; i++) sim.Step();
            Assert.Equal("rain", sim.Context.Environment.Weather.Id);
            Assert.Equal(Fix.FromInt(3), sim.Context.Environment.WindStrength);
            Assert.False(sim.Context.Environment.FireAllowed);
        }

        [Fact(DisplayName = "없는 날씨는 시작 전에 거절")]
        public void UnknownWeather()
        {
            var setup = Setup("test_plain", 1, null, Corps("A", "teamA", "square"), Corps("B", "teamB", "square"));
            setup.Weather = "meteor";
            Assert.Throws<System.ArgumentException>(() => new BattleSimulation(setup));
        }
    }

    /// <summary>화계: 바람 방향으로 번지는 불, 숲 → 불탄 땅 (battle.md 2.17).</summary>
    public class FireTests
    {
        private static BattleSimulation Scene(string flags = null, string weather = "clear", string wind = "S", int windStrength = 1)
        {
            var a = Corps("A", "teamA", "square", general: General("ga", intellect: 70)).Player().At("12.5", "21.5", "N");
            a.General.Skills.Add("fireAttack");
            var b = Corps("B", "teamB", "square").Player().At("12.5", "14.5", "S");
            var setup = Setup("test_forest", 1, flags ?? "{\"fogOfWar\": false}", a, b);
            setup.Weather = weather;
            if (wind != null) setup.Wind = new WindSetup { Direction = wind, Strength = Fix.FromInt(windStrength) };
            return new BattleSimulation(setup);
        }

        private static BattleCommand Fire(int x, int y) => new BattleCommand { CorpsId = "A", Kind = CommandKinds.UseSkill, SkillId = "fireAttack", X = Fix.FromInt(x), Y = Fix.FromInt(y) };

        [Fact(DisplayName = "화계: 전조 뒤 불이 붙고, 바람 방향(아래)으로만 번지고, 다 타면 불탄 땅이 된다")]
        public void SpreadsDownwindAndBurns()
        {
            var sim = Scene();
            Assert.True(sim.Issue(Fire(12, 10)));
            for (int i = 0; i < 15; i++) sim.Step();
            Assert.False(sim.Context.Environment.IsBurning(new Cell(12, 10))); // 전조 2초
            for (int i = 0; i < 10; i++) sim.Step();
            Assert.True(sim.Context.Environment.IsBurning(new Cell(12, 10)));
            for (int i = 0; i < 30; i++) sim.Step();
            Assert.True(sim.Context.Environment.IsBurning(new Cell(12, 12)));   // 바람 아래쪽
            Assert.False(sim.Context.Environment.IsBurning(new Cell(12, 8)));   // 바람 위쪽
            for (int i = 0; i < 40; i++) sim.Step();
            Assert.Equal("burnt", sim.Context.Field.TerrainAt(new Cell(12, 10)));
            Assert.False(sim.Context.Field.RuleAt(new Cell(12, 10)).Conceals);  // 숨을 곳이 사라짐
        }

        [Fact(DisplayName = "불이 지나는 칸의 부대는 피해·사기 하락")]
        public void FireDamagesUnits()
        {
            var sim = Scene(windStrength: 2);
            var b = sim.Context.FindCorps("B");
            Assert.True(sim.Issue(Fire(12, 10)));
            long before = b.Troops;
            Fix morale = b.Morale;
            bool burned = false;
            for (int i = 0; i < 200 && !burned; i++)
            {
                sim.Step();
                burned = b.Units.Any(u => sim.Context.Environment.IsBurning(Cell.Of(u.Position)));
            }
            Assert.True(burned);
            for (int i = 0; i < 20; i++) sim.Step();
            Assert.True(b.Troops < before);
            Assert.True(b.Morale < morale);
        }

        [Fact(DisplayName = "비가 오거나 바람이 없으면 화계를 못 쓴다")]
        public void Conditions()
        {
            Assert.False(Scene(weather: "rain").Issue(Fire(12, 10)));
            Assert.False(Scene(wind: null).Issue(Fire(12, 10)));
            Assert.True(Scene(weather: "rain", flags: "{\"fogOfWar\": false, \"weather\": false}").Issue(Fire(12, 10)));
            Assert.False(new BattleSimulation(Setup("test_plain", 1, "{\"fogOfWar\": false}",
                With(Corps("A", "teamA", "square").Player()), Corps("B", "teamB", "square"))).Issue(Fire(12, 20))); // 평지는 안 탐
        }

        private static CorpsSetup With(CorpsSetup c)
        {
            c.General = General("ga", intellect: 70);
            c.General.Skills.Add("fireAttack");
            return c;
        }

        [Fact(DisplayName = "불 번짐 스위치를 끄면 붙인 칸만 탄다")]
        public void SpreadFlagOff()
        {
            var sim = Scene("{\"fogOfWar\": false, \"fireSpread\": false}");
            sim.Issue(Fire(12, 10));
            int max = 0;
            for (int i = 0; i < 100; i++) { sim.Step(); max = System.Math.Max(max, sim.Context.Environment.Burning.Count); }
            Assert.Equal(1, max);
        }

        [Fact(DisplayName = "AI 화계: 순풍이고 보이는 적이 숲에 있으면 쓴다")]
        public void AiUsesFire()
        {
            var a = Corps("A", "teamA", "square", stance: AiStanceIds.Defensive, general: General("ga", intellect: 70)).At("12.5", "21.5", "N");
            a.General.Skills.Add("fireAttack");
            var b = Corps("B", "teamB", "square").Player().At("12.5", "14.5", "S");
            var setup = Setup("test_forest", 1, "{\"fogOfWar\": false}", a, b);
            setup.Wind = new WindSetup { Direction = "S", Strength = Fix.FromInt(1) };
            var sim = new BattleSimulation(setup);
            var ac = sim.Context.FindCorps("A");
            for (int i = 0; i < 200 && ac.SkillPoints.UsesLeft("fireAttack") == 1; i++) sim.Step();
            Assert.Equal(0, ac.SkillPoints.UsesLeft("fireAttack"));
        }

        [Fact(DisplayName = "화계·바람이 있는 전투도 같은 입력이면 같은 결과")]
        public void Deterministic()
        {
            BattleResult Run()
            {
                var sim = Scene(windStrength: 2);
                sim.Issue(Fire(12, 10));
                return sim.RunToEnd();
            }
            Assert.Equal(Run().StateHash, Run().StateHash);
        }
    }
}
