using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Game.Economy;
using Game.Foundation.Time;
using Game.Online;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Server
{
    /// <summary>
    /// 관리자 페이지 (docs/impl/admin.md, 2026-11-16 사용자): /unitygame1.html + /v1/admin/*.
    /// 들어가려면: ① 관리자 키 (설정 Game:AdminKey, Cloud Run 비밀값 — 저장소에 적지 않는다) 헤더 X-Admin-Key,
    /// ② 마스터 계정 (Game:Masters, 구글 로그인) 토큰 — 설정 Game:AdminGoogle이 "off"면 ②는 묻지 않는다 (개발 중, 2026-11-16 사용자: 출시 전에 켬).
    /// 키 설정이 없으면 관리자 주소는 모두 404 (꺼짐).
    /// </summary>
    public static class AdminRoutes
    {
        public const string KeyHeader = "X-Admin-Key";
        public const string Prefix = "/v1/admin";
        /// <summary>한 번에 보낼 수 있는 자원 상한 (실수로 0을 더 붙이는 것 막기).</summary>
        public const long MaxAmount = 100_000_000;
        /// <summary>구글 확인 없이 키로만 들어온 관리자.</summary>
        public const string KeyOnlyAdmin = "admin-key";

        public static void Map(WebApplication app)
        {
            var time = app.Services.GetRequiredService<ITimeProvider>();
            long Now() => time.UtcNow.ToUnixTimeSeconds();
            string key = app.Configuration["Game:AdminKey"];
            bool google = !string.Equals(app.Configuration["Game:AdminGoogle"], "off", StringComparison.OrdinalIgnoreCase);

            // 키 확인만 (페이지 첫 칸): 맞으면 200 + 구글 로그인이 필요한지, 아니면 404
            app.MapPost(Prefix + "/key", (HttpRequest req) => KeyOk(req, key) ? Json(new AdminKeyResponse { Google = google }) : Results.NotFound());
            // 관리자만 (H8): 설정 · 운영 켜고 끄기 · 등급 지정 · 푸시 등
            IResult Guard(HttpRequest req, IStore store, MasterAccounts masters, Func<string, IResult> handle) => GuardWith(req, key, google, store, masters, handle, false);
            // 서브 관리자도 (H8): 보기 · 우편 · 공지(버전 · 점검)
            IResult GuardSub(HttpRequest req, IStore store, MasterAccounts masters, Func<string, IResult> handle) => GuardWith(req, key, google, store, masters, handle, true);

            app.MapGet(Prefix + "/status", (HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters, IServiceProvider sp) =>
                GuardSub(req, store, masters, id => Json(new AdminStatus
                {
                    NowUnixSeconds = Now(),
                    Revision = Environment.GetEnvironmentVariable("K_REVISION"),
                    Games = new List<string>(r.Codes),
                    Accounts = store.AdminAccounts().Count,
                    Store = store.Kind,
                    Masters = app.Configuration["Game:Masters"],
                    ReceiptVerifier = app.Configuration["Game:ReceiptVerifier"] ?? (string.Equals(app.Configuration["Game:AllowFakeReceipts"], "false", StringComparison.OrdinalIgnoreCase) ? "off" : "fake"),
                    FirebaseProjectId = app.Configuration["Game:Firebase:ProjectId"],
                    You = id,
                    YourRole = id == KeyOnlyAdmin ? AccountRoles.Admin : masters.RoleOf(id),
                })));

            // 버전 · 점검 안내 바꾸기 (R-118): 몸 = AdminStatusNotice. Clear면 지우고 서버 설정으로. 바꾼 뒤 지금 /v1/status 값을 돌려준다.
            var statusFromConfig = ServerStatus.FromConfig(app.Configuration);
            app.MapGet(Prefix + "/notice", (HttpRequest req, IStore store, MasterAccounts masters) =>
                GuardSub(req, store, masters, _ => Json(new AdminStatusNotice { Current = ServerStatus.Current(statusFromConfig, store.StatusOverride(), Now()), Override = store.StatusOverride() })));
            app.MapPost(Prefix + "/notice", async (HttpRequest req, IStore store, MasterAccounts masters) =>
            {
                var body = await Body<AdminStatusNotice>(req);
                return GuardSub(req, store, masters, _ =>
                {
                    if (body == null) return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
                    store.SetStatusOverride(body.Clear ? null : body.Override);
                    return Json(new AdminStatusNotice { Current = ServerStatus.Current(statusFromConfig, store.StatusOverride(), Now()), Override = store.StatusOverride() });
                });
            });

            // 모집 배너 켜고 끄기 (R-122): 배포 없이. 꺼진 배너는 목록에서 빠지고 서버가 모집을 거절한다.
            app.MapGet(Prefix + "/games/{game}/banners", (string game, HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters) =>
                Guard(req, store, masters, _ => r.Find(game) is IGameModule g ? Json(new AdminBannerList { Banners = g.AdminBanners() }) : Results.NotFound()));
            app.MapPost(Prefix + "/games/{game}/banners", async (string game, HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters) =>
            {
                var body = await Body<AdminBannersRequest>(req);
                return Guard(req, store, masters, _ =>
                {
                    if (!(r.Find(game) is IGameModule g)) return Results.NotFound();
                    if (body == null) return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
                    return Json(new AdminBannerList { Banners = g.SetBanners(body.Items) });
                });
            });

            // 상점 상품 표출 (R-123): 배포 없이. 꺼진 상품은 상점 목록에서 빠지고 서버가 구매를 거절한다 (이미 산 영구 상품의 복원은 된다).
            app.MapGet(Prefix + "/games/{game}/products", (string game, HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters) =>
                Guard(req, store, masters, _ => r.Find(game) is IGameModule g ? Json(new AdminProductList { Products = g.AdminProducts() }) : Results.NotFound()));
            app.MapPost(Prefix + "/games/{game}/products", async (string game, HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters) =>
            {
                var body = await Body<AdminBannersRequest>(req);
                return Guard(req, store, masters, _ =>
                {
                    if (!(r.Find(game) is IGameModule g)) return Results.NotFound();
                    if (body == null) return Results.StatusCode(StatusCodes.Status422UnprocessableEntity);
                    return Json(new AdminProductList { Products = g.SetProducts(body.Items) });
                });
            });

            app.MapGet(Prefix + "/accounts", (HttpRequest req, IStore store, MasterAccounts masters) =>
                GuardSub(req, store, masters, _ =>
                {
                    var list = store.AdminAccounts();
                    foreach (var a in list) if (masters.IsFixed(a.AccountId)) { a.Fixed = true; a.Role = AccountRoles.Admin; }
                    return Json(new AdminAccountList { Accounts = list });
                }));

            // 계정 등급 정하기 (H8): 관리자만. 몸 = { Role: "admin" | "subAdmin" | null(일반) }. 설정 마스터(처음 관리자)는 바꿀 수 없다 (422 fixed).
            app.MapPost(Prefix + "/accounts/{account}/role", async (string account, HttpRequest req, IStore store, MasterAccounts masters) =>
            {
                var body = await Body<AdminRoleRequest>(req);
                return Guard(req, store, masters, _ =>
                {
                    string role = string.IsNullOrEmpty(body?.Role) ? null : body.Role;
                    if (body == null || !AccountRoles.IsValid(role)) return Json(new AdminRoleResponse { Reason = "badRole" }, StatusCodes.Status422UnprocessableEntity);
                    if (masters.IsFixed(account)) return Json(new AdminRoleResponse { Reason = "fixed", Role = AccountRoles.Admin }, StatusCodes.Status422UnprocessableEntity);
                    if (!store.SetRole(account, role)) return Json(new AdminRoleResponse { Reason = "noAccount" }, StatusCodes.Status422UnprocessableEntity);
                    return Json(new AdminRoleResponse { Ok = true, Reason = "ok", Role = role });
                });
            });

            app.MapGet(Prefix + "/games/{game}/accounts/{account}", (string game, string account, HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters) =>
                GuardSub(req, store, masters, _ =>
                {
                    if (r.Find(game) == null) return Results.NotFound();
                    var save = store.GetSave(game, account);
                    var detail = new AdminAccountDetail { AccountId = account, Mail = store.MailOf(game, account) };
                    if (save != null)
                    {
                        detail.Revision = save.Revision;
                        detail.SavedAtUnixSeconds = save.SavedAtUnixSeconds;
                        detail.SaveJson = save.Json;
                        try
                        {
                            var s = Game.Flow.SaveJson.Read(save.Json);
                            detail.Wallet = s.Wallet;
                            detail.LordLevel = s.LordLevel;
                            detail.ClearedSteps = s.ClearedSteps.Count;
                            detail.Generals = s.Generals.Count;
                            detail.Units = s.Units.Count;
                        }
                        catch (Exception) { /* 읽지 못하는 세이브는 원문만 */ }
                    }
                    return Json(detail);
                }));

            // 우편 보내기: 계정 목록 (비면 그 게임의 모든 계정)에 자원 우편. 받는 사람이 [받기]로 지갑에 (mail.claim, R-41)
            app.MapPost(Prefix + "/games/{game}/mail", async (string game, HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters) =>
            {
                var body = await Body<AdminMailRequest>(req);
                return GuardSub(req, store, masters, _ =>
                {
                    if (r.Find(game) == null) return Results.NotFound();
                    if (body == null || !IsResource(body.Resource) || body.Amount <= 0 || body.Amount > MaxAmount)
                        return Json(new AdminMailResponse { Reason = "badRequest" }, StatusCodes.Status422UnprocessableEntity);
                    var targets = new List<string>();
                    if (body.AccountIds == null || body.AccountIds.Count == 0)
                    {
                        foreach (var a in store.AdminAccounts()) if (a.Games.ContainsKey(game)) targets.Add(a.AccountId);
                    }
                    else
                    {
                        var known = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var a in store.AdminAccounts()) known.Add(a.AccountId);
                        foreach (var id in body.AccountIds) if (known.Contains(id)) targets.Add(id);
                    }
                    foreach (var id in targets)
                        store.AddMail(game, id, new Mail { Kind = MailKinds.AdminGift, Resource = body.Resource, Amount = body.Amount, Note = Trim(body.Note), CreatedAtUnixSeconds = Now() });
                    return Json(new AdminMailResponse { Ok = targets.Count > 0, Reason = targets.Count > 0 ? "ok" : "noTarget", Sent = targets.Count });
                });
            });

            // 수입 가득 알림 보내기 (R-119): 관리자 페이지 또는 예약(Cloud Scheduler, 한 시간마다)이 부른다. 보낸 기기 수.
            app.MapPost(Prefix + "/games/{game}/push/sweep", (string game, HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters) =>
                Guard(req, store, masters, _ => r.Find(game) is IGameModule g ? Json(new AdminPushSweep { Sent = g.SweepIncomeFull() }) : Results.NotFound()));

            app.MapGet(Prefix + "/games/{game}/territory", (string game, HttpRequest req, IStore store, GameRegistry r, MasterAccounts masters) =>
                GuardSub(req, store, masters, _ =>
                {
                    if (r.Find(game) == null) return Results.NotFound();
                    var res = new AdminTerritory();
                    store.WithTerritory(game, TerritoryScope.All(), owners => { foreach (var o in owners.Values) res.Owners.Add(OnlineJson.Read<TerritoryOwner>(OnlineJson.Write(o))); return 0; });
                    store.WithAlliances(game, all => { foreach (var a in all.Values) res.Alliances.Add(OnlineJson.Read<Alliance>(OnlineJson.Write(a))); return 0; });
                    res.Owners.Sort((a, b) => string.CompareOrdinal(a.RegionId, b.RegionId));
                    res.Alliances.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                    return Json(res);
                }));
        }

        /// <summary>키 설정이 없으면 언제나 거절. 같은 길이 비교 (시간으로 키를 알아내지 못하게).</summary>
        public static bool KeyOk(HttpRequest req, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string given = req.Headers[KeyHeader].ToString();
            var a = SHA256.HashData(Encoding.UTF8.GetBytes(given ?? ""));
            var b = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>키 → 404, (구글 확인이 켜져 있으면) 토큰 없음 → 401, 마스터 아님 → 403, 통과면 처리. 구글 확인이 꺼져 있으면 처리 계정 = "admin-key".</summary>
        private static IResult GuardWith(HttpRequest req, string key, bool google, IStore store, MasterAccounts masters, Func<string, IResult> handle, bool subOk)
        {
            if (!KeyOk(req, key)) return Results.NotFound();
            if (!google) return handle(KeyOnlyAdmin);
            string auth = req.Headers.Authorization.ToString();
            string token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth.Substring(7) : null;
            var id = store.AccountOf(token);
            if (id == null) return Results.Unauthorized();
            if (masters.Contains(id) || (subOk && masters.RoleOf(id) == AccountRoles.SubAdmin)) return handle(id);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        private static bool IsResource(string r) => r == ResourceIds.Supply || r == ResourceIds.Gold || r == ResourceIds.Gem;

        private static string Trim(string note)
        {
            if (string.IsNullOrWhiteSpace(note)) return null;
            note = note.Trim();
            return note.Length > 60 ? note.Substring(0, 60) : note;
        }

        private static IResult Json(object o, int status = StatusCodes.Status200OK) =>
            Results.Content(OnlineJson.Write(o), "application/json", null, status);

        private static async Task<T> Body<T>(HttpRequest req) where T : class
        {
            using var reader = new StreamReader(req.Body);
            var text = await reader.ReadToEndAsync();
            try { return OnlineJson.Read<T>(text); } catch (Exception) { return null; }
        }
    }

    public sealed class AdminKeyResponse
    {
        /// <summary>true = 다음에 마스터 구글 로그인이 필요 (Game:AdminGoogle이 off가 아님).</summary>
        public bool Google { get; set; }
    }

    public sealed class AdminAccount
    {
        public string AccountId { get; set; }
        public string Nickname { get; set; }
        public string Email { get; set; }
        public bool Linked { get; set; }
        /// <summary>게임 코드 → 가입 시각.</summary>
        public Dictionary<string, long> Games { get; set; } = new Dictionary<string, long>();
        /// <summary>삭제 요청 시각 (R-102, 0 = 없음).</summary>
        public long DeletionRequestedAtUnixSeconds { get; set; }
        /// <summary>저장소에 정한 등급 (H8, 일반 = null). 설정 마스터면 관리자 페이지가 Fixed로 따로 받는다.</summary>
        public string Role { get; set; }
        /// <summary>설정 Game:Masters의 처음 관리자 (등급을 바꿀 수 없음).</summary>
        public bool Fixed { get; set; }
    }

    /// <summary>계정 등급 정하기 (H8). Role = admin · subAdmin · null/빈 글(일반).</summary>
    public sealed class AdminRoleRequest
    {
        public string Role { get; set; }
    }

    public sealed class AdminRoleResponse
    {
        public bool Ok { get; set; }
        /// <summary>ok · badRole · fixed(설정 마스터) · noAccount.</summary>
        public string Reason { get; set; }
        public string Role { get; set; }
    }

    public sealed class AdminAccountList
    {
        public List<AdminAccount> Accounts { get; set; } = new List<AdminAccount>();
    }

    public sealed class AdminAccountDetail
    {
        public string AccountId { get; set; }
        public long Revision { get; set; }
        public long SavedAtUnixSeconds { get; set; }
        public Dictionary<string, long> Wallet { get; set; }
        public int LordLevel { get; set; }
        public int ClearedSteps { get; set; }
        public int Generals { get; set; }
        public int Units { get; set; }
        public List<Mail> Mail { get; set; } = new List<Mail>();
        public string SaveJson { get; set; }
    }

    public sealed class AdminPushSweep
    {
        public int Sent { get; set; }
    }

    /// <summary>버전 · 점검 안내 (R-118): Override = 관리자 값 (버전 칸은 비우면 설정 값, Maintenance는 통째), Clear = 지우기. 응답의 Current = 지금 /v1/status.</summary>
    /// <summary>관리자 모집 배너 한 줄 (R-122): 데이터 기간 · 관리자 덮어쓰기 · 지금 열림.</summary>
    public sealed class AdminBanner
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public long DataStartsAtUnixSeconds { get; set; }
        public long DataEndsAtUnixSeconds { get; set; }
        public bool Off { get; set; }
        /// <summary>덮어쓴 기간 (null = 데이터 그대로).</summary>
        public long? StartsAtUnixSeconds { get; set; }
        public long? EndsAtUnixSeconds { get; set; }
        public bool OpenNow { get; set; }
    }

    /// <summary>배너 켜고 끄기 저장 (R-122): 몸 = 배너마다 Id · Off · 기간 덮어쓰기. 적지 않은 배너 = 데이터 그대로.</summary>
    public sealed class AdminBannersRequest
    {
        public List<ContentToggle> Items { get; set; } = new List<ContentToggle>();
    }

    /// <summary>관리자 상점 상품 한 줄 (R-123). Kind = 상품 종류 (치장은 "cosmetic:armor" 처럼), Price = 보이는 가격 글.</summary>
    public sealed class AdminProduct
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Price { get; set; }
        /// <summary>데이터 기본 끔 (새로 등록한 상품).</summary>
        public bool DefaultOff { get; set; }
        /// <summary>지금 상점에 보이고 살 수 있음.</summary>
        public bool Shown { get; set; }
        /// <summary>관리자가 데이터 기본값과 다르게 정함.</summary>
        public bool Overridden { get; set; }
    }

    public sealed class AdminProductList
    {
        public List<AdminProduct> Products { get; set; } = new List<AdminProduct>();
    }

    public sealed class AdminBannerList
    {
        public List<AdminBanner> Banners { get; set; } = new List<AdminBanner>();
    }

    public sealed class AdminStatusNotice
    {
        public ServerStatusResponse Override { get; set; }
        public bool Clear { get; set; }
        public ServerStatusResponse Current { get; set; }
    }

    public sealed class AdminMailRequest
    {
        /// <summary>받을 계정 (비면 그 게임에 들어온 모든 계정).</summary>
        public List<string> AccountIds { get; set; }
        public string Resource { get; set; }
        public long Amount { get; set; }
        /// <summary>우편에 보일 짧은 말 (60자까지).</summary>
        public string Note { get; set; }
    }

    public sealed class AdminMailResponse
    {
        public bool Ok { get; set; }
        public string Reason { get; set; }
        public int Sent { get; set; }
    }

    public sealed class AdminTerritory
    {
        public List<TerritoryOwner> Owners { get; set; } = new List<TerritoryOwner>();
        public List<Alliance> Alliances { get; set; } = new List<Alliance>();
    }

    public sealed class AdminStatus
    {
        public long NowUnixSeconds { get; set; }
        public string Revision { get; set; }
        public List<string> Games { get; set; } = new List<string>();
        public int Accounts { get; set; }
        public string Store { get; set; }
        public string Masters { get; set; }
        public string ReceiptVerifier { get; set; }
        public string FirebaseProjectId { get; set; }
        public string You { get; set; }
        /// <summary>들어온 사람의 등급 (H8): admin · subAdmin. 키만으로 들어오면(구글 확인 꺼짐) admin.</summary>
        public string YourRole { get; set; }
    }
}
