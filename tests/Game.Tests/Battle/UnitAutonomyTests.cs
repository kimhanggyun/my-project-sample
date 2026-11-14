using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Battle.Model.Systems;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-74 소부대 자율 판단 (battle.md 2.2.2).</summary>
    public class UnitAutonomyTests
    {
        private static BattleSetup Step01(bool on = true)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(Kit.Veteran(), new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(plan.Ok);
            if (!on) plan.Setup.Flags = Kit.D.ResolveFlags(Newtonsoft.Json.Linq.JObject.Parse("{\"unitAutonomy\": false}"));
            return plan.Setup;
        }

        [Fact(DisplayName = "상대 고르기: 같은 거리면 상성이 유리한 상대가 점수가 높다 (판단력 반영)")]
        public void PrefersCounter()
        {
            var sim = new BattleSimulation(Step01());
            var mine = sim.Context.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).SelectMany(c => c.Units).First(u => u.Troop.Id == "spearman");
            var enemies = sim.Context.Corps.Where(c => c.Team != StoryFlow.PlayerTeam).SelectMany(c => c.Units).ToList();
            Fix d = Fix.FromInt(4);
            Fix Counter(SubUnitState v) => Kit.D.TroopCounters.Where(r => mine.Troop.HasTag(r.AttackerTag) && v.Troop.HasTag(r.DefenderTag)).Aggregate(Fix.Zero, (a, r) => a + r.Pp);
            var best = enemies.OrderByDescending(Counter).First();
            var worst = enemies.OrderBy(Counter).First();
            Assume(Counter(best) > Counter(worst));
            Assert.True(UnitAutonomy.Score(sim.Context, mine, best, d) > UnitAutonomy.Score(sim.Context, mine, worst, d));
            // 거리가 가까울수록 점수가 높다
            Assert.True(UnitAutonomy.Score(sim.Context, mine, worst, Fix.FromInt(1)) > UnitAutonomy.Score(sim.Context, mine, worst, Fix.FromInt(9)));
        }

        private static void Assume(bool ok) => Assert.True(ok, "step01 적에 상성이 다른 병종이 있어야 한다");

        [Fact(DisplayName = "빈 곳 메우기: 선봉 칸 부대가 없으면 중군 근접 부대가 선봉 자리로")]
        public void CenterFillsVanguard()
        {
            var setup = Step01();
            var corps = setup.Corps.First(c => c.Team == StoryFlow.PlayerTeam && c.Units.Any(u => u.Slot == "vanguard") && c.Units.Any(u => u.Slot == "center"));
            var sim1 = new BattleSimulation(setup);
            var c1 = sim1.Context.FindCorps(corps.Id);
            Assert.Equal("center", UnitAutonomy.SlotOf(c1, c1.Units.First(u => u.Slot == "center")));
            corps.Units.RemoveAll(u => u.Slot == "vanguard");
            var sim2 = new BattleSimulation(setup);
            var c2 = sim2.Context.FindCorps(corps.Id);
            Assert.Equal("vanguard", UnitAutonomy.SlotOf(c2, c2.Units.First(u => u.Slot == "center")));
        }

        [Fact(DisplayName = "스위치를 끄면 결과가 달라진다 (자율 판단이 실제로 전투에 들어간다)")]
        public void AffectsBattle()
        {
            Assert.NotEqual(BattleSimulation.Run(Step01()).StateHash, BattleSimulation.Run(Step01(false)).StateHash);
            Assert.Equal(Fix.FromInt(80), Kit.D.TroopTypes["cavalry"].Stats.Get("pursuit"));
        }
    }
}
