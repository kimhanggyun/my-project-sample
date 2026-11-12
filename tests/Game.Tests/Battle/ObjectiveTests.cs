using System.Linq;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Victory;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-78 승리 조건(버티기·구역 점령·군량 소진)과 맵 오브젝트(보급소·망루).</summary>
    public class ObjectiveTests
    {
        private static BattleSetup Step01()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(plan.Ok);
            return plan.Setup;
        }

        private static Vec PlayerStart(BattleSetup s)
        {
            var sim = new BattleSimulation(s);
            return sim.Context.Corps.First(c => c.Team == StoryFlow.PlayerTeam).Anchor;
        }

        [Fact(DisplayName = "버티기: 정한 팀이 시간까지 남으면 다른 팀이 진다")]
        public void HoldOut()
        {
            var s = Step01();
            s.DefeatConditions.Add(VictoryConditionIds.HoldOut);
            s.HoldOut = new HoldOutSetup { Team = StoryFlow.PlayerTeam, Seconds = Fix.FromInt(3) };
            var r = BattleSimulation.Run(s);
            Assert.Equal((StoryFlow.PlayerTeam, BattleEndReasons.Defeat), (r.Winner, r.EndReason));
            Assert.True(r.ElapsedSeconds <= Fix.FromInt(4));
        }

        [Fact(DisplayName = "구역 점령: 목표 팀이 혼자 정한 초 동안 차지하면 다른 팀이 진다. 보급소는 주인 군단 군량을 채운다")]
        public void ZoneAndDepot()
        {
            var s = Step01();
            var at = PlayerStart(s);
            s.DefeatConditions.Add(VictoryConditionIds.Zone);
            s.Objects.Add(new MapObjectSetup { Id = "z", Type = MapObjectSetup.Zone, X = at.X, Y = at.Y, Radius = Fix.FromInt(4), Team = StoryFlow.PlayerTeam, Seconds = Fix.FromInt(2) });
            s.Objects.Add(new MapObjectSetup { Id = "d", Type = MapObjectSetup.Depot, X = at.X, Y = at.Y, Radius = Fix.FromInt(4), Value = Fix.FromInt(100000) });
            var sim = new BattleSimulation(s);
            var c = sim.Context.Corps.First(x => x.Team == StoryFlow.PlayerTeam);
            for (int i = 0; i < 5; i++) sim.Step();
            Assert.Equal(StoryFlow.PlayerTeam, sim.Context.Objects.First(o => o.Setup.Id == "d").Owner);
            var plain = new BattleSimulation(Step01());
            for (int i = 0; i < 5; i++) plain.Step();
            var pc = plain.Context.Corps.First(x => x.Team == StoryFlow.PlayerTeam);
            Assert.True(pc.Supply < pc.StartSupply, "보급소가 없으면 군량이 준다");
            Assert.True(c.Supply > pc.Supply, "보급소가 소비를 채운다");
            Assert.True(c.Supply <= c.StartSupply);
            var r = sim.RunToEnd();
            Assert.Equal(StoryFlow.PlayerTeam, r.Winner);
            Assert.True(r.ElapsedSeconds <= Fix.FromInt(3));
        }

        [Fact(DisplayName = "망루: 차지한 팀은 그 자리에서 넓게 본다")]
        public void Watchtower()
        {
            var s = Step01();
            var at = PlayerStart(s);
            var without = new BattleSimulation(Step01());
            without.Step();
            int seenWithout = without.Context.Vision[StoryFlow.PlayerTeam].VisibleCorps.Count;
            s.Objects.Add(new MapObjectSetup { Id = "t", Type = MapObjectSetup.Watchtower, X = at.X, Y = at.Y, Radius = Fix.FromInt(4), Value = Fix.FromInt(200) });
            var sim = new BattleSimulation(s);
            for (int i = 0; i < 20; i++) sim.Step();
            Assert.Equal(StoryFlow.PlayerTeam, sim.Context.Objects[0].Owner);
            int enemies = sim.Context.Corps.Count(e => e.Team != StoryFlow.PlayerTeam && e.InBattle);
            Assert.Equal(enemies, sim.Context.Vision[StoryFlow.PlayerTeam].VisibleCorps.Count);
            Assert.True(enemies > seenWithout, "망루 없이는 다 보이지 않아야 의미가 있다");
        }

        [Fact(DisplayName = "군량 소진: 전장의 군단 군량이 모두 0이면 진다")]
        public void SupplyOut()
        {
            var s = Step01();
            s.DefeatConditions.Add(VictoryConditionIds.SupplyOut);
            foreach (var c in s.Corps.Where(c => c.Team != StoryFlow.PlayerTeam)) c.Supply = 1;
            var r = BattleSimulation.Run(s);
            Assert.Equal(StoryFlow.PlayerTeam, r.Winner);
        }
    }
}
