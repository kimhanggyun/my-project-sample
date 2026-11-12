using Game.Battle.Model.Simulation;
using Game.Foundation.Math;

namespace Game.Battle.Model.Combat
{
    /// <summary>
    /// 전열과 후열 (battle.md 2.4.1, R-73). 스위치 frontLine.
    /// 근접 소부대는 앞줄 병력(FrontTroops)만큼만 친다. 앞줄 한도 = frontLine.troopsPerUnit × (100 + 진형 정면 폭 %)/100.
    /// 잃은 병력은 앞줄에서 먼저 빠지고, 뒷줄이 1초에 한도의 refillPctPerSecond%씩 앞으로 나와 채운다 (한꺼번에 무너지면 잠시 얇아진다).
    /// 원거리 소부대는 머리 위로 쏘므로 전체 병력으로 친다.
    /// </summary>
    public static class FrontLine
    {
        public const string Flag = "frontLine";

        public static bool On(BattleContext ctx) => ctx.IsEnabled(Flag) && ctx.Combat.FrontLineTroopsPerUnit > Fix.Zero;

        /// <summary>앞줄 한도 (병력 수).</summary>
        public static Fix Cap(BattleContext ctx, CorpsState corps)
        {
            Fix pct = corps.Formation?.Stats?.FrontWidthPct ?? Fix.Zero;
            return Fix.Max(Fix.FromInt(100), ctx.Combat.FrontLineTroopsPerUnit * (Fix.Hundred + pct) / Fix.Hundred);
        }

        /// <summary>이 소부대가 이번 틱에 치는 병력.</summary>
        internal static Fix Attackers(BattleContext ctx, CorpsState corps, SubUnitState u, bool rangedShot)
        {
            Fix all = Fix.FromInt(u.Troops);
            if (rangedShot || !On(ctx)) return all;
            if (u.FrontTroops < Fix.Zero) u.FrontTroops = Fix.Min(all, Cap(ctx, corps));
            return Fix.Min(all, u.FrontTroops);
        }

        /// <summary>손실을 반영한 뒤: 앞줄에서 빼고, 뒷줄이 채운다.</summary>
        internal static void AfterLosses(BattleContext ctx, CorpsState corps, SubUnitState u, long lost)
        {
            if (!On(ctx)) return;
            Fix cap = Fix.Min(Fix.FromInt(u.Troops), Cap(ctx, corps));
            if (u.FrontTroops < Fix.Zero) u.FrontTroops = cap;
            u.FrontTroops = Fix.Max(Fix.Zero, u.FrontTroops - Fix.FromInt(lost));
            Fix refill = Cap(ctx, corps) * ctx.Combat.FrontLineRefillPctPerSecond / Fix.Hundred * ctx.Combat.TickSeconds;
            u.FrontTroops = Fix.Min(cap, u.FrontTroops + refill);
        }
    }
}
