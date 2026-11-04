using System.Linq;
using Game.Campaign;
using Game.Data.Defs;
using Game.Flow;
using Game.Foundation.Math;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>
    /// 밸런스 시뮬레이션 (dev-rules 6.1): 스텝마다 목표 승률 범위(balanceTarget)를 고정 시드 60개의 위임 출진으로 확인한다.
    /// 결정적이라 흔들리지 않는다. 밸런스를 바꿨는데 범위를 벗어나면 데이터(적 병력·AI)나 목표를 다시 정한다.
    /// </summary>
    public class StepBalanceTests
    {
        private static CampaignState StartOf(string stepId)
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            foreach (var id in Kit.D.StepIndex.Order)
            {
                if (id == stepId) break;
                s.ClearedSteps.Add(id);
            }
            return s;
        }

        [Theory(DisplayName = "스텝 목표 승률 범위 (위임 출진, 초기 편성, 고정 시드 60개)")]
        [InlineData("step01")]
        [InlineData("step02")]
        public void WinRateInTarget(string stepId)
        {
            var step = StepDef.Load(Kit.Source, stepId);
            Assert.True(step.TargetWinRateMin.HasValue, $"{stepId}: balanceTarget이 없습니다.");
            var stats = new StepSimulator(Kit.D, Kit.Source).Measure(stepId, () => StartOf(stepId), Enumerable.Range(1, 60).Select(i => (ulong)i));
            Assert.Empty(stats.Errors);
            Assert.True(stats.WinRate >= step.TargetWinRateMin.Value && stats.WinRate <= step.TargetWinRateMax.Value,
                $"{stepId}: 승률 {stats.Wins}/{stats.Runs} (목표 {step.TargetWinRateMin}~{step.TargetWinRateMax}), 평균 {stats.AverageSeconds}초, 손실 {stats.AverageLossPercent}%");
        }
    }
}
