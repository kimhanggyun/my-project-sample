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
    /// <summary>R-73 전열과 후열 (battle.md 2.4.1): 근접 소부대는 앞줄 병력만큼 치고, 잃으면 뒷줄이 채운다.</summary>
    public class FrontLineTests
    {
        private static BattleSetup Step01(bool on = true)
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(plan.Ok);
            foreach (var c in plan.Setup.Corps) foreach (var u in c.Units) u.Troops = 5000; // 앞줄 한도보다 많게
            if (!on) plan.Setup.Flags = Kit.D.ResolveFlags(Newtonsoft.Json.Linq.JObject.Parse("{\"frontLine\": false}"));
            return plan.Setup;
        }

        [Fact(DisplayName = "앞줄 한도 = troopsPerUnit × (100 + 정면 폭 %)/100: 방진 4000, 횡진 6000, 장사 2400")]
        public void Cap()
        {
            var sim = new BattleSimulation(Step01());
            var c = sim.Context.Corps.First();
            Assert.Equal(Fix.FromInt(4000), Kit.D.Combat.FrontLineTroopsPerUnit);
            Assert.Equal(Fix.FromInt(50), Kit.D.Formations["line"].Stats.FrontWidthPct);
            Assert.Equal(Fix.FromInt(-40), Kit.D.Formations["column"].Stats.FrontWidthPct);
            var expected = Fix.FromInt(4000) * (Fix.Hundred + c.Formation.Stats.FrontWidthPct) / Fix.Hundred;
            Assert.Equal(expected, FrontLine.Cap(sim.Context, c));
        }

        [Fact(DisplayName = "싸우는 근접 소부대의 앞줄 병력은 한도를 넘지 않고, 손실 뒤 뒷줄이 다시 채운다. 스위치를 끄면 결과가 달라진다")]
        public void FrontFillsAndMatters()
        {
            var sim = new BattleSimulation(Step01());
            bool sawThin = false;
            for (int i = 0; i < 3000 && !sim.IsOver; i++)
            {
                sim.Step();
                foreach (var c in sim.Context.Corps)
                    foreach (var u in c.Units.Where(x => x.Alive && x.FrontTroops >= Fix.Zero))
                    {
                        Assert.True(u.FrontTroops <= FrontLine.Cap(sim.Context, c) && u.FrontTroops <= Fix.FromInt(u.Troops));
                        if (u.FrontTroops < Fix.Min(Fix.FromInt(u.Troops), FrontLine.Cap(sim.Context, c))) sawThin = true;
                    }
            }
            Assert.True(sawThin, "손실로 앞줄이 얇아지는 순간이 있어야 한다");
            Assert.NotEqual(BattleSimulation.Run(Step01()).StateHash, BattleSimulation.Run(Step01(false)).StateHash);
        }
    }
}
