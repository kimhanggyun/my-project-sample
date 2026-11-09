using System;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>오프라인 기록 나중에 올리기 (verification R-42, docs/impl/server.md 5장) — 가짜 서버 + 끊었다 이었다 하는 연결.</summary>
    public class JournalTests
    {
        /// <summary>서버 연결을 끊었다 이었다 한다 (끊기면 모든 요청이 null = 닿지 않음).</summary>
        private sealed class Flaky : IOnlineService
        {
            private readonly IOnlineService _inner;
            public bool Down;
            public Flaky(IOnlineService inner) { _inner = inner; }
            public bool IsAvailable => !Down;
            public string AccountId => _inner.AccountId;
            public void SignIn(Action<bool> done) { if (Down) done(false); else _inner.SignIn(done); }
            public void GetTime(Action<ServerTimeResponse> done) { if (Down) done(null); else _inner.GetTime(done); }
            public void LoadSave(Action<CloudSave> done) { if (Down) done(null); else _inner.LoadSave(done); }
            public void PutSave(SavePutRequest r, Action<SavePutResponse> done) { if (Down) done(null); else _inner.PutSave(r, done); }
            public void SubmitBattle(BattleSubmission s, Action<BattleVerdict> done) { if (Down) done(null); else _inner.SubmitBattle(s, done); }
            public void SubmitActions(ActionSubmission s, Action<ActionVerdict> done) { if (Down) done(null); else _inner.SubmitActions(s, done); }
            public void GetListings(string kind, Action<HireListResponse> done) { if (Down) done(null); else _inner.GetListings(kind, done); }
            public void RegisterHire(HireListingRequest r, Action<HireListingResponse> done) { if (Down) done(null); else _inner.RegisterHire(r, done); }
            public void Unregister(string id, Action<bool?> done) { if (Down) done(null); else _inner.Unregister(id, done); }
            public void GetMail(Action<MailListResponse> done) { if (Down) done(null); else _inner.GetMail(done); }
            public void GetTerritory(Action<TerritoryResponse> done) { if (Down) done(null); else _inner.GetTerritory(done); }
            public void CollectTerritory(Action<MailListResponse> done) { if (Down) done(null); else _inner.CollectTerritory(done); }
            public void RegisterDefense(DefenseRequest r, Action<DefenseResponse> done) { if (Down) done(null); else _inner.RegisterDefense(r, done); }
            public void GetAlliances(Action<AllianceListResponse> done) { if (Down) done(null); else _inner.GetAlliances(done); }
            public void CreateAlliance(string n, Action<AllianceResponse> done) { if (Down) done(null); else _inner.CreateAlliance(n, done); }
            public void JoinAlliance(string id, Action<AllianceResponse> done) { if (Down) done(null); else _inner.JoinAlliance(id, done); }
            public void LeaveAlliance(Action<AllianceResponse> done) { if (Down) done(null); else _inner.LeaveAlliance(done); }
        }

        private sealed class World
        {
            public ManualTimeProvider ServerTime = Kit.Clock();
            public LocalOnlineService Server;
            public Flaky Net;
            public CloudSync Sync;
            public ServerClock Clock;
            public OnlineLink Link = new OnlineLink();
            public CampaignState State;

            public World()
            {
                Server = new LocalOnlineService(Kit.D, Kit.Source, ServerTime);
                Net = new Flaky(Server);
                Clock = new ServerClock(ServerTime); // 기기 시계 = 서버 시계 (차이 0)
                Sync = new CloudSync(Net, Clock);
                SyncResult start = null;
                Sync.Start(null, 0, r => start = r);
                State = start.State;
                Link.Revision = start.Revision;
            }

            public void Do(PlayerAction a)
            {
                Assert.True(new PlayerActionRunner(Kit.D).Apply(State, a));
                a.DoneAtUnixSeconds = Clock.UtcNow.ToUnixTimeSeconds();
                Link.Journal.Add(new JournalEntry { Action = a });
            }

            /// <summary>기기에서 위임 전투 한 판 (결과 반영까지) → 기록에 더한다.</summary>
            public BattleSubmission Battle(ulong seed)
            {
                var flow = new StoryFlow(Kit.D, Kit.Source, Clock);
                var plan = flow.Prepare(State, new SortieRequest { StepId = "step01", Seed = seed, Delegate = true });
                Assert.True(plan.Ok);
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                flow.Confirm(State, plan);
                flow.ApplyResult(State, plan, sim.Result);
                var sub = CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, Link.Revision);
                Link.Journal.Add(new JournalEntry { Battle = sub });
                return sub;
            }

            public SyncResult Flush()
            {
                SyncResult r = null;
                Sync.FlushJournal(Link, State, x => r = x);
                Assert.NotNull(r);
                return r;
            }

            public CloudSave ServerSave()
            {
                CloudSave s = null;
                Server.LoadSave(x => s = x);
                return s;
            }
        }

        private static PlayerAction ChangeFormation(CampaignState state)
        {
            var draft = new Game.Campaign.Roster.FormationEditor(Kit.D, state).Draft;
            draft[0].Formation = draft[0].Formation == "craneWing" ? "column" : "craneWing";
            return PlayerActionRunner.SaveFormation(null, draft);
        }

        [Fact(DisplayName = "R-42 오프라인에서 한 행동·전투는 기기에 남았다가, 연결되면 차례로 올라가 서버 세이브가 기기와 같아진다")]
        public void OfflineThenUpload()
        {
            var w = new World();
            long rev0 = w.Link.Revision;
            w.Net.Down = true;
            w.Do(ChangeFormation(w.State));
            w.Battle(7);
            w.Do(PlayerActionRunner.CreateUnit("cavalry"));

            var queued = w.Flush();
            Assert.Equal(SyncOutcomes.Queued, queued.Outcome);
            Assert.Equal(3, w.Link.Journal.Count);
            Assert.Same(w.State, queued.State);

            w.ServerTime.Advance(TimeSpan.FromHours(2)); // 두 시간 뒤에 연결됨: 전투는 한 시각으로 다시 돌린다
            w.Net.Down = false;
            var sent = w.Flush();
            Assert.Equal(SyncOutcomes.Accepted, sent.Outcome);
            Assert.Empty(w.Link.Journal);
            Assert.Equal(rev0 + 3, w.Link.Revision); // 행동 / 전투 / 행동
            Assert.Contains("step01", sent.State.ClearedSteps);
            Assert.Equal(w.State.Units.Count, sent.State.Units.Count);
            Assert.Equal(w.State.Compositions[0].Formation, sent.State.Compositions[0].Formation);
            Assert.True(sent.Verdict.Valid);
            // 서버 세이브 시각 = 마지막 기록 시각 (연결된 시각이 아님)
            Assert.Equal(Kit.Clock().UtcNow.ToUnixTimeSeconds(), w.ServerSave().SavedAtUnixSeconds);
        }

        [Fact(DisplayName = "R-42 중간에 끊기면 올린 만큼만 지우고 나머지는 남겨 둔다")]
        public void PartialUpload()
        {
            var w = new World();
            w.Do(PlayerActionRunner.CreateUnit("cavalry"));
            var battle = w.Battle(3);
            // 행동 묶음만 받고 전투 전에 끊긴다
            ActionVerdict first = null;
            w.Net.SubmitActions(new ActionSubmission { BaseRevision = w.Link.Revision, Actions = { w.Link.Journal[0].Action } }, v => first = v);
            Assert.True(first.Valid);
            w.Link.Journal.RemoveAt(0);
            w.Link.Revision = first.Save.Revision;
            w.Net.Down = true;
            Assert.Equal(SyncOutcomes.Queued, w.Flush().Outcome);
            Assert.Single(w.Link.Journal);
            w.Net.Down = false;
            var r = w.Flush();
            Assert.Equal(SyncOutcomes.Accepted, r.Outcome);
            Assert.Equal(first.Save.Revision + 1, r.Revision);
            Assert.Equal(first.Save.Revision, battle.BaseRevision); // 보낼 때 지금 Revision으로 고쳐 보냈다
        }

        [Fact(DisplayName = "R-42 기록 하나가 거절되면 남은 기록을 모두 버리고 서버 세이브로 되돌린다")]
        public void RejectDropsRest()
        {
            var w = new World();
            w.Battle(5);
            w.Link.Journal[0].Battle.ClaimedHash = "deadbeefdeadbeef"; // 조작
            w.Do(PlayerActionRunner.CreateUnit("cavalry"));
            var r = w.Flush();
            Assert.Equal(SyncOutcomes.Rejected, r.Outcome);
            Assert.Empty(w.Link.Journal);
            Assert.DoesNotContain("step01", r.State.ClearedSteps);
            Assert.Equal(VerdictReasons.Mismatch, r.Verdict.Reason);
        }

        [Fact(DisplayName = "R-42 기록 시각: 서버 세이브 시각보다 이르거나, 미래이거나, 72시간보다 오래되면 거절")]
        public void TimeWindow()
        {
            long now = 1_000_000;
            Assert.True(RecordTime.Allowed(now - 10, now - 5, now));
            Assert.True(RecordTime.Allowed(now, now - RecordTime.SkewSeconds, now)); // 시계 차이는 봐준다
            Assert.False(RecordTime.Allowed(now - 10, now - 10 - RecordTime.SkewSeconds - 1, now)); // 앞 기록보다 이름
            Assert.False(RecordTime.Allowed(0, now + RecordTime.SkewSeconds + 1, now)); // 미래
            Assert.False(RecordTime.Allowed(0, now - RecordTime.MaxOfflineSeconds - 1, now)); // 너무 오래됨
            Assert.Equal(now, RecordTime.Resolve(0, now));

            var w = new World();
            w.Battle(9);
            w.ServerTime.Advance(TimeSpan.FromHours(73));
            var r = w.Flush();
            Assert.Equal(SyncOutcomes.Rejected, r.Outcome);
            Assert.Equal(VerdictReasons.BadTime, r.Verdict.Reason);

            // 행동 시각이 거꾸로 가면 그 순번에서 거절
            var w2 = new World();
            ActionVerdict v = null;
            long t = w2.Clock.UtcNow.ToUnixTimeSeconds();
            var a1 = PlayerActionRunner.CreateUnit("cavalry"); a1.DoneAtUnixSeconds = t;
            var a2 = PlayerActionRunner.CreateUnit("archer"); a2.DoneAtUnixSeconds = t - 3600;
            w2.Server.SubmitActions(new ActionSubmission { BaseRevision = w2.Link.Revision, Actions = { a1, a2 } }, x => v = x);
            Assert.Equal((VerdictReasons.BadTime, 1), (v.Reason, v.FailedIndex));
        }

        [Fact(DisplayName = "예전 형식(행동만 남긴 기록)은 읽을 때 새 기록으로 옮긴다")]
        public void LinkUpgrade()
        {
            var json = "{\"Revision\":4,\"Pending\":[{\"Kind\":\"unit.create\",\"TroopType\":\"cavalry\"}]}";
            var link = OnlineJson.Read<OnlineLink>(json).Upgrade();
            Assert.Single(link.Journal);
            Assert.Equal(ActionKinds.UnitCreate, link.Journal[0].Action.Kind);
            Assert.Null(link.Pending);
        }
    }
}
