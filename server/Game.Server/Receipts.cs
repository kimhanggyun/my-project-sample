using System;
using Game.Flow;
using Microsoft.Extensions.Configuration;

namespace Game.Server
{
    /// <summary>
    /// 영수증 검증기 고르기 (R-114, deploy-cloudrun.md 7장). 설정 <c>Game:ReceiptVerifier</c>:
    /// fake(기본) = 개발용 "fake:" 영수증만 · off = 모두 거절 · googlePlay = Google Play Developer API (자리만, 아직 거절).
    /// 예전 설정 <c>Game:AllowFakeReceipts=false</c>는 ReceiptVerifier가 없을 때 off로 읽는다 (지금 배포 서버와 같음).
    /// </summary>
    public static class ReceiptVerifiers
    {
        public static IReceiptVerifier FromConfig(IConfiguration config)
        {
            string id = config["Game:ReceiptVerifier"];
            if (string.IsNullOrWhiteSpace(id))
                id = string.Equals(config["Game:AllowFakeReceipts"], "false", StringComparison.OrdinalIgnoreCase) ? ReceiptVerifierIds.Off : ReceiptVerifierIds.Fake;
            switch (id.Trim())
            {
                case ReceiptVerifierIds.Fake: return new FakeReceiptVerifier();
                case ReceiptVerifierIds.Off: return new RejectAllReceiptVerifier();
                case ReceiptVerifierIds.GooglePlay: return new GooglePlayReceiptVerifier(config["Game:GooglePlay:PackageName"], config["Game:GooglePlay:ServiceAccountJson"]);
                default: throw new InvalidOperationException($"Game:ReceiptVerifier '{id}'를 모릅니다 (fake · off · googlePlay).");
            }
        }
    }

    /// <summary>
    /// Google Play 영수증 검증 자리 (R-114). 실제 구현은 다음 단계 (deploy-cloudrun.md 7장):
    /// 기기가 보낸 영수증 = 구매 토큰 → Google Play Developer API <c>purchases.products.get(packageName, productId, token)</c>
    /// → purchaseState 0(구매됨) · consumptionState · acknowledgementState를 보고, orderId를 TransactionId로 (한 번만 쓰기).
    /// 서비스 계정 키는 비밀(Secret Manager → 환경 변수 Game__GooglePlay__ServiceAccountJson)로만, 저장소 · 채팅에 넣지 않는다.
    /// 지금은 설정이 있어도 모두 거절한다 (가짜로 통과시키지 않음).
    /// </summary>
    public sealed class GooglePlayReceiptVerifier : IReceiptVerifier
    {
        public string PackageName { get; }
        public bool Configured { get; }

        public GooglePlayReceiptVerifier(string packageName, string serviceAccountJson)
        {
            PackageName = packageName;
            Configured = !string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(serviceAccountJson);
        }

        public ReceiptVerdict Verify(string productId, string receipt)
        {
            if (string.IsNullOrEmpty(receipt)) return ReceiptVerdict.No(ReceiptReasons.Empty);
            // TODO(R-114 다음 단계): Configured면 purchases.products.get 호출. 그 전까지는 거절.
            return ReceiptVerdict.No(ReceiptReasons.NotConfigured);
        }
    }
}
