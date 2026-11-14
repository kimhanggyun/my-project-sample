using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>기기 쪽 동기화 흐름 (docs/impl/server.md 5장) — 서버 대신 기기 안 가짜(LocalOnlineService)로.</summary>
    public class CloudSyncTests
    {
        private static (CloudSync sync, LocalOnlineService online, ServerClock clock) Make()
        {
            var clock = new ServerClock(Kit.Clock());
            var online = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            return (new CloudSync(online, clock), online, clock);
        }

        private static SyncResult Run(System.Action<System.Action<SyncResult>> call)
        {
            SyncResult r = null;
            call(x => r = x);
            Assert.NotNull(r); // 가짜는 바로 답한다
            return r;
        }

        [Fact(DisplayName = "시작: 서버에 연결 못 하면 기기 세이브 그대로, 연결되면 서버 세이브를 받아 쓴다")]
        public void StartPaths()
        {
            var local = Kit.Veteran();
            var off = Run(done => new CloudSync(new OfflineOnlineService(), null).Start(local, 0, done));
            Assert.Equal((SyncOutcomes.Offline, 0L), (off.Outcome, off.Revision));
            Assert.Same(local, off.State);

            var (sync, _, _) = Make();
            var on = Run(done => sync.Start(local, 0, done));
            Assert.Equal(SyncOutcomes.FromServer, on.Outcome);
            Assert.Equal(1, on.Revision);
            Assert.NotSame(local, on.State);
        }

        [Fact(DisplayName = "처음 연결 때 기기 진행이 더 많으면 기기 세이브를 올린다 (그 뒤로는 서버가 주인)")]
        public void FirstLinkUploadsProgress()
        {
            var (sync, online, _) = Make();
            var local = Kit.Veteran();
            local.ClearedSteps.Add("step01");
            var r = Run(done => sync.Start(local, 0, done));
            Assert.Equal(SyncOutcomes.Uploaded, r.Outcome);
            Assert.Equal(2, r.Revision);
            CloudSave server = null;
            online.LoadSave(s => server = s);
            Assert.Contains("step01", SaveJson.Read(server.Json).ClearedSteps);

            var again = Run(done => sync.Start(Kit.Veteran(), r.Revision, done)); // 이미 맞춘 기기
            Assert.Equal(SyncOutcomes.FromServer, again.Outcome);
            Assert.Contains("step01", again.State.ClearedSteps);
        }

        [Fact(DisplayName = "전투 뒤: 제출이 맞으면 서버가 반영한 세이브(새 Revision), 지문을 바꾸면 거절되고 서버 세이브로 되돌린다")]
        public void BattleSubmit()
        {
            var (sync, _, clock) = Make();
            var start = Run(done => sync.Start(null, 0, done));
            var flow = new StoryFlow(Kit.D, Kit.Source, clock);

            SyncResult Play(bool tamper)
            {
                var state = SaveJson.Read(SaveJson.Write(start.State));
                var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 11, Delegate = true });
                Assert.True(plan.Ok);
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                flow.Confirm(state, plan);
                flow.ApplyResult(state, plan, sim.Result);
                var sub = CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, start.Revision);
                if (tamper) sub.ClaimedHash = "deadbeefdeadbeef";
                return Run(done => sync.SubmitBattle(sub, state, done));
            }

            var bad = Play(true);
            Assert.Equal(SyncOutcomes.Rejected, bad.Outcome);
            Assert.Equal(start.Revision, bad.Revision);
            Assert.DoesNotContain("step01", bad.State.ClearedSteps);

            var good = Play(false);
            Assert.Equal(SyncOutcomes.Accepted, good.Outcome);
            Assert.Equal(start.Revision + 1, good.Revision);
            Assert.True(good.Verdict.Valid);
        }

        [Fact(DisplayName = "제출 메시지: 위임·대결 선택·군량·명령 기록이 출진 준비에서 그대로 옮겨진다")]
        public void SubmissionCarriesSortie()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 3, DelegateGenerals = { "general_b" } });
            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            var sub = CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, 5);
            Assert.False(sub.Delegate);
            Assert.Equal(new[] { "general_b" }, sub.DelegateGenerals);
            Assert.Equal(plan.SupplyTaken, sub.Supply);
            Assert.Equal(plan.Setup.Contests.Select(c => c.ContestId), sub.ContestChoices.Keys);
            // 같은 메시지로 출진 준비를 다시 하면 같은 전투
            var again = flow.Prepare(Kit.Veteran(), BattleVerifier.RequestOf(sub));
            Assert.Equal(sim.Result.StateHash, BattleSimulation.Run(again.Setup, sub.Commands).StateHash);
        }

        [Fact(DisplayName = "가짜 서버는 저장소를 주면 앱을 다시 켜도 세이브가 남는다")]
        public void FakePersists()
        {
            var store = new Game.Foundation.Save.InMemorySaveStore();
            var a = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock(), store);
            CloudSave first = null;
            a.LoadSave(x => first = x);
            a.PutSave(new SavePutRequest { BaseRevision = first.Revision, Json = first.Json }, _ => { });
            var b = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock(), store);
            CloudSave again = null;
            b.LoadSave(x => again = x);
            Assert.Equal(2, again.Revision);
        }
    }
}
