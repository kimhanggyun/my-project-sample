using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Foundation.Math;

namespace Game.Flow
{
    /// <summary>
    /// 부대 키우기 (troop-growth.md, verification R-52): 훈련(돈 → 훈련도 +step, 상한 max)과 병종 승급(1단계 → 2단계 특화 → 3단계 정예, 훈련도·클리어한 스텝·돈).
    /// 승급은 **인원을 골라 갈라 낸다** (2026-11-14): 기본 부대는 늘 남고, 고른 병사가 그 아래 승급 부대(ParentUnitId = 기본 부대)로 옮겨 간다.
    /// 모두 고르면 기본 부대는 병력 0으로 남는다. 훈련도는 전투에서 공격·방어 보정 (combat.json training).
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
        /// <summary>승급할 인원이 1 미만이거나 지금 병력보다 많다.</summary>
        public const string BadCount = "badCount";
        /// <summary>새 승급 부대를 만들어야 하는데 부대 수 상한 (campaign units.maxCount).</summary>
        public const string UnitLimit = "unitLimit";
        /// <summary>이미 있는 승급 부대에 합치면 부대 상한(병력+부상병 &gt; unitMax)을 넘는다.</summary>
        public const string NoRoom = "noRoom";

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

        /// <summary>승급 비용 (돈): 승급할 인원 1,000명당 데이터 금액, 올림 (800명 → 1번 몫, 2,096명 → 3번 몫).</summary>
        public long PromoteCost(PromotionDef p, long count) => (System.Math.Max(1, count) + 999) / 1000 * p.Gold;

        /// <summary>승급 병종의 묶음 (병종마다 하나, army-v2.md 3.2). 없으면 null. 승급하면 여기에 합친다.</summary>
        public static UnitState PromotedChild(CampaignState s, string unitId, string to) => Army.Pool(s, to);

        /// <summary>부대 전부 승급 (예전 행동과 같은 입구: 인원 = 지금 병력).</summary>
        public string CheckPromote(CampaignState s, string unitId, string to) => CheckPromote(s, unitId, to, 0);

        /// <param name="count">승급할 인원 (0 = 지금 병력 전부).</param>
        public string CheckPromote(CampaignState s, string unitId, string to, long count)
        {
            var u = s.FindUnit(unitId);
            if (u == null) return NoUnit;
            var p = PromotionsFor(u).Find(x => x.To == to);
            if (p == null) return NoPromotion;
            if (count <= 0) count = u.Troops;
            if (count < 1 || count > u.Troops) return BadCount;
            if (u.Training < p.Training) return LowTraining;
            if (p.Step != null && !s.ClearedSteps.Contains(p.Step)) return StepLocked;
            var child = PromotedChild(s, unitId, to);
            if (child == null && s.Units.Count >= _defs.Campaign.UnitCountMax) return UnitLimit;
            if (child != null && child.Troops + child.Wounded + count > Army.TroopMax(_defs, s)) return NoRoom;
            if (child == null && count > Army.TroopMax(_defs, s)) return NoRoom;
            if (new Wallet(s.Wallet).Get(ResourceIds.Gold) < PromoteCost(p, count)) return NotEnough;
            return Ok;
        }

        public bool Promote(CampaignState s, string unitId, string to) => Promote(s, unitId, to, 0);

        /// <summary>
        /// 승급 (R-52): 기본 부대에서 count명을 빼 그 아래 승급 부대로 옮긴다. 승급 부대가 없으면 새로 만들고 (훈련도·결속은 기본 부대 값),
        /// 있으면 합친다 (훈련도·결속 = 인원 가중 평균, 내림). 부상병은 옮기지 않는다. 기본 부대는 병력 0이 되어도 남는다.
        /// </summary>
        public bool Promote(CampaignState s, string unitId, string to, long count)
        {
            if (CheckPromote(s, unitId, to, count) != Ok) return false;
            var u = s.FindUnit(unitId);
            if (count <= 0) count = u.Troops;
            var p = PromotionsFor(u).Find(x => x.To == to);
            new Wallet(s.Wallet).TrySpend(ResourceIds.Gold, PromoteCost(p, count));
            u.Troops -= count;
            var child = PromotedChild(s, unitId, to);
            if (child == null)
            {
                child = new UnitState { UnitId = to, TroopType = to, ParentUnitId = unitId, Troops = count, Training = u.Training, Cohesion = u.Cohesion };
                s.Units.Insert(s.Units.IndexOf(u) + 1 + CountDescendants(s, unitId), child); // 기본 부대와 그 아래 부대들 바로 뒤
                return true;
            }
            long before = child.Troops;
            child.Training = Average(child.Training, before, u.Training, count);
            child.Cohesion = Average(child.Cohesion, before, u.Cohesion, count);
            child.Troops = before + count;
            return true;
        }

        private static Fix Average(Fix a, long na, Fix b, long nb) =>
            na + nb <= 0 ? a : Fix.FromRaw((a.Raw * na + b.Raw * nb) / (na + nb));

        private static int CountDescendants(CampaignState s, string unitId)
        {
            int n = 0;
            foreach (var x in s.Units) if (x.ParentUnitId == unitId) n += 1 + CountDescendants(s, x.UnitId);
            return n;
        }
    }
}
