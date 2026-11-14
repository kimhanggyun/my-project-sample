using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
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
            // 예전 기준 수치 그대로: 보병 4,000 · 창병 3,000, 병종 최대 5,000 (백부장 Lv3)
            var s = Kit.Veteran();
            s.LordLevel = 3;
            Army.Pool(s, "infantry").Troops = 4000;
            Army.Pool(s, "spearman").Troops = 3000;
            s.Wallet[ResourceIds.Gold] = 100_000;
            return s;
        }

        [Fact(DisplayName = "R-52 훈련: 병력 1,000명당 돈 20(올림)을 내고 훈련도 +0.1, 상한 1.0")]
        public void Train()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("infantry"); // 보병 4,000
            Assert.Equal(80, svc.TrainCost(u));
            Assert.True(svc.Train(s, "infantry"));
            Assert.Equal(Fix.FromRaw(1000), u.Training);
            Assert.Equal(100_000 - 80, new Wallet(s.Wallet).Get(ResourceIds.Gold));
            for (int i = 0; i < 20; i++) svc.Train(s, "infantry");
            Assert.Equal(Fix.OneValue, u.Training);
            Assert.Equal(TroopGrowthService.MaxTraining, svc.CheckTrain(s, "infantry"));
            s.Wallet[ResourceIds.Gold] = 0;
            Assert.Equal(TroopGrowthService.NotEnough, svc.CheckTrain(s, "spearman"));
        }

        [Fact(DisplayName = "R-52 승급 조건: 부모 병종에서만, 훈련도·클리어한 스텝·돈이 모두 되면")]
        public void PromoteChecks()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("infantry");
            Assert.Equal(new[] { "shieldbearer", "engineer" }, svc.PromotionsFor(u).Select(p => p.To)); // 보병 → 방패병 또는 공병 (R-83)
            Assert.Equal(TroopGrowthService.LowTraining, svc.CheckPromote(s, "infantry", "shieldbearer"));
            u.Training = Fix.FromRaw(5000);
            Assert.Equal(TroopGrowthService.StepLocked, svc.CheckPromote(s, "infantry", "shieldbearer"));
            s.ClearedSteps.Add("step02");
            Assert.Equal(TroopGrowthService.NoPromotion, svc.CheckPromote(s, "infantry", "pikeman")); // 다른 계열
            Assert.Equal(TroopGrowthService.BadCount, svc.CheckPromote(s, "infantry", "shieldbearer", 4001)); // 지금 병력보다 많음
            Assert.Equal(TroopGrowthService.Ok, svc.CheckPromote(s, "infantry", "shieldbearer", 800));
            s.Wallet[ResourceIds.Gold] = 399;
            Assert.Equal(TroopGrowthService.NotEnough, svc.CheckPromote(s, "infantry", "shieldbearer", 800));
        }

        [Fact(DisplayName = "R-52 부분 승급: 800명만 방패병 부대로 갈라 나가고(돈 1,000명당 400, 올림) 보병 3,200명은 남는다. 훈련도·결속은 물려받음")]
        public void PartialPromotion()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("infantry"); // 보병 4,000
            u.Training = Fix.FromRaw(5000);
            u.Cohesion = Fix.FromRaw(70000);
            s.ClearedSteps.Add("step02");
            int before = s.Units.Count;
            Assert.True(svc.Promote(s, "infantry", "shieldbearer", 800));
            Assert.Equal(("infantry", 3200L), (u.TroopType, u.Troops)); // 기본 부대는 그대로 남는다
            var child = TroopGrowthService.PromotedChild(s, "infantry", "shieldbearer");
            Assert.NotNull(child);
            Assert.Equal(("shieldbearer", 800L, 5000L, 70000L, "infantry"), (child.TroopType, child.Troops, child.Training.Raw, child.Cohesion.Raw, child.ParentUnitId));
            Assert.Equal(before + 1, s.Units.Count);
            Assert.Equal(s.Units.IndexOf(u) + 1, s.Units.IndexOf(child)); // 기본 부대 바로 뒤
            Assert.Equal(100_000 - 400, new Wallet(s.Wallet).Get(ResourceIds.Gold));
            Assert.Equal(1200, svc.PromoteCost(svc.PromotionsFor(u).First(p => p.To == "shieldbearer"), 2096)); // 2,096명 → 3번 몫
        }

        [Fact(DisplayName = "R-52 다시 승급하면 같은 승급 부대에 합친다 (훈련도·결속 = 인원 가중 평균, 내림). 부대 상한을 넘으면 안 됨")]
        public void MergePromotion()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("infantry");
            u.Training = Fix.FromRaw(5000);
            s.ClearedSteps.Add("step02");
            Assert.True(svc.Promote(s, "infantry", "shieldbearer", 1000));
            u.Training = Fix.FromRaw(8000);
            int count = s.Units.Count;
            Assert.True(svc.Promote(s, "infantry", "shieldbearer", 3000));
            var child = TroopGrowthService.PromotedChild(s, "infantry", "shieldbearer");
            Assert.Equal(count, s.Units.Count); // 새 부대 없음
            Assert.Equal((4000L, 7250L), (child.Troops, child.Training.Raw)); // (0.5×1000 + 0.8×3000) / 4000 = 0.725
            Assert.Equal(0L, u.Troops);
            // 상한: 승급 부대 4,000 + 부상 600 + 새로 500 > 5,000
            u.Troops = 500;
            child.Wounded = 600;
            Assert.Equal(TroopGrowthService.NoRoom, svc.CheckPromote(s, "infantry", "shieldbearer", 500));
            Assert.Equal(TroopGrowthService.Ok, svc.CheckPromote(s, "infantry", "shieldbearer", 400));
        }

        [Fact(DisplayName = "R-52 전체 승급: 인원 0(= 전부)이면 모두 옮기고 기본 부대는 병력 0으로 남는다. 부상병은 기본 부대에 남는다")]
        public void FullPromotion()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("infantry");
            u.Training = Fix.FromRaw(5000);
            u.Wounded = 120;
            s.ClearedSteps.Add("step02");
            Assert.True(svc.Promote(s, "infantry", "shieldbearer"));
            Assert.Equal(("infantry", 0L, 120L), (u.TroopType, u.Troops, u.Wounded));
            Assert.Equal(4000L, TroopGrowthService.PromotedChild(s, "infantry", "shieldbearer").Troops);
            Assert.Equal(TroopGrowthService.BadCount, svc.CheckPromote(s, "infantry", "shieldbearer")); // 병력 0이면 더 못 함
        }

        [Fact(DisplayName = "R-52 새 승급 부대도 부대 수 상한에 든다 (합치기는 상한과 무관)")]
        public void PromotionUnitLimit()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("infantry");
            u.Training = Fix.FromRaw(5000);
            s.ClearedSteps.Add("step02");
            s.ClearedSteps.Add("step03"); // 공병 승급 조건
            Assert.True(svc.Promote(s, "infantry", "shieldbearer", 100));
            var troops = new TroopService(Kit.D);
            while (s.Units.Count < Kit.D.Campaign.UnitCountMax) s.Units.Add(new UnitState { UnitId = "x" + s.Units.Count, TroopType = "x" + s.Units.Count }); // 묶음 수 상한까지 채움
            Assert.Equal(TroopGrowthService.UnitLimit, svc.CheckPromote(s, "infantry", "engineer", 100));
            Assert.Equal(TroopGrowthService.Ok, svc.CheckPromote(s, "infantry", "shieldbearer", 100));
        }

        [Fact(DisplayName = "R-52 승급 부대에서 다시 승급 (방패병 → 근위병): 승급 부대 아래에 또 갈라 나간다")]
        public void PromoteFromPromoted()
        {
            var svc = new TroopGrowthService(Kit.D);
            var s = Rich();
            var u = s.FindUnit("infantry");
            u.Training = Fix.FromRaw(8000);
            s.ClearedSteps.Add("step02");
            s.ClearedSteps.Add("step05");
            Assert.True(svc.Promote(s, "infantry", "shieldbearer", 2000));
            var shield = TroopGrowthService.PromotedChild(s, "infantry", "shieldbearer");
            Assert.Equal(new[] { "guard" }, svc.PromotionsFor(shield).Select(p => p.To));
            long gold = new Wallet(s.Wallet).Get(ResourceIds.Gold);
            Assert.True(svc.Promote(s, shield.UnitId, "guard", 500));
            var guard = TroopGrowthService.PromotedChild(s, shield.UnitId, "guard");
            Assert.Equal(("guard", 3, 500L, 1500L), (guard.TroopType, Kit.D.TroopTypes[guard.TroopType].Tier, guard.Troops, shield.Troops));
            Assert.Equal(gold - 1200, new Wallet(s.Wallet).Get(ResourceIds.Gold));
            Assert.Empty(svc.PromotionsFor(guard)); // 3단계가 끝
            // 목록 차례: 보병 → 방패병 → 근위병
            Assert.Equal(new[] { "infantry", shield.UnitId, guard.UnitId }, s.Units.SkipWhile(x => x.UnitId != "infantry").Take(3).Select(x => x.UnitId));
        }

        [Fact(DisplayName = "R-52 3단계 정예: 전투 2단계 4종마다 하나씩, 승급 조건은 2단계보다 높다 (공병·수군은 3단계 없음)")]
        public void EliteTier()
        {
            var g = Kit.D.TroopGrowth;
            foreach (var t2 in Kit.D.TroopTypes.Values.Where(t => t.Tier == 2 && !t.HasTag("engineer") && !t.HasTag("naval")))
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
                var s = Kit.Veteran();
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

        [Fact(DisplayName = "훈련·승급은 행동(unit.train·unit.promote, 인원 Count): 서버도 같은 규칙")]
        public void AsActions()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = Rich();
            s.ClearedSteps.Add("step02");
            for (int i = 0; i < 5; i++) Assert.True(runner.Apply(s, PlayerActionRunner.TrainUnit("spearman")));
            long troops = s.FindUnit("spearman").Troops;
            Assert.True(runner.Apply(s, PlayerActionRunner.PromoteUnit("spearman", "pikeman", 500)));
            Assert.Equal(("spearman", troops - 500), (s.FindUnit("spearman").TroopType, s.FindUnit("spearman").Troops));
            Assert.Equal(500L, TroopGrowthService.PromotedChild(s, "spearman", "pikeman").Troops);
            Assert.True(runner.Apply(s, PlayerActionRunner.PromoteUnit("spearman", "pikeman"))); // 인원 0 = 나머지 전부 (예전 행동)
            Assert.Equal(0L, s.FindUnit("spearman").Troops);
            Assert.Equal(troops, TroopGrowthService.PromotedChild(s, "spearman", "pikeman").Troops);
            Assert.False(runner.Apply(s, PlayerActionRunner.PromoteUnit("spearman", "pikeman")));
        }
    }
}
