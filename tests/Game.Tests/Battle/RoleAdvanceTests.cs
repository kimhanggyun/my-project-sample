using Game.Battle.Model.Commands;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>역할 진군 (battle.md 4.3.2, verification R-34).</summary>
    public class RoleAdvanceTests
    {
        private const string NoFog = "{\"fogOfWar\": false}";

        private static CorpsSetup Mine(string id, string role, string x)
        {
            var c = Corps(id, "teamA", "square").At(x, "32", "N").Player();
            c.Role = id == "V" ? CorpsRoles.Commander : null;
            c.AdvanceRole = role;
            return c;
        }

        /// <summary>선봉(가운데)·측면(왼쪽)·후위(오른쪽) 대 가만히 있는 적 하나 (플레이어 조종이라 명령이 없으면 대기).</summary>
        private static BattleSimulation Scene(string flankRole = AdvanceRoles.Flank, string rearRole = AdvanceRoles.Rear)
        {
            var enemy = Corps("E", "teamB", "square").At("12", "8", "S").Player();
            var sim = new BattleSimulation(Setup("test_plain", 1, NoFog,
                Mine("V", AdvanceRoles.Vanguard, "12"), Mine("F", flankRole, "7"), Mine("R", rearRole, "17"), enemy));
            foreach (var id in new[] { "V", "F", "R" })
                Assert.True(sim.Issue(new BattleCommand { CorpsId = id, Kind = CommandKinds.Advance }));
            return sim;
        }

        private static Fix Abs(Fix v) => v < Fix.Zero ? -v : v;

        private static void Steps(BattleSimulation sim, int n) { for (int i = 0; i < n && !sim.IsOver; i++) sim.Step(); }

        private static Fix Dist(CorpsState a, CorpsState b) => Fix.Sqrt(Vec.DistanceSquared(a.Anchor, b.Anchor));

        [Fact(DisplayName = "R-34 측면은 자기 쪽(왼쪽)으로 벌려 적 옆으로 돌아가고, 선봉은 곧장 간다")]
        public void FlankSwingsWide()
        {
            var sim = Scene();
            var v = sim.Context.FindCorps("V");
            var f = sim.Context.FindCorps("F");
            Steps(sim, 120);
            Assert.True(v.Anchor.Y < Fix.FromInt(28), "선봉이 나아가지 않았습니다.");
            // 측면은 적(x=12)에서 옆으로 벌린 자리(x≈6)를 향하므로 선봉보다 기준선에서 멀다
            var lateralV = Abs(v.Anchor.X - Fix.FromInt(12));
            var lateralF = Abs(f.Anchor.X - Fix.FromInt(12));
            Assert.True(lateralF > lateralV + Fix.FromInt(3), $"측면 {lateralF} / 선봉 {lateralV}");
            Assert.True(f.Anchor.X < Fix.FromInt(12));
        }

        [Fact(DisplayName = "R-34 후위는 적과 거리(8칸)를 두고 멈춘다 (쫓지 않는다)")]
        public void RearKeepsDistance()
        {
            var sim = Scene();
            var r = sim.Context.FindCorps("R");
            var e = sim.Context.FindCorps("E");
            Fix start = Dist(r, e);
            Steps(sim, 400);
            Fix d = Dist(r, e);
            Assert.True(d < start, "후위가 따라가지 않았습니다.");
            Assert.True(d >= sim.Context.Combat.RearStandoffCells - Fix.FromInt(1), $"후위가 너무 가까이 갔습니다: {d}");
        }

        [Fact(DisplayName = "R-34 역할이 없거나 선봉이면 예전처럼 가장 가까운 적으로 곧장 (같은 결과)")]
        public void NoRoleSameAsVanguard()
        {
            var a = Scene(flankRole: null, rearRole: null);
            var b = Scene(flankRole: AdvanceRoles.Vanguard, rearRole: AdvanceRoles.Vanguard);
            Steps(a, 200);
            Steps(b, 200);
            Assert.Equal(StateHasher.Hash(a.Context), StateHasher.Hash(b.Context));
        }

        [Fact(DisplayName = "R-34 AI의 진군은 역할을 쓰지 않는다 (위임·AI 군단은 그대로)")]
        public void AiIgnoresRole()
        {
            BattleSimulation Make(string role)
            {
                var mine = Corps("A", "teamA", "square").At("6", "32", "N");
                mine.AdvanceRole = role;
                return new BattleSimulation(Setup("test_plain", 1, NoFog, mine, Corps("B", "teamB", "square").At("12", "8", "S")));
            }
            var a = Make(null);
            var b = Make(AdvanceRoles.Flank);
            Steps(a, 300);
            Steps(b, 300);
            Assert.Equal(StateHasher.Hash(a.Context), StateHasher.Hash(b.Context));
        }

        [Fact(DisplayName = "없는 진군 역할은 전투를 만들 때 거절")]
        public void InvalidRoleRejected()
        {
            var c = Corps("A", "teamA", "square");
            c.AdvanceRole = "sneaky";
            Assert.ThrowsAny<System.Exception>(() => new BattleSimulation(Setup("test_plain", 1, null, c, Corps("B", "teamB", "square"))));
        }
    }
}
