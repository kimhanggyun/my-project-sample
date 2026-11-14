using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-76 2차 스킬: 혼란·철벽·진형 급변·집중 사격·화전 일제사격·척후 파견, 재정비 범위.</summary>
    public class SkillTier2Tests
    {
        private static (BattleSimulation sim, CorpsState corps) Make(string general, params string[] skills)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            state.FindGeneral(general).LearnedSkills.AddRange(skills);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 1 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var sim = new BattleSimulation(plan.Setup);
            return (sim, sim.Context.Corps.First(c => c.Setup.General?.Id == general));
        }

        private static bool Use(BattleSimulation sim, CorpsState c, string skill, string target = null) =>
            sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.UseSkill, SkillId = skill, TargetCorpsId = target });

        [Fact(DisplayName = "데이터: 새 스킬 6종이 있고 검사를 통과, 재정비는 둘레 6칸 아군도 혼란을 푼다")]
        public void Data()
        {
            foreach (var id in new[] { "confusion", "holdTheLine", "quickFormation", "focusFire", "fireVolley", "scoutDispatch" })
                Assert.True(Kit.D.Skills.ContainsKey(id), id);
            Assert.Equal(Fix.FromInt(6), Kit.D.Skills["regroup"].Effects[0].Fix("allyRadius"));
        }

        [Fact(DisplayName = "철벽: 쓰면 제자리에 서서 이동 명령을 받아도 움직이지 않는다 (지속 동안)")]
        public void HoldTheLine()
        {
            var (sim, c) = Make("general_c", "holdTheLine"); // 통솔 50
            Assert.True(Use(sim, c, "holdTheLine"));
            sim.Step();
            Assert.Contains(c.Statuses, s => s.Immobile && s.DefensePp == Fix.FromInt(20));
            var start = c.Anchor;
            Assert.True(sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.Advance }));
            for (int i = 0; i < 50; i++) sim.Step();
            Assert.Equal(start, c.Anchor);
        }

        [Fact(DisplayName = "진형 급변: 쓴 뒤 다음 진형 변경은 바로 바뀐다")]
        public void QuickFormation()
        {
            var (sim, c) = Make("general_a", "quickFormation"); // 통솔 60
            string other = c.Setup.KnownFormations.First(f => f != c.Formation.Id);
            Assert.True(Use(sim, c, "quickFormation"));
            sim.Step();
            Assert.True(sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.ChangeFormation, FormationId = other }));
            sim.Step();
            Assert.Equal(other, c.Formation.Id);
            Assert.Null(c.PendingFormation);
            Assert.False(c.HasStatus(StatusIds.QuickFormation)); // 한 번만
        }

        [Fact(DisplayName = "혼란·집중 사격: 보이는 적에게 쓰면 다가가 쓰고, 혼란은 적 소부대 일부를, 집중 사격은 그 군단을 노리는 상태를 남긴다")]
        public void ConfusionAndFocus()
        {
            var (sim, c) = Make("general_c", "confusion", "focusFire"); // 지략 62
            sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.Advance });
            CorpsState target = null;
            for (int i = 0; i < 2000 && target == null && !sim.IsOver; i++)
            {
                sim.Step();
                target = sim.Context.Corps.FirstOrDefault(e => e.Team != c.Team && e.InBattle && sim.Context.IsVisibleTo(c.Team, e));
            }
            Assert.NotNull(target);
            Assert.True(Use(sim, c, "focusFire", target.Id));
            Assert.True(Use(sim, c, "confusion", target.Id));
            bool confused = false, focused = false;
            for (int i = 0; i < 2000 && !sim.IsOver && !(confused && focused); i++)
            {
                sim.Step();
                focused |= c.Statuses.Any(s => s.Id == StatusIds.FocusFire && s.SourceCorpsId == target.Id);
                confused |= sim.Context.Log.Any(e => e.Kind == EventKinds.SkillUsed && e.CorpsId == c.Id && e.Data == "confusion");
            }
            Assert.True(focused, "집중 사격 상태");
            Assert.True(confused, "혼란을 썼다");
        }
    }
}
