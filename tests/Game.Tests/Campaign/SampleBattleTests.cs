using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-87 샘플전투·맵 목록.</summary>
    public class SampleBattleTests
    {
        [Fact(DisplayName = "R-87 맵 목록: 스텝이 쓰는 맵마다 한 줄 (스텝 순서, 같은 맵 스텝은 모음), 수상전 맵은 전장 유형 naval")]
        public void Catalog()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var maps = SampleBattles.Catalog(Kit.D, flow);
            Assert.Equal("test_plain", maps[0].MapId);
            Assert.Equal("step01", maps[0].Steps[0]);
            Assert.Contains("step04", maps[0].Steps);
            Assert.Equal(maps.Count, maps.Select(m => m.MapId).Distinct().Count());
            Assert.Equal("naval", maps.Single(m => m.MapId == "lake_naval").BattleType);
            foreach (var id in Kit.D.StepIndex.Order) Assert.Contains(maps, m => m.Steps.Contains(id));
        }

        [Fact(DisplayName = "R-87 샘플전투: 새 게임에서도 마지막 스텝을 사본으로 출진, 끝까지 싸워도 원본 세이브(군량·진행)는 그대로")]
        public void SampleDoesNotTouchSave()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Game.Campaign.StartingKitService.CreateNewGame(Kit.D);
            string before = SaveJson.Write(state);
            string last = Kit.D.StepIndex.Order.Last();
            Assert.False(flow.Prepare(state, new SortieRequest { StepId = last, Seed = 1 }).Ok); // 원래는 잠김
            var plan = SampleBattles.Prepare(Kit.D, flow, state, last, 1, delegateAll: true);
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var sim = new BattleSimulation(plan.Setup);
            sim.RunToEnd();
            Assert.True(sim.IsOver);
            Assert.Equal(before, SaveJson.Write(state));
        }

        [Fact(DisplayName = "R-87 샘플 수상전: 수군 부대가 없으면 출진 계획 오류 (R-85 규칙 그대로)")]
        public void NavalNeedsNavalUnits()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Game.Campaign.StartingKitService.CreateNewGame(Kit.D);
            Assert.False(SampleBattles.Prepare(Kit.D, flow, state, "naval_drill", 1).Ok);
        }
    }
}
