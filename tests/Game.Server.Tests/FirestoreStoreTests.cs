using System;
using System.Collections.Generic;
using System.Linq;
using Game.Online;
using Game.Server;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>에뮬레이터가 있을 때만 (FIRESTORE_EMULATOR_HOST, CI firestore-tests). 없으면 건너뜀.</summary>
    public sealed class FirestoreFactAttribute : FactAttribute
    {
        public FirestoreFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST"))) Skip = "Firestore 에뮬레이터 없음 (CI firestore-tests에서 돈다)";
        }
    }

    /// <summary>Firestore 저장소 (db.md 2 · 3단계): 폴더 저장소와 같은 동작, 트랜잭션 안의 우편, store.json 옮기기.</summary>
    public class FirestoreStoreTests
    {
        private static FirestoreStore New() => FirestoreStore.Create("demo-test");
        private static string G() => "t" + Guid.NewGuid().ToString("N").Substring(0, 8); // 테스트마다 다른 게임 코드

        [FirestoreFact(DisplayName = "Firestore 계정: 게스트 · 외부 로그인(같은 계정 다시) · 연동 · 닉네임 중복 · 가입 · 삭제 요청 취소")]
        public void Accounts()
        {
            var s = New();
            var guest = s.CreateGuest();
            Assert.Equal(guest.AccountId, s.AccountOf(guest.Token));
            string sub = Guid.NewGuid().ToString("N");
            var first = s.SignInExternal("google.com", sub, "A@B.com");
            Assert.Equal(first.AccountId, s.SignInExternal("google.com", sub).AccountId);
            Assert.Equal("a@b.com", s.EmailOf(first.AccountId));
            Assert.True(s.IsLinked(first.AccountId));
            Assert.Equal(LinkReasons.Taken, s.LinkExternal(guest.AccountId, "google.com", sub));
            Assert.Equal(LinkReasons.Ok, s.LinkExternal(guest.AccountId, "google.com", sub + "x"));
            Assert.Equal(LinkReasons.AlreadyLinked, s.LinkExternal(guest.AccountId, "google.com", sub + "y"));
            string nick = "n" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Assert.True(s.SetNickname(guest.AccountId, nick).Ok);
            Assert.Equal(NicknameRules.Taken, s.SetNickname(first.AccountId, nick.ToUpperInvariant()).Reason);
            s.Touch(guest.AccountId, 1000); // 마지막 접속: 10분 안의 다시 부름은 쓰지 않음
            s.Touch(guest.AccountId, 1000 + AccountActivity.TouchEverySeconds - 1);
            Assert.Equal(1000, s.AdminAccounts().Single(a => a.AccountId == guest.AccountId).LastSeenAtUnixSeconds);
            s.Touch(guest.AccountId, 1000 + AccountActivity.TouchEverySeconds);
            Assert.Equal(1000 + AccountActivity.TouchEverySeconds, s.AdminAccounts().Single(a => a.AccountId == guest.AccountId).LastSeenAtUnixSeconds);
            Assert.Null(s.RoleOf(guest.AccountId)); // H8 등급
            Assert.True(s.SetRole(guest.AccountId, AccountRoles.SubAdmin));
            Assert.Equal(AccountRoles.SubAdmin, s.RoleOf(guest.AccountId));
            Assert.Contains(s.AdminAccounts(), a => a.AccountId == guest.AccountId && a.Role == AccountRoles.SubAdmin);
            Assert.False(s.SetRole(guest.AccountId, "king"));
            Assert.True(s.SetRole(guest.AccountId, null));
            Assert.Null(s.RoleOf(guest.AccountId));
            Assert.Equal(NicknameRules.Taken, s.CheckNickname(first.AccountId, nick.ToUpperInvariant()).Reason); // H7 미리 확인 = 저장 때와 같은 검사
            Assert.True(s.CheckNickname(guest.AccountId, nick).Ok); // 내 이름
            Assert.True(s.CheckNickname(null, nick + "z").Ok);
            Assert.Equal(NicknameRules.BadName, s.CheckNickname(null, "!").Reason);
            Assert.Equal(nick, s.NicknamesOf(new[] { guest.AccountId, first.AccountId })[guest.AccountId]);
            string g = G();
            Assert.True(s.Join(guest.AccountId, g, 100));
            Assert.False(s.Join(guest.AccountId, g, 200));
            Assert.Contains(g, s.GamesOf(guest.AccountId));
            long until = s.RequestDeletion(guest.AccountId, 1000);
            Assert.Equal(until, s.RequestDeletion(guest.AccountId, 5000));
            Assert.True(s.CancelDeletion(guest.AccountId));
            Assert.False(s.CancelDeletion(guest.AccountId));
            Assert.Contains(s.AdminAccounts(), a => a.AccountId == guest.AccountId && a.Nickname == nick);
        }

        [FirestoreFact(DisplayName = "Firestore 게임: 세이브 판 비교 · 등록 갱신(같은 대상 = 같은 id, 판 +1) · 우편 받기 · 결제 기록 · 출석 · 푸시")]
        public void GameData()
        {
            var s = New();
            string g = G(), a = "acct-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Assert.Null(s.GetSave(g, a));
            Assert.True(s.PutSave(g, a, 0, "{}", 10).ok);
            var (ok, cur) = s.PutSave(g, a, 0, "{\"x\":1}", 11);
            Assert.False(ok);
            Assert.Equal(1, cur.Revision);
            Assert.True(s.PutSave(g, a, 1, "{\"x\":2}", 12).ok);
            Assert.Equal(2, s.GetSave(g, a).Revision);

            var l1 = s.UpsertListing(g, new HireListing { OwnerAccountId = a, Kind = HireKinds.Unit, SourceId = "cavalry", Price = 2 });
            var l2 = s.UpsertListing(g, new HireListing { OwnerAccountId = a, Kind = HireKinds.Unit, SourceId = "cavalry", Price = 3 });
            Assert.Equal((l1.Id, 2L, 3L), (l2.Id, l2.Version, s.GetListing(g, l1.Id).Price));
            Assert.Single(s.Listings(g, HireKinds.Unit));
            Assert.False(s.RemoveListing(g, "other", l1.Id));
            Assert.True(s.RemoveListing(g, a, l1.Id));

            s.AddMail(g, a, new Mail { Kind = MailKinds.HireReward, Resource = "gold", Amount = 5 });
            var box = s.MailOf(g, a);
            var m = Assert.Single(box);
            Assert.True(s.TakeMail(g, a, m.Id));
            Assert.False(s.TakeMail(g, a, m.Id));

            Assert.Null(s.ReceiptOf(g, "tx1"));
            s.RecordReceipts(g, new[] { new Game.Flow.ReceiptRecord { TransactionId = "tx1", AccountId = a, ProductId = "p" } });
            Assert.Equal(a, s.ReceiptOf(g, "tx1").AccountId);
            Assert.Single(s.ReceiptsOf(g, a));

            Assert.Empty(s.ContentOf(g).Items);
            s.SetContent(g, new ContentToggles { Items = { new ContentToggle { Kind = ContentKinds.Banner, Id = "standard", Off = true } } });
            Assert.True(s.ContentOf(g).IsOff(ContentKinds.Banner, "standard"));
            s.SetContent(g, new ContentToggles());
            Assert.Empty(s.ContentOf(g).Items);

            s.WithAttendance(g, a, r => { r.LastDay = 7; return 0; });
            s.WithPush(g, a, p => { p.Settings.AllyHelp = false; return 0; });
            Assert.Equal(7, s.WithAttendance(g, a, r => r.LastDay)); // 출석 · 푸시가 같은 문서여도 서로 지우지 않음
            Assert.False(s.WithPush(g, a, p => p.Settings.AllyHelp));
        }

        /// <summary>같은 영수증을 두 계정이 동시에: 하나만 저장 · 기록, 다른 하나는 receiptTaken (세이브도 그대로).</summary>
        internal static void SameReceiptRace(IStore s, string g)
        {
            string tx = "tx-" + Guid.NewGuid().ToString("N");
            var a = s.CreateGuest().AccountId;
            var b = s.CreateGuest().AccountId;
            Game.Flow.ReceiptRecord Rec(string who) => new Game.Flow.ReceiptRecord { TransactionId = tx, ProductId = "gem_pack_small", AccountId = who, Receipt = "fake:" + tx, AtUnixSeconds = 100 };
            var gate = new System.Threading.ManualResetEventSlim(false);
            var ta = System.Threading.Tasks.Task.Run(() => { gate.Wait(); return s.PutSaveWithReceipts(g, a, 0, "{\"a\":1}", 100, new[] { Rec(a) }); });
            var tb = System.Threading.Tasks.Task.Run(() => { gate.Wait(); return s.PutSaveWithReceipts(g, b, 0, "{\"b\":1}", 100, new[] { Rec(b) }); });
            gate.Set();
            var ra = ta.GetAwaiter().GetResult();
            var rb = tb.GetAwaiter().GetResult();
            Assert.True(ra.ok ^ rb.ok, $"하나만 통과해야 함: a={ra.ok} b={rb.ok}");
            var (winner, loser, lost) = ra.ok ? (a, b, rb) : (b, a, ra);
            Assert.True(lost.receiptTaken);
            Assert.Null(s.GetSave(g, loser)); // 진 쪽은 세이브도 쓰지 않음
            Assert.NotNull(s.GetSave(g, winner));
            Assert.Equal(winner, s.ReceiptOf(g, tx).AccountId);
            // 같은 계정이 다시 내도 이미 쓴 거래
            var again = s.PutSaveWithReceipts(g, winner, 1, "{}", 200, new[] { Rec(winner) });
            Assert.True(again.receiptTaken);
            Assert.Equal(1, s.GetSave(g, winner).Revision);
            // 판 번호가 다르면 stale (영수증은 기록하지 않음)
            string tx2 = tx + "b";
            var stale = s.PutSaveWithReceipts(g, winner, 0, "{}", 200, new[] { new Game.Flow.ReceiptRecord { TransactionId = tx2, AccountId = winner } });
            Assert.False(stale.ok);
            Assert.False(stale.receiptTaken);
            Assert.Null(s.ReceiptOf(g, tx2));
        }

        [FirestoreFact(DisplayName = "결제 한 트랜잭션 (backlog F): 같은 영수증을 두 계정이 동시에 내면 하나만 저장 · 기록, 판 번호가 다르면 기록 안 함 — Firestore")]
        public void SameReceiptRaceFirestore() => SameReceiptRace(New(), G());

        [Fact(DisplayName = "결제 한 트랜잭션 (backlog F): 같은 영수증을 두 계정이 동시에 내면 하나만 저장 · 기록, 판 번호가 다르면 기록 안 함 — 메모리 · 폴더 저장소")]
        public void SameReceiptRaceMemory()
        {
            SameReceiptRace(new InMemoryStore(), G());
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "store-" + Guid.NewGuid().ToString("N"));
            try { SameReceiptRace(new FolderStore(dir), G()); }
            finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [FirestoreFact(DisplayName = "Firestore 영토: 범위만 읽고 되쓰기, 트랜잭션 안에서 보낸 우편은 같이 반영, 연합 통째")]
        public void TerritoryAndAlliances()
        {
            var s = New();
            string g = G();
            s.WithTerritory(g, TerritoryScope.All(), o =>
            {
                o["flag:37566000,126978000"] = new TerritoryOwner { RegionId = "flag:37566000,126978000", OwnerAccountId = "a", CollectedUnixSeconds = 1 };
                o["flag:37900000,126978000"] = new TerritoryOwner { RegionId = "flag:37900000,126978000", OwnerAccountId = "b", CollectedUnixSeconds = 1 };
                o["dev_zone_a1"] = new TerritoryOwner { RegionId = "dev_zone_a1", OwnerAccountId = "a" };
                return 0;
            });
            int seen = s.WithTerritory(g, TerritoryScope.Owner("a"), o =>
            {
                Assert.DoesNotContain("flag:37900000,126978000", o.Keys);
                int loaded = o.Count; // a의 깃발 + 옛 지역 (고치기 전에 센다)
                o.Remove("dev_zone_a1");
                o["flag:37566000,126978000"].CollectedUnixSeconds = 99;
                s.AddMail(g, "a", new Mail { Kind = MailKinds.TerritoryIncome, Resource = "supply", Amount = 10 }); // 같은 트랜잭션
                return loaded;
            });
            Assert.Equal(2, seen);
            var all = s.WithTerritory(g, TerritoryScope.All(), o => new Dictionary<string, TerritoryOwner>(o));
            Assert.Equal(99, all["flag:37566000,126978000"].CollectedUnixSeconds);
            Assert.False(all.ContainsKey("dev_zone_a1"));
            Assert.True(all.ContainsKey("flag:37900000,126978000"));
            Assert.Single(s.MailOf(g, "a"));
            Assert.Single(s.WithTerritory(g, TerritoryScope.IncomeDue(10), o => o.Keys.ToList())); // b만 (a는 99)

            AllianceRanges(s, g);
        }

        /// <summary>연합 범위 (db.md 8.1 약한 곳 1): 범위에 드는 연합만 읽고 · 고친 것만 쓰고 · 지운 것은 지운다. 범위 밖은 보이지 않는다.</summary>
        internal static void AllianceRanges(IStore s, string g)
        {
            s.WithAlliances(g, AllianceScope.Account("a"), al => { al["x"] = new Alliance { Id = "x", Name = "연합", LeaderAccountId = "a", Members = { "a" } }; return 0; });
            s.WithAlliances(g, AllianceScope.Account("b"), al => { al["y"] = new Alliance { Id = "y", Name = "다른", LeaderAccountId = "b", Members = { "b" }, Requests = { "c" } }; return 0; });
            Assert.Equal(new[] { "x" }, s.WithAlliances(g, AllianceScope.Account("a"), al => al.Keys.ToList())); // 내 연합만
            Assert.Equal(new[] { "y" }, s.WithAlliances(g, AllianceScope.Account("c"), al => al.Keys.ToList())); // 신청해 둔 연합
            Assert.Equal(new[] { "x" }, s.WithAlliances(g, AllianceScope.Account("z").Name(" 연합 "), al => al.Keys.ToList())); // 같은 이름 (공백 · 대소문자 무시)
            Assert.Equal(new[] { "y" }, s.WithAlliances(g, AllianceScope.Account("z").Id("y"), al => al.Keys.ToList()));
            Assert.Empty(s.WithAlliances(g, AllianceScope.Account("z"), al => al.Keys.ToList()));
            s.WithAlliances(g, AllianceScope.Account("c"), al => { al["y"].Requests.Remove("c"); al["y"].Members.Add("c"); return 0; });
            Assert.Equal(new[] { "y" }, s.WithAlliances(g, AllianceScope.Account("c"), al => al.Keys.ToList())); // 이제 연합원으로 찾힘
            Assert.Equal(2, s.ReadAlliances(g).Count);
            s.WithAlliances(g, AllianceScope.Account("a"), al => { al.Remove("x"); return 0; });
            Assert.Equal(new[] { "y" }, s.ReadAlliances(g).Keys.ToList());
        }

        [Fact(DisplayName = "연합 범위 (db.md 8.1): 내 연합 · 신청한 연합 · 같은 이름 · id만 읽고 고친 것만 씀 — 메모리 저장소")]
        public void AllianceRangesMemory() => AllianceRanges(new InMemoryStore(), G());

        [FirestoreFact(DisplayName = "옮기기: store.json(폴더 저장소) 내용을 Firestore로 — 계정 · 토큰 · 세이브 · 우편 · 영토 · 연합 · 등록 · 결제 기록이 같은 값으로 읽힌다, 두 번 해도 같음")]
        public void Import()
        {
            var mem = new InMemoryStore();
            string g = G();
            var who = mem.CreateGuest();
            mem.SetNickname(who.AccountId, "m" + Guid.NewGuid().ToString("N").Substring(0, 6));
            mem.Join(who.AccountId, g, 5);
            mem.PutSave(g, who.AccountId, 0, "{\"a\":1}", 6);
            mem.AddMail(g, who.AccountId, new Mail { Kind = MailKinds.AdminGift, Resource = "gem", Amount = 3 });
            mem.WithTerritory(g, TerritoryScope.All(), o => { o["flag:37500000,127000000"] = new TerritoryOwner { RegionId = "flag:37500000,127000000", OwnerAccountId = who.AccountId }; return 0; });
            mem.WithAlliances(g, AllianceScope.Account(who.AccountId), al => { al["al1"] = new Alliance { Id = "al1", Name = "옮김", Members = { who.AccountId } }; return 0; });
            var l = mem.UpsertListing(g, new HireListing { OwnerAccountId = who.AccountId, Kind = HireKinds.General, SourceId = "avatar" });
            mem.RecordReceipts(g, new[] { new Game.Flow.ReceiptRecord { TransactionId = "t-" + g, AccountId = who.AccountId } });

            var fs = New();
            var counts = fs.Import(mem.Export());
            fs.Import(mem.Export());
            Assert.True(counts["accounts"] >= 1 && counts["saves"] >= 1);
            Assert.Equal(who.AccountId, fs.AccountOf(who.Token));
            Assert.Equal(mem.NicknameOf(who.AccountId), fs.NicknameOf(who.AccountId));
            Assert.Contains(g, fs.GamesOf(who.AccountId));
            Assert.Equal("{\"a\":1}", fs.GetSave(g, who.AccountId).Json);
            Assert.Single(fs.MailOf(g, who.AccountId));
            Assert.Equal(who.AccountId, fs.WithTerritory(g, TerritoryScope.Owner(who.AccountId), o => o.Values.Single().OwnerAccountId));
            Assert.Equal("옮김", fs.WithAlliances(g, AllianceScope.Account(who.AccountId), al => al["al1"].Name)); // 옮긴 문서도 찾기 칸(members)으로 찾힌다
            Assert.Equal(l.Id, fs.GetListing(g, l.Id).Id);
            Assert.NotNull(fs.ReceiptOf(g, "t-" + g));
        }

        [Fact(DisplayName = "옮기기 도구: store.json이 없으면 2, --dry면 수만 세고 0 (Firestore 없이)")]
        public void MigrateDry()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mig-" + Guid.NewGuid().ToString("N"));
            Assert.Equal(2, Migrate.Run(new[] { "migrate", "--from", dir, "--dry" }));
            var folder = new FolderStore(dir);
            var who = folder.CreateGuest();
            folder.PutSave("ancientwar", who.AccountId, 0, "{}", 1);
            Assert.Equal(0, Migrate.Run(new[] { "migrate", "--from", dir, "--dry" }));
            Assert.Equal(2, Migrate.Run(new[] { "migrate", "--from", dir })); // 쓰려면 --project 필요
        }
    }
}
