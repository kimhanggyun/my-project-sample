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
        private readonly IDataSource _source;
        private readonly DefinitionSet _defs;

        public BattleVerifier(DefinitionSet defs, IDataSource source, ITimeProvider time)
        {
            _flow = new StoryFlow(defs, source, time);
            _source = source;
            _defs = defs;
        }

        /// <summary>
        /// 서버 쪽 한 번에: 기록 시각 검사(R-42) → 그 시각으로 검증·반영(R-40). 맞으면 json = 새 세이브, at = 기록 시각 (새 세이브 시각).
        /// Revision 검사는 부르는 쪽 (저장소).
        /// </summary>
        /// <param name="findListing">고용 등록 찾기 (등록 id → 등록, 없으면 null). 고용이 있는 제출에 필요 (R-43).</param>
        /// <param name="accountId">제출한 계정 (내 등록은 고용할 수 없다).</param>
        /// <param name="hired">맞으면 고용 내역 (서버가 등록자 보상을 만든다). 고용이 없으면 null.</param>
        public static BattleVerdict VerifySave(DefinitionSet defs, IDataSource source, CloudSave save, BattleSubmission s, long nowUnixSeconds, out string json, out long at,
            out HiredForces hired, System.Func<string, HireListing> findListing = null, string accountId = null, IReadOnlyList<HireLogEntry> hireLog = null,
            System.Func<string, TerritoryDefense> findDefense = null, System.Func<string, string> findMap = null)
        {
            json = null;
            hired = null;
            at = RecordTime.Resolve(s?.PlayedAtUnixSeconds ?? 0, nowUnixSeconds);
            if (s == null || !RecordTime.Allowed(save.SavedAtUnixSeconds, at, nowUnixSeconds))
                return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadTime };
            var state = SaveJson.Read(save.Json);
            if (state == null) return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie };
            if (s.Hires != null && s.Hires.Count > 0)
            {
                if (findListing == null || s.Compositions == null) return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie };
                hired = new HireRules(defs).Resolve(s.Hires, findListing, accountId, state, out var error);
                if (hired == null) return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie };
                if (HireBook.CheckLimits(defs.Balance, hireLog, s.Hires, at) != null) { hired = null; return new BattleVerdict { Valid = false, Reason = VerdictReasons.HireLimit }; }
            }
            // 영토: 서버의 지금 수비 편성과 같은 판이어야 같은 전투 (R-48)
            TerritoryDefense defense = null;
            if (TerritoryBattles.IsTerritoryStep(s.StepId))
            {
                defense = findDefense?.Invoke(TerritoryBattles.RegionOf(s.StepId));
                if ((defense?.Version ?? 0) != s.DefenseVersion) { hired = null; return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie }; }
            }
            var clock = new ManualTimeProvider(System.DateTimeOffset.FromUnixTimeSeconds(at));
            string mapId = s.CrowdCommands != null ? findMap?.Invoke(TerritoryBattles.RegionOf(s.StepId)) : null; // 깃발전 전장은 서버 장부의 깃발 맵 (R-168)
            var v = new BattleVerifier(defs, source, clock).VerifyAndApply(state, s, hired, defense, mapId, out _);
            if (!v.Valid) hired = null;
            if (v.Valid) json = SaveJson.Write(state);
            at = System.Math.Min(at, nowUnixSeconds);
            return v;
        }

        public static SortieRequest RequestOf(BattleSubmission s, HiredForces hired = null, TerritoryDefense defense = null) => new SortieRequest
        {
            Defense = defense,
            Compositions = s.Compositions == null ? null : PlayerActionRunner.FromActionCorps(s.Compositions),
            Location = s.Location,
            Hired = hired,
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
        public BattleVerdict VerifyAndApply(CampaignState state, BattleSubmission s, out BattleReport report) => VerifyAndApply(state, s, null, null, out report);

        public BattleVerdict VerifyAndApply(CampaignState state, BattleSubmission s, HiredForces hired, out BattleReport report) => VerifyAndApply(state, s, hired, null, out report);

        public BattleVerdict VerifyAndApply(CampaignState state, BattleSubmission s, HiredForces hired, TerritoryDefense defense, out BattleReport report) =>
            VerifyAndApply(state, s, hired, defense, null, out report);

        public BattleVerdict VerifyAndApply(CampaignState state, BattleSubmission s, HiredForces hired, TerritoryDefense defense, string mapId, out BattleReport report)
        {
            report = null;
            // 출진 시각(시계) 기준 시간 경과: 기기도 출진 직전에 같은 처리를 한다 (부상 회복·부상병 자연 회복, R-61)
            _flow.Tick(state);
            SortiePlan plan;
            try { plan = _flow.Prepare(state, RequestOf(s, hired, defense)); }
            catch (System.Exception) { return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie }; }
            if (!plan.Ok) return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie };

            BattleResult result;
            if (s.CrowdCommands != null)
            {
                // 깃발전 (R-166): 영지 공성전만, 같은 설정 · 명령 기록으로 병사 단위 전투를 다시 돌린다
                if (!TerritoryBattles.IsTerritoryStep(s.StepId) || !Game.Territory.FlagDefs.IsFlagKey(TerritoryBattles.RegionOf(s.StepId)))
                    return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie };
                var fd = TerritoryBattles.TryLoad(_source)?.Flags;
                string map = fd != null && fd.IsMap(mapId) ? mapId : fd?.DefaultMap ?? "crowd_plain";
                Game.Battle.Model.Crowd.CrowdBattle crowd;
                try { crowd = FlagBattles.Replay(_defs, _source, FlagBattles.Setup(_defs, _source, plan, map), s.CrowdCommands, s.ClaimedTicks); }
                catch (System.Exception) { return new BattleVerdict { Valid = false, Reason = VerdictReasons.BadSortie }; }
                result = FlagBattles.Result(crowd, plan);
            }
            else result = BattleSimulation.Run(plan.Setup, s.Commands);
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
            if (hired != null)
            {
                hired.StepId = s.StepId;
                hired.Outcomes.Clear();
                hired.Outcomes.AddRange(HireBook.Outcomes(plan, result, hired));
                hired.Replay = hired.Outcomes.Count > 0 ? ReplayService.Capture(plan, s.Commands, result, 0) : null; // 내 용병이 고용된 전투 녹화 (R-120)
            }
            verdict.Valid = true;
            verdict.Reason = VerdictReasons.Ok;
            return verdict;
        }
    }
}
