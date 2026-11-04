using Game.Data.Rules;
using Game.Foundation.Math;

namespace Game.Online
{
    /// <summary>부대 고용 계산 (verification R-24, R-25, R-27).</summary>
    public static class HireCalculator
    {
        /// <summary>
        /// R-24: 고용 가능 인원 = 최소(등록 인원, 내 군량으로 먹일 수 있는 인원, 장수 한도의 남은 자리).
        /// 먹일 수 있는 인원 = 가용 군량 ÷ (병사 1명의 출진 분량 군량), 내림.
        /// </summary>
        public static long MaxHireable(BalanceData b, long registered, long availableSupply, long supplyPerDay, long capRemaining)
        {
            Fix perSoldier = b.SortieSupplyMinutes * (supplyPerDay * b.SupplyDaysPerMinute);
            long affordable = perSoldier <= Fix.Zero || availableSupply <= 0 ? 0 : Fix.DivideFloor(availableSupply, perSoldier);
            long max = System.Math.Min(registered, affordable);
            max = System.Math.Min(max, capRemaining);
            return System.Math.Max(0, max);
        }

        /// <summary>R-25: 비용 = 1명당 가격 × 인원.</summary>
        public static long UnitHireCost(long pricePerSoldier, long count) => checked(pricePerSoldier * count);

        /// <summary>R-25: 등록자 보상은 고용 인원에 비례.</summary>
        public static long OwnerReward(Fix rewardPerSoldier, long count) => (rewardPerSoldier * count).FloorToLong();

        /// <summary>R-27: 등록 가격 ≤ 추천 가격 × 상한 배수.</summary>
        public static bool IsPriceAllowed(BalanceData b, long price, long recommendedPrice) =>
            price >= 0 && Fix.FromInt(price) <= b.HirePriceCapMultiplier * recommendedPrice;
    }
}
