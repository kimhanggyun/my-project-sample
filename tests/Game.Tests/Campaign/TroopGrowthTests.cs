using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Economy;
using Game.Flow;
using Game.Foundation.Math;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>부대 키우기 (troop-growth.md, verification R-52): 훈련·승급·훈련도 전투 보정.</summary>
    public class TroopGrowthTests
    {
        private static CampaignState Rich()
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            s.Wallet[ResourceIds.Gold] = 100_000;
            return s;
        }

        [Fact(DisplayName = "R-52 훈련: 병력 1,000명당 돈 20(올림)을 내고 훈련도 +0.1, 상한 1.0")]
        public void Train()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("u01"); // 보병 4,000
            Assert.Equal(80, svc.TrainCost(u));
            Assert.True(svc.Train(s, "u01"));
            Assert.Equal(Fix.FromRaw(1000), u.Training);
            Assert.Equal(100_000 - 80, new Wallet(s.Wallet).Get(ResourceIds.Gold));
            for (int i = 0; i < 20; i++) svc.Train(s, "u01");
            Assert.Equal(Fix.OneValue, u.Training);
            Assert.Equal(TroopGrowthService.MaxTraining, svc.CheckTrain(s, "u01"));
            s.Wallet[ResourceIds.Gold] = 0;
            Assert.Equal(TroopGrowthService.NotEnough, svc.CheckTrain(s, "u02"));
        }

        [Fact(DisplayName = "R-52 승급: 부모 병종에서만, 훈련도·클리어한 스텝·돈이 모두 되면. 병력·훈련도는 그대로")]
        public void Promote()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("u01");
            Assert.Equal(new[] { "shieldbearer", "engineer" }, svc.PromotionsFor(u).Select(p => p.To)); // 보병 → 방패병 또는 공병 (R-83)
            Assert.Equal(TroopGrowthService.LowTraining, svc.CheckPromote(s, "u01", "shieldbearer"));
            u.Training = Fix.FromRaw(5000);
            Assert.Equal(TroopGrowthService.StepLocked, svc.CheckPromote(s, "u01", "shieldbearer"));
            s.ClearedSteps.Add("step02");
            Assert.Equal(TroopGrowthService.NoPromotion, svc.CheckPromote(s, "u01", "pikeman")); // 다른 계열
            Assert.True(svc.Promote(s, "u01", "shieldbearer"));
            Assert.Equal(("shieldbearer", 4000L, 5000L), (u.TroopType, u.Troops, u.Training.Raw));
            Assert.Equal(100_000 - 400, new Wallet(s.Wallet).Get(ResourceIds.Gold));
            // 2단계 다음은 3단계 정예: 방패병 → 근위병 (훈련도 0.8, step05, 돈 1,200)
            Assert.Equal(new[] { "guard" }, svc.PromotionsFor(u).Select(p => p.To));
            Assert.Equal(TroopGrowthService.LowTraining, svc.CheckPromote(s, "u01", "guard"));
            u.Training = Fix.FromRaw(8000);
            Assert.Equal(TroopGrowthService.StepLocked, svc.CheckPromote(s, "u01", "guard"));
            s.ClearedSteps.Add("step05");
            Assert.True(svc.Promote(s, "u01", "guard"));
            Assert.Equal(("guard", 3, 4000L), (u.TroopType, Kit.D.TroopTypes[u.TroopType].Tier, u.Troops));
            Assert.Equal(100_000 - 400 - 1200, new Wallet(s.Wallet).Get(ResourceIds.Gold));
            Assert.Empty(svc.PromotionsFor(u)); // 3단계가 끝
        }

        [Fact(DisplayName = "R-52 3단계 정예: 전투 2단계 4종마다 하나씩, 승급 조건은 2단계보다 높다 (공병은 3단계 없음)")]
        public void EliteTier()
        {
            var g = Kit.D.TroopGrowth;
            foreach (var t2 in Kit.D.TroopTypes.Values.Where(t => t.Tier == 2 && !t.HasTag("engineer")))
            {
                var up = g.Promotions.Where(p => p.From == t2.Id).ToList();
                var p3 = Assert.Single(up);
                var t3 = Kit.D.TroopTypes[p3.To];
                Assert.Equal(3, t3.Tier);
                Assert.True(t3.HasTag("elite"), t3.Id);
                var p2 = g.Promotions.Single(p => p.To == t2.Id);
                Assert.True(p3.Training > p2.Training && p3.Gold > p2.Gold, t3.Id);
                Assert.True(Kit.D.Strings.ContainsKey(t3.NameKey) && Kit.D.Strings.ContainsKey("troop.desc." + t3.Id), t3.Id);
            }
        }

        [Fact(DisplayName = "R-52 훈련도 전투 보정: 같은 조건에서 훈련도 1인 부대가 더 많이 남긴다, 훈련도 0이면 보정 없음 (기준 전투 그대로)")]
        public void TrainingInBattle()
        {
            long Remaining(long trainingRaw)
            {
                var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
                var s = StartingKitService.CreateNewGame(Kit.D);
                foreach (var u in s.Units) u.Training = Fix.FromRaw(trainingRaw);
                var plan = flow.Prepare(s, new SortieRequest { StepId = "step01", Seed = 1, Delegate = true });
                var r = BattleSimulation.Run(plan.Setup, new List<Game.Battle.Model.Commands.BattleCommand>());
                return r.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).Sum(c => c.RemainingTroops);
            }
            Assert.True(Remaining(10000) > Remaining(0));
        }

        [Fact(DisplayName = "부대 키우기 데이터 검사: 승급의 to는 from의 자식 병종, 같은 승급 두 번 없음")]
        public void Validation()
        {
            var troops = Kit.D.TroopTypes;
            TroopGrowthRules.From(JsonUtil.ParseObject("{\"training\":{\"step\":0.1,\"max\":1,\"goldPer1000\":1},\"promotions\":[{\"from\":\"infantry\",\"to\":\"shieldbearer\"}]}"), troops);
            Assert.Throws<DataException>(() => TroopGrowthRules.From(JsonUtil.ParseObject("{\"training\":{\"step\":0.1,\"max\":1,\"goldPer1000\":1},\"promotions\":[{\"from\":\"archer\",\"to\":\"shieldbearer\"}]}"), troops));
            Assert.Throws<DataException>(() => TroopGrowthRules.From(JsonUtil.ParseObject("{\"training\":{\"step\":0.1,\"max\":2,\"goldPer1000\":1}}"), troops));
        }

        [Fact(DisplayName = "훈련·승급은 행동(unit.train·unit.promote): 서버도 같은 규칙")]
        public void AsActions()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = Rich();
            s.ClearedSteps.Add("step02");
            for (int i = 0; i < 5; i++) Assert.True(runner.Apply(s, PlayerActionRunner.TrainUnit("u02")));
            Assert.True(runner.Apply(s, PlayerActionRunner.PromoteUnit("u02", "pikeman")));
            Assert.Equal("pikeman", s.FindUnit("u02").TroopType);
            Assert.False(runner.Apply(s, PlayerActionRunner.PromoteUnit("u02", "pikeman")));
        }
    }
}
