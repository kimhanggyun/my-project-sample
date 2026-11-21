using System;
using Game.Battle.Model.Crowd;
using Game.Data.Json;
using Game.Foundation.Math;
using Xunit;
using Xunit.Abstractions;

namespace Game.Tests.Battle
{
    /// <summary>샘플전투2 병사 단위 전투 (R-125): 결정성 · 겹치지 않음 · 진형 상성.</summary>
    public class CrowdBattleTests
    {
        private readonly ITestOutputHelper _out;
        public CrowdBattleTests(ITestOutputHelper output) { _out = output; }

        private static CrowdRules Rules() => CrowdRules.Load(new FileDataSource(TestData.DataRoot));

        private static CrowdBattle Run(CrowdSetup setup, int maxTicks)
        {
            var b = new CrowdBattle(Rules(), setup);
            for (int i = 0; i < maxTicks && !b.IsOver; i++) b.Step();
            return b;
        }

        [Fact]
        public void SameInputSameResult()
        {
            var setup = new CrowdSetup { AllyFormation = CrowdFormations.Wedge, EnemyFormation = CrowdFormations.Line, Seed = 7 };
            var a = Run(setup, 400);
            var b = Run(setup, 400);
            Assert.Equal(a.Fingerprint(), b.Fingerprint());
            Assert.Equal(a.Tick, b.Tick);
        }

        [Fact]
        public void SoldiersDoNotPileUp()
        {
            var setup = new CrowdSetup { AllyFormation = CrowdFormations.Thick, EnemyFormation = CrowdFormations.Thick, EnemyAdvance = true, Seed = 3 };
            var b = new CrowdBattle(Rules(), setup);
            Fix minAllowed = b.Rules.SoldierRadius; // 지름의 절반보다 가까이 겹친 병사가 없어야 (밀어내기)
            Fix worst = Fix.FromInt(100);
            for (int t = 0; t < 300 && !b.IsOver; t++)
            {
                b.Step();
                if (t < 100) continue;
                for (int i = 0; i < b.Soldiers.Count; i++)
                {
                    var s = b.Soldiers[i];
                    if (!s.Alive) continue;
                    for (int j = i + 1; j < b.Soldiers.Count; j++)
                    {
                        var o = b.Soldiers[j];
                        if (!o.Alive) continue;
                        var d = (s.Pos - o.Pos).Length;
                        if (d < worst) worst = d;
                    }
                }
            }
            _out.WriteLine("closest = " + worst);
            Assert.True(worst >= minAllowed, "closest " + worst);
        }

        /// <summary>적 장수가 처음 맞은 틱 (안 맞았으면 큰 값).</summary>
        private static int TicksToReachGeneral(string ally, string enemy, ulong seed)
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyFormation = ally, EnemyFormation = enemy, Seed = seed });
            for (int t = 0; t < 2400 && !b.IsOver; t++)
            {
                b.Step();
                var g = b.Commander(CrowdBattle.Enemy).General;
                if (g.Hp < g.MaxHp) return b.Tick;
            }
            return 99999;
        }

        [Fact]
        public void WedgePiercesThinLineFasterThanThickLine()
        {
            int thin = 0, thick = 0;
            for (ulong seed = 1; seed <= 3; seed++)
            {
                thin += TicksToReachGeneral(CrowdFormations.Wedge, CrowdFormations.Line, seed);
                thick += TicksToReachGeneral(CrowdFormations.Wedge, CrowdFormations.Thick, seed);
            }
            _out.WriteLine($"wedge→line {thin / 3} ticks, wedge→thick {thick / 3} ticks");
            Assert.True(thin < thick, $"thin {thin} thick {thick}");
        }

        [Fact]
        public void ChargeSpeedsUpAndImpactPushes()
        {
            // 공격 명령: 가까워지면 돌격 (빨라짐), 달려와 처음 친 한 방은 부딪침 (밀침 · 피해 더)
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyFormation = CrowdFormations.Thick, EnemyFormation = CrowdFormations.Line, Seed = 4 });
            var ally = b.Commander(CrowdBattle.Ally);
            bool charged = false;
            int impacts = 0;
            for (int t = 0; t < 600 && !b.IsOver; t++)
            {
                b.Step();
                charged |= ally.Charging;
                impacts += b.ImpactsThisTick;
            }
            _out.WriteLine($"charged {charged}, impacts {impacts}");
            Assert.True(charged);
            Assert.True(impacts > 10, "impacts " + impacts);
        }

        [Fact]
        public void MoveOrderGoesToPointAndHolds()
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false, Seed = 2 });
            var left = b.Armies[0];
            Assert.Equal("left", left.Place);
            var goal = new Game.Battle.Model.Geometry.Vec(Fix.FromInt(8), Fix.FromInt(26));
            b.OrderMove(left.Id, goal);
            for (int t = 0; t < 300; t++) b.Step();
            Assert.Equal(CrowdOrders.Hold, left.Order);
            Assert.True((left.Center - goal).Length <= Fix.OneValue, "center " + left.Center);
            Assert.True((left.Centroid() - goal).Length <= Fix.FromInt(2), "soldiers " + left.Centroid());
        }

        [Fact]
        public void ThreeCorpsDeterministicAndCommanderDecides()
        {
            var setup = new CrowdSetup { AllyCorps = 3, EnemyCorps = 3, AllyFormation = CrowdFormations.Wedge, EnemyFormation = CrowdFormations.Thick, EnemyAdvance = true, Seed = 9 };
            var a = Run(setup, 3000);
            var b = Run(setup, 3000);
            Assert.Equal(a.Fingerprint(), b.Fingerprint());
            Assert.True(a.IsOver);
            _out.WriteLine($"winner {a.Winner} ({a.EndReason}) at {a.Tick}, ally {a.AliveCount(0)} enemy {a.AliveCount(1)}");
            if (a.EndReason == "general")
                Assert.False(a.Commander(a.Winner == CrowdBattle.Ally ? CrowdBattle.Enemy : CrowdBattle.Ally).General.Alive);
        }

        /// <summary>앞 시간 동안 적이 잃은 병사.</summary>
        private static int EnemyLosses(string ally, string enemy, ulong seed, int ticks)
        {
            var b = Run(new CrowdSetup { AllyFormation = ally, EnemyFormation = enemy, Seed = seed }, ticks);
            return 200 - b.AliveCount(CrowdBattle.Enemy);
        }

        [Fact]
        public void VeeHurtsThickBlockMoreThanLine()
        {
            int vee = 0, line = 0;
            for (ulong seed = 1; seed <= 3; seed++)
            {
                vee += EnemyLosses(CrowdFormations.Vee, CrowdFormations.Thick, seed, 500);
                line += EnemyLosses(CrowdFormations.Line, CrowdFormations.Thick, seed, 500);
            }
            _out.WriteLine($"vee→thick losses {vee}, line→thick losses {line}");
            Assert.True(vee > line, $"vee {vee} line {line}");
        }
    }
}
