using Game.Campaign;
using Game.Campaign.Roster;
using Game.Campaign.Troops;
using Game.Data.Defs;
using Game.Economy;

namespace Game.Flow
{
    /// <summary>
    /// 병력 관리 (formation.md 4.1, verification R-10·R-33): 징병(군량)·모병(돈)으로 부대를 채우고, 부상병을 치료한다.
    /// 부대 최대 병력 = 병력 + 부상병 ≤ 부대 최대치 (`balance.json` troopCap.unitMax). 비용이 모자라면 아무것도 바뀌지 않는다.
    /// </summary>
    public sealed class TroopService
    {
        private readonly DefinitionSet _defs;

        public TroopService(DefinitionSet defs) { _defs = defs; }

        /// <summary>더 채울 수 있는 인원.</summary>
        public long Room(UnitState u) => System.Math.Max(0, _defs.Balance.UnitMaxTroops - u.Troops - u.Wounded);

        /// <summary>징병: 군량으로 count명(남은 자리까지) 채운다. 채운 인원 (0이면 실패).</summary>
        public long Conscript(CampaignState state, string unitId, long count) =>
            Fill(state, unitId, count, ResourceIds.Supply, n => HealCostCalculator.ConscriptCost(_defs.Balance, n));

        /// <summary>모병: 돈으로 채운다.</summary>
        public long Recruit(CampaignState state, string unitId, long count) =>
            Fill(state, unitId, count, ResourceIds.Gold, n => HealCostCalculator.RecruitCost(_defs.Balance, n));

        /// <summary>치료: 부상병 count명을 즉시 병력으로 (비용 = 징병·모병의 절반, R-10). payWith = supply 또는 gold.</summary>
        public long Heal(CampaignState state, string unitId, long count, string payWith)
        {
            var u = state.FindUnit(unitId);
            if (u == null || count <= 0) return 0;
            if (count > u.Wounded) count = u.Wounded;
            if (count <= 0) return 0;
            long cost = payWith == ResourceIds.Gold ? HealCostCalculator.HealCostInGold(_defs.Balance, count) : HealCostCalculator.HealCostInSupply(_defs.Balance, count);
            if (!new Wallet(state.Wallet).TrySpend(payWith == ResourceIds.Gold ? ResourceIds.Gold : ResourceIds.Supply, cost)) return 0;
            return CasualtyService.Heal(u, count);
        }

        public long ConscriptCost(long count) => HealCostCalculator.ConscriptCost(_defs.Balance, count);
        public long RecruitCost(long count) => HealCostCalculator.RecruitCost(_defs.Balance, count);

        private long Fill(CampaignState state, string unitId, long count, string resource, System.Func<long, long> cost)
        {
            var u = state.FindUnit(unitId);
            if (u == null || count <= 0) return 0;
            long n = System.Math.Min(count, Room(u));
            if (n <= 0) return 0;
            if (!new Wallet(state.Wallet).TrySpend(resource, cost(n))) return 0;
            u.Troops = checked(u.Troops + n);
            return n;
        }
    }
}
