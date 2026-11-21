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
                var g = b.Armies[CrowdBattle.Enemy].General;
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

        /// <summary>앞 시간 동안 적이 잃은 병사.</summary>
        private static int EnemyLosses(string ally, string enemy, ulong seed, int ticks)
        {
            var b = Run(new CrowdSetup { AllyFormation = ally, EnemyFormation = enemy, Seed = seed }, ticks);
            return b.Armies[CrowdBattle.Enemy].StartCount - b.Armies[CrowdBattle.Enemy].AliveCount;
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
