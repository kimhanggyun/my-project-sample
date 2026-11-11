using System.Linq;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-66 포로 처리 행동: 등용 시도(시드 확률, 실패하면 재시도 대기), 해방(보상). 같은 행동이면 서버도 같은 결과.</summary>
    public class PrisonerActionTests
    {
        private const long Now = 1_800_000_000;

        private static CampaignState WithPrisoner(string id = "enemy_general_02")
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            s.Prisoners.Add(new PrisonerState { GeneralId = id, CapturedAtUnixSeconds = Now - 100 });
            return s;
        }

        private static PlayerAction At(PlayerAction a, long t) { a.DoneAtUnixSeconds = t; return a; }

        [Fact]
        public void RecruitSucceedsOrWaitsThenRetries()
        {
            var runner = new PlayerActionRunner(Kit.D);
            bool sawSuccess = false, sawFail = false;
            for (ulong seed = 1; seed <= 40 && !(sawSuccess && sawFail); seed++)
            {
                var s = WithPrisoner();
                Assert.True(runner.Apply(s, At(PlayerActionRunner.RecruitPrisoner("enemy_general_02", seed), Now)));
                if (s.FindGeneral("enemy_general_02") != null)
                {
                    sawSuccess = true;
                    Assert.Null(s.FindPrisoner("enemy_general_02"));
                    continue;
                }
                sawFail = true;
                var p = s.FindPrisoner("enemy_general_02");
                Assert.Equal(Now, p.LastRecruitAttemptUnixSeconds);
                // 대기 중엔 다시 못 하고, 대기 시간이 지나면 다시 할 수 있다
                Assert.False(runner.Apply(s, At(PlayerActionRunner.RecruitPrisoner("enemy_general_02", seed + 100), Now + 60)));
                Assert.True(runner.Apply(s, At(PlayerActionRunner.RecruitPrisoner("enemy_general_02", seed + 100), Now + Kit.D.Campaign.RecruitRetrySeconds)));
            }
            Assert.True(sawSuccess && sawFail, "40개 시드 안에 성공·실패가 둘 다 나와야 한다 (확률 30% 예시)");
        }

        [Fact]
        public void SameActionSameResultOnServer()
        {
            var runner = new PlayerActionRunner(Kit.D);
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var a = WithPrisoner();
                var b = SaveJson.Read(SaveJson.Write(a));
                Assert.True(runner.Apply(a, At(PlayerActionRunner.RecruitPrisoner("enemy_general_02", seed), Now)));
                Assert.True(runner.Apply(b, At(PlayerActionRunner.RecruitPrisoner("enemy_general_02", seed), Now)));
                Assert.Equal(SaveJson.Write(a), SaveJson.Write(b));
            }
        }

        [Fact]
        public void ReleaseGivesRewardAndRemoves()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = WithPrisoner();
            long gold = s.Wallet["gold"];
            Assert.True(runner.Apply(s, At(PlayerActionRunner.ReleasePrisoner("enemy_general_02"), Now)));
            Assert.Null(s.FindPrisoner("enemy_general_02"));
            Assert.Equal(gold + Kit.D.Campaign.ReleaseReward["gold"], s.Wallet["gold"]);
            Assert.False(runner.Apply(s, At(PlayerActionRunner.ReleasePrisoner("enemy_general_02"), Now))); // 이미 없음
            Assert.False(runner.Apply(s, At(PlayerActionRunner.RecruitPrisoner("noSuch", 1), Now)));
        }
    }
}
