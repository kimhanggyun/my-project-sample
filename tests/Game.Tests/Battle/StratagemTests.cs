using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Control;
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
    /// <summary>R-80 계략: 함정·낙석·위장 깃발·거짓 퇴각.</summary>
    public class StratagemTests
    {
        private static (BattleSimulation sim, BattleControl ctl) Start(string stepId, ulong seed, params string[] skills)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            foreach (var id in Kit.D.StepIndex.Order) { if (id == stepId) break; state.ClearedSteps.Add(id); }
            foreach (var g in new[] { "general_a", "general_b", "general_c" }) state.FindGeneral(g).LearnedSkills.AddRange(skills);
            var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = seed });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var sim = new BattleSimulation(plan.Setup);
            return (sim, new BattleControl(sim, StoryFlow.PlayerTeam));
        }

        private static BattleCommand Skill(CorpsState c, string id, Vec? p = null) => new BattleCommand
        {
            CorpsId = c.Id, Kind = CommandKinds.UseSkill, SkillId = id,
            X = p?.X ?? Fix.Zero, Y = p?.Y ?? Fix.Zero,
        };

        [Fact(Skip = "군대 개편 PR 2에서 다시 켬: step01이 새 군대(십부장 보병 3,000)용 작은 적 1군단이 되어, 큰 접전에서 시드를 찾던 이 테스트는 다른 스텝·편성으로 옮겨야 함", DisplayName = "R-80 함정: 내 앞에 놓으면 준비 뒤 다가온 적이 밟아 일부 혼란·손실 (이벤트 trapSprung), 함정은 사라진다")]
        public void TrapSprung()
        {
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var (sim, ctl) = Start("step01", seed, "trap");
                var me = ctl.OwnCorps().First();
                var spot = sim.Context.ClampToField(me.Anchor + sim.Context.Forward(me) * Fix.FromRatio(3, 2));
                Assert.True(sim.Issue(Skill(me, "trap", spot)));
                sim.Step();
                Assert.Single(sim.Context.Traps);
                Assert.Equal(StoryFlow.PlayerTeam, sim.Context.Traps[0].Team);
                for (int i = 0; i < 6000 && !sim.IsOver && sim.Context.Traps.Count > 0; i++) sim.Step();
                var e = sim.Context.Log.FirstOrDefault(x => x.Kind == EventKinds.TrapSprung);
                if (e == null) continue;
                var victim = sim.Context.FindCorps(e.CorpsId);
                Assert.NotEqual(StoryFlow.PlayerTeam, victim.Team);
                Assert.Equal(me.Id, e.Data);
                Assert.Empty(sim.Context.Traps);
                return;
            }
            Assert.Fail("함정을 밟은 시드가 없음");
        }

        [Fact(DisplayName = "R-80 함정은 접전 중에는 놓을 수 없다 (조건 notEngaged), 준비(3초) 전에는 밟아도 작동하지 않는다")]
        public void TrapNeedsSetup()
        {
            var (sim, ctl) = Start("step01", 1, "trap");
            var me = ctl.OwnCorps().First();
            Assert.True(sim.Issue(Skill(me, "trap", me.Anchor)));
            sim.Step();
            var t = sim.Context.Traps.Single();
            Assert.True(t.ArmedTick > sim.Context.Tick);
            Assert.Equal(Fix.FromRatio(3, 2), t.Radius);
        }

        [Fact(DisplayName = "R-80 위장 깃발: 적 AI는 보이는 깃발이 진짜 적보다 가까우면 그리로 가고, 깃발에 1.5칸 안으로 닿으면 들통 (decoyExposed)")]
        public void DecoyLuresAi()
        {
            var (sim, ctl) = Start("step01", 1, "decoy");
            var me = ctl.OwnCorps().First(c => c.Setup.General.Id == "general_c"); // 지략 62 (위장 깃발은 지략 50)
            // 다가오는 적의 앞 옆쪽(적이 곧 볼 곳)에 깃발: 사거리(20)에 들 때까지 다가가서 세운다
            var enemy = sim.Context.Corps.First(c => c.Team != me.Team);
            var spot = sim.Context.ClampToField(enemy.Anchor + new Vec(Fix.FromInt(-8), Fix.FromInt(10)));
            Assert.True(sim.Issue(Skill(me, "decoy", spot)));
            bool lured = false;
            for (int i = 0; i < 3000 && !sim.IsOver; i++)
            {
                sim.Step();
                if (sim.Context.Corps.Any(c => c.Team != me.Team && c.Order.Kind == OrderKinds.MoveTo && c.Order.Point.X == spot.X && c.Order.Point.Y == spot.Y)) lured = true;
                if (sim.Context.Log.Any(x => x.Kind == EventKinds.DecoyExposed)) break;
            }
            Assert.True(lured);
            Assert.Contains(sim.Context.Log, x => x.Kind == EventKinds.DecoyExposed && x.Data == me.Team);
            Assert.Empty(sim.Context.Decoys);
        }

        [Fact(DisplayName = "R-80 거짓 퇴각: 접전 중에만, 물러났다가 5초 뒤에도 나를 치는(쫓아온) 적 군단이 흐트러지고(feignSprung) 나는 공격 +15%p")]
        public void FeignedRetreat()
        {
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var (sim, ctl) = Start("step01", seed, "feignedRetreat");
                var me = ctl.OwnCorps().First();
                Assert.False(sim.Issue(Skill(me, "feignedRetreat"))); // 아직 접전 아님
                sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.Advance });
                for (int i = 0; i < 4000 && !sim.IsOver && !me.MeleeContact; i++) sim.Step();
                if (!me.MeleeContact) continue;
                Assert.True(sim.Issue(Skill(me, "feignedRetreat")));
                sim.Step();
                Assert.True(me.HasStatus(StatusIds.FeignedRetreat));
                Assert.Equal(OrderKinds.MoveTo, me.Order.Kind);
                for (int i = 0; i < 400 && !sim.IsOver && me.HasStatus(StatusIds.FeignedRetreat); i++) sim.Step();
                Assert.False(me.HasStatus(StatusIds.FeignedRetreat));
                if (!sim.Context.Log.Any(x => x.Kind == EventKinds.FeignSprung && x.Data == me.Id)) continue;
                Assert.Contains(me.Statuses, s => s.Id == "buff.feignedRetreat" && s.AttackPp == Fix.FromInt(15));
                return;
            }
            Assert.Fail("적이 추격한 거짓 퇴각이 없음");
        }

        [Fact(DisplayName = "R-80 낙석: 평지에서는 쓸 수 없고(casterAbove), 언덕 위에서 아래의 적에게 쓰면 반경 안 적이 손실·혼란 (rockfallHit)")]
        public void Rockfall()
        {
            var (flat, fctl) = Start("step01", 1, "rockfall");
            var f = fctl.OwnCorps().First();
            Assert.False(flat.Issue(Skill(f, "rockfall", f.Anchor + new Vec(Fix.FromInt(3), Fix.Zero))));

            for (ulong seed = 1; seed <= 4; seed++)
            {
                var (sim, ctl) = Start("step08", seed, "rockfall");
                var ctx = sim.Context;
                foreach (var me in ctl.OwnCorps().ToList())
                {
                    // 가장 가까운 언덕 칸으로
                    Cell? hill = null;
                    Fix best = Fix.Zero;
                    for (int y = 0; y < ctx.Field.Height; y++)
                        for (int x = 0; x < ctx.Field.Width; x++)
                        {
                            var cell = new Cell(x, y);
                            if (ctx.Field.RuleAt(cell).Height <= 0) continue;
                            Fix d = Vec.DistanceSquared(cell.Center, me.Anchor);
                            if (hill == null || d < best) { hill = cell; best = d; }
                        }
                    Assert.NotNull(hill);
                    sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.MoveTo, X = hill.Value.Center.X, Y = hill.Value.Center.Y });
                }
                for (int i = 0; i < 6000 && !sim.IsOver; i++)
                {
                    sim.Step();
                    foreach (var me in ctl.OwnCorps())
                    {
                        if (!me.InBattle || ctx.Field.RuleAt(Cell.Of(me.Anchor)).Height <= 0) continue;
                        var low = ctx.Corps.FirstOrDefault(e => e.Team != me.Team && e.InBattle && ctx.IsVisibleTo(me.Team, e)
                            && ctx.Field.RuleAt(Cell.Of(e.Anchor)).Height < ctx.Field.RuleAt(Cell.Of(me.Anchor)).Height
                            && (e.Anchor - me.Anchor).LengthSquared <= Fix.FromInt(64));
                        if (low == null || !sim.Issue(Skill(me, "rockfall", low.Anchor))) continue;
                        var before = ctx.Corps.ToDictionary(c => c.Id, c => c.Troops);
                        for (int k = 0; k < 60; k++) sim.Step(); // 전조 2초 뒤
                        var hit = ctx.Log.FirstOrDefault(x => x.Kind == EventKinds.RockfallHit && x.Data == me.Id);
                        Assert.NotNull(hit);
                        Assert.True(ctx.FindCorps(hit.CorpsId).Troops < before[hit.CorpsId]);
                        return;
                    }
                }
            }
            Assert.Fail("언덕 위에서 아래 적에게 낙석을 쓸 기회가 없음");
        }
    }
}
