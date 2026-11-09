using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Foundation.Math;

namespace Game.Flow
{
    /// <summary>
    /// 부대 키우기 (troop-growth.md, verification R-52): 훈련(돈 → 훈련도 +step, 상한 max)과 병종 승급(1단계 → 2단계 특화, 훈련도·클리어한 스텝·돈).
    /// 승급해도 병력·부상병·훈련도·결속은 그대로. 훈련도는 전투에서 공격·방어 보정 (combat.json training).
    /// </summary>
    public sealed class TroopGrowthService
    {
        public const string Ok = "ok";
        public const string NoUnit = "noUnit";
        public const string MaxTraining = "maxTraining";
        public const string NotEnough = "notEnough";
        public const string NoPromotion = "noPromotion";
        public const string LowTraining = "lowTraining";
        public const string StepLocked = "stepLocked";

        private readonly DefinitionSet _defs;
        public TroopGrowthService(DefinitionSet defs) { _defs = defs; }

        private TroopGrowthRules Rules => _defs.TroopGrowth;

        /// <summary>훈련 한 번 비용 (돈): 병력+부상병 1,000명당 goldPer1000, 올림. 최소 1번 몫.</summary>
        public long TrainCost(UnitState u)
        {
            long troops = System.Math.Max(1, u.Troops + u.Wounded);
            return (troops + 999) / 1000 * Rules.TrainingGoldPer1000;
        }

        public string CheckTrain(CampaignState s, string unitId)
        {
            if (!Rules.Enabled) return NoPromotion;
            var u = s.FindUnit(unitId);
            if (u == null) return NoUnit;
            if (u.Training >= Rules.TrainingMax) return MaxTraining;
            if (new Wallet(s.Wallet).Get(ResourceIds.Gold) < TrainCost(u)) return NotEnough;
            return Ok;
        }

        public bool Train(CampaignState s, string unitId)
        {
            if (CheckTrain(s, unitId) != Ok) return false;
            var u = s.FindUnit(unitId);
            new Wallet(s.Wallet).TrySpend(ResourceIds.Gold, TrainCost(u));
            u.Training = Fix.Min(Rules.TrainingMax, u.Training + Rules.TrainingStep);
            return true;
        }

        /// <summary>이 부대가 갈 수 있는 승급 (지금 병종에서 시작하는 것).</summary>
        public List<PromotionDef> PromotionsFor(UnitState u)
        {
            var list = new List<PromotionDef>();
            if (u != null) foreach (var p in Rules.Promotions) if (p.From == u.TroopType) list.Add(p);
            return list;
        }

        public string CheckPromote(CampaignState s, string unitId, string to)
        {
            var u = s.FindUnit(unitId);
            if (u == null) return NoUnit;
            var p = PromotionsFor(u).Find(x => x.To == to);
            if (p == null) return NoPromotion;
            if (u.Training < p.Training) return LowTraining;
            if (p.Step != null && !s.ClearedSteps.Contains(p.Step)) return StepLocked;
            if (new Wallet(s.Wallet).Get(ResourceIds.Gold) < p.Gold) return NotEnough;
            return Ok;
        }

        public bool Promote(CampaignState s, string unitId, string to)
        {
            if (CheckPromote(s, unitId, to) != Ok) return false;
            var u = s.FindUnit(unitId);
            var p = PromotionsFor(u).Find(x => x.To == to);
            new Wallet(s.Wallet).TrySpend(ResourceIds.Gold, p.Gold);
            u.TroopType = to;
            return true;
        }
    }
}
