using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Game.Data.Json;
using Game.Foundation.Time;
using Game.Online;
using Game.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// 참고 서버 (docs/impl/server.md): 개발·테스트용. 공통 계정 + 게임별 데이터 (4.5).
// 실행: dotnet run --project server/Game.Server
// 설정: Game:Store = memory · folder · firestore (없으면 Game:StoreRoot가 있으면 folder, 아니면 memory — docs/impl/db.md),
//       Game:StoreRoot (folder), Game:Firestore:ProjectId · DatabaseId (firestore), Game:Games:0:Code / DataRoot / Theme / PackageIds:0 … (없으면 ancientwar 하나)
// 옮기기 (db.md 6장): dotnet Game.Server.dll migrate --from <store.json이 있는 폴더> --project <GCP 프로젝트> [--database <id>]
if (args.Length > 0 && args[0] == "migrate") return Migrate.Run(args);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
builder.Services.AddSingleton<IStore>(sp => ServerSetup.Store(builder.Configuration));
builder.Services.AddSingleton<IPlatformStore>(sp => sp.GetRequiredService<IStore>());
builder.Services.AddSingleton<IGameStore>(sp => sp.GetRequiredService<IStore>());
// Firebase 로그인 (docs/impl/deploy-cloudrun.md): Game:Firebase:ProjectId가 있을 때만
builder.Services.AddSingleton<IExternalTokenVerifier>(sp =>
{
    var project = builder.Configuration["Game:Firebase:ProjectId"];
    return string.IsNullOrEmpty(project) ? null : new FirebaseTokenVerifier(project, FirebaseTokenVerifier.GoogleCerts(new System.Net.Http.HttpClient()));
});
// 마스터 계정 (R-86): Game:Masters = 쉼표로 나눈 계정 id 또는 구글 로그인 이메일 (섞어도 됨)
builder.Services.AddSingleton(sp =>
{
    var masters = MasterAccounts.Parse(builder.Configuration["Game:Masters"]);
    masters.EmailOf = id => sp.GetRequiredService<IPlatformStore>().EmailOf(id);
    masters.StoredRole = id => sp.GetRequiredService<IPlatformStore>().RoleOf(id); // 관리자 페이지에서 정한 등급 (H8)
    return masters;
});
builder.Services.AddSingleton(sp => ServerSetup.Registry(builder.Configuration, sp.GetRequiredService<ITimeProvider>(), sp.GetRequiredService<IGameStore>(), sp.GetRequiredService<MasterAccounts>()));

var app = builder.Build();
var time = app.Services.GetRequiredService<ITimeProvider>();
long Now() => time.UtcNow.ToUnixTimeSeconds();

// 공통 (플랫폼)
app.MapPost(OnlineRoutes.GuestSignIn, (IPlatformStore p) => Json(p.CreateGuest()));
app.MapPost(OnlineRoutes.FirebaseSignIn, async (HttpRequest req, IPlatformStore p, IServiceProvider sp) =>
{
    var verifier = sp.GetService<IExternalTokenVerifier>();
    if (verifier == null) return Results.NotFound();
    var body = await Body<ExternalSignInRequest>(req);
    var who = verifier.Verify(body?.IdToken, Now());
    return who == null ? Results.Unauthorized() : Json(p.SignInExternal(who.Provider, who.Subject, who.Email));
});
app.MapGet(OnlineRoutes.Time, () => Json(new ServerTimeResponse { UnixSeconds = Now() }));
// 운영 켜고 끄기 (R-122): 인증 없이, 기기가 시작 때 받는다
app.MapGet("/v1/games/{game}/content", (string game, GameRegistry r) => r.Find(game) is IGameModule g ? Json(g.Content()) : Results.NotFound());
// 버전 확인 · 점검 안내 (R-118): 인증 없이, 설정 + 관리자 값
var statusFromConfig = ServerStatus.FromConfig(builder.Configuration);
app.MapGet(OnlineRoutes.Status, (IPlatformStore p) => Json(ServerStatus.Current(statusFromConfig, p.StatusOverride(), Now())));
app.MapGet(OnlineRoutes.Games, (GameRegistry r) => Json(new GameListResponse { Games = new List<string>(r.Codes) }));
app.MapGet("/v1/games/resolve", (string app, GameRegistry r) =>
{
    var code = r.CodeOfPackage(app);
    return code == null ? Results.NotFound() : Json(new ResolveAppResponse { Game = code });
});
// 계정 (공통): 닉네임
// 내 계정: 들어오면 삭제 요청을 취소한다 (R-102, 7일 유예 안에 다시 로그인)
app.MapGet(OnlineRoutes.Me, (HttpRequest req, IPlatformStore p, MasterAccounts masters) =>
{
    p.PurgeDeleted(Now());
    return Authed(req, p, id => { p.Touch(id, Now()); return Json(new ProfileResponse { AccountId = id, Nickname = p.NicknameOf(id), IsMaster = masters.Contains(id), Role = masters.RoleOf(id), DeletionCancelled = p.CancelDeletion(id), Linked = p.IsLinked(id) }); });
});
// 구글 연동 (R-105): 게스트 계정에 구글을 붙인다. 그 구글이 이미 다른 계정 것이면 409 taken (진행을 섞지 않는다)
app.MapPost(OnlineRoutes.LinkFirebase, async (HttpRequest req, IPlatformStore p, IServiceProvider sp) =>
{
    var verifier = sp.GetService<IExternalTokenVerifier>();
    if (verifier == null) return Results.NotFound();
    var body = await Body<ExternalSignInRequest>(req);
    return Authed(req, p, id =>
    {
        var who = verifier.Verify(body?.IdToken, Now());
        if (who == null) return Results.Unauthorized();
        string reason = p.LinkExternal(id, who.Provider, who.Subject, who.Email);
        var res = new LinkResponse { Ok = reason == LinkReasons.Ok, Reason = reason };
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status409Conflict);
    });
});
app.MapPost(OnlineRoutes.MyDelete, (HttpRequest req, IPlatformStore p) =>
    Authed(req, p, id => Json(new DeleteAccountResponse { Ok = true, PurgeAtUnixSeconds = p.RequestDeletion(id, Now()) })));
app.MapPost(OnlineRoutes.MyNickname, async (HttpRequest req, IPlatformStore p) =>
{
    var body = await Body<NicknameRequest>(req);
    return Authed(req, p, id =>
    {
        var res = p.SetNickname(id, body?.Nickname);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);
    });
});
// 닉네임 미리 확인 (H7): 인증 없이도 되고, 토큰이 맞으면 내 지금 이름은 사용 중으로 치지 않는다. 저장(POST /v1/me/nickname)도 그대로 다시 확인.
app.MapGet(OnlineRoutes.NicknameCheck, (HttpRequest req, IPlatformStore p) =>
{
    string auth = req.Headers.Authorization.ToString();
    string me = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? p.AccountOf(auth.Substring(7)) : null;
    string name = req.Query.TryGetValue("name", out var v) ? v.ToString() : "";
    return Json(p.CheckNickname(me, name));
});
app.MapGet("/v1/accounts/names", (HttpRequest req, IPlatformStore p) =>
    Authed(req, p, id =>
    {
        string ids = req.Query.TryGetValue("ids", out var v) ? v.ToString() : "";
        var list = new List<string>(ids.Split(',', StringSplitOptions.RemoveEmptyEntries));
        if (list.Count > 200) list = list.GetRange(0, 200); // 한 번에 묻는 수 상한
        return Json(new NamesResponse { Names = p.NicknamesOf(list) });
    }));
app.MapGet(OnlineRoutes.MyGames, (HttpRequest req, IPlatformStore p) =>
    Authed(req, p, id => Json(new GameListResponse { Games = new List<string>(p.GamesOf(id)) })));

// 게임별
app.MapGet("/v1/games/{game}/save", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.LoadOrCreate(id))));
app.MapPut("/v1/games/{game}/save", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<SavePutRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var res = g.Put(id, body);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status409Conflict);
    });
});
app.MapPost("/v1/games/{game}/battles", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<BattleSubmission>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var v = g.SubmitBattle(id, body);
        return v.Valid ? Json(v) : Json(v, v.Reason == VerdictReasons.StaleSave ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity);
    });
});

app.MapPost("/v1/games/{game}/actions", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<ActionSubmission>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var v = g.SubmitActions(id, body);
        return v.Valid ? Json(v) : Json(v, v.Reason == VerdictReasons.StaleSave ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity);
    });
});

// 고용 (R-43)
app.MapGet("/v1/games/{game}/hire/listings", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.Listings(req.Query.TryGetValue("kind", out var k) ? k.ToString() : null, id))));
app.MapPost("/v1/games/{game}/hire/listings", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<HireListingRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var res = g.RegisterHire(id, body);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);
    });
});
app.MapDelete("/v1/games/{game}/hire/listings/{listing}", (string game, string listing, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => g.Unregister(id, listing) ? Results.NoContent() : Results.NotFound()));
// 녹화 공유 (R-81)
app.MapPost("/v1/games/{game}/replays", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<ShareReplayRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var res = g.ShareReplay(id, p.NicknameOf(id), body);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);
    });
});
app.MapGet("/v1/games/{game}/replays", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.SharedReplays(req.Query.TryGetValue("step", out var s) ? s.ToString() : null))));
app.MapGet("/v1/games/{game}/replays/{replay}", (string game, string replay, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => { var res = g.SharedReplay(replay); return res == null ? Results.NotFound() : Json(res); }));
app.MapGet("/v1/games/{game}/mail", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.MailOf(id))));
// 내 용병이 고용된 전투 녹화 (R-120)
app.MapGet("/v1/games/{game}/hire/replays/{id}", (string game, string id, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (acct, g) => { var res = g.HireReplay(acct, id); return res == null ? Results.NotFound() : Json(res); }));
// 푸시 알림 설정 · 기기 토큰 (R-119)
app.MapGet("/v1/games/{game}/push/settings", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.PushSettingsOf(id))));
app.MapPost("/v1/games/{game}/push/settings", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<PushSettings>(req);
    return InGame(req, p, r, game, Now(), (id, g) => Json(g.SetPushSettings(id, body)));
});
app.MapPost("/v1/games/{game}/push/token", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<PushTokenRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) => g.RegisterPushToken(id, body) ? Results.Ok() : Results.StatusCode(StatusCodes.Status422UnprocessableEntity));
});
// 출석 (R-112): 하루 한 번, 서버 시각. 보상은 우편
app.MapPost("/v1/games/{game}/attendance", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.CheckIn(id))));

// 영토 (R-46)
app.MapGet("/v1/games/{game}/territory", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) =>
    {
        // 지도 보기: ?at=위도,경도&r=반경(m)이면 주변만 (db.md 8.1), 없으면 전체 (예전 앱)
        string at = req.Query.TryGetValue("at", out var a) ? a.ToString() : null;
        long.TryParse(req.Query.TryGetValue("r", out var rv) ? rv.ToString() : null, out long radius);
        return Json(g.TerritoryAround(id, string.IsNullOrEmpty(at) ? null : at, radius));
    }));
app.MapPost("/v1/games/{game}/territory/defense", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<DefenseRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var res = g.RegisterDefense(id, body);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);
    });
});
app.MapPost("/v1/games/{game}/territory/plant", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<PlantRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var res = g.PlantFlag(id, body);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);
    });
});
// 공성전 준비 1분 · 취소 (R-113)
app.MapPost("/v1/games/{game}/territory/siege", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<SiegeRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var res = g.StartSiege(id, body);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);
    });
});
app.MapPost("/v1/games/{game}/territory/siege/cancel", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<SiegeRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) => g.CancelSiege(id, body?.FlagKey) ? Results.Ok() : Results.StatusCode(StatusCodes.Status422UnprocessableEntity));
});
app.MapPost("/v1/games/{game}/territory/recall", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<RecallRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var res = g.RecallFlag(id, body);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);
    });
});
// 연합 (R-49)
app.MapGet("/v1/games/{game}/alliances", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.Alliances(id))));
app.MapPost("/v1/games/{game}/alliances", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<AllianceRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) => AllianceResult(g.CreateAlliance(id, body)));
});
app.MapPost("/v1/games/{game}/alliances/leave", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => AllianceResult(g.LeaveAlliance(id))));
app.MapPost("/v1/games/{game}/alliances/manage", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<AllianceManageRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) => AllianceResult(g.ManageAlliance(id, body)));
});
app.MapPost("/v1/games/{game}/alliances/{alliance}/join", (string game, string alliance, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => AllianceResult(g.JoinAlliance(id, alliance))));
app.MapPost("/v1/games/{game}/territory/collect", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    // 몸이 있으면 그 지역 · 깃발만 (지도에서 깃발마다 받기), 없으면 모두
    CollectRequest body = null;
    if (req.ContentLength > 0) { try { body = await Body<CollectRequest>(req); } catch (Exception) { body = null; } }
    return InGame(req, p, r, game, Now(), (id, g) => Json(g.CollectTerritory(id, body?.RegionId)));
});

// 관리자 페이지 (docs/impl/admin.md): /unitygame1.html (wwwroot) + /v1/admin/* (Game:AdminKey + 마스터 로그인)
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-store" });
AdminRoutes.Map(app);

app.Run();
return 0;

static IResult AllianceResult(AllianceResponse res) => res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);

static IResult Json(object o, int status = StatusCodes.Status200OK) =>
    Results.Content(OnlineJson.Write(o), "application/json", null, status);

static async Task<T> Body<T>(HttpRequest req) where T : class
{
    using var reader = new StreamReader(req.Body);
    var text = await reader.ReadToEndAsync();
    try { return OnlineJson.Read<T>(text); } catch (Exception) { return null; }
}

static IResult Authed(HttpRequest req, IPlatformStore p, Func<string, IResult> handle)
{
    string auth = req.Headers.Authorization.ToString();
    string token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth.Substring(7) : null;
    var id = p.AccountOf(token);
    return id == null ? Results.Unauthorized() : handle(id);
}

/// 게임 요청: 인증 → 게임 찾기(없으면 404) → 처음이면 가입 기록 → 처리.
static IResult InGame(HttpRequest req, IPlatformStore p, GameRegistry r, string code, long now, Func<string, IGameModule, IResult> handle) =>
    Authed(req, p, id =>
    {
        var g = r.Find(code);
        if (g == null) return Results.NotFound();
        p.Join(id, g.Code, now);
        return handle(id, g);
    });

namespace Game.Server
{
    public static class ServerSetup
    {
        public const string DefaultGame = "ancientwar";

        /// <summary>설정의 게임 목록으로 등록표를 만든다. 비어 있으면 이 저장소의 게임 하나.</summary>
        /// <summary>저장소 고르기 (db.md): Game:Store = memory · folder · firestore. 없으면 예전처럼 Game:StoreRoot가 있으면 folder, 아니면 memory.</summary>
        public static IStore Store(IConfiguration config)
        {
            string root = config["Game:StoreRoot"];
            string kind = config["Game:Store"];
            if (string.IsNullOrWhiteSpace(kind)) kind = string.IsNullOrEmpty(root) ? "memory" : "folder";
            switch (kind.Trim())
            {
                case "memory": return new InMemoryStore();
                case "folder": return new FolderStore(string.IsNullOrEmpty(root) ? "store" : root);
                case "firestore":
                    {
                        string project = config["Game:Firestore:ProjectId"];
                        if (string.IsNullOrWhiteSpace(project)) project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
                        if (string.IsNullOrWhiteSpace(project)) throw new InvalidOperationException("Game:Store=firestore에는 Game:Firestore:ProjectId가 필요합니다.");
                        return FirestoreStore.Create(project, config["Game:Firestore:DatabaseId"]);
                    }
                default: throw new InvalidOperationException($"Game:Store '{kind}'를 모릅니다 (memory · folder · firestore).");
            }
        }

        public static GameRegistry Registry(IConfiguration config, ITimeProvider time, IGameStore store, MasterAccounts masters = null)
        {
            var registry = new GameRegistry();
            var games = config.GetSection("Game:Games").GetChildren();
            bool any = false;
            foreach (var g in games)
            {
                any = true;
                string code = g["Code"];
                var packages = new List<string>();
                foreach (var pkg in g.GetSection("PackageIds").GetChildren()) packages.Add(pkg.Value);
                registry.Register(new StrategyGameModule(code, packages, new FileDataSource(ServerPaths.DataRoot(g["DataRoot"])), g["Theme"] ?? code, time, store));
            }
            if (!any)
                registry.Register(new StrategyGameModule(DefaultGame, new[] { "net.itgold.unitygame1" },
                    new FileDataSource(ServerPaths.DataRoot(config["Game:DataRoot"])), DefaultGame, time, store));
            var receipts = ReceiptVerifiers.FromConfig(config, time);
            string receiptId = ReceiptVerifiers.IdOf(config);
            bool allowOverride = string.Equals(config["Game:AllowAdminReceiptOverride"], "true", StringComparison.OrdinalIgnoreCase); // R-124, 기본 끔
            var push = PushSenders.FromConfig(config, time);
            foreach (var code in registry.Codes)
                if (registry.Find(code) is StrategyGameModule m)
                {
                    m.Receipts = receipts; m.ReceiptVerifierId = receiptId; m.AllowReceiptOverride = allowOverride; m.PushSender = push;
                    if (masters != null) m.Masters = masters;
                }
            return registry;
        }
    }

    /// <summary>
    /// 마스터 계정 목록 (R-86). 항목은 계정 id(acct-…) 또는 이메일("@"가 있으면 이메일, 대소문자 무시).
    /// 이메일은 구글(Firebase) 로그인 때 확인된 이메일이 남은 계정에만 맞는다 (<see cref="EmailOf"/>).
    /// 게스트(Unity 에디터 등)는 이메일이 없으니 계정 id로 적는다. 실행 중에 더할 수 있다 (테스트·운영 도구).
    /// </summary>
    public sealed class MasterAccounts
    {
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        /// <summary>계정 id → 확인된 이메일 (없으면 null). 정하지 않으면 이메일 항목은 맞지 않는다.</summary>
        public Func<string, string> EmailOf { get; set; }
        /// <summary>계정 id → 저장소에 정한 등급 (H8, 없으면 null). 정하지 않으면 설정 마스터만.</summary>
        public Func<string, string> StoredRole { get; set; }

        /// <summary>
        /// 관리자인가 (H8): 설정 마스터(처음 관리자) 또는 저장소 등급 admin. 개발 행동(R-86) · /me IsMaster · 관리자 페이지 전부.
        /// </summary>
        public bool Contains(string accountId) => IsFixed(accountId) || (accountId != null && StoredRole?.Invoke(accountId) == AccountRoles.Admin);

        /// <summary>계정의 등급: 설정 마스터면 admin, 아니면 저장소 값 (일반 = null).</summary>
        public string RoleOf(string accountId)
        {
            if (accountId == null) return null;
            if (IsFixed(accountId)) return AccountRoles.Admin;
            return StoredRole?.Invoke(accountId);
        }

        public static MasterAccounts Parse(string csv)
        {
            var m = new MasterAccounts();
            if (!string.IsNullOrWhiteSpace(csv))
                foreach (var part in csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) m.Add(part.Trim());
            return m;
        }

        /// <summary>계정 id 또는 이메일을 더한다.</summary>
        public void Add(string idOrEmail)
        {
            if (string.IsNullOrEmpty(idOrEmail)) return;
            lock (_lock)
            {
                if (idOrEmail.Contains('@')) _emails.Add(idOrEmail.Trim());
                else _ids.Add(idOrEmail);
            }
        }

        /// <summary>설정 Game:Masters에 있는 처음 관리자 (등급을 바꿀 수 없음).</summary>
        public bool IsFixed(string accountId)
        {
            if (accountId == null) return false;
            lock (_lock)
            {
                if (_ids.Contains(accountId)) return true;
                if (_emails.Count == 0) return false;
            }
            string email = EmailOf?.Invoke(accountId);
            if (string.IsNullOrEmpty(email)) return false;
            lock (_lock) return _emails.Contains(email);
        }
    }

    public static class ServerPaths
    {
        /// <summary>데이터 폴더: 설정값, 없으면 실행 위치에서 위로 올라가며 data/Steps가 있는 곳.</summary>
        public static string DataRoot(string configured)
        {
            if (!string.IsNullOrEmpty(configured)) return configured;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "data");
                if (Directory.Exists(Path.Combine(candidate, "Steps"))) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("data 폴더를 찾지 못했습니다. Game:DataRoot를 설정하세요.");
        }
    }
}

public partial class Program { }
