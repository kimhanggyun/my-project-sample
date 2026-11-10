using Game.Campaign.Roster;
using Game.Data.Rules;

namespace Game.Campaign.Troops
{
    /// <summary>전투 손실을 보유 부대에 반영 (R-9): 병력에서 빼고, 부상병은 따로 모은다 (치료하면 돌아옴).</summary>
    public static class CasualtyService
    {
        public const string WoundedRecoveryFlag = "woundedRecovery";

        public static (long wounded, long dead) Apply(BalanceData b, RuleFlags flags, UnitState unit, long losses)
        {
            if (losses <= 0) return (0, 0);
            if (losses > unit.Troops) losses = unit.Troops;
            var (wounded, dead) = CasualtySplitter.Split(b, losses, flags.IsEnabled(WoundedRecoveryFlag));
            unit.Troops -= losses;
            unit.Wounded = checked(unit.Wounded + wounded);
            return (wounded, dead);
        }

        /// <summary>
        /// 부상병이 새로 생기면 회복 시각을 지금 + 회복 시간으로 다시 잡는다 (R-61: 부대의 부상병 전부가 그 시각에 함께 돌아온다).
        /// 회복 시간 0이면 자연 회복 없음.
        /// </summary>
        public static void ScheduleRecovery(BalanceData b, UnitState unit, long nowUnixSeconds)
        {
            if (unit.Wounded <= 0 || b.WoundedRecoverMinutes <= 0) return;
            unit.WoundedRecoverAtUnixSeconds = checked(nowUnixSeconds + b.WoundedRecoverMinutes * 60);
        }

        /// <summary>회복 시각이 지난 부대의 부상병을 전부 병력으로 (R-61). 돌아온 인원.</summary>
        public static long RecoverDue(UnitState unit, long nowUnixSeconds)
        {
            if (unit.Wounded <= 0 || unit.WoundedRecoverAtUnixSeconds <= 0 || nowUnixSeconds < unit.WoundedRecoverAtUnixSeconds) return 0;
            unit.WoundedRecoverAtUnixSeconds = 0;
            return Heal(unit, unit.Wounded);
        }

        /// <summary>부상병 치료 → 병력으로 복귀 (비용은 Economy의 HealCostCalculator로 따로 낸다).</summary>
        public static long Heal(UnitState unit, long count)
        {
            if (count > unit.Wounded) count = unit.Wounded;
            if (count <= 0) return 0;
            unit.Wounded -= count;
            unit.Troops = checked(unit.Troops + count);
            if (unit.Wounded == 0) unit.WoundedRecoverAtUnixSeconds = 0;
            return count;
        }
    }
}
