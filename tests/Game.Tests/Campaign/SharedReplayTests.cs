using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-81 녹화 공유 (가짜 서버와 고르기 규칙). HTTP는 서버 테스트 ShareReplay.</summary>
    public class SharedReplayTests
    {
        [Fact(DisplayName = "R-81 고르기: 그 스텝만, 이긴 것 → 손실률 낮은 것 → 짧은 것 → 최근 것, 최대 개수")]
        public void PickOrder()
        {
            var all = new List<SharedReplaySummary>
            {
                new SharedReplaySummary { Id = "lose", StepId = "s", Won = false, PlayerLosses = 0, PlayerStartTroops = 100, Seconds = 10 },
                new SharedReplaySummary { Id = "win30", StepId = "s", Won = true, PlayerLosses = 30, PlayerStartTroops = 100, Seconds = 10 },
                new SharedReplaySummary { Id = "win10slow", StepId = "s", Won = true, PlayerLosses = 20, PlayerStartTroops = 200, Seconds = 90 },
                new SharedReplaySummary { Id = "win10fast", StepId = "s", Won = true, PlayerLosses = 10, PlayerStartTroops = 100, Seconds = 60 },
                new SharedReplaySummary { Id = "other", StepId = "t", Won = true },
            };
            Assert.Equal(new[] { "win10fast", "win10slow", "win30", "lose" }, SharedReplays.Pick(all, "s", 10).Select(x => x.Id));
            Assert.Equal(2, SharedReplays.Pick(all, "s", 2).Count);
        }

        [Fact(DisplayName = "R-81 가짜 서버: 공유한 녹화가 스텝 목록에 나오고 받아서 재생할 수 있다, 고친 녹화는 거절")]
        public void LocalShare()
        {
            var online = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Seed = 2, Delegate = true });
            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            var rec = ReplayService.Capture(plan, sim.CommandLog, sim.Result, 5);
            ShareReplayResponse res = null;
            online.ShareReplay(new ShareReplayRequest { ReplayJson = ReplayService.Write(rec) }, r => res = r);
            Assert.True(res.Ok);
            rec.Won = !rec.Won;
            rec.Hash = "00";
            online.ShareReplay(new ShareReplayRequest { ReplayJson = ReplayService.Write(rec) }, r => res = r);
            Assert.False(res.Ok);

            SharedReplayListResponse list = null;
            online.GetSharedReplays("step01", r => list = r);
            var item = Assert.Single(list.Items);
            SharedReplayResponse got = null;
            online.GetSharedReplay(item.Id, r => got = r);
            Assert.True(new ReplayService(flow).Check(ReplayService.Read(got.ReplayJson)).Ok);
        }
    }
}
