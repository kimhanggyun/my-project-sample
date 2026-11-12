using System.Collections.Generic;
using Game.Campaign;
using Game.Data.Defs;

namespace Game.Flow
{
    /// <summary>
    /// 출진 전 안내 (story.md 출진 준비, E22): 적 병력은 대략(천 단위 반올림)으로, 내 출진 병력과 견준 전력 판단(유리·비슷·불리·위험),
    /// 군량을 몇 분 분량 가져갈지(R-7 자동 = 3분)와 지갑으로 가져갈 수 있는 최대 분.
    /// </summary>
    public static class SortieAdvice
    {
        public const string Favorable = "favorable", Even = "even", Unfavorable = "unfavorable", Dangerous = "dangerous";
        /// <summary>R-7 자동 군량이 몇 분 분량인지.</summary>
        public const int AutoMinutes = 3;
        public const int MinMinutes = 1, MaxMinutes = 10;

        /// <summary>대략 병력: 천 단위 반올림 (1000 미만이면 백 단위).</summary>
        public static long Rough(long troops) => troops >= 1000 ? (troops + 500) / 1000 * 1000 : (troops + 50) / 100 * 100;

        public static long EnemyTroops(StepDef step, bool includeReserve)
        {
            long n = 0;
            foreach (var e in step.Enemy) if (includeReserve || !e.Reserve) foreach (var u in e.Units) n += u.Troops;
            return n;
        }

        /// <summary>전력 판단: 내 병력 ÷ 적 병력(예비 포함) — 1.1 이상 유리, 0.8 이상 비슷, 0.55 이상 불리, 그 밑 위험.</summary>
        public static string Judge(long mine, long enemy)
        {
            if (enemy <= 0) return Favorable;
            long m = mine * 100;
            return m >= enemy * 110 ? Favorable : m >= enemy * 80 ? Even : m >= enemy * 55 ? Unfavorable : Dangerous;
        }

        /// <summary>몇 분 분량 군량 = 자동 군량 × 분 / 3 (올림).</summary>
        public static long SupplyFor(long autoSupply, int minutes) => (autoSupply * minutes + AutoMinutes - 1) / AutoMinutes;

        /// <summary>지갑의 군량으로 가져갈 수 있는 최대 분 (MinMinutes 밑이면 0).</summary>
        public static int AffordableMinutes(long autoSupply, long wallet)
        {
            for (int m = MaxMinutes; m >= MinMinutes; m--) if (SupplyFor(autoSupply, m) <= wallet) return m;
            return 0;
        }
    }
}
