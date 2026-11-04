using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Data.Rules;
using Game.Foundation.Math;
using Game.Foundation.Random;
using Game.Foundation.Time;

namespace Game.Campaign.Captivity
{
    public static class CaptivityFlags
    {
        public const string Capture = "capture";
        public const string RescueBattle = "rescueBattle";
        public const string RescueDeadline = "rescueDeadline";
        public const string Recruitment = "recruitment";
        public const string RecruitRetry = "recruitRetry";
        public const string ReleaseReward = "releaseReward";
    }

    public enum RecruitOutcome
    {
        /// <summary>아군 장수가 됨.</summary>
        Recruited,
        /// <summary>실패, 계속 감금 (재시도 대기).</summary>
        FailedKept,
        /// <summary>실패, 재시도 스위치가 꺼져 있어 해방됨.</summary>
        FailedReleased,
        /// <summary>시도할 수 없음 (등용 불가 장수, 대기 중, 스위치 꺼짐, 없는 포로).</summary>
        NotAllowed,
    }

    /// <summary>
    /// 포로·구출·등용 (captivity.md). 시간은 <see cref="ITimeProvider"/>에서만, 무작위는 시드 난수에서만 얻는다.
    /// 스위치는 기능별 (상위 capture가 꺼지면 모두 꺼짐, R-19).
    /// </summary>
    public sealed class CaptivityService
    {
        private readonly DefinitionSet _defs;
        private readonly RuleFlags _flags;
        private readonly ITimeProvider _time;

        public CaptivityService(DefinitionSet defs, RuleFlags flags, ITimeProvider time)
        {
            _defs = defs;
            _flags = flags;
            _time = time;
        }

        private long Now => _time.UtcNow.ToUnixTimeSeconds();
        private CampaignRules Rules => _defs.Campaign;

        /// <summary>아군 장수가 잡힘 → 수용소에 갇힘, 출전 불가, 구출 기한 기록.</summary>
        public void CaptureOwn(CampaignState state, string generalId, string campId)
        {
            var g = state.FindGeneral(generalId);
            if (g == null || !_flags.IsEnabled(CaptivityFlags.Capture)) return;
            g.Status = GeneralStatus.Captured;
            g.CampId = campId;
            g.CapturedAtUnixSeconds = Now;
            g.RescueDeadlineUnixSeconds = Now + Rules.RescueDeadlineSeconds;
            g.StatusUntilUnixSeconds = 0;
        }

        /// <summary>적 장수를 잡음 → 감금 목록.</summary>
        public void AddPrisoner(CampaignState state, string generalId, bool brainwashBonus)
        {
            if (!_flags.IsEnabled(CaptivityFlags.Capture) || state.FindGeneral(generalId) != null) return;
            var p = state.FindPrisoner(generalId);
            if (p != null) { p.BrainwashBonus |= brainwashBonus; return; }
            state.Prisoners.Add(new PrisonerState { GeneralId = generalId, CapturedAtUnixSeconds = Now, BrainwashBonus = brainwashBonus });
        }

        /// <summary>구출 기한이 지난 아군 장수를 자동 석방 (rescueDeadline). 풀려난 장수 id 목록.</summary>
        public List<string> ReleaseExpired(CampaignState state)
        {
            var freed = new List<string>();
            if (!_flags.IsEnabled(CaptivityFlags.RescueDeadline)) return freed;
            foreach (var g in state.Generals)
                if (g.Status == GeneralStatus.Captured && Now >= g.RescueDeadlineUnixSeconds)
                {
                    Free(g);
                    freed.Add(g.GeneralId);
                }
            return freed;
        }

        /// <summary>구출 전투 승리 → 그 수용소의 아군 장수를 되찾는다 (rescueBattle).</summary>
        public List<string> Rescue(CampaignState state, string campId)
        {
            var freed = new List<string>();
            if (!_flags.IsEnabled(CaptivityFlags.RescueBattle)) return freed;
            foreach (var g in state.Generals)
                if (g.Status == GeneralStatus.Captured && g.CampId == campId)
                {
                    Free(g);
                    freed.Add(g.GeneralId);
                }
            return freed;
        }

        /// <summary>아군 장수가 갇힌 수용소 id 목록 (스토리 탭에 구출 전투를 띄운다, 구출 전투 스위치가 켜졌을 때).</summary>
        public List<string> CampsWithCaptives(CampaignState state)
        {
            var camps = new List<string>();
            if (!_flags.IsEnabled(CaptivityFlags.RescueBattle)) return camps;
            foreach (var g in state.Generals)
                if (g.Status == GeneralStatus.Captured && g.CampId != null && !camps.Contains(g.CampId)) camps.Add(g.CampId);
            return camps;
        }

        /// <summary>등용 성공률 = 기본 + 성향 보정 + (세뇌 보너스), 0~1.</summary>
        public Fix RecruitChance(PrisonerState p)
        {
            var def = _defs.Generals[p.GeneralId];
            Fix c = Rules.RecruitBaseChance;
            if (Rules.TemperamentRecruitBonus.TryGetValue(def.Temperament, out var t)) c += t;
            if (p.BrainwashBonus) c += Rules.BrainwashBonus;
            return Fix.Clamp(c, Fix.Zero, Fix.OneValue);
        }

        public bool CanAttemptRecruit(CampaignState state, string generalId)
        {
            var p = state.FindPrisoner(generalId);
            if (p == null || !_flags.IsEnabled(CaptivityFlags.Recruitment) || !_defs.Generals[generalId].Recruitable) return false;
            return p.LastRecruitAttemptUnixSeconds == 0 || Now >= p.LastRecruitAttemptUnixSeconds + Rules.RecruitRetrySeconds;
        }

        /// <summary>등용 시도. 성공하면 아군 장수(기본 스킬·특성 포함), 실패하면 대기 후 재시도 또는 해방.</summary>
        public RecruitOutcome TryRecruit(CampaignState state, string generalId, IRandom rng)
        {
            if (!CanAttemptRecruit(state, generalId)) return RecruitOutcome.NotAllowed;
            var p = state.FindPrisoner(generalId);
            if (rng.Chance(RecruitChance(p)))
            {
                state.Prisoners.Remove(p);
                var def = _defs.Generals[generalId];
                var g = new GeneralState { GeneralId = generalId };
                g.LearnedSkills.AddRange(def.InnateSkills);
                g.LearnedTraits.AddRange(def.Traits);
                state.Generals.Add(g);
                return RecruitOutcome.Recruited;
            }
            if (_flags.IsEnabled(CaptivityFlags.RecruitRetry))
            {
                p.LastRecruitAttemptUnixSeconds = Now;
                return RecruitOutcome.FailedKept;
            }
            Release(state, generalId);
            return RecruitOutcome.FailedReleased;
        }

        /// <summary>해방 → 보상 (releaseReward). 받은 보상 (자원 id → 양).</summary>
        public Dictionary<string, long> Release(CampaignState state, string generalId)
        {
            var reward = new Dictionary<string, long>();
            var p = state.FindPrisoner(generalId);
            if (p == null) return reward;
            state.Prisoners.Remove(p);
            if (!_flags.IsEnabled(CaptivityFlags.ReleaseReward)) return reward;
            foreach (var kv in Rules.ReleaseReward)
            {
                state.Wallet.TryGetValue(kv.Key, out var have);
                state.Wallet[kv.Key] = checked(have + kv.Value);
                reward[kv.Key] = kv.Value;
            }
            return reward;
        }

        private static void Free(GeneralState g)
        {
            g.Status = GeneralStatus.Available;
            g.CampId = null;
            g.CapturedAtUnixSeconds = 0;
            g.RescueDeadlineUnixSeconds = 0;
        }
    }
}
