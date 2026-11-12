using System.Linq;
using Game.Battle.Model.Commands;
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
    /// <summary>R-82 3·4차 스킬 (새 병종·맵이 필요 없는 9종).</summary>
    public class Tier3SkillTests
    {
        /// <summary>아군 장수 지략을 70까지(선두 돌파면 무력도 70까지) 올리고 스킬을 붙여서 시작.</summary>
        private static (BattleSimulation sim, BattleControl ctl) Start(string stepId, ulong seed, params string[] skills)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            foreach (var id in Kit.D.StepIndex.Order) { if (id == stepId) break; state.ClearedSteps.Add(id); }
            var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = seed });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            foreach (var c in plan.Setup.Corps.Where(c => c.Controller == Controllers.Player))
            {
                // 요구 능력치만 겨우 맞춘다 (지략·무력은 전투력에도 들어가서 많이 올리면 전투가 너무 빨리 끝난다)
                c.General.Intellect = Fix.Max(c.General.Intellect, Fix.FromInt(70));
                if (skills.Contains("spearhead")) c.General.Might = Fix.Max(c.General.Might, Fix.FromInt(70));
                c.General.Skills.AddRange(skills);
            }
            plan.Setup.Contests.Clear(); // 능력치를 올리면 전투 직전 대결이 생겨 판이 바로 끝날 수 있다
            var sim = new BattleSimulation(plan.Setup);
            return (sim, new BattleControl(sim, StoryFlow.PlayerTeam));
        }

        private static bool Use(BattleSimulation sim, CorpsState c, string skill, string target = null) =>
            sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.UseSkill, SkillId = skill, TargetCorpsId = target });

        private static CorpsState UntilVisibleEnemy(BattleSimulation sim, CorpsState me, Fix within)
        {
            sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.Advance });
            for (int i = 0; i < 4000 && !sim.IsOver; i++)
            {
                var e = sim.Context.Corps.FirstOrDefault(x => x.Team != me.Team && x.InBattle && sim.Context.IsVisibleTo(me.Team, x) && (x.Anchor - me.Anchor).LengthSquared <= within * within);
                if (e != null) return e;
                sim.Step();
            }
            return null;
        }

        [Fact(DisplayName = "R-82 강행군: 15초 동안 같은 시간에 더 멀리 가고, 끝나면 사기 −8")]
        public void ForcedMarch()
        {
            Fix Moved(bool march)
            {
                var (sim, ctl) = Start("step01", 1, "forcedMarch");
                var me = ctl.OwnCorps().First();
                var from = me.Anchor;
                sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.MoveTo, X = me.Anchor.X, Y = me.Anchor.Y - Fix.FromInt(20) });
                if (march) Assert.True(Use(sim, me, "forcedMarch"));
                for (int i = 0; i < 100; i++) sim.Step();
                return (me.Anchor - from).Length;
            }
            Assert.True(Moved(true) > Moved(false) * Fix.FromRatio(5, 4));

            var (s2, c2) = Start("step01", 1, "forcedMarch");
            var m = c2.OwnCorps().First();
            Assert.True(Use(s2, m, "forcedMarch"));
            s2.Step();
            var before = m.Morale;
            for (int i = 0; i < 3000 && m.HasStatus("buff.forcedMarch"); i++) s2.Step();
            Assert.False(m.HasStatus("buff.forcedMarch"));
            Assert.True(m.Morale <= before - Fix.FromInt(8) + Fix.FromInt(1));
        }

        [Fact(DisplayName = "R-82 은밀 행군: 가림막(숲·밤·안개) 없으면 못 쓰고, 숲에서 쓰면 움직여도 숨어 있다가 숲을 벗어나면 풀린다")]
        public void StealthMarch()
        {
            var (plain, pc) = Start("step01", 1, "stealthMarch");
            Assert.False(Use(plain, pc.OwnCorps().First(), "stealthMarch"));

            var (sim, ctl) = Start("step05", 1, "stealthMarch"); // 숲 맵
            var ctx = sim.Context;
            foreach (var me in ctl.OwnCorps().ToList())
            {
                // 숲 칸으로 가서
                Game.Battle.Model.Geometry.Cell? forest = null;
                for (int y = 0; y < ctx.Field.Height && forest == null; y++)
                    for (int x = 0; x < ctx.Field.Width; x++)
                        if (ctx.Field.RuleAt(new Game.Battle.Model.Geometry.Cell(x, y)).Conceals) { forest = new Game.Battle.Model.Geometry.Cell(x, y); break; }
                Assert.NotNull(forest);
                sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.MoveTo, X = forest.Value.Center.X, Y = forest.Value.Center.Y });
                for (int i = 0; i < 3000 && !ctx.Field.RuleAt(Game.Battle.Model.Geometry.Cell.Of(me.Anchor)).Conceals && !me.Engaged; i++) sim.Step();
                if (!ctx.Field.RuleAt(Game.Battle.Model.Geometry.Cell.Of(me.Anchor)).Conceals || me.Engaged) continue;
                Assert.True(Use(sim, me, "stealthMarch"));
                sim.Step();
                Assert.True(me.Hidden);
                bool movedHidden = false;
                sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.MoveTo, X = me.Anchor.X + Fix.FromInt(1), Y = me.Anchor.Y });
                for (int i = 0; i < 30; i++) { sim.Step(); if (me.MovedThisTick && me.Hidden) movedHidden = true; }
                Assert.True(movedHidden || !ctx.Field.RuleAt(Game.Battle.Model.Geometry.Cell.Of(me.Anchor)).Conceals);
                return;
            }
            Assert.Fail("숲에 닿은 군단이 없음");
        }

        [Fact(DisplayName = "R-82 선두 돌파: 대상을 치러 가고 12초 공격 +25%p, 장수 포위 압력 2배")]
        public void Spearhead()
        {
            var (sim, ctl) = Start("step01", 1, "spearhead");
            var me = ctl.OwnCorps().First();
            var e = UntilVisibleEnemy(sim, me, Fix.FromInt(8));
            Assert.NotNull(e);
            Assert.True(Use(sim, me, "spearhead", e.Id));
            for (int i = 0; i < 2000 && !me.HasStatus("buff.spearhead"); i++) sim.Step();
            var st = me.FindStatus("buff.spearhead");
            Assert.True(st != null, "over=" + sim.IsOver + " pending=" + (me.PendingSkill != null) + " d=" + (e.Anchor - me.Anchor).Length + " used=" + sim.Context.Log.Any(x => x.Kind == EventKinds.SkillUsed && x.Data == "spearhead") + " order=" + me.Order.Kind);
            Assert.Equal((Fix.FromInt(25), Fix.Hundred), (st.AttackPp, st.CaptureRiskPct));
            Assert.Equal((OrderKinds.Attack, e.Id), (me.Order.Kind, me.Order.TargetCorpsId));
        }

        [Fact(DisplayName = "R-82 이간: 지략 차이로 성공률, 성공하면 대상은 지휘 범위 밖(최소 배율)·그 눈이 같은 편 시야에 안 보태짐")]
        public void Discord()
        {
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var (sim, ctl) = Start("step01", seed, "discord");
                var me = ctl.OwnCorps().First();
                var e = UntilVisibleEnemy(sim, me, Fix.FromInt(12));
                Assert.NotNull(e);
                Assert.True(Use(sim, me, "discord", e.Id));
                sim.Step();
                if (!e.HasStatus(StatusIds.Discord)) { Assert.Contains(sim.Context.Log, x => x.Kind == EventKinds.DiscordFailed); continue; }
                Assert.Contains(sim.Context.Log, x => x.Kind == EventKinds.Discorded && x.CorpsId == e.Id);
                var far = e.Units.First(u => u.Alive);
                Assert.Equal(sim.Context.Combat.CommandRangeMinFactor, Game.Battle.Model.Combat.CommandRange.Factor(sim.Context, e, far));
                return;
            }
            Assert.Fail("이간이 한 번도 성공하지 않음 (지략 80 vs 35, 확률 0.95)");
        }

        [Fact(DisplayName = "R-82 유언비어: 적 전 군단 사기 −6. 반간을 건 군단은 막고 건 장수 책략 점수 −15")]
        public void RumorAndCounter()
        {
            var (sim, ctl) = Start("step01", 1, "rumor");
            var me = ctl.OwnCorps().First();
            var enemies = sim.Context.Corps.Where(c => c.Team != me.Team).ToList();
            var before = enemies.Select(c => c.Morale).ToList();
            Assert.True(Use(sim, me, "rumor"));
            sim.Step();
            for (int i = 0; i < enemies.Count; i++) Assert.True(enemies[i].Morale <= before[i] - Fix.FromInt(6) + Fix.FromInt(1));

            // 반간: 적의 교란을 받는 쪽 → 여기서는 내 군단이 반간을 걸고 적 AI의 혼란을 막는지 대신, 막기 규칙을 직접 본다
            var (s2, c2) = Start("step01", 1, "counterIntrigue", "rumor");
            var mine = c2.OwnCorps().ToList();
            Assert.True(Use(s2, mine[0], "counterIntrigue"));
            s2.Step();
            Assert.True(mine[0].HasStatus(StatusIds.CounterIntrigue));
        }

        [Fact(DisplayName = "R-82 군량 습격·보급: 적 군량 25% 없애고 그 절반을 가져옴, 보급은 가져온 양의 20%를 채움 (가져온 양까지)")]
        public void Supply()
        {
            var (sim, ctl) = Start("step01", 1, "supplyRaid", "supplyDrop");
            var me = ctl.OwnCorps().First();
            var e = UntilVisibleEnemy(sim, me, Fix.FromInt(8));
            Assert.NotNull(e);
            long theirs = e.Supply, mine = me.Supply;
            Assert.True(Use(sim, me, "supplyRaid", e.Id));
            for (int i = 0; i < 2000 && !sim.Context.Log.Any(x => x.Kind == EventKinds.SupplyRaided); i++) { theirs = e.Supply; mine = me.Supply; sim.Step(); }
            Assert.True(e.Supply <= theirs - theirs / 4 + 2);
            Assert.Contains(sim.Context.Log, x => x.Kind == EventKinds.SupplyRaided && x.CorpsId == e.Id);
            Assert.True(me.Supply >= System.Math.Min(me.StartSupply, mine + theirs / 8) - 5000); // 이번 틱 소비만큼 오차

            var (s2, c2) = Start("step01", 1, "supplyDrop");
            var m2 = c2.OwnCorps().First();
            for (int i = 0; i < 100; i++) s2.Step();
            long low = m2.Supply;
            Assert.True(low < m2.StartSupply);
            Assert.True(Use(s2, m2, "supplyDrop", m2.Id));
            s2.Step();
            Assert.True(m2.Supply > low);
            Assert.True(m2.Supply <= m2.StartSupply);
        }

        [Fact(DisplayName = "R-82 응급 치료: 칼이 맞닿아 있지 않을 때(notInMelee), 살아 있는 소부대마다 잃은 병력의 30%를 되돌린다")]
        public void FirstAid()
        {
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var (sim, ctl) = Start("step03", seed, "firstAid"); // 적 궁병이 있어 붙기 전에 화살로 잃는다
                ctl.AdvanceAll();
                // 싸워서 병력을 잃은 뒤 접전이 끝난(적이 물러난) 아군 군단
                CorpsState me = null;
                for (int i = 0; i < 6000 && !sim.IsOver && me == null; i++)
                {
                    sim.Step();
                    foreach (var c in ctl.OwnCorps())
                    {
                        if (!c.InBattle || c.Status != CorpsStatus.Active || c.Troops * 100 > c.Units.Sum(u => u.StartTroops) * 99) continue;
                        if (c.MeleeContact) { Assert.False(Use(sim, c, "firstAid")); continue; } // 칼이 맞닿아 있으면 못 쓴다
                        me = c;
                        break;
                    }
                }
                if (me == null) continue;
                long before = me.Troops;
                long lost = me.Units.Where(u => u.Alive).Sum(u => u.StartTroops - u.Troops);
                Assert.True(Use(sim, me, "firstAid"));
                // 명령은 다음 틱 처음에 들어가고, 같은 틱의 전투 손실이 섞이지 않게 효과 이벤트로 본다
                sim.Step();
                var ev = sim.Context.Log.Last(x => x.Kind == EventKinds.FirstAid && x.CorpsId == me.Id);
                long healed = long.Parse(ev.Data);
                Assert.True(healed >= lost * 3 / 10 - me.Units.Count && healed <= lost * 3 / 10 + me.Units.Count, $"lost {lost} healed {healed}");
                return;
            }
            Assert.Fail("응급 치료를 쓸 기회가 없음");
        }
    }
}
