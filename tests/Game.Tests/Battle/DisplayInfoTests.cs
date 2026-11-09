using System.Linq;
using Game.Battle.Model.Simulation;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>화면 연출용 정보 (규칙 계산에 쓰지 않는다).</summary>
    public class DisplayInfoTests
    {
        [Fact(DisplayName = "공격 대상 표시: 이번 틱에 공격한 소부대만 대상(적 소부대)을 가진다")]
        public void AttackTarget()
        {
            var sim = new BattleSimulation(Setup("test_plain", 1, null, Corps("A", "teamA", "square"), Corps("B", "teamB", "square")));
            for (int i = 0; i < 3000 && !sim.Context.Corps.Any(c => c.Units.Any(u => u.AttackTarget != null)) && !sim.IsOver; i++) sim.Step();
            var attackers = sim.Context.Corps.SelectMany(c => c.Units.Where(u => u.AttackTarget != null).Select(u => (c, u))).ToList();
            Assert.NotEmpty(attackers);
            foreach (var (c, u) in attackers)
                Assert.Contains(sim.Context.Corps, e => e.Team != c.Team && e.Units.Contains(u.AttackTarget));
        }
    }
}
