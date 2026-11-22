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

        // ---- 3×3 배치 (R-131) ----

        [Fact]
        public void GridPlacesUnitsByRowAndColumn()
        {
            var setup = new CrowdSetup
            {
                AllyUnits = new System.Collections.Generic.List<CrowdUnitSpec>
                {
                    new CrowdUnitSpec { Row = 0, Col = 0, Troop = "cavalry", Count = 40 },
                    new CrowdUnitSpec { Row = 1, Col = 1, Troop = "infantry", Commander = true },
                    new CrowdUnitSpec { Row = 2, Col = 2, Troop = "archer", Count = 999 },
                },
                EnemyUnits = new System.Collections.Generic.List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Troop = "spearman" } },
                AllyAdvance = false,
            };
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var allies = new System.Collections.Generic.List<CrowdArmy>(b.TeamArmies(CrowdBattle.Ally));
            Assert.Equal(3, allies.Count);
            var cav = allies.Find(a => a.Place == "front_left");
            var inf = allies.Find(a => a.Place == "middle_center");
            var arc = allies.Find(a => a.Place == "back_right");
            Assert.Equal(40, cav.StartCount);
            Assert.Equal(b.Rules.GridMaxCount, arc.StartCount); // 최대로 잘림
            Assert.Equal(b.Rules.GridDefaultCount, inf.StartCount);
            Assert.True(inf.IsCommander && !cav.IsCommander && !arc.IsCommander);
            Assert.True(cav.Center.X < inf.Center.X && inf.Center.X < arc.Center.X); // 왼 → 오
            Assert.True(cav.Center.Y > inf.Center.Y && inf.Center.Y > arc.Center.Y); // 앞줄이 적 쪽 (위)
            var enemy = b.Commander(CrowdBattle.Enemy);
            Assert.Equal("front_center", enemy.Place);
            Assert.True(enemy.IsCommander); // 총대장 표시가 없으면 첫 부대
            for (int t = 0; t < 50; t++) b.Step();
        }

        // ---- 지형 (R-133) ----

        private static CrowdTerrain Terrain(string mapId) =>
            new CrowdTerrain(Game.Data.Defs.MapDef.Load(new FileDataSource(TestData.DataRoot), mapId), Defs.TerrainRules, Defs.TerrainCombat, Rules().MapCellSize);

        private static CrowdSetup One(string troop, string mapId) => new CrowdSetup
        {
            AllyUnits = new System.Collections.Generic.List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 1, Col = 1, Troop = troop, Commander = true } },
            EnemyUnits = new System.Collections.Generic.List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 1, Col = 1, Troop = "infantry", Commander = true } },
            AllyAdvance = false, Seed = 3, Terrain = mapId == null ? null : Terrain(mapId),
        };

        [Fact]
        public void MapSetsFieldSizeAndDeploysAtSpawns()
        {
            var b = new CrowdBattle(Rules(), One("infantry", "river_ford"), Defs.TroopTypes, Defs.TroopCounters);
            Assert.Equal(Fix.FromInt(60), b.Rules.FieldWidth); // 24칸 × 2.5
            Assert.Equal(Fix.FromInt(100), b.Rules.FieldDepth); // 40칸 × 2.5
            var me = b.Commander(CrowdBattle.Ally).Center;
            var foe = b.Commander(CrowdBattle.Enemy).Center;
            Assert.True(me.Y < Fix.FromInt(30) && foe.Y > Fix.FromInt(70), $"ally {me} enemy {foe}"); // 강(가운데)을 사이에 두고
            Assert.Equal("river", b.Terrain.TerrainAt(new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(50))));
        }

        [Fact]
        public void TerrainCombatAndCostComeFromData()
        {
            var forest = Terrain("test_forest");
            var p = new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(50));
            var inf = CrowdTroop.Of("infantry", Defs.TroopTypes);
            var cav = CrowdTroop.Of("cavalry", Defs.TroopTypes);
            Assert.Equal(Fix.FromInt(10), forest.CombatPp(inf, p)); // 숲 보병 +10
            Assert.True(forest.CombatPp(cav, p) < Fix.Zero); // 숲 기병 −
            Assert.True(forest.SpeedFactor(cav, p) < forest.SpeedFactor(inf, p)); // 숲에서 기병이 더 느림
            Assert.True(Terrain("test_plain").CombatPp(cav, new Game.Battle.Model.Geometry.Vec(Fix.FromInt(2), Fix.FromInt(2))) >= Fix.Zero);
        }

        [Fact]
        public void ForestSlowsCavalryMoreThanPlain()
        {
            Fix Gone(string map)
            {
                var b = new CrowdBattle(Rules(), One("cavalry", map), Defs.TroopTypes, Defs.TroopCounters);
                var a = b.Commander(CrowdBattle.Ally);
                var y0 = a.Center.Y;
                b.OrderMove(a.Id, a.Center + new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.FromInt(-8)));
                for (int t = 0; t < 40; t++) b.Step();
                return y0 - a.Center.Y;
            }
            Fix plain = Gone(null), forest = Gone("test_forest");
            _out.WriteLine($"cavalry 40 ticks: plain {plain} forest {forest}");
            Assert.True(forest * 2 < plain, $"plain {plain} forest {forest}");
        }

        [Fact]
        public void RiverIsCrossedOnlyAtFordOrBridge()
        {
            var b = new CrowdBattle(Rules(), One("infantry", "river_ford"), Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            var goal = new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(66));
            b.OrderMove(a.Id, goal);
            int wet = 0, t = 0;
            for (; t < 1500 && a.Order == CrowdOrders.Move; t++)
            {
                b.Step();
                foreach (var s in a.Soldiers) if (s.Alive && b.Terrain.Blocks(a.Troop, s.Pos)) wet++;
            }
            int across = 0;
            foreach (var s in a.Soldiers) if (s.Alive && s.Pos.Y > Fix.FromInt(56)) across++;
            _out.WriteLine($"ticks {t}, center {a.Center}, across {across}/{a.Soldiers.Count}, wet {wet}");
            Assert.Equal(0, wet); // 강에 선 병사 없음
            Assert.True(a.Center.Y > Fix.FromInt(60), "center " + a.Center);
            Assert.True(across * 10 >= a.Soldiers.Count * 8, $"across {across}"); // 대부분 건넘
        }

        [Fact]
        public void PathGoesThroughFordOrBridge()
        {
            var tr = Terrain("river_ford");
            var inf = CrowdTroop.Of("infantry", Defs.TroopTypes);
            var path = tr.FindPath(inf, new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(30)), new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(70)));
            Assert.NotEmpty(path);
            bool crossed = false;
            foreach (var p in path)
            {
                Assert.False(tr.Blocks(inf, p));
                var id = tr.TerrainAt(p);
                if (id == "ford" || id == "bridge") crossed = true;
            }
            Assert.True(crossed);
        }

        [Fact]
        public void SameInputSameResultWithTerrain()
        {
            var x = new CrowdBattle(Rules(), One("cavalry", "hills_valley"), Defs.TroopTypes, Defs.TroopCounters);
            var y = new CrowdBattle(Rules(), One("cavalry", "hills_valley"), Defs.TroopTypes, Defs.TroopCounters);
            x.OrderAttackNearest(x.Commander(CrowdBattle.Ally).Id);
            y.OrderAttackNearest(y.Commander(CrowdBattle.Ally).Id);
            for (int t = 0; t < 300; t++) { x.Step(); y.Step(); }
            Assert.Equal(x.Fingerprint(), y.Fingerprint());
        }

        // ---- 조작 명령 (R-132): 진군 · 후퇴 · 근접 공격 · 위임 · 경로 ----

        [Fact]
        public void AdvanceGoesStraightAheadAndFights()
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyAdvance = false, Seed = 5 }, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            var startX = a.Center.X;
            var startY = a.Center.Y;
            b.OrderAdvance(a.Id);
            Assert.Equal(CrowdOrders.Advance, a.Order);
            bool fought = false;
            for (int t = 0; t < 300 && !b.IsOver; t++) { b.Step(); foreach (var s in a.Soldiers) if (s.LastAttackTick >= 0) fought = true; }
            Assert.True(a.Center.Y > startY + Fix.FromInt(5));
            Assert.Equal(startX, a.Center.X); // 곧게
            Assert.True(fought);
        }

        [Fact]
        public void RetreatGoesBackShowingTheBack()
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyAdvance = false, Seed = 5 }, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            var startY = a.Center.Y;
            b.OrderRetreat(a.Id);
            b.Step();
            Assert.True(a.Facing.Y > Fix.Zero); // 대열은 뒤집지 않는다
            Assert.Equal(3, b.SideOf(a, a.Center, a.Center + new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.FromInt(5)))); // 쫓는 적(앞)에게 맞아도 뒤
            for (int t = 0; t < 400 && a.Order == CrowdOrders.Retreat; t++) b.Step();
            Assert.Equal(CrowdOrders.Hold, a.Order);
            Assert.True(a.Center.Y < startY);
            Assert.True(a.Center.Y <= b.Rules.RetreatMargin + a.HalfDepth + Fix.OneValue);
            Assert.Equal(0, b.SideOf(a, a.Center, a.Center + new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.FromInt(5)))); // 다 물러나면 다시 앞
        }

        [Fact]
        public void AttackNearestSendsArchersIntoMelee()
        {
            var setup = new CrowdSetup { AllyCorps = 3, EnemyCorps = 3, AllyTroops = new[] { "infantry", "archer", "infantry" }, AllyAdvance = false, Seed = 4 };
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var archers = b.Commander(CrowdBattle.Ally);
            Assert.True(archers.Troop.Ranged);
            Assert.True(b.OrderAttackNearest(archers.Id));
            Assert.True(archers.Melee);
            Assert.Equal(b.NearestEnemyArmy(archers.Id), archers.OrderTarget);
            Assert.Equal("center", b.Armies[archers.OrderTarget].Place); // 마주 보는 적
            bool struck = false;
            for (int t = 0; t < 400 && !b.IsOver && !struck; t++)
            {
                b.Step();
                foreach (var s in archers.Soldiers) if (!s.IsGeneral && s.LastAttackTick == b.Tick && b.Soldiers[s.LastTargetId].Pos.Y - s.Pos.Y < Fix.FromInt(3)) struck = true;
            }
            Assert.True(struck); // 활 대신 붙어서 친다
            Assert.Empty(b.Arrows.FindAll(ar => b.Soldiers[ar.ShooterId].Army == archers.Id));
            b.OrderHold(archers.Id);
            Assert.False(archers.Melee);
        }

        [Fact]
        public void DelegatedAllyFightsByItself()
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyAdvance = false, Seed = 2 }, Defs.TroopTypes, Defs.TroopCounters);
            b.UseSkillData(Defs.Skills);
            var a = b.Commander(CrowdBattle.Ally);
            for (int t = 0; t < 20; t++) b.Step();
            Assert.Equal(CrowdOrders.Hold, a.Order); // 위임 전에는 제자리
            b.SetDelegated(a.Id, true);
            for (int t = 0; t < 20; t++) b.Step();
            Assert.Equal(CrowdOrders.Attack, a.Order);
            Assert.Equal(b.Commander(CrowdBattle.Enemy).Id, a.OrderTarget);
        }

        [Fact]
        public void PathFollowsPointsThenAttacks()
        {
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyAdvance = false, Seed = 2 }, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            var foe = b.Commander(CrowdBattle.Enemy);
            var pts = new System.Collections.Generic.List<Game.Battle.Model.Geometry.Vec>();
            for (int k = 0; k < 20; k++) pts.Add(new Game.Battle.Model.Geometry.Vec(Fix.FromInt(10 + k), Fix.FromInt(20 + k / 2)));
            b.OrderPath(a.Id, pts, foe.Id);
            Assert.Equal(CrowdOrders.Move, a.Order);
            Assert.Equal(CrowdBattle.MaxPathPoints - 1, a.Waypoints.Count); // 상한
            Assert.Equal(foe.Id, a.PathThenAttack);
            for (int t = 0; t < 600 && a.Order == CrowdOrders.Move; t++) b.Step();
            Assert.Equal(CrowdOrders.Attack, a.Order); // 길 끝에서 공격
            Assert.Equal(foe.Id, a.OrderTarget);
        }

        // ---- 스킬 (R-130) ----

        private static CrowdBattle WithSkills(CrowdSetup setup)
        {
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            b.UseSkillData(Defs.Skills);
            return b;
        }

        [Fact]
        public void SkillsHaveUsesAndGap()
        {
            var b = WithSkills(new CrowdSetup { AllyTroops = new[] { "infantry" }, AllyAdvance = false });
            var a = b.Commander(CrowdBattle.Ally);
            Assert.False(a.SkillUses.ContainsKey(CrowdBattle.ArrowRain)); // 보병은 화살비 없음
            int uses = a.SkillUses[CrowdBattle.Rally];
            Assert.True(uses >= 1);
            Assert.True(b.UseSkill(a.Id, CrowdBattle.Rally));
            Assert.False(b.UseSkill(a.Id, CrowdBattle.WarCry)); // 다시 쓰기 간격
            for (int t = 0; t < 40; t++) b.Step();
            Assert.True(b.UseSkill(a.Id, CrowdBattle.WarCry));
            Assert.Equal(uses - 1, a.SkillUses[CrowdBattle.Rally]);
        }

        [Fact]
        public void WarCryHitsHarderAndHoldTheLineTakesLess()
        {
            var b = WithSkills(new CrowdSetup());
            var me = b.Commander(CrowdBattle.Ally);
            var foe = b.Commander(CrowdBattle.Enemy);
            int baseDmg = b.HitDamage(me.Troop, foe.Troop);
            b.UseSkill(me.Id, CrowdBattle.WarCry);
            b.UseSkill(foe.Id, CrowdBattle.HoldTheLine);
            // 함성 +20 · 철벽 −20 → 같음, 함성만이면 더 큼
            var b2 = WithSkills(new CrowdSetup());
            b2.UseSkill(b2.Commander(CrowdBattle.Ally).Id, CrowdBattle.WarCry);
            Assert.True(b2.Commander(CrowdBattle.Ally).AttackBuffUntil > 0);
            Assert.True(b.Commander(CrowdBattle.Enemy).FortifyUntil > 0);
            Assert.Equal(Fix.FromInt(20), b2.Commander(CrowdBattle.Ally).AttackBuffPp); // skills.json warCry pp
            Assert.True(b2.HitDamage(me.Troop, foe.Troop, Fix.FromInt(60)) > baseDmg); // %p가 피해에 들어감
        }

        [Fact]
        public void RallyRaisesMoraleAndArrowRainLandsAfterTelegraph()
        {
            var b = WithSkills(new CrowdSetup { AllyTroops = new[] { "archer" }, AllyAdvance = false, EnemyFormation = CrowdFormations.Thick });
            var a = b.Commander(CrowdBattle.Ally);
            a.Morale = Fix.FromInt(30);
            Assert.True(b.UseSkill(a.Id, CrowdBattle.Rally));
            Assert.True(a.Morale > Fix.FromInt(30));
            for (int t = 0; t < 40; t++) b.Step();
            var target = b.Commander(CrowdBattle.Enemy).Centroid();
            // 사거리 밖은 못 쓴다
            Assert.False(b.UseSkill(a.Id, CrowdBattle.ArrowRain, target));
            a.Center = target - new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.FromInt(10));
            foreach (var s in a.Soldiers) s.Pos = a.SlotWorld(s.Slot);
            int hpBefore = EnemyHp(b);
            Assert.True(b.UseSkill(a.Id, CrowdBattle.ArrowRain, target));
            b.Step();
            Assert.Equal(hpBefore, EnemyHp(b)); // 전조 동안은 아직
            for (int t = 0; t < 30; t++) b.Step();
            _out.WriteLine($"enemy hp {hpBefore} → {EnemyHp(b)}");
            Assert.True(EnemyHp(b) < hpBefore - 20);
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
