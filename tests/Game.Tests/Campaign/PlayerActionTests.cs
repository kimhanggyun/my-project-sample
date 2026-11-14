using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>기기 행동을 서버가 다시 적용 (verification R-41, docs/impl/server.md 5장) — 서버 대신 기기 안 가짜(LocalOnlineService)로.</summary>
    public class PlayerActionTests
    {
        private static PlayerActionRunner Runner() => new PlayerActionRunner(Kit.D);

        private static (CloudSync sync, LocalOnlineService online, ServerClock clock, SyncResult start) Online()
        {
            var clock = new ServerClock(Kit.Clock());
            var online = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var sync = new CloudSync(online, clock);
            SyncResult start = null;
            sync.Start(null, 0, r => start = r);
            return (sync, online, clock, start);
        }

        private static SyncResult Submit(CloudSync sync, long revision, CampaignState local, params PlayerAction[] actions)
        {
            SyncResult r = null;
            sync.SubmitActions(new ActionSubmission { BaseRevision = revision, Actions = new System.Collections.Generic.List<PlayerAction>(actions) }, local, x => r = x);
            Assert.NotNull(r);
            return r;
        }

        [Fact(DisplayName = "R-41 행동은 화면이 부르던 규칙과 같은 결과: 창설·징병·해산, 편성 저장, 스킬북 구매·습득")]
        public void ActionsMatchServices()
        {
            var runner = Runner();
            var state = Kit.Veteran();
            int units = state.Units.Count;
            Assert.True(runner.Apply(state, PlayerActionRunner.CreateUnit("catapult")));
            var made = state.Units[state.Units.Count - 1];
            Assert.Equal(units + 1, state.Units.Count);
            Assert.True(runner.Apply(state, PlayerActionRunner.Conscript(made.UnitId, 100)));
            Assert.Equal(100, made.Troops);
            Assert.True(runner.Apply(state, PlayerActionRunner.DisbandUnit(made.UnitId)));
            Assert.Equal(units, state.Units.Count);

            var draft = new Game.Campaign.Roster.FormationEditor(Kit.D, state).Draft;
            draft[0].Formation = draft[0].Formation == "column" ? "square" : "column";
            Assert.True(runner.Apply(state, PlayerActionRunner.SaveFormation(null, draft)));
            Assert.Equal(draft[0].Formation, state.Compositions[0].Formation);

            state.Wallet[ResourceIds.Gold] = 5000;
            Assert.True(runner.Apply(state, PlayerActionRunner.BuySkillBook("arrowRain")));
            Assert.Equal(1, new SkillBookService(Kit.D).Owned(state, "arrowRain"));

            // 규칙상 안 되는 행동·모르는 종류는 false (세이브 그대로)
            Assert.False(runner.Apply(state, PlayerActionRunner.CreateUnit("no_such_troop")));
            Assert.False(runner.Apply(state, PlayerActionRunner.DisbandUnit(FirstSlot(state))));
            Assert.False(runner.Apply(state, new PlayerAction { Kind = "unknown.kind" }));
            Assert.False(runner.Apply(state, PlayerActionRunner.Heal(made.UnitId, 1, "gem")));
        }

        private static string FirstSlot(CampaignState s)
        {
            foreach (var p in s.Compositions[0].Picks) return p.UnitId;
            return null;
        }

        [Fact(DisplayName = "R-41 서버가 같은 행동을 자기 세이브에 적용: 기기 결과와 같은 세이브, Revision +1")]
        public void ServerAppliesSameActions()
        {
            var (sync, _, _, start) = Online();
            var local = SaveJson.Read(SaveJson.Write(start.State));
            var runner = Runner();
            var create = PlayerActionRunner.CreateUnit("catapult");
            Assert.True(runner.Apply(local, create));
            var recruit = PlayerActionRunner.Recruit(local.Units[local.Units.Count - 1].UnitId, 50);
            Assert.True(runner.Apply(local, recruit));
            var actions = new[] { create, recruit };

            var r = Submit(sync, start.Revision, local, actions);
            Assert.Equal(SyncOutcomes.Accepted, r.Outcome);
            Assert.Equal(start.Revision + 1, r.Revision);
            Assert.Equal(SaveJson.Write(local), SaveJson.Write(r.State));
        }

        [Fact(DisplayName = "R-41 하나라도 안 되는 행동이 있거나 옛 세이브 위면 아무것도 반영하지 않고 서버 세이브로 되돌린다")]
        public void RejectsBadOrStale()
        {
            var (sync, online, _, start) = Online();
            var bad = Submit(sync, start.Revision, start.State, PlayerActionRunner.CreateUnit("catapult"), PlayerActionRunner.DisbandUnit("u99"));
            Assert.Equal(SyncOutcomes.Rejected, bad.Outcome);
            Assert.Equal(start.Revision, bad.Revision);
            Assert.Equal(start.State.Units.Count, bad.State.Units.Count);

            ActionVerdict v = null;
            online.SubmitActions(new ActionSubmission { BaseRevision = start.Revision, Actions = { PlayerActionRunner.CreateUnit("catapult"), PlayerActionRunner.DisbandUnit("u99") } }, x => v = x);
            Assert.Equal((VerdictReasons.BadAction, 1), (v.Reason, v.FailedIndex));

            var stale = Submit(sync, start.Revision + 5, start.State, PlayerActionRunner.CreateUnit("catapult"));
            Assert.Equal(SyncOutcomes.Rejected, stale.Outcome);
            var empty = Submit(sync, start.Revision, start.State);
            Assert.Equal(SyncOutcomes.Rejected, empty.Outcome);
        }

        [Fact(DisplayName = "R-41 세이브 통째 올리기는 처음 가져오기(서버가 새 게임일 때)만: 진행이 생긴 뒤에는 거절")]
        public void PutOnlyOnFreshServer()
        {
            var (sync, online, _, start) = Online();
            var after = Submit(sync, start.Revision, start.State, PlayerActionRunner.CreateUnit("catapult"));
            Assert.Equal(2, after.Revision);
            var cheat = Kit.Veteran();
            cheat.Wallet[ResourceIds.Gold] = 999999;
            SavePutResponse put = null;
            online.PutSave(new SavePutRequest { BaseRevision = after.Revision, Json = SaveJson.Write(cheat) }, x => put = x);
            Assert.False(put.Ok);
            Assert.Equal(2, put.Current.Revision);
        }

        [Fact(DisplayName = "R-41 편성을 바꾼 행동을 보내야 그 편성으로 한 전투가 서버에서 맞는다 (안 보내면 서버 편성과 달라 거절)")]
        public void FormationActionBeforeBattle()
        {
            SyncResult Play(bool sendAction)
            {
                var (sync, _, clock, start) = Online();
                var state = SaveJson.Read(SaveJson.Write(start.State));
                var draft = new Game.Campaign.Roster.FormationEditor(Kit.D, state).Draft;
                draft[0].Formation = draft[0].Formation == "craneWing" ? "column" : "craneWing";
                var action = PlayerActionRunner.SaveFormation(null, draft);
                Assert.True(Runner().Apply(state, action));
                long rev = start.Revision;
                if (sendAction) rev = Submit(sync, rev, state, action).Revision;

                var flow = new StoryFlow(Kit.D, Kit.Source, clock);
                var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 5, Delegate = true });
                Assert.True(plan.Ok);
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                flow.Confirm(state, plan);
                flow.ApplyResult(state, plan, sim.Result);
                SyncResult r = null;
                sync.SubmitBattle(CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, rev), state, x => r = x);
                return r;
            }

            Assert.Equal(SyncOutcomes.Accepted, Play(true).Outcome);
            Assert.Equal(SyncOutcomes.Rejected, Play(false).Outcome);
        }

        [Fact(DisplayName = "행동 메시지는 JSON으로 오가도 같다 (기기 보관·HTTP)")]
        public void ActionJsonRoundTrip()
        {
            var state = Kit.Veteran();
            var a = PlayerActionRunner.SaveFormation("siege", new Game.Campaign.Roster.FormationEditor(Kit.D, state).Draft);
            var link = new OnlineLink { Revision = 3, Journal = { new JournalEntry { Action = a }, new JournalEntry { Action = PlayerActionRunner.Heal("archer", 5, ResourceIds.Gold) } } };
            var back = OnlineJson.Read<OnlineLink>(OnlineJson.Write(link));
            Assert.Equal(2, back.Journal.Count);
            Assert.Equal(OnlineJson.Write(link), OnlineJson.Write(back));
        }
    }
}
