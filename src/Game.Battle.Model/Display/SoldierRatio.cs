namespace Game.Battle.Model.Display
{
    /// <summary>
    /// 화면 병사 1명이 대표하는 병력 수 (verification R-28). 한 전투 안의 모든 군단이 같은 값을 쓴다.
    /// 예산을 넘지 않도록 올림한다.
    /// </summary>
    public static class SoldierRatio
    {
        public static long TroopsPerSoldier(long totalTroopsOnField, long soldierBudget)
        {
            if (soldierBudget <= 0) throw new System.ArgumentOutOfRangeException(nameof(soldierBudget));
            if (totalTroopsOnField <= soldierBudget) return 1;
            return (totalTroopsOnField + soldierBudget - 1) / soldierBudget;
        }

        public static long VisibleSoldiers(long troops, long troopsPerSoldier) =>
            troops <= 0 ? 0 : (troops + troopsPerSoldier - 1) / troopsPerSoldier;
    }
}
