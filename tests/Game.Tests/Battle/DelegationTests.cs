using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>위임 (ai.md 9장): 아군 군단을 같은 AI가 지휘, 플레이어 명령 우선, 판단 수준은 장수 능력치.</summary>
    public class DelegationTests
    {
        private static CorpsSetup Mine(bool delegated, int intellect = 50, int leadership = 50)
        {
            var c = Corps("A", "teamA", "square", general: General("ga", leadership: leadership, intellect: intellect)).Player();
            c.Delegated = delegated;
            return c;
        }

        [Fact(DisplayName = "위임한 군단은 스스로 진군해 싸우고, 위임하지 않은 군단은 대기한다")]
        public void DelegatedFights()
        {
            var on = new BattleSimulation(Setup("test_plain", 1, null, Mine(true), Corps("B", "teamB", "square", stance: "defensive")));
            var off = new BattleSimulation(Setup("test_plain", 1, null, Mine(false), Corps("B", "teamB", "square", stance: "defensive")));
            Fix y0 = on.Context.FindCorps("A").Anchor.Y;
            for (int i = 0; i < 200; i++) { on.Step(); off.Step(); }
            Assert.True(on.Context.FindCorps("A").Anchor.Y < y0 - Fix.FromInt(5));
            Assert.Equal(y0, off.Context.FindCorps("A").Anchor.Y);
            Assert.Equal("teamA", on.RunToEnd().Winner);
        }

        [Fact(DisplayName = "위임 중 플레이어 명령은 끝날 때까지 우선하고, 끝나면 AI가 다시 맡는다")]
        public void PlayerOrderFirst()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, Mine(true), Corps("B", "teamB", "square", stance: "defensive")));
            var a = sim.Context.FindCorps("A");
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.MoveTo, X = Fix.FromInt(3), Y = Fix.FromInt(34) }));
            for (int i = 0; i < 30; i++) sim.Step();
            Assert.Equal(OrderKinds.MoveTo, a.Order.Kind);
            Assert.True(a.OrderByPlayer);
            for (int i = 0; i < 300 && a.OrderByPlayer; i++) sim.Step(); // 도착하면 플레이어 명령 끝
            Assert.False(a.OrderByPlayer);
            for (int i = 0; i < 20; i++) sim.Step();
            Assert.NotEqual(OrderKinds.Hold, a.Order.Kind); // AI가 다시 진군
        }

        [Fact(DisplayName = "위임 중 플레이어의 대기 명령은 유지되고, 진형 변경은 AI를 멈추지 않는다")]
        public void HoldAndFormation()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, Mine(true), Corps("B", "teamB", "square", stance: "defensive")));
            var a = sim.Context.FindCorps("A");
            sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.ChangeFormation, FormationId = "fishScale" });
            for (int i = 0; i < 20; i++) sim.Step();
            Assert.False(a.OrderByPlayer);
            sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Hold });
            for (int i = 0; i < 200; i++) sim.Step();
            Assert.Equal(OrderKinds.Hold, a.Order.Kind);
            Assert.True(a.OrderByPlayer);
        }

        [Fact(DisplayName = "전투 중 위임 켜고 끄기 (명령 기록으로 재생 가능), 스위치를 끄면 위임 불가")]
        public void Toggle()
        {
            BattleSetup Make(string flags = null) => Setup("test_plain", 1, flags, Mine(false), Corps("B", "teamB", "square", stance: "defensive"));
            var sim = new BattleSimulation(Make());
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Delegate, Mode = "off" })); // 이미 꺼짐
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Delegate, Mode = "on" }));
            for (int i = 0; i < 100; i++) sim.Step();
            Assert.True(sim.Context.FindCorps("A").Delegated);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Delegate, Mode = "off" }));
            var r = sim.RunToEnd();
            Assert.Equal(r.StateHash, BattleSimulation.Run(Make(), r.Commands).StateHash);

            var off = new BattleSimulation(Make("{\"delegate\": false}"));
            Assert.False(off.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.Delegate, Mode = "on" }));
            var pre = Make("{\"delegate\": false}");
            pre.Corps[0].Delegated = true;
            Assert.False(new BattleSimulation(pre).Context.FindCorps("A").Delegated);
        }

        [Fact(DisplayName = "위임 판단 수준은 장수 지략·통솔 평균으로 정한다")]
        public void DifficultyFromGeneral()
        {
            Assert.Equal("easy", new BattleSimulation(Setup("test_plain", 1, null, Mine(true, 40, 40), Corps("B", "teamB", "square"))).Context.FindCorps("A").AiDifficulty);
            Assert.Equal("normal", new BattleSimulation(Setup("test_plain", 1, null, Mine(true, 70, 60), Corps("B", "teamB", "square"))).Context.FindCorps("A").AiDifficulty);
        }
    }

    public class DelegatedSortieTests
    {
        [Fact(DisplayName = "위임 출진: 전체 또는 지정한 장수의 군단만")]
        public void SortieDelegation()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var all = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Delegate = true });
            Assert.All(all.Setup.Corps.Where(c => c.Team == StoryFlow.PlayerTeam), c => Assert.True(c.Delegated));
            var one = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", DelegateGenerals = { "general_b" } });
            Assert.Equal(new[] { "general_b" }, one.Setup.Corps.Where(c => c.Delegated).Select(c => c.General.Id));
        }

        [Fact(DisplayName = "스텝 시뮬레이터: 같은 시드 목록이면 같은 통계")]
        public void Simulator()
        {
            var sim = new StepSimulator(Kit.D, Kit.Source);
            System.Func<CampaignState> fresh = () => StartingKitService.CreateNewGame(Kit.D);
            var s1 = sim.Measure("step01", fresh, new ulong[] { 1, 2, 3, 4 });
            var s2 = sim.Measure("step01", fresh, new ulong[] { 1, 2, 3, 4 });
            Assert.Empty(s1.Errors);
            Assert.Equal(4, s1.Runs);
            Assert.Equal(s1.Wins, s2.Wins);
            Assert.Equal(s1.AverageSeconds, s2.AverageSeconds);
            Assert.Equal(s1.AverageLossPercent, s2.AverageLossPercent);
        }
    }
}
