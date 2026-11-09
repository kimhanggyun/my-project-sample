using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Save;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>전투 이어하기 (R-55): 전투 중 앱이 꺼져도 같은 틱부터 이어서, 끝까지 하면 꺼지지 않은 전투와 같다.</summary>
    public class BattleResumeTests
    {
        private static (StoryFlow flow, CampaignState state, SortiePlan plan) Start(string stepId, ulong seed, bool delegate_)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            foreach (var id in Kit.D.StepIndex.Order) { if (id == stepId) break; state.ClearedSteps.Add(id); }
            var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = seed, Delegate = delegate_ });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.True(flow.Confirm(state, plan));
            return (flow, state, plan);
        }

        /// <summary>몇 틱마다 명령을 넣으며 진행 (직접 지휘 흉내).</summary>
        private static void Drive(BattleSimulation sim, SortiePlan plan, long untilTick)
        {
            var mine = plan.Setup.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).Select(c => c.Id).ToList();
            while (!sim.IsOver && sim.Tick < untilTick)
            {
                if (sim.Tick % 25 == 3)
                    sim.Issue(new BattleCommand { CorpsId = mine[(int)(sim.Tick / 25) % mine.Count], Kind = sim.Tick % 50 == 3 ? CommandKinds.Hold : CommandKinds.Advance });
                sim.Step();
            }
        }

        [Theory(DisplayName = "R-55 전투 중에 남긴 기록으로 이어 하면, 끝까지 했을 때 결과 지문·명령 기록·결과 반영이 꺼지지 않은 전투와 같다")]
        [InlineData("step01", 3UL, false)]
        [InlineData("step02", 3UL, true)]
        [InlineData("step04", 6UL, false)]
        public void SameAsUninterrupted(string stepId, ulong seed, bool delegate_)
        {
            // 꺼지지 않은 전투
            var (flowA, stateA, planA) = Start(stepId, seed, delegate_);
            var simA = new BattleSimulation(planA.Setup);
            Drive(simA, planA, long.MaxValue);
            var reportA = flowA.ApplyResult(stateA, planA, simA.Result);
            long stopAt = simA.Result.Ticks / 2; // 전투 한가운데에서 꺼진다
            Assert.True(stopAt > 20, $"{stepId}/{seed}: 전투가 너무 짧습니다 ({simA.Result.Ticks}틱).");

            // 같은 전투를 stopAt에서 끊고 (기기 저장소에 남김) 이어서
            var (flowB, stateB, planB) = Start(stepId, seed, delegate_);
            var simB = new BattleSimulation(planB.Setup);
            Drive(simB, planB, stopAt);
            Assert.False(simB.IsOver, "끊을 틱 전에 전투가 끝났습니다.");
            var store = new InMemorySaveStore();
            BattleResume.Write(store, BattleResume.Capture(planB, simB, 100));

            var resumed = BattleResume.Restore(new StoryFlow(Kit.D, Kit.Source, Kit.Clock()), BattleResume.Read(store));
            Assert.NotNull(resumed);
            Assert.Equal(stopAt, resumed.Simulation.Tick);
            Drive(resumed.Simulation, resumed.Plan, long.MaxValue);
            var simC = resumed.Simulation;
            Assert.Equal(simA.Result.StateHash, simC.Result.StateHash);
            Assert.Equal(simA.CommandLog.Count, simC.CommandLog.Count);
            Assert.Equal(simA.CommandLog.Select(c => (c.Tick, c.CorpsId, c.Kind)), simC.CommandLog.Select(c => (c.Tick, c.CorpsId, c.Kind)));
            var reportC = flowB.ApplyResult(stateB, resumed.Plan, simC.Result);
            Assert.Equal(reportA.Won, reportC.Won);
            Assert.Equal(reportA.SupplyReturned, reportC.SupplyReturned);
            Assert.Equal(Game.Flow.SaveJson.Write(stateA), Game.Flow.SaveJson.Write(stateB));

            // 서버에 올리는 제출도 같다 (R-40)
            var subA = CloudSync.SubmissionOf(planA, simA.CommandLog, simA.Result, 7);
            var subC = CloudSync.SubmissionOf(resumed.Plan, simC.CommandLog, simC.Result, 7);
            Assert.Equal(Game.Online.OnlineJson.Write(subA), Game.Online.OnlineJson.Write(subC));
        }

        [Fact(DisplayName = "R-55 끝난 전투는 남기지 않고, 깨진 기록·없는 스텝·기록과 다르게 흐르면 이어 하지 않는다")]
        public void NotResumable()
        {
            var (flow, _, plan) = Start("step01", 3, false);
            var sim = new BattleSimulation(plan.Setup);
            Drive(sim, plan, 80);
            var saved = BattleResume.Capture(plan, sim, 1);
            Assert.NotNull(saved);

            var noStep = BattleResume.Copy(saved); noStep.StepId = "nope";
            Assert.Null(BattleResume.Restore(flow, noStep));
            var future = BattleResume.Copy(saved); future.Format++;
            Assert.Null(BattleResume.Restore(flow, future));
            var bad = BattleResume.Copy(saved); bad.Commands.Add(new BattleCommand { Tick = 10, CorpsId = "enemy_corps_01", Kind = CommandKinds.Hold }); // 적 군단 명령은 받지 않는다
            bad.Commands = bad.Commands.OrderBy(c => c.Tick).ToList();
            Assert.Null(BattleResume.Restore(flow, bad));

            var store = new InMemorySaveStore();
            store.Write(SuspendedBattle.SaveKey, "{망가짐");
            Assert.Null(BattleResume.Read(store));
            BattleResume.Clear(store);
            Assert.False(store.Exists(SuspendedBattle.SaveKey));

            sim.RunToEnd();
            Assert.Null(BattleResume.Capture(plan, sim, 1));
        }
    
        [Fact(DisplayName = "R-55 고용 출진도 이어 할 수 있다: 이어서 끝낸 전투를 가짜 서버에 올리면 같은 시장으로 재현해 받아들인다")]
        public void HiredSortieResumes()
        {
            var clock = new ServerClock(Kit.Clock());
            var fake = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var sync = new CloudSync(fake, clock);
            SyncResult start = null;
            sync.Start(null, 0, r => start = r);
            var state = start.State;
            HireListResponse market = null;
            fake.GetListings(null, x => market = x);
            var unit = market.Listings.First(l => l.Kind == HireKinds.Unit && l.TroopType == "cavalry");
            var draft = new HireDraft(Kit.D);
            draft.Picks.Add(new HirePick { Listing = unit, CorpsIndex = 0, Count = 300 });
            var (comps, hired) = draft.Build(state, "field", fake.AccountId, out var error);
            Assert.Null(error);

            var flow = new StoryFlow(Kit.D, Kit.Source, clock);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 4, Delegate = true, Compositions = comps, Hired = hired });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.True(flow.Confirm(state, plan));
            var sim = new BattleSimulation(plan.Setup);
            for (int i = 0; i < 150 && !sim.IsOver; i++) sim.Step();
            var saved = BattleResume.Capture(plan, sim, 1);
            Assert.NotNull(saved);
            Assert.NotNull(saved.Hire);

            var resumed = BattleResume.Restore(new StoryFlow(Kit.D, Kit.Source, clock), BattleResume.Copy(saved));
            Assert.NotNull(resumed);
            Assert.Equal(plan.Hired.Uses.Count, resumed.Plan.Hired.Uses.Count);
            var rs = resumed.Simulation;
            while (!rs.IsOver) rs.Step();
            flow.ApplyResult(state, resumed.Plan, rs.Result);
            SyncResult r = null;
            sync.SubmitBattle(CloudSync.SubmissionOf(resumed.Plan, rs.CommandLog, rs.Result, start.Revision), state, x => r = x);
            Assert.Equal(SyncOutcomes.Accepted, r.Outcome);
            Assert.Equal(state.Wallet["gold"], r.State.Wallet["gold"]);
        }
}
}
