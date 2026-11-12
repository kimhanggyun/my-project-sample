using System.Linq;
using Game.Battle.Model.Combat;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Foundation.Math;
using Game.Tests.Campaign;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>R-72 진형 특성치 (formations.md 2장): formations.json stats의 보정이 전투에 들어간다.</summary>
    public class FormationStatTests
    {
        private static BattleSetup Step01(string formation, bool flagOn = true)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(plan.Ok);
            foreach (var c in plan.Setup.Corps.Where(c => c.Team == StoryFlow.PlayerTeam)) c.Formation = formation;
            if (!flagOn) plan.Setup.Flags = Kit.D.ResolveFlags(Newtonsoft.Json.Linq.JObject.Parse("{\"formationStats\": false}"));
            return plan.Setup;
        }

        [Fact(DisplayName = "모든 진형에 특성치가 있고, 방진(기준)은 응집력만 있다")]
        public void AllFormationsHaveStats()
        {
            foreach (var f in Kit.D.Formations.Values) Assert.NotNull(f.Stats);
            var square = Kit.D.Formations["square"].Stats;
            Assert.Equal((Fix.Zero, Fix.Zero, Fix.FromInt(-10)), (square.BreakthroughPp, square.MoveSpeedPct, square.MoraleLossPct));
            Assert.Equal(Fix.FromInt(20), Kit.D.Formations["circle"].Stats.RearDefensePp);
            Assert.Equal(Fix.FromInt(-40), Kit.D.Formations["loose"].Stats.AreaDamageTakenPct);
        }

        [Fact(DisplayName = "이동·범위 피해·손실 사기 배율, 스위치를 끄면 1")]
        public void Multipliers()
        {
            var sim = new BattleSimulation(Step01("column"));
            var c = sim.Context.Corps.First(x => x.Team == StoryFlow.PlayerTeam);
            Assert.Equal(Fix.FromRatio(125, 100), FormationStatEffects.MoveMultiplier(sim.Context, c));
            var loose = new BattleSimulation(Step01("loose"));
            var l = loose.Context.Corps.First(x => x.Team == StoryFlow.PlayerTeam);
            Assert.Equal(Fix.FromRatio(60, 100), FormationStatEffects.AreaDamageMultiplier(loose.Context, l));
            Assert.Equal(Fix.FromRatio(115, 100), FormationStatEffects.MoraleLossMultiplier(loose.Context, l));
            var off = new BattleSimulation(Step01("loose", false));
            Assert.Equal(Fix.OneValue, FormationStatEffects.AreaDamageMultiplier(off.Context, off.Context.Corps.First(x => x.Team == StoryFlow.PlayerTeam)));
        }

        [Fact(DisplayName = "스위치를 끄면 결과가 달라진다 (특성치가 실제로 전투에 들어간다)")]
        public void AffectsBattle()
        {
            Assert.NotEqual(BattleSimulation.Run(Step01("arrowhead")).StateHash, BattleSimulation.Run(Step01("arrowhead", false)).StateHash);
        }
    }
}
