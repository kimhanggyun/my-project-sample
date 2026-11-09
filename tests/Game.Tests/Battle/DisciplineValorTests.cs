using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>규율·용맹 (battle.md 2.6, R-56). 규율 = 통솔 0.8 + 지략 0.2, 용맹 = 무력 0.8 + 통솔 0.2 (combat.json, 예시).</summary>
    public class DisciplineValorTests
    {
        private static Fix F(int v) => Fix.FromInt(v);

        [Fact(DisplayName = "R-56 규율·용맹 계산과 진형 전환 시간·손실 사기 배율 (50이 기준)")]
        public void Formulas()
        {
            var c = Defs.Combat;
            Assert.Equal(F(74), c.Discipline(F(80), F(50)));
            Assert.Equal(F(50), c.Discipline(F(50), F(50)));
            Assert.Equal(F(74), c.Valor(F(80), F(50)));
            // 전환 시간 = 3초 × (1 − (규율 − 50) × 0.01), 1~6초
            Assert.Equal(F(3), c.FormationChangeSecondsFor(F(50)));
            Assert.Equal(Fix.FromRatio(228, 100), c.FormationChangeSecondsFor(F(74)));
            Assert.Equal(Fix.FromRatio(39, 10), c.FormationChangeSecondsFor(F(20)));
            Assert.Equal(Fix.FromRatio(15, 10), c.FormationChangeSecondsFor(F(100)));
            // 손실 사기 하락 배율 = 1 − (용맹 − 50) × 0.005, 0.6~1.4
            Assert.Equal(Fix.OneValue, c.ValorMoraleLossMultiplier(F(50)));
            Assert.Equal(Fix.FromRatio(88, 100), c.ValorMoraleLossMultiplier(F(74)));
            Assert.Equal(Fix.FromRatio(115, 100), c.ValorMoraleLossMultiplier(F(20)));
            Assert.Equal(Fix.FromRatio(75, 100), c.ValorMoraleLossMultiplier(F(100)));
        }

        private static long ChangeTicks(int leadership, int intellect, string flags = null)
        {
            var a = Corps("A", "teamA", "square", general: General("ga", leadership: leadership, intellect: intellect), controller: Controllers.Player);
            var sim = new BattleSimulation(Setup("test_plain", 1, flags, a, Corps("B", "teamB", "square")));
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.ChangeFormation, FormationId = "column" }));
            var corps = sim.Context.FindCorps("A");
            long n = 0;
            do { sim.Step(); n++; } while (corps.Reforming);
            Assert.Equal("column", corps.Formation.Id);
            return n;
        }

        [Fact(DisplayName = "R-56 규율이 높을수록 진형이 빨리 바뀐다 (통솔 80·지략 50 → 2.28초 = 23틱, 통솔 20·지략 20 → 3.9초 = 39틱), 스위치를 끄면 바로")]
        public void FormationChangeTime()
        {
            Assert.Equal(23, ChangeTicks(80, 50));
            Assert.Equal(30, ChangeTicks(50, 50));
            Assert.Equal(39, ChangeTicks(20, 20));
            Assert.Equal(1, ChangeTicks(20, 20, "{\"discipline\": false}"));
        }

        [Fact(DisplayName = "R-56 전환 중에 원래 진형을 고르면 전환을 그만둔다, 바꾸는 중인 진형을 또 고를 수는 없다")]
        public void CancelChange()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square", controller: Controllers.Player), Corps("B", "teamB", "square")));
            var a = sim.Context.FindCorps("A");
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.ChangeFormation, FormationId = "column" }));
            sim.Step();
            Assert.True(a.Reforming);
            Assert.False(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.ChangeFormation, FormationId = "column" }));
            Assert.True(sim.Issue(new BattleCommand { CorpsId = "A", Kind = CommandKinds.ChangeFormation, FormationId = "square" }));
            sim.Step();
            Assert.False(a.Reforming);
            Assert.Equal("square", a.Formation.Id);
        }

        /// <summary>같은 편성끼리 싸워 A의 사기 (A 장수 무력·통솔만 다르게, 공격·방어 보정은 0으로).</summary>
        private static Fix MoraleAfter(int might, string flags)
        {
            var combat = Defs.ResolveCombat(Newtonsoft.Json.Linq.JObject.Parse("{\"general\": {\"mightToAttack\": 0, \"leadershipToDefense\": 0}}"));
            var setup = Setup("test_plain", 7, flags, Corps("A", "teamA", "square", general: General("ga", might: might)), Corps("B", "teamB", "square"));
            setup.Combat = combat;
            var sim = new BattleSimulation(setup);
            var a = sim.Context.FindCorps("A");
            for (int i = 0; i < 400 && !sim.IsOver; i++) sim.Step();
            return a.Morale;
        }

        [Fact(DisplayName = "R-56 용맹이 높은 장수의 군단은 병력 손실에 사기가 덜 떨어진다 (스위치 valor를 끄면 같다)")]
        public void ValorHoldsMorale()
        {
            Assert.True(MoraleAfter(90, null) > MoraleAfter(50, null));
            Assert.True(MoraleAfter(50, null) > MoraleAfter(10, null));
            Assert.Equal(MoraleAfter(90, "{\"valor\": false}"), MoraleAfter(50, "{\"valor\": false}"));
        }
    }
}
