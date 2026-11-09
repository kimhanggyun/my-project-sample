using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Foundation.Save;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>전투 녹화 (replay.md, R-54): 설정 + 명령 기록만 저장해 같은 전투를 다시 본다.</summary>
    public class ReplayTests
    {
        /// <summary>step01 직접 지휘: 몇 틱 뒤 대기·진군 명령을 섞어 끝까지.</summary>
        private static (SortiePlan plan, BattleSimulation sim, StoryFlow flow) Play(ulong seed)
        {
            // 시작 전 대결로 바로 끝나는 시드는 건너뛴다 (명령을 넣어 볼 수 없다)
            for (ulong s = seed; ; s++)
            {
                var r = PlayOnce(s);
                if (r.sim.CommandLog.Count >= 2) return r;
                Assert.True(s < seed + 20, "명령을 넣을 수 있는 시드가 없습니다.");
            }
        }

        private static (SortiePlan plan, BattleSimulation sim, StoryFlow flow) PlayOnce(ulong seed)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed });
            Assert.True(plan.Ok);
            flow.Confirm(state, plan);
            var sim = new BattleSimulation(plan.Setup);
            string p1 = plan.Setup.Corps.First(c => c.Team == StoryFlow.PlayerTeam).Id;
            int issued = 0;
            while (!sim.IsOver)
            {
                if (issued == 0 && sim.Tick >= 5 && sim.Issue(new BattleCommand { CorpsId = p1, Kind = CommandKinds.Hold })) issued++;
                else if (issued == 1 && sim.Tick >= 40 && sim.Issue(new BattleCommand { CorpsId = p1, Kind = CommandKinds.Advance })) issued++;
                else if (issued == 2 && sim.Tick >= 60 && sim.Issue(new BattleCommand { CorpsId = p1, Kind = CommandKinds.Delegate, Mode = "on" })) issued++;
                sim.Step();
            }
            flow.ApplyResult(state, plan, sim.Result);
            return (plan, sim, flow);
        }

        [Fact(DisplayName = "R-54 녹화(설정 + 명령 기록)를 JSON으로 저장했다 다시 돌리면 같은 결과 지문")]
        public void SameResult()
        {
            var (plan, sim, flow) = Play(11);
            Assert.True(sim.CommandLog.Count >= 2);
            var rec = ReplayService.Capture(plan, sim.CommandLog, sim.Result, 1000);
            Assert.Null(rec.Setup.Defs);
            Assert.Null(rec.Setup.Map);
            var json = ReplayService.Write(rec);
            Assert.True(json.Length < 200_000, "녹화가 너무 큽니다: " + json.Length);

            var svc = new ReplayService(flow);
            var play = svc.Check(ReplayService.Read(json));
            Assert.Equal(ReplayStatus.Ok, play.Status);
            var again = BattleSimulation.Run(play.Setup, play.Commands);
            Assert.Equal(sim.Result.StateHash, again.StateHash);
            Assert.Equal(sim.Result.Ticks, again.Ticks);

            // 화면처럼 한 틱씩: IssueDue → Step
            var p = svc.Prepare(rec);
            var step = new BattleSimulation(p.Setup);
            int next = 0;
            while (!step.IsOver) { next = p.IssueDue(step, next); step.Step(); }
            Assert.Equal(sim.Result.StateHash, step.Result.StateHash);
            Assert.Equal(rec.Won, sim.Result.Winner == StoryFlow.PlayerTeam);
            Assert.Equal(sim.Result.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).Sum(c => c.Losses), rec.PlayerLosses);
        }

        [Theory(DisplayName = "R-54 대결로 시작하는 전투, 증원·연출이 있는 위임 전투도 녹화대로 다시 돌아간다")]
        [InlineData("step01", 11UL)]
        [InlineData("step02", 3UL)]
        [InlineData("step05", 7UL)]
        public void ContestsAndEvents(string stepId, ulong seed)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            foreach (var id in Kit.D.StepIndex.Order) { if (id == stepId) break; state.ClearedSteps.Add(id); }
            var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = seed, Delegate = stepId != "step01" });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            flow.Confirm(state, plan);
            var result = BattleSimulation.Run(plan.Setup);
            var rec = ReplayService.Capture(plan, result.Commands, result, 1);
            Assert.Equal(ReplayStatus.Ok, new ReplayService(flow).Check(ReplayService.Read(ReplayService.Write(rec))).Status);
        }

        [Fact(DisplayName = "R-54 결과가 녹화와 다르면 (규칙·데이터가 바뀜) 재생 불가, 스텝이 없으면 깨진 녹화")]
        public void VersionChangedOrBroken()
        {
            var (plan, sim, flow) = Play(12);
            var svc = new ReplayService(flow);
            var rec = ReplayService.Capture(plan, sim.CommandLog, sim.Result, 1000);
            var changed = ReplayService.Copy(rec);
            changed.Setup.Corps[0].Units[0].Troops -= 500; // 규칙이 바뀐 것과 같은 효과
            Assert.Equal(ReplayStatus.VersionChanged, svc.Check(changed).Status);
            var broken = ReplayService.Copy(rec);
            broken.StepId = "nope";
            Assert.Equal(ReplayStatus.Broken, svc.Check(broken).Status);
            var future = ReplayService.Copy(rec);
            future.Format = ReplayRecord.CurrentFormat + 1;
            Assert.Equal(ReplayStatus.Broken, svc.Prepare(future).Status);
        }

        [Fact(DisplayName = "R-54 내 녹화는 새것이 앞, 한도(maxSaved)를 넘으면 가장 오래된 것부터 지운다. 기기 저장소에 남는다")]
        public void Library()
        {
            var rules = ReplayRules.Load(Kit.Source);
            Assert.True(rules.MaxSaved >= 1);
            var (plan, sim, _) = Play(13);
            var lib = new ReplayLibrary();
            for (int i = 0; i < 3; i++) Assert.Equal(0, lib.Add(ReplayService.Capture(plan, sim.CommandLog, sim.Result, 1000 + i), 3));
            Assert.Equal(1, lib.Add(ReplayService.Capture(plan, sim.CommandLog, sim.Result, 2000), 3));
            Assert.Equal(new[] { "rp4", "rp3", "rp2" }, lib.Items.Select(x => x.Id));
            var store = new InMemorySaveStore();
            lib.Write(store);
            var loaded = ReplayLibrary.Read(store);
            Assert.Equal(3, loaded.Items.Count);
            Assert.Equal(5, loaded.NextNumber);
            Assert.True(loaded.Remove("rp3"));
            Assert.Null(loaded.Find("rp3"));
            store.Write(ReplayLibrary.SaveKey, "{망가짐");
            Assert.Empty(ReplayLibrary.Read(store).Items);
        }
    }
}
