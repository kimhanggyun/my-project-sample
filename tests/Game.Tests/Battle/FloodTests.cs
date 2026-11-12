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
    /// <summary>R-84 둑·수공 (step07 강 건너기 맵의 서쪽 둑).</summary>
    public class FloodTests
    {
        private static (BattleSimulation sim, CorpsState me) Start()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            foreach (var id in Kit.D.StepIndex.Order) { if (id == "step07") break; state.ClearedSteps.Add(id); }
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step07", Seed = 1 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            plan.Setup.Contests.Clear();
            var c = plan.Setup.Corps.First(x => x.Controller == Controllers.Player);
            c.General.Intellect = Fix.Max(c.General.Intellect, Fix.FromInt(70));
            c.General.Skills.Add("floodAttack");
            foreach (var e in plan.Setup.Corps.Where(x => x.Team != c.Team)) e.AiStance = Game.Battle.Model.Ai.AiStanceIds.Defensive;
            var sim = new BattleSimulation(plan.Setup);
            return (sim, sim.Context.FindCorps(c.Id));
        }

        private static bool Flood(BattleSimulation sim, CorpsState me) =>
            sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.UseSkill, SkillId = "floodAttack" });

        [Fact(DisplayName = "R-84 수공: 내 팀이 차지한 둑에 서야 쓰고, 전조 3초 뒤 잠길 곳(높이 0, 지나갈 수 있던 칸)이 물 → 20초 뒤 진흙 → 30초 뒤 원래대로")]
        public void FloodCycle()
        {
            var (sim, me) = Start();
            var ctx = sim.Context;
            Assert.False(Flood(sim, me)); // 둑에 없음
            var dam = ctx.Objects.Single(o => o.Setup.Type == "dam");
            sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.MoveTo, X = dam.Setup.X, Y = dam.Setup.Y });
            for (int i = 0; i < 3000 && dam.Owner != me.Team; i++) sim.Step();
            Assert.Equal(me.Team, dam.Owner);
            var ford = new Cell(4, 19);
            Assert.Equal("ford", ctx.Field.TerrainAt(ford));
            Assert.Equal("river", ctx.Field.TerrainAt(new Cell(8, 19)));
            Assert.True(Flood(sim, me));
            for (int i = 0; i < 31; i++) sim.Step();
            Assert.True(dam.Broken);
            Assert.Contains(ctx.Log, e => e.Kind == EventKinds.DamBroken && e.CorpsId == me.Id);
            Assert.Equal("flood", ctx.Field.TerrainAt(ford));
            Assert.Equal("river", ctx.Field.TerrainAt(new Cell(8, 19))); // 강은 그대로
            Assert.False(Flood(sim, me)); // 한 번 터진 둑
            for (int i = 0; i < 200; i++) sim.Step();
            Assert.Equal("mud", ctx.Field.TerrainAt(ford));
            for (int i = 0; i < 300; i++) sim.Step();
            Assert.Equal("ford", ctx.Field.TerrainAt(ford));
            Assert.Empty(ctx.Floods);
        }

        [Fact(DisplayName = "R-84 수공: 터질 때 잠긴 곳의 소부대는 (어느 편이든) 15% 손실·혼란, 기병은 물을 건널 수 없다")]
        public void FloodHitsAndBlocks()
        {
            Assert.Contains("mounted", Kit.D.TerrainRules["flood"].Impassable);
            var (sim, me) = Start();
            var ctx = sim.Context;
            var dam = ctx.Objects.Single(o => o.Setup.Type == "dam");
            // 둘째 아군 군단을 여울에 세워 둔다
            var other = ctx.Corps.First(c => c.Team == me.Team && c != me);
            sim.Issue(new BattleCommand { CorpsId = other.Id, Kind = CommandKinds.MoveTo, X = Fix.FromInt(5), Y = Fix.FromInt(19) + Fix.FromRatio(1, 2) });
            sim.Issue(new BattleCommand { CorpsId = me.Id, Kind = CommandKinds.MoveTo, X = dam.Setup.X, Y = dam.Setup.Y });
            for (int i = 0; i < 3000 && (dam.Owner != me.Team || (other.Anchor - new Vec(Fix.FromInt(5), Fix.FromInt(19) + Fix.FromRatio(1, 2))).LengthSquared > Fix.FromInt(2)); i++) sim.Step();
            long before = other.Troops;
            Assert.True(Flood(sim, me));
            for (int i = 0; i < 32; i++) sim.Step();
            Assert.Contains(ctx.Log, e => e.Kind == EventKinds.Flooded && e.CorpsId == other.Id);
            Assert.True(other.Troops < before);
        }
    }
}
