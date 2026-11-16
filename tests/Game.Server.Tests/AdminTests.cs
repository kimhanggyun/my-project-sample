using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>관리자 페이지 (docs/impl/admin.md): 관리자 키 + 마스터 계정 둘 다 있어야, 키 설정이 없으면 꺼짐.</summary>
    public class AdminTests : IClassFixture<AdminTests.Factory>, IClassFixture<ServerTests.Factory>, IClassFixture<AdminTests.KeyOnlyFactory>
    {
        private const string Key = "test-admin-key";

        public sealed class Factory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseSetting("Game:AdminKey", Key);
                builder.ConfigureServices(s =>
                {
                    s.RemoveAll<ITimeProvider>();
                    s.AddSingleton<ITimeProvider>(new ManualTimeProvider(ServerTests.Clock));
                });
            }
        }

        /// <summary>개발 중 설정: Game:AdminGoogle=off → 키만으로.</summary>
        public sealed class KeyOnlyFactory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseSetting("Game:AdminKey", Key);
                builder.UseSetting("Game:AdminGoogle", "off");
            }
        }

        private readonly Factory _factory;
        private readonly ServerTests.Factory _noKey;
        private readonly KeyOnlyFactory _keyOnly;
        public AdminTests(Factory f, ServerTests.Factory noKey, KeyOnlyFactory keyOnly) { _factory = f; _noKey = noKey; _keyOnly = keyOnly; }

        [Fact(DisplayName = "관리자 (개발 중 AdminGoogle=off): 키만 맞으면 로그인 없이 상태 · 우편, 키가 틀리면 그대로 404")]
        public async Task KeyOnlyWhenGoogleOff()
        {
            var (player, them) = await Guest(_keyOnly);
            var admin = _keyOnly.CreateClient();
            admin.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, "wrong");
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(AdminRoutes.Prefix + "/status")).StatusCode);
            admin.DefaultRequestHeaders.Remove(AdminRoutes.KeyHeader);
            admin.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, Key);
            Assert.False((await Read<AdminKeyResponse>(await admin.PostAsync(AdminRoutes.Prefix + "/key", null))).Google);
            Assert.Equal(AdminRoutes.KeyOnlyAdmin, (await Read<AdminStatus>(await admin.GetAsync(AdminRoutes.Prefix + "/status"))).You);
            var sent = await Read<AdminMailResponse>(await admin.PostAsync(AdminRoutes.Prefix + "/games/" + ServerSetup.DefaultGame + "/mail",
                Body(new AdminMailRequest { AccountIds = new List<string> { them.AccountId }, Resource = "supply", Amount = 100 })));
            Assert.Equal((true, 1), (sent.Ok, sent.Sent));
            var box = await Read<MailListResponse>(await player.GetAsync(OnlineRoutes.Mail(ServerSetup.DefaultGame)));
            Assert.Single(box.Mail, m => m.Kind == MailKinds.AdminGift && m.Amount == 100);
        }

        private static StringContent Body(object o) => new StringContent(OnlineJson.Write(o), Encoding.UTF8, "application/json");
        private static async Task<T> Read<T>(HttpResponseMessage r) => OnlineJson.Read<T>(await r.Content.ReadAsStringAsync());

        private static async Task<(HttpClient c, SignInResponse who)> Guest(WebApplicationFactory<Program> f)
        {
            var c = f.CreateClient();
            var who = await Read<SignInResponse>(await c.PostAsync(OnlineRoutes.GuestSignIn, null));
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", who.Token);
            await c.GetAsync(OnlineRoutes.Save(ServerSetup.DefaultGame)); // 게임에 들어옴
            return (c, who);
        }

        [Fact(DisplayName = "관리자: 키 설정이 없으면 관리자 주소는 404, 페이지 파일은 열리지만 키가 들어 있지 않다")]
        public async Task OffWithoutKey()
        {
            var (c, who) = await Guest(_noKey);
            _noKey.Services.GetRequiredService<MasterAccounts>().Add(who.AccountId);
            c.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, "anything");
            Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync(AdminRoutes.Prefix + "/key", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(AdminRoutes.Prefix + "/status")).StatusCode);
            var page = await c.GetAsync("/unitygame1.html");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.DoesNotContain(Key, await page.Content.ReadAsStringAsync());
        }

        [Fact(DisplayName = "관리자: 키가 틀리면 404, 토큰 없으면 401, 마스터가 아니면 403, 둘 다 맞으면 상태 · 계정 · 세이브 · 영토")]
        public async Task KeyAndMasterRequired()
        {
            var (c, who) = await Guest(_factory);
            var bare = _factory.CreateClient();
            bare.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, "wrong");
            Assert.Equal(HttpStatusCode.NotFound, (await bare.PostAsync(AdminRoutes.Prefix + "/key", null)).StatusCode);
            var keyOnly = _factory.CreateClient();
            keyOnly.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, Key);
            var k = await keyOnly.PostAsync(AdminRoutes.Prefix + "/key", null);
            Assert.Equal(HttpStatusCode.OK, k.StatusCode);
            Assert.True((await Read<AdminKeyResponse>(k)).Google);
            Assert.Equal(HttpStatusCode.Unauthorized, (await keyOnly.GetAsync(AdminRoutes.Prefix + "/status")).StatusCode);
            c.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, Key);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(AdminRoutes.Prefix + "/status")).StatusCode);

            _factory.Services.GetRequiredService<MasterAccounts>().Add(who.AccountId);
            var st = await Read<AdminStatus>(await c.GetAsync(AdminRoutes.Prefix + "/status"));
            Assert.Equal(who.AccountId, st.You);
            Assert.Contains(ServerSetup.DefaultGame, st.Games);
            var list = await Read<AdminAccountList>(await c.GetAsync(AdminRoutes.Prefix + "/accounts"));
            Assert.Contains(list.Accounts, a => a.AccountId == who.AccountId && a.Games.ContainsKey(ServerSetup.DefaultGame));
            var d = await Read<AdminAccountDetail>(await c.GetAsync(AdminRoutes.Prefix + "/games/" + ServerSetup.DefaultGame + "/accounts/" + who.AccountId));
            Assert.True(d.Revision > 0);
            Assert.NotNull(d.Wallet);
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(AdminRoutes.Prefix + "/games/" + ServerSetup.DefaultGame + "/territory")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(AdminRoutes.Prefix + "/games/nope/territory")).StatusCode);
        }

        [Fact(DisplayName = "관리자 우편: 지정 계정에 운영 선물 → 받는 사람이 mail.claim으로 지갑에, 틀린 자원 · 양은 422, 없는 계정만이면 noTarget")]
        public async Task GiftMail()
        {
            var (admin, me) = await Guest(_factory);
            _factory.Services.GetRequiredService<MasterAccounts>().Add(me.AccountId);
            admin.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, Key);
            var (player, them) = await Guest(_factory);
            string url = AdminRoutes.Prefix + "/games/" + ServerSetup.DefaultGame + "/mail";

            Assert.Equal((HttpStatusCode)422, (await admin.PostAsync(url, Body(new AdminMailRequest { AccountIds = new List<string> { them.AccountId }, Resource = "wood", Amount = 5 }))).StatusCode);
            Assert.Equal((HttpStatusCode)422, (await admin.PostAsync(url, Body(new AdminMailRequest { AccountIds = new List<string> { them.AccountId }, Resource = "gem", Amount = AdminRoutes.MaxAmount + 1 }))).StatusCode);
            var none = await Read<AdminMailResponse>(await admin.PostAsync(url, Body(new AdminMailRequest { AccountIds = new List<string> { "acct-nobody" }, Resource = "gem", Amount = 5 })));
            Assert.Equal(("noTarget", 0), (none.Reason, none.Sent));

            var sent = await Read<AdminMailResponse>(await admin.PostAsync(url, Body(new AdminMailRequest { AccountIds = new List<string> { them.AccountId }, Resource = "gem", Amount = 30, Note = "점검 보상" })));
            Assert.Equal((true, 1), (sent.Ok, sent.Sent));
            var box = await Read<MailListResponse>(await player.GetAsync(OnlineRoutes.Mail(ServerSetup.DefaultGame)));
            var gift = Assert.Single(box.Mail, m => m.Kind == MailKinds.AdminGift);
            Assert.Equal(("gem", 30L, "점검 보상"), (gift.Resource, gift.Amount, gift.Note));

            var save = await Read<CloudSave>(await player.GetAsync(OnlineRoutes.Save(ServerSetup.DefaultGame)));
            var claim = await player.PostAsync(OnlineRoutes.Actions(ServerSetup.DefaultGame), Body(new ActionSubmission { BaseRevision = save.Revision, Actions = { PlayerActionRunner.ClaimMail(gift) } }));
            Assert.True(claim.StatusCode == HttpStatusCode.OK, await claim.Content.ReadAsStringAsync());
            var before = SaveJson.Read(save.Json);
            var after = SaveJson.Read((await Read<ActionVerdict>(claim)).Save.Json);
            long had = before.Wallet.TryGetValue("gem", out var g) ? g : 0;
            Assert.Equal(had + 30, after.Wallet["gem"]);
        }
    }
}
