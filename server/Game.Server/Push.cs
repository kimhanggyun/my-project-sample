using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Game.Data.Defs;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Game.Server
{
    /// <summary>계정 하나의 푸시 설정 · 기기 토큰 (게임별, 저장소, R-119).</summary>
    public sealed class PushProfile
    {
        public PushSettings Settings { get; set; } = new PushSettings();
        public List<PushTokenEntry> Tokens { get; set; } = new List<PushTokenEntry>();
    }

    public sealed class PushTokenEntry
    {
        public string Token { get; set; }
        public string Platform { get; set; }
        public long AtUnixSeconds { get; set; }
    }

    public enum PushSendResult { Sent, InvalidToken, Failed }

    /// <summary>푸시 보내기 (R-119, 외부 서비스 — 인터페이스 뒤, 기본 꺼짐). 설정 Game:Push = off · fake · fcm.</summary>
    public interface IPushSender
    {
        PushSendResult Send(string token, string title, string body, IDictionary<string, string> data);
    }

    /// <summary>꺼짐 (기본): 아무것도 보내지 않는다.</summary>
    public sealed class OffPushSender : IPushSender
    {
        public PushSendResult Send(string token, string title, string body, IDictionary<string, string> data) => PushSendResult.Failed;
    }

    /// <summary>시험용: 보낸 것을 메모리에 남긴다 ("bad:"로 시작하는 토큰은 무효 토큰으로).</summary>
    public sealed class FakePushSender : IPushSender
    {
        public readonly List<(string token, string title, string body, IDictionary<string, string> data)> Sent = new List<(string, string, string, IDictionary<string, string>)>();
        public PushSendResult Send(string token, string title, string body, IDictionary<string, string> data)
        {
            if (token.StartsWith("bad:", StringComparison.Ordinal)) return PushSendResult.InvalidToken;
            lock (Sent) Sent.Add((token, title, body, data));
            return PushSendResult.Sent;
        }
    }

    /// <summary>
    /// Firebase Cloud Messaging HTTP v1 (R-119): POST https://fcm.googleapis.com/v1/projects/{project}/messages:send.
    /// 서비스 계정 키(Game:Push:ServiceAccountJson, 비밀)로 접근 토큰 (scope firebase.messaging). 404 · UNREGISTERED = 무효 토큰 (지운다).
    /// </summary>
    public sealed class FcmPushSender : IPushSender
    {
        public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
        private readonly HttpClient _http;
        private readonly ServiceAccountCredential _cred;
        private readonly string _project;
        private readonly ITimeProvider _time;
        private readonly object _lock = new object();
        private string _token;
        private long _until;

        public FcmPushSender(HttpClient http, ServiceAccountCredential cred, string projectId, ITimeProvider time = null)
        {
            _http = http;
            _cred = cred;
            _project = projectId;
            _time = time ?? new SystemTimeProvider();
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        private string Access()
        {
            lock (_lock)
            {
                long now = _time.UtcNow.ToUnixTimeSeconds();
                if (_token != null && now < _until - 60) return _token;
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                    new KeyValuePair<string, string>("assertion", _cred.Assertion(now, Scope)),
                });
                var res = _http.PostAsync(_cred.TokenUri, form).GetAwaiter().GetResult();
                if (!res.IsSuccessStatusCode) throw new InvalidOperationException("FCM 접근 토큰을 받지 못했습니다: " + (int)res.StatusCode);
                var o = JObject.Parse(res.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                _token = o.Value<string>("access_token");
                _until = now + (o.Value<long?>("expires_in") ?? 3600);
                return _token;
            }
        }

        public PushSendResult Send(string token, string title, string body, IDictionary<string, string> data)
        {
            try
            {
                var msg = new JObject
                {
                    ["message"] = new JObject
                    {
                        ["token"] = token,
                        ["notification"] = new JObject { ["title"] = title, ["body"] = body },
                        ["data"] = JObject.FromObject(data ?? new Dictionary<string, string>()),
                    },
                };
                var req = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/v1/projects/" + Uri.EscapeDataString(_project) + "/messages:send")
                {
                    Content = new StringContent(msg.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json"),
                };
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Access());
                var res = _http.SendAsync(req).GetAwaiter().GetResult();
                if (res.IsSuccessStatusCode) return PushSendResult.Sent;
                string text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if ((int)res.StatusCode == 404 || text.Contains("UNREGISTERED") || text.Contains("INVALID_ARGUMENT")) return PushSendResult.InvalidToken;
                return PushSendResult.Failed;
            }
            catch (Exception) { return PushSendResult.Failed; }
        }
    }

    public static class PushSenders
    {
        public static IPushSender FromConfig(IConfiguration config, ITimeProvider time = null)
        {
            string id = (config["Game:Push"] ?? "off").Trim();
            switch (id)
            {
                case "off": return new OffPushSender();
                case "fake": return new FakePushSender();
                case "fcm":
                    {
                        string key = config["Game:Push:ServiceAccountJson"], project = config["Game:Push:ProjectId"] ?? config["Game:Firebase:ProjectId"];
                        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(project)) return new OffPushSender(); // 설정이 모자라면 꺼진 채로
                        return new FcmPushSender(new HttpClient(), ServiceAccountCredential.Parse(key), project, time);
                    }
                default: throw new InvalidOperationException($"Game:Push '{id}'를 모릅니다 (off · fake · fcm).");
            }
        }
    }

    /// <summary>규칙이 고른 알림을 받는 사람의 설정 · 토큰으로 보낸다 (R-119). 무효 토큰은 지운다. 보내기 실패는 게임에 영향 없음.</summary>
    public sealed class PushService
    {
        private readonly IGameStore _store;
        private readonly IPushSender _sender;
        private readonly DefinitionSet _defs;
        private readonly string _game;

        public PushService(string game, IGameStore store, IPushSender sender, DefinitionSet defs)
        {
            _game = game;
            _store = store;
            _sender = sender ?? new OffPushSender();
            _defs = defs;
        }

        public bool Enabled => !(_sender is OffPushSender);

        /// <summary>보낸 기기 수.</summary>
        public int Send(IEnumerable<PushMessage> messages)
        {
            int sent = 0;
            if (!Enabled || messages == null) return 0;
            foreach (var m in messages)
            {
                var (title, body) = PushRules.Render(_defs, m);
                var data = new Dictionary<string, string> { ["kind"] = m.Kind, ["regionId"] = m.RegionId ?? "" };
                sent += _store.WithPush(_game, m.AccountId, p =>
                {
                    if (!p.Settings.Allows(m.Kind)) return 0;
                    int n = 0;
                    foreach (var t in p.Tokens.ToArray())
                    {
                        var r = _sender.Send(t.Token, title, body, data);
                        if (r == PushSendResult.Sent) n++;
                        else if (r == PushSendResult.InvalidToken) p.Tokens.Remove(t);
                    }
                    return n;
                });
            }
            return sent;
        }
    }
}
