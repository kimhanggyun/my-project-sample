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

        // ---- 고용 등록 (게임별, R-43) ----
        /// <summary>같은 등록자·종류·대상(SourceId)이 있으면 갱신(Id 유지, Version +1), 없으면 새로 (Version 1).</summary>
        HireListing UpsertListing(string gameCode, HireListing listing);
        HireListing GetListing(string gameCode, string listingId);
        /// <summary>kind가 null이면 전부. 등록 시각 순.</summary>
        List<HireListing> Listings(string gameCode, string kind);
        bool RemoveListing(string gameCode, string ownerAccountId, string listingId);
        /// <summary>등록의 이력에 결과를 더한다 (판은 그대로, R-44).</summary>
        void RecordHire(string gameCode, string listingId, Game.Flow.HireOutcome outcome, string stepId, long atUnixSeconds, int recentMax);
        /// <summary>고용한 계정의 고용 기록 (24시간 지난 것은 지워도 된다).</summary>
        List<Game.Flow.HireLogEntry> HireLog(string gameCode, string accountId, long nowUnixSeconds);
        void AddHireLog(string gameCode, string accountId, IEnumerable<Game.Flow.HireLogEntry> entries);

        // ---- 영토 (게임별, R-46) ----
        /// <summary>영토 주인 장부를 잠근 채 읽거나 고친다 (고친 뒤 저장). 우편을 보내도 된다.</summary>
        T WithTerritory<T>(string gameCode, Func<Dictionary<string, TerritoryOwner>, T> use);

        // ---- 우편 (게임별) ----
        void AddMail(string gameCode, string accountId, Mail mail);
        List<Mail> MailOf(string gameCode, string accountId);
        /// <summary>우편을 지운다 (받음). 없으면 false.</summary>
        bool TakeMail(string gameCode, string accountId, string mailId);
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
            /// <summary>게임 코드 → 고용 등록 목록.</summary>
            public Dictionary<string, List<HireListing>> Listings { get; set; } = new Dictionary<string, List<HireListing>>();
            /// <summary>게임 코드 → (계정 → 우편).</summary>
            public Dictionary<string, Dictionary<string, List<Mail>>> Mail { get; set; } = new Dictionary<string, Dictionary<string, List<Mail>>>();
            /// <summary>게임 코드 → (계정 → 고용 기록).</summary>
            public Dictionary<string, Dictionary<string, List<Game.Flow.HireLogEntry>>> HireLogs { get; set; } = new Dictionary<string, Dictionary<string, List<Game.Flow.HireLogEntry>>>();
            /// <summary>게임 코드 → (지역 → 주인).</summary>
            public Dictionary<string, Dictionary<string, TerritoryOwner>> Territory { get; set; } = new Dictionary<string, Dictionary<string, TerritoryOwner>>();
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

        private List<HireListing> ListingsOf(string gameCode)
        {
            if (Data.Listings == null) Data.Listings = new Dictionary<string, List<HireListing>>();
            if (!Data.Listings.TryGetValue(gameCode, out var list)) Data.Listings[gameCode] = list = new List<HireListing>();
            return list;
        }

        private List<Mail> Box(string gameCode, string accountId)
        {
            if (Data.Mail == null) Data.Mail = new Dictionary<string, Dictionary<string, List<Mail>>>();
            if (!Data.Mail.TryGetValue(gameCode, out var g)) Data.Mail[gameCode] = g = new Dictionary<string, List<Mail>>();
            if (!g.TryGetValue(accountId, out var box)) g[accountId] = box = new List<Mail>();
            return box;
        }

        public HireListing UpsertListing(string gameCode, HireListing listing)
        {
            lock (_lock)
            {
                var list = ListingsOf(gameCode);
                var old = list.Find(l => l.OwnerAccountId == listing.OwnerAccountId && l.Kind == listing.Kind && l.SourceId == listing.SourceId);
                var next = Copy(listing);
                if (old != null) { next.Id = old.Id; next.Version = old.Version + 1; list[list.IndexOf(old)] = next; }
                else { next.Id = "h-" + NewId(6); next.Version = 1; list.Add(next); }
                Persist();
                return Copy(next);
            }
        }

        public HireListing GetListing(string gameCode, string listingId)
        {
            lock (_lock) { var l = ListingsOf(gameCode).Find(x => x.Id == listingId); return l == null ? null : Copy(l); }
        }

        public List<HireListing> Listings(string gameCode, string kind)
        {
            lock (_lock) return ListingsOf(gameCode).FindAll(l => kind == null || l.Kind == kind).ConvertAll(Copy);
        }

        public bool RemoveListing(string gameCode, string ownerAccountId, string listingId)
        {
            lock (_lock)
            {
                int n = ListingsOf(gameCode).RemoveAll(l => l.Id == listingId && l.OwnerAccountId == ownerAccountId);
                if (n > 0) Persist();
                return n > 0;
            }
        }

        public void RecordHire(string gameCode, string listingId, Game.Flow.HireOutcome outcome, string stepId, long atUnixSeconds, int recentMax)
        {
            lock (_lock)
            {
                var l = ListingsOf(gameCode).Find(x => x.Id == listingId);
                if (l == null) return; // 그사이 해제됨
                Game.Flow.HireBook.Record(l, outcome, stepId, atUnixSeconds, recentMax);
                Persist();
            }
        }

        private List<Game.Flow.HireLogEntry> LogOf(string gameCode, string accountId)
        {
            if (Data.HireLogs == null) Data.HireLogs = new Dictionary<string, Dictionary<string, List<Game.Flow.HireLogEntry>>>();
            if (!Data.HireLogs.TryGetValue(gameCode, out var g)) Data.HireLogs[gameCode] = g = new Dictionary<string, List<Game.Flow.HireLogEntry>>();
            if (!g.TryGetValue(accountId, out var log)) g[accountId] = log = new List<Game.Flow.HireLogEntry>();
            return log;
        }

        public List<Game.Flow.HireLogEntry> HireLog(string gameCode, string accountId, long nowUnixSeconds)
        {
            lock (_lock)
            {
                var log = LogOf(gameCode, accountId);
                log.RemoveAll(e => nowUnixSeconds - e.AtUnixSeconds >= Game.Flow.HireBook.DaySeconds);
                return log.ConvertAll(e => new Game.Flow.HireLogEntry { ListingId = e.ListingId, AtUnixSeconds = e.AtUnixSeconds });
            }
        }

        public void AddHireLog(string gameCode, string accountId, IEnumerable<Game.Flow.HireLogEntry> entries)
        {
            lock (_lock)
            {
                LogOf(gameCode, accountId).AddRange(entries);
                Persist();
            }
        }

        private Dictionary<string, TerritoryOwner> OwnersOf(string gameCode)
        {
            if (Data.Territory == null) Data.Territory = new Dictionary<string, Dictionary<string, TerritoryOwner>>();
            if (!Data.Territory.TryGetValue(gameCode, out var g)) Data.Territory[gameCode] = g = new Dictionary<string, TerritoryOwner>();
            return g;
        }

        public T WithTerritory<T>(string gameCode, Func<Dictionary<string, TerritoryOwner>, T> use)
        {
            lock (_lock)
            {
                var r = use(OwnersOf(gameCode));
                Persist();
                return r;
            }
        }

        public void AddMail(string gameCode, string accountId, Mail mail)
        {
            lock (_lock)
            {
                var m = Copy(mail);
                m.Id = "m-" + NewId(6);
                Box(gameCode, accountId).Add(m);
                Persist();
            }
        }

        public List<Mail> MailOf(string gameCode, string accountId)
        {
            lock (_lock) return Box(gameCode, accountId).ConvertAll(Copy);
        }

        public bool TakeMail(string gameCode, string accountId, string mailId)
        {
            lock (_lock)
            {
                int n = Box(gameCode, accountId).RemoveAll(m => m.Id == mailId);
                if (n > 0) Persist();
                return n > 0;
            }
        }

        private static HireListing Copy(HireListing l) => OnlineJson.Read<HireListing>(OnlineJson.Write(l));
        private static Mail Copy(Mail m) => OnlineJson.Read<Mail>(OnlineJson.Write(m));

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
