using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Economy;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-108 치장 (cosmetics.md): 갑옷 한 벌 · 말 색, 엽전으로 사기, 계열에 입히기, 전투 설정에 겉모습만 (결과 · 해시 같음).</summary>
    public class CosmeticTests
    {
        private static CosmeticService Svc() => new CosmeticService(Kit.D);

        [Fact(DisplayName = "R-108 데이터: cosmetics.json 검사 통과, 첫 목록(갈색 갑옷 · 진남색 말, 엽전), 계열 = 1단계 병종 (승급해도 같음, 공병은 따로, 투석기 · 배는 없음)")]
        public void DataAndLines()
        {
            var svc = Svc();
            Assert.Empty(svc.Validate());
            var goldShop = svc.Items.Where(i => i.PriceGold != null).Select(i => i.Id);
            Assert.Equal(new[] { "armor_brown", "horse_navy", "armor_coin_white", "armor_coin_black", "armor_coin_gold", "armor_coin_red" }, goldShop); // 첫 목록 (엽전) + 상품 세트 샘플 (R-150, 2026-11-25). 보상 · 결제 치장은 R-111
            Assert.Equal("infantry", CosmeticService.LineOf(Kit.D, "guard"));
            Assert.Equal("spearman", CosmeticService.LineOf(Kit.D, "pikeWall"));
            Assert.Equal("cavalry", CosmeticService.LineOf(Kit.D, "ironCavalry"));
            Assert.Equal("engineer", CosmeticService.LineOf(Kit.D, "engineer"));
            Assert.Null(CosmeticService.LineOf(Kit.D, "catapult"));
            Assert.Null(CosmeticService.LineOf(Kit.D, "towerShip"));
        }

        [Fact(DisplayName = "R-108 사기 · 입히기: 엽전이 모자라면 못 삼, 사면 엽전이 줄고 다시 못 삼. 가진 것만 · 입을 수 있는 계열에만(말은 기병만), 벗기면 기본")]
        public void BuyAndEquip()
        {
            var svc = Svc();
            var s = StartingKitService.CreateNewGame(Kit.D);
            var w = new Wallet(s.Wallet);
            w.TrySpend(ResourceIds.Gold, w.Get(ResourceIds.Gold));
            Assert.False(svc.Buy(s, "armor_brown"));
            Assert.False(svc.Equip(s, "infantry", CosmeticKinds.Armor, "armor_brown")); // 안 가짐
            w.Add(ResourceIds.Gold, 1200);
            Assert.True(svc.Buy(s, "armor_brown"));
            Assert.Equal(700, w.Get(ResourceIds.Gold));
            Assert.False(svc.Buy(s, "armor_brown"));                                   // 이미 가짐
            Assert.True(svc.Equip(s, "infantry", CosmeticKinds.Armor, "armor_brown"));
            Assert.True(svc.Equip(s, "spearman", CosmeticKinds.Armor, "armor_brown")); // 하나 사면 모든 계열에
            Assert.False(svc.Equip(s, "infantry", CosmeticKinds.Armor, "armor_brown")); // 이미 입음
            Assert.False(svc.Equip(s, "infantry", CosmeticKinds.Horse, "armor_brown")); // 종류가 다름
            Assert.True(svc.Buy(s, "horse_navy"));
            Assert.False(svc.Equip(s, "infantry", CosmeticKinds.Horse, "horse_navy"));  // 말은 기병만
            Assert.True(svc.Equip(s, "cavalry", CosmeticKinds.Horse, "horse_navy"));
            Assert.Equal("armor_brown", CosmeticService.Worn(s, "infantry", CosmeticKinds.Armor));
            Assert.True(svc.Equip(s, "infantry", CosmeticKinds.Armor, null));
            Assert.Null(CosmeticService.Worn(s, "infantry", CosmeticKinds.Armor));
            Assert.False(svc.Equip(s, "infantry", CosmeticKinds.Armor, null));          // 벗을 것 없음
        }

        [Fact(DisplayName = "R-108 가진 세트 목록 (2026-11-25): 산 세트는 모두 가진 목록에 (데이터 차례, 입을 수 있는 모든 계열), 언제든 바꿔 입기 · 벗기, 안 가진 것은 더 얻기 목록")]
        public void OwnedList()
        {
            var svc = Svc();
            var s = StartingKitService.CreateNewGame(Kit.D);
            new Wallet(s.Wallet).Add(ResourceIds.Gold, 100000);
            Assert.Empty(svc.OwnedFor(s, "infantry", CosmeticKinds.Armor));
            Assert.Contains(svc.NotOwnedFor(s, "infantry", CosmeticKinds.Armor), d => d.Id == "armor_coin_white");
            Assert.True(svc.Buy(s, "armor_coin_red"));
            Assert.True(svc.Buy(s, "armor_coin_white"));
            Assert.Equal(new[] { "armor_coin_white", "armor_coin_red" }, svc.OwnedFor(s, "infantry", CosmeticKinds.Armor).Select(d => d.Id)); // 데이터 차례
            Assert.Equal(new[] { "armor_coin_white", "armor_coin_red" }, svc.OwnedFor(s, "archer", CosmeticKinds.Armor).Select(d => d.Id));   // 다른 계열에도
            Assert.DoesNotContain(svc.NotOwnedFor(s, "infantry", CosmeticKinds.Armor), d => d.Id == "armor_coin_white");
            Assert.True(svc.Equip(s, "infantry", CosmeticKinds.Armor, "armor_coin_white"));
            Assert.True(svc.Equip(s, "infantry", CosmeticKinds.Armor, "armor_coin_red"));   // 바꿔 입기
            Assert.Equal("armor_coin_red", CosmeticService.Worn(s, "infantry", CosmeticKinds.Armor));
            Assert.Null(CosmeticService.Worn(s, "archer", CosmeticKinds.Armor));             // 계열마다 따로
            Assert.True(svc.Equip(s, "infantry", CosmeticKinds.Armor, null));                // 기본으로
            Assert.Equal(2, svc.OwnedFor(s, "infantry", CosmeticKinds.Armor).Count);          // 벗어도 가진 것은 그대로
        }

        [Fact(DisplayName = "R-108 행동: cosmetic.buy · cosmetic.equip을 기기 · 서버가 같은 처리기로 적용 (세이브에 남음)")]
        public void Actions()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            new Wallet(s.Wallet).Add(ResourceIds.Gold, 1000);
            Assert.True(runner.Apply(s, PlayerActionRunner.BuyCosmetic("horse_navy")));
            Assert.True(runner.Apply(s, PlayerActionRunner.EquipCosmetic("cavalry", CosmeticKinds.Horse, "horse_navy")));
            Assert.False(runner.Apply(s, PlayerActionRunner.EquipCosmetic("archer", CosmeticKinds.Horse, "horse_navy")));
            var back = SaveJson.Read(SaveJson.Write(s));
            Assert.Contains("horse_navy", back.OwnedCosmetics);
            Assert.Equal("horse_navy", back.EquippedHorse["cavalry"]);
        }

        [Fact(DisplayName = "R-108 전투: 입은 갑옷이 출진 설정의 소부대에 실리지만, 같은 시드면 입은 전투와 안 입은 전투의 결과 · 상태 해시가 같다")]
        public void BattleLooksOnly()
        {
            var plain = Kit.Veteran();
            var dressed = Kit.Veteran();
            foreach (var line in CosmeticService.PersonLines) dressed.EquippedArmor[line] = "armor_brown";
            dressed.OwnedCosmetics.Add("armor_brown");
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var a = flow.Prepare(plain, new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            var b = flow.Prepare(dressed, new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(a.Ok && b.Ok);
            var mine = b.Setup.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).SelectMany(c => c.Units).ToList();
            Assert.NotEmpty(mine);
            Assert.All(mine.Where(u => CosmeticService.LineOf(Kit.D, u.TroopType) != null), u => Assert.Equal("armor_brown", u.Armor));
            Assert.All(a.Setup.Corps.SelectMany(c => c.Units), u => Assert.Null(u.Armor));
            var sa = new BattleSimulation(a.Setup);
            var sb = new BattleSimulation(b.Setup);
            while (!sa.IsOver) sa.Step();
            while (!sb.IsOver) sb.Step();
            Assert.Equal(sa.Result.StateHash, sb.Result.StateHash);
            Assert.Equal(sa.Result.Winner, sb.Result.Winner);
        }
    }
}
