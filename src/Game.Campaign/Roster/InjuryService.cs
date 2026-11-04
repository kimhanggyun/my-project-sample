using Game.Data.Defs;
using Game.Data.Rules;
using Game.Foundation.Time;

namespace Game.Campaign.Roster
{
    public static class InjuryKinds
    {
        public const string Light = "light";
        public const string Heavy = "heavy";
    }

    /// <summary>
    /// 장수 부상 (battle.md 2.5.3, 스위치 generalInjury): 경상(출전 가능, 능력치 × 배율), 중상(출전 불가).
    /// 회복 시각이 지나면 회복. 돈으로 즉시 치료. 포로 상태에는 부상을 덮어쓰지 않는다.
    /// </summary>
    public sealed class InjuryService
    {
        public const string GeneralInjuryFlag = "generalInjury";

        private readonly DefinitionSet _defs;
        private readonly RuleFlags _flags;
        private readonly ITimeProvider _time;

        public InjuryService(DefinitionSet defs, RuleFlags flags, ITimeProvider time)
        {
            _defs = defs;
            _flags = flags;
            _time = time;
        }

        private long Now => _time.UtcNow.ToUnixTimeSeconds();

        public void Apply(CampaignState state, string generalId, string kind)
        {
            var g = state.FindGeneral(generalId);
            if (g == null || !_flags.IsEnabled(GeneralInjuryFlag) || g.Status == GeneralStatus.Captured) return;
            if (kind == InjuryKinds.Heavy)
            {
                g.Status = GeneralStatus.SeriousInjury;
                g.StatusUntilUnixSeconds = Now + _defs.Campaign.HeavyInjurySeconds;
            }
            else if (kind == InjuryKinds.Light && g.Status != GeneralStatus.SeriousInjury)
            {
                g.Status = GeneralStatus.LightInjury;
                g.StatusUntilUnixSeconds = Now + _defs.Campaign.LightInjurySeconds;
            }
        }

        /// <summary>회복 시각이 지난 부상을 회복.</summary>
        public void Recover(CampaignState state)
        {
            foreach (var g in state.Generals)
                if ((g.Status == GeneralStatus.LightInjury || g.Status == GeneralStatus.SeriousInjury) && Now >= g.StatusUntilUnixSeconds)
                {
                    g.Status = GeneralStatus.Available;
                    g.StatusUntilUnixSeconds = 0;
                }
        }

        /// <summary>즉시 치료 비용 (돈) = 남은 시간(시간 단위, 올림) × 시간당 비용.</summary>
        public long HealCost(GeneralState g)
        {
            if (g.Status != GeneralStatus.LightInjury && g.Status != GeneralStatus.SeriousInjury) return 0;
            long left = System.Math.Max(0, g.StatusUntilUnixSeconds - Now);
            long hours = (left + 3599) / 3600;
            return checked(hours * _defs.Campaign.HealGoldPerHourLeft);
        }

        /// <summary>돈을 내고 즉시 치료. 돈이 모자라면 false (아무것도 바뀌지 않음).</summary>
        public bool HealNow(CampaignState state, string generalId, string goldResourceId = "gold")
        {
            var g = state.FindGeneral(generalId);
            if (g == null) return false;
            long cost = HealCost(g);
            if (cost == 0) return false;
            state.Wallet.TryGetValue(goldResourceId, out var have);
            if (have < cost) return false;
            state.Wallet[goldResourceId] = have - cost;
            g.Status = GeneralStatus.Available;
            g.StatusUntilUnixSeconds = 0;
            return true;
        }
    }
}
