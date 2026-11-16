using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Game.Flow;
using Game.Online;
using Game.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>Google Play 영수증 실제 검증 (R-116, deploy-cloudrun.md 7장). HTTP는 가짜 API로.</summary>
    public class GooglePlayTests
    {
        private const string Pkg = "net.example.game";
        private static readonly Game.Data.Defs.DefinitionSet Defs = Game.Data.Defs.DefinitionSet.Load(new Game.Data.Json.FileDataSource(ServerPaths.DataRoot(null)), ServerSetup.DefaultGame);

        private sealed class FakeApi : IGooglePlayApi
        {
            public Dictionary<string, (int, GooglePlayPurchase)> Answers = new Dictionary<string, (int, GooglePlayPurchase)>();
            public List<string> Calls = new List<string>();
            public (int status, GooglePlayPurchase purchase) GetProduct(string packageName, string productId, string token)
            {
                Calls.Add("get " + productId + " " + token);
                return Answers.TryGetValue(productId + "/" + token, out var a) ? a : (404, null);
            }
            public bool Acknowledge(string packageName, string productId, string token) { Calls.Add("ack " + productId); return true; }
            public bool Consume(string packageName, string productId, string token) { Calls.Add("consume " + productId); return true; }
        }

        private static GooglePlayPurchase Bought(string order = "GPA.1", int state = 0, int consumed = 0, int? type = null) =>
            new GooglePlayPurchase { PurchaseState = state, ConsumptionState = consumed, OrderId = order, PurchaseType = type };

        [Fact(DisplayName = "R-116 구글 검증: 구매됨만 통과 (취소 · 대기 · 소모함 · 없는 토큰 · 다른 상품은 거절), 거래 id = orderId, 테스트 구매는 설정, 스토어 오류는 unavailable")]
        public void Verify()
        {
            var api = new FakeApi();
            api.Answers["gold_pack_small/t1"] = (200, Bought("GPA.1"));
            api.Answers["gold_pack_small/t2"] = (200, Bought(state: 1));
            api.Answers["gold_pack_small/t3"] = (200, Bought(state: 2));
            api.Answers["gold_pack_small/t4"] = (200, Bought(consumed: 1));
            api.Answers["gold_pack_small/t5"] = (200, Bought(order: null, type: 0));
            api.Answers["gold_pack_small/t6"] = (503, null);
            var v = new GooglePlayReceiptVerifier(Pkg, api);
            Assert.True(v.Configured);

            var ok = v.Verify("gold_pack_small", "t1");
            Assert.Equal((true, "GPA.1"), (ok.Valid, ok.TransactionId));
            Assert.Equal(ReceiptReasons.Rejected, v.Verify("gold_pack_small", "t2").Reason);
            Assert.Equal(ReceiptReasons.Pending, v.Verify("gold_pack_small", "t3").Reason);
            Assert.Equal(ReceiptReasons.Used, v.Verify("gold_pack_small", "t4").Reason);
            Assert.Equal("gp:t5", v.Verify("gold_pack_small", "t5").TransactionId);            // 테스트 구매 (기본 허용), orderId 없음
            Assert.Equal(ReceiptReasons.Unavailable, v.Verify("gold_pack_small", "t6").Reason);
            Assert.Equal(ReceiptReasons.Rejected, v.Verify("gem_pack_small", "t1").Reason);   // 다른 상품의 토큰
            Assert.Equal(ReceiptReasons.Empty, v.Verify("gold_pack_small", " ").Reason);
            Assert.Equal(ReceiptReasons.NotAccepted, new GooglePlayReceiptVerifier(Pkg, api, allowTestPurchases: false).Verify("gold_pack_small", "t5").Reason);
            Assert.Equal(ReceiptReasons.NotConfigured, new GooglePlayReceiptVerifier(Pkg, null).Verify("gold_pack_small", "t1").Reason);
        }

        [Fact(DisplayName = "R-116 영수증 모양: 토큰 그대로 · {purchaseToken} · Unity IAP 영수증(Payload → json → purchaseToken)")]
        public void TokenOf()
        {
            Assert.Equal("abc", GooglePlayReceiptVerifier.TokenOf("abc"));
            Assert.Equal("abc", GooglePlayReceiptVerifier.TokenOf("{\"purchaseToken\":\"abc\"}"));
            var inner = new JObject { ["orderId"] = "GPA.9", ["purchaseToken"] = "tok" }.ToString();
            var payload = new JObject { ["json"] = inner, ["signature"] = "sig" }.ToString();
            var unity = new JObject { ["Store"] = "GooglePlay", ["TransactionID"] = "GPA.9", ["Payload"] = payload }.ToString();
            Assert.Equal("tok", GooglePlayReceiptVerifier.TokenOf(unity));
            Assert.Null(GooglePlayReceiptVerifier.TokenOf("{\"x\":1}"));
            Assert.Null(GooglePlayReceiptVerifier.TokenOf(""));
        }

        [Fact(DisplayName = "R-116 반영 뒤 스토어에 알림: 영구 상품은 acknowledge, 소모성은 consume")]
        public void Settle()
        {
            var api = new FakeApi();
            var v = new GooglePlayReceiptVerifier(Pkg, api);
            Assert.True(v.Settle("remove_banner_permanent", "t", consumable: false));
            Assert.True(v.Settle("gold_pack_small", "t", consumable: true));
            Assert.Equal(new[] { "ack remove_banner_permanent", "consume gold_pack_small" }, api.Calls);
            Assert.True(Defs.Products["remove_banner_permanent"].Permanent);
            Assert.True(Defs.Products["cosmetic_armor_gold"].Permanent);
            Assert.False(Defs.Products["remove_banner_30d"].Permanent);
            Assert.False(Defs.Products["gold_pack_small"].Permanent);
        }

        [Fact(DisplayName = "R-116 서비스 계정 JWT: RS256 서명, iss = 서비스 계정, scope = androidpublisher, 1시간")]
        public void Assertion()
        {
            using var rsa = RSA.Create(2048);
            var cred = ServiceAccountCredential.FromKey("svc@example.iam.gserviceaccount.com", rsa);
            string jwt = cred.Assertion(1_800_000_000);
            var parts = jwt.Split('.');
            Assert.Equal(3, parts.Length);
            byte[] Dec(string s) { s = s.Replace('-', '+').Replace('_', '/'); return System.Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')); }
            var claims = JObject.Parse(Encoding.UTF8.GetString(Dec(parts[1])));
            Assert.Equal("svc@example.iam.gserviceaccount.com", (string)claims["iss"]);
            Assert.Equal(ServiceAccountCredential.Scope, (string)claims["scope"]);
            Assert.Equal(1_800_003_600L, (long)claims["exp"]);
            Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Dec(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            var json = new JObject { ["client_email"] = "svc@x", ["private_key"] = rsa.ExportPkcs8PrivateKeyPem() }.ToString();
            Assert.Equal("svc@x", ServiceAccountCredential.Parse(json).ClientEmail);
        }

        [Fact(DisplayName = "R-116 쓴 거래는 저장소에: 통과한 결제는 Pending에만 모았다가 서버가 세이브 저장 뒤 기록, 기록된 거래는 서버를 다시 띄워도 used")]
        public void UsedReceiptsPersist()
        {
            var store = new InMemoryStore();
            var check = new ReceiptCheck(new FakeReceiptVerifier(), tx => store.ReceiptOf("g", tx) != null);
            Assert.True(check.Check(PlayerActionRunner.Purchase("gold_pack_small", "fake:1")));
            Assert.False(check.Check(PlayerActionRunner.Purchase("gold_pack_small", "fake:1"))); // 같은 묶음 두 번
            Assert.Single(check.Pending);
            Assert.Null(store.ReceiptOf("g", "fake:1")); // 아직 기록 전 (행동이 거절되면 타지 않음)
            foreach (var r in check.Pending) r.AccountId = "a";
            store.RecordReceipts("g", check.Pending);
            var again = new ReceiptCheck(new FakeReceiptVerifier(), tx => store.ReceiptOf("g", tx) != null);
            Assert.False(again.Check(PlayerActionRunner.Purchase("gold_pack_small", "fake:1")));
            Assert.Equal(ReceiptReasons.Used, again.Last.Reason);
            Assert.Single(store.ReceiptsOf("g", "a"));
        }
    }
}
