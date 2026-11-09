using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;
using Game.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>참고 서버: 게스트 계정, 클라우드 세이브(Revision 충돌), 전투 검증·반영 (verification R-40).</summary>
    public class ServerTests : IClassFixture<ServerTests.Factory>
    {
        /// <summary>서버 시각을 고정해 기기 쪽 계산과 같게 한다.</summary>
        public static readonly DateTimeOffset Clock = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

        public sealed class Factory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
            {
                builder.ConfigureServices(s =>
                {
                    s.RemoveAll<ITimeProvider>();
                    s.AddSingleton<ITimeProvider>(new ManualTimeProvider(Clock));
                });
            }
        }

        private readonly Factory _factory;
        public ServerTests(Factory f) { _factory = f; }

        private static string DataRoot => ServerPaths.DataRoot(null);

        private async Task<HttpClient> SignedIn()
        {
            var c = _factory.CreateClient();
            var r = await c.PostAsync("/v1/auth/guest", null);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var who = OnlineJson.Read<SignInResponse>(await r.Content.ReadAsStringAsync());
            Assert.NotNull(who.Token);
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", who.Token);
            return c;
        }

        private static StringContent Body(object o) => new StringContent(OnlineJson.Write(o), Encoding.UTF8, "application/json");

        private static async Task<T> Read<T>(HttpResponseMessage r) => OnlineJson.Read<T>(await r.Content.ReadAsStringAsync());

        [Fact(DisplayName = "인증 없으면 401, 시각은 서버 시각")]
        public async Task AuthAndTime()
        {
            var c = _factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/v1/save")).StatusCode);
            var t = await Read<ServerTimeResponse>(await c.GetAsync("/v1/time"));
            Assert.Equal(Clock.ToUnixTimeSeconds(), t.UnixSeconds);
        }

        [Fact(DisplayName = "클라우드 세이브: 처음엔 새 게임(Revision 1), 받은 Revision 위에만 저장, 다르면 409 + 서버 사본")]
        public async Task CloudSave()
        {
            var c = await SignedIn();
            var first = await Read<CloudSave>(await c.GetAsync("/v1/save"));
            Assert.Equal(1, first.Revision);
            Assert.NotNull(GameService.FromJson(first.Json));

            var ok = await c.PutAsync("/v1/save", Body(new SavePutRequest { BaseRevision = 1, Json = first.Json }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(2, (await Read<SavePutResponse>(ok)).Current.Revision);

            var stale = await c.PutAsync("/v1/save", Body(new SavePutRequest { BaseRevision = 1, Json = first.Json }));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(2, (await Read<SavePutResponse>(stale)).Current.Revision);

            var junk = await c.PutAsync("/v1/save", Body(new SavePutRequest { BaseRevision = 2, Json = "{not a save" }));
            Assert.Equal(HttpStatusCode.Conflict, junk.StatusCode);
        }

        /// <summary>기기 쪽: 서버 세이브로 출진 준비 → 전투 → 제출 메시지.</summary>
        private static (BattleSubmission sub, BattleResult result) PlayOnDevice(CloudSave save, ulong seed, bool tamper = false)
        {
            var source = new FileDataSource(DataRoot);
            var defs = DefinitionSet.Load(source, "ancientwar");
            var flow = new StoryFlow(defs, source, new ManualTimeProvider(Clock));
            var state = GameService.FromJson(save.Json);
            var req = new SortieRequest { StepId = "step01", Seed = seed, Delegate = true };
            var plan = flow.Prepare(state, req);
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            var r = sim.Result;
            var sub = new BattleSubmission
            {
                BaseRevision = save.Revision, StepId = req.StepId, Seed = seed, Delegate = true,
                ContestChoices = plan.Setup.Contests.ToDictionary(x => x.ContestId, x => x.ChoicesA ?? new System.Collections.Generic.List<string>()),
                Commands = sim.CommandLog.ToList(),
                ClaimedHash = tamper ? "0000000000000000" : r.StateHash.ToString("x16"),
                ClaimedTicks = r.Ticks,
            };
            return (sub, r);
        }

        [Fact(DisplayName = "R-40 전투 제출: 서버가 다시 돌려 같으면 반영(새 Revision, 스텝 클리어), 지문이 다르면 422, 옛 세이브 위면 409")]
        public async Task BattleVerification()
        {
            var c = await SignedIn();
            var save = await Read<CloudSave>(await c.GetAsync("/v1/save"));

            var (bad, _) = PlayOnDevice(save, 7, tamper: true);
            var rejected = await c.PostAsync("/v1/battles", Body(bad));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            Assert.Equal(VerdictReasons.Mismatch, (await Read<BattleVerdict>(rejected)).Reason);
            Assert.Equal(save.Revision, (await Read<CloudSave>(await c.GetAsync("/v1/save"))).Revision); // 그대로

            var (sub, result) = PlayOnDevice(save, 7);
            var accepted = await c.PostAsync("/v1/battles", Body(sub));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var verdict = await Read<BattleVerdict>(accepted);
            Assert.True(verdict.Valid);
            Assert.Equal(result.StateHash.ToString("x16"), verdict.Hash);
            Assert.Equal(save.Revision + 1, verdict.Save.Revision);
            var after = GameService.FromJson(verdict.Save.Json);
            Assert.Equal(result.Winner == StoryFlow.PlayerTeam, after.ClearedSteps.Contains("step01"));

            var again = await c.PostAsync("/v1/battles", Body(sub)); // 같은 제출을 옛 세이브 위에서 다시
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal(VerdictReasons.StaleSave, (await Read<BattleVerdict>(again)).Reason);
        }
    }
}
