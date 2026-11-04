using System.Collections.Generic;
using Game.Battle.Model.Damage;
using Game.Battle.Model.Morale;
using Game.Battle.Model.Skills;
using Game.Campaign.Troops;
using Game.Economy;
using Game.Online;
using Game.Foundation.Math;
using Xunit;
using static Game.Tests.TestData;

namespace Game.Tests
{
    /// <summary>
    /// docs/design/verification.md 의 수치 예시(N). 예시 수치(Fixtures/balance.example.json)로 고정해서 돌린다.
    /// </summary>
    public class VerificationNumericTests
    {
        private static DamageCalculator Damage() => new DamageCalculator(Example(), Flags(Real()));

        [Fact(DisplayName = "N-1 피해: 100, +20 +15 −10 → 125")]
        public void N1()
        {
            var r = Damage().Calculate(F(100), new[]
            {
                new DamageModifier("troopCounter", F(20)), new DamageModifier("flank", F(15)), new DamageModifier("terrain", F(-10)),
            });
            Assert.Equal(F(25), r.TotalPp);
            Assert.Equal(F(125), r.Final);
        }

        [Fact(DisplayName = "N-2 피해 상한: 합계 +125 → +100으로 잘려 200")]
        public void N2()
        {
            var r = Damage().Calculate(F(100), new[]
            {
                new DamageModifier("a", F(40)), new DamageModifier("b", F(35)), new DamageModifier("c", F(30)), new DamageModifier("d", F(20)),
            });
            Assert.Equal(F(125), r.TotalPp);
            Assert.Equal(F(100), r.ClampedPp);
            Assert.Equal(F(200), r.Final);
        }

        [Fact(DisplayName = "N-3 피해 하한: 합계 −80 → −60으로 잘려 40")]
        public void N3()
        {
            var r = Damage().Calculate(F(100), new[] { new DamageModifier("a", F(-80)) });
            Assert.Equal(F(-60), r.ClampedPp);
            Assert.Equal(F(40), r.Final);
        }

        [Theory(DisplayName = "N-4 사기 보정")]
        [InlineData(100, 80, 40, 20, 10)]
        [InlineData(75, 80, 40, 10, 5)]
        [InlineData(50, 80, 40, 0, 0)]
        [InlineData(25, 80, 40, -15, -15)]
        [InlineData(0, 80, 40, -30, -30)]
        public void N4(int morale, int might, int intellect, int atk, int def)
        {
            var (a, d) = MoraleModifier.Calculate(Example(), F(morale), F(might), F(intellect));
            Assert.Equal(F(atk), a);
            Assert.Equal(F(def), d);
        }

        [Theory(DisplayName = "N-5 병력 한도")]
        [InlineData(1, 5000)]
        [InlineData(50, 12424)]
        [InlineData(100, 20000)]
        public void N5(int leadership, long cap) => Assert.Equal(cap, TroopCapCalculator.Cap(Example(), F(leadership)));

        [Fact(DisplayName = "N-7 군량: 보병 10,000 × 3분 = 210,000 / 보병 8,000 + 철기(3) 2,000 × 1분 = 98,000")]
        public void N7()
        {
            var b = Example();
            long daily1 = SupplyCalculator.DailyConsumption(new[] { (10000L, 1L) });
            Assert.Equal(210000, SupplyCalculator.BattleConsumption(b, daily1, F(3)));
            long daily2 = SupplyCalculator.DailyConsumption(new[] { (8000L, 1L), (2000L, 3L) });
            Assert.Equal(98000, SupplyCalculator.BattleConsumption(b, daily2, F(1)));
        }

        [Fact(DisplayName = "N-8 퇴각 군량: 9,000 → 3,000, 남은 90,000 → 가져감 30,000 / 몰수 60,000")]
        public void N8()
        {
            var (keep, conf) = SupplyCalculator.RetreatSplit(90000, 3000, 9000, false);
            Assert.Equal(30000, keep);
            Assert.Equal(60000, conf);
        }

        [Fact(DisplayName = "N-9 손실 1,000 → 부상 400 / 사망 600, 400명 치료 = 군량 2,000")]
        public void N9()
        {
            var b = Example();
            var (wounded, dead) = CasualtySplitter.Split(b, 1000, true);
            Assert.Equal(400, wounded);
            Assert.Equal(600, dead);
            Assert.Equal(2000, HealCostCalculator.HealCostInSupply(b, 400));
        }

        [Fact(DisplayName = "N-10 지략 80: 화계(40,1회) 후 40점·0회 → 다시 못 씀, 간파(15) 두 번 → 10점")]
        public void N10()
        {
            var pool = new SkillPointPool(F(80), new Dictionary<string, int> { ["fireAttack"] = 1, ["reveal"] = 2 });
            Assert.True(pool.TryConsume("fireAttack", F(40), true));
            Assert.Equal(F(40), pool.Points);
            Assert.False(pool.CanUse("fireAttack", F(40), true));
            Assert.True(pool.TryConsume("reveal", F(15), true));
            Assert.True(pool.TryConsume("reveal", F(15), true));
            Assert.Equal(F(10), pool.Points);
        }

        [Fact(DisplayName = "N-11 고용: 등록 8,000, 군량 84,000(1명 3분 = 21), 한도 남은 5,000 → 4,000명")]
        public void N11() => Assert.Equal(4000, HireCalculator.MaxHireable(Example(), 8000, 84000, 1, 5000));

        [Fact(DisplayName = "N-12 고용 비용: 1명당 2 × 4,000 = 8,000")]
        public void N12() => Assert.Equal(8000, HireCalculator.UnitHireCost(2, 4000));
    }
}
