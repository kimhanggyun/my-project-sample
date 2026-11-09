using System;

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

        private static T Parse<T>(string body) where T : class
        {
            if (string.IsNullOrEmpty(body)) return null;
            try { return OnlineJson.Read<T>(body); } catch (Exception) { return null; }
        }
    }
}
