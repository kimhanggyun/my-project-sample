using System;
using System.IO;
using System.Threading.Tasks;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Time;
using Game.Online;
using Game.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// 참고 서버 (docs/impl/server.md): 개발·테스트용. 게스트 계정, 클라우드 세이브, 서버 시각, 전투 검증·반영.
// 실행: dotnet run --project server/Game.Server  (설정: Game:DataRoot, Game:StoreRoot)
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
builder.Services.AddSingleton<IDataSource>(sp => new FileDataSource(ServerPaths.DataRoot(builder.Configuration["Game:DataRoot"])));
builder.Services.AddSingleton(sp => DefinitionSet.Load(sp.GetRequiredService<IDataSource>(), "ancientwar"));
builder.Services.AddSingleton<IAccountStore>(sp =>
{
    var root = builder.Configuration["Game:StoreRoot"];
    return string.IsNullOrEmpty(root) ? new InMemoryAccountStore() : new FolderAccountStore(root);
});
builder.Services.AddSingleton<GameService>();

var app = builder.Build();

app.MapPost("/v1/auth/guest", (GameService g) => Json(g.Store.CreateGuest()));
app.MapGet("/v1/time", (GameService g) => Json(new ServerTimeResponse { UnixSeconds = g.Now }));
app.MapGet("/v1/save", (HttpRequest req, GameService g) =>
    Authed(req, g, id => Json(g.LoadOrCreate(id))));
app.MapPut("/v1/save", async (HttpRequest req, GameService g) =>
{
    var body = await Body<SavePutRequest>(req);
    return Authed(req, g, id =>
    {
        var r = g.Put(id, body);
        return r.Ok ? Json(r) : Json(r, StatusCodes.Status409Conflict);
    });
});
app.MapPost("/v1/battles", async (HttpRequest req, GameService g) =>
{
    var body = await Body<BattleSubmission>(req);
    return Authed(req, g, id =>
    {
        var v = g.SubmitBattle(id, body);
        return v.Valid ? Json(v) : Json(v, v.Reason == VerdictReasons.StaleSave ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity);
    });
});

app.Run();

static IResult Json(object o, int status = StatusCodes.Status200OK) =>
    Results.Content(OnlineJson.Write(o), "application/json", null, status);

static async Task<T> Body<T>(HttpRequest req) where T : class
{
    using var reader = new StreamReader(req.Body);
    var text = await reader.ReadToEndAsync();
    try { return OnlineJson.Read<T>(text); } catch (Exception) { return null; }
}

static IResult Authed(HttpRequest req, GameService g, Func<string, IResult> handle)
{
    string auth = req.Headers.Authorization.ToString();
    string token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth.Substring(7) : null;
    var id = g.Store.AccountOf(token);
    return id == null ? Results.Unauthorized() : handle(id);
}

namespace Game.Server
{
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
