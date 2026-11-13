using System.Collections.Generic;
using System.Linq;
using Game.Campaign;
using Game.Flow;
using Game.Online;
using Game.Progression;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>연습 대결 (general-growth.md 2.4, R-59).</summary>
    public class PracticeTests
    {
        private const long Now = 1_800_000_000;

        /// <summary>화면처럼 한 라운드씩: 고를 수 있는 첫 행동(공격 우선)으로 끝까지.</summary>
        private static PracticeMatch PlayOut(PracticeService svc, CampaignState s, string contest, string opp, ulong seed)
        {
            var m = svc.Start(s, "general_a", contest, opp, seed, Now);
            Assert.NotNull(m);
            while (!m.IsOver)
            {
                var avail = m.Session.Available(m.Session.A);
                Assert.True(m.Play(avail.Contains("attack") ? "attack" : avail[0]));
            }
            return m;
        }

        [Fact(DisplayName = "R-59 연습 대결: 같은 시드·선택으로 다시 돌리면 같은 결과, 전적·경험치가 들고 오늘 횟수가 오른다")]
        public void SameResultAndRecords()
        {
            var svc = new PracticeService(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            var m = PlayOut(svc, s, "duel", "p_rookie", 7);
            var action = m.ToAction(Now);
            var copy = Game.Flow.SaveJson.Read(Game.Flow.SaveJson.Write(s));
            var o = svc.Apply(s, action);
            Assert.NotNull(o);
            Assert.Equal(m.Session.Result.Winner, o.Result.Winner);
            Assert.Equal(m.Session.Result.Rounds.Count, o.Result.Rounds.Count);
            var g = s.FindGeneral("general_a");
            var rec = PracticeService.RecordOf(g, "duel");
            Assert.Equal(1, rec.wins + rec.losses + rec.retreats + rec.draws);
            Assert.Equal(Kit.D.Practice.Exp[o.Outcome], g.Exp); // 레벨 1 → 100까지 안 차므로 그대로
            Assert.Equal(1, PracticeService.TodayCount(s, Now));
            // 서버도 같은 규칙: 행동으로 적용하면 같은 세이브
            Assert.True(new PlayerActionRunner(Kit.D).Apply(copy, ReplayJsonCopy(action)));
            Assert.Equal(Game.Flow.SaveJson.Write(s), Game.Flow.SaveJson.Write(copy));
        }

        [Fact(DisplayName = "R-59 대결 화면용 연습 (PracticeContestPlay): 몇 라운드 고르고 나머지 자동으로 끝내도 그 선택으로 행동이 반영된다")]
        public void ContestScreenAdapter()
        {
            var svc = new PracticeService(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            var m = svc.Start(s, "general_a", "duel", "p_veteran", 11, Now);
            var play = new PracticeContestPlay(Kit.D, m);
            Assert.Equal(("general_a", "p_veteran"), (play.GeneralA, play.GeneralB));
            Assert.True(play.Choose(play.Available()[0]));
            Assert.Single(play.Rounds());
            Assert.NotNull(play.ChooseRandom(3));
            play.AutoFinish();
            Assert.True(play.IsOver);
            Assert.NotNull(play.Result);
            Assert.Equal(play.Rounds().Count, play.Choices.Count);
            var o = svc.Apply(s, m.ToAction(Now));
            Assert.NotNull(o);
            Assert.Equal(play.Result.Winner, o.Result.Winner);
        }

        private static PlayerAction ReplayJsonCopy(PlayerAction a) => OnlineJson.Read<PlayerAction>(OnlineJson.Write(a));

        [Fact(DisplayName = "R-59 조작된 선택(그 라운드에 못 고르는 행동·모자람·남음)이나 없는 상대·장수는 반영하지 않는다")]
        public void RejectsTampered()
        {
            var svc = new PracticeService(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            var a = PlayOut(svc, s, "duel", "p_veteran", 3).ToAction(Now);
            var before = Game.Flow.SaveJson.Write(s);
            var shortA = OnlineJson.Read<PlayerAction>(OnlineJson.Write(a)); shortA.Choices.RemoveAt(shortA.Choices.Count - 1);
            Assert.Null(svc.Apply(s, shortA));
            var longA = OnlineJson.Read<PlayerAction>(OnlineJson.Write(a)); longA.Choices.Add("attack");
            Assert.Null(svc.Apply(s, longA));
            var special = OnlineJson.Read<PlayerAction>(OnlineJson.Write(a)); special.Choices[0] = "special"; // 기운 0에서 필살은 못 고른다
            Assert.Null(svc.Apply(s, special));
            var noOpp = OnlineJson.Read<PlayerAction>(OnlineJson.Write(a)); noOpp.OpponentId = "nobody";
            Assert.Null(svc.Apply(s, noOpp));
            Assert.Equal(before, Game.Flow.SaveJson.Write(s));
            Assert.Equal(PracticeService.NoGeneral, svc.Check(s, "general_h", "duel", "p_rookie", Now));
        }

        [Fact(DisplayName = "R-59 연습 승리도 전투 대결과 같은 성장 보상: 일기토 100승마다 무력 +1")]
        public void GrowthReward()
        {
            var svc = new PracticeService(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            var g = s.FindGeneral("general_a");
            g.Records[Counters.Wins("duel")] = 99;
            g.GrantedRewards.Clear();
            // 이길 때까지 시드를 바꿔 가며 (약한 상대)
            for (ulong seed = 1; seed < 200; seed++)
            {
                var copy = Game.Flow.SaveJson.Read(Game.Flow.SaveJson.Write(s));
                var m = PlayOut(svc, copy, "duel", "p_rookie", seed);
                if (ContestRecords.OutcomeOf(m.Session.Result, "A") != "win") continue;
                var o = svc.Apply(s, m.ToAction(Now));
                Assert.Equal("win", o.Outcome);
                Assert.Contains(o.Rewards, r => r.Stat == "might");
                Assert.Equal(100, s.FindGeneral("general_a").Records[Counters.Wins("duel")]);
                return;
            }
            Assert.Fail("약한 상대에게 이긴 판이 없습니다.");
        }

        [Fact(DisplayName = "R-59 하루 횟수 상한은 데이터 값 (0이면 없음): 상한이 있으면 그날은 막히고 다음 날 다시 된다")]
        public void DailyLimit()
        {
            var defs = Kit.D;
            Assert.Equal(0, defs.Practice.DailyLimit); // 지금은 제한 없음 (서버 연동 후 서버 값)
            var s = StartingKitService.CreateNewGame(defs);
            s.PracticeDay = PracticeService.DayOf(Now);
            s.PracticeCount = 1000;
            Assert.Equal(PracticeService.Ok, new PracticeService(defs).Check(s, "general_a", "debate", "p_master", Now));
            Assert.Equal(1000, PracticeService.TodayCount(s, Now));
            Assert.Equal(0, PracticeService.TodayCount(s, Now + PracticeService.SecondsPerDay));
        }
    }
}
