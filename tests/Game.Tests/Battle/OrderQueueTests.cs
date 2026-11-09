using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Events;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>명령 예약·휴식·협공 대기 (battle.md 4.3.4~4.3.6, verification R-36~R-38).</summary>
    public class OrderQueueTests
    {
        private const string NoFog = "{\"fogOfWar\": false}";

        private static CorpsSetup Mine(string id, string x, string y = "32")
        {
            var c = Corps(id, "teamA", "square").At(x, y, "N").Player();
            c.Role = id == "A" ? CorpsRoles.Commander : null;
            return c;
        }

        private static CorpsSetup Enemy(string x = "12", string y = "6") => Corps("E", "teamB", "square").At(x, y, "S").Player();

        private static BattleCommand Move(string id, int x, int y, bool queued = false) =>
            new BattleCommand { CorpsId = id, Kind = CommandKinds.MoveTo, X = Fix.FromInt(x), Y = Fix.FromInt(y), Queued = queued };

        private static void Steps(BattleSimulation sim, int n) { for (int i = 0; i < n && !sim.IsOver; i++) sim.Step(); }

        private static bool Near(CorpsState c, int x, int y) => Vec.DistanceSquared(c.Anchor, new Vec(Fix.FromInt(x) + Fix.FromRatio(1, 2), Fix.FromInt(y) + Fix.FromRatio(1, 2))) <= Fix.FromInt(1);

        [Fact(DisplayName = "R-36 예약: 지금 이동이 끝나면 예약한 지점을 차례로 간다 (경유)")]
        public void WaypointsInOrder()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, NoFog, Mine("A", "12"), Enemy()));
            var a = sim.Context.FindCorps("A");
            Assert.True(sim.Issue(Move("A", 12, 28)));
            Assert.True(sim.Issue(Move("A", 6, 28, queued: true)));
            Assert.True(sim.Issue(Move("A", 6, 24, queued: true)));
            sim.Step();
            Assert.Equal(2, a.Queue.Count);
            bool reached1 = false, reached2 = false;
            for (int i = 0; i < 1500 && !sim.IsOver && !(reached2 && a.Queue.Count == 0 && a.Order.Kind == OrderKinds.Hold); i++)
            {
                sim.Step();
                if (Near(a, 6, 28)) reached1 = true;
                if (Near(a, 6, 24)) { Assert.True(reached1, "두 번째 지점보다 먼저 도착"); reached2 = true; }
            }
            Assert.True(reached2);
            Assert.Empty(a.Queue);
        }

        [Fact(DisplayName = "R-36 예약은 지금이 대기면 바로 실행, 새 명령(예약 아님)은 예약을 비운다, 상한을 넘으면 거절")]
        public void QueueRules()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, NoFog, Mine("A", "12"), Enemy()));
            var a = sim.Context.FindCorps("A");
            Assert.True(sim.Issue(Move("A", 12, 28, queued: true)));
            sim.Step();
            Assert.Equal(OrderKinds.MoveTo, a.Order.Kind); // 대기였으니 바로
            Assert.Empty(a.Queue);

            int max = sim.Context.Combat.OrderQueueMax;
            for (int i = 0; i < max; i++) Assert.True(sim.Issue(Move("A", 2 + i, 20, queued: true)));
            sim.Step();
            Assert.Equal(max, a.Queue.Count);
            Assert.False(sim.Issue(Move("A", 20, 20, queued: true)));

            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Hold }));
            sim.Step();
            Assert.Empty(a.Queue);
            Assert.Equal(OrderKinds.Hold, a.Order.Kind);
        }

        [Fact(DisplayName = "R-37 협공 대기: 함께 협공 대기하는 군단이 모두 자리를 잡으면 함께 공격 (한 군단이 아직 이동 중이면 기다린다)")]
        public void CoordinatedAttack()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, NoFog, Mine("A", "8"), Mine("B", "16"), Enemy()));
            var a = sim.Context.FindCorps("A");
            var b = sim.Context.FindCorps("B");
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Hold, Mode = HoldModes.Coordinate, TargetCorpsId = "E" }));
            Assert.True(sim.Issue(Move("B", 16, 26)));
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "B", Kind = CommandKinds.Hold, Mode = HoldModes.Coordinate, TargetCorpsId = "E", Queued = true }));
            sim.Step();
            Assert.True(a.Coordinating); // B가 아직 가는 중
            for (int i = 0; i < 1500 && !sim.IsOver && a.Coordinating; i++) sim.Step();
            Assert.Equal(OrderKinds.Attack, a.Order.Kind);
            Assert.Equal(OrderKinds.Attack, b.Order.Kind);
            Assert.Equal("E", a.Order.TargetCorpsId);
            Assert.True(Near(b, 16, 26), "B가 자리에 도착하기 전에 공격했습니다.");
        }

        [Fact(DisplayName = "R-37 혼자 오래 기다리면 한 번 묻고(coordinateWaiting), 응답 시간이 지나면 단독 공격")]
        public void CoordinateTimeout()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, NoFog, Mine("A", "8"), Mine("B", "16"), Enemy()));
            var a = sim.Context.FindCorps("A");
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Hold, Mode = HoldModes.Coordinate }));
            // B는 협공을 예약만 하고 오지 않는다 (적을 끝까지 공격한 뒤에 협공 대기 예약)
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "B", Kind = CommandKinds.Attack, TargetCorpsId = "E" }));
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "B", Kind = CommandKinds.Hold, Mode = HoldModes.Coordinate, Queued = true }));
            var c = sim.Context.Combat;
            long wait = (c.CoordinateWaitSec / c.TickSeconds).CeilToLong();
            long respond = (c.CoordinateResponseSec / c.TickSeconds).CeilToLong();
            Steps(sim, (int)wait + 2);
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.CoordinateWaiting && e.CorpsId == "A");
            Assert.True(a.Coordinating);
            // 계속 대기: 다시 협공 대기를 내리면 시간이 다시 센다
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Hold, Mode = HoldModes.Coordinate }));
            Steps(sim, (int)respond + 2);
            Assert.True(a.Coordinating);
            Steps(sim, (int)(wait + respond) + 2);
            Assert.False(a.Coordinating);
            Assert.Equal(OrderKinds.Advance, a.Order.Kind); // 대상이 없으면 진군
        }

        [Fact(DisplayName = "R-38 휴식: 싸우지 않으면 책략 점수가 더 빨리 회복되고, 싸우게 되면 깨진다")]
        public void Rest()
        {
            BattleSimulation Make(string mode)
            {
                var a = Mine("A", "12");
                a.General = General("g_A", intellect: 80);
                a.General.Skills.Add("rally");
                var sim = new BattleSimulation(Setup("test_plain", 1, NoFog, a, Enemy()));
                Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.UseSkill, SkillId = "rally" }));
                sim.Step();
                Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Hold, Mode = mode }));
                sim.Step();
                return sim;
            }
            var rest = Make(HoldModes.Rest);
            var normal = Make(HoldModes.Normal);
            var ra = rest.Context.FindCorps("A");
            var na = normal.Context.FindCorps("A");
            Assert.True(ra.Resting);
            Assert.False(na.Resting);
            Fix r0 = ra.SkillPoints.Points, n0 = na.SkillPoints.Points;
            Steps(rest, 50);
            Steps(normal, 50);
            // 같은 점수에서 시작해(스킬 사용 직후) 휴식 쪽이 회복량이 배율만큼 크다
            Assert.True(ra.SkillPoints.Points - r0 > na.SkillPoints.Points - n0, $"휴식 {ra.SkillPoints.Points - r0} / 보통 {na.SkillPoints.Points - n0}");
            Assert.True(ra.SkillPoints.Points > na.SkillPoints.Points);

            // 적에게 다가가 싸우게 되면 휴식이 깨진다
            var e = rest.Context.FindCorps("E");
            Assert.True(rest.Issue(new BattleCommand { CorpsId = "E", Kind = CommandKinds.Attack, TargetCorpsId = "A" }));
            for (int i = 0; i < 2000 && !rest.IsOver && ra.Resting; i++) rest.Step();
            Assert.False(ra.Resting);
        }
    }
}
