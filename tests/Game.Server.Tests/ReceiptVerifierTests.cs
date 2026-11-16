using System.Collections.Generic;
using Game.Flow;
using Game.Server;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>영수증 검증기 설정 (R-114, deploy-cloudrun.md 7장).</summary>
    public class ReceiptVerifierTests
    {
        private static IConfiguration Config(params (string k, string v)[] kv)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in kv) d[k] = v;
            return new ConfigurationBuilder().AddInMemoryCollection(d).Build();
        }

        [Fact(DisplayName = "R-114 설정: 기본 fake, AllowFakeReceipts=false면 off (지금 배포와 같음), googlePlay는 자리라 모두 거절, 모르는 값은 오류")]
        public void FromConfig()
        {
            Assert.IsType<FakeReceiptVerifier>(ReceiptVerifiers.FromConfig(Config()));
            Assert.IsType<RejectAllReceiptVerifier>(ReceiptVerifiers.FromConfig(Config(("Game:AllowFakeReceipts", "false"))));
            Assert.IsType<FakeReceiptVerifier>(ReceiptVerifiers.FromConfig(Config(("Game:AllowFakeReceipts", "false"), ("Game:ReceiptVerifier", "fake"))));
            var gp = Assert.IsType<GooglePlayReceiptVerifier>(ReceiptVerifiers.FromConfig(Config(("Game:ReceiptVerifier", "googlePlay"), ("Game:GooglePlay:PackageName", "net.example.game"))));
            Assert.False(gp.Configured); // 서비스 계정 키 없음
            Assert.Equal(ReceiptReasons.NotConfigured, gp.Verify("gold_pack_small", "token").Reason);
            Assert.Throws<System.InvalidOperationException>(() => ReceiptVerifiers.FromConfig(Config(("Game:ReceiptVerifier", "apple"))));
        }
    }
}
