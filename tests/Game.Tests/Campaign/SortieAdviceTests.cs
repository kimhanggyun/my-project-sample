using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>E22 출진 전 안내.</summary>
    public class SortieAdviceTests
    {
        [Fact(DisplayName = "대략 병력·전력 판단·군량 분 계산")]
        public void Advice()
        {
            Assert.Equal(25000, SortieAdvice.Rough(24600));
            Assert.Equal(800, SortieAdvice.Rough(760));
            Assert.Equal(SortieAdvice.Favorable, SortieAdvice.Judge(12000, 10000));
            Assert.Equal(SortieAdvice.Even, SortieAdvice.Judge(9000, 10000));
            Assert.Equal(SortieAdvice.Unfavorable, SortieAdvice.Judge(6000, 10000));
            Assert.Equal(SortieAdvice.Dangerous, SortieAdvice.Judge(4000, 10000));
            Assert.Equal(3000, SortieAdvice.SupplyFor(3000, 3));
            Assert.Equal(5000, SortieAdvice.SupplyFor(3000, 5));
            Assert.Equal(4, SortieAdvice.AffordableMinutes(3000, 4500));
            Assert.Equal(0, SortieAdvice.AffordableMinutes(3000, 500));
            var step = Game.Data.Defs.StepDef.Load(Kit.Source, "step10");
            Assert.True(SortieAdvice.EnemyTroops(step, true) > SortieAdvice.EnemyTroops(step, false)); // 예비 군단
        }
    }
}
