using System;
using Game.Flow;
using Microsoft.Extensions.Configuration;

namespace Game.Server
{
    /// <summary>
    /// 영수증 검증기 고르기 (R-114, deploy-cloudrun.md 7장). 설정 <c>Game:ReceiptVerifier</c>:
    /// fake(기본) = 개발용 "fake:" 영수증만 · off = 모두 거절 · googlePlay = Google Play Developer API (R-116, GooglePlay.cs).
    /// 예전 설정 <c>Game:AllowFakeReceipts=false</c>는 ReceiptVerifier가 없을 때 off로 읽는다 (지금 배포 서버와 같음).
    /// </summary>
    public static class ReceiptVerifiers
    {
        /// <summary>설정의 검증기 이름 (없으면 예전 AllowFakeReceipts로).</summary>
        public static string IdOf(IConfiguration config)
        {
            string id = config["Game:ReceiptVerifier"];
            if (string.IsNullOrWhiteSpace(id))
                id = string.Equals(config["Game:AllowFakeReceipts"], "false", StringComparison.OrdinalIgnoreCase) ? ReceiptVerifierIds.Off : ReceiptVerifierIds.Fake;
            return id.Trim();
        }

        public static IReceiptVerifier FromConfig(IConfiguration config, Game.Foundation.Time.ITimeProvider time = null)
        {
            string id = IdOf(config);
            switch (id)
            {
                case ReceiptVerifierIds.Fake: return new FakeReceiptVerifier();
                case ReceiptVerifierIds.Off: return new RejectAllReceiptVerifier();
                case ReceiptVerifierIds.GooglePlay:
                    {
                        string pkg = config["Game:GooglePlay:PackageName"], key = config["Game:GooglePlay:ServiceAccountJson"];
                        bool allowTest = !string.Equals(config["Game:GooglePlay:AllowTestPurchases"], "false", StringComparison.OrdinalIgnoreCase);
                        var api = string.IsNullOrWhiteSpace(pkg) || string.IsNullOrWhiteSpace(key) ? null : new GooglePlayHttpApi(new System.Net.Http.HttpClient(), ServiceAccountCredential.Parse(key), time);
                        return new GooglePlayReceiptVerifier(pkg, api, allowTest);
                    }
                default: throw new InvalidOperationException($"Game:ReceiptVerifier '{id}'를 모릅니다 (fake · off · googlePlay).");
            }
        }
    }
}
