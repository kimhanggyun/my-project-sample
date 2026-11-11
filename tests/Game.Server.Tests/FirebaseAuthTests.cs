using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Game.Foundation.Time;
using Game.Online;
using Game.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>Firebase 로그인 (docs/impl/deploy-cloudrun.md 3장): ID 토큰 확인, 같은 사용자는 같은 계정.</summary>
    public class FirebaseAuthTests
    {
        private const string Project = "test-project";
        private const long Now = 1_800_000_000;
        private static readonly RSA Key = RSA.Create(2048);
        private static readonly X509Certificate2 Cert = new CertificateRequest("CN=test", Key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        private static readonly IReadOnlyDictionary<string, X509Certificate2> Keys = new Dictionary<string, X509Certificate2> { ["k1"] = Cert };

        private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string Token(string sub = "user1", string aud = Project, long exp = Now + 3600, string kid = "k1", RSA key = null, string provider = "google.com")
        {
            string header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"" + kid + "\",\"typ\":\"JWT\"}"));
            string payload = B64(Encoding.UTF8.GetBytes(
                "{\"iss\":\"https://securetoken.google.com/" + aud + "\",\"aud\":\"" + aud + "\",\"sub\":\"" + sub + "\",\"iat\":" + (Now - 10) +
                ",\"auth_time\":" + (Now - 10) + ",\"exp\":" + exp + ",\"firebase\":{\"sign_in_provider\":\"" + provider + "\"}}"));
            var sig = (key ?? Key).SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return header + "." + payload + "." + B64(sig);
        }

        private static FirebaseTokenVerifier Verifier() => new FirebaseTokenVerifier(Project, () => Keys);

        [Fact]
        public void VerifiesSignatureAndClaims()
        {
            var v = Verifier();
            var who = v.Verify(Token(), Now);
            Assert.NotNull(who);
            Assert.Equal(("google.com", "user1"), (who.Provider, who.Subject));

            Assert.Null(v.Verify(Token(aud: "other-project"), Now));           // 다른 프로젝트
            Assert.Null(v.Verify(Token(exp: Now - 1000), Now));                // 만료
            Assert.Null(v.Verify(Token(kid: "nope"), Now));                    // 모르는 키
            using var other = RSA.Create(2048);
            Assert.Null(v.Verify(Token(key: other), Now));                     // 다른 키로 서명
            var t = Token().Split('.');
            Assert.Null(v.Verify(t[0] + "." + B64(Encoding.UTF8.GetBytes("{\"sub\":\"x\"}")) + "." + t[2], Now)); // 내용 바꿈
            Assert.Null(v.Verify("garbage", Now));
            Assert.Null(v.Verify(null, Now));
        }

        public sealed class WithFirebase : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
            {
                builder.ConfigureServices(s =>
                {
                    s.RemoveAll<ITimeProvider>();
                    s.AddSingleton<ITimeProvider>(new ManualTimeProvider(DateTimeOffset.FromUnixTimeSeconds(Now)));
                    s.RemoveAll<IExternalTokenVerifier>();
                    s.AddSingleton<IExternalTokenVerifier>(Verifier());
                });
            }
        }

        private static StringContent Body(object o) => new StringContent(OnlineJson.Write(o), Encoding.UTF8, "application/json");

        [Fact]
        public async Task SignInEndpoint()
        {
            using var f = new WithFirebase();
            var c = f.CreateClient();
            var r1 = await c.PostAsync(OnlineRoutes.FirebaseSignIn, Body(new ExternalSignInRequest { IdToken = Token("userA") }));
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            var a1 = OnlineJson.Read<SignInResponse>(await r1.Content.ReadAsStringAsync());
            var r2 = await c.PostAsync(OnlineRoutes.FirebaseSignIn, Body(new ExternalSignInRequest { IdToken = Token("userA") }));
            var a2 = OnlineJson.Read<SignInResponse>(await r2.Content.ReadAsStringAsync());
            Assert.Equal(a1.AccountId, a2.AccountId);   // 같은 사용자 = 같은 계정 (기기를 바꿔도)
            Assert.NotEqual(a1.Token, a2.Token);
            var r3 = await c.PostAsync(OnlineRoutes.FirebaseSignIn, Body(new ExternalSignInRequest { IdToken = Token("userB") }));
            Assert.NotEqual(a1.AccountId, OnlineJson.Read<SignInResponse>(await r3.Content.ReadAsStringAsync()).AccountId);
            var bad = await c.PostAsync(OnlineRoutes.FirebaseSignIn, Body(new ExternalSignInRequest { IdToken = Token(aud: "x") }));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

            c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", a1.Token);
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(OnlineRoutes.Me)).StatusCode); // 받은 토큰으로 다른 API
        }

        [Fact]
        public async Task OffWithoutConfig()
        {
            using var f = new WebApplicationFactory<Program>();
            var r = await f.CreateClient().PostAsync(OnlineRoutes.FirebaseSignIn, Body(new ExternalSignInRequest { IdToken = Token() }));
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
    }
}
