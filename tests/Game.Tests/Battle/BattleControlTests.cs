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

        [Fact(DisplayName = "R-60·R-63 전군 진군: 모두 자기 줄 그대로 앞으로 곧게 (측면도 벌리지 않는다), 후위만 지원 이동")]
        public void AdvanceAll()
        {
            var (sim, ctl) = Make();
            int n = ctl.AdvanceAll();
            Assert.Equal(ctl.OwnCorps().Count(ctl.CanCommand), n);
            var moves = sim.CommandLog.Where(c => c.Kind == CommandKinds.MoveTo).ToList();
            Assert.Equal(n, moves.Count); // 군단마다 하나 (벌리기 단계 없음)
            foreach (var m in moves)
            {
                var c = sim.Context.FindCorps(m.CorpsId);
                Assert.Equal(c.Anchor.X, m.X); // 가로 위치 그대로
                Assert.True(m.Y < c.Anchor.Y); // 앞(북쪽)으로
                Assert.Equal(c.Setup.AdvanceRole == AdvanceRoles.Rear ? MoveModes.Support : MoveModes.AttackMove, m.Mode);
            }
            Assert.Equal(n, moves.Select(m => m.X).Distinct().Count()); // 줄이 겹치지 않는다
        }

        [Fact(DisplayName = "R-63 후위 지원 이동: 적이 rearStandoffCells 안에 들어오면 그 자리에 멈춘다 (쫓지 않는다)")]
        public void RearStandsOff()
        {
            var (sim, ctl) = Make();
            var rear = Own(ctl, "general_c");
            ctl.Select(rear.Id);
            Assert.True(ctl.Advance());
            var keep = sim.Context.Combat.RearStandoffCells;
            int t = 0;
            for (; t < 2000; t++)
            {
                sim.Step();
                var near = sim.Context.NearestEnemy(rear);
                if (near != null && Vec.DistanceSquared(near.Anchor, rear.Anchor) <= keep * keep) break;
            }
            Assert.True(t < 2000, "적이 가까이 오지 않음");
            var at = rear.Anchor;
            for (int i = 0; i < 10; i++) sim.Step();
            Assert.True(Vec.DistanceSquared(at, rear.Anchor) <= Fix.FromInt(1), "후위가 멈추지 않고 나아감"); // 1칸 안 (밀림 정도)
        }

        [Fact(DisplayName = "R-60 후퇴: 뒤로 곧게 fallBackCells칸 강행 이동, 계속 명령할 수 있어 다시 진군하면 앞으로 (퇴각과 다르다)")]
        public void FallBackThenAdvance()
        {
            var (sim, ctl) = Make();
            var a = ctl.OwnCorps()[0];
            ctl.Select(a.Id);
            ctl.AdvanceAll();
            for (int i = 0; i < 60; i++) sim.Step();
            var before = a.Anchor;
            Assert.True(ctl.FallBack());
            var back = sim.CommandLog.Last();
            Assert.Equal((CommandKinds.MoveTo, MoveModes.Forced), (back.Kind, back.Mode));
            Assert.Equal(before.X, back.X);
            Assert.True(back.Y > before.Y); // 남쪽(뒤)으로
            for (int i = 0; i < 30; i++) sim.Step();
            Assert.Equal(CorpsStatus.Active, a.Status); // 전장을 떠나지 않는다
            Assert.True(ctl.CanCommand(a));
            Assert.True(ctl.Advance());
            var fwd = sim.CommandLog.Last();
            Assert.True(fwd.Y < a.Anchor.Y); // 다시 앞으로
            Assert.True(ctl.Retreat()); // 퇴각은 따로 (전장 이탈)
            sim.Step();
            Assert.Equal(CorpsStatus.Retreating, a.Status);
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
            Assert.True(a.Reforming); // 규율(R-56)만큼 전환 시간
            string target = a.PendingFormation.Id;
            Assert.NotEqual(before, target);
            while (a.Reforming) sim.Step();
            Assert.Equal(target, a.Formation.Id);
            Assert.True(a.Delegated);
            Assert.True(ctl.SetDelegate(false));
            sim.Step();
            Assert.False(a.Delegated);
        }

        [Fact(DisplayName = "녹화 재생 보기: 안개 끄고 보기면 모든 군단이 보이고, 적군 시점이면 적군 시야로 본다 (전투 결과는 같다)")]
        public void ReplayViewOptions()
        {
            var setup = Step01Manual();
            var sim = new BattleSimulation(setup);
            sim.Step();
            var ally = new BattleControl(sim, StoryFlow.PlayerTeam);
            var enemyCorps = sim.Context.Corps.First(c => c.Team == StoryFlow.EnemyTeam);
            Assert.False(ally.Sees(enemyCorps)); // 시작 때는 적이 안개 속
            var reveal = new BattleControl(sim, StoryFlow.PlayerTeam) { RevealAll = true };
            Assert.All(sim.Context.Corps, c => Assert.True(reveal.Sees(c)));
            var enemyView = new BattleControl(sim, StoryFlow.EnemyTeam);
            Assert.True(enemyView.Sees(enemyCorps));
            // 보기만 바꾼다: 같은 전투를 끝까지 돌리면 결과가 같다
            var a = sim.RunToEnd();
            var b = BattleSimulation.Run(Step01Manual());
            Assert.Equal(b.StateHash, a.StateHash);
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

        [Fact(DisplayName = "예약 모드: 빈 땅·적 누름과 휴식·협공이 예약 명령이 된다")]
        public void QueueingMode()
        {
            var (sim, ctl) = Make();
            var a = Own(ctl, "general_a");
            ctl.Select(a.Id);
            Assert.True(ctl.MoveTo(EmptyPoint(sim.Context, a)));
            ctl.SetQueueing(true);
            Assert.Equal(TapResult.Move, ctl.Tap(new Vec(Fix.FromInt(3), Fix.FromInt(20))));
            Assert.True(sim.CommandLog.Last().Queued);
            Assert.True(ctl.Coordinate());
            Assert.Equal((CommandKinds.Hold, HoldModes.Coordinate, true), (sim.CommandLog.Last().Kind, sim.CommandLog.Last().Mode, sim.CommandLog.Last().Queued));
            sim.Step();
            Assert.Equal(2, ctl.QueueCount);
            ctl.SetQueueing(false);
            Assert.True(ctl.Rest());
            sim.Step();
            Assert.Equal(0, ctl.QueueCount); // 예약 아닌 새 명령이 예약을 비움
            Assert.True(a.Resting);
        }

        [Fact(DisplayName = "이동 방식 고르기 (4.3.5): 경유는 첫 지점 바로, 다음 지점부터 예약. 도착 후 스킬은 이동 + 지점 스킬 예약")]
        public void PointThenMethod()
        {
            var (sim, ctl) = Make();
            var a = Own(ctl, "general_a");
            ctl.Select(a.Id);
            var p1 = EmptyPoint(sim.Context, a);
            Assert.True(ctl.MoveTo(p1, MoveModes.Forced, false));
            Assert.True(ctl.MoveTo(new Vec(Fix.FromInt(3), Fix.FromInt(20)), MoveModes.Normal, true));
            sim.Step();
            Assert.Equal((OrderKinds.MoveTo, 1), (a.Order.Kind, ctl.QueueCount));
            Assert.Equal(MoveModes.Forced, sim.CommandLog.First(c => c.CorpsId == a.Id).Mode);

            var c = Own(ctl, "general_c"); // reveal (지점)
            ctl.Select(c.Id);
            var p = EmptyPoint(sim.Context, c);
            Assert.True(ctl.MoveThenSkill(p, MoveModes.Normal, "reveal", false));
            var skill = sim.CommandLog.Last();
            Assert.Equal((CommandKinds.UseSkill, "reveal", true, p), (skill.Kind, skill.SkillId, skill.Queued, new Vec(skill.X, skill.Y)));
            sim.Step();
            Assert.Equal((OrderKinds.MoveTo, 1), (c.Order.Kind, ctl.QueueCount)); // 이동 중, 스킬은 도착 뒤
            Assert.False(ctl.MoveThenSkill(p, MoveModes.Normal, "rally", false)); // 모르는 스킬
            Assert.False(BattleControl.UsableOnArrival("enemyCorps"));
        }

        [Fact(DisplayName = "스킬 확인 모드 (4.2): 지점 스킬은 누름으로 자리만 잡고(미리보기 반지름·사거리), 확인하면 쓴다. 취소하면 아무 명령 없음")]
        public void AimThenConfirm()
        {
            var (sim, ctl) = Make();
            ctl.ConfirmPointSkills = true;
            var c = Own(ctl, "general_c"); // reveal (지점, 효과 반지름 4)
            ctl.Select(c.Id);
            Assert.Equal(Fix.FromInt(4), ctl.PreviewRadius("reveal"));
            Assert.Equal(Fix.FromInt(2), ctl.PreviewRadius("arrowRain")); // 대상 범위
            int logged = sim.CommandLog.Count;
            Assert.True(ctl.PressSkill("reveal"));
            var p = EmptyPoint(sim.Context, c);
            Assert.Equal(TapResult.Aimed, ctl.Tap(p));
            Assert.Equal(p, ctl.AimPoint);
            Assert.True(ctl.AimInRange()); // 사거리 없음
            var p2 = new Vec(p.X + Fix.FromInt(1), p.Y);
            Assert.Equal(TapResult.Aimed, ctl.Tap(p2)); // 다시 누르면 자리를 옮긴다
            Assert.Equal(logged, sim.CommandLog.Count);
            ctl.CancelTargeting();
            Assert.Null(ctl.AimPoint);
            Assert.False(ctl.ConfirmAim());

            ctl.PressSkill("reveal");
            ctl.Tap(p2);
            Assert.True(ctl.ConfirmAim());
            var cmd = sim.CommandLog.Last();
            Assert.Equal(("reveal", p2), (cmd.SkillId, new Vec(cmd.X, cmd.Y)));
            Assert.Null(ctl.TargetingSkill);
        }

        [Fact(DisplayName = "R-69 복병 지점 지정: 자리를 먼저 고르는 스킬은 그 자리로 이동 + 도착 후 복병 예약 (지형 조건은 도착해서 보고, 안 맞으면 건너뛰고 알림)")]
        public void AmbushPoint()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            state.FindGeneral("general_c").LearnedSkills.Add("ambush");
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 1 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var sim = new BattleSimulation(plan.Setup);
            var ctl = new BattleControl(sim, StoryFlow.PlayerTeam);
            var c = Own(ctl, "general_c");
            ctl.Select(c.Id);
            Assert.True(Kit.D.Skills["ambush"].MoveFirst);
            Assert.True(ctl.PressSkill("ambush"));
            Assert.Equal("ambush", ctl.TargetingSkill); // 바로 쓰지 않고 자리 고르기
            // 적에게서 먼 쪽(뒤)으로 두 칸: 싸우지 않고 도착한다
            var back = c.Anchor.Y < Fix.FromInt(sim.Context.Field.Map.Height / 2) ? -2 : 2;
            var p = new Vec(c.Anchor.X, c.Anchor.Y + Fix.FromInt(back));
            Assert.Equal(TapResult.Skill, ctl.Tap(p)); // 확인 모드가 아니면 바로 (이동 + 예약)
            var move = sim.CommandLog[sim.CommandLog.Count - 2];
            var skill = sim.CommandLog.Last();
            Assert.Equal((CommandKinds.MoveTo, CommandKinds.UseSkill, "ambush", true), (move.Kind, skill.Kind, skill.SkillId, skill.Queued));
            sim.Step();
            Assert.Single(c.Queue); // 넓은 땅이라 지금은 쓸 수 없어도 예약은 받는다
            // 도착할 때까지 돌린다: 그 자리가 숨을 땅이 아니면 건너뛰고 알림
            for (int i = 0; i < 3000 && c.Queue.Count > 0 && c.InBattle; i++) sim.Step();
            Assert.True(c.Queue.Count == 0, $"tick {sim.Context.Tick} over {sim.IsOver} order {c.Order.Kind} anchor {c.Anchor} target {p} inBattle {c.InBattle}");
            bool used = sim.Context.Log.Any(e => e.Kind == Game.Battle.Model.Events.EventKinds.SkillUsed && e.CorpsId == c.Id && e.Data == "ambush");
            bool skipped = sim.Context.Log.Any(e => e.Kind == Game.Battle.Model.Events.EventKinds.QueuedSkipped && e.CorpsId == c.Id && e.Data == "ambush");
            Assert.True(used ^ skipped);
        }
    }
}
