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
    /// <summary>전투 화면 입력 계층 (battle.md 4.1·4.2·4.3): 선택·누름·버튼 → 명령. 받을지는 전투 규칙이 정한다.</summary>
    public class BattleControlTests
    {
        private static BattleSetup Step01Manual(ulong seed = 1)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Seed = seed });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            return plan.Setup;
        }

        private static (BattleSimulation sim, BattleControl ctl) Make()
        {
            var sim = new BattleSimulation(Step01Manual());
            return (sim, new BattleControl(sim, StoryFlow.PlayerTeam));
        }

        private static CorpsState Own(BattleControl ctl, string generalId) => ctl.OwnCorps().First(c => c.Setup.General.Id == generalId);

        /// <summary>군단 중심에서 먼 빈 땅 (전장 안).</summary>
        private static Vec EmptyPoint(BattleContext ctx, CorpsState from)
        {
            var map = ctx.Field.Map;
            return new Vec(Fix.FromInt(map.Width / 2), from.Anchor.Y < Fix.FromInt(map.Height / 2) ? Fix.FromInt(map.Height / 2 - 2) : Fix.FromInt(map.Height / 2 + 2));
        }

        [Fact(DisplayName = "선택: 아군 플레이어 군단만 고를 수 있고, 적은 고를 수 없다")]
        public void Selection()
        {
            var (sim, ctl) = Make();
            Assert.Equal(sim.Context.Corps.Count(c => c.Team == StoryFlow.PlayerTeam), ctl.OwnCorps().Count);
            Assert.True(ctl.Select(ctl.OwnCorps()[0].Id));
            Assert.Same(ctl.OwnCorps()[0], ctl.Selected);
            var enemy = sim.Context.Corps.First(c => c.Team == StoryFlow.EnemyTeam);
            Assert.False(ctl.Select(enemy.Id));
            Assert.Same(ctl.OwnCorps()[0], ctl.Selected);
        }

        [Fact(DisplayName = "전군 진군: 지휘 가능한 아군 군단마다 진군 명령 하나")]
        public void AdvanceAll()
        {
            var (sim, ctl) = Make();
            int n = ctl.AdvanceAll();
            Assert.Equal(ctl.OwnCorps().Count(ctl.CanCommand), n);
            Assert.Equal(n, sim.CommandLog.Count(c => c.Kind == CommandKinds.Advance));
            sim.Step();
            Assert.All(ctl.OwnCorps().Where(c => c.InBattle), c => Assert.Equal(OrderKinds.Advance, c.Order.Kind));
        }

        [Fact(DisplayName = "전장 누름: 선택 없이 빈 땅은 무시, 선택 후 빈 땅은 고른 이동 방식으로 이동, 아군 부대원을 누르면 그 군단 선택")]
        public void TapMoveAndSelect()
        {
            var (sim, ctl) = Make();
            var a = Own(ctl, "general_a");
            var b = Own(ctl, "general_b");
            var p = EmptyPoint(sim.Context, a);
            Assert.Equal(TapResult.None, ctl.Tap(p));

            Assert.Equal(TapResult.Selected, ctl.Tap(b.Units.First(u => u.Alive).Position));
            Assert.Same(b, ctl.Selected);

            ctl.Select(a.Id);
            ctl.SetMoveMode(MoveModes.Forced);
            Assert.Equal(TapResult.Move, ctl.Tap(p));
            var cmd = sim.CommandLog.Last();
            Assert.Equal(CommandKinds.MoveTo, cmd.Kind);
            Assert.Equal(a.Id, cmd.CorpsId);
            Assert.Equal(MoveModes.Forced, cmd.Mode);
            Assert.Equal(p, new Vec(cmd.X, cmd.Y));
            Assert.Equal(TapResult.None, ctl.Tap(new Vec(Fix.FromInt(-1), Fix.FromInt(-1)))); // 전장 밖
        }

        [Fact(DisplayName = "적 군단: 보이지 않으면 고를 수 없고 (빈 땅 = 이동), 보이면 선택 군단의 공격 대상이 된다")]
        public void TapEnemy()
        {
            var (sim, ctl) = Make();
            var ctx = sim.Context;
            ctl.Select(ctl.OwnCorps()[0].Id);
            var enemy = ctx.Corps.First(c => c.Team == StoryFlow.EnemyTeam && c.InBattle);
            var at = enemy.Units.First(u => u.Alive).Position;
            if (!ctx.IsVisibleTo(StoryFlow.PlayerTeam, enemy))
                Assert.Equal(TapResult.Move, ctl.Tap(at));

            ctl.AdvanceAll();
            for (int i = 0; i < 2000 && !ctx.IsVisibleTo(StoryFlow.PlayerTeam, enemy) && !sim.IsOver; i++) sim.Step();
            Assert.True(ctx.IsVisibleTo(StoryFlow.PlayerTeam, enemy), "진군해도 적이 보이지 않았습니다.");
            ctl.Refresh();
            Assert.Equal(TapResult.Attack, ctl.Tap(enemy.Units.First(u => u.Alive).Position));
            Assert.Equal(CommandKinds.Attack, sim.CommandLog.Last().Kind);
            Assert.Equal(enemy.Id, sim.CommandLog.Last().TargetCorpsId);
        }

        [Fact(DisplayName = "스킬: 자신 대상은 누르자마자, 지점 대상은 지정 모드 → 누른 지점에 쓴다. 같은 버튼을 다시 누르면 지정 취소")]
        public void Skills()
        {
            var (sim, ctl) = Make();
            var a = Own(ctl, "general_a"); // rally (자신)
            ctl.Select(a.Id);
            var rally = ctl.Skills().Single(s => s.SkillId == "rally");
            Assert.Equal("self", rally.TargetType);
            Assert.True(ctl.PressSkill("rally"));
            Assert.Null(ctl.TargetingSkill);
            Assert.Equal(CommandKinds.UseSkill, sim.CommandLog.Last().Kind);

            var c = Own(ctl, "general_c"); // reveal (지점)
            ctl.Select(c.Id);
            Assert.True(ctl.PressSkill("reveal"));
            Assert.Equal("reveal", ctl.TargetingSkill);
            Assert.False(ctl.PressSkill("reveal"));
            Assert.Null(ctl.TargetingSkill);

            ctl.PressSkill("reveal");
            var p = EmptyPoint(sim.Context, c);
            Assert.Equal(TapResult.Skill, ctl.Tap(p));
            var cmd = sim.CommandLog.Last();
            Assert.Equal(("reveal", c.Id, p), (cmd.SkillId, cmd.CorpsId, new Vec(cmd.X, cmd.Y)));
            Assert.Null(ctl.TargetingSkill);
            Assert.False(ctl.PressSkill("rally")); // 이 장수는 모르는 스킬
        }

        [Fact(DisplayName = "진형 버튼은 배운 진형을 차례로, 위임 켜기·끄기는 다음 틱에 반영된다")]
        public void FormationAndDelegate()
        {
            var (sim, ctl) = Make();
            var a = ctl.OwnCorps()[0];
            ctl.Select(a.Id);
            string before = a.Formation.Id;
            Assert.True(ctl.Formations().Count > 1);
            Assert.True(ctl.NextFormation());
            Assert.True(ctl.SetDelegate(true));
            sim.Step();
            Assert.NotEqual(before, a.Formation.Id);
            Assert.True(a.Delegated);
            Assert.True(ctl.SetDelegate(false));
            sim.Step();
            Assert.False(a.Delegated);
        }

        [Fact(DisplayName = "직접 지휘한 전투도 명령 기록으로 다시 돌리면 같은 결과 (R-29)")]
        public void CommandedBattleReplays()
        {
            var (sim, ctl) = Make();
            var a = Own(ctl, "general_a");
            ctl.Select(a.Id);
            ctl.PressSkill("rally");
            ctl.AdvanceAll();
            for (int i = 0; i < 100; i++) sim.Step();
            ctl.Select(Own(ctl, "general_c").Id);
            ctl.MoveTo(EmptyPoint(sim.Context, a), MoveModes.AttackMove);
            var live = sim.RunToEnd();
            var replay = BattleSimulation.Run(Step01Manual(), sim.CommandLog);
            Assert.Equal(live.StateHash, replay.StateHash);
            Assert.Equal(live.Ticks, replay.Ticks);
        }
    }
}
