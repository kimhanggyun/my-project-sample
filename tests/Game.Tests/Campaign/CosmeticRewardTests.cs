using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Validation;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-111 치장 2단계: 스텝·숨은 보상, 중복이면 엽전, 치장 상품(결제 전용 허용), 남의 군대 겉모습.</summary>
    public class CosmeticRewardTests
    {
        private static CampaignState StartOf(string stepId)
        {
            var s = Kit.Veteran();
            // R-159: 군단마다 병종 하나 → 옛 전투(스토리, 지금 숨김)에서 step06(군단 3 상한, 적 약 6만)을 이기려면 장수를 키워 통솔 한도까지 채움.
            // 이 시험은 보상(치장)을 보는 것 — 새 전투 연결 뒤에는 그 전투로 다시
            foreach (var (i, unit) in new[] { (0, "spearman"), (1, "infantry"), (2, "archer") })
            {
                s.FindGeneral(s.Compositions[i].GeneralId).StatGrowth["leadership"] = Game.Foundation.Math.Fix.FromInt(60);
                Army.EnsurePool(Kit.D, s, unit).Troops = 20_000;
                s.Compositions[i].Picks[0].Troops = 20_000;
            }
            foreach (var id in Kit.D.StepIndex.Order) { if (id == stepId) break; if (!s.ClearedSteps.Contains(id)) s.ClearedSteps.Add(id); }
            s.ClearedSteps.Remove(stepId);
            return s;
        }

        /// <summary>위임 출진으로 이길 때까지 (최대 seeds판). 이긴 판의 보고와 세이브.</summary>
        private static (BattleReport report, CampaignState state) WinOnce(string stepId, System.Action<CampaignState> prepare = null, int seeds = 40)
        {
            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
                var state = StartOf(stepId);
                prepare?.Invoke(state);
                var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = seed, Delegate = true });
                Assert.True(plan.Ok, string.Join("\n", plan.Errors));
                Assert.True(flow.Confirm(state, plan));
                var report = flow.ApplyResult(state, plan, BattleSimulation.Run(plan.Setup));
                if (report.Won) return (report, state);
            }
            Assert.Fail(stepId + " 이긴 판이 없음");
            return default;
        }

        [Fact(DisplayName = "R-111 스텝 첫 클리어 치장: step06 = 무쇠 갑옷. 이미 가졌으면 엽전 duplicateGold(임시 200)로")]
        public void StepReward()
        {
            Assert.Contains("armor_iron", new StoryFlow(Kit.D, Kit.Source, Kit.Clock()).LoadStep("step06").RewardCosmetics);
            var (r, s) = WinOnce("step06");
            Assert.True(r.FirstClear);
            Assert.Contains("armor_iron", r.Cosmetics);
            Assert.Contains("armor_iron", s.OwnedCosmetics);
            Assert.Equal(0, r.CosmeticRefundGold);

            var (r2, s2) = WinOnce("step06", st => st.OwnedCosmetics.Add("armor_iron"));
            Assert.Empty(r2.Cosmetics);
            Assert.Equal(200, r2.CosmeticRefundGold);
            Assert.Equal(1, s2.OwnedCosmetics.Count(x => x == "armor_iron"));
        }

        [Fact(DisplayName = "R-111 숨은 보상 치장: '들판의 철벽'(step04)을 찾으면 진홍 갑옷도 계정이 얻는다")]
        public void SecretReward()
        {
            Assert.Equal("armor_crimson", Kit.D.Secrets.Single(x => x.Id == "secret_step04_iron_wall").RewardCosmetic);
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
                var state = Game.Campaign.StartingKitService.CreateNewGame(Kit.D);
                foreach (var id in Kit.D.StepIndex.Order) { if (id == "step04") break; state.ClearedSteps.Add(id); }
                var plan = flow.Prepare(state, new SortieRequest { StepId = "step04", Seed = seed, Delegate = true });
                flow.Confirm(state, plan);
                var report = flow.ApplyResult(state, plan, BattleSimulation.Run(plan.Setup));
                var f = report.Secrets.FirstOrDefault(x => x.SecretId == "secret_step04_iron_wall");
                if (f == null) continue;
                Assert.Equal("armor_crimson", f.Cosmetic);
                Assert.Contains("armor_crimson", state.OwnedCosmetics);
                return;
            }
            Assert.Fail("숨은 보상을 찾은 판이 없음");
        }

        [Fact(DisplayName = "R-111 치장 상품: 결제(영수증)하면 치장을 얻고, 이미 가졌으면 엽전. 플레이 경로 없이 팔아도 R-30 통과, 얻는 길 맞춤 검사 통과")]
        public void ProductAndData()
        {
            var p = Kit.D.Products["cosmetic_armor_gold"];
            Assert.Equal("armor_gold", p.CosmeticId);
            Assert.True(p.PlayPathExempt);
            Assert.DoesNotContain(DataValidator.Validate(Kit.D), e => e.Contains("R-30"));
            var svc = new CosmeticService(Kit.D);
            Assert.Empty(svc.Validate());
            Assert.Empty(svc.ValidateRewards(new StoryFlow(Kit.D, Kit.Source, Kit.Clock()).LoadStep));

            var s = Game.Campaign.StartingKitService.CreateNewGame(Kit.D);
            var shop = new PurchaseService(Kit.D);
            Assert.True(shop.Apply(s, PlayerActionRunner.Purchase("cosmetic_armor_gold", "fake:1")));
            Assert.Contains("armor_gold", s.OwnedCosmetics);
            long gold = new Wallet(s.Wallet).Get(ResourceIds.Gold);
            Assert.True(shop.Apply(s, PlayerActionRunner.Purchase("cosmetic_armor_gold", "fake:2")));
            Assert.Equal(gold + svc.DuplicateGold, new Wallet(s.Wallet).Get(ResourceIds.Gold));
        }

        [Fact(DisplayName = "R-111 남의 군대 겉모습: 용병 부대 등록에 주인의 갑옷·말이 실려 고용한 쪽 전투 설정에, 영토 수비 스냅샷도 주인의 치장으로")]
        public void OthersAppearance()
        {
            var owner = Kit.Veteran();
            owner.Compositions.Add(new CorpsComposition { GeneralId = AvatarGeneral.GeneralId, Formation = "square", Role = "rear", Picks = { new TroopPick { UnitId = "cavalry", Troops = 6000 } } });
            owner.OwnedCosmetics.Add("armor_brown");
            owner.OwnedCosmetics.Add("horse_navy");
            var svc = new CosmeticService(Kit.D);
            Assert.True(svc.Equip(owner, "cavalry", CosmeticKinds.Armor, "armor_brown"));
            Assert.True(svc.Equip(owner, "cavalry", CosmeticKinds.Horse, "horse_navy"));
            var rules = new HireRules(Kit.D);
            var reg = rules.Register(owner, new HireListingRequest { Kind = HireKinds.Unit, UnitId = "cavalry", Price = 2, PayWith = ResourceIds.Gold }, 0);
            Assert.True(reg.Ok, reg.Reason);
            Assert.Equal(("armor_brown", "horse_navy"), (reg.Listing.Armor, reg.Listing.Horse));
            reg.Listing.Id = "L1"; reg.Listing.Version = 1; reg.Listing.OwnerAccountId = "owner";

            var hirer = Kit.Veteran(); // 고용한 쪽은 치장 없음
            var hired = rules.Resolve(new List<HireUse> { new HireUse { ListingId = "L1", Version = 1, Count = 500 } }, id => id == "L1" ? reg.Listing : null, "hirer", hirer, out var error);
            Assert.Null(error);
            var comps = hirer.Compositions.Select(FormationEditor.Copy).ToList();
            comps[0].Picks.Clear(); // R-159: 고용 부대는 그 군단의 병종을 대신한다
            comps[0].Picks.Add(new TroopPick { UnitId = HireRules.UnitIdOf("L1"), Troops = 500, Hired = true });
            var plan = new StoryFlow(Kit.D, Kit.Source, Kit.Clock()).Prepare(hirer, new SortieRequest { StepId = "step01", Seed = 1, Hired = hired, Compositions = comps });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var hiredUnit = plan.Setup.Corps.SelectMany(c => c.Units).First(u => StoryFlow.PoolIdOf(u.UnitId) == "hire.L1");
            Assert.Equal(("armor_brown", "horse_navy"), (hiredUnit.Armor, hiredUnit.Horse));
            Assert.All(plan.Setup.Corps.SelectMany(c => c.Units).Where(u => StoryFlow.PoolIdOf(u.UnitId) != "hire.L1"), u => Assert.Null(u.Armor)); // 내 부대는 내 치장 (없음)

            var defense = TerritoryDefenses.SnapshotGeneral(Kit.D, owner, AvatarGeneral.GeneralId, out var derr);
            Assert.Null(derr);
            Assert.All(defense.Corps.SelectMany(c => c.Units), du => Assert.Equal(("armor_brown", "horse_navy"), (du.Armor, du.Horse)));
        }
    }
}
