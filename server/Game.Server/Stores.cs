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
        /// <summary>외부 로그인(제공자 + 사용자 id)으로: 처음이면 새 계정, 아니면 그 계정에 새 토큰.</summary>
        /// <param name="email">확인된 이메일 (있으면 계정에 남긴다, 마스터 확인용). 없으면 null.</param>
        SignInResponse SignInExternal(string provider, string subject, string email = null);
        /// <summary>계정에 남은 확인된 이메일 (외부 로그인만, 소문자). 없으면 null.</summary>
        string EmailOf(string accountId);
        /// <summary>토큰 → 계정 id (없으면 null).</summary>
        string AccountOf(string token);
        /// <summary>그 게임에 처음 들어오면 가입 기록을 남긴다. 처음이면 true.</summary>
        bool Join(string accountId, string gameCode, long nowUnixSeconds);
        /// <summary>계정이 가입한 게임 코드.</summary>
        IReadOnlyList<string> GamesOf(string accountId);

        /// <summary>닉네임 (공통 계정). 정하기: 규칙에 맞고 다른 계정과 겹치지 않을 때 (대소문자 무시). 내 이름을 그대로 다시 정하는 건 된다.</summary>
        NicknameResponse SetNickname(string accountId, string nickname);
        string NicknameOf(string accountId);
        Dictionary<string, string> NicknamesOf(IEnumerable<string> accountIds);

        // ---- 구글 연동 (R-105) ----
        /// <summary>지금 계정에 외부 로그인을 붙인다. 그 외부 로그인이 이미 다른 계정 것이면 Taken (섞지 않음), 이 계정에 다른 외부 로그인이 있으면 AlreadyLinked.</summary>
        string LinkExternal(string accountId, string provider, string subject, string email = null);
        /// <summary>외부 로그인이 하나라도 붙어 있으면 true (게스트가 아님).</summary>
        bool IsLinked(string accountId);

        // ---- 계정 삭제 (R-102) ----
        /// <summary>삭제 요청 (이미 요청 중이면 그 시각 그대로). 영구 삭제 시각을 돌려준다.</summary>
        long RequestDeletion(string accountId, long nowUnixSeconds);
        /// <summary>삭제 요청 중이면 취소하고 true.</summary>
        bool CancelDeletion(string accountId);
        /// <summary>유예가 지난 계정을 모두 지운다 (토큰 · 외부 로그인 · 이메일 · 닉네임 · 가입 · 게임별 세이브 · 고용 등록 · 우편 · 고용 기록 · 영토 · 공유 녹화). 지운 수.</summary>
        int PurgeDeleted(long nowUnixSeconds);
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
        /// <summary>연합 장부를 잠근 채 읽거나 고친다 (고친 뒤 저장, R-49).</summary>
        T WithAlliances<T>(string gameCode, Func<Dictionary<string, Alliance>, T> use);
        /// <summary>새 id (연합·우편 등).</summary>
        string NewKey(string prefix);

        // ---- 녹화 공유 (게임별, R-81) ----
        /// <summary>올린다 (같은 계정·같은 지문이면 바꿔 넣음). storeMax를 넘으면 오래된 것부터 지운다. Id는 저장소가 정한다.</summary>
        SharedReplaySummary AddSharedReplay(string gameCode, SharedReplaySummary summary, string json, int storeMax);
        List<SharedReplaySummary> SharedReplays(string gameCode);
        /// <summary>(요약, 녹화 JSON). 없으면 (null, null).</summary>
        (SharedReplaySummary summary, string json) GetSharedReplay(string gameCode, string id);

        // ---- 우편 (게임별) ----
        void AddMail(string gameCode, string accountId, Mail mail);
        List<Mail> MailOf(string gameCode, string accountId);
        /// <summary>우편을 지운다 (받음). 없으면 false.</summary>
        bool TakeMail(string gameCode, string accountId, string mailId);

        // ---- 출석 (게임별, R-112) ----
        /// <summary>계정의 출석 기록을 잠근 채 읽거나 고친다 (고친 뒤 저장, 없으면 새 기록). 우편을 보내도 된다.</summary>
        T WithAttendance<T>(string gameCode, string accountId, Func<Game.Flow.AttendanceRecord, T> use);

        // ---- 결제 기록 (게임별, R-116) ----
        /// <summary>반영한 거래 (없으면 null). 같은 거래를 다시 쓰지 못하게 · 복원 때 "이미 반영" 확인.</summary>
        Game.Flow.ReceiptRecord ReceiptOf(string gameCode, string transactionId);
        /// <summary>반영한 결제를 남긴다 (세이브 저장 뒤). 계정을 지워도 남긴다 (같은 거래를 다른 계정에서 다시 못 쓰게).</summary>
        void RecordReceipts(string gameCode, IEnumerable<Game.Flow.ReceiptRecord> records);
        /// <summary>그 계정이 반영한 결제 (복원 · 관리용).</summary>
        List<Game.Flow.ReceiptRecord> ReceiptsOf(string gameCode, string accountId);
    }

    /// <summary>개발용 메모리 저장소 (공통 + 게임별을 한 객체에). 테스트·로컬 실행용.</summary>
    public class InMemoryStore : IPlatformStore, IGameStore
    {
        private readonly object _lock = new object();

        protected sealed class Snapshot
        {
            /// <summary>토큰 → 계정.</summary>
            public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>();
            /// <summary>"제공자:사용자 id" → 계정 id (외부 로그인).</summary>
            public Dictionary<string, string> External { get; set; } = new Dictionary<string, string>();
            /// <summary>계정 → (게임 코드 → 가입 시각).</summary>
            public Dictionary<string, Dictionary<string, long>> Memberships { get; set; } = new Dictionary<string, Dictionary<string, long>>();
            /// <summary>계정 → 확인된 이메일 (외부 로그인, 마스터 확인용).</summary>
            public Dictionary<string, string> Emails { get; set; } = new Dictionary<string, string>();
            /// <summary>계정 → 닉네임.</summary>
            public Dictionary<string, string> Nicknames { get; set; } = new Dictionary<string, string>();
            /// <summary>계정 → 삭제 요청 시각 (R-102).</summary>
            public Dictionary<string, long> Deletions { get; set; } = new Dictionary<string, long>();
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
            /// <summary>게임 코드 → (연합 id → 연합).</summary>
            public Dictionary<string, Dictionary<string, Alliance>> Alliances { get; set; } = new Dictionary<string, Dictionary<string, Alliance>>();
            /// <summary>게임 코드 → 공유 녹화 (올린 순서).</summary>
            public Dictionary<string, List<SharedReplayEntry>> SharedReplays { get; set; } = new Dictionary<string, List<SharedReplayEntry>>();
            /// <summary>게임 코드 → (계정 → 출석 기록, R-112).</summary>
            public Dictionary<string, Dictionary<string, Game.Flow.AttendanceRecord>> Attendance { get; set; } = new Dictionary<string, Dictionary<string, Game.Flow.AttendanceRecord>>();
            /// <summary>게임 코드 → (거래 id → 반영한 결제, R-116).</summary>
            public Dictionary<string, Dictionary<string, Game.Flow.ReceiptRecord>> Receipts { get; set; } = new Dictionary<string, Dictionary<string, Game.Flow.ReceiptRecord>>();
        }

        protected Snapshot Data = new Snapshot();

        public SignInResponse CreateGuest()
        {
            var r = new SignInResponse { AccountId = "acct-" + NewId(8), Token = NewId(24) };
            lock (Fresh())
            {
                Data.Tokens[r.Token] = r.AccountId;
                Data.Memberships[r.AccountId] = new Dictionary<string, long>();
                Persist();
            }
            return r;
        }

        public SignInResponse SignInExternal(string provider, string subject, string email = null)
        {
            string key = provider + ":" + subject;
            lock (Fresh())
            {
                if (!Data.External.TryGetValue(key, out var accountId))
                {
                    accountId = "acct-" + NewId(8);
                    Data.External[key] = accountId;
                    Data.Memberships[accountId] = new Dictionary<string, long>();
                }
                if (Data.Emails == null) Data.Emails = new Dictionary<string, string>(); // 예전 저장 파일
                if (!string.IsNullOrEmpty(email)) Data.Emails[accountId] = email.Trim().ToLowerInvariant();
                var r = new SignInResponse { AccountId = accountId, Token = NewId(24) };
                Data.Tokens[r.Token] = accountId;
                Persist();
                return r;
            }
        }

        public string LinkExternal(string accountId, string provider, string subject, string email = null)
        {
            string key = provider + ":" + subject;
            lock (Fresh())
            {
                if (Data.External.TryGetValue(key, out var owner)) return owner == accountId ? LinkReasons.Ok : LinkReasons.Taken;
                foreach (var kv in Data.External)
                    if (kv.Value == accountId && kv.Key.StartsWith(provider + ":", StringComparison.Ordinal)) return LinkReasons.AlreadyLinked;
                Data.External[key] = accountId;
                if (Data.Emails == null) Data.Emails = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(email)) Data.Emails[accountId] = email.Trim().ToLowerInvariant();
                Persist();
                return LinkReasons.Ok;
            }
        }

        public bool IsLinked(string accountId)
        {
            lock (Fresh())
            {
                foreach (var v in Data.External.Values) if (v == accountId) return true;
                return false;
            }
        }

        public long RequestDeletion(string accountId, long nowUnixSeconds)
        {
            lock (Fresh())
            {
                if (Data.Deletions == null) Data.Deletions = new Dictionary<string, long>();
                if (!Data.Deletions.TryGetValue(accountId, out var at)) { Data.Deletions[accountId] = at = nowUnixSeconds; Persist(); }
                return at + AccountDeletion.GraceSeconds;
            }
        }

        public bool CancelDeletion(string accountId)
        {
            lock (Fresh())
            {
                if (Data.Deletions == null || accountId == null || !Data.Deletions.Remove(accountId)) return false;
                Persist();
                return true;
            }
        }

        public int PurgeDeleted(long nowUnixSeconds)
        {
            lock (Fresh())
            {
                if (Data.Deletions == null || Data.Deletions.Count == 0) return 0;
                var gone = new HashSet<string>();
                foreach (var kv in Data.Deletions) if (nowUnixSeconds >= kv.Value + AccountDeletion.GraceSeconds) gone.Add(kv.Key);
                if (gone.Count == 0) return 0;
                Drop(Data.Tokens, (k, v) => gone.Contains(v));
                Drop(Data.External, (k, v) => gone.Contains(v));
                Drop(Data.Emails, (k, v) => gone.Contains(k));
                Drop(Data.Nicknames, (k, v) => gone.Contains(k));
                Drop(Data.Memberships, (k, v) => gone.Contains(k));
                Drop(Data.Deletions, (k, v) => gone.Contains(k));
                foreach (var g in Data.Saves.Values) Drop(g, (k, v) => gone.Contains(k));
                foreach (var g in Data.Mail.Values) Drop(g, (k, v) => gone.Contains(k));
                foreach (var g in Data.HireLogs.Values) Drop(g, (k, v) => gone.Contains(k));
                if (Data.Attendance != null) foreach (var g in Data.Attendance.Values) Drop(g, (k, v) => gone.Contains(k));
                foreach (var g in Data.Territory.Values) Drop(g, (k, v) => v != null && gone.Contains(v.OwnerAccountId));
                foreach (var l in Data.Listings.Values) l.RemoveAll(x => gone.Contains(x.OwnerAccountId));
                foreach (var l in Data.SharedReplays.Values) l.RemoveAll(x => x.Summary != null && gone.Contains(x.Summary.OwnerAccountId));
                Persist();
                return gone.Count;
            }
        }

        private static void Drop<V>(Dictionary<string, V> d, Func<string, V, bool> hit)
        {
            if (d == null) return;
            var keys = new List<string>();
            foreach (var kv in d) if (hit(kv.Key, kv.Value)) keys.Add(kv.Key);
            foreach (var k in keys) d.Remove(k);
        }

        public string AccountOf(string token)
        {
            if (token == null) return null;
            lock (Fresh()) return Data.Tokens.TryGetValue(token, out var id) ? id : null;
        }

        public bool Join(string accountId, string gameCode, long nowUnixSeconds)
        {
            lock (Fresh())
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
            lock (Fresh())
            {
                var list = new List<string>();
                if (Data.Memberships.TryGetValue(accountId, out var games)) list.AddRange(games.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        public NicknameResponse SetNickname(string accountId, string nickname)
        {
            var n = NicknameRules.Normalize(nickname);
            if (n == null) return new NicknameResponse { Ok = false, Reason = NicknameRules.BadName };
            lock (Fresh())
            {
                if (Data.Nicknames == null) Data.Nicknames = new Dictionary<string, string>();
                foreach (var kv in Data.Nicknames)
                    if (kv.Key != accountId && string.Equals(kv.Value, n, StringComparison.OrdinalIgnoreCase)) return new NicknameResponse { Ok = false, Reason = NicknameRules.Taken };
                Data.Nicknames[accountId] = n;
                Persist();
                return new NicknameResponse { Ok = true, Reason = NicknameRules.Ok, Nickname = n };
            }
        }

        public string EmailOf(string accountId)
        {
            lock (Fresh()) return Data.Emails != null && accountId != null && Data.Emails.TryGetValue(accountId, out var e) ? e : null;
        }

        public string NicknameOf(string accountId)
        {
            lock (Fresh()) return Data.Nicknames != null && accountId != null && Data.Nicknames.TryGetValue(accountId, out var n) ? n : null;
        }

        public Dictionary<string, string> NicknamesOf(IEnumerable<string> accountIds)
        {
            var r = new Dictionary<string, string>();
            lock (Fresh())
                foreach (var id in accountIds)
                    if (id != null && Data.Nicknames != null && Data.Nicknames.TryGetValue(id, out var n)) r[id] = n;
            return r;
        }

        public CloudSave GetSave(string gameCode, string accountId)
        {
            lock (Fresh()) return Data.Saves.TryGetValue(gameCode, out var g) && g.TryGetValue(accountId, out var s) ? Copy(s) : null;
        }

        public (bool ok, CloudSave current) PutSave(string gameCode, string accountId, long baseRevision, string json, long nowUnixSeconds)
        {
            lock (Fresh())
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
            lock (Fresh())
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
            lock (Fresh()) { var l = ListingsOf(gameCode).Find(x => x.Id == listingId); return l == null ? null : Copy(l); }
        }

        public List<HireListing> Listings(string gameCode, string kind)
        {
            lock (Fresh()) return ListingsOf(gameCode).FindAll(l => kind == null || l.Kind == kind).ConvertAll(Copy);
        }

        public bool RemoveListing(string gameCode, string ownerAccountId, string listingId)
        {
            lock (Fresh())
            {
                int n = ListingsOf(gameCode).RemoveAll(l => l.Id == listingId && l.OwnerAccountId == ownerAccountId);
                if (n > 0) Persist();
                return n > 0;
            }
        }

        public void RecordHire(string gameCode, string listingId, Game.Flow.HireOutcome outcome, string stepId, long atUnixSeconds, int recentMax)
        {
            lock (Fresh())
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
            lock (Fresh())
            {
                var log = LogOf(gameCode, accountId);
                log.RemoveAll(e => nowUnixSeconds - e.AtUnixSeconds >= Game.Flow.HireBook.DaySeconds);
                return log.ConvertAll(e => new Game.Flow.HireLogEntry { ListingId = e.ListingId, AtUnixSeconds = e.AtUnixSeconds });
            }
        }

        public void AddHireLog(string gameCode, string accountId, IEnumerable<Game.Flow.HireLogEntry> entries)
        {
            lock (Fresh())
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

        public T WithAlliances<T>(string gameCode, Func<Dictionary<string, Alliance>, T> use)
        {
            lock (Fresh())
            {
                if (Data.Alliances == null) Data.Alliances = new Dictionary<string, Dictionary<string, Alliance>>();
                if (!Data.Alliances.TryGetValue(gameCode, out var g)) Data.Alliances[gameCode] = g = new Dictionary<string, Alliance>();
                var r = use(g);
                Persist();
                return r;
            }
        }

        public string NewKey(string prefix) => prefix + NewId(6);

        private Dictionary<string, Game.Flow.ReceiptRecord> ReceiptBook(string gameCode)
        {
            if (Data.Receipts == null) Data.Receipts = new Dictionary<string, Dictionary<string, Game.Flow.ReceiptRecord>>(); // 예전 저장 파일
            if (!Data.Receipts.TryGetValue(gameCode, out var g)) Data.Receipts[gameCode] = g = new Dictionary<string, Game.Flow.ReceiptRecord>();
            return g;
        }

        public Game.Flow.ReceiptRecord ReceiptOf(string gameCode, string transactionId)
        {
            if (transactionId == null) return null;
            lock (Fresh()) return ReceiptBook(gameCode).TryGetValue(transactionId, out var r) ? r : null;
        }

        public void RecordReceipts(string gameCode, IEnumerable<Game.Flow.ReceiptRecord> records)
        {
            lock (Fresh())
            {
                var book = ReceiptBook(gameCode);
                bool any = false;
                foreach (var r in records) if (r?.TransactionId != null) { book[r.TransactionId] = r; any = true; }
                if (any) Persist();
            }
        }

        public List<Game.Flow.ReceiptRecord> ReceiptsOf(string gameCode, string accountId)
        {
            lock (Fresh())
            {
                var list = new List<Game.Flow.ReceiptRecord>();
                foreach (var r in ReceiptBook(gameCode).Values) if (r.AccountId == accountId) list.Add(r);
                list.Sort((x, y) => x.AtUnixSeconds.CompareTo(y.AtUnixSeconds));
                return list;
            }
        }

        public T WithAttendance<T>(string gameCode, string accountId, Func<Game.Flow.AttendanceRecord, T> use)
        {
            lock (Fresh())
            {
                if (Data.Attendance == null) Data.Attendance = new Dictionary<string, Dictionary<string, Game.Flow.AttendanceRecord>>(); // 예전 저장 파일
                if (!Data.Attendance.TryGetValue(gameCode, out var g)) Data.Attendance[gameCode] = g = new Dictionary<string, Game.Flow.AttendanceRecord>();
                if (!g.TryGetValue(accountId, out var rec)) g[accountId] = rec = new Game.Flow.AttendanceRecord();
                var r = use(rec);
                Persist();
                return r;
            }
        }

        public T WithTerritory<T>(string gameCode, Func<Dictionary<string, TerritoryOwner>, T> use)
        {
            lock (Fresh())
            {
                var r = use(OwnersOf(gameCode));
                Persist();
                return r;
            }
        }

        protected sealed class SharedReplayEntry
        {
            public SharedReplaySummary Summary { get; set; }
            public string Json { get; set; }
        }

        private List<SharedReplayEntry> Shared(string gameCode)
        {
            if (!Data.SharedReplays.TryGetValue(gameCode, out var list)) Data.SharedReplays[gameCode] = list = new List<SharedReplayEntry>();
            return list;
        }

        private static SharedReplaySummary Copy(SharedReplaySummary s) => OnlineJson.Read<SharedReplaySummary>(OnlineJson.Write(s));

        public SharedReplaySummary AddSharedReplay(string gameCode, SharedReplaySummary summary, string json, int storeMax)
        {
            lock (Fresh())
            {
                var list = Shared(gameCode);
                var s = Copy(summary);
                s.Id = "r-" + NewId(8);
                list.RemoveAll(e => e.Summary.OwnerAccountId == s.OwnerAccountId && e.Summary.Hash == s.Hash);
                list.Add(new SharedReplayEntry { Summary = s, Json = json });
                if (storeMax > 0 && list.Count > storeMax) list.RemoveRange(0, list.Count - storeMax);
                Persist();
                return Copy(s);
            }
        }

        public List<SharedReplaySummary> SharedReplays(string gameCode)
        {
            lock (Fresh()) return Shared(gameCode).ConvertAll(e => Copy(e.Summary));
        }

        public (SharedReplaySummary summary, string json) GetSharedReplay(string gameCode, string id)
        {
            lock (Fresh())
            {
                var e = Shared(gameCode).Find(x => x.Summary.Id == id);
                return e == null ? (null, null) : (Copy(e.Summary), e.Json);
            }
        }

        public void AddMail(string gameCode, string accountId, Mail mail)
        {
            lock (Fresh())
            {
                var m = Copy(mail);
                m.Id = "m-" + NewId(6);
                Box(gameCode, accountId).Add(m);
                Persist();
            }
        }

        /// <summary>관리자 페이지 (admin.md): 모든 계정 한 줄씩 (닉네임 · 확인된 이메일 · 구글 연동 · 가입한 게임 · 삭제 요청 시각).</summary>
        public List<AdminAccount> AdminAccounts()
        {
            lock (Fresh())
            {
                var linked = new HashSet<string>(Data.External.Values, StringComparer.Ordinal);
                var list = new List<AdminAccount>();
                foreach (var kv in Data.Memberships)
                    list.Add(new AdminAccount
                    {
                        AccountId = kv.Key,
                        Nickname = Data.Nicknames != null && Data.Nicknames.TryGetValue(kv.Key, out var n) ? n : null,
                        Email = Data.Emails != null && Data.Emails.TryGetValue(kv.Key, out var e) ? e : null,
                        Linked = linked.Contains(kv.Key),
                        Games = new Dictionary<string, long>(kv.Value),
                        DeletionRequestedAtUnixSeconds = Data.Deletions != null && Data.Deletions.TryGetValue(kv.Key, out var d) ? d : 0,
                    });
                list.Sort((a, b) => string.CompareOrdinal(a.AccountId, b.AccountId));
                return list;
            }
        }

        public List<Mail> MailOf(string gameCode, string accountId)
        {
            lock (Fresh()) return Box(gameCode, accountId).ConvertAll(Copy);
        }

        public bool TakeMail(string gameCode, string accountId, string mailId)
        {
            lock (Fresh())
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

        /// <summary>
        /// 저장소를 쓰기 전에 (잠금 안에서) 부른다. 폴더 저장소는 다른 서버(배포가 바뀌는 동안의 옛 · 새 판)가 파일을 바꿨으면 다시 읽는다.
        /// </summary>
        protected virtual void Sync() { }

        /// <summary>잠금 + 최신으로: <c>lock (Fresh())</c>.</summary>
        private object Fresh()
        {
            lock (_lock) Sync();
            return _lock;
        }

        private static CloudSave Copy(CloudSave s) => new CloudSave { Revision = s.Revision, Json = s.Json, SavedAtUnixSeconds = s.SavedAtUnixSeconds };

        private static string NewId(int bytes)
        {
            var b = new byte[bytes];
            RandomNumberGenerator.Fill(b);
            return Convert.ToHexString(b).ToLowerInvariant();
        }
    }

    /// <summary>
    /// 개발용 폴더 저장소: 바뀔 때마다 store.json으로 쓴다 (서버를 껐다 켜도 남는다).
    /// 배포가 바뀌는 동안 옛 · 새 서버가 같은 폴더(Cloud Storage 버킷)를 잠깐 함께 쓴다 (2026-11-16: 새 서버가 켜질 때 읽은 옛 내용으로 덮어써
    /// 방금 받은 우편 · 세이브가 사라짐 → staleSave). 그래서 쓸 때마다 작은 판 표시 파일 store.gen에 새 값을 남기고,
    /// 저장소를 쓰기 전에 store.gen이 내가 마지막에 본 값과 다르면 store.json을 다시 읽는다 (CheckEvery마다 한 번까지).
    /// </summary>
    public sealed class FolderStore : InMemoryStore
    {
        private readonly string _file;
        private readonly string _genFile;
        private string _gen;
        private DateTime _checkedAt = DateTime.MinValue;

        /// <summary>판 표시를 다시 보는 간격 (기본 1초, 테스트는 0).</summary>
        public TimeSpan CheckEvery { get; set; } = TimeSpan.FromSeconds(1);

        public FolderStore(string root)
        {
            Directory.CreateDirectory(root);
            _file = Path.Combine(root, "store.json");
            _genFile = Path.Combine(root, "store.gen");
            _gen = ReadGen();
            Load();
        }

        private void Load()
        {
            if (File.Exists(_file)) Data = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_file)) ?? new Snapshot();
        }

        private string ReadGen()
        {
            try { return File.Exists(_genFile) ? File.ReadAllText(_genFile).Trim() : null; }
            catch (IOException) { return _gen; }
        }

        protected override void Sync()
        {
            var now = DateTime.UtcNow;
            if (now - _checkedAt < CheckEvery) return;
            _checkedAt = now;
            string gen = ReadGen();
            if (gen == null || gen == _gen) return;
            _gen = gen; // 다른 서버가 썼다
            Load();
        }

        protected override void Persist()
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(Data, Formatting.Indented));
            File.Move(tmp, _file, true);
            _gen = Guid.NewGuid().ToString("N");
            File.WriteAllText(_genFile, _gen);
        }
    }
}
