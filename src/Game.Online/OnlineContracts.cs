using System;
using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Foundation.Math;
using Newtonsoft.Json;

namespace Game.Online
{
    // 서버와 주고받는 메시지 (백엔드 종류와 무관, docs/impl/server.md). JSON은 OnlineJson.Settings로 (Fix는 원본 정수).

    /// <summary>게스트 로그인 결과. 토큰은 다음 요청의 Authorization: Bearer.</summary>
    public sealed class SignInResponse
    {
        public string AccountId { get; set; }
        public string Token { get; set; }
    }

    /// <summary>
    /// 서버 주소 (docs/impl/server.md 3장). 계정·시각은 공통, 세이브·전투는 게임 코드별 (여러 게임을 서버 한 곳에서, 4.5).
    /// </summary>
    public static class OnlineRoutes
    {
        public const string GuestSignIn = "/v1/auth/guest";
        public const string Time = "/v1/time";
        public const string Games = "/v1/games";
        public const string MyGames = "/v1/me/games";
        public static string ResolveApp(string appId) => "/v1/games/resolve?app=" + Uri.EscapeDataString(appId ?? "");
        public static string Save(string game) => "/v1/games/" + Uri.EscapeDataString(game) + "/save";
        public static string Battles(string game) => "/v1/games/" + Uri.EscapeDataString(game) + "/battles";
        public static string Actions(string game) => "/v1/games/" + Uri.EscapeDataString(game) + "/actions";
        /// <summary>고용 등록 목록 (GET ?kind=general|unit) / 등록 (POST).</summary>
        public static string HireListings(string game) => "/v1/games/" + Uri.EscapeDataString(game) + "/hire/listings";
        /// <summary>등록 하나 (DELETE = 해제).</summary>
        public static string HireListing(string game, string id) => HireListings(game) + "/" + Uri.EscapeDataString(id);
        /// <summary>받은 우편 (고용 보상 …). 받기는 행동 mail.claim.</summary>
        public static string Mail(string game) => "/v1/games/" + Uri.EscapeDataString(game) + "/mail";
        /// <summary>영토 주인 목록 (차지는 영토 전투 제출로, R-46).</summary>
        public static string Territory(string game) => "/v1/games/" + Uri.EscapeDataString(game) + "/territory";
        /// <summary>내 지역의 쌓인 수입을 우편으로 받기 (POST).</summary>
        public static string TerritoryCollect(string game) => Territory(game) + "/collect";
        /// <summary>내 지역에 수비 편성 등록 (POST, R-48).</summary>
        public static string TerritoryDefense(string game) => Territory(game) + "/defense";
        /// <summary>연합 목록 (GET) / 만들기 (POST, R-49).</summary>
        public static string Alliances(string game) => "/v1/games/" + Uri.EscapeDataString(game) + "/alliances";
        public static string AllianceJoin(string game, string id) => Alliances(game) + "/" + Uri.EscapeDataString(id) + "/join";
        public static string AllianceLeave(string game) => Alliances(game) + "/leave";
    }

    /// <summary>게임 코드 목록 (서버에 등록된 게임 / 내가 가입한 게임).</summary>
    public sealed class GameListResponse
    {
        public List<string> Games { get; set; } = new List<string>();
    }

    /// <summary>앱 id(package명·번들 id) → 게임 코드.</summary>
    public sealed class ResolveAppResponse
    {
        public string Game { get; set; }
    }

    /// <summary>서버 시각 (unix 초). 부상 회복·구출 기한 등 시간 규칙은 서버 시각을 쓴다.</summary>
    public sealed class ServerTimeResponse
    {
        public long UnixSeconds { get; set; }
    }

    /// <summary>
    /// 클라우드 세이브 한 벌. Revision은 저장할 때마다 1 오른다 (0 = 아직 없음).
    /// SavedAt = 이 세이브가 반영한 마지막 기록의 시각 (전투 출진 시각·행동 시각). 다음 기록은 이보다 이를 수 없다 (R-42).
    /// </summary>
    public sealed class CloudSave
    {
        public long Revision { get; set; }
        /// <summary>CampaignSaveService가 쓰는 세이브 JSON 그대로 (버전 포함).</summary>
        public string Json { get; set; }
        public long SavedAtUnixSeconds { get; set; }
    }

    /// <summary>
    /// 세이브 올리기 = 처음 연결 때 기기 진행 가져오기 전용 (R-41): 서버 세이브가 아직 새 게임(Revision 1)이고 내가 받은 Revision 위일 때만.
    /// 그 밖에는 충돌로 서버 사본을 돌려준다. 진행은 전투 제출(R-40)과 행동 제출(R-41)로만 바뀐다.
    /// </summary>
    public sealed class SavePutRequest
    {
        public long BaseRevision { get; set; }
        public string Json { get; set; }
    }

    public sealed class SavePutResponse
    {
        public bool Ok { get; set; }
        /// <summary>성공이면 새 세이브, 충돌이면 서버에 있는 세이브.</summary>
        public CloudSave Current { get; set; }
    }

    /// <summary>
    /// 전투 결과 제출 (verification R-40): 서버가 같은 입력(서버 세이브 + 출진 요청 + 시드 + 대결 선택 + 명령 기록)으로 다시 돌려
    /// 결과가 같으면 반영한다. 기기 결과를 그대로 믿지 않는다 (secrets.md 4장, hiring.md).
    /// </summary>
    public sealed class BattleSubmission
    {
        /// <summary>이 세이브 위에서 출진했다 (서버 세이브와 다르면 거절).</summary>
        public long BaseRevision { get; set; }
        public string StepId { get; set; }
        public ulong Seed { get; set; }
        public bool Delegate { get; set; }
        public List<string> DelegateGenerals { get; set; } = new List<string>();
        public long? Supply { get; set; }
        /// <summary>대결 id → 내가 고른 행동 기록.</summary>
        public Dictionary<string, List<string>> ContestChoices { get; set; } = new Dictionary<string, List<string>>();
        public List<BattleCommand> Commands { get; set; } = new List<BattleCommand>();
        /// <summary>기기가 계산한 상태 지문(16진)·틱 수.</summary>
        public string ClaimedHash { get; set; }
        public long ClaimedTicks { get; set; }
        /// <summary>출진 준비를 한 시각 (서버 시각 기준 unix 초, 0 = 지금). 서버는 이 시각으로 출진 준비·결과 반영을 다시 한다 (R-42, 오프라인 전투를 나중에 올릴 때).</summary>
        public long PlayedAtUnixSeconds { get; set; }
        /// <summary>고용한 장수·부대 (R-43). 서버는 등록 목록에서 같은 판(Version)의 스냅샷을 찾아 같은 출진을 만든다.</summary>
        public List<HireUse> Hires { get; set; } = new List<HireUse>();
        /// <summary>이 전투에 쓴 편성 (고용이 있으면 필수: 고용 장수·부대가 들어간 자리). 없으면 서버 세이브의 편성.</summary>
        public List<ActionCorps> Compositions { get; set; }
        /// <summary>영토 전투: 공격자 위치 "위도,경도" (서버가 공격할 수 있는 지역인지 본다, R-46).</summary>
        public string Location { get; set; }
        /// <summary>영토 전투: 상대한 수비 편성의 판 (0 = AI 수비군). 서버의 지금 판과 다르면 거절 (R-48).</summary>
        public long DefenseVersion { get; set; }
    }

    /// <summary>지역 하나의 주인 (다른 유저의 위치는 담지 않는다, territory.md 4장).</summary>
    public sealed class TerritoryOwner
    {
        public string RegionId { get; set; }
        public string OwnerAccountId { get; set; }
        public long SinceUnixSeconds { get; set; }
        /// <summary>마지막으로 수입을 받은 시각 (처음엔 차지한 시각).</summary>
        public long CollectedUnixSeconds { get; set; }
        /// <summary>요청한 계정이 주인일 때만: 지금 받을 수 있는 수입 (자원 id → 양).</summary>
        public Dictionary<string, long> Pending { get; set; }
        /// <summary>주인이 등록한 수비 편성 (없으면 null = AI 수비군, R-48). 공격하는 사람도 본다 (고용 등록처럼).</summary>
        public TerritoryDefense Defense { get; set; }
        /// <summary>주인이 든 연합 (없으면 null, R-49).</summary>
        public string OwnerAllianceId { get; set; }
        public string OwnerAllianceName { get; set; }
    }

    /// <summary>연합 하나 (territory.md 6장, R-49). 한 계정은 한 연합에만.</summary>
    public sealed class Alliance
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LeaderAccountId { get; set; }
        /// <summary>연합원 계정 (들어온 순서, 첫째가 처음 만든 사람).</summary>
        public List<string> Members { get; set; } = new List<string>();
        public long CreatedAtUnixSeconds { get; set; }
    }

    public sealed class AllianceListResponse
    {
        public List<Alliance> Alliances { get; set; } = new List<Alliance>();
        /// <summary>요청한 계정의 연합 id (없으면 null).</summary>
        public string MineId { get; set; }
        public int MemberMax { get; set; }
    }

    public sealed class AllianceRequest
    {
        public string Name { get; set; }
    }

    public static class AllianceReasons
    {
        public const string Ok = "ok";
        public const string BadName = "badName";
        public const string NameTaken = "nameTaken";
        public const string AlreadyMember = "alreadyMember";
        public const string Full = "full";
        public const string NotFound = "notFound";
        public const string NotMember = "notMember";
    }

    public sealed class AllianceResponse
    {
        public bool Ok { get; set; }
        /// <summary><see cref="AllianceReasons"/>.</summary>
        public string Reason { get; set; }
        /// <summary>지금 내 연합 (탈퇴했거나 없으면 null).</summary>
        public Alliance Alliance { get; set; }
    }

    /// <summary>수비 편성 스냅샷 (R-48): 등록할 때 주인의 서버 세이브에서 만든다. 다시 등록하면 Version이 오른다.</summary>
    public sealed class TerritoryDefense
    {
        public long Version { get; set; }
        public long RegisteredAtUnixSeconds { get; set; }
        public List<DefenseCorps> Corps { get; set; } = new List<DefenseCorps>();
    }

    public sealed class DefenseCorps
    {
        /// <summary>장수 상태 JSON (출전 가능 상태로).</summary>
        public string GeneralJson { get; set; }
        public string Formation { get; set; }
        public List<DefenseUnit> Units { get; set; } = new List<DefenseUnit>();
    }

    public sealed class DefenseUnit
    {
        public string Slot { get; set; }
        /// <summary>부대 상태 JSON (부상병 없이).</summary>
        public string UnitJson { get; set; }
    }

    /// <summary>수비 편성 등록: 내 지역에, 내 서버 세이브의 편성(상황, null = 기본)으로.</summary>
    public sealed class DefenseRequest
    {
        public string RegionId { get; set; }
        public string Situation { get; set; }
    }

    public sealed class DefenseResponse
    {
        public bool Ok { get; set; }
        /// <summary>ok / notOwner / badFormation.</summary>
        public string Reason { get; set; }
        public TerritoryDefense Defense { get; set; }
    }

    public sealed class TerritoryResponse
    {
        public List<TerritoryOwner> Owners { get; set; } = new List<TerritoryOwner>();
    }

    public static class HireKinds
    {
        public const string General = "general";
        public const string Unit = "unit";
    }

    /// <summary>
    /// 고용 등록 하나 (hiring.md 2·3장): 등록 시점의 스냅샷. 서버가 등록자의 서버 세이브에서 만든다 (기기가 보낸 값을 믿지 않는다).
    /// 같은 등록을 여러 유저가 동시에 고용해도 줄지 않는다 (R-26).
    /// </summary>
    public sealed class HireListing
    {
        public string Id { get; set; }
        /// <summary><see cref="HireKinds"/>.</summary>
        public string Kind { get; set; }
        public string OwnerAccountId { get; set; }
        /// <summary>등록자 세이브의 장수 id·부대 id (같은 대상을 다시 등록하면 갱신).</summary>
        public string SourceId { get; set; }
        /// <summary>스냅샷 판. 다시 등록(갱신)하면 오른다. 고용은 이 판을 가리킨다.</summary>
        public long Version { get; set; }
        /// <summary>스냅샷 JSON (장수 상태 또는 부대 상태).</summary>
        public string SnapshotJson { get; set; }
        /// <summary>장수: 1회 고용 가격, 부대: 1명당 가격.</summary>
        public long Price { get; set; }
        /// <summary>지불 자원: gold / supply (등록자가 고름).</summary>
        public string PayWith { get; set; }
        /// <summary>목록 표시용: 장수 정의 id·레벨 / 병종·등록 인원·훈련도(원본 정수).</summary>
        public string GeneralId { get; set; }
        public int Level { get; set; }
        public string TroopType { get; set; }
        public long Troops { get; set; }
        public long TrainingRaw { get; set; }
        public long CreatedAtUnixSeconds { get; set; }
        /// <summary>이력 (hiring.md 4장): 서버가 재현해 확정한 전투로만 쌓인다. 다시 등록해도 이어진다.</summary>
        public HireStats Stats { get; set; } = new HireStats();
        /// <summary>최근 전투 (새것이 앞, 개수는 balance hire.recentMax).</summary>
        public List<HireRecent> Recent { get; set; } = new List<HireRecent>();
    }

    /// <summary>고용 이력 집계.</summary>
    public sealed class HireStats
    {
        public long Hires { get; set; }
        public long Wins { get; set; }
        public long Losses { get; set; }
        public long Retreats { get; set; }
        public long Captured { get; set; }
        public long Destroyed { get; set; }
        /// <summary>고용된 전투에서 잃은 병력 합계 (장수는 그 군단 전체, 부대는 그 부대).</summary>
        public long TroopsLost { get; set; }
        /// <summary>고용된 병력 합계 (장수는 그 군단 시작 병력, 부대는 고용 인원).</summary>
        public long TroopsHired { get; set; }
    }

    public static class HireResults
    {
        public const string Win = "win";
        public const string Loss = "loss";
        public const string Retreat = "retreat";
        public const string Captured = "captured";
        public const string Destroyed = "destroyed";
    }

    /// <summary>최근 전투 하나.</summary>
    public sealed class HireRecent
    {
        public long AtUnixSeconds { get; set; }
        public string StepId { get; set; }
        /// <summary><see cref="HireResults"/>.</summary>
        public string Result { get; set; }
        public long TroopsStart { get; set; }
        public long TroopsLost { get; set; }
    }

    /// <summary>등록 요청: 내 서버 세이브의 장수 또는 부대를 이 가격으로.</summary>
    public sealed class HireListingRequest
    {
        public string Kind { get; set; }
        public string GeneralId { get; set; }
        public string UnitId { get; set; }
        public long Price { get; set; }
        public string PayWith { get; set; }
    }

    public static class HireReasons
    {
        public const string Ok = "ok";
        public const string NotOwned = "notOwned";
        public const string NotAvailable = "notAvailable";
        public const string BadPrice = "badPrice";
        public const string TooMany = "tooMany";
        public const string BadRequest = "badRequest";
    }

    public sealed class HireListingResponse
    {
        public bool Ok { get; set; }
        /// <summary><see cref="HireReasons"/>.</summary>
        public string Reason { get; set; }
        public HireListing Listing { get; set; }
        /// <summary>추천 가격 (등록 화면 안내).</summary>
        public long RecommendedPrice { get; set; }
    }

    public sealed class HireListResponse
    {
        public List<HireListing> Listings { get; set; } = new List<HireListing>();
        /// <summary>요청한 계정의 지난 24시간 고용 수 / 하루 상한 (R-44).</summary>
        public int DailyUsed { get; set; }
        public int DailyMax { get; set; }
        /// <summary>등록 id → 다시 고용할 수 있는 시각 (대기 중인 것만).</summary>
        public Dictionary<string, long> CooldownUntil { get; set; } = new Dictionary<string, long>();
    }

    /// <summary>전투에 고용 하나: 등록 id·판, 부대면 인원.</summary>
    public sealed class HireUse
    {
        public string ListingId { get; set; }
        public long Version { get; set; }
        /// <summary>부대 고용 인원 (장수는 0).</summary>
        public long Count { get; set; }
    }

    /// <summary>우편 하나 (고용 보상 등). 받으면(mail.claim) 세이브 지갑에 더해진다.</summary>
    public sealed class Mail
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Resource { get; set; }
        public long Amount { get; set; }
        /// <summary>고용 보상이면 등록 id.</summary>
        public string ListingId { get; set; }
        /// <summary>영토 수입이면 지역 id.</summary>
        public string RegionId { get; set; }
        public long CreatedAtUnixSeconds { get; set; }
    }

    public static class MailKinds
    {
        public const string HireReward = "hireReward";
        /// <summary>영토 수입 (차지한 지역에서 쌓인 자원, R-47). ListingId 칸 대신 RegionId.</summary>
        public const string TerritoryIncome = "territoryIncome";
    }

    public sealed class MailListResponse
    {
        public List<Mail> Mail { get; set; } = new List<Mail>();
    }

    public static class VerdictReasons
    {
        public const string Ok = "ok";
        public const string StaleSave = "staleSave";
        public const string BadSortie = "badSortie";
        public const string Mismatch = "mismatch";
        /// <summary>기기 행동을 서버 세이브에 적용할 수 없음 (R-41).</summary>
        public const string BadAction = "badAction";
        /// <summary>기록 시각이 허용 범위 밖 (서버 세이브 시각보다 이르거나, 미래이거나, 너무 오래됨, R-42).</summary>
        public const string BadTime = "badTime";
        /// <summary>고용 제한 (하루 고용 수, 같은 등록 대기시간, R-44).</summary>
        public const string HireLimit = "hireLimit";
    }

    public sealed class BattleVerdict
    {
        public bool Valid { get; set; }
        /// <summary><see cref="VerdictReasons"/>.</summary>
        public string Reason { get; set; }
        public string Hash { get; set; }
        public long Ticks { get; set; }
        public string Winner { get; set; }
        /// <summary>반영한 뒤의 세이브 (Valid일 때). 기기는 이것을 받아 쓴다.</summary>
        public CloudSave Save { get; set; }
    }

    /// <summary>기기 행동 종류 (verification R-41). 새 행동은 종류를 더하고 처리기를 등록한다.</summary>
    public static class ActionKinds
    {
        public const string FormationSave = "formation.save";
        public const string FormationRemovePreset = "formation.removePreset";
        public const string SkillBookBuy = "skillBook.buy";
        public const string SkillLearn = "skill.learn";
        public const string UnitConscript = "unit.conscript";
        public const string UnitRecruit = "unit.recruit";
        public const string UnitHeal = "unit.heal";
        public const string UnitCreate = "unit.create";
        public const string UnitDisband = "unit.disband";
        /// <summary>우편 받기: MailId, Resource(SkillId 칸 대신 PayWith), Count = 양. 서버는 우편이 있는지 먼저 확인한다.</summary>
        public const string MailClaim = "mail.claim";
    }

    /// <summary>편성 저장 행동의 군단 하나 (캠페인 편성과 같은 내용).</summary>
    public sealed class ActionCorps
    {
        public string GeneralId { get; set; }
        public string Formation { get; set; }
        public string Role { get; set; }
        public Dictionary<string, string> Slots { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// 전투 밖에서 기기가 한 행동 하나 (편성 저장, 스킬북 구매·습득, 징병·모병·치료, 부대 창설·해산).
    /// 서버는 세이브를 통째로 받지 않고 같은 규칙 코드로 행동을 다시 적용한다 (R-41). 종류마다 쓰는 칸만 채운다.
    /// </summary>
    public sealed class PlayerAction
    {
        /// <summary><see cref="ActionKinds"/>.</summary>
        public string Kind { get; set; }
        /// <summary>편성: 상황 id (null = 기본 편성).</summary>
        public string Situation { get; set; }
        public List<ActionCorps> Corps { get; set; }
        public string GeneralId { get; set; }
        public string SkillId { get; set; }
        public string UnitId { get; set; }
        public string TroopType { get; set; }
        public long Count { get; set; }
        /// <summary>치료 비용: supply / gold. 우편 받기: 받는 자원.</summary>
        public string PayWith { get; set; }
        /// <summary>우편 받기: 우편 id.</summary>
        public string MailId { get; set; }
        /// <summary>행동한 시각 (서버 시각 기준 unix 초, 0 = 지금, R-42).</summary>
        public long DoneAtUnixSeconds { get; set; }
    }

    /// <summary>행동 묶음 제출: 이 세이브 위에서 차례로 했다.</summary>
    public sealed class ActionSubmission
    {
        public long BaseRevision { get; set; }
        public List<PlayerAction> Actions { get; set; } = new List<PlayerAction>();
    }

    public sealed class ActionVerdict
    {
        public bool Valid { get; set; }
        /// <summary><see cref="VerdictReasons"/> (ok / staleSave / badAction).</summary>
        public string Reason { get; set; }
        /// <summary>적용하지 못한 첫 행동의 순번 (없으면 -1). 하나라도 실패하면 아무것도 반영하지 않는다.</summary>
        public int FailedIndex { get; set; } = -1;
        /// <summary>반영한 뒤의 세이브 (Valid), 아니면 서버에 있는 세이브.</summary>
        public CloudSave Save { get; set; }
    }

    /// <summary>
    /// 온라인 서비스 경계 (structure.md 5장 "서버", dev-rules 4장). 구현: off(서버 없음) / fake(기기 안) / 실제(HTTP·백엔드 SDK).
    /// 꺼져 있어도 게임은 기기 세이브로 동작한다. 콜백은 실패하면 null.
    /// </summary>
    public interface IOnlineService
    {
        bool IsAvailable { get; }
        string AccountId { get; }
        void SignIn(Action<bool> done);
        void GetTime(Action<ServerTimeResponse> done);
        void LoadSave(Action<CloudSave> done);
        void PutSave(SavePutRequest request, Action<SavePutResponse> done);
        void SubmitBattle(BattleSubmission submission, Action<BattleVerdict> done);
        void SubmitActions(ActionSubmission submission, Action<ActionVerdict> done);

        // ---- 고용·우편 (R-43). 서버에 닿지 않으면 null ----
        /// <param name="kind">general / unit / null = 전부.</param>
        void GetListings(string kind, Action<HireListResponse> done);
        void RegisterHire(HireListingRequest request, Action<HireListingResponse> done);
        /// <summary>해제: 되면 true, 아니면 false, 닿지 않으면 null.</summary>
        void Unregister(string listingId, Action<bool?> done);
        void GetMail(Action<MailListResponse> done);

        /// <summary>영토 주인 목록 (닿지 않으면 null).</summary>
        void GetTerritory(Action<TerritoryResponse> done);
        /// <summary>내 지역의 쌓인 수입을 우편으로 (보낸 우편, 닿지 않으면 null).</summary>
        void CollectTerritory(Action<MailListResponse> done);
        /// <summary>내 지역에 수비 편성 등록 (닿지 않으면 null).</summary>
        void RegisterDefense(DefenseRequest request, Action<DefenseResponse> done);

        // ---- 연합 (R-49). 닿지 않으면 null ----
        void GetAlliances(Action<AllianceListResponse> done);
        void CreateAlliance(string name, Action<AllianceResponse> done);
        void JoinAlliance(string allianceId, Action<AllianceResponse> done);
        void LeaveAlliance(Action<AllianceResponse> done);
    }

    /// <summary>서버 없음: 모든 요청이 곧바로 실패(null). 게임은 기기 세이브만 쓴다.</summary>
    public sealed class OfflineOnlineService : IOnlineService
    {
        public bool IsAvailable => false;
        public string AccountId => null;
        public void SignIn(Action<bool> done) => done?.Invoke(false);
        public void GetTime(Action<ServerTimeResponse> done) => done?.Invoke(null);
        public void LoadSave(Action<CloudSave> done) => done?.Invoke(null);
        public void PutSave(SavePutRequest request, Action<SavePutResponse> done) => done?.Invoke(null);
        public void SubmitBattle(BattleSubmission submission, Action<BattleVerdict> done) => done?.Invoke(null);
        public void SubmitActions(ActionSubmission submission, Action<ActionVerdict> done) => done?.Invoke(null);
        public void GetListings(string kind, Action<HireListResponse> done) => done?.Invoke(null);
        public void RegisterHire(HireListingRequest request, Action<HireListingResponse> done) => done?.Invoke(null);
        public void Unregister(string listingId, Action<bool?> done) => done?.Invoke(null);
        public void GetMail(Action<MailListResponse> done) => done?.Invoke(null);
        public void GetTerritory(Action<TerritoryResponse> done) => done?.Invoke(null);
        public void CollectTerritory(Action<MailListResponse> done) => done?.Invoke(null);
        public void RegisterDefense(DefenseRequest request, Action<DefenseResponse> done) => done?.Invoke(null);
        public void GetAlliances(Action<AllianceListResponse> done) => done?.Invoke(null);
        public void CreateAlliance(string name, Action<AllianceResponse> done) => done?.Invoke(null);
        public void JoinAlliance(string allianceId, Action<AllianceResponse> done) => done?.Invoke(null);
        public void LeaveAlliance(Action<AllianceResponse> done) => done?.Invoke(null);
    }

    /// <summary>온라인 메시지 JSON 설정 (기기·서버 공통). Fix는 원본 정수로.</summary>
    public static class OnlineJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new FixRawConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            FloatParseHandling = FloatParseHandling.Decimal,
        };

        public static string Write(object o) => JsonConvert.SerializeObject(o, Settings);
        public static T Read<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        private sealed class FixRawConverter : JsonConverter<Fix>
        {
            public override void WriteJson(JsonWriter writer, Fix value, JsonSerializer serializer) => writer.WriteValue(value.Raw);
            public override Fix ReadJson(JsonReader reader, Type objectType, Fix existingValue, bool hasExistingValue, JsonSerializer serializer) =>
                Fix.FromRaw(Convert.ToInt64(reader.Value));
        }
    }
}
