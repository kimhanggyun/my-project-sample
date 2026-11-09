using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Time;
using Game.Online;

namespace Game.Flow
{
    /// <summary>
    /// 서버 쪽 전투 검증·반영 (verification R-40): 서버가 가진 진행 상태에서 같은 출진 요청으로 출진 준비를 하고,
    /// 같은 시드·대결 선택·명령 기록으로 전투를 다시 돌려 기기가 낸 상태 지문·틱 수와 비교한다. 같으면 출진 확정 + 결과 반영.
    /// 전투가 결정적이라(R-29) 기기와 서버가 같은 답을 낸다. 백엔드 종류와 무관한 순수 C#.
    /// </summary>
    public sealed class BattleVerifier
    {
        private readonly StoryFlow _flow;

        public BattleVerifier(DefinitionSet defs, IDataSource source, ITimeProvider time)
        {
            _flow = new StoryFlow(defs, source, time);
        }

        /// <summary>
        /// 서버 쪽 한 번에: 기록 시각 검사(R-42) → 그 시각으로 검증·반영(R-40). 맞으면 json = 새 세이브, at = 기록 시각 (새 세이브 시각).
        /// Revision 검사는 부르는 쪽 (저장소).
        /// </summary>
        public static BattleVerdict VerifySave(DefinitionSet defs, IDataSource source, CloudSave save, BattleSubmission s, long nowUnixSeconds, out string json, out long at)
        {
            json = null;
            at = RecordTime.Resolve(s?.PlayedAtUnixSeconds ?? 0, nowUnixSeconds);
            if (s == null || !RecordTime.Allowed(save.SavedAtUnixSeconds, at, nowUnixSeconds))
                return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadTime };
            var state = SaveJson.Read(save.Json);
            if (state == null) return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie };
            var clock = new ManualTimeProvider(System.DateTimeOffset.FromUnixTimeSeconds(at));
            var v = new BattleVerifier(defs, source, clock).VerifyAndApply(state, s, out _);
            if (v.Valid) json = SaveJson.Write(state);
            at = System.Math.Min(at, nowUnixSeconds);
            return v;
        }

        public static SortieRequest RequestOf(BattleSubmission s) => new SortieRequest
        {
            StepId = s.StepId,
            Seed = s.Seed,
            Delegate = s.Delegate,
            DelegateGenerals = new List<string>(s.DelegateGenerals ?? new List<string>()),
            Supply = s.Supply,
            ContestChoices = s.ContestChoices == null ? new Dictionary<string, List<string>>() : new Dictionary<string, List<string>>(s.ContestChoices),
        };

        /// <summary>
        /// 검증하고 맞으면 state에 반영한다 (출진 확정 → 결과 반영). 틀리면 state는 그대로.
        /// </summary>
        public BattleVerdict VerifyAndApply(CampaignState state, BattleSubmission s, out BattleReport report)
        {
            report = null;
            SortiePlan plan;
            try { plan = _flow.Prepare(state, RequestOf(s)); }
            catch (System.Exception) { return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie }; }
            if (!plan.Ok) return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie };

            var result = BattleSimulation.Run(plan.Setup, s.Commands);
            var verdict = new BattleVerdict { Hash = result.StateHash.ToString("x16"), Ticks = result.Ticks, Winner = result.Winner };
            if (verdict.Hash != s.ClaimedHash || verdict.Ticks != s.ClaimedTicks)
            {
                verdict.Reason = VerdictReasons.Mismatch;
                return verdict;
            }
            if (!_flow.Confirm(state, plan))
            {
                verdict.Reason = VerdictReasons.BadSortie;
                return verdict;
            }
            report = _flow.ApplyResult(state, plan, result);
            verdict.Valid = true;
            verdict.Reason = VerdictReasons.Ok;
            return verdict;
        }
    }
}
