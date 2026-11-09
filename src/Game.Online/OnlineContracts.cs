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

    /// <summary>클라우드 세이브 한 벌. Revision은 저장할 때마다 1 오른다 (0 = 아직 없음).</summary>
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
    }

    public static class VerdictReasons
    {
        public const string Ok = "ok";
        public const string StaleSave = "staleSave";
        public const string BadSortie = "badSortie";
        public const string Mismatch = "mismatch";
        /// <summary>기기 행동을 서버 세이브에 적용할 수 없음 (R-41).</summary>
        public const string BadAction = "badAction";
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
        /// <summary>치료 비용: supply / gold.</summary>
        public string PayWith { get; set; }
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
