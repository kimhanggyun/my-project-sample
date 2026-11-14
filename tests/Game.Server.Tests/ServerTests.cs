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
        private static readonly string Save = OnlineRoutes.Save(ServerSetup.DefaultGame);
        private static readonly string Battles = OnlineRoutes.Battles(ServerSetup.DefaultGame);

        private async Task<HttpClient> SignedIn()
        {
            var c = _factory.CreateClient();
            var r = await c.PostAsync(OnlineRoutes.GuestSignIn, null);
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
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(Save)).StatusCode);
            var t = await Read<ServerTimeResponse>(await c.GetAsync(OnlineRoutes.Time));
            Assert.Equal(Clock.ToUnixTimeSeconds(), t.UnixSeconds);
        }

        [Fact(DisplayName = "클라우드 세이브: 처음엔 새 게임(Revision 1), 받은 Revision 위에만 저장, 다르면 409 + 서버 사본")]
        public async Task CloudSave()
        {
            var c = await SignedIn();
            var first = await Read<CloudSave>(await c.GetAsync(Save));
            Assert.Equal(1, first.Revision);
            Assert.NotNull(StrategyGameModule.FromJson(first.Json));

            var ok = await c.PutAsync(Save, Body(new SavePutRequest { BaseRevision = 1, Json = first.Json }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(2, (await Read<SavePutResponse>(ok)).Current.Revision);

            var stale = await c.PutAsync(Save, Body(new SavePutRequest { BaseRevision = 1, Json = first.Json }));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(2, (await Read<SavePutResponse>(stale)).Current.Revision);

            var junk = await c.PutAsync(Save, Body(new SavePutRequest { BaseRevision = 2, Json = "{not a save" }));
            Assert.Equal(HttpStatusCode.Conflict, junk.StatusCode);
        }

        [Fact(DisplayName = "R-64 결제: 가짜 영수증(fake:)은 참고 서버가 받고 자원을 반영, 같은 영수증 다시·영수증 없음·다른 형식은 422")]
        public async Task Purchase()
        {
            var c = await SignedIn();
            var actions = OnlineRoutes.Actions(ServerSetup.DefaultGame);
            var first = await Read<CloudSave>(await c.GetAsync(Save));
            long gold = StrategyGameModule.FromJson(first.Json).Wallet["gold"];
            string receipt = "fake:gold_pack_small:" + System.Guid.NewGuid();
            var ok = await c.PostAsync(actions, Body(new ActionSubmission { BaseRevision = first.Revision, Actions = { Game.Flow.PlayerActionRunner.Purchase("gold_pack_small", receipt) } }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var v = await Read<ActionVerdict>(ok);
            Assert.Equal(gold + 1000, StrategyGameModule.FromJson(v.Save.Json).Wallet["gold"]);
            foreach (var r in new[] { receipt, "", "store:real-receipt" })
            {
                var bad = await c.PostAsync(actions, Body(new ActionSubmission { BaseRevision = v.Save.Revision, Actions = { Game.Flow.PlayerActionRunner.Purchase("gold_pack_small", r) } }));
                Assert.Equal((HttpStatusCode)422, bad.StatusCode);
            }
        }

        [Fact(DisplayName = "R-86 마스터: 개발 행동 dev.*은 마스터 계정만 (아니면 422), 프로필에 IsMaster")]
        public async Task MasterOnlyDevActions()
        {
            var c = _factory.CreateClient();
            var who = OnlineJson.Read<SignInResponse>(await (await c.PostAsync(OnlineRoutes.GuestSignIn, null)).Content.ReadAsStringAsync());
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", who.Token);
            var actions = OnlineRoutes.Actions(ServerSetup.DefaultGame);
            var first = await Read<CloudSave>(await c.GetAsync(Save));
            Assert.False((await Read<ProfileResponse>(await c.GetAsync(OnlineRoutes.Me))).IsMaster);
            var denied = await c.PostAsync(actions, Body(new ActionSubmission { BaseRevision = first.Revision, Actions = { PlayerActionRunner.DevGrant("supply", 1000) } }));
            Assert.Equal((HttpStatusCode)422, denied.StatusCode);

            _factory.Services.GetRequiredService<MasterAccounts>().Add(who.AccountId);
            Assert.True((await Read<ProfileResponse>(await c.GetAsync(OnlineRoutes.Me))).IsMaster);
            long supply = StrategyGameModule.FromJson(first.Json).Wallet["supply"];
            var ok = await c.PostAsync(actions, Body(new ActionSubmission { BaseRevision = first.Revision, Actions = { PlayerActionRunner.DevGrant("supply", 1000), PlayerActionRunner.DevUnlockAll() } }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var after = StrategyGameModule.FromJson((await Read<ActionVerdict>(ok)).Save.Json);
            Assert.Equal(supply + 1000, after.Wallet["supply"]);
            Assert.NotEmpty(after.ClearedSteps);
        }

        [Fact(DisplayName = "R-41 행동 제출: 서버가 같은 규칙으로 적용(200), 안 되는 행동은 422, 옛 세이브는 409. 진행이 생긴 뒤 세이브 통째 올리기는 409")]
        public async Task Actions()
        {
            var c = await SignedIn();
            var actions = OnlineRoutes.Actions(ServerSetup.DefaultGame);
            var first = await Read<CloudSave>(await c.GetAsync(Save));
            var create = new ActionSubmission { BaseRevision = first.Revision, Actions = { Game.Flow.PlayerActionRunner.CreateUnit("catapult") } };
            var ok = await c.PostAsync(actions, Body(create));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var v = await Read<ActionVerdict>(ok);
            Assert.Equal(2, v.Save.Revision);
            var state = StrategyGameModule.FromJson(v.Save.Json);
            Assert.Equal(StrategyGameModule.FromJson(first.Json).Units.Count + 1, state.Units.Count);

            var bad = await c.PostAsync(actions, Body(new ActionSubmission { BaseRevision = 2, Actions = { Game.Flow.PlayerActionRunner.DisbandUnit("u99") } }));
            Assert.Equal((HttpStatusCode)422, bad.StatusCode);
            Assert.Equal(VerdictReasons.BadAction, (await Read<ActionVerdict>(bad)).Reason);
            Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync(actions, Body(create))).StatusCode);

            var put = await c.PutAsync(Save, Body(new SavePutRequest { BaseRevision = 2, Json = first.Json }));
            Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
            Assert.Equal(2, (await Read<SavePutResponse>(put)).Current.Revision);
        }

        [Fact(DisplayName = "R-42 전투 기록 시각이 서버 세이브 시각보다 이르면 422 badTime, 세이브 그대로")]
        public async Task BattleTimeWindow()
        {
            var c = await SignedIn();
            var save = await Read<CloudSave>(await c.GetAsync(Save));
            var (sub, _) = PlayOnDevice(save, 4);
            sub.PlayedAtUnixSeconds = save.SavedAtUnixSeconds - 3600;
            var r = await c.PostAsync(Battles, Body(sub));
            Assert.Equal((HttpStatusCode)422, r.StatusCode);
            var v = await Read<BattleVerdict>(r);
            Assert.Equal(VerdictReasons.BadTime, v.Reason);
            Assert.Equal(save.Revision, v.Save.Revision);
        }

        [Fact(DisplayName = "R-81 녹화 공유: A가 올린 녹화(서버가 다시 돌려 확인)를 B가 스텝 목록에서 보고 받아 재생 가능, 고친 녹화는 422")]
        public async Task ShareReplay()
        {
            var a = await SignedIn();
            var b = await SignedIn();
            var route = OnlineRoutes.SharedReplays(ServerSetup.DefaultGame);
            var source = new FileDataSource(DataRoot);
            var defs = DefinitionSet.Load(source, "ancientwar");
            var flow = new StoryFlow(defs, source, new ManualTimeProvider(Clock));
            var state = Game.Campaign.StartingKitService.CreateNewGame(defs);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(plan.Ok);
            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            var rec = ReplayService.Capture(plan, sim.CommandLog, sim.Result, 1000);
            string json = ReplayService.Write(rec);

            var up = await a.PostAsync(route, Body(new ShareReplayRequest { ReplayJson = json }));
            Assert.Equal(HttpStatusCode.OK, up.StatusCode);
            var shared = (await Read<ShareReplayResponse>(up)).Replay;
            Assert.Equal(("step01", rec.Won, rec.Hash), (shared.StepId, shared.Won, shared.Hash));
            // 같은 녹화를 다시 올려도 하나
            Assert.Equal(HttpStatusCode.OK, (await a.PostAsync(route, Body(new ShareReplayRequest { ReplayJson = json }))).StatusCode);

            // 결과를 고친 녹화는 거절 (다시 돌린 지문이 다름)
            var forged = ReplayService.Read(json);
            forged.Hash = "deadbeef";
            var bad = await a.PostAsync(route, Body(new ShareReplayRequest { ReplayJson = ReplayService.Write(forged) }));
            Assert.Equal((HttpStatusCode)422, bad.StatusCode);
            Assert.Equal(ReplayStatus.VersionChanged, (await Read<ShareReplayResponse>(bad)).Reason);
            Assert.Equal((HttpStatusCode)422, (await a.PostAsync(route, Body(new ShareReplayRequest { ReplayJson = "{" }))).StatusCode);

            var list = await Read<SharedReplayListResponse>(await b.GetAsync(route + "?step=step01"));
            var item = Assert.Single(list.Items);
            Assert.Empty((await Read<SharedReplayListResponse>(await b.GetAsync(route + "?step=step02"))).Items);
            var got = await Read<SharedReplayResponse>(await b.GetAsync(OnlineRoutes.SharedReplay(ServerSetup.DefaultGame, item.Id)));
            Assert.True(new ReplayService(flow).Check(ReplayService.Read(got.ReplayJson)).Ok);
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(OnlineRoutes.SharedReplay(ServerSetup.DefaultGame, "nope"))).StatusCode);
        }

        [Fact(DisplayName = "R-43 고용: A가 부대 등록 → B가 목록에서 골라 고용해 전투(200) → A 우편에 보상 → A가 받으면 지갑에, 두 번은 422")]
        public async Task HireFlow()
        {
            var a = await SignedIn();
            var b = await SignedIn();
            var listings = OnlineRoutes.HireListings(ServerSetup.DefaultGame);
            var actions = OnlineRoutes.Actions(ServerSetup.DefaultGame);
            var mail = OnlineRoutes.Mail(ServerSetup.DefaultGame);

            var tooExpensive = await a.PostAsync(listings, Body(new HireListingRequest { Kind = HireKinds.Unit, UnitId = "cavalry", Price = 99, PayWith = "gold" }));
            Assert.Equal((HttpStatusCode)422, tooExpensive.StatusCode);
            var reg = await a.PostAsync(listings, Body(new HireListingRequest { Kind = HireKinds.Unit, UnitId = "cavalry", Price = 2, PayWith = "gold" }));
            Assert.True(reg.StatusCode == HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
            var listing = (await Read<HireListingResponse>(reg)).Listing;
            Assert.Equal((1L, 1000L), (listing.Version, listing.Troops));

            var list = await Read<HireListResponse>(await b.GetAsync(listings + "?kind=unit"));
            var seen = Assert.Single(list.Listings, l => l.Id == listing.Id);

            // B: 서버 세이브로 출진 준비 (고용 500명을 general_a 군단의 정찰 칸에)
            var save = await Read<CloudSave>(await b.GetAsync(Save));
            var source = new FileDataSource(DataRoot);
            var defs = DefinitionSet.Load(source, "ancientwar");
            var flow = new StoryFlow(defs, source, new ManualTimeProvider(Clock));
            var state = StrategyGameModule.FromJson(save.Json);
            var hired = new HireRules(defs).Resolve(new[] { new HireUse { ListingId = seen.Id, Version = seen.Version, Count = 500 } }, id => id == seen.Id ? seen : null, null, state, out var err);
            Assert.Null(err);
            var comps = state.Compositions.Select(Game.Campaign.Roster.FormationEditor.Copy).ToList();
            comps[0].Picks.Add(new Game.Campaign.Roster.TroopPick { UnitId = HireRules.UnitIdOf(seen.Id), Troops = 500, Hired = true }); // 고용 부대는 계급 칸 밖 (army-v2.md 6장)
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 6, Delegate = true, Hired = hired, Compositions = comps });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            var battle = await b.PostAsync(Battles, Body(CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, save.Revision)));
            Assert.Equal(HttpStatusCode.OK, battle.StatusCode);

            var box = await Read<MailListResponse>(await a.GetAsync(mail));
            var reward = Assert.Single(box.Mail);
            Assert.Equal((MailKinds.HireReward, "gold", 700L), (reward.Kind, reward.Resource, reward.Amount)); // 2 × 500 × 0.7
            Assert.Empty((await Read<MailListResponse>(await b.GetAsync(mail))).Mail);

            var aSave = await Read<CloudSave>(await a.GetAsync(Save));
            long before = StrategyGameModule.FromJson(aSave.Json).Wallet["gold"];
            var claim = await a.PostAsync(actions, Body(new ActionSubmission { BaseRevision = aSave.Revision, Actions = { PlayerActionRunner.ClaimMail(reward) } }));
            Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
            var after = await Read<ActionVerdict>(claim);
            Assert.Equal(before + 700, StrategyGameModule.FromJson(after.Save.Json).Wallet["gold"]);
            Assert.Empty((await Read<MailListResponse>(await a.GetAsync(mail))).Mail);
            var again = await a.PostAsync(actions, Body(new ActionSubmission { BaseRevision = after.Save.Revision, Actions = { PlayerActionRunner.ClaimMail(reward) } }));
            Assert.Equal((HttpStatusCode)422, again.StatusCode);

            // 이력·제한 (R-44): 이력이 쌓이고, B는 대기시간 동안 같은 등록을 다시 고용할 수 없다
            var listed = await Read<HireListResponse>(await b.GetAsync(listings));
            var withStats = Assert.Single(listed.Listings, l => l.Id == listing.Id);
            Assert.Equal(1, withStats.Stats.Hires);
            Assert.Equal(500, withStats.Recent.Single().TroopsStart);
            Assert.Equal(1, listed.DailyUsed);
            Assert.True(listed.CooldownUntil.ContainsKey(listing.Id));
            var bSave = await Read<CloudSave>(await b.GetAsync(Save));
            var bState = StrategyGameModule.FromJson(bSave.Json);
            var hired2 = new HireRules(defs).Resolve(new[] { new HireUse { ListingId = seen.Id, Version = seen.Version, Count = 500 } }, id => id == seen.Id ? seen : null, null, bState, out _);
            var comps2 = bState.Compositions.Select(Game.Campaign.Roster.FormationEditor.Copy).ToList();
            comps2[0].Picks.Add(new Game.Campaign.Roster.TroopPick { UnitId = HireRules.UnitIdOf(seen.Id), Troops = 500, Hired = true });
            var plan2 = flow.Prepare(bState, new SortieRequest { StepId = "step01", Seed = 7, Delegate = true, Hired = hired2, Compositions = comps2 });
            var sim2 = new BattleSimulation(plan2.Setup);
            while (!sim2.IsOver) sim2.Step();
            var limited = await b.PostAsync(Battles, Body(CloudSync.SubmissionOf(plan2, sim2.CommandLog, sim2.Result, bSave.Revision)));
            Assert.Equal((HttpStatusCode)422, limited.StatusCode);
            Assert.Equal(VerdictReasons.HireLimit, (await Read<BattleVerdict>(limited)).Reason);

            // 해제: 남의 등록은 못 지운다
            Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync(OnlineRoutes.HireListing(ServerSetup.DefaultGame, listing.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync(OnlineRoutes.HireListing(ServerSetup.DefaultGame, listing.Id))).StatusCode);
        }

        [Fact(Skip = "군대 개편 PR 2에서 다시 켬: 새 군대 규모(army-v2.md, 처음 십부장 보병 3,000)에 맞춘 스텝·영토 밸런스 재조정 전", DisplayName = "R-46 영토 전투: 이기면 그 지역 주인이 된다, 위치가 공격할 수 있는 곳이 아니면 422 badSortie")]
        public async Task TerritoryCapture()
        {
            var c = await SignedIn();
            var territory = OnlineRoutes.Territory(ServerSetup.DefaultGame);
            Assert.Empty((await Read<TerritoryResponse>(await c.GetAsync(territory))).Owners);
            var source = new FileDataSource(DataRoot);
            var defs = DefinitionSet.Load(source, "ancientwar");
            var flow = new StoryFlow(defs, source, new ManualTimeProvider(Clock));
            const string here = "37.566500,126.978000"; // 가짜 위치 서비스의 기본 좌표 (가동 1구역)

            var save = await Read<CloudSave>(await c.GetAsync(Save));
            BattleSubmission won = null;
            for (ulong seed = 1; seed <= 10 && won == null; seed++)
            {
                var plan = flow.Prepare(StrategyGameModule.FromJson(save.Json), new SortieRequest { StepId = "territory.dev_zone_a1", Seed = seed, Delegate = true, Location = here });
                Assert.True(plan.Ok, string.Join("\n", plan.Errors));
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                if (sim.Result.Winner == StoryFlow.PlayerTeam) won = CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, save.Revision);
            }
            Assert.NotNull(won);
            won.Location = "35.0,129.0"; // 등록된 지역 밖
            Assert.Equal((HttpStatusCode)422, (await c.PostAsync(Battles, Body(won))).StatusCode);
            won.Location = here;
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync(Battles, Body(won))).StatusCode);
            var owner = Assert.Single((await Read<TerritoryResponse>(await c.GetAsync(territory))).Owners);
            Assert.Equal("dev_zone_a1", owner.RegionId);
            Assert.NotNull(owner.Pending); // 내 지역: 쌓인 수입 (시계가 멈춰 있어 0)
            var collect = await c.PostAsync(OnlineRoutes.TerritoryCollect(ServerSetup.DefaultGame), null);
            Assert.Equal(HttpStatusCode.OK, collect.StatusCode);
            Assert.Empty((await Read<MailListResponse>(collect)).Mail);

            // 수비 편성 (R-48): 주인이 등록하면 목록에 판과 함께 보이고, 그 뒤 AI 수비군(판 0)으로 싸운 제출은 거절
            var defenseRoute = OnlineRoutes.TerritoryDefense(ServerSetup.DefaultGame);
            Assert.Equal((HttpStatusCode)422, (await c.PostAsync(defenseRoute, Body(new DefenseRequest { RegionId = "dev_zone_a2" }))).StatusCode); // 내 지역 아님
            var reg = await c.PostAsync(defenseRoute, Body(new DefenseRequest { RegionId = "dev_zone_a1" }));
            Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
            Assert.Equal(1, (await Read<DefenseResponse>(reg)).Defense.Version);
            Assert.Equal(1, Assert.Single((await Read<TerritoryResponse>(await c.GetAsync(territory))).Owners).Defense.Version);
            var now = await Read<CloudSave>(await c.GetAsync(Save));
            var plan2 = flow.Prepare(StrategyGameModule.FromJson(now.Json), new SortieRequest { StepId = "territory.dev_zone_a1", Seed = 30, Delegate = true, Location = here });
            var sim2 = new BattleSimulation(plan2.Setup);
            while (!sim2.IsOver) sim2.Step();
            var stale = await c.PostAsync(Battles, Body(CloudSync.SubmissionOf(plan2, sim2.CommandLog, sim2.Result, now.Revision)));
            Assert.Equal((HttpStatusCode)422, stale.StatusCode);
        }

        [Fact(DisplayName = "R-49 연합: A가 만들고 B가 가입, 목록에 둘, 이름이 겹치면 422, 탈퇴하면 연합장이 넘어간다")]
        public async Task Alliances()
        {
            var a = await SignedIn();
            var b = await SignedIn();
            var route = OnlineRoutes.Alliances(ServerSetup.DefaultGame);
            var made = await Read<AllianceResponse>(await a.PostAsync(route, Body(new AllianceRequest { Name = "시험연합" + System.Guid.NewGuid().ToString("N").Substring(0, 4) })));
            Assert.True(made.Ok);
            Assert.Equal((HttpStatusCode)422, (await b.PostAsync(route, Body(new AllianceRequest { Name = made.Alliance.Name }))).StatusCode);
            var joined = await Read<AllianceResponse>(await b.PostAsync(OnlineRoutes.AllianceJoin(ServerSetup.DefaultGame, made.Alliance.Id), null));
            Assert.Equal(2, joined.Alliance.Members.Count);
            var list = await Read<AllianceListResponse>(await b.GetAsync(route));
            Assert.Equal(made.Alliance.Id, list.MineId);
            Assert.Equal(HttpStatusCode.OK, (await a.PostAsync(OnlineRoutes.AllianceLeave(ServerSetup.DefaultGame), null)).StatusCode);
            var after = await Read<AllianceListResponse>(await b.GetAsync(route));
            var mine = Assert.Single(after.Alliances, x => x.Id == made.Alliance.Id);
            Assert.Single(mine.Members);
            Assert.Equal(mine.Members[0], mine.LeaderAccountId);

            // R-50: 연합장(B)이 닫으면 A는 신청만, B가 수락하면 들어온다
            var manage = OnlineRoutes.AllianceManage(ServerSetup.DefaultGame);
            Assert.Equal(HttpStatusCode.OK, (await b.PostAsync(manage, Body(new AllianceManageRequest { Action = AllianceActions.Close }))).StatusCode);
            Assert.Equal(AllianceReasons.Requested, (await Read<AllianceResponse>(await a.PostAsync(OnlineRoutes.AllianceJoin(ServerSetup.DefaultGame, made.Alliance.Id), null))).Reason);
            Assert.Equal((HttpStatusCode)422, (await a.PostAsync(manage, Body(new AllianceManageRequest { Action = AllianceActions.Open }))).StatusCode); // A는 연합원 아님
            var aId = Assert.Single((await Read<AllianceListResponse>(await b.GetAsync(route))).Alliances, x => x.Id == made.Alliance.Id).Requests.Single();
            var accepted = await Read<AllianceResponse>(await b.PostAsync(manage, Body(new AllianceManageRequest { Action = AllianceActions.Accept, AccountId = aId })));
            Assert.Equal(2, accepted.Alliance.Members.Count);
        }

        [Fact(DisplayName = "닉네임: 정하면 내 계정·이름 찾기에 나오고, 남이 쓰는 이름(대소문자 무시)·규칙 밖은 422")]
        public async Task Nicknames()
        {
            var a = await SignedIn();
            var b = await SignedIn();
            string name = "N" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
            var set = await a.PostAsync(OnlineRoutes.MyNickname, Body(new NicknameRequest { Nickname = name }));
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
            var me = await Read<ProfileResponse>(await a.GetAsync(OnlineRoutes.Me));
            Assert.Equal(name, me.Nickname);
            Assert.Equal((HttpStatusCode)422, (await b.PostAsync(OnlineRoutes.MyNickname, Body(new NicknameRequest { Nickname = name.ToLowerInvariant() }))).StatusCode);
            Assert.Equal((HttpStatusCode)422, (await b.PostAsync(OnlineRoutes.MyNickname, Body(new NicknameRequest { Nickname = "!" }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a.PostAsync(OnlineRoutes.MyNickname, Body(new NicknameRequest { Nickname = name }))).StatusCode); // 내 이름 다시
            var bMe = await Read<ProfileResponse>(await b.GetAsync(OnlineRoutes.Me));
            var names = await Read<NamesResponse>(await b.GetAsync(OnlineRoutes.Names(new[] { me.AccountId, bMe.AccountId })));
            Assert.Equal(name, names.Names[me.AccountId]);
            Assert.False(names.Names.ContainsKey(bMe.AccountId)); // b는 아직 없음
        }

        /// <summary>기기 쪽: 서버 세이브로 출진 준비 → 전투 → 제출 메시지.</summary>
        private static (BattleSubmission sub, BattleResult result) PlayOnDevice(CloudSave save, ulong seed, bool tamper = false)
        {
            var source = new FileDataSource(DataRoot);
            var defs = DefinitionSet.Load(source, "ancientwar");
            var flow = new StoryFlow(defs, source, new ManualTimeProvider(Clock));
            var state = StrategyGameModule.FromJson(save.Json);
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
            var save = await Read<CloudSave>(await c.GetAsync(Save));

            var (bad, _) = PlayOnDevice(save, 7, tamper: true);
            var rejected = await c.PostAsync(Battles, Body(bad));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            Assert.Equal(VerdictReasons.Mismatch, (await Read<BattleVerdict>(rejected)).Reason);
            Assert.Equal(save.Revision, (await Read<CloudSave>(await c.GetAsync(Save))).Revision); // 그대로

            var (sub, result) = PlayOnDevice(save, 7);
            var accepted = await c.PostAsync(Battles, Body(sub));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var verdict = await Read<BattleVerdict>(accepted);
            Assert.True(verdict.Valid);
            Assert.Equal(result.StateHash.ToString("x16"), verdict.Hash);
            Assert.Equal(save.Revision + 1, verdict.Save.Revision);
            var after = StrategyGameModule.FromJson(verdict.Save.Json);
            Assert.Equal(result.Winner == StoryFlow.PlayerTeam, after.ClearedSteps.Contains("step01"));

            var again = await c.PostAsync(Battles, Body(sub)); // 같은 제출을 옛 세이브 위에서 다시
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal(VerdictReasons.StaleSave, (await Read<BattleVerdict>(again)).Reason);
        }

        [Fact(DisplayName = "게임 목록·앱 id 찾기, 없는 게임은 404, 게임에 들어오면 가입 기록")]
        public async Task GamesAndMembership()
        {
            var c = await SignedIn();
            var games = await Read<GameListResponse>(await c.GetAsync(OnlineRoutes.Games));
            Assert.Contains(ServerSetup.DefaultGame, games.Games);
            var resolved = await Read<ResolveAppResponse>(await c.GetAsync(OnlineRoutes.ResolveApp("net.itgold.unitygame1")));
            Assert.Equal(ServerSetup.DefaultGame, resolved.Game);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(OnlineRoutes.ResolveApp("com.unknown"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(OnlineRoutes.Save("nogame"))).StatusCode);

            Assert.Empty((await Read<GameListResponse>(await c.GetAsync(OnlineRoutes.MyGames))).Games);
            await c.GetAsync(Save);
            Assert.Equal(new[] { ServerSetup.DefaultGame }, (await Read<GameListResponse>(await c.GetAsync(OnlineRoutes.MyGames))).Games);
        }
    }

    /// <summary>여러 게임을 서버 한 곳에서: 같은 계정이라도 게임마다 세이브가 따로 (docs/impl/server.md 4.5).</summary>
    public class MultiGameTests : IClassFixture<MultiGameTests.TwoGames>
    {
        public sealed class TwoGames : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
            {
                builder.UseSetting("Game:Games:0:Code", "ancientwar");
                builder.UseSetting("Game:Games:0:Theme", "ancientwar");
                builder.UseSetting("Game:Games:0:PackageIds:0", "net.itgold.unitygame1");
                builder.UseSetting("Game:Games:1:Code", "secondgame");
                builder.UseSetting("Game:Games:1:Theme", "ancientwar");
                builder.UseSetting("Game:Games:1:PackageIds:0", "com.myproject.second");
                builder.ConfigureServices(s =>
                {
                    s.RemoveAll<ITimeProvider>();
                    s.AddSingleton<ITimeProvider>(new ManualTimeProvider(ServerTests.Clock));
                });
            }
        }

        private readonly TwoGames _f;
        public MultiGameTests(TwoGames f) { _f = f; }

        [Fact(DisplayName = "같은 계정, 게임마다 따로 세이브·Revision, 가입 기록은 두 게임 모두")]
        public async Task SeparateSaves()
        {
            var c = _f.CreateClient();
            var who = OnlineJson.Read<SignInResponse>(await (await c.PostAsync(OnlineRoutes.GuestSignIn, null)).Content.ReadAsStringAsync());
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", who.Token);

            var a = OnlineJson.Read<CloudSave>(await (await c.GetAsync(OnlineRoutes.Save("ancientwar"))).Content.ReadAsStringAsync());
            var put = await c.PutAsync(OnlineRoutes.Save("ancientwar"), new StringContent(OnlineJson.Write(new SavePutRequest { BaseRevision = a.Revision, Json = a.Json }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            var b = OnlineJson.Read<CloudSave>(await (await c.GetAsync(OnlineRoutes.Save("secondgame"))).Content.ReadAsStringAsync());
            Assert.Equal(1, b.Revision); // 첫 게임 저장과 무관
            var a2 = OnlineJson.Read<CloudSave>(await (await c.GetAsync(OnlineRoutes.Save("ancientwar"))).Content.ReadAsStringAsync());
            Assert.Equal(2, a2.Revision);

            var mine = OnlineJson.Read<GameListResponse>(await (await c.GetAsync(OnlineRoutes.MyGames)).Content.ReadAsStringAsync());
            Assert.Equal(new[] { "ancientwar", "secondgame" }, mine.Games);
            var resolved = OnlineJson.Read<ResolveAppResponse>(await (await c.GetAsync(OnlineRoutes.ResolveApp("com.myproject.second"))).Content.ReadAsStringAsync());
            Assert.Equal("secondgame", resolved.Game);
        }

        [Fact(DisplayName = "등록표: 같은 게임 코드·같은 앱 id를 두 번 등록하면 거절")]
        public void RegistryGuards()
        {
            var store = new InMemoryStore();
            var src = new FileDataSource(ServerPaths.DataRoot(null));
            var time = new ManualTimeProvider(ServerTests.Clock);
            var r = new GameRegistry().Register(new StrategyGameModule("g1", new[] { "app.one" }, src, "ancientwar", time, store));
            Assert.Throws<ArgumentException>(() => r.Register(new StrategyGameModule("g1", new string[0], src, "ancientwar", time, store)));
            Assert.Throws<ArgumentException>(() => r.Register(new StrategyGameModule("g2", new[] { "app.one" }, src, "ancientwar", time, store)));
        }
    }
}

namespace Game.Server.Tests
{
    /// <summary>기기용 HTTP 클라이언트(HttpOnlineService + CloudSync)를 실제 참고 서버에 붙여 끝까지 (테스트용 전송 = HttpClient).</summary>
    public class ClientEndToEndTests : IClassFixture<ServerTests.Factory>
    {
        private sealed class HttpClientTransport : IOnlineTransport
        {
            private readonly HttpClient _c;
            public HttpClientTransport(HttpClient c) { _c = c; }
            public void Send(string method, string path, string jsonBody, string bearerToken, Action<int, string> done)
            {
                var req = new HttpRequestMessage(new HttpMethod(method), path);
                if (jsonBody != null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                if (bearerToken != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                var res = _c.SendAsync(req).GetAwaiter().GetResult();
                done((int)res.StatusCode, res.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            }
        }

        private readonly ServerTests.Factory _f;
        public ClientEndToEndTests(ServerTests.Factory f) { _f = f; }

        [Fact(DisplayName = "기기 흐름: 로그인 → 서버 세이브 → 전투 → 제출 → 반영된 세이브, 토큰을 보관했다가 다시 쓰면 같은 계정")]
        public void DeviceFlow()
        {
            var transport = new HttpClientTransport(_f.CreateClient());
            var online = new HttpOnlineService(transport, ServerSetup.DefaultGame);
            var clock = new ServerClock(new ManualTimeProvider(ServerTests.Clock));
            var sync = new CloudSync(online, clock);

            SyncResult start = null;
            sync.Start(null, 0, r => start = r);
            Assert.Equal(SyncOutcomes.FromServer, start.Outcome);
            Assert.Equal(0, clock.OffsetSeconds);

            var source = new FileDataSource(ServerPaths.DataRoot(null));
            var defs = DefinitionSet.Load(source, "ancientwar");
            var flow = new StoryFlow(defs, source, clock);
            var state = start.State;
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 21, Delegate = true });
            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            flow.Confirm(state, plan);
            flow.ApplyResult(state, plan, sim.Result);
            SyncResult after = null;
            sync.SubmitBattle(CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, start.Revision), state, r => after = r);
            Assert.Equal(SyncOutcomes.Accepted, after.Outcome);
            Assert.Equal(start.Revision + 1, after.Revision);
            Assert.Equal(state.ClearedSteps, after.State.ClearedSteps); // 기기 계산 = 서버 계산

            var again = new HttpOnlineService(transport, ServerSetup.DefaultGame);
            again.Restore(online.AccountId, online.Token);
            CloudSave save = null;
            again.LoadSave(s => save = s);
            Assert.Equal(after.Revision, save.Revision);
        }

        [Fact(DisplayName = "서버가 꺼져 있으면(네트워크 실패) 기기 세이브로 계속")]
        public void ServerDown()
        {
            var down = new HttpOnlineService(new DownTransport(), ServerSetup.DefaultGame);
            SyncResult r = null;
            new CloudSync(down, null).Start(null, 3, x => r = x);
            Assert.Equal((SyncOutcomes.Offline, 3L), (r.Outcome, r.Revision));
        }

        private sealed class DownTransport : IOnlineTransport
        {
            public void Send(string method, string path, string jsonBody, string bearerToken, Action<int, string> done) => done(0, null);
        }
    }
}
