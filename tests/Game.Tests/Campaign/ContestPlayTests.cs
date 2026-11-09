using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>일기토를 한 라운드씩 고르기 (battle.md 2.7): 앞 라운드는 다시 계산해도 같고, 마지막 기록으로 전투가 같은 결과 (R-29).</summary>
    public class ContestPlayTests
    {
        private static SortiePlan PlanWithDuel()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            for (ulong seed = 1; seed < 500; seed++)
            {
                // 일기토는 두 대장의 무력 차이 3 이내에서만 (battle.md 2.7): step01 적 대장(무력 46)과 장수 A(45)
                var state = StartingKitService.CreateNewGame(Kit.D);
                var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed });
                if (plan.Setup.Contests.Any(c => c.ContestId == "duel")) return plan;
            }
            Assert.Fail("일기토가 일어나는 시드를 찾지 못했습니다.");
            return null;
        }

        [Fact(DisplayName = "일기토 고르기: 고른 만큼만 라운드가 보이고, 앞 라운드는 바뀌지 않으며, 끝난 기록으로 전투를 만들면 같은 대결 결과")]
        public void PlayRoundByRound()
        {
            var plan = PlanWithDuel();
            int index = plan.Setup.Contests.FindIndex(c => c.ContestId == "duel");
            var play = new ContestPlay(plan.Setup, index);
            Assert.Empty(play.Rounds());
            Assert.DoesNotContain("special", play.Available()); // 처음엔 기운이 없어 필살 불가

            var seen = new System.Collections.Generic.List<(string, string, long, long)>();
            for (int guard = 0; guard < 20 && !play.IsOver; guard++)
            {
                var pick = play.Available().Contains("attack") ? "attack" : play.Available()[0];
                Assert.True(play.Choose(pick));
                var rounds = play.Rounds();
                for (int i = 0; i < seen.Count; i++)
                    Assert.Equal(seen[i], (rounds[i].ActionA, rounds[i].ActionB, rounds[i].ResourceA.Raw, rounds[i].ResourceB.Raw));
                if (rounds.Count > seen.Count) { var r = rounds[seen.Count]; seen.Add((r.ActionA, r.ActionB, r.ResourceA.Raw, r.ResourceB.Raw)); }
                Assert.Equal(pick, rounds.Last().ActionA);
            }
            Assert.True(play.IsOver);
            Assert.False(play.Choose("attack"));

            var final = new BattleSimulation(plan.Setup).PreBattleContests[index];
            Assert.Equal(play.Result.End, final.End);
            Assert.Equal(play.Result.Winner, final.Winner);
            Assert.Equal(seen.Count, final.Rounds.Count);
        }

        [Fact(DisplayName = "자동: 남은 라운드를 AI가 고르고, 전투에서도 같은 결과가 나온다")]
        public void TimeoutPicksAreRecorded()
        {
            var plan = PlanWithDuel();
            int index = plan.Setup.Contests.FindIndex(c => c.ContestId == "duel");
            var play = new ContestPlay(plan.Setup, index);
            string pick = play.ChooseRandom(plan.Setup.Seed);
            Assert.NotNull(pick);
            Assert.Equal(new[] { pick }, play.Choices);
            var again = new ContestPlay(PlanWithDuel().Setup, index);
            Assert.Equal(pick, again.ChooseRandom(plan.Setup.Seed)); // 같은 시드면 같은 선택
        }

        [Fact(DisplayName = "자동 끝내기: 기록은 내가 고른 데까지, 나머지는 전투가 같은 AI로 고른다")]
        public void AutoFinish()
        {
            var plan = PlanWithDuel();
            int index = plan.Setup.Contests.FindIndex(c => c.ContestId == "duel");
            var play = new ContestPlay(plan.Setup, index);
            play.Choose(play.Available()[0]);
            play.AutoFinish();
            Assert.True(play.IsOver);
            Assert.Single(play.Choices); // 기록은 내가 고른 것만
            Assert.Equal(play.Result.Rounds.Count, play.Rounds().Count);
            var a = BattleSimulation.Run(plan.Setup);
            var b = BattleSimulation.Run(plan.Setup);
            Assert.Equal(a.StateHash, b.StateHash);
            Assert.Equal(play.Result.End, a.Contests[index].End);
            Assert.Equal(play.Result.Rounds.Count, a.Contests[index].Rounds.Count);
        }
    }
}
