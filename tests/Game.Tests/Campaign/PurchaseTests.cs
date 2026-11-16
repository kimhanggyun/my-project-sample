using Game.Campaign;
using Game.Flow;
using Game.Online;
using Game.Services;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-64 결제 상품 받기 (영수증 검사는 서버), 로그인 방식별 서비스 (구글 개발용 가짜, 애플 막음).</summary>
    public class PurchaseTests
    {
        private const long Now = 1_800_000_000;

        private static PlayerAction Buy(string id, string receipt = "fake:x") { var a = PlayerActionRunner.Purchase(id, receipt); a.DoneAtUnixSeconds = Now; return a; }

        [Fact]
        public void GrantsResourcesAndAdFree()
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            var runner = new PlayerActionRunner(Kit.D);
            long gold = s.Wallet["gold"];
            Assert.True(runner.Apply(s, Buy("gold_pack_small")));
            Assert.Equal(gold + 1000, s.Wallet["gold"]);

            Assert.False(PurchaseService.AdFree(s, Now));
            Assert.True(runner.Apply(s, Buy("remove_banner_30d")));
            Assert.Equal(Now + 30 * 86400, s.AdFreeUntilUnixSeconds);
            Assert.True(runner.Apply(s, Buy("remove_banner_30d"))); // 이어 붙인다
            Assert.Equal(Now + 60 * 86400, s.AdFreeUntilUnixSeconds);
            Assert.False(PurchaseService.AdFree(s, Now + 61 * 86400));
            Assert.True(runner.Apply(s, Buy("remove_banner_permanent")));
            Assert.True(PurchaseService.AdFree(s, Now + 1000 * 86400));

            Assert.False(runner.Apply(s, Buy("skill_book")));       // 가격 없음 = 상점 상품 아님
            Assert.False(runner.Apply(s, Buy("noSuch")));
            Assert.False(runner.Apply(s, Buy("gold_pack_small", ""))); // 영수증 없음
        }

        [Fact]
        public void ReceiptCheckOnServer()
        {
            var check = new ReceiptCheck(allowFake: true);
            Assert.True(check.Check(PlayerActionRunner.CreateUnit("catapult"))); // 결제가 아니면 통과
            Assert.True(check.Check(Buy("gold_pack_small", "fake:a")));
            Assert.False(check.Check(Buy("gold_pack_small", "fake:a")));        // 같은 영수증 두 번
            Assert.False(check.Check(Buy("gold_pack_small", "store:real")));    // 실제 검증 전에는 받지 않음
            Assert.False(new ReceiptCheck(allowFake: false).Check(Buy("gold_pack_small", "fake:b")));
        }

        private sealed class StoreLike : IReceiptVerifier
        {
            // 스토어처럼: "ok:<주문>"만 진짜, 같은 주문이면 영수증 문자열이 달라도 같은 거래
            public ReceiptVerdict Verify(string productId, string receipt) =>
                receipt.StartsWith("ok:") ? ReceiptVerdict.Ok(receipt.Split('#')[0]) : ReceiptVerdict.No(ReceiptReasons.Rejected);
        }

        [Fact(DisplayName = "R-114 영수증 검증기: 인터페이스로 바꿔 끼움 (가짜 · 모두 거절 · 스토어), 한 번만 쓰기는 거래 id로, 까닭은 Last")]
        public void ReceiptVerifiers()
        {
            var fake = new ReceiptCheck(new FakeReceiptVerifier());
            Assert.True(fake.Check(Buy("gold_pack_small", "fake:1")));
            Assert.False(fake.Check(Buy("gold_pack_small", "fake:1")));
            Assert.Equal(ReceiptReasons.Used, fake.Last.Reason);
            Assert.False(fake.Check(Buy("gold_pack_small", "store:x")));
            Assert.Equal(ReceiptReasons.NotAccepted, fake.Last.Reason);
            Assert.False(fake.Check(Buy("gold_pack_small", "")));
            Assert.Equal(ReceiptReasons.Empty, fake.Last.Reason);

            Assert.False(new ReceiptCheck(new RejectAllReceiptVerifier()).Check(Buy("gold_pack_small", "fake:2")));

            var store = new ReceiptCheck(new StoreLike());
            Assert.True(store.Check(Buy("gold_pack_small", "ok:order1#a")));
            Assert.False(store.Check(Buy("gold_pack_small", "ok:order1#b"))); // 같은 거래
            Assert.True(store.Check(Buy("gold_pack_small", "ok:order2")));
            Assert.False(store.Check(Buy("gold_pack_small", "bad")));
            Assert.Equal(ReceiptReasons.Rejected, store.Last.Reason);
        }

[Fact(DisplayName = "R-117 복원 행동: 영구 상품만 (광고 영구 제거 · 치장), 없으면 반영 · 있으면 그대로 true, 치장은 중복 엽전 없음, 소모성 · 기간제는 false")]
        public void RestoreAction()
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            var runner = new PlayerActionRunner(Kit.D);
            long gold = s.Wallet["gold"];
            Assert.True(runner.Apply(s, PlayerActionRunner.RestorePurchase("remove_banner_permanent", "r1")));
            Assert.True(s.AdFreeForever);
            Assert.True(runner.Apply(s, PlayerActionRunner.RestorePurchase("remove_banner_permanent", "r1"))); // 이미 있음 = 그대로
            Assert.True(runner.Apply(s, PlayerActionRunner.RestorePurchase("cosmetic_armor_gold", "r2")));
            Assert.Contains("armor_gold", s.OwnedCosmetics);
            Assert.True(runner.Apply(s, PlayerActionRunner.RestorePurchase("cosmetic_armor_gold", "r2")));
            Assert.Equal(gold, s.Wallet["gold"]); // 중복 엽전 없음
            Assert.False(runner.Apply(s, PlayerActionRunner.RestorePurchase("gold_pack_small", "r3")));
            Assert.False(runner.Apply(s, PlayerActionRunner.RestorePurchase("remove_banner_30d", "r4")));
            Assert.False(runner.Apply(s, PlayerActionRunner.RestorePurchase("remove_banner_permanent", "")));
            Assert.Equal(new[] { "cosmetic_armor_gold", "remove_banner_permanent" }, new PurchaseService(Kit.D).Restorable().ConvertAll(p => p.Id));
        }

        [Fact(DisplayName = "R-117 복원 검사: 다시 확인해 진짜면 통과, 이 계정에 반영된 거래는 그대로 · 다른 계정이면 otherAccount, 처음 보는 거래는 기록할 것에")]
        public void RestoreCheck()
        {
            var owners = new System.Collections.Generic.Dictionary<string, string> { ["fake:mine"] = "me", ["fake:theirs"] = "other" };
            var check = new ReceiptCheck(new FakeReceiptVerifier(), tx => owners.ContainsKey(tx)) { OwnerOf = tx => owners.TryGetValue(tx, out var o) ? o : null, AccountId = "me" };
            Assert.True(check.Check(PlayerActionRunner.RestorePurchase("remove_banner_permanent", "fake:mine")));
            Assert.Empty(check.Pending);
            Assert.False(check.Check(PlayerActionRunner.RestorePurchase("remove_banner_permanent", "fake:theirs")));
            Assert.Equal(ReceiptReasons.OtherAccount, check.Last.Reason);
            Assert.True(check.Check(PlayerActionRunner.RestorePurchase("remove_banner_permanent", "fake:new")));
            Assert.Single(check.Pending);
            Assert.False(check.Check(PlayerActionRunner.RestorePurchase("remove_banner_permanent", "store:x")));
            Assert.False(check.Check(PlayerActionRunner.Purchase("remove_banner_permanent", "fake:mine"))); // 산 거래로 다시 사기는 used
        }

                [Fact]
        public void ListedProductsHavePricesAndStrings()
        {
            var listed = new PurchaseService(Kit.D).Listed();
            Assert.NotEmpty(listed);
            foreach (var p in listed)
            {
                Assert.True(p.PriceKrw > 0, p.Id);
                Assert.True(Kit.D.Strings.ContainsKey("product." + p.Id), p.Id);
                Assert.True(p.Grants.Count > 0 || p.AdFreeDays.HasValue || p.CosmeticId != null, p.Id); // 치장 상품은 치장을 준다 (R-111)
            }
        }

        [Fact]
        public void SignInProviders()
        {
            var reg = ServiceRegistry.WithBuiltIns();
            var dev = new ServiceConfig(new System.Collections.Generic.Dictionary<string, string> { ["auth.google"] = "fake", ["auth.apple"] = "off" });
            bool ok = false;
            var google = reg.Create<IAuthService>(ServiceIds.AuthFor(ServiceIds.SignInGoogle), dev);
            google.SignInAutomatically(r => ok = r);
            Assert.True(ok);
            Assert.Equal("google-fake-local", google.AccountId);
            Assert.False(google.IsGuest);
            var apple = reg.Create<IAuthService>(ServiceIds.AuthFor(ServiceIds.SignInApple), dev);
            apple.SignInAutomatically(r => ok = r);
            Assert.False(ok);
            Assert.Equal(ServiceIds.Auth, ServiceIds.AuthFor("guest"));
        }
    }
}
