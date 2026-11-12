using Game.Campaign;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>E21 탭 알림 빨간 점: 할 일이 있는 탭.</summary>
    public class TabAlertTests
    {
        [Fact(DisplayName = "새 게임: 스토리(열린 스텝)·연습(오늘 남음)에 점, 포로는 없으면 없음. 포로가 생기면 포로 탭, 다 깨면 스토리 점이 꺼진다")]
        public void Alerts()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var s = StartingKitService.CreateNewGame(Kit.D);
            long now = Kit.Clock().UtcNow.ToUnixTimeSeconds();
            var a = TabAlerts.Compute(Kit.D, s, flow, now);
            Assert.Contains("story", a);
            Assert.DoesNotContain("prisoner", a);
            if (Kit.D.Practice.DailyLimit > 0) Assert.Contains("practice", a);
            s.Prisoners.Add(new Game.Campaign.Roster.PrisonerState { GeneralId = "enemy_general_01" });
            Assert.Contains("prisoner", TabAlerts.Compute(Kit.D, s, flow, now));
            foreach (var id in Kit.D.StepIndex.Order) s.ClearedSteps.Add(id);
            Assert.DoesNotContain("story", TabAlerts.Compute(Kit.D, s, flow, now));
        }

        [Fact(DisplayName = "부대 돌보기: 부상병이 생기면 점")]
        public void CareWhenWounded()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var s = StartingKitService.CreateNewGame(Kit.D);
            s.Units[0].Wounded = 100;
            Assert.Contains("care", TabAlerts.Compute(Kit.D, s, flow, 0));
        }
    }
}
