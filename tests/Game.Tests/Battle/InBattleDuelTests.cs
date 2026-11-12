using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Contests;
using Game.Battle.Model.Control;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-71 전투 중 일기토 (battle.md 2.7.5, 스킬 challenge).</summary>
    public class InBattleDuelTests
    {
        private static BattleSetup Setup(ulong seed)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            state.FindGeneral("general_b").LearnedSkills.Add("challenge"); // 무력 62
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            return plan.Setup;
        }

        /// <summary>장수 B가 가장 가까운 적에게 일기토를 걸고, 응함·거절이 날 때까지 돌린다.</summary>
        private static (BattleSimulation sim, CorpsState b, CorpsState target, BattleEvent answer) Challenge(ulong seed, string targetGeneral = null)
        {
            var sim = new BattleSimulation(Setup(seed));
            var ctl = new BattleControl(sim, StoryFlow.PlayerTeam);
            var b = ctl.OwnCorps().First(c => c.Setup.General.Id == "general_b");
            // 보이는 적이 생길 때까지 진군 (보이지 않는 적에게는 스킬을 쓸 수 없다)
            sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.Advance });
            CorpsState target = null;
            for (int i = 0; i < 2000 && target == null && !sim.IsOver; i++)
            {
                sim.Step();
                target = sim.Context.Corps.Where(c => c.Team != b.Team && c.InBattle && sim.Context.IsVisibleTo(b.Team, c) && (targetGeneral == null || c.Setup.General.Id == targetGeneral))
                    .OrderBy(c => (c.Anchor - b.Anchor).LengthSquared).FirstOrDefault();
            }
            if (target == null) return (sim, b, null, null);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.UseSkill, SkillId = "challenge", TargetCorpsId = target.Id }));
            for (int i = 0; i < 2000 && !sim.IsOver; i++)
            {
                var before = target.Morale;
                sim.Step();
                var e = sim.Context.Log.FirstOrDefault(x => x.Kind == EventKinds.DuelStarted || x.Kind == EventKinds.DuelRefused);
                if (e != null) { MoraleBefore = before; return (sim, b, target, e); }
            }
            return (sim, b, target, null);
        }

        private static Fix MoraleBefore;

        private static ulong FindSeed(string kind, string targetGeneral = null)
        {
            for (ulong s = 1; s < 60; s++) { var r = Challenge(s, targetGeneral); if (r.answer?.Kind == kind) return s; }
            Assert.Fail("그런 시드 없음: " + kind);
            return 0;
        }

        [Fact(DisplayName = "R-71 응함: 두 군단은 서로 싸우지 않고 명령을 받지 않는다. 선택 기록 명령으로 끝나고, 결과는 화면 계산(Simulate)과 같다")]
        public void AcceptedThenChoices()
        {
            var (sim, b, target, e) = Challenge(FindSeed(EventKinds.DuelStarted));
            var d = InBattleDuels.PendingFor(sim.Context, b.Id);
            Assert.NotNull(d);
            Assert.Equal((target.Id, b.Id), (b.DuelWith, target.DuelWith));
            Assert.False(sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.Advance })); // 대결 중 명령 없음
            var choices = new List<string> { "attack", "evade", "attack" };
            var expected = InBattleDuels.Simulate(Kit.D, sim.Context.Setup, d, choices, true);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = b.Id, Kind = CommandKinds.DuelChoices, Mode = string.Join(",", choices) }));
            sim.Step();
            Assert.NotNull(d.Result);
            Assert.Equal((expected.End, expected.Winner, expected.Rounds.Count), (d.Result.End, d.Result.Winner, d.Result.Rounds.Count));
            Assert.Null(b.DuelWith);
            Assert.Null(target.DuelWith);
            Assert.Contains(sim.Context.Log, x => x.Kind == EventKinds.DuelEnded && x.CorpsId == b.Id);
            // 명령 기록으로 다시 돌리면 같은 결과 (R-29)
            var live = sim.RunToEnd();
            var replay = BattleSimulation.Run(sim.Context.Setup, sim.CommandLog);
            Assert.Equal(live.StateHash, replay.StateHash);
        }

        [Fact(DisplayName = "R-71 화면 한 판 (BattleDuelPlay): 고른 만큼 라운드를 보여 주고, 끝낸 기록을 명령으로 넣으면 전투 결과가 화면 결과와 같다")]
        public void PlayMatchesBattle()
        {
            var (sim, b, target, _) = Challenge(FindSeed(EventKinds.DuelStarted));
            var play = new BattleDuelPlay(sim, InBattleDuels.PendingFor(sim.Context, b.Id));
            Assert.Equal("general_b", play.GeneralA);
            Assert.Empty(play.Rounds());
            Assert.True(play.Choose(play.Available()[0]));
            Assert.True(play.Rounds().Count >= 1 || play.IsOver);
            play.AutoFinish();
            var shown = play.Result;
            Assert.True(sim.Issue(play.Command()));
            sim.Step();
            var d = sim.Context.Duels.Single();
            Assert.Equal((shown.End, shown.Winner, shown.Rounds.Count), (d.Result.End, d.Result.Winner, d.Result.Rounds.Count));
        }

        [Fact(DisplayName = "R-71 기한까지 선택이 없으면 양쪽 AI로 끝낸다")]
        public void DeadlineAutoResolves()
        {
            var (sim, b, target, _) = Challenge(FindSeed(EventKinds.DuelStarted));
            var d = InBattleDuels.PendingFor(sim.Context, b.Id);
            for (long t = sim.Context.Tick; t <= d.DeadlineTick + 1 && !sim.IsOver; t++) sim.Step();
            Assert.NotNull(d.Result);
            Assert.Null(b.DuelWith);
        }

        [Fact(DisplayName = "R-71 거절: 거절한 군단 사기가 떨어지고(refuseMorale −10, 같은 틱 교전 손실 포함), 대결 없음")]
        public void Refused()
        {
            // 신중한 장수 (응할 확률 0.25)
            ulong seed = FindSeed(EventKinds.DuelRefused, "enemy_general_02");
            var (sim, b, target, e) = Challenge(seed, "enemy_general_02");
            Assert.Equal(target.Id, e.CorpsId);
            Assert.Null(target.DuelWith);
            Assert.Empty(sim.Context.Duels);
            Assert.True(target.Morale <= MoraleBefore - Fix.FromInt(10), $"{MoraleBefore} → {target.Morale}");
        }
    }
}
