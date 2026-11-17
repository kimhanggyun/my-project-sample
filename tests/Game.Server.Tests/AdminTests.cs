using System.Linq;
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
                TestStore.Isolate(builder);
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
                TestStore.Isolate(builder);
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

[Fact(DisplayName = "R-118 버전 · 점검: /v1/status는 인증 없이, 관리자가 바꾸면 바로 반영 (버전은 있는 칸만, 점검은 통째), Clear면 서버 설정으로")]
        public async Task StatusNotice()
        {
            var anyone = _keyOnly.CreateClient();
            var first = await Read<ServerStatusResponse>(await anyone.GetAsync(OnlineRoutes.Status));
            Assert.Null(first.MinVersion);
            Assert.False(first.Maintenance.On);

            var admin = _keyOnly.CreateClient();
            admin.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, Key);
            var set = await admin.PostAsync(AdminRoutes.Prefix + "/notice", Body(new AdminStatusNotice { Override = new ServerStatusResponse { MinVersion = "1.2.0", Maintenance = new MaintenanceInfo { On = true, EndsAtUnixSeconds = 2_000_000_000 } } }));
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
            var now = await Read<ServerStatusResponse>(await anyone.GetAsync(OnlineRoutes.Status));
            Assert.Equal(("1.2.0", true, 2_000_000_000L), (now.MinVersion, now.Maintenance.On, now.Maintenance.EndsAtUnixSeconds));
            Assert.True(Game.Flow.AppStatus.Evaluate(now, "1.1.9", 1_900_000_000).UpdateRequired);

            var wrong = _keyOnly.CreateClient();
            wrong.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, "wrong");
            Assert.Equal(HttpStatusCode.NotFound, (await wrong.PostAsync(AdminRoutes.Prefix + "/notice", Body(new AdminStatusNotice { Clear = true }))).StatusCode);

            await admin.PostAsync(AdminRoutes.Prefix + "/notice", Body(new AdminStatusNotice { Clear = true }));
            var cleared = await Read<ServerStatusResponse>(await anyone.GetAsync(OnlineRoutes.Status));
            Assert.Null(cleared.MinVersion);
            Assert.False(cleared.Maintenance.On);
        }

        [Fact(DisplayName = "R-122 모집 배너 켜고 끄기: 관리자가 끄면 공개 /content에 실리고 서버가 그 배너 모집을 거절(422), 다시 켜면 됨, 키가 틀리면 404")]
        public async Task BannerToggles()
        {
            var (player, them) = await Guest(_keyOnly);
            var admin = _keyOnly.CreateClient();
            admin.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, Key);
            string url = AdminRoutes.Prefix + "/games/" + ServerSetup.DefaultGame + "/banners";
            var list = await Read<AdminBannerList>(await admin.GetAsync(url));
            Assert.Contains(list.Banners, b => b.Id == "standard" && !b.Off);

            var off = await Read<AdminBannerList>(await admin.PostAsync(url, Body(new AdminBannersRequest { Items = { new ContentToggle { Id = "standard", Off = true }, new ContentToggle { Id = "noSuch", Off = true } } })));
            Assert.True(off.Banners.Single(b => b.Id == "standard").Off);
            var content = await Read<ContentToggles>(await _keyOnly.CreateClient().GetAsync(OnlineRoutes.Content(ServerSetup.DefaultGame)));
            Assert.True(content.IsOff(ContentKinds.Banner, "standard"));
            Assert.DoesNotContain(content.Items, x => x.Id == "noSuch"); // 모르는 배너는 무시

            var actions = OnlineRoutes.Actions(ServerSetup.DefaultGame);
            var save = await Read<CloudSave>(await player.GetAsync(OnlineRoutes.Save(ServerSetup.DefaultGame)));
            var draw = Game.Flow.PlayerActionRunner.DrawGacha("standard", 1, 7);
            var rejected = await player.PostAsync(actions, Body(new ActionSubmission { BaseRevision = save.Revision, Actions = { draw } }));
            Assert.Equal((HttpStatusCode)422, rejected.StatusCode);

            await admin.PostAsync(url, Body(new AdminBannersRequest()));
            Assert.Empty((await Read<ContentToggles>(await _keyOnly.CreateClient().GetAsync(OnlineRoutes.Content(ServerSetup.DefaultGame)))).Items);
            var ok = await player.PostAsync(actions, Body(new ActionSubmission { BaseRevision = save.Revision, Actions = { draw } }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

            var wrong = _keyOnly.CreateClient();
            wrong.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, "wrong");
            Assert.Equal(HttpStatusCode.NotFound, (await wrong.GetAsync(url)).StatusCode);
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

        [Fact(DisplayName = "H8 계정 등급: 관리자가 정함, 서브 관리자 = 보기 · 우편 · 공지만 (운영 켜고 끄기 · 등급 지정 403, 개발 행동 없음), 저장소 관리자 = 마스터와 같음, 처음 관리자는 바꿀 수 없음")]
        public async Task AccountRoles()
        {
            var (boss, me) = await Guest(_factory);
            _factory.Services.GetRequiredService<MasterAccounts>().Add(me.AccountId);
            boss.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, Key);
            var (sub, them) = await Guest(_factory);
            sub.DefaultRequestHeaders.Add(AdminRoutes.KeyHeader, Key);
            string Role(string id) => AdminRoutes.Prefix + "/accounts/" + id + "/role";
            string banners = AdminRoutes.Prefix + "/games/" + ServerSetup.DefaultGame + "/banners";

            Assert.Equal(HttpStatusCode.Forbidden, (await sub.GetAsync(AdminRoutes.Prefix + "/status")).StatusCode); // 일반
            Assert.Equal(HttpStatusCode.OK, (await boss.PostAsync(Role(them.AccountId), Body(new AdminRoleRequest { Role = Game.Online.AccountRoles.SubAdmin }))).StatusCode);
            var st = await Read<AdminStatus>(await sub.GetAsync(AdminRoutes.Prefix + "/status"));
            Assert.Equal(Game.Online.AccountRoles.SubAdmin, st.YourRole);
            Assert.Equal(HttpStatusCode.OK, (await sub.GetAsync(AdminRoutes.Prefix + "/accounts")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await sub.GetAsync(AdminRoutes.Prefix + "/notice")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await sub.PostAsync(AdminRoutes.Prefix + "/games/" + ServerSetup.DefaultGame + "/mail", Body(new AdminMailRequest { AccountIds = new List<string> { me.AccountId }, Resource = "gold", Amount = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await sub.GetAsync(banners)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await sub.PostAsync(Role(them.AccountId), Body(new AdminRoleRequest { Role = Game.Online.AccountRoles.Admin }))).StatusCode);
            var subMe = await Read<ProfileResponse>(await sub.GetAsync(OnlineRoutes.Me));
            Assert.Equal((Game.Online.AccountRoles.SubAdmin, false), (subMe.Role, subMe.IsMaster)); // 개발 행동 없음

            Assert.Equal(HttpStatusCode.OK, (await boss.PostAsync(Role(them.AccountId), Body(new AdminRoleRequest { Role = Game.Online.AccountRoles.Admin }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await sub.GetAsync(banners)).StatusCode);
            Assert.True((await Read<ProfileResponse>(await sub.GetAsync(OnlineRoutes.Me))).IsMaster);
            var list = await Read<AdminAccountList>(await boss.GetAsync(AdminRoutes.Prefix + "/accounts"));
            Assert.Contains(list.Accounts, a => a.AccountId == me.AccountId && a.Fixed && a.Role == Game.Online.AccountRoles.Admin);
            Assert.Contains(list.Accounts, a => a.AccountId == them.AccountId && !a.Fixed && a.Role == Game.Online.AccountRoles.Admin);

            var fixedRes = await sub.PostAsync(Role(me.AccountId), Body(new AdminRoleRequest { Role = null }));
            Assert.Equal((HttpStatusCode)422, fixedRes.StatusCode);
            Assert.Equal("fixed", (await Read<AdminRoleResponse>(fixedRes)).Reason);
            Assert.Equal((HttpStatusCode)422, (await boss.PostAsync(Role(them.AccountId), Body(new AdminRoleRequest { Role = "king" }))).StatusCode);
            Assert.Equal((HttpStatusCode)422, (await boss.PostAsync(Role("acct-nobody"), Body(new AdminRoleRequest { Role = Game.Online.AccountRoles.SubAdmin }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await boss.PostAsync(Role(them.AccountId), Body(new AdminRoleRequest { Role = null }))).StatusCode); // 일반으로
            Assert.Equal(HttpStatusCode.Forbidden, (await sub.GetAsync(AdminRoutes.Prefix + "/status")).StatusCode);
            Assert.Null((await Read<ProfileResponse>(await sub.GetAsync(OnlineRoutes.Me))).Role);
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
