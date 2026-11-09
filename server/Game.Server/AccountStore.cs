using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Game.Online;
using Newtonsoft.Json;

namespace Game.Server
{
    /// <summary>
    /// 계정·세이브 저장소 경계. 지금은 개발용 게스트 계정 (메모리 / 폴더). 실제 백엔드(UGS·PlayFab 등)를 쓰면 그쪽 저장소로 바꾼다.
    /// 세이브는 Revision이 맞을 때만 덮어쓴다 (동시 저장·다른 기기 충돌 방지).
    /// </summary>
    public interface IAccountStore
    {
        SignInResponse CreateGuest();
        /// <summary>토큰 → 계정 id (없으면 null).</summary>
        string AccountOf(string token);
        CloudSave GetSave(string accountId);
        /// <summary>baseRevision이 지금 세이브와 같을 때만 저장. (성공, 저장 뒤 또는 지금 세이브).</summary>
        (bool ok, CloudSave current) PutSave(string accountId, long baseRevision, string json, long nowUnixSeconds);
    }

    public class InMemoryAccountStore : IAccountStore
    {
        private readonly object _lock = new object();
        protected readonly Dictionary<string, string> Tokens = new Dictionary<string, string>();
        protected readonly Dictionary<string, CloudSave> Saves = new Dictionary<string, CloudSave>();

        public SignInResponse CreateGuest()
        {
            var r = new SignInResponse { AccountId = "guest-" + NewId(8), Token = NewId(24) };
            lock (_lock)
            {
                Tokens[r.Token] = r.AccountId;
                Persist();
            }
            return r;
        }

        public string AccountOf(string token)
        {
            if (token == null) return null;
            lock (_lock) return Tokens.TryGetValue(token, out var id) ? id : null;
        }

        public CloudSave GetSave(string accountId)
        {
            lock (_lock) return Saves.TryGetValue(accountId, out var s) ? Copy(s) : null;
        }

        public (bool ok, CloudSave current) PutSave(string accountId, long baseRevision, string json, long nowUnixSeconds)
        {
            lock (_lock)
            {
                Saves.TryGetValue(accountId, out var cur);
                long rev = cur?.Revision ?? 0;
                if (rev != baseRevision) return (false, cur == null ? null : Copy(cur));
                var next = new CloudSave { Revision = rev + 1, Json = json, SavedAtUnixSeconds = nowUnixSeconds };
                Saves[accountId] = next;
                Persist();
                return (true, Copy(next));
            }
        }

        /// <summary>바뀐 뒤 (잠금 안에서) 부른다. 폴더 저장소가 디스크에 쓴다.</summary>
        protected virtual void Persist() { }

        private static CloudSave Copy(CloudSave s) => new CloudSave { Revision = s.Revision, Json = s.Json, SavedAtUnixSeconds = s.SavedAtUnixSeconds };

        private static string NewId(int bytes)
        {
            var b = new byte[bytes];
            RandomNumberGenerator.Fill(b);
            return Convert.ToHexString(b).ToLowerInvariant();
        }
    }

    /// <summary>개발용 폴더 저장소: 바뀔 때마다 accounts.json으로 쓴다 (서버를 껐다 켜도 남는다).</summary>
    public sealed class FolderAccountStore : InMemoryAccountStore
    {
        private sealed class Snapshot
        {
            public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, CloudSave> Saves { get; set; } = new Dictionary<string, CloudSave>();
        }

        private readonly string _file;

        public FolderAccountStore(string root)
        {
            Directory.CreateDirectory(root);
            _file = Path.Combine(root, "accounts.json");
            if (!File.Exists(_file)) return;
            var snap = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_file)) ?? new Snapshot();
            foreach (var kv in snap.Tokens) Tokens[kv.Key] = kv.Value;
            foreach (var kv in snap.Saves) Saves[kv.Key] = kv.Value;
        }

        protected override void Persist()
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(new Snapshot { Tokens = Tokens, Saves = Saves }, Formatting.Indented));
            File.Move(tmp, _file, true);
        }
    }
}
