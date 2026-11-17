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

            s.WithAttendance(g, a, r => { r.LastDay = 7; return 0; });
            s.WithPush(g, a, p => { p.Settings.AllyHelp = false; return 0; });
            Assert.Equal(7, s.WithAttendance(g, a, r => r.LastDay)); // 출석 · 푸시가 같은 문서여도 서로 지우지 않음
            Assert.False(s.WithPush(g, a, p => p.Settings.AllyHelp));
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

            s.WithAlliances(g, al => { al["x"] = new Alliance { Id = "x", Name = "연합", LeaderAccountId = "a", Members = { "a" } }; return 0; });
            Assert.Equal("연합", s.WithAlliances(g, al => al["x"].Name));
            s.WithAlliances(g, al => { al.Remove("x"); return 0; });
            Assert.Empty(s.WithAlliances(g, al => al.Keys.ToList()));
        }

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
            mem.WithAlliances(g, al => { al["al1"] = new Alliance { Id = "al1", Name = "옮김", Members = { who.AccountId } }; return 0; });
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
            Assert.Equal("옮김", fs.WithAlliances(g, al => al["al1"].Name));
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
