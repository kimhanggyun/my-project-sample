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
    /// <summary>R-77 수비 방어·휴식 기습·지휘 범위.</summary>
    public class HoldRestCommandTests
    {
        private static BattleSetup Step01(string offFlag = null)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(plan.Ok);
            if (offFlag != null) plan.Setup.Flags = Kit.D.ResolveFlags(Newtonsoft.Json.Linq.JObject.Parse("{\"" + offFlag + "\": false}"));
            return plan.Setup;
        }

        [Fact(DisplayName = "지휘 범위: 장수(군단 중심) 가까이는 1, 범위 밖은 줄어 minFactor까지. 통솔이 높으면 범위가 넓다")]
        public void CommandRangeFactor()
        {
            var sim = new BattleSimulation(Step01());
            var c = sim.Context.Corps.First(x => x.Team == StoryFlow.PlayerTeam);
            Assert.Equal(Kit.D.Combat.CommandRangeBaseCells * c.Setup.General.Leadership / Fix.FromInt(50), CommandRange.Range(sim.Context, c));
            foreach (var u in c.Units)
            {
                var f = CommandRange.Factor(sim.Context, c, u);
                Assert.True(f <= Fix.OneValue && f >= Kit.D.Combat.CommandRangeMinFactor);
            }
            var off = new BattleSimulation(Step01("commandRange"));
            var c2 = off.Context.Corps.First(x => x.Team == StoryFlow.PlayerTeam);
            Assert.All(c2.Units, u => Assert.Equal(Fix.OneValue, CommandRange.Factor(off.Context, c2, u)));
        }

        [Fact(DisplayName = "수비 방어·휴식 기습·지휘 범위 스위치를 끄면 결과가 달라진다 (실제로 전투에 들어간다)")]
        public void AffectsBattle()
        {
            var on = BattleSimulation.Run(Step01()).StateHash;
            Assert.NotEqual(on, BattleSimulation.Run(Step01("commandRange")).StateHash);
            Assert.Equal(Fix.FromInt(10), Kit.D.Combat.HoldDefensePp);
            Assert.Equal(Fix.FromRatio(1, 2), Kit.D.Combat.RestSurpriseConfusionChance);
        }
    }
}
