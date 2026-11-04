using System;
using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Math;
using Game.Foundation.Time;

namespace Game.Flow
{
    /// <summary>스텝 한 개를 여러 시드로 돌린 결과 (ai.md 11장 "스텝 승률 확인").</summary>
    public sealed class StepStats
    {
        public string StepId { get; internal set; }
        public int Runs { get; internal set; }
        public int Wins { get; internal set; }
        public int TimeLimitEnds { get; internal set; }
        public Fix WinRate => Runs == 0 ? Fix.Zero : Fix.FromRatio(Wins, Runs);
        public Fix AverageSeconds { get; internal set; }
        /// <summary>아군 손실 비율 평균 (%).</summary>
        public Fix AverageLossPercent { get; internal set; }
        /// <summary>지참 군량 중 쓴 비율 평균 (%), 포획 전.</summary>
        public Fix AverageSupplyUsedPercent { get; internal set; }
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// 밸런스 확인 도구: 진행 상태를 만들어(같은 출발점) 위임 출진으로 스텝을 여러 시드 돌린다.
    /// 상태는 매 판 새로 만들므로 판끼리 영향이 없다. 결정적이라 같은 시드 목록이면 같은 결과.
    /// </summary>
    public sealed class StepSimulator
    {
        private readonly DefinitionSet _defs;
        private readonly IDataSource _source;

        public StepSimulator(DefinitionSet defs, IDataSource source)
        {
            _defs = defs;
            _source = source;
        }

        /// <param name="makeState">한 판의 출발 상태 (예: 새 게임 + 앞 스텝 클리어).</param>
        public StepStats Measure(string stepId, Func<CampaignState> makeState, IEnumerable<ulong> seeds, bool delegate_ = true)
        {
            var stats = new StepStats { StepId = stepId };
            var flow = new StoryFlow(_defs, _source, new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            Fix seconds = Fix.Zero, loss = Fix.Zero, supply = Fix.Zero;
            foreach (var seed in seeds)
            {
                var state = makeState();
                var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = seed, Delegate = delegate_ });
                if (!plan.Ok || !flow.Confirm(state, plan))
                {
                    stats.Errors.Add($"시드 {seed}: " + string.Join("; ", plan.Errors));
                    continue;
                }
                var result = BattleSimulation.Run(plan.Setup);
                var report = flow.ApplyResult(state, plan, result);
                stats.Runs++;
                if (report.Won) stats.Wins++;
                if (result.EndReason == BattleEndReasons.TimeLimit) stats.TimeLimitEnds++;
                seconds += result.ElapsedSeconds;
                long start = 0, lost = 0;
                foreach (var c in result.Corps)
                    if (c.Team == StoryFlow.PlayerTeam) { start += c.StartTroops; lost += c.Losses; }
                if (start > 0) loss += Fix.FromRatio(lost * 100, start);
                if (plan.SupplyTaken > 0) supply += Fix.FromRatio((plan.SupplyTaken - report.SupplyReturned) * 100, plan.SupplyTaken);
            }
            if (stats.Runs > 0)
            {
                stats.AverageSeconds = seconds / stats.Runs;
                stats.AverageLossPercent = loss / stats.Runs;
                stats.AverageSupplyUsedPercent = supply / stats.Runs;
            }
            return stats;
        }
    }
}
