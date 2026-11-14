using System;
using System.Collections.Generic;

namespace Game.Online
{
    /// <summary>서비스 설정(config/services.*.json)의 온라인 항목: online = off / fake / http, onlineUrl, onlineGame (게임 코드).</summary>
    public static class OnlineConfig
    {
        public const string Key = "online";
        public const string UrlKey = "onlineUrl";
        public const string GameKey = "onlineGame";
        public const string Off = "off";
        /// <summary>기기 안 가짜 서버 (Game.Flow.LocalOnlineService).</summary>
        public const string Fake = "fake";
        /// <summary>참고 서버·실제 서버 (HttpOnlineService).</summary>
        public const string Http = "http";
    }

    /// <summary>기기에 보관하는 온라인 연결 정보 (게스트 토큰, 마지막으로 맞춘 서버 Revision).</summary>
    public sealed class OnlineLink
    {
        public const string SaveKey = "online";
        public string AccountId { get; set; }
        public string Token { get; set; }
        public long Revision { get; set; }
        /// <summary>
        /// 서버에 아직 반영되지 않은 기록 (행동·전투, 한 순서). 서버에 닿지 않으면 남겨 두고 다음에 차례로 보낸다 (R-41, R-42).
        /// </summary>
        public List<JournalEntry> Journal { get; set; } = new List<JournalEntry>();
        /// <summary>예전 형식(행동만). 읽을 때 Journal로 옮긴다.</summary>
        public List<PlayerAction> Pending { get; set; }

        /// <summary>예전 형식을 옮긴다 (읽은 뒤 한 번).</summary>
        public OnlineLink Upgrade()
        {
            if (Journal == null) Journal = new List<JournalEntry>();
            if (Pending != null) foreach (var a in Pending) if (a != null) Journal.Add(new JournalEntry { Action = a });
            Pending = null;
            Journal.RemoveAll(e => e == null || (e.Action == null) == (e.Battle == null));
            return this;
        }
    }

    /// <summary>기록 하나: 행동 또는 전투 (둘 중 하나만).</summary>
    public sealed class JournalEntry
    {
        public PlayerAction Action { get; set; }
        public BattleSubmission Battle { get; set; }
    }

    /// <summary>
    /// HTTP 요청 경계 (엔진과 무관). Unity는 UnityWebRequest, 테스트는 HttpClient로 구현한다.
    /// done(상태 코드, 본문): 네트워크 실패면 상태 0.
    /// </summary>
    public interface IOnlineTransport
    {
        void Send(string method, string path, string jsonBody, string bearerToken, Action<int, string> done);
    }

    /// <summary>
    /// 참고 서버(docs/impl/server.md)를 부르는 온라인 서비스. 게임 코드별 주소(/v1/games/{game}/…)를 쓴다.
    /// 게스트 토큰은 기기에 보관해 다음 실행에 다시 쓴다 (<see cref="Restore"/>).
    /// </summary>
    public sealed class HttpOnlineService : IOnlineService
    {
        private readonly IOnlineTransport _transport;
        private readonly string _game;

        public HttpOnlineService(IOnlineTransport transport, string gameCode)
        {
            _transport = transport;
            _game = gameCode;
        }

        public string GameCode => _game;
        public string Token { get; private set; }
        public string AccountId { get; private set; }
        /// <summary>로그인되어 있으면 true (요청은 그래도 실패할 수 있다).</summary>
        public bool IsAvailable => Token != null;

        /// <summary>보관해 둔 계정으로 (로그인 없이) 이어서 쓴다.</summary>
        public void Restore(string accountId, string token)
        {
            AccountId = accountId;
            Token = token;
        }

        public void SignIn(Action<bool> done)
        {
            if (Token != null) { done?.Invoke(true); return; }
            _transport.Send("POST", OnlineRoutes.GuestSignIn, null, null, (status, body) =>
            {
                var r = status == 200 ? Parse<SignInResponse>(body) : null;
                if (r?.Token != null) { Token = r.Token; AccountId = r.AccountId; }
                done?.Invoke(r?.Token != null);
            });
        }

        /// <summary>
        /// Firebase 로그인 (POST /v1/auth/firebase): 서버가 ID 토큰을 확인하고 그 사용자의 계정 토큰을 준다. 지금 토큰(게스트 등)을 바꾼다.
        /// 서버가 Firebase를 쓰지 않으면(404) 실패.
        /// </summary>
        public void SignInWithFirebase(string idToken, Action<bool> done)
        {
            _transport.Send("POST", OnlineRoutes.FirebaseSignIn, OnlineJson.Write(new ExternalSignInRequest { IdToken = idToken }), null, (status, body) =>
            {
                var r = status == 200 ? Parse<SignInResponse>(body) : null;
                if (r?.Token != null) { Token = r.Token; AccountId = r.AccountId; }
                done?.Invoke(r?.Token != null);
            });
        }

        public void GetTime(Action<ServerTimeResponse> done) =>
            _transport.Send("GET", OnlineRoutes.Time, null, null, (status, body) => done?.Invoke(status == 200 ? Parse<ServerTimeResponse>(body) : null));

        public void LoadSave(Action<CloudSave> done) =>
            _transport.Send("GET", OnlineRoutes.Save(_game), null, Token, (status, body) => done?.Invoke(status == 200 ? Parse<CloudSave>(body) : null));

        /// <summary>200 = 저장, 409 = 충돌(서버 사본). 그 밖은 null.</summary>
        public void PutSave(SavePutRequest request, Action<SavePutResponse> done) =>
            _transport.Send("PUT", OnlineRoutes.Save(_game), OnlineJson.Write(request), Token, (status, body) =>
                done?.Invoke(status == 200 || status == 409 ? Parse<SavePutResponse>(body) : null));

        /// <summary>200 = 반영, 409 = 옛 세이브, 422 = 거절. 그 밖(네트워크 실패 등)은 null.</summary>
        public void SubmitBattle(BattleSubmission submission, Action<BattleVerdict> done) =>
            _transport.Send("POST", OnlineRoutes.Battles(_game), OnlineJson.Write(submission), Token, (status, body) =>
                done?.Invoke(status == 200 || status == 409 || status == 422 ? Parse<BattleVerdict>(body) : null));

        /// <summary>200 = 반영, 409 = 옛 세이브, 422 = 적용 불가. 그 밖은 null.</summary>
        public void SubmitActions(ActionSubmission submission, Action<ActionVerdict> done) =>
            _transport.Send("POST", OnlineRoutes.Actions(_game), OnlineJson.Write(submission), Token, (status, body) =>
                done?.Invoke(status == 200 || status == 409 || status == 422 ? Parse<ActionVerdict>(body) : null));

        public void GetListings(string kind, Action<HireListResponse> done) =>
            _transport.Send("GET", OnlineRoutes.HireListings(_game) + (kind == null ? "" : "?kind=" + Uri.EscapeDataString(kind)), null, Token, (status, body) =>
                done?.Invoke(status == 200 ? Parse<HireListResponse>(body) : null));

        /// <summary>200 = 등록, 422 = 거절(까닭). 그 밖은 null.</summary>
        public void RegisterHire(HireListingRequest request, Action<HireListingResponse> done) =>
            _transport.Send("POST", OnlineRoutes.HireListings(_game), OnlineJson.Write(request), Token, (status, body) =>
                done?.Invoke(status == 200 || status == 422 ? Parse<HireListingResponse>(body) : null));

        public void Unregister(string listingId, Action<bool?> done) =>
            _transport.Send("DELETE", OnlineRoutes.HireListing(_game, listingId), null, Token, (status, body) =>
                done?.Invoke(status == 204 ? true : status == 404 ? false : (bool?)null));

        /// <summary>200 = 올림, 422 = 거절(까닭). 그 밖은 null.</summary>
        public void ShareReplay(ShareReplayRequest request, Action<ShareReplayResponse> done) =>
            _transport.Send("POST", OnlineRoutes.SharedReplays(_game), OnlineJson.Write(request), Token, (status, body) =>
                done?.Invoke(status == 200 || status == 422 ? Parse<ShareReplayResponse>(body) : null));

        public void GetSharedReplays(string stepId, Action<SharedReplayListResponse> done) =>
            _transport.Send("GET", OnlineRoutes.SharedReplays(_game) + "?step=" + Uri.EscapeDataString(stepId ?? ""), null, Token, (status, body) =>
                done?.Invoke(status == 200 ? Parse<SharedReplayListResponse>(body) : null));

        public void GetSharedReplay(string id, Action<SharedReplayResponse> done) =>
            _transport.Send("GET", OnlineRoutes.SharedReplay(_game, id), null, Token, (status, body) =>
                done?.Invoke(status == 200 ? Parse<SharedReplayResponse>(body) : null));

        public void GetMail(Action<MailListResponse> done) =>
            _transport.Send("GET", OnlineRoutes.Mail(_game), null, Token, (status, body) => done?.Invoke(status == 200 ? Parse<MailListResponse>(body) : null));

        public void GetTerritory(Action<TerritoryResponse> done) =>
            _transport.Send("GET", OnlineRoutes.Territory(_game), null, Token, (status, body) => done?.Invoke(status == 200 ? Parse<TerritoryResponse>(body) : null));

        public void CollectTerritory(Action<MailListResponse> done) =>
            _transport.Send("POST", OnlineRoutes.TerritoryCollect(_game), null, Token, (status, body) => done?.Invoke(status == 200 ? Parse<MailListResponse>(body) : null));

        /// <summary>200 = 등록, 422 = 거절(까닭). 그 밖은 null.</summary>
        public void RegisterDefense(DefenseRequest request, Action<DefenseResponse> done) =>
            _transport.Send("POST", OnlineRoutes.TerritoryDefense(_game), OnlineJson.Write(request), Token, (status, body) =>
                done?.Invoke(status == 200 || status == 422 ? Parse<DefenseResponse>(body) : null));

        /// <summary>200 = 꽂음, 422 = 못 꽂음(까닭 · 공성전 대상). 그 밖은 null.</summary>
        public void PlantFlag(PlantRequest request, Action<PlantResponse> done) =>
            _transport.Send("POST", OnlineRoutes.TerritoryPlant(_game), OnlineJson.Write(request), Token, (status, body) =>
                done?.Invoke(status == 200 || status == 422 ? Parse<PlantResponse>(body) : null));

        public void GetAlliances(Action<AllianceListResponse> done) =>
            _transport.Send("GET", OnlineRoutes.Alliances(_game), null, Token, (status, body) => done?.Invoke(status == 200 ? Parse<AllianceListResponse>(body) : null));

        /// <summary>200 = 됨, 422 = 거절(까닭). 그 밖은 null.</summary>
        public void CreateAlliance(string name, Action<AllianceResponse> done) =>
            _transport.Send("POST", OnlineRoutes.Alliances(_game), OnlineJson.Write(new AllianceRequest { Name = name }), Token, (status, body) => done?.Invoke(AllianceParse(status, body)));

        public void JoinAlliance(string allianceId, Action<AllianceResponse> done) =>
            _transport.Send("POST", OnlineRoutes.AllianceJoin(_game, allianceId), null, Token, (status, body) => done?.Invoke(AllianceParse(status, body)));

        public void LeaveAlliance(Action<AllianceResponse> done) =>
            _transport.Send("POST", OnlineRoutes.AllianceLeave(_game), null, Token, (status, body) => done?.Invoke(AllianceParse(status, body)));

        public void ManageAlliance(AllianceManageRequest request, Action<AllianceResponse> done) =>
            _transport.Send("POST", OnlineRoutes.AllianceManage(_game), OnlineJson.Write(request), Token, (status, body) => done?.Invoke(AllianceParse(status, body)));

        private static AllianceResponse AllianceParse(int status, string body) => status == 200 || status == 422 ? Parse<AllianceResponse>(body) : null;

        public void GetProfile(Action<ProfileResponse> done) =>
            _transport.Send("GET", OnlineRoutes.Me, null, Token, (status, body) => done?.Invoke(status == 200 ? Parse<ProfileResponse>(body) : null));

        /// <summary>200 = 정함, 422 = 거절(까닭). 그 밖은 null.</summary>
        public void SetNickname(string nickname, Action<NicknameResponse> done) =>
            _transport.Send("POST", OnlineRoutes.MyNickname, OnlineJson.Write(new NicknameRequest { Nickname = nickname }), Token, (status, body) =>
                done?.Invoke(status == 200 || status == 422 ? Parse<NicknameResponse>(body) : null));

        public void GetNames(IReadOnlyCollection<string> accountIds, Action<NamesResponse> done) =>
            _transport.Send("GET", OnlineRoutes.Names(accountIds), null, Token, (status, body) => done?.Invoke(status == 200 ? Parse<NamesResponse>(body) : null));

        private static T Parse<T>(string body) where T : class
        {
            if (string.IsNullOrEmpty(body)) return null;
            try { return OnlineJson.Read<T>(body); } catch (Exception) { return null; }
        }
    }
}
