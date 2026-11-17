using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Game.Online;
using Game.Territory;
using Google.Cloud.Firestore;
using Newtonsoft.Json;

namespace Game.Server
{
    /// <summary>
    /// Firestore 저장소 (docs/impl/db.md, 2단계). 설정 Game:Store=firestore, Game:Firestore:ProjectId · DatabaseId.
    /// 문서마다 내용은 JSON 문자열 칸 "j"(지금 store.json과 같은 모양) + 찾기용 칸(owner · kind · lat …).
    /// 읽고 고치는 일(With… · 세이브 · 등록 갱신 …)은 트랜잭션 하나로. 그 안에서 부른 다른 저장소 일(우편 보내기 · 지우기 · 연합 읽기)은
    /// 같은 트랜잭션의 읽기 · 쓰기로 묶인다 (쓰기는 모아 두었다가 끝에 한 번에) — 트랜잭션이 다시 돌아도 우편이 두 번 가지 않는다.
    /// FIRESTORE_EMULATOR_HOST가 있으면 에뮬레이터로 (테스트).
    /// </summary>
    public sealed class FirestoreStore : IStore
    {
        private readonly FirestoreDb _db;

        public FirestoreStore(FirestoreDb db) { _db = db; }

        public static FirestoreStore Create(string projectId, string databaseId = null) =>
            new FirestoreStore(new FirestoreDbBuilder
            {
                ProjectId = projectId,
                DatabaseId = string.IsNullOrWhiteSpace(databaseId) ? "(default)" : databaseId,
                EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction,
            }.Build());

        public string Kind => "firestore";

        // ================= 트랜잭션 · 읽기 · 쓰기 도우미 =================

        private sealed class TxCtx
        {
            public Transaction Tx;
            public readonly List<Action<Transaction>> Writes = new List<Action<Transaction>>();
        }

        private static readonly AsyncLocal<TxCtx> Current = new AsyncLocal<TxCtx>();

        /// <summary>읽고 고치기를 한 트랜잭션으로. 이미 트랜잭션 안이면 그 트랜잭션에 묶는다 (중첩 없음).</summary>
        private T Atomic<T>(Func<T> body)
        {
            if (Current.Value != null) return body();
            return _db.RunTransactionAsync(tx =>
            {
                var ctx = new TxCtx { Tx = tx };
                Current.Value = ctx;
                try
                {
                    var r = body();
                    foreach (var w in ctx.Writes) w(tx);
                    return Task.FromResult(r);
                }
                finally { Current.Value = null; }
            }).GetAwaiter().GetResult();
        }

        private DocumentSnapshot Read(DocumentReference r) =>
            Current.Value?.Tx != null ? Current.Value.Tx.GetSnapshotAsync(r).GetAwaiter().GetResult() : r.GetSnapshotAsync().GetAwaiter().GetResult();

        private IReadOnlyList<DocumentSnapshot> Query(Query q)
        {
            var snap = Current.Value?.Tx != null ? Current.Value.Tx.GetSnapshotAsync(q).GetAwaiter().GetResult() : q.GetSnapshotAsync().GetAwaiter().GetResult();
            return snap.Documents;
        }

        private void Put(DocumentReference r, Dictionary<string, object> d, bool merge = false)
        {
            if (Current.Value != null) { Current.Value.Writes.Add(tx => { if (merge) tx.Set(r, d, SetOptions.MergeAll); else tx.Set(r, d); }); return; }
            (merge ? r.SetAsync(d, SetOptions.MergeAll) : r.SetAsync(d)).GetAwaiter().GetResult();
        }

        private void Del(DocumentReference r)
        {
            if (Current.Value != null) { Current.Value.Writes.Add(tx => tx.Delete(r)); return; }
            r.DeleteAsync().GetAwaiter().GetResult();
        }

        private static string J(object o) => JsonConvert.SerializeObject(o);
        private static T FromJ<T>(DocumentSnapshot s, string field = "j") where T : class =>
            s != null && s.Exists && s.TryGetValue<string>(field, out var j) && j != null ? JsonConvert.DeserializeObject<T>(j) : null;
        private static string Str(DocumentSnapshot s, string field) => s != null && s.Exists && s.TryGetValue<string>(field, out var v) ? v : null;

        /// <summary>문서 id에 못 쓰는 '/'를 바꾼다.</summary>
        private static string Id(string s) => (s ?? "").Replace("%", "%25").Replace("/", "%2F");
        private static string NewId(int bytes)
        {
            var b = new byte[bytes];
            RandomNumberGenerator.Fill(b);
            return Convert.ToHexString(b).ToLowerInvariant();
        }

        private static long Seq() => DateTime.UtcNow.Ticks; // 같은 모음 안 넣은 순서 (정렬용)

        private DocumentReference Token(string t) => _db.Collection("tokens").Document(Id(t));
        private DocumentReference Login(string key) => _db.Collection("logins").Document(Id(key));
        private DocumentReference Account(string a) => _db.Collection("accounts").Document(Id(a));
        private DocumentReference NickIndex(string lower) => _db.Collection("nicknames").Document(Id(lower));
        private DocumentReference StatusDoc => _db.Collection("config").Document("status");
        private DocumentReference GameDoc(string g) => _db.Collection("games").Document(Id(g));
        private CollectionReference G(string g, string name) => GameDoc(g).Collection(name);
        private DocumentReference Player(string g, string a) => G(g, "players").Document(Id(a));

        // ================= 계정 (공통) =================

        /// <summary>계정 문서 (j): 이메일 · 닉네임 · 가입 게임 · 삭제 요청 · 붙은 외부 로그인.</summary>
        public sealed class AccountDoc
        {
            public string Email { get; set; }
            public string Nickname { get; set; }
            public Dictionary<string, long> Games { get; set; } = new Dictionary<string, long>();
            public long DeletionAt { get; set; }
            public List<string> Providers { get; set; } = new List<string>();
            /// <summary>등급 (H8, 일반 = null).</summary>
            public string Role { get; set; }
        }

        private AccountDoc ReadAccount(string a) => a == null ? null : FromJ<AccountDoc>(Read(Account(a)));

        private void PutAccount(string a, AccountDoc d) =>
            Put(Account(a), new Dictionary<string, object> { ["j"] = J(d), ["del"] = d.DeletionAt, ["nick"] = d.Nickname?.ToLowerInvariant() });

        public SignInResponse CreateGuest()
        {
            var r = new SignInResponse { AccountId = "acct-" + NewId(8), Token = NewId(24) };
            Atomic(() =>
            {
                PutAccount(r.AccountId, new AccountDoc());
                Put(Token(r.Token), new Dictionary<string, object> { ["a"] = r.AccountId });
                return 0;
            });
            return r;
        }

        public SignInResponse SignInExternal(string provider, string subject, string email = null)
        {
            string key = provider + ":" + subject;
            return Atomic(() =>
            {
                string a = Str(Read(Login(key)), "a");
                AccountDoc acct;
                if (a == null)
                {
                    a = "acct-" + NewId(8);
                    acct = new AccountDoc();
                    Put(Login(key), new Dictionary<string, object> { ["a"] = a });
                }
                else acct = ReadAccount(a) ?? new AccountDoc();
                if (!acct.Providers.Contains(provider)) acct.Providers.Add(provider);
                if (!string.IsNullOrEmpty(email)) acct.Email = email.Trim().ToLowerInvariant();
                PutAccount(a, acct);
                var r = new SignInResponse { AccountId = a, Token = NewId(24) };
                Put(Token(r.Token), new Dictionary<string, object> { ["a"] = a });
                return r;
            });
        }

        public string LinkExternal(string accountId, string provider, string subject, string email = null)
        {
            string key = provider + ":" + subject;
            return Atomic(() =>
            {
                string owner = Str(Read(Login(key)), "a");
                if (owner != null) return owner == accountId ? LinkReasons.Ok : LinkReasons.Taken;
                var acct = ReadAccount(accountId) ?? new AccountDoc();
                if (acct.Providers.Contains(provider)) return LinkReasons.AlreadyLinked;
                Put(Login(key), new Dictionary<string, object> { ["a"] = accountId });
                acct.Providers.Add(provider);
                if (!string.IsNullOrEmpty(email)) acct.Email = email.Trim().ToLowerInvariant();
                PutAccount(accountId, acct);
                return LinkReasons.Ok;
            });
        }

        public bool IsLinked(string accountId) => (ReadAccount(accountId)?.Providers.Count ?? 0) > 0;
        public string EmailOf(string accountId) => ReadAccount(accountId)?.Email;
        public string NicknameOf(string accountId) => ReadAccount(accountId)?.Nickname;
        public string AccountOf(string token) => token == null ? null : Str(Read(Token(token)), "a");

        public Dictionary<string, string> NicknamesOf(IEnumerable<string> accountIds)
        {
            var r = new Dictionary<string, string>();
            var ids = accountIds.Where(x => x != null).Distinct().ToList();
            if (ids.Count == 0) return r;
            var snaps = _db.GetAllSnapshotsAsync(ids.Select(Account)).GetAwaiter().GetResult();
            for (int i = 0; i < ids.Count; i++)
            {
                var n = FromJ<AccountDoc>(snaps[i])?.Nickname;
                if (n != null) r[ids[i]] = n;
            }
            return r;
        }

        public bool Join(string accountId, string gameCode, long nowUnixSeconds) => Atomic(() =>
        {
            var acct = ReadAccount(accountId) ?? new AccountDoc();
            if (acct.Games.ContainsKey(gameCode)) return false;
            acct.Games[gameCode] = nowUnixSeconds;
            PutAccount(accountId, acct);
            return true;
        });

        public IReadOnlyList<string> GamesOf(string accountId)
        {
            var list = new List<string>(ReadAccount(accountId)?.Games.Keys ?? Enumerable.Empty<string>());
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public NicknameResponse SetNickname(string accountId, string nickname)
        {
            var n = NicknameRules.Normalize(nickname);
            if (n == null) return new NicknameResponse { Ok = false, Reason = NicknameRules.BadName };
            string lower = n.ToLowerInvariant();
            return Atomic(() =>
            {
                string holder = Str(Read(NickIndex(lower)), "a");
                if (holder != null && holder != accountId) return new NicknameResponse { Ok = false, Reason = NicknameRules.Taken };
                var acct = ReadAccount(accountId) ?? new AccountDoc();
                string oldLower = acct.Nickname?.ToLowerInvariant();
                if (oldLower != null && oldLower != lower) Del(NickIndex(oldLower));
                Put(NickIndex(lower), new Dictionary<string, object> { ["a"] = accountId });
                acct.Nickname = n;
                PutAccount(accountId, acct);
                return new NicknameResponse { Ok = true, Reason = NicknameRules.Ok, Nickname = n };
            });
        }

        public string RoleOf(string accountId) => ReadAccount(accountId)?.Role;

        public bool SetRole(string accountId, string role)
        {
            if (accountId == null || !AccountRoles.IsValid(role)) return false;
            return Atomic(() =>
            {
                var acct = ReadAccount(accountId);
                if (acct == null) return false;
                acct.Role = role;
                PutAccount(accountId, acct);
                return true;
            });
        }

        public long RequestDeletion(string accountId, long nowUnixSeconds) => Atomic(() =>
        {
            var acct = ReadAccount(accountId) ?? new AccountDoc();
            if (acct.DeletionAt <= 0) { acct.DeletionAt = nowUnixSeconds; PutAccount(accountId, acct); }
            return acct.DeletionAt + AccountDeletion.GraceSeconds;
        });

        public bool CancelDeletion(string accountId) => accountId != null && Atomic(() =>
        {
            var acct = ReadAccount(accountId);
            if (acct == null || acct.DeletionAt <= 0) return false;
            acct.DeletionAt = 0;
            PutAccount(accountId, acct);
            return true;
        });

        public int PurgeDeleted(long nowUnixSeconds)
        {
            var due = _db.Collection("accounts").WhereGreaterThan("del", 0L).WhereLessThanOrEqualTo("del", nowUnixSeconds - AccountDeletion.GraceSeconds)
                .GetSnapshotAsync().GetAwaiter().GetResult().Documents;
            int n = 0;
            foreach (var doc in due)
            {
                string a = doc.Id.Replace("%2F", "/").Replace("%25", "%");
                var acct = FromJ<AccountDoc>(doc) ?? new AccountDoc();
                foreach (var t in _db.Collection("tokens").WhereEqualTo("a", a).GetSnapshotAsync().GetAwaiter().GetResult().Documents) t.Reference.DeleteAsync().GetAwaiter().GetResult();
                foreach (var l in _db.Collection("logins").WhereEqualTo("a", a).GetSnapshotAsync().GetAwaiter().GetResult().Documents) l.Reference.DeleteAsync().GetAwaiter().GetResult();
                if (acct.Nickname != null) NickIndex(acct.Nickname.ToLowerInvariant()).DeleteAsync().GetAwaiter().GetResult();
                foreach (var g in acct.Games.Keys) PurgeGame(g, a);
                doc.Reference.DeleteAsync().GetAwaiter().GetResult();
                n++;
            }
            return n;
        }

        /// <summary>계정 하나의 게임 자료를 지운다 (결제 기록은 남긴다 — 같은 거래를 다른 계정에서 다시 못 쓰게).</summary>
        private void PurgeGame(string g, string a)
        {
            void DeleteAll(Query q) { foreach (var d in q.GetSnapshotAsync().GetAwaiter().GetResult().Documents) d.Reference.DeleteAsync().GetAwaiter().GetResult(); }
            G(g, "saves").Document(Id(a)).DeleteAsync().GetAwaiter().GetResult();
            DeleteAll(Player(g, a).Collection("mail"));
            DeleteAll(Player(g, a).Collection("hireLog"));
            Player(g, a).DeleteAsync().GetAwaiter().GetResult();
            DeleteAll(G(g, "territory").WhereEqualTo("owner", a));
            DeleteAll(G(g, "listings").WhereEqualTo("owner", a));
            DeleteAll(G(g, "sharedReplays").WhereEqualTo("owner", a));
            foreach (var d in G(g, "hireReplays").WhereArrayContains("viewers", a).GetSnapshotAsync().GetAwaiter().GetResult().Documents)
            {
                var viewers = d.TryGetValue<List<string>>("viewers", out var v) ? v : new List<string>();
                viewers.Remove(a);
                if (viewers.Count == 0) d.Reference.DeleteAsync().GetAwaiter().GetResult();
                else d.Reference.UpdateAsync("viewers", viewers).GetAwaiter().GetResult();
            }
        }

        // ---- 운영 켜고 끄기 (R-122): games/{게임}/config/content ----
        public ContentToggles ContentOf(string gameCode) => FromJ<ContentToggles>(Read(G(gameCode, "config").Document("content"))) ?? new ContentToggles();

        public void SetContent(string gameCode, ContentToggles content)
        {
            var r = G(gameCode, "config").Document("content");
            if (content == null || content.Items == null || content.Items.Count == 0) Del(r);
            else Put(r, new Dictionary<string, object> { ["j"] = J(content) });
        }

        public ServerStatusResponse StatusOverride() => FromJ<ServerStatusResponse>(Read(StatusDoc));

        public void SetStatusOverride(ServerStatusResponse status)
        {
            if (status == null) Del(StatusDoc);
            else Put(StatusDoc, new Dictionary<string, object> { ["j"] = J(status) });
        }

        public List<AdminAccount> AdminAccounts()
        {
            var list = new List<AdminAccount>();
            foreach (var d in Query(_db.Collection("accounts")))
            {
                var acct = FromJ<AccountDoc>(d) ?? new AccountDoc();
                list.Add(new AdminAccount
                {
                    AccountId = d.Id.Replace("%2F", "/").Replace("%25", "%"),
                    Nickname = acct.Nickname,
                    Email = acct.Email,
                    Linked = acct.Providers.Count > 0,
                    Games = new Dictionary<string, long>(acct.Games),
                    DeletionRequestedAtUnixSeconds = acct.DeletionAt,
                    Role = acct.Role,
                });
            }
            list.Sort((x, y) => string.CompareOrdinal(x.AccountId, y.AccountId));
            return list;
        }

        // ================= 세이브 =================

        public CloudSave GetSave(string gameCode, string accountId) => FromJ<CloudSave>(Read(G(gameCode, "saves").Document(Id(accountId))));

        public (bool ok, CloudSave current) PutSave(string gameCode, string accountId, long baseRevision, string json, long nowUnixSeconds) => Atomic(() =>
        {
            var r = G(gameCode, "saves").Document(Id(accountId));
            var cur = FromJ<CloudSave>(Read(r));
            long rev = cur?.Revision ?? 0;
            if (rev != baseRevision) return (false, cur);
            var next = new CloudSave { Revision = rev + 1, Json = json, SavedAtUnixSeconds = nowUnixSeconds };
            Put(r, new Dictionary<string, object> { ["j"] = J(next) });
            return (true, next);
        });

        // ================= 고용 등록 =================

        private void PutListing(string g, HireListing l, long pos) =>
            Put(G(g, "listings").Document(Id(l.Id)), new Dictionary<string, object> { ["j"] = J(l), ["kind"] = l.Kind, ["owner"] = l.OwnerAccountId, ["source"] = l.SourceId, ["pos"] = pos });

        public HireListing UpsertListing(string gameCode, HireListing listing) => Atomic(() =>
        {
            var same = Query(G(gameCode, "listings").WhereEqualTo("owner", listing.OwnerAccountId).WhereEqualTo("kind", listing.Kind).WhereEqualTo("source", listing.SourceId));
            var next = JsonConvert.DeserializeObject<HireListing>(J(listing));
            long pos = Seq();
            if (same.Count > 0)
            {
                var old = FromJ<HireListing>(same[0]);
                next.Id = old.Id;
                next.Version = old.Version + 1;
                if (same[0].TryGetValue<long>("pos", out var p)) pos = p; // 목록 순서 그대로
            }
            else { next.Id = "h-" + NewId(6); next.Version = 1; }
            PutListing(gameCode, next, pos);
            return next;
        });

        public HireListing GetListing(string gameCode, string listingId) => listingId == null ? null : FromJ<HireListing>(Read(G(gameCode, "listings").Document(Id(listingId))));

        public List<HireListing> Listings(string gameCode, string kind)
        {
            Query q = G(gameCode, "listings");
            if (kind != null) q = q.WhereEqualTo("kind", kind);
            return Query(q).OrderBy(d => d.TryGetValue<long>("pos", out var p) ? p : 0).Select(d => FromJ<HireListing>(d)).ToList();
        }

        public bool RemoveListing(string gameCode, string ownerAccountId, string listingId) => Atomic(() =>
        {
            var r = G(gameCode, "listings").Document(Id(listingId ?? ""));
            var l = FromJ<HireListing>(Read(r));
            if (l == null || l.OwnerAccountId != ownerAccountId) return false;
            Del(r);
            return true;
        });

        public void RecordHire(string gameCode, string listingId, Game.Flow.HireOutcome outcome, string stepId, long atUnixSeconds, int recentMax, string replayId = null) => Atomic(() =>
        {
            var r = G(gameCode, "listings").Document(Id(listingId ?? ""));
            var snap = Read(r);
            var l = FromJ<HireListing>(snap);
            if (l == null) return 0; // 그사이 해제됨
            Game.Flow.HireBook.Record(l, outcome, stepId, atUnixSeconds, recentMax, replayId);
            PutListing(gameCode, l, snap.TryGetValue<long>("pos", out var p) ? p : Seq());
            return 0;
        });

        public string AddHireReplay(string gameCode, string json, IEnumerable<string> viewers, long atUnixSeconds, int storeMax)
        {
            string id = "hr-" + NewId(8);
            Put(G(gameCode, "hireReplays").Document(id), new Dictionary<string, object> { ["j"] = json, ["viewers"] = viewers.Distinct().ToList(), ["at"] = atUnixSeconds, ["pos"] = Seq() });
            if (storeMax > 0 && Current.Value == null)
                foreach (var old in G(gameCode, "hireReplays").OrderByDescending("pos").Offset(storeMax).Limit(50).GetSnapshotAsync().GetAwaiter().GetResult().Documents)
                    old.Reference.DeleteAsync().GetAwaiter().GetResult();
            return id;
        }

        public string HireReplayFor(string gameCode, string id, string accountId)
        {
            var s = Read(G(gameCode, "hireReplays").Document(Id(id ?? "")));
            if (!s.Exists || !s.TryGetValue<List<string>>("viewers", out var v) || !v.Contains(accountId)) return null;
            return Str(s, "j");
        }

        public List<Game.Flow.HireLogEntry> HireLog(string gameCode, string accountId, long nowUnixSeconds)
        {
            var list = new List<Game.Flow.HireLogEntry>();
            foreach (var d in Query(Player(gameCode, accountId).Collection("hireLog")))
            {
                var e = FromJ<Game.Flow.HireLogEntry>(d);
                if (e == null) continue;
                if (nowUnixSeconds - e.AtUnixSeconds >= Game.Flow.HireBook.DaySeconds) { Del(d.Reference); continue; }
                list.Add(e);
            }
            list.Sort((x, y) => x.AtUnixSeconds.CompareTo(y.AtUnixSeconds));
            return list;
        }

        public void AddHireLog(string gameCode, string accountId, IEnumerable<Game.Flow.HireLogEntry> entries)
        {
            foreach (var e in entries)
                Put(Player(gameCode, accountId).Collection("hireLog").Document(NewId(8)), new Dictionary<string, object> { ["j"] = J(e), ["at"] = e.AtUnixSeconds });
        }

        // ================= 영토 · 연합 =================

        private void PutTerritory(string g, string key, TerritoryOwner o)
        {
            var d = new Dictionary<string, object> { ["j"] = J(o), ["owner"] = o.OwnerAccountId, ["coll"] = o.CollectedUnixSeconds };
            if (FlagDefs.TryParseKey(key, out var cell)) { d["lat"] = cell.LatE6; d["lon"] = cell.LonE6; }
            Put(G(g, "territory").Document(Id(key)), d);
        }

        private static string KeyOf(DocumentSnapshot d) => d.Id.Replace("%2F", "/").Replace("%25", "%");

        public T WithTerritory<T>(string gameCode, TerritoryScope scope, Func<Dictionary<string, TerritoryOwner>, T> use) => Atomic(() =>
        {
            var col = G(gameCode, "territory");
            var docs = new Dictionary<string, DocumentSnapshot>();
            void Take(IEnumerable<DocumentSnapshot> ds) { foreach (var d in ds) if (d.Exists) docs[KeyOf(d)] = d; }
            if (scope.IsAll) Take(Query(col));
            else
            {
                foreach (var k in scope.KeyList) Take(new[] { Read(col.Document(Id(k))) });
                foreach (var a in scope.OwnerList) Take(Query(col.WhereEqualTo("owner", a)));
                foreach (var (fd, cell, r) in scope.NearList)
                {
                    long dLat = r * Game.Foundation.Geo.GeoPoint.Scale / Math.Max(1, fd.MetersPerDegreeLat) + fd.CellLatE6;
                    Take(Query(col.WhereGreaterThanOrEqualTo("lat", cell.LatE6 - dLat).WhereLessThanOrEqualTo("lat", cell.LatE6 + dLat)));
                }
                if (scope.CollectedBefore != null) Take(Query(col.WhereLessThanOrEqualTo("coll", scope.CollectedBefore.Value)));
            }
            var part = new Dictionary<string, TerritoryOwner>();
            foreach (var kv in docs)
            {
                var o = FromJ<TerritoryOwner>(kv.Value);
                if (o != null && scope.Matches(kv.Key, o)) part[kv.Key] = o;
            }
            var loaded = new List<string>(part.Keys);
            var result = use(part);
            foreach (var k in loaded) if (!part.ContainsKey(k)) Del(col.Document(Id(k)));
            foreach (var kv in part) PutTerritory(gameCode, kv.Key, kv.Value);
            return result;
        });

        public T WithAlliances<T>(string gameCode, Func<Dictionary<string, Alliance>, T> use) => Atomic(() =>
        {
            var col = G(gameCode, "alliances");
            var all = new Dictionary<string, Alliance>();
            foreach (var d in Query(col)) { var a = FromJ<Alliance>(d); if (a != null) all[KeyOf(d)] = a; }
            var loaded = new List<string>(all.Keys);
            var before = all.ToDictionary(kv => kv.Key, kv => J(kv.Value));
            var result = use(all);
            foreach (var k in loaded) if (!all.ContainsKey(k)) Del(col.Document(Id(k)));
            foreach (var kv in all)
            {
                string j = J(kv.Value);
                if (before.TryGetValue(kv.Key, out var old) && old == j) continue; // 바뀐 연합만 쓴다
                Put(col.Document(Id(kv.Key)), new Dictionary<string, object> { ["j"] = j });
            }
            return result;
        });

        public string NewKey(string prefix) => prefix + NewId(6);

        // ================= 공유 녹화 =================

        public SharedReplaySummary AddSharedReplay(string gameCode, SharedReplaySummary summary, string json, int storeMax)
        {
            var s = JsonConvert.DeserializeObject<SharedReplaySummary>(J(summary));
            s.Id = "r-" + NewId(8);
            var col = G(gameCode, "sharedReplays");
            Atomic(() =>
            {
                foreach (var d in Query(col.WhereEqualTo("owner", s.OwnerAccountId).WhereEqualTo("hash", s.Hash))) Del(d.Reference);
                Put(col.Document(s.Id), new Dictionary<string, object> { ["s"] = J(s), ["j"] = json, ["owner"] = s.OwnerAccountId, ["hash"] = s.Hash, ["step"] = s.StepId, ["pos"] = Seq() });
                return 0;
            });
            if (storeMax > 0 && Current.Value == null)
                foreach (var old in col.OrderByDescending("pos").Offset(storeMax).Limit(50).GetSnapshotAsync().GetAwaiter().GetResult().Documents)
                    old.Reference.DeleteAsync().GetAwaiter().GetResult();
            return s;
        }

        public List<SharedReplaySummary> SharedReplays(string gameCode) =>
            Query(G(gameCode, "sharedReplays")).OrderBy(d => d.TryGetValue<long>("pos", out var p) ? p : 0).Select(d => FromJ<SharedReplaySummary>(d, "s")).Where(x => x != null).ToList();

        public (SharedReplaySummary summary, string json) GetSharedReplay(string gameCode, string id)
        {
            var d = Read(G(gameCode, "sharedReplays").Document(Id(id ?? "")));
            return d.Exists ? (FromJ<SharedReplaySummary>(d, "s"), Str(d, "j")) : (null, null);
        }

        // ================= 우편 =================

        public void AddMail(string gameCode, string accountId, Mail mail)
        {
            var m = JsonConvert.DeserializeObject<Mail>(J(mail));
            m.Id = "m-" + NewId(6);
            Put(Player(gameCode, accountId).Collection("mail").Document(m.Id), new Dictionary<string, object> { ["j"] = J(m), ["pos"] = Seq() });
        }

        public List<Mail> MailOf(string gameCode, string accountId) =>
            Query(Player(gameCode, accountId).Collection("mail")).OrderBy(d => d.TryGetValue<long>("pos", out var p) ? p : 0).Select(d => FromJ<Mail>(d)).Where(x => x != null).ToList();

        public bool TakeMail(string gameCode, string accountId, string mailId)
        {
            if (mailId == null) return false;
            var r = Player(gameCode, accountId).Collection("mail").Document(Id(mailId));
            if (!Read(r).Exists) return false;
            Del(r);
            return true;
        }

        // ================= 출석 · 푸시 · 결제 기록 =================

        public T WithAttendance<T>(string gameCode, string accountId, Func<Game.Flow.AttendanceRecord, T> use) => Atomic(() =>
        {
            var r = Player(gameCode, accountId);
            var rec = FromJ<Game.Flow.AttendanceRecord>(Read(r), "att") ?? new Game.Flow.AttendanceRecord();
            var result = use(rec);
            Put(r, new Dictionary<string, object> { ["att"] = J(rec) }, merge: true);
            return result;
        });

        public T WithPush<T>(string gameCode, string accountId, Func<PushProfile, T> use) => Atomic(() =>
        {
            var r = Player(gameCode, accountId ?? "");
            var p = FromJ<PushProfile>(Read(r), "push") ?? new PushProfile();
            if (p.Settings == null) p.Settings = new PushSettings();
            if (p.Tokens == null) p.Tokens = new List<PushTokenEntry>();
            var result = use(p);
            Put(r, new Dictionary<string, object> { ["push"] = J(p) }, merge: true);
            return result;
        });

        public Game.Flow.ReceiptRecord ReceiptOf(string gameCode, string transactionId) =>
            transactionId == null ? null : FromJ<Game.Flow.ReceiptRecord>(Read(G(gameCode, "receipts").Document(Id(transactionId))));

        public void RecordReceipts(string gameCode, IEnumerable<Game.Flow.ReceiptRecord> records)
        {
            foreach (var r in records)
                if (r?.TransactionId != null)
                    Put(G(gameCode, "receipts").Document(Id(r.TransactionId)), new Dictionary<string, object> { ["j"] = J(r), ["a"] = r.AccountId });
        }

        public List<Game.Flow.ReceiptRecord> ReceiptsOf(string gameCode, string accountId)
        {
            var list = Query(G(gameCode, "receipts").WhereEqualTo("a", accountId)).Select(d => FromJ<Game.Flow.ReceiptRecord>(d)).Where(x => x != null).ToList();
            list.Sort((x, y) => x.AtUnixSeconds.CompareTo(y.AtUnixSeconds));
            return list;
        }

        // ================= 옮기기 (db.md 6장, 3단계) =================

        /// <summary>store.json(폴더 저장소) 내용을 그대로 문서로 쓴다. 두 번 돌려도 같은 결과 (덮어쓰기). 모음별로 쓴 수.</summary>
        public Dictionary<string, int> Import(InMemoryStore.Snapshot s)
        {
            var counts = new Dictionary<string, int>();
            var batch = new BatchWriter(_db);
            void W(string what, DocumentReference r, Dictionary<string, object> d) { batch.Set(r, d); counts[what] = (counts.TryGetValue(what, out var n) ? n : 0) + 1; }

            // 계정: 가입 · 이메일 · 닉네임 · 삭제 요청 · 붙은 외부 로그인
            var accounts = new Dictionary<string, AccountDoc>();
            AccountDoc Acct(string a) { if (!accounts.TryGetValue(a, out var d)) accounts[a] = d = new AccountDoc(); return d; }
            foreach (var kv in s.Memberships ?? new Dictionary<string, Dictionary<string, long>>()) Acct(kv.Key).Games = new Dictionary<string, long>(kv.Value ?? new Dictionary<string, long>());
            foreach (var kv in s.Emails ?? new Dictionary<string, string>()) Acct(kv.Key).Email = kv.Value;
            foreach (var kv in s.Nicknames ?? new Dictionary<string, string>()) Acct(kv.Key).Nickname = kv.Value;
            foreach (var kv in s.Deletions ?? new Dictionary<string, long>()) Acct(kv.Key).DeletionAt = kv.Value;
            foreach (var kv in s.Roles ?? new Dictionary<string, string>()) Acct(kv.Key).Role = kv.Value;
            foreach (var kv in s.External ?? new Dictionary<string, string>())
            {
                string provider = kv.Key.Contains(':') ? kv.Key.Substring(0, kv.Key.IndexOf(':')) : kv.Key;
                if (!Acct(kv.Value).Providers.Contains(provider)) Acct(kv.Value).Providers.Add(provider);
                W("logins", Login(kv.Key), new Dictionary<string, object> { ["a"] = kv.Value });
            }
            foreach (var kv in s.Tokens ?? new Dictionary<string, string>()) W("tokens", Token(kv.Key), new Dictionary<string, object> { ["a"] = kv.Value });
            foreach (var kv in accounts)
            {
                W("accounts", Account(kv.Key), new Dictionary<string, object> { ["j"] = J(kv.Value), ["del"] = kv.Value.DeletionAt, ["nick"] = kv.Value.Nickname?.ToLowerInvariant() });
                if (kv.Value.Nickname != null) W("nicknames", NickIndex(kv.Value.Nickname.ToLowerInvariant()), new Dictionary<string, object> { ["a"] = kv.Key });
            }
            if (s.StatusOverride != null) W("config", StatusDoc, new Dictionary<string, object> { ["j"] = J(s.StatusOverride) });
            foreach (var kv in s.Content ?? new Dictionary<string, ContentToggles>())
                if (kv.Value?.Items != null && kv.Value.Items.Count > 0) W("content", G(kv.Key, "config").Document("content"), new Dictionary<string, object> { ["j"] = J(kv.Value) });

            // 게임별
            foreach (var g in s.Saves ?? new Dictionary<string, Dictionary<string, CloudSave>>())
                foreach (var kv in g.Value) W("saves", G(g.Key, "saves").Document(Id(kv.Key)), new Dictionary<string, object> { ["j"] = J(kv.Value) });
            long seq = 0;
            foreach (var g in s.Listings ?? new Dictionary<string, List<HireListing>>())
                foreach (var l in g.Value)
                    W("listings", G(g.Key, "listings").Document(Id(l.Id)), new Dictionary<string, object> { ["j"] = J(l), ["kind"] = l.Kind, ["owner"] = l.OwnerAccountId, ["source"] = l.SourceId, ["pos"] = ++seq });
            foreach (var g in s.Mail ?? new Dictionary<string, Dictionary<string, List<Mail>>>())
                foreach (var kv in g.Value)
                    foreach (var m in kv.Value)
                        W("mail", Player(g.Key, kv.Key).Collection("mail").Document(Id(m.Id ?? "m-" + NewId(6))), new Dictionary<string, object> { ["j"] = J(m), ["pos"] = ++seq });
            foreach (var g in s.HireLogs ?? new Dictionary<string, Dictionary<string, List<Game.Flow.HireLogEntry>>>())
                foreach (var kv in g.Value)
                    foreach (var e in kv.Value)
                        W("hireLog", Player(g.Key, kv.Key).Collection("hireLog").Document(NewId(8)), new Dictionary<string, object> { ["j"] = J(e), ["at"] = e.AtUnixSeconds });
            foreach (var g in s.Territory ?? new Dictionary<string, Dictionary<string, TerritoryOwner>>())
                foreach (var kv in g.Value)
                {
                    var d = new Dictionary<string, object> { ["j"] = J(kv.Value), ["owner"] = kv.Value.OwnerAccountId, ["coll"] = kv.Value.CollectedUnixSeconds };
                    if (FlagDefs.TryParseKey(kv.Key, out var cell)) { d["lat"] = cell.LatE6; d["lon"] = cell.LonE6; }
                    W("territory", G(g.Key, "territory").Document(Id(kv.Key)), d);
                }
            foreach (var g in s.Alliances ?? new Dictionary<string, Dictionary<string, Alliance>>())
                foreach (var kv in g.Value) W("alliances", G(g.Key, "alliances").Document(Id(kv.Key)), new Dictionary<string, object> { ["j"] = J(kv.Value) });
            foreach (var g in s.SharedReplays ?? new Dictionary<string, List<InMemoryStore.SharedReplayEntry>>())
                foreach (var e in g.Value)
                    if (e?.Summary?.Id != null)
                        W("sharedReplays", G(g.Key, "sharedReplays").Document(Id(e.Summary.Id)), new Dictionary<string, object> { ["s"] = J(e.Summary), ["j"] = e.Json, ["owner"] = e.Summary.OwnerAccountId, ["hash"] = e.Summary.Hash, ["step"] = e.Summary.StepId, ["pos"] = ++seq });
            foreach (var g in s.HireReplays ?? new Dictionary<string, List<InMemoryStore.HireReplayEntry>>())
                foreach (var e in g.Value)
                    W("hireReplays", G(g.Key, "hireReplays").Document(Id(e.Id)), new Dictionary<string, object> { ["j"] = e.Json, ["viewers"] = e.Viewers ?? new List<string>(), ["at"] = e.AtUnixSeconds, ["pos"] = ++seq });
            var players = new Dictionary<(string g, string a), Dictionary<string, object>>();
            foreach (var g in s.Attendance ?? new Dictionary<string, Dictionary<string, Game.Flow.AttendanceRecord>>())
                foreach (var kv in g.Value) players[(g.Key, kv.Key)] = new Dictionary<string, object> { ["att"] = J(kv.Value) };
            foreach (var g in s.Push ?? new Dictionary<string, Dictionary<string, PushProfile>>())
                foreach (var kv in g.Value)
                {
                    if (!players.TryGetValue((g.Key, kv.Key), out var d)) players[(g.Key, kv.Key)] = d = new Dictionary<string, object>();
                    d["push"] = J(kv.Value);
                }
            foreach (var kv in players) W("players", Player(kv.Key.g, kv.Key.a), kv.Value);
            foreach (var g in s.Receipts ?? new Dictionary<string, Dictionary<string, Game.Flow.ReceiptRecord>>())
                foreach (var kv in g.Value) W("receipts", G(g.Key, "receipts").Document(Id(kv.Key)), new Dictionary<string, object> { ["j"] = J(kv.Value), ["a"] = kv.Value.AccountId });
            batch.Flush();
            return counts;
        }

        /// <summary>쓰기를 400개씩 묶어 보낸다 (Firestore 한 묶음 최대 500).</summary>
        private sealed class BatchWriter
        {
            private readonly FirestoreDb _db;
            private WriteBatch _batch;
            private int _n;
            public BatchWriter(FirestoreDb db) { _db = db; _batch = db.StartBatch(); }
            public void Set(DocumentReference r, Dictionary<string, object> d)
            {
                _batch.Set(r, d);
                if (++_n >= 400) Flush();
            }
            public void Flush()
            {
                if (_n == 0) return;
                _batch.CommitAsync().GetAwaiter().GetResult();
                _batch = _db.StartBatch();
                _n = 0;
            }
        }
    }
}
