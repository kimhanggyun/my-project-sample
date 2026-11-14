using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Game.Server
{
    /// <summary>확인된 외부 로그인 (Firebase): 제공자(google.com, apple.com …)와 그 안의 사용자 id.</summary>
    public sealed class ExternalIdentity
    {
        public string Provider { get; set; }
        public string Subject { get; set; }
        /// <summary>확인된 이메일 (소문자, 토큰의 email_verified가 true일 때만). 없으면 null. 마스터 계정 확인에 쓴다 (R-86).</summary>
        public string Email { get; set; }
    }

    /// <summary>외부 로그인 토큰 확인 (docs/impl/deploy-cloudrun.md 3장). 틀리면 null.</summary>
    public interface IExternalTokenVerifier
    {
        ExternalIdentity Verify(string idToken, long nowUnixSeconds);
    }

    /// <summary>
    /// Firebase ID 토큰 확인: RS256 서명(구글 공개 인증서), iss = https://securetoken.google.com/&lt;프로젝트&gt;, aud = 프로젝트,
    /// exp가 지나지 않았고 iat·auth_time이 미래가 아니며(시계 차 5분 허용) sub가 있어야 한다. 외부 패키지 없이 .NET 암호화만 쓴다.
    /// </summary>
    public sealed class FirebaseTokenVerifier : IExternalTokenVerifier
    {
        public const string CertsUrl = "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";
        private const long SkewSeconds = 300;

        private readonly string _projectId;
        private readonly Func<IReadOnlyDictionary<string, X509Certificate2>> _keys;

        /// <param name="keys">kid → 인증서. 기본은 구글에서 받아 캐시 (<see cref="GoogleCerts"/>).</param>
        public FirebaseTokenVerifier(string projectId, Func<IReadOnlyDictionary<string, X509Certificate2>> keys)
        {
            _projectId = projectId;
            _keys = keys;
        }

        public ExternalIdentity Verify(string idToken, long now)
        {
            try
            {
                var parts = (idToken ?? "").Split('.');
                if (parts.Length != 3) return null;
                using var header = JsonDocument.Parse(B64(parts[0]));
                using var payload = JsonDocument.Parse(B64(parts[1]));
                var h = header.RootElement;
                if (Str(h, "alg") != "RS256") return null;
                string kid = Str(h, "kid");
                if (kid == null || !_keys().TryGetValue(kid, out var cert)) return null;
                using (var rsa = cert.GetRSAPublicKey())
                {
                    if (rsa == null || !rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), B64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        return null;
                }
                var p = payload.RootElement;
                if (Str(p, "iss") != "https://securetoken.google.com/" + _projectId || Str(p, "aud") != _projectId) return null;
                if (Num(p, "exp") is not long exp || exp + SkewSeconds < now) return null;
                if (Num(p, "iat") is not long iat || iat > now + SkewSeconds) return null;
                if (Num(p, "auth_time") is long at && at > now + SkewSeconds) return null;
                string sub = Str(p, "sub");
                if (string.IsNullOrEmpty(sub) || sub.Length > 128) return null;
                string provider = p.TryGetProperty("firebase", out var fb) ? Str(fb, "sign_in_provider") : null;
                string email = Str(p, "email");
                bool verified = p.TryGetProperty("email_verified", out var ev) && ev.ValueKind == JsonValueKind.True;
                return new ExternalIdentity { Provider = provider ?? "firebase", Subject = sub, Email = verified && !string.IsNullOrWhiteSpace(email) ? email.Trim().ToLowerInvariant() : null };
            }
            catch (Exception) { return null; }
        }

        private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static long? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

        private static byte[] B64(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        /// <summary>구글 공개 인증서를 받아 캐시한다 (응답의 max-age 동안, 못 받으면 지난 것을 그대로).</summary>
        public static Func<IReadOnlyDictionary<string, X509Certificate2>> GoogleCerts(HttpClient http)
        {
            object gate = new object();
            IReadOnlyDictionary<string, X509Certificate2> cached = new Dictionary<string, X509Certificate2>();
            DateTimeOffset until = DateTimeOffset.MinValue;
            return () =>
            {
                lock (gate)
                {
                    if (DateTimeOffset.UtcNow < until) return cached;
                    try
                    {
                        using var res = http.GetAsync(CertsUrl).GetAwaiter().GetResult();
                        res.EnsureSuccessStatusCode();
                        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(res.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        var next = new Dictionary<string, X509Certificate2>();
                        foreach (var kv in map) next[kv.Key] = X509Certificate2.CreateFromPem(kv.Value);
                        cached = next;
                        var age = res.Headers.CacheControl?.MaxAge ?? TimeSpan.FromHours(1);
                        until = DateTimeOffset.UtcNow + age;
                    }
                    catch (Exception) { until = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1); }
                    return cached;
                }
            };
        }
    }
}
