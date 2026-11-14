using System.Linq;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Data.Json;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-104 패배 팁 (20 시안) · 성장 게이지용 경험치 전후.</summary>
    public class DefeatAdviceTests
    {
        private sealed class Memory : IDataSource
        {
            private readonly string _text;
            public Memory(string text) { _text = text; }
            public bool Exists(string path) => path == DefeatAdvice.File;
            public string ReadText(string path) => _text;
        }

        /// <summary>step01을 모든 아군 퇴각으로 진다 (총대장 군단도 물러남).</summary>
        private static (SortiePlan plan, BattleResult result) Lose()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(Kit.Veteran(), new SortieRequest { StepId = "step01", Seed = 2 });
            Assert.True(plan.Ok);
            var sim = new BattleSimulation(plan.Setup);
            foreach (var c in plan.Setup.Corps.Where(c => c.Team == StoryFlow.PlayerTeam))
                Assert.True(sim.Issue(new BattleCommand { CorpsId = c.Id, Kind = CommandKinds.Retreat }));
            while (!sim.IsOver) sim.Step();
            Assert.NotEqual(StoryFlow.PlayerTeam, sim.Result.Winner);
            return (plan, sim.Result);
        }

        [Fact(DisplayName = "R-104 데이터: defeatTips.json 차례 · 수치 · 바로가기, 이기면 팁 없음")]
        public void Data()
        {
            var a = DefeatAdvice.Load(Kit.Source);
            Assert.Equal(new[] { "supply", "commander", "troops", "losses", "time" }, a.Order);
            Assert.Equal(Game.Foundation.Math.Fix.FromRatio(4, 5), a.TroopsRatio);
            Assert.Equal(new[] { "supply", "shop" }, a.Shortcuts["supply"]);
            var (plan, result) = Lose();
            Assert.Null(a.Pick(plan, result, true));
        }

        [Fact(DisplayName = "R-104 고르기: 모두 물러나 지면 (총대장 군단 퇴각) commander + 편성 · 장수 바로가기, 차례를 바꾸면 그 차례의 첫 이유, 맞는 게 없으면 other")]
        public void Pick()
        {
            var (plan, result) = Lose();
            var tip = DefeatAdvice.Load(Kit.Source).Pick(plan, result, false);
            Assert.Equal(DefeatAdvice.Commander, tip.Id);
            Assert.Equal(new[] { "formation", "general" }, tip.Shortcuts);

            var troopsFirst = DefeatAdvice.Load(new Memory("{\"order\": [\"troops\", \"commander\"], \"troopsRatio\": 1000}"));
            Assert.Equal(DefeatAdvice.Troops, troopsFirst.Pick(plan, result, false).Id);
            var none = DefeatAdvice.Load(new Memory("{\"order\": [\"time\"]}"));
            var other = none.Pick(plan, result, false);
            Assert.Equal(DefeatAdvice.Other, other.Id);
            Assert.Equal(new[] { "shared", "formation" }, other.Shortcuts);
        }

        [Fact(DisplayName = "성장 게이지: 이긴 전투의 경험치는 장수마다 전 레벨 · 전 경험치 · 받은 양 · 후 레벨 · 후 경험치로 보고에 남는다")]
        public void ExpGains()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = Kit.Veteran();
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 3, Delegate = true });
            Assert.True(plan.Ok);
            var before = plan.PlayerCorps.Values.ToDictionary(c => c.GeneralId, c => (state.FindGeneral(c.GeneralId).Level, state.FindGeneral(c.GeneralId).Exp));
            Assert.True(flow.Confirm(state, plan));
            var report = flow.ApplyResult(state, plan, BattleSimulation.Run(plan.Setup));
            if (!report.Won) return; // 기준 전투가 지면 경험치가 없다 (다른 테스트가 승패를 본다)
            Assert.NotEmpty(report.ExpGains);
            foreach (var kv in report.ExpGains)
            {
                Assert.Equal(before[kv.Key].Level, kv.Value.levelBefore);
                Assert.Equal(before[kv.Key].Exp, kv.Value.expBefore);
                Assert.True(kv.Value.gained > 0);
                var g = state.FindGeneral(kv.Key);
                Assert.Equal(g.Level, kv.Value.levelAfter);
                Assert.Equal(g.Exp, kv.Value.expAfter);
            }
        }
    }
}
