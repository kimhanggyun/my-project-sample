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
    /// <summary>R-83 공병·투석기: 목책·다리 파괴·집중 포격, 병종 규칙.</summary>
    public class EngineeringTests
    {
        /// <summary>첫 아군 군단의 부대를 모두 그 병종으로 바꾸고 스킬을 붙인다.</summary>
        private static (BattleSimulation sim, CorpsState me) Start(string stepId, string troop, params string[] skills) => Start(stepId, troop, false, skills);

        private static (BattleSimulation sim, CorpsState me) Start(string stepId, string troop, bool enemyHolds, params string[] skills)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            foreach (var id in Kit.D.StepIndex.Order) { if (id == stepId) break; state.ClearedSteps.Add(id); }
            var plan = flow.Prepare(state, new SortieRequest { StepId = stepId, Seed = 1 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            plan.Setup.Contests.Clear();
            var c = plan.Setup.Corps.First(x => x.Controller == Controllers.Player);
            if (troop != null) foreach (var u in c.Units) u.TroopType = troop;
            c.General.Skills.AddRange(skills);
            if (enemyHolds) foreach (var e in plan.Setup.Corps.Where(x => x.Team != c.Team)) e.AiStance = Game.Battle.Model.Ai.AiStanceIds.Defensive;
            var sim = new BattleSimulation(plan.Setup);
            return (sim, sim.Context.FindCorps(c.Id));
        }

        private static bool Use(BattleSimulation sim, CorpsState c, string skill, Vec p) =>
            sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.UseSkill, SkillId = skill, X = p.X, Y = p.Y });

        [Fact(DisplayName = "R-83 병종: 투석기(T1, 사거리 18, 숲·산 못 감, 기병·경보병에 약함), 공병(T2, 보병에서 승급)")]
        public void TroopRules()
        {
            var cat = Kit.D.TroopTypes["catapult"];
            Assert.Equal((1, Fix.FromInt(18)), (cat.Tier, cat.AttackRange));
            Assert.True(cat.HasTag("siege"));
            Assert.Contains("siege", Kit.D.TerrainRules["forest"].Impassable);
            Assert.Contains("siege", Kit.D.TerrainRules["mountain"].Impassable);
            var eng = Kit.D.TroopTypes["engineer"];
            Assert.Equal(("infantry", 2), (eng.Parent, eng.Tier));
            Assert.True(eng.HasTag("engineer"));
        }

        [Fact(DisplayName = "R-83 목책: 공병만, 5초 뒤 서고, 안에 선 같은 편이 받는 피해 −15%p (기병이 치면 −30%p 더), 불이 붙으면 무너진다")]
        public void Palisade()
        {
            var (plain, p0) = Start("step01", null, "palisade");
            Assert.False(Use(plain, p0, "palisade", p0.Anchor)); // 공병 없음

            var (sim, me) = Start("step01", "engineer", "palisade");
            Assert.True(Use(sim, me, "palisade", me.Anchor));
            sim.Step();
            var pal = Assert.Single(sim.Context.Palisades);
            Assert.False(pal.Ready(sim.Context));
            Assert.Contains(sim.Context.Log, e => e.Kind == EventKinds.PalisadeBuilt && e.CorpsId == me.Id);
            for (int i = 0; i < 60; i++) sim.Step();
            Assert.True(pal.Ready(sim.Context));
            Assert.True(pal.Covers(me.Anchor));
        }

        [Fact(DisplayName = "R-83 다리 파괴: 공병이 다리 칸에만, 작업 5초 뒤 반경 안 다리가 강이 되어 건널 수 없다")]
        public void DestroyBridge()
        {
            var (sim, me) = Start("step07", "engineer", true, "destroyBridge"); // 적은 제자리 (다리까지 가는 동안 싸우지 않게)
            var ctx = sim.Context;
            var bridge = new Cell(16, 19);
            Assert.Equal("bridge", ctx.Field.TerrainAt(bridge));
            Assert.False(Use(sim, me, "destroyBridge", new Cell(10, 19).Center)); // 다리가 아님
            Assert.True(Use(sim, me, "destroyBridge", new Vec(Fix.FromInt(17), Fix.FromInt(19) + Fix.FromRatio(1, 2)))); // 멀면 다가가서
            for (int i = 0; i < 3000 && !ctx.Log.Any(e => e.Kind == EventKinds.BridgeDestroyed) && !sim.IsOver; i++) sim.Step();
            Assert.True(ctx.Log.Any(e => e.Kind == EventKinds.BridgeDestroyed && e.CorpsId == me.Id),
                $"over={sim.IsOver} tick={ctx.Tick} st={me.Status} pending={me.PendingSkill != null} used={ctx.Log.Any(e => e.Kind == EventKinds.SkillUsed && e.Data == "destroyBridge")} at={me.Anchor.X},{me.Anchor.Y} order={me.Order.Kind}");
            for (int y = 18; y <= 20; y++)
                for (int x = 16; x <= 17; x++)
                    Assert.Equal("river", ctx.Field.TerrainAt(new Cell(x, y)));
        }

        [Fact(DisplayName = "R-83 집중 포격: 투석기가 있어야 쓰고, 전조 2초 뒤 반경 2 안 적에게 투석기 병력 × 0.6 피해")]
        public void Bombard()
        {
            var (plain, p0) = Start("step01", null, "bombard");
            var enemy0 = plain.Context.Corps.First(c => c.Team != p0.Team);
            Assert.False(Use(plain, p0, "bombard", enemy0.Anchor));

            var (sim, me) = Start("step01", "catapult", "bombard");
            var ctx = sim.Context;
            CorpsState e = null;
            for (int i = 0; i < 3000 && e == null && !sim.IsOver; i++)
            {
                sim.Step();
                e = ctx.Corps.FirstOrDefault(x => x.Team != me.Team && x.InBattle && ctx.IsVisibleTo(me.Team, x) && (x.Anchor - me.Anchor).LengthSquared <= Fix.FromInt(18 * 18));
            }
            Assert.NotNull(e);
            long before = e.Troops;
            Assert.True(Use(sim, me, "bombard", e.Anchor));
            for (int i = 0; i < 25; i++) sim.Step();
            Assert.True(e.Troops < before);
        }
    }
}
