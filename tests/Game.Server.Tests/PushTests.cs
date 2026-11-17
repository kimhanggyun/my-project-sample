using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;
using Game.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>푸시 알림 서버 쪽 (R-119): 설정 · 토큰 저장, 공성전 시작 때 보내기, 설정 끔, 무효 토큰 지움. 보내기는 가짜(Game:Push=fake).</summary>
    public class PushTests : IClassFixture<PushTests.Factory>
    {
        public sealed class Factory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseSetting("Game:Push", "fake");
                builder.ConfigureServices(s =>
                {
                    s.RemoveAll<ITimeProvider>();
                    s.AddSingleton<ITimeProvider>(new ManualTimeProvider(ServerTests.Clock));
                });
            }
        }

        private readonly Factory _f;
        public PushTests(Factory f) { _f = f; }

        private static readonly string Game = ServerSetup.DefaultGame;
        private static StringContent Body(object o) => new StringContent(OnlineJson.Write(o), Encoding.UTF8, "application/json");
        private static async Task<T> Read<T>(HttpResponseMessage r) => OnlineJson.Read<T>(await r.Content.ReadAsStringAsync());

        private async Task<HttpClient> SignedIn()
        {
            var c = _f.CreateClient();
            var who = await Read<SignInResponse>(await c.PostAsync(OnlineRoutes.GuestSignIn, null));
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", who.Token);
            return c;
        }

        private FakePushSender Sender => (FakePushSender)((StrategyGameModule)_f.Services.GetRequiredService<GameRegistry>().Find(Game)).PushSender;

        [Fact(DisplayName = "R-119 설정 · 토큰: 처음엔 모두 켬, 저장하면 그대로, 빈 토큰은 422, 지우기")]
        public async Task SettingsAndToken()
        {
            var c = await SignedIn();
            var s = await Read<PushSettings>(await c.GetAsync(OnlineRoutes.PushSettings(Game)));
            Assert.True(s.Attacked && s.AllyHelp && s.IncomeFull);
            var saved = await Read<PushSettings>(await c.PostAsync(OnlineRoutes.PushSettings(Game), Body(new PushSettings { Attacked = true, AllyHelp = false, IncomeFull = false })));
            Assert.False(saved.AllyHelp);
            Assert.False((await Read<PushSettings>(await c.GetAsync(OnlineRoutes.PushSettings(Game)))).IncomeFull);
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync(OnlineRoutes.PushToken(Game), Body(new PushTokenRequest { Token = "t-1", Platform = "android" }))).StatusCode);
            Assert.Equal((HttpStatusCode)422, (await c.PostAsync(OnlineRoutes.PushToken(Game), Body(new PushTokenRequest { Token = " " }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync(OnlineRoutes.PushToken(Game), Body(new PushTokenRequest { Token = "t-1", Remove = true }))).StatusCode);
        }

        [Fact(DisplayName = "R-119 공성전 준비 시작 → 수비자 기기에 attacked 푸시 (설정을 끄면 안 감, 무효 토큰은 지움)")]
        public async Task SiegeSendsPush()
        {
            var attacker = await SignedIn();
            var defender = await SignedIn();
            const string here = "37.3012,127.0101";
            var dSave = SaveJson.Read((await Read<CloudSave>(await defender.GetAsync(OnlineRoutes.Save(Game)))).Json);
            var plant = await Read<PlantResponse>(await defender.PostAsync(OnlineRoutes.TerritoryPlant(Game), Body(new PlantRequest { Location = here, GeneralId = dSave.CompositionsFor(null)[0].GeneralId })));
            Assert.True(plant.Ok, plant.Reason);
            string token = "def-" + System.Guid.NewGuid();
            await defender.PostAsync(OnlineRoutes.PushToken(Game), Body(new PushTokenRequest { Token = token, Platform = "android" }));
            await defender.PostAsync(OnlineRoutes.PushToken(Game), Body(new PushTokenRequest { Token = "bad:" + token }));

            var r = await attacker.PostAsync(OnlineRoutes.TerritorySiege(Game), Body(new SiegeRequest { FlagKey = plant.Flag.RegionId, Location = here }));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var got = Sender.Sent.Where(x => x.token == token).ToList();
            var push = Assert.Single(got);
            Assert.Equal(PushKinds.Attacked, push.data["kind"]);
            Assert.Equal(plant.Flag.RegionId, push.data["regionId"]);
            Assert.False(push.title.StartsWith("push."));

            // 취소 뒤 수비자가 attacked를 끄고 다시 준비 → 안 감 (무효 토큰은 첫 보내기에서 지워짐)
            await attacker.PostAsync(OnlineRoutes.TerritorySiegeCancel(Game), Body(new SiegeRequest { FlagKey = plant.Flag.RegionId }));
            await defender.PostAsync(OnlineRoutes.PushSettings(Game), Body(new PushSettings { Attacked = false }));
            await attacker.PostAsync(OnlineRoutes.TerritorySiege(Game), Body(new SiegeRequest { FlagKey = plant.Flag.RegionId, Location = here }));
            Assert.Single(Sender.Sent, x => x.token == token);
            var store = _f.Services.GetRequiredService<IGameStore>();
            var me = (await Read<ProfileResponse>(await defender.GetAsync(OnlineRoutes.Me))).AccountId;
            Assert.Equal(new[] { token }, store.WithPush(Game, me, p => p.Tokens.Select(t => t.Token).ToArray()));
        }
    }
}
