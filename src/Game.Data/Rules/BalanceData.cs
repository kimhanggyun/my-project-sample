using Game.Data.Json;
using Game.Foundation.Math;
using Newtonsoft.Json.Linq;

namespace Game.Data.Rules
{
    /// <summary>병합이 끝난 balance 값을 규칙 코드가 쓰기 좋게 담은 읽기 전용 객체.</summary>
    public sealed class BalanceData
    {
        public Fix DamageModifierMinPp { get; private set; }
        public Fix DamageModifierMaxPp { get; private set; }

        public Fix MoraleBase { get; private set; }
        public Fix MoraleBonusCoefficient { get; private set; }
        public Fix MoraleMinPenaltyPp { get; private set; }

        public Fix TroopCapMinLeadership { get; private set; }
        public Fix TroopCapMaxLeadership { get; private set; }
        public long TroopCapMin { get; private set; }
        public long TroopCapMax { get; private set; }
        public long UnitMaxTroops { get; private set; }

        public long SupplyDaysPerMinute { get; private set; }
        public Fix SortieSupplyMinutes { get; private set; }

        public Fix WoundedRatio { get; private set; }
        public Fix HealCostRatio { get; private set; }
        public long ConscriptSupplyPerSoldier { get; private set; }
        public long RecruitGoldPerSoldier { get; private set; }

        public Fix HirePriceCapMultiplier { get; private set; }
        public long HireGeneralBasePrice { get; private set; }
        public long HireGeneralPricePerLevel { get; private set; }
        public long HireUnitPricePerSoldier { get; private set; }
        public long HireUnitPricePerTier { get; private set; }
        public Fix HireUnitPricePerTraining { get; private set; }
        public long HireSupplyPerGold { get; private set; }
        public Fix HireOwnerRewardRatio { get; private set; }
        public int HireMaxListingsPerKind { get; private set; }

        public Fix LightInjuryStatMultiplier { get; private set; }

        public long SoldierBudget { get; private set; }

        public JObject Raw { get; private set; }

        public static BalanceData From(JObject merged)
        {
            const string c = "balance";
            var dmg = JsonUtil.RequireObject(merged, "damage", c);
            var morale = JsonUtil.RequireObject(merged, "morale", c);
            var cap = JsonUtil.RequireObject(merged, "troopCap", c);
            var supply = JsonUtil.RequireObject(merged, "supply", c);
            var casualty = JsonUtil.RequireObject(merged, "casualty", c);
            var hire = JsonUtil.RequireObject(merged, "hire", c);
            var injury = JsonUtil.RequireObject(merged, "generalInjury", c);
            var display = JsonUtil.RequireObject(merged, "display", c);
            return new BalanceData
            {
                Raw = merged,
                DamageModifierMinPp = JsonUtil.RequireFix(dmg, "modifierMinPp", c + ".damage"),
                DamageModifierMaxPp = JsonUtil.RequireFix(dmg, "modifierMaxPp", c + ".damage"),
                MoraleBase = JsonUtil.RequireFix(morale, "base", c + ".morale"),
                MoraleBonusCoefficient = JsonUtil.RequireFix(morale, "bonusCoefficient", c + ".morale"),
                MoraleMinPenaltyPp = JsonUtil.RequireFix(morale, "minPenaltyPp", c + ".morale"),
                TroopCapMinLeadership = JsonUtil.RequireFix(cap, "minLeadership", c + ".troopCap"),
                TroopCapMaxLeadership = JsonUtil.RequireFix(cap, "maxLeadership", c + ".troopCap"),
                TroopCapMin = JsonUtil.RequireLong(cap, "min", c + ".troopCap"),
                TroopCapMax = JsonUtil.RequireLong(cap, "max", c + ".troopCap"),
                UnitMaxTroops = JsonUtil.RequireLong(cap, "unitMax", c + ".troopCap"),
                SupplyDaysPerMinute = JsonUtil.RequireLong(supply, "daysPerMinute", c + ".supply"),
                SortieSupplyMinutes = JsonUtil.RequireFix(supply, "sortieMinutes", c + ".supply"),
                WoundedRatio = JsonUtil.RequireFix(casualty, "woundedRatio", c + ".casualty"),
                HealCostRatio = JsonUtil.RequireFix(casualty, "healCostRatio", c + ".casualty"),
                ConscriptSupplyPerSoldier = JsonUtil.RequireLong(casualty, "conscriptSupplyPerSoldier", c + ".casualty"),
                RecruitGoldPerSoldier = JsonUtil.RequireLong(casualty, "recruitGoldPerSoldier", c + ".casualty"),
                HirePriceCapMultiplier = JsonUtil.RequireFix(hire, "priceCapMultiplier", c + ".hire"),
                HireGeneralBasePrice = JsonUtil.RequireLong(hire, "generalBasePrice", c + ".hire"),
                HireGeneralPricePerLevel = JsonUtil.RequireLong(hire, "generalPricePerLevel", c + ".hire"),
                HireUnitPricePerSoldier = JsonUtil.RequireLong(hire, "unitPricePerSoldier", c + ".hire"),
                HireUnitPricePerTier = JsonUtil.RequireLong(hire, "unitPricePerTier", c + ".hire"),
                HireUnitPricePerTraining = JsonUtil.RequireFix(hire, "unitPricePerTraining", c + ".hire"),
                HireSupplyPerGold = JsonUtil.RequireLong(hire, "supplyPerGold", c + ".hire"),
                HireOwnerRewardRatio = JsonUtil.RequireFix(hire, "ownerRewardRatio", c + ".hire"),
                HireMaxListingsPerKind = (int)JsonUtil.RequireLong(hire, "maxListingsPerKind", c + ".hire"),
                LightInjuryStatMultiplier = JsonUtil.RequireFix(injury, "lightStatMultiplier", c + ".generalInjury"),
                SoldierBudget = JsonUtil.RequireLong(display, "soldierBudget", c + ".display"),
            };
        }
    }
}
