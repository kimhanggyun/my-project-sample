using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-75 전체 난이도 설정·어려움(사령관 협공).</summary>
    public class DifficultyTests
    {
        [Fact(DisplayName = "설정은 스텝 난이도를 차례에서 한 칸 옮긴다 (끝을 넘지 않음), 보통은 그대로")]
        public void Shift()
        {
            var ai = Kit.D.Ai;
            Assert.Equal("normal", ai.Shift("normal", "normal"));
            Assert.Equal("hard", ai.Shift("normal", "hard"));
            Assert.Equal("easy", ai.Shift("normal", "easy"));
            Assert.Equal("easy", ai.Shift("easy", "easy"));
            Assert.Equal("hard", ai.Shift("hard", "hard"));
            Assert.Equal("normal", ai.Shift("normal", null));
            Assert.True(ai.Difficulties["hard"].Coordinate);
            Assert.False(ai.Difficulties["normal"].Coordinate);
        }

        [Fact(DisplayName = "설정 행동(settings.difficulty)은 상태에 남고, 출진 때 적 군단 난이도에 들어간다")]
        public void ActionAppliesToSortie()
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            Assert.Equal("normal", s.Difficulty);
            var runner = new PlayerActionRunner(Kit.D);
            Assert.True(runner.Apply(s, PlayerActionRunner.SetDifficulty("hard")));
            Assert.False(runner.Apply(s, PlayerActionRunner.SetDifficulty("hard"))); // 같은 값
            Assert.False(runner.Apply(s, PlayerActionRunner.SetDifficulty("nightmare"))); // 없는 값
            Assert.Equal("hard", s.Difficulty);
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(s, new SortieRequest { StepId = "step01", Seed = 1, Delegate = true });
            Assert.True(plan.Ok);
            var step = Game.Data.Defs.StepDef.Load(Kit.Source, "step01");
            foreach (var e in step.Enemy)
                Assert.Equal(Kit.D.Ai.Shift(e.Difficulty, "hard"), plan.Setup.Corps.First(c => c.Id == e.Id).AiDifficulty);
            Assert.Contains(plan.Setup.Corps, c => c.Team == StoryFlow.EnemyTeam && c.AiDifficulty != step.Enemy.First(e => e.Id == c.Id).Difficulty);
        }

        [Fact(DisplayName = "어려움은 결과가 달라진다 (협공·상성 대응이 실제로 들어간다)")]
        public void HardChangesBattle()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var s = StartingKitService.CreateNewGame(Kit.D);
            s.ClearedSteps.Add("step01");
            var normal = flow.Prepare(s, new SortieRequest { StepId = "step02", Seed = 2, Delegate = true });
            s.Difficulty = "hard";
            var hard = flow.Prepare(s, new SortieRequest { StepId = "step02", Seed = 2, Delegate = true });
            Assert.NotEqual(BattleSimulation.Run(normal.Setup).StateHash, BattleSimulation.Run(hard.Setup).StateHash);
        }
    }
}
