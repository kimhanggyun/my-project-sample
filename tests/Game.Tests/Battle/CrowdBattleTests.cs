using System;
using Game.Battle.Model.Crowd;
using Game.Battle.Model.Geometry;
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

        private static CrowdTerrain Terrain(string mapId)
        {
            var map = Game.Data.Defs.MapDef.Load(new FileDataSource(TestData.DataRoot), mapId);
            return new CrowdTerrain(map, Defs.TerrainRules, Defs.TerrainCombat, CrowdTerrain.CellSizeFor(map, Rules().MapFieldWidth));
        }

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

        // ---- 샘플전투2 맵 (R-135) ----

        [Theory]
        [InlineData("crowd_plain")]
        [InlineData("crowd_highland")]
        [InlineData("crowd_gorge")]
        [InlineData("crowd_river")]
        [InlineData("crowd_marsh")]
        [InlineData("crowd_forest")]
        [InlineData("crowd_pass")]
        [InlineData("crowd_siege")]
        [InlineData("crowd_siege_plain")]
        [InlineData("crowd_siege_river")]
        [InlineData("crowd_siege_mountain")]
        public void CrowdMapsAreValidAndBothSidesCanReachEachOther(string id)
        {
            var map = Game.Data.Defs.MapDef.Load(new FileDataSource(TestData.DataRoot), id);
            Assert.Empty(Game.Data.Validation.DataValidator.ValidateMap(Defs, map));
            Assert.Equal(48, map.Width);
            Assert.Equal(80, map.Height);
            Assert.True(map.HasHeights);
            var tr = Terrain(id);
            Assert.Equal(Fix.FromInt(60), tr.Width);
            Assert.Equal(Fix.FromInt(100), tr.Depth);
            // 우리 시작 자리 → 적 시작 자리: 보병 · 기병 모두 길이 있어야 (막힌 맵이면 싸울 수 없음)
            var b = new CrowdBattle(Rules(), One("infantry", id), Defs.TroopTypes, Defs.TroopCounters);
            var from = b.Commander(CrowdBattle.Ally).Center;
            var to = b.Commander(CrowdBattle.Enemy).Center;
            foreach (var troop in new[] { "infantry", "cavalry", "archer" })
            {
                var t = CrowdTroop.Of(troop, Defs.TroopTypes);
                Assert.False(tr.Blocks(t, from), $"{id} {troop} start blocked");
                Assert.False(tr.Blocks(t, to), $"{id} {troop} enemy start blocked");
                Assert.NotEmpty(tr.FindPath(t, from, to));
            }
        }

        [Fact]
        public void HeightLayerGivesGradualSlope()
        {
            var tr = Terrain("crowd_highland");
            Fix low = tr.LevelAt(new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(10)));
            Fix mid = tr.LevelAt(new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(50)));
            Fix top = tr.LevelAt(new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(90)));
            _out.WriteLine($"highland levels: ally side {low}, middle {mid}, enemy side {top}");
            Assert.True(low < mid && mid < top); // 우리 쪽에서 적 쪽으로 계속 오른다
            Assert.Equal(Fix.FromInt(3), top); // 꼭대기 = 9 ÷ 3
        }

        // ---- 진형 11개 (R-141) ----

        [Fact]
        public void EveryFormationPlacesEveryoneWithoutOverlap()
        {
            foreach (var id in CrowdFormations.All)
                foreach (int n in new[] { 1, 7, 60, 150 })
                {
                    var slots = CrowdFormations.Slots(id, n, Fix.FromRatio(45, 100));
                    Assert.Equal(n, slots.Count);
                    Fix sx = Fix.Zero, sy = Fix.Zero;
                    foreach (var s in slots) { sx += s.X; sy += s.Y; }
                    Assert.True(Fix.Max(sx / n, -(sx / n)) < Fix.FromRatio(1, 100) && Fix.Max(sy / n, -(sy / n)) < Fix.FromRatio(1, 100), $"{id} {n} centered");
                    for (int i = 0; i < slots.Count; i++)
                        for (int j = i + 1; j < slots.Count; j++)
                            Assert.True((slots[i] - slots[j]).Length >= Fix.FromRatio(30, 100), $"{id} {n} overlap {i} {j}");
                }
        }

        [Fact]
        public void FormationShapesMatchTheirIcons()
        {
            Fix sp = Fix.OneValue;
            Fix Width(System.Collections.Generic.List<Game.Battle.Model.Geometry.Vec> s) { Fix a = Fix.FromInt(999), b = Fix.FromInt(-999); foreach (var p in s) { if (p.X < a) a = p.X; if (p.X > b) b = p.X; } return b - a; }
            Fix Depth(System.Collections.Generic.List<Game.Battle.Model.Geometry.Vec> s) { Fix a = Fix.FromInt(999), b = Fix.FromInt(-999); foreach (var p in s) { if (p.Y < a) a = p.Y; if (p.Y > b) b = p.Y; } return b - a; }
            var single = CrowdFormations.Slots(CrowdFormations.Single, 60, sp);
            var line = CrowdFormations.Slots(CrowdFormations.Line, 60, sp);
            var column = CrowdFormations.Slots(CrowdFormations.Column, 60, sp);
            Assert.Equal(Fix.Zero, Depth(single)); // 1줄
            Assert.True(Width(single) > Width(line)); // 가장 넓음
            Assert.True(Depth(column) > Width(column) * 3); // 종대 = 좁고 김 (폭 4명)
            Assert.True(Width(CrowdFormations.Slots(CrowdFormations.Loose, 60, sp)) > Width(CrowdFormations.Slots(CrowdFormations.Thick, 60, sp))); // 산개 = 간격 넓음
            // 좌 사선: 오른쪽이 앞 (왼쪽을 물려 뺌), 우 사선은 반대
            Fix Slope(string f) { Fix s = Fix.Zero; foreach (var p in CrowdFormations.Slots(f, 60, sp)) s += p.X * p.Y; return s; }
            Assert.True(Slope(CrowdFormations.EchelonL) > Fix.Zero);
            Assert.True(Slope(CrowdFormations.EchelonR) < Fix.Zero);
            // 분진: 가운데가 빔
            foreach (var p in CrowdFormations.Slots(CrowdFormations.Split, 60, sp)) Assert.True(Fix.Max(p.X, -p.X) > Fix.OneValue, "split gap");
            // 원진: 사람이 적으면 가운데가 빈 둘레
            var ring = CrowdFormations.Slots(CrowdFormations.Circle, 20, sp);
            foreach (var p in ring) Assert.True(p.Length > Fix.OneValue, "circle hollow");
        }

        [Fact]
        public void LooseTakesFewerArrows()
        {
            int Hits(string formation)
            {
                var setup = One("archer", "crowd_plain");
                setup.EnemyUnits[0].Formation = formation;
                setup.EnemyUnits[0].Count = 100;
                var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
                var a = b.Commander(CrowdBattle.Ally);
                b.OrderAttack(a.Id, b.Commander(CrowdBattle.Enemy).Id);
                for (int t = 0; t < 900 && !b.IsOver; t++) b.Step();
                return b.ArrowHits;
            }
            int line = Hits(CrowdFormations.Line), loose = Hits(CrowdFormations.Loose);
            _out.WriteLine($"arrow hits: line {line}, loose {loose}");
            Assert.True(loose < line, $"loose {loose} line {line}");
        }

        // ---- 높이 (R-143) ----

        /// <summary>맵에서 땅 높이가 가장 높은 칸 · 가장 낮은 칸 가운데 (못 가는 칸 빼고).</summary>
        private static (Vec high, Vec low) HighLow(CrowdBattle b)
        {
            var t = b.Terrain;
            var troop = b.Commander(CrowdBattle.Ally).Troop;
            Vec high = Vec.Zero, low = Vec.Zero;
            Fix hi = Fix.FromInt(-1), lo = Fix.FromInt(99);
            for (int r = 0; r < t.Map.Height; r++)
                for (int c = 0; c < t.Map.Width; c++)
                {
                    if (t.Blocks(troop, c, r)) continue;
                    var p = t.CellCenter(c, r);
                    var l = t.LevelAt(p);
                    if (l > hi) { hi = l; high = p; }
                    if (l < lo) { lo = l; low = p; }
                }
            return (high, low);
        }

        [Fact]
        public void HigherGroundHitsHarderAndShootsFarther()
        {
            var b = new CrowdBattle(Rules(), One("archer", "crowd_highland"), Defs.TroopTypes, Defs.TroopCounters);
            var (high, low) = HighLow(b);
            Assert.True(b.Terrain.LevelAt(high) - b.Terrain.LevelAt(low) >= Fix.FromInt(2));
            Assert.Equal(b.Rules.HeightMaxLevels, b.HeightDiff(high, low)); // ±heightMaxLevels까지
            Assert.Equal(-b.Rules.HeightMaxLevels, b.HeightDiff(low, high));
            // 근접: 내려치면 %p 더, 올려치면 덜
            var foe = CrowdTroop.Of("infantry", Defs.TroopTypes);
            Fix up = b.HeightDiff(high, low) * b.Rules.HeightMeleePpPerLevel;
            Assert.True(b.HitDamage(foe, foe, up) > b.HitDamage(foe, foe, -up));
            // 궁병: 위에서 더 멀리, 아래서는 그대로
            var me = new CrowdSoldier { Pos = high };
            var them = new CrowdSoldier { Pos = low };
            Fix baseRange = b.RangeOf(b.Commander(CrowdBattle.Ally).Troop);
            Assert.True(b.ShotRange(me, them, baseRange) > baseRange);
            Assert.Equal(baseRange, b.ShotRange(them, me, baseRange));
        }

        [Fact]
        public void WallTopStandsAboveTheGround()
        {
            var b = new CrowdBattle(Rules(), One("archer", "crowd_siege"), Defs.TroopTypes, Defs.TroopCounters);
            var t = b.Terrain;
            Vec top = Vec.Zero, ground = Vec.Zero;
            bool found = false;
            for (int r = 0; r < t.Map.Height && !found; r++)
                for (int c = 0; c + 1 < t.Map.Width && !found; c++)
                    if (t.Map.TerrainAt(c, r) == "wallTop") { top = t.CellCenter(c, r); found = true; }
            Assert.True(found);
            // 성 앞 들판(높이 0)보다 성벽 위 궁병이 2단 높다 (맵 heights: 성벽 위 6 = 2단)
            ground = b.Commander(CrowdBattle.Ally).Center;
            Assert.Equal(Fix.Zero, t.LevelAt(ground));
            Assert.Equal(Fix.FromInt(2), b.HeightDiff(top, ground));
            var me = new CrowdSoldier { Pos = top };
            var them = new CrowdSoldier { Pos = ground };
            Fix baseRange = b.RangeOf(b.Commander(CrowdBattle.Ally).Troop);
            Assert.True(b.ShotRange(me, them, baseRange) > baseRange); // 성벽 위에서 더 멀리
        }

        [Fact]
        public void ClimbingIsSlowerThanDescending()
        {
            var b = new CrowdBattle(Rules(), One("infantry", "crowd_highland"), Defs.TroopTypes, Defs.TroopCounters);
            var (high, low) = HighLow(b);
            var t = b.Terrain;
            // 비탈 한 곳: 낮은 곳에서 높은 곳 쪽으로 걸으며 땅이 처음 오르는 자리
            var dir = (high - low).Normalized();
            Vec at = low;
            Fix step = t.CellSize / 4;
            for (int k = 0; k < 2000; k++)
            {
                if (t.LevelAt(at + dir * t.CellSize) > t.LevelAt(at)) break;
                at = at + dir * step;
            }
            Fix up = b.SlopePct(at, dir), down = b.SlopePct(at + dir * t.CellSize, dir * -Fix.OneValue);
            _out.WriteLine($"slope at {at.X},{at.Y}: up {up}% down {down}%");
            Assert.True(up < Fix.FromInt(100));
            Assert.True(up >= b.Rules.SlopeMinPct);
            Assert.True(down > Fix.FromInt(100) && down <= b.Rules.SlopeMaxPct);
            // 평지는 그대로
            var flat = new CrowdBattle(Rules(), One("infantry", "crowd_plain"), Defs.TroopTypes, Defs.TroopCounters);
            Assert.Equal(Fix.FromInt(100), flat.SlopePct(flat.Commander(CrowdBattle.Ally).Center, new Vec(Fix.Zero, Fix.OneValue)));
        }

        [Fact]
        public void ArmyClimbsHillSlowerThanItWalksPlain()
        {
            int Ticks(string map)
            {
                var b = new CrowdBattle(Rules(), One("infantry", map), Defs.TroopTypes, Defs.TroopCounters);
                var a = b.Commander(CrowdBattle.Ally);
                var goal = new Vec(a.Center.X, a.Center.Y + Fix.FromInt(30));
                b.OrderMove(a.Id, goal);
                int t = 0;
                for (; t < 3000 && a.Order == CrowdOrders.Move; t++) b.Step();
                return t;
            }
            int plain = Ticks("crowd_plain"), hill = Ticks("crowd_highland");
            _out.WriteLine($"30 cells forward: plain {plain} ticks, highland (uphill) {hill} ticks");
            Assert.True(hill > plain, $"plain {plain} hill {hill}");
        }

        // ---- 공성 (R-140) ----

        [Fact]
        public void WallTopOnlyFromStairs()
        {
            // R-142 (2026-11-22 사용자 영상 "진형 맞춘다고 성벽 위로 올라감"): 성벽 위 ↔ 땅은 바로 못 오감, 계단으로만
            var setup = One("infantry", "crowd_siege");
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var t = b.Terrain;
            int w = t.Map.Width, h = t.Map.Height, top = -1, ground = -1, stairs = -1;
            for (int r = 0; r < h && (top < 0 || stairs < 0); r++)
                for (int c = 0; c < w; c++)
                {
                    if (t.Map.TerrainAt(c, r) == "stairs" && stairs < 0) stairs = r * w + c;
                    if (t.Map.TerrainAt(c, r) != "wallTop" || top >= 0) continue;
                    foreach (var (dc, dr) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                    {
                        int nc = c + dc, nr = r + dr;
                        if (nc < 0 || nr < 0 || nc >= w || nr >= h) continue;
                        string nt = t.Map.TerrainAt(nc, nr);
                        if (nt != "wallTop" && nt != "stairs" && nt != "wall" && !t.Blocks(b.Commander(0).Troop, nc, nr)) { top = r * w + c; ground = nr * w + nc; break; }
                    }
                }
            Assert.True(top >= 0 && ground >= 0 && stairs >= 0, "map has wallTop next to ground and stairs");
            Assert.False(t.Links(ground, top));
            Assert.False(t.Links(top, ground));
            var troop = b.Commander(CrowdBattle.Ally).Troop;
            Vec G = t.CellCenter(ground % w, ground / w), Top = t.CellCenter(top % w, top / w);
            Assert.False(t.Clear(troop, G, Top));
            // 땅에 선 병사에게 성벽 위 자리는 가까운 땅으로
            Assert.NotEqual("wallTop", t.TerrainAt(t.SnapLinked(troop, G, Top)));
            // 길 찾기는 계단을 지난다
            var path = t.FindPath(troop, G, Top);
            Assert.NotEmpty(path);
            bool viaStairs = false;
            foreach (var p in path) if (t.TerrainAt(p) == "stairs") viaStairs = true;
            Assert.True(viaStairs, "path climbs by stairs");
        }

        /// <summary>공성 평지 맵 공격 쪽 (캡처 투어 45와 같은 배치).</summary>
        private static CrowdBattle SiegeAttack(string map) => new CrowdBattle(Rules(), new CrowdSetup
        {
            AllyUnits = new System.Collections.Generic.List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 120, Commander = true, General = "warrior" }, new CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer" } },
            EnemyUnits = new System.Collections.Generic.List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Troop = "spearman", Commander = true }, new CrowdUnitSpec { Row = 2, Col = 0, Troop = "archer" } },
            AllyAdvance = true, Seed = 19, Terrain = Terrain(map),
        }, Defs.TroopTypes, Defs.TroopCounters);

        // ---- 공성 명령 (R-145) ----

        /// <summary>공성 시험 배치: 우리(공격) · 적(수비) 부대 병종을 고르고, 적 AI는 끔 (명령은 시험이 직접).</summary>
        private static CrowdBattle Siege(string map, string[] ally, string[] enemy, bool enemyAi = false)
        {
            var au = new System.Collections.Generic.List<CrowdUnitSpec>();
            for (int k = 0; k < ally.Length; k++) au.Add(new CrowdUnitSpec { Row = k / 3, Col = k % 3, Troop = ally[k], Count = 90, Commander = k == 0 });
            var eu = new System.Collections.Generic.List<CrowdUnitSpec>();
            for (int k = 0; k < enemy.Length; k++) eu.Add(new CrowdUnitSpec { Row = k / 3, Col = k % 3, Troop = enemy[k], Count = 60, Commander = k == 0 });
            return new CrowdBattle(Rules(), new CrowdSetup { AllyUnits = au, EnemyUnits = eu, AllyAdvance = false, EnemyAdvance = enemyAi, Seed = 7, Terrain = Terrain(map) },
                Defs.TroopTypes, Defs.TroopCounters);
        }

        [Fact]
        public void SiegeOrdersDependOnSideAndTroop()
        {
            var b = Siege("crowd_siege", new[] { "infantry", "archer", "cavalry" }, new[] { "spearman", "archer", "cavalry" });
            Assert.True(b.SiegeActive);
            Assert.Equal(CrowdBattle.Enemy, b.SiegeDefender);
            string Tasks(int id) => string.Join(",", b.SiegeTasksFor(id));
            Assert.Equal("bash,ladder,ram", Tasks(0));
            Assert.Equal("shootWall", Tasks(1));
            Assert.Equal("waitBreach", Tasks(2));
            Assert.Equal("brace,wall", Tasks(3));
            Assert.Equal("wall", Tasks(4));
            Assert.Equal("brace", Tasks(5));
            b.OrderSiege(0, SiegeTasks.Wall); // 고를 수 없는 명령은 무시
            Assert.Null(b.Armies[0].SiegeTask);
        }

        [Fact]
        public void ArchersManTheWallsByStairs()
        {
            var b = Siege("crowd_siege", new[] { "infantry" }, new[] { "spearman", "archer" });
            var archers = b.Armies[2];
            b.OrderSiege(archers.Id, SiegeTasks.Wall);
            Assert.Equal(SiegeTasks.Wall, archers.SiegeTask);
            int on = 0;
            for (int t = 0; t < 600; t++) b.Step();
            foreach (var s in archers.Soldiers) if (s.Alive && !s.IsGeneral && b.Terrain.TerrainAt(s.Pos) == "wallTop") on++;
            _out.WriteLine($"archers on the wall: {on}/{archers.AliveCount}");
            Assert.True(on * 2 > archers.AliveCount, $"{on} on wall");
            b.OrderHold(archers.Id); // 다른 명령 = 공성 명령 지움
            Assert.Null(archers.SiegeTask);
        }

        [Fact]
        public void BracedGateLastsLongerAndRamBreaksFaster()
        {
            int Break(bool brace, string task)
            {
                var b = Siege("crowd_siege", new[] { "infantry" }, new[] { "spearman", "cavalry" });
                if (brace) { b.OrderSiege(1, SiegeTasks.Brace); b.OrderSiege(2, SiegeTasks.Brace); }
                b.OrderSiege(0, task);
                for (int t = 0; t < 6000 && b.SiegeActive; t++) b.Step();
                _out.WriteLine($"brace {brace} {task}: breach tick {b.BreachTick}, brace% {b.GateBracePct(b.Gates[0])}");
                return b.BreachTick < 0 ? 99999 : b.BreachTick;
            }
            int open = Break(false, SiegeTasks.Bash), braced = Break(true, SiegeTasks.Bash), ram = Break(true, SiegeTasks.Ram);
            Assert.True(braced > open, $"braced {braced} open {open}");
            Assert.True(ram < braced, $"ram {ram} braced {braced}");
        }

        [Fact]
        public void LadderClimbersGetInOrAreThrownDown()
        {
            int Climbed(bool guards)
            {
                var b = Siege("crowd_siege", new[] { "spearman" }, guards ? new[] { "infantry", "spearman" } : new[] { "infantry" });
                if (guards) b.OrderSiege(2, SiegeTasks.Wall);
                b.OrderSiege(0, SiegeTasks.Ladder);
                Assert.Equal(SiegeTasks.Ladder, b.Armies[0].SiegeTask);
                for (int t = 0; t < 1500 && b.SiegeActive; t++) b.Step();
                var l = b.LadderOf(b.Armies[0]);
                Assert.NotNull(l);
                int inside = 0;
                foreach (var s in b.Armies[0].Soldiers) if (s.Alive && s.Pos.Y > b.Gates[0].Center.Y) inside++;
                _out.WriteLine($"guards {guards}: climbed {l.Climbed}, thrown {l.Thrown}, inside now {inside}, breach {b.BreachTick}");
                if (guards) Assert.True(l.Thrown > 0, "guards threw climbers down");
                return l.Climbed;
            }
            int free = Climbed(false), guarded = Climbed(true);
            Assert.True(free > 0, "climbed");
            Assert.True(guarded < free, $"guarded {guarded} free {free}");
        }

        [Fact]
        public void WallDefendersDropStones()
        {
            var b = Siege("crowd_siege", new[] { "infantry" }, new[] { "spearman", "infantry" });
            b.OrderSiege(2, SiegeTasks.Wall);
            for (int t = 0; t < 400; t++) b.Step(); // 성벽에 오름
            b.OrderSiege(0, SiegeTasks.Bash);
            for (int t = 0; t < 900 && b.SiegeActive; t++) b.Step();
            _out.WriteLine($"stone hits {b.StoneHits}");
            Assert.True(b.StoneHits > 0);
        }

        [Fact]
        public void BreachTurnsSiegeIntoFieldBattle()
        {
            var b = Siege("crowd_siege", new[] { "infantry", "cavalry" }, new[] { "spearman" });
            b.OrderSiege(0, SiegeTasks.Ram);
            b.OrderSiege(1, SiegeTasks.WaitBreach);
            for (int t = 0; t < 6000 && b.SiegeActive; t++) b.Step();
            Assert.True(b.BreachTick > 0, "gate broke");
            Assert.False(b.SiegeActive);
            Assert.Empty(b.SiegeTasksFor(0)); // 야전 명령으로
            Assert.Null(b.Armies[1].SiegeTask);
            Assert.Equal(CrowdOrders.Attack, b.Armies[1].Order); // 기다리던 기병이 돌입
        }

        [Fact]
        public void EnemyAiAttacksWhenWeDefend()
        {
            // 공수 교대 (맵 뒤집기): 우리가 성 안, 적 AI가 공성 — 보병 성문 부수기 · 창병 사다리 · 궁병 성벽 쏘기
            var map = Game.Data.Defs.MapDef.Load(new FileDataSource(TestData.DataRoot), "crowd_siege");
            var flipped = new CrowdTerrain(map, Defs.TerrainRules, Defs.TerrainCombat, CrowdTerrain.CellSizeFor(map, Rules().MapFieldWidth), flipped: true);
            var au = new System.Collections.Generic.List<CrowdUnitSpec>
            {
                new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true },
                new CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer" },
            };
            var eu = new System.Collections.Generic.List<CrowdUnitSpec>
            {
                new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 100, Commander = true },
                new CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman", Count = 80 },
                new CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer", Count = 80 },
            };
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyUnits = au, EnemyUnits = eu, AllyAdvance = false, EnemyAdvance = true, Seed = 37, Terrain = flipped }, Defs.TroopTypes, Defs.TroopCounters);
            Assert.Equal(CrowdBattle.Ally, b.SiegeDefender);
            b.OrderSiege(1, SiegeTasks.Wall);
            b.OrderSiege(0, SiegeTasks.Brace);
            for (int t = 0; t < 30; t++) b.Step();
            CrowdArmy Of(string troop) { foreach (var x in b.Armies) if (x.Team == CrowdBattle.Enemy && x.Troop.BaseId == troop) return x; return null; }
            Assert.Equal(SiegeTasks.Bash, Of("infantry").SiegeTask);
            Assert.Equal(SiegeTasks.Ladder, Of("spearman").SiegeTask);
            Assert.Equal(SiegeTasks.ShootWall, Of("archer").SiegeTask);
            for (int t = 0; t < 2000 && !b.IsOver; t++) b.Step();
            _out.WriteLine($"we defend: ladders {b.Ladders.Count}, climbed {(b.Ladders.Count > 0 ? b.Ladders[0].Climbed : -1)}, thrown {(b.Ladders.Count > 0 ? b.Ladders[0].Thrown : -1)}, breach {b.BreachTick}, arrows {b.ArrowHits}, end {b.EndReason}");
            Assert.NotEmpty(b.Ladders);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(15)]
        [InlineData(30)]
        public void FullAssaultCanBreakTheGate(int waitBeforeOrders)
        {
            // 2026-11-23 캡처 47: 충차 · 사다리 · 성벽 쏘기 · 돌입 대기로 쳐도 성문이 열리기 전에 졌던 것 — 수치 조정 (문 막기 최대 40%, 돌 4, 공성 중엔 맹장이 앞으로 나서지 않음)
            var au = new System.Collections.Generic.List<CrowdUnitSpec>
            {
                new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 120, Commander = true, General = "warrior" },
                new CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman", Count = 90 },
                new CrowdUnitSpec { Row = 0, Col = 2, Troop = "cavalry", Count = 60 },
                new CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer", Count = 90, General = "strategist" },
            };
            var eu = new System.Collections.Generic.List<CrowdUnitSpec>
            {
                new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true },
                new CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman" },
                new CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer" },
            };
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyUnits = au, EnemyUnits = eu, AllyAdvance = false, EnemyAdvance = true, Seed = 31, Terrain = Terrain("crowd_siege") }, Defs.TroopTypes, Defs.TroopCounters);
            b.UseSkillData(Defs.Skills);
            for (int t = 0; t < waitBeforeOrders; t++) b.Step(); // 명령 내리는 때가 조금 달라도 (캡처 투어는 화면 시간으로 몇 틱 돈 뒤) 결과가 뒤집히지 않게
            int Id(string place) { foreach (var x in b.TeamArmies(CrowdBattle.Ally)) if (x.Place == place) return x.Id; return -1; }
            b.OrderSiege(Id("front_center"), SiegeTasks.Ram);
            b.OrderSiege(Id("front_left"), SiegeTasks.Ladder);
            b.OrderSiege(Id("front_right"), SiegeTasks.WaitBreach);
            b.OrderSiege(Id("middle_center"), SiegeTasks.ShootWall);
            for (int t = 0; t < 4000 && b.SiegeActive && !b.IsOver; t++) b.Step();
            var cmd = b.Commander(CrowdBattle.Ally);
            _out.WriteLine($"full assault: breach {b.BreachTick}, over {b.IsOver} {b.EndReason}, ally {b.AliveCount(CrowdBattle.Ally)} enemy {b.AliveCount(CrowdBattle.Enemy)}, commander hp {cmd.General.Hp}/{cmd.General.MaxHp}, stones {b.StoneHits}, gate hp {b.Gates[0].Hp}, ladder {b.Ladders[0].Climbed}/{b.Ladders[0].Thrown}");
            Assert.True(b.BreachTick > 0, "gate broke");
            Assert.False(b.IsOver && b.Winner == CrowdBattle.Enemy, "not lost before the breach");
        }

        [Fact]
        public void EnemyAiDefendsTheCastle()
        {
            var b = Siege("crowd_siege", new[] { "infantry" }, new[] { "spearman", "archer", "infantry" }, enemyAi: true);
            for (int t = 0; t < 30; t++) b.Step();
            Assert.Equal(SiegeTasks.Wall, b.Armies[1].SiegeTask); // 창병 = 성벽
            Assert.Equal(SiegeTasks.Wall, b.Armies[2].SiegeTask); // 궁병 = 성벽
            Assert.Equal(SiegeTasks.Brace, b.Armies[3].SiegeTask); // 보병 = 문 막기
        }

        [Fact]
        public void AttackersOnStairsDoNotClimbTheWall()
        {
            // 2026-11-23 캡처 45 "성벽 위로 올라감": 성 안에서 계단을 밟은 공격 병사가 성벽 위 자리로 올라가던 것 — 공격 쪽은 계단에서도 안 올라감
            var b = SiegeAttack("crowd_siege_plain");
            var t = b.Terrain;
            var a = b.Commander(CrowdBattle.Ally);
            var stairs = t.CellCenter(12, 29);
            var top = t.CellCenter(12, 30);
            Assert.Equal("stairs", t.TerrainAt(stairs));
            Assert.Equal("wallTop", t.TerrainAt(top));
            a.Center = top; // 진형 자리 대부분이 성벽 위
            foreach (var s in a.Soldiers) s.Pos = stairs;
            b.OrderHold(a.Id);
            for (int k = 0; k < 200; k++)
            {
                b.Step();
                foreach (var s in a.Soldiers) Assert.False(s.Alive && t.TerrainAt(s.Pos) == "wallTop", $"tick {b.Tick} soldier {s.Id} on wall");
            }
        }

        [Fact]
        public void AttackerOnTheWallComesDownByStairs()
        {
            var b = SiegeAttack("crowd_siege_plain");
            var t = b.Terrain;
            var a = b.Commander(CrowdBattle.Ally);
            var s0 = a.Soldiers[0];
            s0.Pos = t.CellCenter(12, 30); // 성벽 위 (계단 바로 옆)
            a.Center = t.CellCenter(12, 26); // 부대는 성 안 땅
            b.OrderHold(a.Id);
            for (int k = 0; k < 400 && t.TerrainAt(s0.Pos) == "wallTop"; k++) b.Step();
            Assert.NotEqual("wallTop", t.TerrainAt(s0.Pos)); // 계단으로 내려옴 (성벽 위에 남아 있지 않음)
        }

        [Fact]
        public void GateHoldsUntilBrokenThenAttackersPourIn()
        {
            var setup = One("infantry", "crowd_siege");
            setup.AllyUnits[0].Count = 120;
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            Assert.Single(b.Gates);
            var gate = b.Gates[0];
            Assert.Equal(CrowdBattle.Enemy, gate.Defender); // 성 안에 선 편이 지킴
            Assert.Equal(CrowdBattle.Enemy, b.SiegeDefender);
            var a = b.Commander(CrowdBattle.Ally);
            b.OrderAttack(a.Id, b.Commander(CrowdBattle.Enemy).Id);
            // 앞 성벽(맵 30행) 안쪽 = 전장 y 62.5 넘음
            Fix wallY = Fix.FromRatio(625, 10);
            int openTick = -1, insideBefore = 0;
            for (int t = 0; t < 4000 && !b.IsOver; t++)
            {
                b.Step();
                if (openTick < 0)
                {
                    foreach (var s in a.Soldiers) if (s.Alive && s.Pos.Y > wallY) insideBefore++;
                    if (gate.Open) openTick = b.Tick;
                }
                else if (b.Tick > openTick + 300) break;
            }
            int inside = 0;
            foreach (var s in a.Soldiers) if (s.Alive && s.Pos.Y > wallY) inside++;
            _out.WriteLine($"siege: gate opened at {openTick}, hp {gate.Hp}/{gate.MaxHp}, inside before {insideBefore}, inside after {inside}, end {b.EndReason}");
            int onTop = 0;
            foreach (var s in a.Soldiers) if (s.Alive && b.Terrain.TerrainAt(s.Pos) == "wallTop") onTop++;
            Assert.Equal(0, onTop); // 성문으로 들어가도 성벽 위로 올라서지 않음 (R-142)
            Assert.Equal(0, insideBefore); // 부서지기 전엔 아무도 못 들어감
            Assert.True(openTick > 0, "gate never broke");
            Assert.True(inside > 0 || b.IsOver, "nobody went in"); // 부서지면 들어감
        }

        /// <summary>공성전 맵 세트 (평지 성곽 · 강가 성채 · 산성): 성문이 부서지기 전엔 아무도 성 안(성벽 x0..x1 · y0..y1 안쪽 칸)에 못 들어가고, 부서지면 들어간다.</summary>
        [Theory]
        [InlineData("crowd_siege_plain", 2, 5, 42, 3, 32)]
        [InlineData("crowd_siege_river", 1, 8, 39, 3, 26)]
        [InlineData("crowd_siege_mountain", 1, 10, 37, 2, 24)]
        public void SiegeMapsHoldGatesUntilBroken(string id, int gates, int x0, int x1, int y0, int y1)
        {
            var setup = One("infantry", id);
            setup.AllyUnits[0].Count = 120;
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            Assert.Equal(gates, b.Gates.Count);
            foreach (var g in b.Gates) Assert.Equal(CrowdBattle.Enemy, g.Defender);
            Assert.Equal(CrowdBattle.Enemy, b.SiegeDefender);
            var a = b.Commander(CrowdBattle.Ally);
            b.OrderAttack(a.Id, b.Commander(CrowdBattle.Enemy).Id);
            int Inside()
            {
                int n = 0;
                foreach (var s in a.Soldiers)
                {
                    if (!s.Alive) continue;
                    b.Terrain.CellOf(s.Pos, out int c, out int r);
                    if (c > x0 && c < x1 && r > y0 && r < y1) n++;
                }
                return n;
            }
            int openTick = -1, insideBefore = 0;
            for (int t = 0; t < 6000 && !b.IsOver; t++)
            {
                b.Step();
                if (openTick < 0)
                {
                    insideBefore += Inside();
                    foreach (var g in b.Gates) if (g.Open) openTick = b.Tick;
                }
                else if (b.Tick > openTick + 300) break;
            }
            int inside = Inside();
            _out.WriteLine($"{id}: gate opened at {openTick}, inside before {insideBefore}, inside after {inside}, end {b.EndReason}");
            Assert.Equal(0, insideBefore);
            Assert.True(openTick > 0, $"{id} gate never broke");
            Assert.True(inside > 0 || b.IsOver, $"{id} nobody went in");
        }

        // ---- 장수 성향 (R-138) ----

        private static CrowdBattle WithGeneral(string ally, string enemy = "standard", string map = "crowd_plain")
        {
            var setup = One("infantry", map);
            setup.AllyUnits[0].General = ally;
            setup.EnemyUnits[0].General = enemy;
            return new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
        }

        [Fact]
        public void GeneralStandsByRoleAndToughnessFollowsMight()
        {
            var w = WithGeneral("warrior").Commander(CrowdBattle.Ally);
            var st = WithGeneral("standard").Commander(CrowdBattle.Ally);
            var sg = WithGeneral("strategist").Commander(CrowdBattle.Ally);
            Fix front = Fix.FromInt(-99), back = Fix.FromInt(99);
            foreach (var s in w.Soldiers) if (!s.IsGeneral) { if (s.Slot.Y > front) front = s.Slot.Y; if (s.Slot.Y < back) back = s.Slot.Y; }
            // R-144 (2026-11-23 사용자 "진형에서 선두 · 중위 · 후위"): 맹장 = 앞줄 가운데 빈자리 (만나면 앞으로), 지장 = 대열 가운데 빈자리, 책사 = 대열 뒤
            Assert.False(w.GeneralEngaged);
            Assert.True(w.General.Slot.Y >= front - Fix.FromRatio(1, 100) && w.General.Slot.Y <= front + Fix.FromRatio(1, 100), "warrior in the front row");
            Assert.True(w.GeneralRoleSlot.Y > front); // 만나면 대열 맨 앞 (선봉)
            // 2줄 진형은 가운데가 곧 앞줄이라 지장은 대열 바로 뒤 (R-147), 두툼(6줄)이면 대열 가운데 빈자리
            Assert.True(st.General.Slot.Y < back, "standard behind a thin line");
            Assert.Equal(st.GeneralBaseSlot, st.GeneralRoleSlot);
            var thickSetup = One("infantry", "crowd_plain");
            thickSetup.AllyUnits[0].General = "standard";
            thickSetup.AllyUnits[0].Formation = CrowdFormations.Thick;
            var tk = new CrowdBattle(Rules(), thickSetup, Defs.TroopTypes, Defs.TroopCounters).Commander(CrowdBattle.Ally);
            Fix tf = Fix.FromInt(-99), tb = Fix.FromInt(99);
            foreach (var s in tk.Soldiers) if (!s.IsGeneral) { if (s.Slot.Y > tf) tf = s.Slot.Y; if (s.Slot.Y < tb) tb = s.Slot.Y; }
            Assert.True(tk.General.Slot.Y < tf && tk.General.Slot.Y > tb, "standard in the middle of a thick block");
            foreach (var s in tk.Soldiers) if (!s.IsGeneral) Assert.True((s.Slot - tk.General.Slot).Length > Fix.FromRatio(3, 10), "hole for the general");
            Assert.True(sg.General.Slot.Y < back); // 책사 = 대열 뒤
            // 장수 빈자리: 장수 자리에 병사 자리가 없음 (병사와 겹쳐 밀지 않게)
            Assert.True(w.General.MaxHp > st.General.MaxHp && st.General.MaxHp > sg.General.MaxHp); // 무력만큼 버팀
            Assert.Equal(b0().Rules.GeneralHp, st.General.MaxHp); // 장수(무력 70) = 지금까지와 같음
            CrowdBattle b0() => WithGeneral("standard");
        }

        [Fact]
        public void EnemyUnitsStartByTheirBehavior()
        {
            // 2026-11-23 사용자: 시험 탭 적 부대마다 시작 행동 [위임][진군][대기], 기본 대기
            var eu = new System.Collections.Generic.List<CrowdUnitSpec>
            {
                new CrowdUnitSpec { Row = 0, Col = 0, Troop = "infantry", Commander = true },
                new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Behavior = CrowdUnitSpec.Behaviors.Advance },
                new CrowdUnitSpec { Row = 0, Col = 2, Troop = "infantry", Behavior = CrowdUnitSpec.Behaviors.Delegate },
            };
            var au = new System.Collections.Generic.List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true } };
            var b = new CrowdBattle(Rules(), new CrowdSetup { AllyUnits = au, EnemyUnits = eu, AllyAdvance = false, EnemyAdvance = false, Seed = 3, Terrain = Terrain("crowd_plain") }, Defs.TroopTypes, Defs.TroopCounters);
            CrowdArmy At(string place) { foreach (var x in b.TeamArmies(CrowdBattle.Enemy)) if (x.Place == place) return x; return null; }
            for (int t = 0; t < 20; t++) b.Step();
            Assert.Equal(CrowdOrders.Hold, At("front_left").Order); // 대기 (기본)
            Assert.Equal(CrowdOrders.Advance, At("front_center").Order); // 진군
            Assert.True(At("front_right").Delegated); // 위임 = AI가 지휘
            Assert.Equal(CrowdOrders.Attack, At("front_right").Order);
        }

        [Fact]
        public void WarriorPullsBackHealsAndReturnsToTheFront()
        {
            // R-144 (2026-11-23 사용자 "맹장은 선두에서 싸우다 일정 체력 이하면 뒤로 잠시 빠졌다가 체력 회복해서 다시 싸움")
            var b = WithGeneral("warrior");
            var a = b.Commander(CrowdBattle.Ally);
            a.General.Hp = a.General.MaxHp * 15 / 100;
            b.Step();
            Assert.True(a.GeneralPulledBack);
            int back = -1;
            for (int t = 0; t < 400 && back < 0; t++) { b.Step(); if (!a.GeneralPulledBack) back = b.Tick; }
            _out.WriteLine($"warrior returned at tick {back}, hp {a.General.Hp}/{a.General.MaxHp}");
            Assert.True(back > 0, "returned to the front");
            Assert.True(a.General.Hp * 100 >= a.General.MaxHp * b.Rules.GeneralReturnHpPct);
            // 체력 15% → 물러나 회복
            Assert.InRange(back, 180, 300); // (80 − 15)% ÷ 초당 3% ≈ 21.7초 + 맞지 않은 2초 — 틱 0.1초면 약 237틱
        }

        [Fact]
        public void StandardGeneralStrikesButStaysInTheMiddle()
        {
            // R-144 (2026-11-23 사용자 "지장은 중위에서 부하들이 싸울 때 적들을 한 대씩 때리면서 다님")
            var setup = One("infantry", "crowd_plain");
            setup.AllyUnits[0].General = "standard";
            setup.AllyUnits[0].Formation = CrowdFormations.Thick;
            setup.EnemyUnits[0].Count = 140;
            setup.EnemyAdvance = true;
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            int swings = 0;
            Fix far = Fix.Zero;
            for (int t = 0; t < 2500 && !b.IsOver; t++)
            {
                b.Step();
                if (!a.General.Alive || a.GeneralEscaping || a.GeneralPulledBack || a.Routed) break;
                if (a.General.LastAttackTick == b.Tick) swings++;
                Fix off = (a.General.Pos - a.SlotWorld(a.General.Slot)).Length;
                if (off > far) far = off;
            }
            _out.WriteLine($"standard general: swings {swings}, farthest from spot {far}, end {b.EndReason}");
            Assert.True(swings > 0, "standard general struck");
            Assert.True(far < b.Rules.GeneralRoamRange + Fix.FromInt(2), $"stayed near the middle ({far})");
        }

        [Fact]
        public void SmartGeneralsUseSkillsMoreOften()
        {
            int Gap(string preset) { var b = WithGeneral(preset); return b.SkillGapTicksFor(b.Commander(CrowdBattle.Ally)); }
            int warrior = Gap("warrior"), standard = Gap("standard"), strategist = Gap("strategist");
            _out.WriteLine($"skill gap ticks: warrior {warrior}, standard {standard}, strategist {strategist}");
            Assert.True(strategist < standard && standard < warrior); // 지력 92 < 65 < 40 순으로 자주
        }

        [Fact]
        public void ColumnKeepsGeneralInTheMiddle()
        {
            foreach (var preset in new[] { "warrior", "standard", "strategist" })
            {
                var setup = One("infantry", "crowd_plain");
                setup.AllyUnits[0].General = preset;
                setup.AllyUnits[0].Formation = CrowdFormations.Column;
                var a = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters).Commander(CrowdBattle.Ally);
                Fix front = Fix.FromInt(-99), back = Fix.FromInt(99);
                foreach (var s in a.Soldiers) if (!s.IsGeneral) { if (s.Slot.Y > front) front = s.Slot.Y; if (s.Slot.Y < back) back = s.Slot.Y; }
                Fix mid = (front + back) / 2, quarter = (front - back) / 4;
                Assert.True(Fix.Max(a.GeneralRoleSlot.Y - mid, mid - a.GeneralRoleSlot.Y) <= quarter, $"{preset} in the middle of the column");
                Assert.Equal(a.GeneralBaseSlot, a.GeneralRoleSlot);
            }
        }

        [Fact]
        public void WarriorFightsStrategistDoesNot()
        {
            int Swings(string preset)
            {
                var b = WithGeneral(preset);
                b.UseSkillData(Defs.Skills);
                var a = b.Commander(CrowdBattle.Ally);
                b.OrderAttack(a.Id, b.Commander(CrowdBattle.Enemy).Id);
                int hits = 0;
                for (int t = 0; t < 1500 && !b.IsOver; t++) { b.Step(); if (a.General.LastAttackTick == b.Tick) hits++; }
                _out.WriteLine($"{preset}: end {b.Tick} {b.EndReason} hp {a.General.Hp}/{a.General.MaxHp} esc {a.GeneralEscaping} fled {a.General.FledTick} died {a.General.DiedTick}");
                return hits;
            }
            int warrior = Swings("warrior"), strategist = Swings("strategist");
            _out.WriteLine($"general swings: warrior {warrior}, strategist {strategist}");
            Assert.True(warrior > 0);
            Assert.Equal(0, strategist);
        }

        [Fact]
        public void WoundedGeneralRidesAwayAndBattleSaysSo()
        {
            var b = WithGeneral("warrior");
            var a = b.Commander(CrowdBattle.Ally);
            var y0 = a.General.Pos.Y;
            a.General.Hp = 1; // 체력 generalEscapeHpPct(5)% 이하 (R-144: 부대 상태와 관계없이 도망)
            for (int t = 0; t < 600 && !b.IsOver; t++) b.Step();
            Assert.True(a.GeneralEscaping);
            Assert.True(a.General.FledTick >= 0, "general fled");
            Assert.True(a.General.DiedTick < 0); // 쓰러진 게 아니라 빠져나감
            Assert.Equal(CrowdBattle.Enemy, b.Winner);
            Assert.Equal("generalFled", b.EndReason); // 총대장이 달아남 (사로잡음과 다름)
        }

        [Fact]
        public void ArmyRetreatsAtFivePercentTroops()
        {
            // R-147 (2026-11-24 사용자 "부대 5% 병력일 때 퇴각"): 살아 있는 병사가 처음의 5% 이하면 부대 후퇴 + 장수 퇴로
            var setup = One("infantry", "crowd_plain");
            setup.AllyUnits.Add(new CrowdUnitSpec { Row = 0, Col = 0, Troop = "infantry", General = "standard", Count = 100 });
            setup.AllyUnits.Add(new CrowdUnitSpec { Row = 0, Col = 2, Troop = "infantry", General = "standard", Count = 100 });
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            CrowdArmy At(string place) { foreach (var x in b.TeamArmies(CrowdBattle.Ally)) if (x.Place == place) return x; return null; }
            var thin = At("front_left");
            int k = 0;
            foreach (var s in thin.Soldiers) if (!s.IsGeneral && k++ < 95) s.DiedTick = 0; // 100명 중 5명 남음
            b.Step();
            b.Step();
            Assert.True(thin.Routed); // 퇴각 (R-148): 부대가 도망
            Assert.True(thin.GeneralEscaping); // 장수도 퇴로로
            // 장수 체력 5%는 장수 혼자 빠짐 (부대는 그대로)
            var other = At("front_right");
            other.General.Hp = 1;
            b.Step();
            Assert.True(other.GeneralEscaping);
            Assert.False(other.Routed);
        }

        [Fact]
        public void LowMoraleWeakensButDoesNotRout()
        {
            // R-148 (2026-11-24 사용자 "사기가 깎이면 공격력 · 방어력을 줄이거나 이탈자"): 사기로는 무너지지 않고, 약해지고, 이탈자가 나옴
            var b = WithGeneral("standard");
            var a = b.Commander(CrowdBattle.Ally);
            var r = b.Rules;
            a.Morale = Fix.FromInt(100);
            Assert.Equal(Fix.Zero, b.MoralePenaltyPp(a));
            a.Morale = r.WaverAt;
            Assert.Equal(r.MoraleLowPct, b.MoralePenaltyPp(a));
            a.Morale = Fix.Zero;
            Assert.Equal(r.MoraleWaverPct, b.MoralePenaltyPp(a));
            // 사기 0이어도 퇴각은 안 함, 대신 이탈자가 뒷줄부터 나감
            int start = a.FightingCount;
            for (int t = 0; t < 100; t++) { a.Morale = Fix.Zero; b.Step(); }
            Assert.False(a.Routed);
            int deserters = 0;
            foreach (var s in a.Soldiers) if (s.Deserting) deserters++;
            _out.WriteLine($"morale 0 for 10s: deserters {deserters}/{start}, fighting {a.FightingCount}");
            Assert.True(deserters > 0 && deserters < start / 2, $"deserters {deserters}");
            // 이탈자는 맞붙지 않은 뒷줄부터
            Fix backAvg = Fix.Zero, allAvg = Fix.Zero; int nb = 0, na = 0;
            foreach (var s in a.Soldiers) { if (s.IsGeneral || !s.Alive) continue; var y = s.Slot.Y; allAvg += y; na++; if (s.Deserting) { backAvg += y; nb++; } }
            Assert.True(nb == 0 || backAvg / nb < allAvg / na, "deserters from the rear");
        }

        [Fact]
        public void WoundedGeneralStaysBehindWhileArmyHolds()
        {
            // R-145 (2026-11-23 사용자 "20%일 때 뒤로 빠져서 체력 회복"): 성향과 관계없이 30%(2026-11-24 사용자, 예전 20%) 이하면 물러나 회복, 맵 끝까지 가지 않음
            foreach (var preset in new[] { "warrior", "standard", "strategist" })
            {
                var b = WithGeneral(preset);
                var a = b.Commander(CrowdBattle.Ally);
                a.General.Hp = System.Math.Max(2, a.General.MaxHp * 15 / 100); // 30% 이하 · 5% 넘음 (책사 체력 13이면 2)
                b.Step();
                Assert.True(a.GeneralPulledBack, preset + " pulled back");
                Assert.False(a.GeneralEscaping);
                for (int t = 0; t < 400 && a.GeneralPulledBack; t++) b.Step();
                Assert.False(a.GeneralPulledBack, preset + " healed and returned");
                Assert.True(a.General.FledTick < 0, "stayed");
                // 부대가 흔들려도 5% 넘으면 남음, 5% 이하면 장수 퇴로 + 부대 후퇴
                a.Morale = Fix.FromInt(20);
                b.Step();
                Assert.False(a.GeneralEscaping);
                a.General.Hp = 1;
                b.Step();
                Assert.True(a.GeneralEscaping);
            }
        }

        [Fact]
        public void PulledBackGeneralCommandsInsteadOfFighting()
        {
            // R-142 (2026-11-22 사용자 영상): 물러나 지휘인 맹장이 앞줄에서 계속 싸우다 체력이 떨어져 도망가던 것 — 물러나면 먼저 덤비지 않는다
            var setup = One("infantry", "crowd_plain");
            setup.AllyUnits[0].General = "warrior";
            setup.AllyUnits[0].Count = 150;
            setup.EnemyUnits[0].Count = 60; // 부대가 버티는 싸움 (무너지면 장수도 함께 달아남)
            setup.EnemyAdvance = true;
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            int t = 0;
            for (; t < 3000 && !b.IsOver && a.General.LastAttackTick < 0; t++) b.Step(); // 선봉에서 싸우기 시작
            Assert.True(a.General.LastAttackTick >= 0, "warrior fought");
            a.General.Hp = a.General.MaxHp * 15 / 100;
            b.Step();
            Assert.True(a.GeneralPulledBack);
            int swings = 0;
            for (int k = 0; k < 60 && !b.IsOver; k++) { b.Step(); if (a.General.LastAttackTick == b.Tick) swings++; }
            Fix off = (a.General.Pos - a.SlotWorld(a.General.Slot)).Length;
            _out.WriteLine($"pulled back warrior: swings after {swings}, off spot {off}, hp {a.General.Hp}/{a.General.MaxHp}, escaping {a.GeneralEscaping}");
            Assert.True(off < Fix.FromInt(2), $"went to command spot ({off})"); // 적을 쫓지 않고 지휘 자리로
            Assert.False(a.GeneralEscaping);
        }

        [Fact]
        public void CircleKeepsGeneralInTheMiddle()
        {
            // R-142: 원진은 성향과 관계없이 장수가 원 가운데 (맹장이 원 밖 앞에 혼자 튀어나오지 않게)
            foreach (var preset in new[] { "warrior", "standard", "strategist" })
            {
                var setup = One("infantry", "crowd_plain");
                setup.AllyUnits[0].General = preset;
                setup.AllyUnits[0].Formation = CrowdFormations.Circle;
                var a = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters).Commander(CrowdBattle.Ally);
                Assert.Equal(Vec.Zero, a.GeneralBaseSlot);
                Assert.Equal(Vec.Zero, a.GeneralRoleSlot);
                Assert.Equal(Vec.Zero, a.GeneralCommandSlot);
                Assert.Equal(Vec.Zero, a.GeneralWoundedSlot);
            }
        }

        [Fact]
        public void FormationChangeAppliesWhileHolding()
        {
            // R-142 (2026-11-22 사용자 영상 "진형 눌렀는데 이동 누르기 전까지 안 바뀜"): 대기 중 멈춘 병사도 바로 새 자리로
            var setup = One("infantry", "crowd_plain");
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            for (int t = 0; t < 200; t++) b.Step();
            Assert.Equal(CrowdOrders.Hold, a.Order);
            Fix Off() { Fix sum = Fix.Zero; foreach (var s in a.Soldiers) if (s.Alive) sum += (s.Pos - a.SlotWorld(s.Slot)).Length; return sum; }
            b.SetFormation(a.Id, CrowdFormations.Column);
            Fix before = Off();
            for (int t = 0; t < 150; t++) b.Step();
            Fix after = Off();
            _out.WriteLine($"formation change while holding: off {before} -> {after}");
            Assert.True(after * 4 < before, $"{before} -> {after}");
            Assert.Equal(CrowdOrders.Hold, a.Order); // 이동 명령 없이
        }

        [Fact]
        public void WoundedVanguardStepsBackBehindTheLine()
        {
            var b = WithGeneral("warrior");
            var a = b.Commander(CrowdBattle.Ally);
            Fix back = Fix.FromInt(99);
            foreach (var s in a.Soldiers) if (!s.IsGeneral && s.Slot.Y < back) back = s.Slot.Y;
            Assert.True(a.GeneralRoleSlot.Y > Fix.Zero); // 만나면 선봉
            a.General.Hp = a.General.MaxHp * 15 / 100;
            b.Step();
            Assert.True(a.GeneralPulledBack);
            Fix front = Fix.FromInt(-99);
            foreach (var s in a.Soldiers) if (!s.IsGeneral && s.Slot.Y > front) front = s.Slot.Y;
            Assert.True(a.General.Slot.Y < front); // 앞줄 뒤로
            Assert.True(a.General.Slot.Y >= back - b.Rules.GeneralBack); // 하지만 대열 맨 뒤보다는 앞 (대열 안 — 싸움 가까이서 지휘)
            Assert.False(a.GeneralEscaping); // 아직 도망은 아님
        }

        [Fact]
        public void VanguardHoldsWhileArmyIsStrong()
        {
            // 맹장 vs 같은 수 보병: 장수가 물러나(도망) 빠질 때는 부대가 이미 많이 줄었거나 흔들릴 때 (R-139 — 병사가 많은데 먼저 퇴각하지 않음)
            var setup = One("infantry", "crowd_plain");
            setup.AllyUnits[0].General = "warrior";
            setup.AllyUnits[0].Count = 120;
            setup.EnemyUnits[0].Count = 120;
            setup.EnemyAdvance = true;
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            int escapeTick = -1, aliveAtEscape = -1;
            bool shakyAtEscape = false;
            for (int t = 0; t < 3000 && !b.IsOver; t++)
            {
                b.Step();
                if (escapeTick < 0 && a.GeneralEscaping) { escapeTick = b.Tick; aliveAtEscape = a.AliveCount; shakyAtEscape = a.Wavering || a.Routed; }
            }
            _out.WriteLine($"vanguard: escape tick {escapeTick}, alive then {aliveAtEscape}/120, shaky {shakyAtEscape}, end {b.EndReason} winner {b.Winner}, general hp {a.General.Hp}/{a.General.MaxHp}");
            if (escapeTick >= 0) Assert.True(aliveAtEscape * 2 < 120 || shakyAtEscape, $"escaped with {aliveAtEscape} alive");
        }

        [Fact]
        public void DefendingFlipsTheMapSoWeHoldTheHeights()
        {
            var map = Game.Data.Defs.MapDef.Load(new FileDataSource(TestData.DataRoot), "crowd_highland");
            var flipped = new CrowdTerrain(map, Defs.TerrainRules, Defs.TerrainCombat, CrowdTerrain.CellSizeFor(map, Rules().MapFieldWidth), flipped: true);
            var setup = One("infantry", null);
            setup.Terrain = flipped;
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var me = b.Commander(CrowdBattle.Ally).Center;
            var foe = b.Commander(CrowdBattle.Enemy).Center;
            Assert.True(me.Y < foe.Y); // 우리는 여전히 아래쪽
            Assert.True(flipped.LevelAt(me) > flipped.LevelAt(foe)); // 하지만 높은 곳은 우리 쪽
            Assert.Equal(Fix.FromInt(3), flipped.LevelAt(me));
        }

        [Fact]
        public void TroopAptitudeChangesDamage()
        {
            var b = WithGeneral("standard");
            var me = b.Commander(CrowdBattle.Ally);
            Assert.Equal(Fix.Zero, b.AptitudePp(me)); // 기본 B
            me.GeneralInfo.Aptitude["infantry"] = "S";
            Assert.Equal(Fix.FromInt(15), b.AptitudePp(me));
            me.GeneralInfo.Aptitude.Clear();
            me.GeneralInfo.Aptitude["*"] = "C"; // 모든 병종 C
            Assert.Equal(Fix.FromInt(-10), b.AptitudePp(me));
        }

        // ---- 행군 대열 (R-136) ----

        /// <summary>협곡 끝까지 이동: (행군했는지, 도착 뒤 부대 가운데 15칸 안 병사 비율 %, 걸린 틱).</summary>
        private (bool marched, int together, int ticks) ThroughGorge(bool march)
        {
            var setup = One("infantry", "crowd_gorge");
            setup.AllyUnits[0].Count = 150;
            setup.AllyUnits[0].Formation = CrowdFormations.Thick;
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            b.OrderMove(a.Id, new Game.Battle.Model.Geometry.Vec(Fix.FromInt(30), Fix.FromInt(84)), march: march); // [행군 이동] (R-137)
            bool marched = false;
            int t = 0;
            for (; t < 4000 && a.Order == CrowdOrders.Move; t++) { b.Step(); if (a.Marching) marched = true; }
            for (int k = 0; k < 400; k++) b.Step(); // 다 모이게
            int near = 0, alive = 0;
            foreach (var s in a.Soldiers)
            {
                if (!s.Alive || s.IsGeneral) continue;
                alive++;
                if ((s.Pos - a.Center).Length <= Fix.FromInt(15)) near++;
            }
            return (marched, near * 100 / System.Math.Max(1, alive), t);
        }

        [Fact]
        public void MarchColumnGetsEveryoneThroughTheGorge()
        {
            var on = ThroughGorge(true);
            var off = ThroughGorge(false);
            _out.WriteLine($"gorge: march {on.together}% together in {on.ticks} ticks, no march {off.together}% in {off.ticks} ticks");
            Assert.True(on.marched);
            Assert.False(off.marched); // 기본은 진형 유지 (R-137)
            Assert.True(on.together >= 90, "march together " + on.together); // 절벽에 붙어 길을 잃은 병사 없이
            Assert.True(off.together >= 90, "formation together " + off.together); // 진형으로 가도 막힌 병사는 지름길 지도로 돌아온다
        }

        [Fact]
        public void MarchBackDoesNotRetraceOldTrail()
        {
            var b = new CrowdBattle(Rules(), One("infantry", "crowd_plain"), Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            var start = a.Center;
            b.OrderMove(a.Id, start + new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.FromInt(25)), march: true);
            for (int t = 0; t < 600 && a.Order == CrowdOrders.Move; t++) b.Step();
            for (int t = 0; t < 100; t++) b.Step();
            Fix top = Fix.Zero;
            foreach (var s in a.Soldiers) if (s.Alive && s.Pos.Y > top) top = s.Pos.Y;
            // 되돌아 내려가기: 아무도 더 위로 올라갔다 오지 않는다 (예전 자취를 되밟지 않음, 2026-11-22 사용자 영상)
            b.OrderMove(a.Id, start, march: true);
            Fix worst = Fix.Zero;
            for (int t = 0; t < 600 && a.Order == CrowdOrders.Move; t++)
            {
                b.Step();
                foreach (var s in a.Soldiers) if (s.Alive && s.Pos.Y - top > worst) worst = s.Pos.Y - top;
            }
            _out.WriteLine($"march back: went above the old top by {worst}");
            Assert.True(worst < Fix.OneValue, "retraced " + worst);
        }

        [Fact]
        public void PlainNeverMarches()
        {
            var b = new CrowdBattle(Rules(), One("infantry", "crowd_plain"), Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            b.OrderMove(a.Id, a.Center + new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.FromInt(20)));
            for (int t = 0; t < 200; t++) { b.Step(); Assert.False(a.Marching); }
        }

        // ---- 막는 소품 (R-134): 나무 · 바위 ----

        [Fact]
        public void ObstaclesComeFromTerrainAndAreTheSameEachTime()
        {
            var a = new CrowdBattle(Rules(), One("infantry", "test_forest"), Defs.TroopTypes, Defs.TroopCounters);
            var b = new CrowdBattle(Rules(), One("infantry", "test_forest"), Defs.TroopTypes, Defs.TroopCounters);
            Assert.True(a.Obstacles.Count > 24 * 40 * 2, "trees " + a.Obstacles.Count); // 칸마다 2.2그루
            Assert.Equal(a.Obstacles.Count, b.Obstacles.Count);
            for (int k = 0; k < a.Obstacles.Count; k++) Assert.True(a.Obstacles[k].Pos.Equals(b.Obstacles[k].Pos));
            var plain = new CrowdBattle(Rules(), One("infantry", "test_plain"), Defs.TroopTypes, Defs.TroopCounters);
            Assert.Empty(plain.Obstacles); // 평지엔 없음
            var pass = new CrowdBattle(Rules(), One("infantry", "test_mountain_pass"), Defs.TroopTypes, Defs.TroopCounters);
            Assert.NotEmpty(pass.Obstacles);
            Assert.All(pass.Obstacles, o => Assert.Equal("mountain", o.Terrain)); // 산에만 바위
        }

        [Fact]
        public void SoldiersGoAroundTreesNotThrough()
        {
            var b = new CrowdBattle(Rules(), One("infantry", "test_forest"), Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            Fix y0 = Fix.Zero;
            foreach (var s in a.Soldiers) y0 += s.Pos.Y;
            b.OrderMove(a.Id, a.Center + new Game.Battle.Model.Geometry.Vec(Fix.Zero, Fix.FromInt(-14)));
            Fix worst = Fix.FromInt(9);
            for (int t = 0; t < 400; t++)
            {
                b.Step();
                foreach (var s in a.Soldiers)
                {
                    if (!s.Alive) continue;
                    foreach (var o in b.Obstacles)
                    {
                        var gap = (s.Pos - o.Pos).Length - o.Radius - (s.IsGeneral ? b.Rules.SoldierRadius : s.Radius); // 장수(말)는 나무에 병사만 한 몸집 (R-136)
                        if (gap < worst) worst = gap;
                    }
                }
            }
            Fix y1 = Fix.Zero;
            foreach (var s in a.Soldiers) y1 += s.Pos.Y;
            Fix moved = (y0 - y1) / a.Soldiers.Count;
            _out.WriteLine($"deepest into a tree {worst}, mean moved {moved}");
            Assert.True(worst > Fix.FromRatio(-5, 100), "into a tree " + worst); // 기둥 안으로 들어간 병사 없음 (반올림 여유)
            Assert.True(moved > Fix.FromInt(11), "moved " + moved); // 나무 사이로 빠져 거의 다 감 (끼어 멈추지 않음)
        }

        [Fact]
        public void HoldingArmyInForestSettlesInsteadOfJittering()
        {
            var setup = One("infantry", "test_forest");
            setup.AllyUnits[0].Count = 150;
            setup.AllyUnits[0].Formation = CrowdFormations.Thick;
            var b = new CrowdBattle(Rules(), setup, Defs.TroopTypes, Defs.TroopCounters);
            var a = b.Commander(CrowdBattle.Ally);
            b.SetAutoMarch(a.Id, false); // 대기 떨림만 본다 (행군 뒤 다시 펼치는 중이 아니게)
            b.OrderMove(a.Id, a.Center + new Game.Battle.Model.Geometry.Vec(Fix.FromInt(3), Fix.FromInt(-6)));
            for (int t = 0; t < 300; t++) b.Step(); // 도착해 대기 (지장이 대열 가운데 빈자리로 들어가 자리 잡을 때까지, R-144)
            Assert.Equal(CrowdOrders.Hold, a.Order);
            Fix moved = Fix.Zero;
            int jitter = 0;
            for (int t = 0; t < 60; t++)
            {
                b.Step();
                foreach (var s in a.Soldiers)
                {
                    if (!s.Alive) continue;
                    moved += s.Moved.Length;
                    if (s.Moved.Length > Fix.FromRatio(3, 100)) jitter++;
                }
            }
            _out.WriteLine($"holding in forest: total moved {moved}, moving soldier-ticks {jitter}");
            Assert.True(jitter < 200, "jitter " + jitter); // 나무에 걸린 병사가 계속 왔다 갔다 하지 않음 (60틱 × 151명 중, 고치기 전 4298)
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
