using System.Collections.Generic;
using Game.Data.Rules;
using Game.Foundation.Math;

namespace Game.Economy
{
    /// <summary>군량 계산 (verification R-6, R-7, R-8).</summary>
    public static class SupplyCalculator
    {
        /// <summary>하루치 소비 = Σ(병력 × 병종 하루 소비량).</summary>
        public static long DailyConsumption(IEnumerable<(long troops, long supplyPerDay)> units)
        {
            long sum = 0;
            foreach (var u in units) sum = checked(sum + u.troops * u.supplyPerDay);
            return sum;
        }

        /// <summary>R-6: 전투 소비 = 하루치 × 하루 환산(1분 = 7일) × 분. 내림.</summary>
        public static long BattleConsumption(BalanceData b, long dailyConsumption, Fix minutes) =>
            (minutes * (dailyConsumption * b.SupplyDaysPerMinute)).FloorToLong();

        /// <summary>R-7: 출진 군량 자동 계산 (기본 3분 분량, 고용 병사 포함한 전체 출전 병력 기준). 올림.</summary>
        public static long AutoSortieSupply(BalanceData b, long dailyConsumption) =>
            (b.SortieSupplyMinutes * (dailyConsumption * b.SupplyDaysPerMinute)).CeilToLong();

        /// <summary>
        /// R-8: 퇴각한 적 군단의 군량. 가져감 = 남은 군량 × 퇴각 시 병력 ÷ 출전 병력 (내림), 나머지 몰수.
        /// 궤멸·포로 군단은 전량 몰수.
        /// </summary>
        public static (long keep, long confiscated) RetreatSplit(long remainingSupply, long troopsAtRetreat, long troopsAtStart, bool destroyedOrCaptured)
        {
            if (remainingSupply <= 0) return (0, 0);
            if (destroyedOrCaptured || troopsAtStart <= 0 || troopsAtRetreat <= 0) return (0, remainingSupply);
            long survivors = System.Math.Min(troopsAtRetreat, troopsAtStart);
            long keep = checked(remainingSupply * survivors) / troopsAtStart;
            return (keep, remainingSupply - keep);
        }
    }
}
