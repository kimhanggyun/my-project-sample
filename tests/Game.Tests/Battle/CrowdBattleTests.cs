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
        public void WaypointsAreFollowedInOrder()
        {
            // 명령 예약 (R-129): 이동 중에 더한 지점들을 차례로 지나 마지막에서 제자리
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyAdvance = false, Seed = 2 });
            var a = b.Commander(CrowdBattle.Ally);
            var p1 = new Game.Battle.Model.Geometry.Vec(Fix.FromInt(20), Fix.FromInt(20));
            var p2 = new Game.Battle.Model.Geometry.Vec(Fix.FromInt(40), Fix.FromInt(20));
            b.OrderMove(a.Id, p1);
            b.OrderMove(a.Id, p2, append: true);
            Assert.Single(a.Waypoints);
            bool passedP1 = false;
            for (int t = 0; t < 600 && a.Order != CrowdOrders.Hold; t++)
            {
                b.Step();
                if ((a.Center - p1).Length <= Fix.OneValue) passedP1 = true;
            }
            Assert.True(passedP1, "did not pass p1");
            Assert.Equal(CrowdOrders.Hold, a.Order);
            Assert.True((a.Center - p2).Length <= Fix.OneValue, "center " + a.Center);
            b.OrderMove(a.Id, p1);
            b.OrderHold(a.Id);
            Assert.Empty(a.Waypoints);
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

        // ---- 병종 · 상성 (R-127) ----

        private static readonly Game.Data.Defs.DefinitionSet Defs = TestData.Real();

        private static CrowdBattle RunTroops(string ally, string enemy, bool allyAdvance, bool enemyAdvance, ulong seed, int ticks)
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup
            {
                AllyTroops = new[] { ally }, EnemyTroops = new[] { enemy }, AllyAdvance = allyAdvance, EnemyAdvance = enemyAdvance,
                AllyFormation = CrowdFormations.Line, EnemyFormation = CrowdFormations.Thick, Seed = seed,
            }, Defs.TroopTypes, Defs.TroopCounters);
            for (int t = 0; t < ticks && !b.IsOver; t++) b.Step();
            return b;
        }

        [Fact]
        public void CavalryCrushesArchersButBouncesOffSpears()
        {
            // 기병 돌격: 궁병은 쓸어버리고, 창병에게는 받아치기를 당해 더 많이 잃는다 (창 > 기병 > 궁)
            int archersLost = 0, spearsLost = 0, cavLostVsArchers = 0, cavLostVsSpears = 0;
            for (ulong seed = 1; seed <= 2; seed++)
            {
                var a = RunTroops("cavalry", "archer", true, false, seed, 600);
                archersLost += 200 - a.AliveCount(CrowdBattle.Enemy);
                cavLostVsArchers += 200 - a.AliveCount(CrowdBattle.Ally);
                var s = RunTroops("cavalry", "spearman", true, false, seed, 600);
                spearsLost += 200 - s.AliveCount(CrowdBattle.Enemy);
                cavLostVsSpears += 200 - s.AliveCount(CrowdBattle.Ally);
            }
            _out.WriteLine($"cav→archer: archers lost {archersLost}, cav lost {cavLostVsArchers}; cav→spear: spears lost {spearsLost}, cav lost {cavLostVsSpears}");
            Assert.True(archersLost > spearsLost, $"archers {archersLost} spears {spearsLost}");
            Assert.True(cavLostVsSpears > cavLostVsArchers, $"vs spears {cavLostVsSpears} vs archers {cavLostVsArchers}");
        }

        [Fact]
        public void ArchersHitBeforeMeleeContact()
        {
            // 궁병(제자리)은 다가오는 보병이 닿기 전에 화살로 깎는다
            var b = new CrowdBattle(Rules(), new CrowdSetup
            {
                AllyTroops = new[] { "archer" }, EnemyTroops = new[] { "infantry" }, AllyAdvance = false, EnemyAdvance = true, Seed = 3,
            }, Defs.TroopTypes, Defs.TroopCounters);
            int lostBeforeContact = -1, hpBeforeContact = -1, arrows = 0;
            for (int t = 0; t < 1200 && !b.IsOver; t++)
            {
                b.Step();
                arrows = System.Math.Max(arrows, b.Arrows.Count);
                bool melee = false;
                foreach (var s in b.Soldiers)
                    if (s.Team == CrowdBattle.Enemy && s.LastAttackTick == b.Tick) { melee = true; break; }
                if (melee) { lostBeforeContact = 200 - b.AliveCount(CrowdBattle.Enemy); hpBeforeContact = 200 * 10 - EnemyHp(b); _out.WriteLine("contact tick " + b.Tick + " arrow hits " + b.ArrowHits); break; }
            }
            _out.WriteLine($"enemy lost before contact {lostBeforeContact} soldiers, {hpBeforeContact} hp, most arrows in air {arrows}");
            Assert.True(arrows > 0);
            // 화살은 앞줄 여럿에게 고루 박혀 (상처) 다가오는 동안 체력을 10% 넘게 깎는다 — 맞붙으면 다친 병사가 먼저 쓰러진다
            Assert.True(hpBeforeContact >= 200, "hp lost before contact " + hpBeforeContact);
        }

        private static int EnemyHp(CrowdBattle b) { int h = 0; foreach (var s in b.Soldiers) if (s.Team == CrowdBattle.Enemy && !s.IsGeneral) h += s.Hp; return h; }

        [Fact]
        public void CounterDamageFollowsData()
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup(), Defs.TroopTypes, Defs.TroopCounters);
            var spear = CrowdTroop.Of("spearman", Defs.TroopTypes);
            var cav = CrowdTroop.Of("cavalry", Defs.TroopTypes);
            var archer = CrowdTroop.Of("archer", Defs.TroopTypes);
            Assert.True(b.HitDamage(spear, cav) > b.HitDamage(spear, archer)); // 창 → 기병 +25%p
            Assert.True(b.HitDamage(cav, archer) > b.HitDamage(cav, spear));   // 기병 → 궁 +20%p, → 창 −15%p
            Assert.Equal("cavalry", cav.BaseId);
            Assert.True(cav.Mounted && spear.Polearm && archer.Ranged);
        }

        // ---- 사기 · 퇴각 (R-128) ----

        /// <summary>적 부대가 처음 무너진 틱 (안 무너지면 큰 값) · 그때까지 적이 잃은 병사.</summary>
        private (int tick, int lost, int fled) FirstEnemyRout(string ally, ulong seed)
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyFormation = ally, EnemyFormation = CrowdFormations.Thick, Seed = seed });
            for (int t = 0; t < 2400 && !b.IsOver; t++)
            {
                b.Step();
                if (b.Commander(CrowdBattle.Enemy).Routed)
                {
                    int fled = 0;
                    for (int k = 0; k < 400 && !b.IsOver; k++) b.Step();
                    foreach (var s in b.Soldiers) if (s.FledTick >= 0) fled++;
                    return (b.Commander(CrowdBattle.Enemy).RoutedTick, 200 - b.AliveCount(CrowdBattle.Enemy), fled);
                }
            }
            _out.WriteLine($"  no enemy rout: tick {b.Tick} winner {b.Winner} {b.EndReason}, morale ally {b.Commander(0).Morale} enemy {b.Commander(1).Morale}, ally routed {b.Commander(0).Routed}");
            return (99999, 200 - b.AliveCount(CrowdBattle.Enemy), 0);
        }

        [Fact]
        public void EncircledBlockBreaksSooner()
        {
            // V자로 감싸면 (옆 · 뒤에서 맞음 → 포위 사기 깎임) 일자로 정면에서 미는 것보다 빨리 무너진다
            int vee = 0, line = 0;
            for (ulong seed = 1; seed <= 2; seed++)
            {
                var v = FirstEnemyRout(CrowdFormations.Vee, seed);
                var l = FirstEnemyRout(CrowdFormations.Line, seed);
                _out.WriteLine($"seed {seed}: vee rout at {v.tick} (lost {v.lost}, fled {v.fled}), line rout at {l.tick} (lost {l.lost}, fled {l.fled})");
                vee += v.tick; line += l.tick;
            }
            Assert.True(vee < line, $"vee {vee} line {line}");
        }

        [Fact]
        public void RoutedArmyRunsToItsOwnEdge()
        {
            // 무너진 적은 싸우지 않고 자기 쪽 가장자리(위 = 큰 y)로 도망친다 — 끝나기 전 routEndSeconds 동안 보인다
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyFormation = CrowdFormations.Vee, EnemyFormation = CrowdFormations.Thick, Seed = 1 });
            var e = b.Commander(CrowdBattle.Enemy);
            for (int t = 0; t < 2400 && !b.IsOver && !e.Routed; t++) b.Step();
            Assert.True(e.Routed, "never routed");
            Fix y0 = e.Centroid().Y;
            for (int t = 0; t < 30 && !b.IsOver; t++) b.Step();
            Fix y1 = e.Centroid().Y;
            _out.WriteLine($"routed at {e.RoutedTick}, centroid y {y0} → {y1}, over {b.IsOver} {b.EndReason}");
            Assert.True(y1 - y0 >= Fix.FromInt(2), $"moved {y1 - y0}");
        }

        [Fact]
        public void FlankAndRearHitsAreStronger()
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup());
            var a = b.Armies[0]; // 앞 = +y
            var at = a.Center;
            Assert.Equal(0, b.SideOf(a, at, at + new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.OneValue)));
            Assert.Equal(3, b.SideOf(a, at, at - new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.OneValue)));
            Assert.NotEqual(0, b.SideOf(a, at, at + new Game.Battle.Model.Geometry.Vec(Fix.OneValue, Fix.Zero)));
            var inf = new CrowdTroop();
            Assert.True(b.HitDamage(inf, inf, b.Rules.RearPp) > b.HitDamage(inf, inf));
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
