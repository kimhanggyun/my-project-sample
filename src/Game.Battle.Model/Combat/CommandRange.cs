using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Combat
{
    /// <summary>
    /// 지휘 범위 (battle.md 2.6 추가 규칙, R-77). 스위치 commandRange.
    /// 장수는 군단 중심(Anchor)에 있다. 범위 = baseCells × 통솔/50. 범위 안이면 1, 넘으면 fadeCells에 걸쳐 minFactor까지 줄어든다.
    /// 장수 능력치가 소부대 공격·방어에 주는 영향에 곱한다.
    /// </summary>
    public static class CommandRange
    {
        public const string Flag = "commandRange";
        public const string HoldDefenseFlag = "holdDefense";
        public const string RestSurpriseFlag = "restSurprise";
        private static readonly Fix Fifty = Fix.FromInt(50);

        public static Fix Range(BattleContext ctx, CorpsState corps) =>
            ctx.Combat.CommandRangeBaseCells * corps.Setup.General.Leadership / Fifty;

        public static Fix Factor(BattleContext ctx, CorpsState corps, SubUnitState u)
        {
            var c = ctx.Combat;
            if (!ctx.IsEnabled(Flag) || c.CommandRangeBaseCells <= Fix.Zero || corps.Setup.General == null) return Fix.OneValue;
            Fix range = Range(ctx, corps);
            Fix d2 = Geometry.Vec.DistanceSquared(u.Position, corps.Anchor);
            if (d2 <= range * range) return Fix.OneValue;
            Fix over = Fix.Sqrt(d2) - range;
            if (over >= c.CommandRangeFadeCells) return c.CommandRangeMinFactor;
            return Fix.OneValue - (Fix.OneValue - c.CommandRangeMinFactor) * over / c.CommandRangeFadeCells;
        }
    }
}
