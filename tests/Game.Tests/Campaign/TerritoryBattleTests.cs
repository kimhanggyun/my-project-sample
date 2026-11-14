using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Online;
using Game.Services;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>영토 전투 (territory.md 5장, verification R-46): 지역 → 스텝, 출진 검사, 결과 반영, 이기면 차지.</summary>
    public class TerritoryBattleTests
    {
        private static readonly string Here = FakeLocationService.Default.ToString();

        [Fact(DisplayName = "R-46 지역 → 스텝: 단계 기본 수비군 + 지역 지형, 종류 territory, 이름 = 지역 이름")]
        public void StepForRegion()
        {
            var defs = Game.Territory.TerritoryDefs.Load(Kit.Source);
            var step = TerritoryBattles.StepFor(defs, "dev_zone_a2");
            Assert.True(step.IsTerritory);
            Assert.Equal("territory.dev_zone_a2", step.Id);
            Assert.Equal("test_forest", step.Map);
            Assert.Equal("region.dev_zone_a2", step.NameKey);
            Assert.Single(step.Enemy);
            Assert.Equal(6000, step.Enemy[0].Units.Sum(u => u.Troops));
            Assert.Null(TerritoryBattles.StepFor(defs, "nowhere"));
        }

        [Fact(DisplayName = "R-46 출진 검사: 혼자 차지할 수 있는 단계(필요 군단 1)만, 지금 위치에서 공격할 수 있는 지역만, 위치가 있어야")]
        public void SortieChecks()
        {
            var defs = Game.Territory.TerritoryDefs.Load(Kit.Source);
            Assert.Null(TerritoryBattles.CheckSortie(defs, "dev_zone_a3", Here));
            Assert.Contains("연합", TerritoryBattles.CheckSortie(defs, "dev_dong_a", Here));
            Assert.Contains("위치", TerritoryBattles.CheckSortie(defs, "dev_zone_a1", null));
            Assert.Contains("공격할 수 없", TerritoryBattles.CheckSortie(defs, "dev_zone_a1", "37.51,126.96")); // 나동 (구역 없음)
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var bad = flow.Prepare(Kit.Veteran(), new SortieRequest { StepId = "territory.dev_dong_a", Seed = 1, Location = Here });
            Assert.False(bad.Ok);
        }

        [Fact(DisplayName = "R-46 결과 반영: 손실·경험은 스토리와 같고, 스텝 클리어·탐색 기억·첫 보상은 남지 않는다")]
        public void ApplyWithoutProgress()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            var plan = flow.Prepare(state, new SortieRequest { StepId = "territory.dev_zone_a1", Seed = 2, Delegate = true, Location = Here });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.Equal(Here, plan.Location);
            Assert.True(flow.Confirm(state, plan));
            var result = BattleSimulation.Run(plan.Setup, new System.Collections.Generic.List<Game.Battle.Model.Commands.BattleCommand>());
            long troopsBefore = state.Units.Sum(u => u.Troops);
            var report = flow.ApplyResult(state, plan, result);
            Assert.Empty(state.ClearedSteps);
            Assert.False(report.FirstClear);
            Assert.DoesNotContain(state.ExploredBySteps.Keys, k => k.StartsWith("territory."));
            Assert.True(state.Units.Sum(u => u.Troops) <= troopsBefore);
        }

        [Fact(Skip = "군대 개편 PR 2에서 다시 켬: 새 군대 규모(army-v2.md, 처음 십부장 보병 3,000)에 맞춘 스텝·영토 밸런스 재조정 전", DisplayName = "R-46 가짜 서버: 영토 전투를 이기면 그 지역 주인이 내가 되고, 제출에 위치가 실린다")]
        public void FakeCapture()
        {
            var clock = new ServerClock(Kit.Clock());
            var fake = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var sync = new CloudSync(fake, clock);
            SyncResult start = null;
            sync.Start(null, 0, r => start = r);
            var flow = new StoryFlow(Kit.D, Kit.Source, clock);
            SyncResult won = null;
            for (ulong seed = 1; seed <= 10 && won == null; seed++)
            {
                var state = SaveJson.Read(SaveJson.Write(start.State));
                var plan = flow.Prepare(state, new SortieRequest { StepId = "territory.dev_zone_a1", Seed = seed, Delegate = true, Location = Here });
                Assert.True(flow.Confirm(state, plan));
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                flow.ApplyResult(state, plan, sim.Result);
                if (sim.Result.Winner != StoryFlow.PlayerTeam) continue;
                var sub = CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, start.Revision);
                Assert.Equal(Here, sub.Location);
                sync.SubmitBattle(sub, state, r => won = r);
            }
            Assert.NotNull(won);
            Assert.Equal(SyncOutcomes.Accepted, won.Outcome);
            TerritoryResponse owners = null;
            fake.GetTerritory(x => owners = x);
            var o = Assert.Single(owners.Owners);
            Assert.Equal(("dev_zone_a1", fake.AccountId), (o.RegionId, o.OwnerAccountId));
        }
    }
}
