using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Game.Online;
using Newtonsoft.Json;

namespace Game.Server
{
    /// <summary>
    /// 저장소 경계 (docs/impl/server.md 4.5): 공통 계정(플랫폼)과 게임별 데이터를 나눈다.
    /// 계정·토큰·"유저 × 게임" 가입 기록은 한 곳, 세이브는 (게임 코드, 계정)별로. 실제 서비스에서는 공통 DB + 게임별 DB(또는 스키마).
    /// 지금은 개발용 구현 (메모리 / 폴더).
    /// </summary>
    public interface IPlatformStore
    {
        SignInResponse CreateGuest();
        /// <summary>토큰 → 계정 id (없으면 null).</summary>
        string AccountOf(string token);
        /// <summary>그 게임에 처음 들어오면 가입 기록을 남긴다. 처음이면 true.</summary>
        bool Join(string accountId, string gameCode, long nowUnixSeconds);
        /// <summary>계정이 가입한 게임 코드.</summary>
        IReadOnlyList<string> GamesOf(string accountId);
    }

    public interface IGameStore
    {
        CloudSave GetSave(string gameCode, string accountId);
        /// <summary>baseRevision이 지금 세이브와 같을 때만 저장. (성공, 저장 뒤 또는 지금 세이브).</summary>
        (bool ok, CloudSave current) PutSave(string gameCode, string accountId, long baseRevision, string json, long nowUnixSeconds);
    }

    /// <summary>개발용 메모리 저장소 (공통 + 게임별을 한 객체에). 테스트·로컬 실행용.</summary>
    public class InMemoryStore : IPlatformStore, IGameStore
    {
        private readonly object _lock = new object();

        protected sealed class Snapshot
        {
            /// <summary>토큰 → 계정.</summary>
            public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>();
            /// <summary>계정 → (게임 코드 → 가입 시각).</summary>
            public Dictionary<string, Dictionary<string, long>> Memberships { get; set; } = new Dictionary<string, Dictionary<string, long>>();
            /// <summary>게임 코드 → (계정 → 세이브).</summary>
            public Dictionary<string, Dictionary<string, CloudSave>> Saves { get; set; } = new Dictionary<string, Dictionary<string, CloudSave>>();
        }

        protected Snapshot Data = new Snapshot();

        public SignInResponse CreateGuest()
        {
            var r = new SignInResponse { AccountId = "acct-" + NewId(8), Token = NewId(24) };
            lock (_lock)
            {
                Data.Tokens[r.Token] = r.AccountId;
                Data.Memberships[r.AccountId] = new Dictionary<string, long>();
                Persist();
            }
            return r;
        }

        public string AccountOf(string token)
        {
            if (token == null) return null;
            lock (_lock) return Data.Tokens.TryGetValue(token, out var id) ? id : null;
        }

        public bool Join(string accountId, string gameCode, long nowUnixSeconds)
        {
            lock (_lock)
            {
                if (!Data.Memberships.TryGetValue(accountId, out var games)) Data.Memberships[accountId] = games = new Dictionary<string, long>();
                if (games.ContainsKey(gameCode)) return false;
                games[gameCode] = nowUnixSeconds;
                Persist();
                return true;
            }
        }

        public IReadOnlyList<string> GamesOf(string accountId)
        {
            lock (_lock)
            {
                var list = new List<string>();
                if (Data.Memberships.TryGetValue(accountId, out var games)) list.AddRange(games.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        public CloudSave GetSave(string gameCode, string accountId)
        {
            lock (_lock) return Data.Saves.TryGetValue(gameCode, out var g) && g.TryGetValue(accountId, out var s) ? Copy(s) : null;
        }

        public (bool ok, CloudSave current) PutSave(string gameCode, string accountId, long baseRevision, string json, long nowUnixSeconds)
        {
            lock (_lock)
            {
                if (!Data.Saves.TryGetValue(gameCode, out var g)) Data.Saves[gameCode] = g = new Dictionary<string, CloudSave>();
                g.TryGetValue(accountId, out var cur);
                long rev = cur?.Revision ?? 0;
                if (rev != baseRevision) return (false, cur == null ? null : Copy(cur));
                var next = new CloudSave { Revision = rev + 1, Json = json, SavedAtUnixSeconds = nowUnixSeconds };
                g[accountId] = next;
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

    /// <summary>개발용 폴더 저장소: 바뀔 때마다 store.json으로 쓴다 (서버를 껐다 켜도 남는다).</summary>
    public sealed class FolderStore : InMemoryStore
    {
        private readonly string _file;

        public FolderStore(string root)
        {
            Directory.CreateDirectory(root);
            _file = Path.Combine(root, "store.json");
            if (File.Exists(_file)) Data = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_file)) ?? new Snapshot();
        }

        protected override void Persist()
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(Data, Formatting.Indented));
            File.Move(tmp, _file, true);
        }
    }
}
