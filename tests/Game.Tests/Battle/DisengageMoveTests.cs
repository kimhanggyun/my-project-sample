using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Control;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-88 접전 중 이동: 플레이어 보통 이동은 적에게서 멀어지는 쪽이면 움직인다.</summary>
    public class DisengageMoveTests
    {
        private static (BattleSimulation sim, CorpsState me, CorpsState enemy) Engaged(ulong seed)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            plan.Setup.Contests.Clear();
            var sim = new BattleSimulation(plan.Setup);
            var ctl = new BattleControl(sim, StoryFlow.PlayerTeam);
            var me = ctl.OwnCorps().First();
            sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.Advance });
            for (int i = 0; i < 4000 && !sim.IsOver && !me.MeleeContact; i++) sim.Step();
            return (sim, me, me.MeleeContact ? sim.Context.NearestEnemy(me) : null);
        }

        private static BattleCommand Move(CorpsState c, Vec p, string mode = null) => new BattleCommand
        {
            CorpsId = c.Id, Kind = CommandKinds.MoveTo, X = p.X, Y = p.Y, Mode = mode,
        };

        [Fact(DisplayName = "R-88 접전 중 플레이어 보통 이동: 적 반대쪽(뒤)이면 물러난다")]
        public void PullBack()
        {
            var (sim, me, enemy) = Engaged(1);
            Assert.NotNull(enemy);
            var away = sim.Context.ClampToField(me.Anchor + (me.Anchor - enemy.Anchor) * Fix.FromInt(3));
            var start = me.Anchor;
            Assert.True(sim.Issue(Move(me, away)));
            for (int i = 0; i < 60 && !sim.IsOver; i++) sim.Step();
            Assert.True(Vec.DistanceSquared(me.Anchor, start) > Fix.FromInt(1), "물러나지 않음");
        }

        [Fact(DisplayName = "R-88 접전 중 적 쪽으로 가는 보통 이동·공격 이동은 그대로 멈춘다")]
        public void TowardEnemyStops()
        {
            foreach (var mode in new[] { MoveModes.Normal, MoveModes.AttackMove })
            {
                var (sim, me, enemy) = Engaged(1);
                Assert.NotNull(enemy);
                var beyond = sim.Context.ClampToField(enemy.Anchor + (enemy.Anchor - me.Anchor) * Fix.FromInt(2));
                Assert.True(sim.Issue(Move(me, beyond, mode)));
                int moved = 0;
                for (int i = 0; i < 30 && !sim.IsOver; i++) { sim.Step(); if (me.MovedThisTick && me.MeleeContact) moved++; }
                Assert.Equal(0, moved);
            }
        }
    }
}
