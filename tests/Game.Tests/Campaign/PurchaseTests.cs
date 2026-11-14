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

        [Fact]
        public void ListedProductsHavePricesAndStrings()
        {
            var listed = new PurchaseService(Kit.D).Listed();
            Assert.NotEmpty(listed);
            foreach (var p in listed)
            {
                Assert.True(p.PriceKrw > 0, p.Id);
                Assert.True(Kit.D.Strings.ContainsKey("product." + p.Id), p.Id);
                Assert.True(p.Grants.Count > 0 || p.AdFreeDays.HasValue, p.Id);
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
