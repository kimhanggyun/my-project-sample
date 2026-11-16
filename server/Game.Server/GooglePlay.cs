using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Game.Flow;
using Game.Foundation.Time;
using Newtonsoft.Json.Linq;

namespace Game.Server
{
    /// <summary>Google Play 상품 구매 하나 (purchases.products.get 응답 중 쓰는 칸).</summary>
    public sealed class GooglePlayPurchase
    {
        /// <summary>0 구매됨 · 1 취소 · 2 대기.</summary>
        public int PurchaseState { get; set; }
        /// <summary>0 아직 소모 안 함 · 1 소모함.</summary>
        public int ConsumptionState { get; set; }
        /// <summary>0 확인 안 함 · 1 확인함.</summary>
        public int AcknowledgementState { get; set; }
        public string OrderId { get; set; }
        /// <summary>0 테스트(라이선스 테스터) · 1 프로모션 · 2 보상형. 일반 결제면 없음(null).</summary>
        public int? PurchaseType { get; set; }

        public static GooglePlayPurchase FromJson(string json)
        {
            var o = JObject.Parse(json);
            return new GooglePlayPurchase
            {
                PurchaseState = o.Value<int?>("purchaseState") ?? 0,
                ConsumptionState = o.Value<int?>("consumptionState") ?? 0,
                AcknowledgementState = o.Value<int?>("acknowledgementState") ?? 0,
                OrderId = o.Value<string>("orderId"),
                PurchaseType = o.Value<int?>("purchaseType"),
            };
        }
    }

    /// <summary>Google Play Developer API (Android Publisher v3) 중 쓰는 것. 테스트는 가짜 구현으로.</summary>
    public interface IGooglePlayApi
    {
        /// <summary>(HTTP 상태, 구매). 상태가 200이 아니면 구매는 null. 닿지 않으면 상태 0.</summary>
        (int status, GooglePlayPurchase purchase) GetProduct(string packageName, string productId, string token);
        bool Acknowledge(string packageName, string productId, string token);
        bool Consume(string packageName, string productId, string token);
    }

    /// <summary>
    /// Google Play 영수증 검증 (R-114 자리 → R-116 구현). 설정 Game:GooglePlay:PackageName · ServiceAccountJson이 없으면 notConfigured로 거절.
    /// 기기가 보낸 영수증 = 구매 토큰 (또는 Unity IAP 영수증 JSON — 안의 purchaseToken을 꺼낸다). Play 상품 id = products.json의 id.
    /// 통과: purchaseState 0이고 아직 소모하지 않음 (소모함 = 이미 지급 → used). 테스트 구매(purchaseType 0)는 AllowTestPurchases일 때만.
    /// TransactionId = orderId (없으면 "gp:" + 토큰). 반영 뒤 <see cref="Settle"/>: 영구 상품은 acknowledge, 소모성은 consume.
    /// </summary>
    public sealed class GooglePlayReceiptVerifier : IReceiptVerifier, IReceiptSettler
    {
        public string PackageName { get; }
        public bool Configured => _api != null && !string.IsNullOrWhiteSpace(PackageName);
        public bool AllowTestPurchases { get; }
        private readonly IGooglePlayApi _api;

        public GooglePlayReceiptVerifier(string packageName, IGooglePlayApi api, bool allowTestPurchases = true)
        {
            PackageName = packageName;
            _api = api;
            AllowTestPurchases = allowTestPurchases;
        }

        public ReceiptVerdict Verify(string productId, string receipt)
        {
            string token = TokenOf(receipt);
            if (string.IsNullOrEmpty(token)) return ReceiptVerdict.No(ReceiptReasons.Empty);
            if (!Configured) return ReceiptVerdict.No(ReceiptReasons.NotConfigured);
            if (string.IsNullOrEmpty(productId)) return ReceiptVerdict.No(ReceiptReasons.Rejected);
            (int status, GooglePlayPurchase p) r;
            try { r = _api.GetProduct(PackageName, productId, token); }
            catch (Exception) { return ReceiptVerdict.No(ReceiptReasons.Unavailable); }
            if (r.status == 400 || r.status == 404 || r.status == 410) return ReceiptVerdict.No(ReceiptReasons.Rejected); // 없는 토큰 · 다른 상품
            if (r.status != 200 || r.p == null) return ReceiptVerdict.No(ReceiptReasons.Unavailable);
            var p = r.p;
            if (p.PurchaseState == 2) return ReceiptVerdict.No(ReceiptReasons.Pending);
            if (p.PurchaseState != 0) return ReceiptVerdict.No(ReceiptReasons.Rejected);
            if (p.ConsumptionState == 1) return ReceiptVerdict.No(ReceiptReasons.Used);
            if (p.PurchaseType == 0 && !AllowTestPurchases) return ReceiptVerdict.No(ReceiptReasons.NotAccepted);
            return ReceiptVerdict.Ok(string.IsNullOrEmpty(p.OrderId) ? "gp:" + token : p.OrderId);
        }

        public bool Settle(string productId, string receipt, bool consumable)
        {
            string token = TokenOf(receipt);
            if (!Configured || string.IsNullOrEmpty(token)) return false;
            try { return consumable ? _api.Consume(PackageName, productId, token) : _api.Acknowledge(PackageName, productId, token); }
            catch (Exception) { return false; }
        }

        /// <summary>영수증에서 구매 토큰: 그대로의 토큰, {"purchaseToken"}, Unity IAP {"Payload": "{\"json\": \"{...purchaseToken...}\"}"} 모두.</summary>
        public static string TokenOf(string receipt)
        {
            if (string.IsNullOrWhiteSpace(receipt)) return null;
            string r = receipt.Trim();
            if (!r.StartsWith("{")) return r;
            try
            {
                var o = JObject.Parse(r);
                for (int depth = 0; depth < 4 && o != null; depth++)
                {
                    var t = o.Value<string>("purchaseToken");
                    if (!string.IsNullOrEmpty(t)) return t;
                    string inner = o.Value<string>("Payload") ?? o.Value<string>("json");
                    o = inner != null && inner.TrimStart().StartsWith("{") ? JObject.Parse(inner) : null;
                }
            }
            catch (Newtonsoft.Json.JsonException) { }
            return null;
        }
    }

    /// <summary>서비스 계정 키 (JSON: client_email · private_key · token_uri). 값은 비밀 — 저장소 · 기록에 남기지 않는다.</summary>
    public sealed class ServiceAccountCredential
    {
        public const string Scope = "https://www.googleapis.com/auth/androidpublisher";
        public string ClientEmail { get; private set; }
        public string TokenUri { get; private set; }
        private RSA _key;

        public static ServiceAccountCredential Parse(string json)
        {
            var o = JObject.Parse(json);
            var c = new ServiceAccountCredential
            {
                ClientEmail = o.Value<string>("client_email"),
                TokenUri = o.Value<string>("token_uri") ?? "https://oauth2.googleapis.com/token",
            };
            string pem = o.Value<string>("private_key");
            if (string.IsNullOrEmpty(c.ClientEmail) || string.IsNullOrEmpty(pem)) throw new InvalidOperationException("서비스 계정 키에 client_email · private_key가 없습니다.");
            c._key = RSA.Create();
            c._key.ImportFromPem(pem);
            return c;
        }

        public static ServiceAccountCredential FromKey(string email, RSA key, string tokenUri = "https://oauth2.googleapis.com/token") =>
            new ServiceAccountCredential { ClientEmail = email, TokenUri = tokenUri, _key = key };

        /// <summary>토큰 요청용 JWT (RS256, 1시간).</summary>
        public string Assertion(long nowUnixSeconds)
        {
            string header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            var claims = new JObject { ["iss"] = ClientEmail, ["scope"] = Scope, ["aud"] = TokenUri, ["iat"] = nowUnixSeconds, ["exp"] = nowUnixSeconds + 3600 };
            string body = B64(Encoding.UTF8.GetBytes(claims.ToString(Newtonsoft.Json.Formatting.None)));
            byte[] sig = _key.SignData(Encoding.ASCII.GetBytes(header + "." + body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return header + "." + body + "." + B64(sig);
        }

        public static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>실제 Android Publisher API (HTTPS). 접근 토큰은 서비스 계정 JWT로 받아 만료 1분 전까지 다시 쓴다.</summary>
    public sealed class GooglePlayHttpApi : IGooglePlayApi
    {
        private const string Base = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/";
        private readonly HttpClient _http;
        private readonly ServiceAccountCredential _cred;
        private readonly ITimeProvider _time;
        private readonly object _lock = new object();
        private string _token;
        private long _tokenUntil;

        public GooglePlayHttpApi(HttpClient http, ServiceAccountCredential cred, ITimeProvider time = null)
        {
            _http = http;
            _cred = cred;
            _time = time ?? new SystemTimeProvider();
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        private long Now => _time.UtcNow.ToUnixTimeSeconds();

        private string AccessToken()
        {
            lock (_lock)
            {
                if (_token != null && Now < _tokenUntil - 60) return _token;
                var form = new FormUrlEncodedContent(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                    new System.Collections.Generic.KeyValuePair<string, string>("assertion", _cred.Assertion(Now)),
                });
                var res = _http.PostAsync(_cred.TokenUri, form).GetAwaiter().GetResult();
                string body = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!res.IsSuccessStatusCode) throw new InvalidOperationException("구글 접근 토큰을 받지 못했습니다: " + (int)res.StatusCode);
                var o = JObject.Parse(body);
                _token = o.Value<string>("access_token");
                _tokenUntil = Now + (o.Value<long?>("expires_in") ?? 3600);
                return _token;
            }
        }

        private static string Path(string pkg, string productId, string token) =>
            Base + Uri.EscapeDataString(pkg) + "/purchases/products/" + Uri.EscapeDataString(productId) + "/tokens/" + Uri.EscapeDataString(token);

        private HttpResponseMessage Send(HttpMethod method, string url)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken());
            if (method == HttpMethod.Post) req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return _http.SendAsync(req).GetAwaiter().GetResult();
        }

        public (int status, GooglePlayPurchase purchase) GetProduct(string packageName, string productId, string token)
        {
            var res = Send(HttpMethod.Get, Path(packageName, productId, token));
            if (!res.IsSuccessStatusCode) return ((int)res.StatusCode, null);
            return (200, GooglePlayPurchase.FromJson(res.Content.ReadAsStringAsync().GetAwaiter().GetResult()));
        }

        public bool Acknowledge(string packageName, string productId, string token) =>
            Send(HttpMethod.Post, Path(packageName, productId, token) + ":acknowledge").IsSuccessStatusCode;

        public bool Consume(string packageName, string productId, string token) =>
            Send(HttpMethod.Post, Path(packageName, productId, token) + ":consume").IsSuccessStatusCode;
    }
}
