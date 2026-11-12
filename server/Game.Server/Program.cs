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
// 설정: Game:StoreRoot (없으면 메모리), Game:Games:0:Code / DataRoot / Theme / PackageIds:0 … (없으면 ancientwar 하나)
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
builder.Services.AddSingleton(sp =>
{
    var root = builder.Configuration["Game:StoreRoot"];
    return string.IsNullOrEmpty(root) ? new InMemoryStore() : new FolderStore(root);
});
builder.Services.AddSingleton<IPlatformStore>(sp => sp.GetRequiredService<InMemoryStore>());
builder.Services.AddSingleton<IGameStore>(sp => sp.GetRequiredService<InMemoryStore>());
// Firebase 로그인 (docs/impl/deploy-cloudrun.md): Game:Firebase:ProjectId가 있을 때만
builder.Services.AddSingleton<IExternalTokenVerifier>(sp =>
{
    var project = builder.Configuration["Game:Firebase:ProjectId"];
    return string.IsNullOrEmpty(project) ? null : new FirebaseTokenVerifier(project, FirebaseTokenVerifier.GoogleCerts(new System.Net.Http.HttpClient()));
});
builder.Services.AddSingleton(sp => ServerSetup.Registry(builder.Configuration, sp.GetRequiredService<ITimeProvider>(), sp.GetRequiredService<IGameStore>()));

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
    return who == null ? Results.Unauthorized() : Json(p.SignInExternal(who.Provider, who.Subject));
});
app.MapGet(OnlineRoutes.Time, () => Json(new ServerTimeResponse { UnixSeconds = Now() }));
app.MapGet(OnlineRoutes.Games, (GameRegistry r) => Json(new GameListResponse { Games = new List<string>(r.Codes) }));
app.MapGet("/v1/games/resolve", (string app, GameRegistry r) =>
{
    var code = r.CodeOfPackage(app);
    return code == null ? Results.NotFound() : Json(new ResolveAppResponse { Game = code });
});
// 계정 (공통): 닉네임
app.MapGet(OnlineRoutes.Me, (HttpRequest req, IPlatformStore p) =>
    Authed(req, p, id => Json(new ProfileResponse { AccountId = id, Nickname = p.NicknameOf(id) })));
app.MapPost(OnlineRoutes.MyNickname, async (HttpRequest req, IPlatformStore p) =>
{
    var body = await Body<NicknameRequest>(req);
    return Authed(req, p, id =>
    {
        var res = p.SetNickname(id, body?.Nickname);
        return res.Ok ? Json(res) : Json(res, StatusCodes.Status422UnprocessableEntity);
    });
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

// 영토 (R-46)
app.MapGet("/v1/games/{game}/territory", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.Territory(id))));
app.MapPost("/v1/games/{game}/territory/defense", async (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
{
    var body = await Body<DefenseRequest>(req);
    return InGame(req, p, r, game, Now(), (id, g) =>
    {
        var res = g.RegisterDefense(id, body);
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
app.MapPost("/v1/games/{game}/territory/collect", (string game, HttpRequest req, IPlatformStore p, GameRegistry r) =>
    InGame(req, p, r, game, Now(), (id, g) => Json(g.CollectTerritory(id))));

app.Run();

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
        public static GameRegistry Registry(IConfiguration config, ITimeProvider time, IGameStore store)
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
            bool allowFake = !string.Equals(config["Game:AllowFakeReceipts"], "false", StringComparison.OrdinalIgnoreCase);
            foreach (var code in registry.Codes)
                if (registry.Find(code) is StrategyGameModule m) m.AllowFakeReceipts = allowFake;
            return registry;
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
