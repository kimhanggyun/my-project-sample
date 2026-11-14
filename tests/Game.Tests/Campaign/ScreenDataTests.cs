using System.Linq;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>디자인 시안 04·05가 쓰는 값 (스토리 카드, 장수 정렬·할 일 점, 약관 다시 동의).</summary>
    public class ScreenDataTests
    {
        [Fact(DisplayName = "스토리 카드: 새 게임은 step01이 다음 도전(열림), 나머지 본편은 앞 스텝 잠김, 요약은 스텝 데이터에서")]
        public void StoryCards()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var s = StartingKitService.CreateNewGame(Kit.D);
            var cards = StoryBoard.Build(Kit.D, flow, s);
            var first = cards.Single(c => c.IsNext);
            Assert.Equal("step01", first.Id);
            Assert.Equal(StepCardState.Open, first.State);
            var step = flow.LoadStep("step01");
            Assert.Equal(step.Enemy.Count, first.EnemyCorps);
            Assert.Equal(step.Enemy.Sum(e => e.Units.Sum(u => u.Troops)), first.EnemyTroops);
            Assert.Equal(step.MaxMinutes, first.MaxMinutes);
            Assert.All(cards.Where(c => c.Id != "step01" && !c.IsRescue), c => { Assert.Equal(StepCardState.Locked, c.State); Assert.Equal("previous", c.LockReason); });
            Assert.Equal((0, Kit.D.StepIndex.Order.Count), StoryBoard.Progress(Kit.D, s));

            s.ClearedSteps.Add("step01");
            cards = StoryBoard.Build(Kit.D, flow, s);
            Assert.Equal(StepCardState.Cleared, cards.First(c => c.Id == "step01").State);
            Assert.Equal("step02", cards.Single(c => c.IsNext).Id);
            Assert.Equal(1, StoryBoard.Progress(Kit.D, s).cleared);
        }

        [Fact(DisplayName = "장수 정렬: 등급(높은 순)·레벨·출전 가능 먼저, 할 일 점은 탭 할 일 점과 같은 조건")]
        public void GeneralSortAndAttention()
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            new PlayerActionRunner(Kit.D).Apply(s, PlayerActionRunner.DevGiveGeneral("*"));
            var byGrade = GeneralRoster.Sorted(Kit.D, s.Generals, GeneralSorts.Grade);
            for (int i = 1; i < byGrade.Count; i++)
                Assert.True(GeneralRoster.GradeRank(Kit.D, byGrade[i - 1].GeneralId) >= GeneralRoster.GradeRank(Kit.D, byGrade[i].GeneralId));
            s.Generals[0].Status = GeneralStatus.SeriousInjury;
            var ready = GeneralRoster.Sorted(Kit.D, s.Generals, GeneralSorts.Ready);
            Assert.Equal(GeneralStatus.SeriousInjury, ready.Last().Status); // 나머지는 모두 출전 가능
            Assert.NotEqual(s.Generals[0].GeneralId, ready[0].GeneralId);
            bool any = s.Generals.Any(g => GeneralRoster.NeedsAttention(Kit.D, s, g.GeneralId));
            var tabs = TabAlerts.Compute(Kit.D, s, new StoryFlow(Kit.D, Kit.Source, Kit.Clock()), 0);
            Assert.Equal(tabs.Contains("general"), any);
        }

        [Fact(DisplayName = "약관 다시 동의: 예전 판으로 동의했으면 true, 처음(기록 없음·0)이면 false")]
        public void TermsReagree()
        {
            var f = StartupFlow.Load(Kit.Source);
            Assert.False(f.IsReagree(null));
            Assert.False(f.IsReagree(new StartupRecord { TermsVersion = 0 }));
            Assert.False(f.IsReagree(new StartupRecord { TermsVersion = f.TermsVersion }));
            Assert.True(f.IsReagree(new StartupRecord { TermsVersion = f.TermsVersion - 1 }) || f.TermsVersion == 1);
        }
    }
}
