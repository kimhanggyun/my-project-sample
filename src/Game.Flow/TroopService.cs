using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Campaign.Troops;
using Game.Data.Defs;
using Game.Economy;
using Game.Foundation.Math;

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

        /// <summary>새로 만들 수 있는 병종: 기본 병종(1단계, formation.md 4.1).</summary>
        public List<string> CreatableTroopTypes()
        {
            var list = new List<string>();
            foreach (var t in _defs.TroopTypes.Values) if (t.Tier == 1) list.Add(t.Id);
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        public bool CanCreate(CampaignState state) => state.Units.Count < _defs.Campaign.UnitCountMax;

        /// <summary>
        /// 새 부대 창설 (verification R-39): 기본 병종만, 부대 수 상한까지. 병력 0·훈련도 0·결속 0으로 만들고 징병·모병으로 채운다.
        /// 만들 수 없으면 null.
        /// </summary>
        public UnitState Create(CampaignState state, string troopType)
        {
            if (!CanCreate(state) || !_defs.TroopTypes.TryGetValue(troopType ?? "", out var t) || t.Tier != 1) return null;
            var u = new UnitState { UnitId = NextUnitId(state), TroopType = t.Id, Troops = 0, Training = Fix.Zero, Cohesion = Fix.Zero };
            state.Units.Add(u);
            return u;
        }

        /// <summary>해산할 수 있는가: 기본·상황별 어느 편성에도 들어 있지 않아야 한다.</summary>
        public bool CanDisband(CampaignState state, string unitId) => state.FindUnit(unitId) != null && !InAnyComposition(state, unitId);

        /// <summary>부대 해산 (R-39): 편성에 없는 부대만. 병력·부상병은 사라진다.</summary>
        public bool Disband(CampaignState state, string unitId)
        {
            if (!CanDisband(state, unitId)) return false;
            state.Units.Remove(state.FindUnit(unitId));
            return true;
        }

        public static bool InAnyComposition(CampaignState state, string unitId)
        {
            foreach (var c in state.Compositions) if (c.Slots.ContainsValue(unitId)) return true;
            foreach (var p in state.CompositionPresets.Values)
                if (p != null) foreach (var c in p) if (c.Slots.ContainsValue(unitId)) return true;
            return false;
        }

        /// <summary>"u" + 두 자리 이상 번호. 지금 있는 번호 중 가장 큰 것 + 1.</summary>
        private static string NextUnitId(CampaignState state)
        {
            int max = 0;
            foreach (var u in state.Units)
                if (u.UnitId != null && u.UnitId.Length > 1 && u.UnitId[0] == 'u' && int.TryParse(u.UnitId.Substring(1), out var n) && n > max) max = n;
            string id;
            do { id = "u" + (++max).ToString("D2"); } while (state.FindUnit(id) != null);
            return id;
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
