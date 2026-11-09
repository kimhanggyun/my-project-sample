using System.Collections.Generic;
using Game.Flow;
using Game.Online;
using Game.Territory;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>연합 (territory.md 6장, verification R-49): 만들기·가입·탈퇴, 인원 상한, 이름 규칙, 영토 주인의 연합 표시.</summary>
    public class AllianceTests
    {
        private static TerritoryDefs Defs() => TerritoryDefs.Load(Kit.Source);

        [Fact(DisplayName = "R-49 만들기: 이름 2~12자(앞뒤 공백 빼고)·겹치면 안 됨(대소문자 무시), 한 계정은 한 연합")]
        public void Create()
        {
            var d = Defs();
            var book = new Dictionary<string, Alliance>();
            Assert.Equal(AllianceReasons.BadName, AllianceBook.Create(d, book, "A", " 가 ", "x1", 0).Reason);
            Assert.Equal(AllianceReasons.BadName, AllianceBook.Create(d, book, "A", new string('가', 13), "x1", 0).Reason);
            var ok = AllianceBook.Create(d, book, "A", "  청룡단  ", "x1", 5);
            Assert.True(ok.Ok);
            Assert.Equal(("청룡단", "A", 5L), (ok.Alliance.Name, ok.Alliance.LeaderAccountId, ok.Alliance.CreatedAtUnixSeconds));
            Assert.Equal(AllianceReasons.AlreadyMember, AllianceBook.Create(d, book, "A", "백호단", "x2", 0).Reason);
            Assert.Equal(AllianceReasons.NameTaken, AllianceBook.Create(d, book, "B", "청룡단", "x2", 0).Reason);
            AllianceBook.Create(d, book, "C", "Dragons", "x3", 0);
            Assert.Equal(AllianceReasons.NameTaken, AllianceBook.Create(d, book, "D", "dragons", "x4", 0).Reason);
        }

        [Fact(DisplayName = "R-49 가입·탈퇴: 인원 상한까지, 연합장이 나가면 다음 사람이 연합장, 모두 나가면 연합이 없어진다")]
        public void JoinLeave()
        {
            var d = Defs();
            var book = new Dictionary<string, Alliance>();
            AllianceBook.Create(d, book, "A", "청룡단", "x1", 0);
            Assert.Equal(AllianceReasons.NotFound, AllianceBook.Join(d, book, "B", "nope").Reason);
            for (int i = 1; i < d.AllianceMemberMax; i++) Assert.True(AllianceBook.Join(d, book, "M" + i, "x1").Ok);
            Assert.Equal(AllianceReasons.Full, AllianceBook.Join(d, book, "Z", "x1").Reason);
            Assert.Equal(AllianceReasons.AlreadyMember, AllianceBook.Join(d, book, "M1", "x1").Reason);

            Assert.True(AllianceBook.Leave(book, "A").Ok);
            Assert.Equal("M1", book["x1"].LeaderAccountId);
            Assert.Equal(AllianceReasons.NotMember, AllianceBook.Leave(book, "A").Reason);
            for (int i = 1; i < d.AllianceMemberMax; i++) AllianceBook.Leave(book, "M" + i);
            Assert.Empty(book);

            var list = AllianceBook.List(d, book, "A");
            Assert.Null(list.MineId);
            Assert.Equal(30, list.MemberMax);
        }

        [Fact(DisplayName = "R-50 가입 방식: 닫으면 신청 → 연합장이 수락·거절, 연합장만 관리, 내보내기, 다른 곳에 들어가면 신청은 사라진다")]
        public void Manage()
        {
            var d = Defs();
            var book = new Dictionary<string, Alliance>();
            AllianceBook.Create(d, book, "A", "청룡단", "x1", 0);
            AllianceBook.Create(d, book, "C", "백호단", "x2", 0);
            AllianceManageRequest Do(string action, string who = null) => new AllianceManageRequest { Action = action, AccountId = who };
            Assert.True(AllianceBook.Manage(d, book, "A", Do(AllianceActions.Close)).Ok);
            var req = AllianceBook.Join(d, book, "B", "x1");
            Assert.Equal((true, AllianceReasons.Requested), (req.Ok, req.Reason));
            Assert.Null(AllianceBook.Of(book, "B"));
            AllianceBook.Join(d, book, "E", "x1");
            Assert.Equal(new[] { "B", "E" }, book["x1"].Requests);

            AllianceBook.Join(d, book, "M", "x2"); // 백호단 가입 (열림)
            Assert.Equal(AllianceReasons.NotLeader, AllianceBook.Manage(d, book, "M", Do(AllianceActions.Kick, "C")).Reason);
            Assert.Equal(AllianceReasons.NotMember, AllianceBook.Manage(d, book, "B", Do(AllianceActions.Open)).Reason);
            Assert.True(AllianceBook.Manage(d, book, "A", Do(AllianceActions.Accept, "B")).Ok);
            Assert.Equal("x1", AllianceBook.Of(book, "B").Id);
            Assert.Equal(new[] { "E" }, book["x1"].Requests);
            Assert.True(AllianceBook.Manage(d, book, "A", Do(AllianceActions.Reject, "E")).Ok);
            Assert.Empty(book["x1"].Requests);
            Assert.Equal(AllianceReasons.BadRequest, AllianceBook.Manage(d, book, "A", Do(AllianceActions.Accept, "nobody")).Reason);

            AllianceBook.Join(d, book, "F", "x1"); // 신청
            AllianceBook.Join(d, book, "F", "x2"); // 백호단에 바로 들어감 → 청룡단 신청은 사라짐
            Assert.Empty(book["x1"].Requests);

            Assert.Equal(AllianceReasons.BadRequest, AllianceBook.Manage(d, book, "A", Do(AllianceActions.Kick, "A")).Reason); // 자기 자신
            Assert.True(AllianceBook.Manage(d, book, "A", Do(AllianceActions.Kick, "B")).Ok);
            Assert.Null(AllianceBook.Of(book, "B"));
        }

        [Fact(DisplayName = "R-49 영토 목록에 주인의 연합 이름이 붙는다 (가짜 서버)")]
        public void TerritoryShowsAlliance()
        {
            var owners = new Dictionary<string, TerritoryOwner>();
            TerritoryLedger.Capture(null, owners, "dev_zone_a1", "A", 0, (_, __) => { });
            var book = new Dictionary<string, Alliance>();
            AllianceBook.Create(Defs(), book, "A", "청룡단", "x1", 0);
            var view = TerritoryLedger.View(Defs(), owners, "B", 0, a => AllianceBook.Of(book, a));
            Assert.Equal(("x1", "청룡단"), (view.Owners[0].OwnerAllianceId, view.Owners[0].OwnerAllianceName));

            var fake = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            AllianceResponse r = null;
            fake.CreateAlliance("시험연합", x => r = x);
            Assert.True(r.Ok);
            AllianceListResponse list = null;
            fake.GetAlliances(x => list = x);
            Assert.Equal(r.Alliance.Id, list.MineId);
            fake.LeaveAlliance(x => r = x);
            fake.GetAlliances(x => list = x);
            Assert.Empty(list.Alliances);
        }
    }
}
