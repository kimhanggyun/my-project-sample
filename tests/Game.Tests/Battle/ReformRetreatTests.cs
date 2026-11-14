using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Events;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-92 진형 전환 중 자리 옮기기, R-93 퇴각 선택 (퇴각/버티기).</summary>
    public class ReformRetreatTests
    {
        private static BattleSimulation Start(ulong seed = 1, bool delegateAll = false)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed, Delegate = delegateAll });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            plan.Setup.Contests.Clear();
            plan.Setup.EncounterContests.Clear();
            return new BattleSimulation(plan.Setup);
        }

        private static Vec Offset(CorpsState c, SubUnitState u)
        {
            Vec d = u.Position - c.Anchor;
            Vec fwd = c.Facing, right = fwd.RightOf();
            return new Vec(d.Dot(right), d.Dot(fwd));
        }

        [Fact(DisplayName = "R-92 진형 전환 중 소부대는 옛 자리에서 새 자리로 전환 시간에 걸쳐 옮겨 가고, 끝나면 새 자리")]
        public void ReformMovesGradually()
        {
            var sim = Start();
            var me = sim.Context.Corps.First(c => c.Team == StoryFlow.PlayerTeam);
            var unit = me.Units.First(u => u.Slot == "vanguard");
            string target = me.Formation.Id == "column" ? "line" : "column";
            var from = me.Formation.Slots["vanguard"];
            var to = Kit.D.Formations[target].Slots["vanguard"];
            Assert.NotEqual(from.Y, to.Y);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.ChangeFormation, FormationId = target }));
            sim.Step();
            Assert.NotNull(me.PendingFormation);
            long total = me.ReformTotalTicks;
            Assert.True(total > 2);
            for (long i = 0; i < total / 2; i++) sim.Step();
            var mid = Offset(me, unit);
            Fix tol = Fix.FromDecimal(0.05m);
            // 한가운데쯤: 옛 자리와 새 자리 사이
            Fix lo = Fix.Min(from.Y, to.Y) + tol, hi = Fix.Max(from.Y, to.Y) - tol;
            Assert.True(mid.Y > lo && mid.Y < hi, $"중간 자리 {mid.Y} (옛 {from.Y}, 새 {to.Y})");
            for (long i = 0; i < total; i++) sim.Step();
            Assert.Null(me.PendingFormation);
            var end = Offset(me, unit);
            Fix diff = end.Y - to.Y;
            Assert.True(diff <= tol && Fix.Zero - diff <= tol);
        }

        [Fact(DisplayName = "R-92 전환 중인 군단이 치면 피해 보정 reformingAttack −10%p")]
        public void ReformingAttackPenalty()
        {
            Assert.Equal(Fix.FromInt(-10), Kit.D.Combat.ReformingAttackPp);
            Assert.Equal(Fix.FromInt(10), Kit.D.Combat.ReformingDamageTakenPp);
        }

        /// <summary>사기를 직접 정한다 (시험용, 규칙 코드의 setter는 internal).</summary>
        private static void SetMorale(CorpsState c, Fix v) => typeof(CorpsState).GetProperty(nameof(CorpsState.Morale)).SetValue(c, v);

        private static CorpsState BreakMorale(BattleSimulation sim)
        {
            var me = sim.Context.Corps.First(c => c.Team == StoryFlow.PlayerTeam);
            SetMorale(me, Fix.Max(Fix.Zero, me.Temperament.RetreatMorale - Fix.FromInt(20))); // 고무(+15)를 써도 기준 밑
            sim.Step();
            return me;
        }

        [Fact(DisplayName = "R-93 직접 지휘하는 군단은 퇴각 조건에서 바로 퇴각하지 않고 묻는다 (retreatAsked, 까닭 morale), 답이 없으면 askSec 뒤 퇴각")]
        public void AskThenRetreatOnTimeout()
        {
            var sim = Start();
            var me = BreakMorale(sim);
            Assert.Equal(CorpsStatus.Active, me.Status);
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.RetreatAsked && e.CorpsId == me.Id && e.Data == "morale");
            long ticks = (Kit.D.Combat.RetreatAskSec / Kit.D.Combat.TickSeconds).CeilToLong();
            for (long i = 0; i < ticks + 2 && me.Status == CorpsStatus.Active; i++) { SetMorale(me, Fix.Min(me.Morale, me.Temperament.RetreatMorale - Fix.FromInt(1))); sim.Step(); }
            Assert.Equal(CorpsStatus.Retreating, me.Status);
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.RetreatStarted && e.CorpsId == me.Id && e.Data == "morale");
        }

        [Fact(DisplayName = "R-93 버티기: 퇴각하지 않고 싸우다 사기 0 또는 병력 10% 이하면 무너져 퇴각 (rout)")]
        public void StandFastThenRout()
        {
            var sim = Start();
            var me = BreakMorale(sim);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.RetreatAnswer, Mode = "hold" }));
            sim.Step();
            Assert.True(me.StandFast);
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.StandFast && e.CorpsId == me.Id);
            for (int i = 0; i < 200; i++) { SetMorale(me, Fix.FromInt(5)); sim.Step(); }
            Assert.Equal(CorpsStatus.Active, me.Status); // 사기 5로는 버틴다
            SetMorale(me, Fix.Zero);
            sim.Step();
            Assert.Equal(CorpsStatus.Retreating, me.Status);
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.RetreatStarted && e.CorpsId == me.Id && e.Data == "rout");
        }

        [Fact(DisplayName = "R-93 [퇴각]으로 답하면 바로 퇴각, 위임 군단과 AI는 묻지 않고 바로 퇴각, 명령 퇴각은 까닭 order")]
        public void AnswerRetreatAndDelegated()
        {
            var sim = Start();
            var me = BreakMorale(sim);
            Assert.True(sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.RetreatAnswer, Mode = "retreat" }));
            sim.Step();
            Assert.Equal(CorpsStatus.Retreating, me.Status);

            var sim2 = Start(1, true);
            var d = BreakMorale(sim2);
            Assert.Equal(CorpsStatus.Retreating, d.Status);
            Assert.DoesNotContain(sim2.Context.Log, e => e.Kind == EventKinds.RetreatAsked);

            var sim3 = Start();
            var o = sim3.Context.Corps.First(c => c.Team == StoryFlow.PlayerTeam);
            Assert.True(sim3.Issue(new BattleCommand { CorpsId = o.Id, Kind = CommandKinds.Retreat }));
            sim3.Step();
            Assert.Contains(sim3.Context.Log, e => e.Kind == EventKinds.RetreatStarted && e.Data == "order");
        }
    }
}
