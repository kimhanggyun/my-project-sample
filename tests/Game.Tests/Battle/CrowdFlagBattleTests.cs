using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Crowd;
using Game.Battle.Model.Geometry;
using Game.Data.Json;
using Game.Foundation.Math;
using Xunit;
using Xunit.Abstractions;

namespace Game.Tests.Battle
{
    /// <summary>깃발전 · 공포 확산 · 총대장 · 화계 · 낙석 · 매복 · 함정 · 명령 기록 재현 (R-161 ~ R-166, battle-crowd.md 3.35).</summary>
    public class CrowdFlagBattleTests
    {
        private readonly ITestOutputHelper _out;
        public CrowdFlagBattleTests(ITestOutputHelper output) { _out = output; }

        private static readonly Game.Data.Defs.DefinitionSet Defs = TestData.Real();
        private static CrowdRules Rules() => CrowdRules.Load(new FileDataSource(TestData.DataRoot));

        private static CrowdTerrain Terrain(string mapId)
        {
            var map = Game.Data.Defs.MapDef.Load(new FileDataSource(TestData.DataRoot), mapId);
            return new CrowdTerrain(map, Defs.TerrainRules, Defs.TerrainCombat, CrowdTerrain.CellSizeFor(map, Rules().MapFieldWidth));
        }

        private static CrowdUnitSpec U(int row, int col, string troop = "infantry", bool commander = false, int count = 60) =>
            new CrowdUnitSpec { Row = row, Col = col, Troop = troop, Commander = commander, Count = count };

        private static CrowdSetup Setup(List<CrowdUnitSpec> ally, List<CrowdUnitSpec> enemy, string map = "crowd_plain", bool flag = false) => new CrowdSetup
        {
            AllyUnits = ally, EnemyUnits = enemy, AllyAdvance = false, EnemyAdvance = false, Seed = 11,
            Terrain = map == null ? null : Terrain(map), FlagBattle = flag,
        };

        private static CrowdBattle Make(CrowdSetup s, bool skills = false)
        {
            var b = new CrowdBattle(Rules(), s, Defs.TroopTypes, Defs.TroopCounters);
            if (skills) b.UseSkillData(Defs.Skills);
            return b;
        }

        /// <summary>그 부대 병사를 p 둘레 촘촘히 옮긴다 (장수는 p, take명 넘는 병사는 멀리).</summary>
        private static void Teleport(CrowdArmy a, Vec p, int take = int.MaxValue, Fix? spread = null)
        {
            Fix sp = spread ?? Fix.FromRatio(1, 2);
            int i = 0;
            foreach (var s in a.Soldiers)
            {
                if (!s.Alive) continue;
                if (s.IsGeneral) { s.Pos = p; continue; }
                if (i >= take) { s.Pos = p + new Vec(Fix.FromInt(40), Fix.Zero); i++; continue; }
                s.Pos = p + new Vec(sp * (i % 8 - 4), sp * (i / 8 % 8 - 4));
                i++;
            }
        }

        // ---- R-161 공포 확산 ----

        [Fact]
        public void PanicSpreadsToOverlappingAllies()
        {
            Fix NeighborMorale(bool routeFront)
            {
                var b = Make(Setup(new List<CrowdUnitSpec> { U(0, 1, commander: true), U(1, 1), U(1, 0) },
                                   new List<CrowdUnitSpec> { U(1, 1, commander: true) }));
                var front = b.Armies.First(a => a.Team == CrowdBattle.Ally && a.Place == "front_center");
                var behind = b.Armies.First(a => a.Team == CrowdBattle.Ally && a.Place == "middle_center");
                if (routeFront) front.Routed = true;
                for (int t = 0; t < 30; t++) b.Step();
                _out.WriteLine($"route {routeFront}: behind {behind.Morale}");
                return behind.Morale;
            }
            var calm = NeighborMorale(false);
            var shaken = NeighborMorale(true);
            Assert.True(shaken < calm, $"panic spreads to the army right behind ({shaken} < {calm})");
        }

        [Fact]
        public void PanicDoesNotReachFarAllies()
        {
            var b = Make(Setup(new List<CrowdUnitSpec> { U(0, 0, commander: true), U(2, 2) },
                               new List<CrowdUnitSpec> { U(1, 1, commander: true) }));
            var near = b.Armies.First(a => a.Team == CrowdBattle.Ally && a.Place == "front_left");
            var far = b.Armies.First(a => a.Team == CrowdBattle.Ally && a.Place == "back_right");
            // 둘을 멀리 떼어 놓음
            Teleport(far, near.Centroid() + new Vec(Fix.FromInt(40), Fix.Zero));
            var start = far.Morale;
            near.Routed = true;
            for (int t = 0; t < 30; t++) b.Step();
            Assert.Equal(start, far.Morale);
        }

        // ---- R-162 총대장 ----

        [Fact]
        public void CommanderFallShakesWholeSideButDoesNotEndBattle()
        {
            var b = Make(Setup(new List<CrowdUnitSpec> { U(0, 1, commander: true), U(0, 0), U(0, 2) },
                               new List<CrowdUnitSpec> { U(1, 1, commander: true) }));
            var cmd = b.Commander(CrowdBattle.Ally);
            var other = b.Armies.First(a => a.Team == CrowdBattle.Ally && !a.IsCommander);
            var before = other.Morale;
            cmd.General.Hp = 0;
            cmd.General.DiedTick = 0;
            b.Step();
            Assert.True(b.CommanderFell(CrowdBattle.Ally));
            Assert.False(b.IsOver, "commander is not a defeat condition (commanderDefeats false)");
            Assert.True(before - other.Morale >= b.Rules.CommanderFallMorale - Fix.OneValue, $"{before} -> {other.Morale}");
            var after = other.Morale;
            b.Step();
            Assert.True(after - other.Morale < Fix.FromInt(5), "shock happens once");
        }

        // ---- R-163 깃발 ----

        private static CrowdBattle FlagBattle()
        {
            return Make(Setup(new List<CrowdUnitSpec> { U(0, 1, commander: true, count: 80) },
                              new List<CrowdUnitSpec> { U(1, 1, commander: true, count: 30), U(0, 0, count: 30) }, flag: true));
        }

        [Fact]
        public void FlagStandsAtDefenderCommanderStart()
        {
            var b = FlagBattle();
            Assert.Equal(CrowdBattle.Enemy, b.FlagDefender);
            Assert.True(b.FlagPos.HasValue);
            Assert.Equal(b.Commander(CrowdBattle.Enemy).General.Pos, b.FlagPos.Value);
        }

        [Fact]
        public void FlagFillsOnlyWithEnoughAttackersOutnumberingDefenders()
        {
            var b = FlagBattle();
            var atk = b.Commander(CrowdBattle.Ally);
            var defArmy = b.Commander(CrowdBattle.Enemy);
            var flag = b.FlagPos.Value;
            var away = flag + new Vec(Fix.FromInt(25), Fix.Zero);
            // 수비 부대들을 깃발에서 멀리
            foreach (var a in b.Armies) if (a.Team == CrowdBattle.Enemy) Teleport(a, away);

            // 1) 공격 10명 (< flagMinAttackers 20): 안 참
            for (int t = 0; t < 20; t++) { Teleport(atk, flag, 10); foreach (var a in b.Armies) if (a.Team == CrowdBattle.Enemy) Teleport(a, away); b.Step(); }
            Assert.Equal(Fix.Zero, b.FlagProgress);

            // 2) 공격 40명, 수비 없음: 참
            for (int t = 0; t < 20; t++) { Teleport(atk, flag, 40); foreach (var a in b.Armies) if (a.Team == CrowdBattle.Enemy) Teleport(a, away); b.Step(); }
            var filled = b.FlagProgress;
            Assert.True(filled > Fix.Zero, $"fills: {filled}");

            // 3) 공격 25명 < 수비 30명: 그대로 (줄지도 않음, 붙어 싸우기 전 몇 틱)
            for (int t = 0; t < 3; t++) { Teleport(atk, flag, 25); Teleport(defArmy, flag); b.Step(); }
            _out.WriteLine($"in ring: atk {b.FlagAttackersIn} def {b.FlagDefendersIn}, progress {b.FlagProgress} (was {filled})");
            Assert.True(b.FlagDefendersIn > b.FlagAttackersIn);
            Assert.Equal(filled, b.FlagProgress);

            // 4) 공격이 원을 떠나면 천천히 줄어듦
            for (int t = 0; t < 20; t++) { Teleport(atk, away + new Vec(Fix.Zero, Fix.FromInt(20))); foreach (var a in b.Armies) if (a.Team == CrowdBattle.Enemy) Teleport(a, away); b.Step(); }
            Assert.True(b.FlagProgress < filled, "decays");
            Assert.True(b.FlagProgress > Fix.Zero, "slowly");
        }

        [Fact]
        public void MoreAttackersFillFaster()
        {
            Fix After(int n)
            {
                var b = FlagBattle();
                var away = b.FlagPos.Value + new Vec(Fix.FromInt(25), Fix.Zero);
                for (int t = 0; t < 30; t++)
                {
                    Teleport(b.Commander(CrowdBattle.Ally), b.FlagPos.Value, n);
                    foreach (var a in b.Armies) if (a.Team == CrowdBattle.Enemy) Teleport(a, away);
                    b.Step();
                }
                return b.FlagProgress;
            }
            Assert.True(After(70) > After(25));
        }

        [Fact]
        public void FlagCaptureWinsForAttacker()
        {
            var b = FlagBattle();
            var away = b.FlagPos.Value + new Vec(Fix.FromInt(25), Fix.Zero);
            for (int t = 0; t < 3000 && !b.IsOver; t++)
            {
                Teleport(b.Commander(CrowdBattle.Ally), b.FlagPos.Value);
                foreach (var a in b.Armies) if (a.Team == CrowdBattle.Enemy) Teleport(a, away);
                b.Step();
            }
            Assert.True(b.IsOver);
            Assert.Equal(CrowdBattle.Ally, b.Winner);
            Assert.Equal("flag", b.EndReason);
        }

        [Fact]
        public void FlagBattleTimeOutIsDefenderWin()
        {
            var b = FlagBattle();
            int limit = (int)(b.Rules.FlagMaxSeconds / b.Rules.TickSeconds).FloorToLong();
            for (int t = 0; t < limit + 5 && !b.IsOver; t++) b.Step();
            Assert.True(b.IsOver);
            Assert.Equal(CrowdBattle.Enemy, b.Winner);
            Assert.Equal("time", b.EndReason);
            Assert.Equal(limit, b.Tick);
        }

        [Fact]
        public void FlagAiAttackersGoForFlagDefendersStayNear()
        {
            var ally = new List<CrowdUnitSpec> { U(0, 1, commander: true), U(0, 0, "cavalry"), U(0, 2) };
            var enemy = new List<CrowdUnitSpec> { U(1, 1, commander: true), U(0, 0), U(0, 2, "archer") };
            foreach (var u in ally) u.Behavior = CrowdUnitSpec.Behaviors.Delegate;
            foreach (var u in enemy) u.Behavior = CrowdUnitSpec.Behaviors.Delegate;
            var b = Make(Setup(ally, enemy, flag: true), skills: true);
            var flag = b.FlagPos.Value;
            Fix closest = Fix.FromInt(999), farthestDef = Fix.Zero;
            var start = new Dictionary<int, Vec>();
            foreach (var a in b.Armies) start[a.Id] = a.Center;
            for (int t = 0; t < 4000 && !b.IsOver; t++)
            {
                b.Step();
                foreach (var a in b.Armies)
                {
                    if (!b.Standing(a) || a.Routed) continue;
                    if (a.Team == CrowdBattle.Ally) closest = Fix.Min(closest, (a.Centroid() - flag).Length);
                    else farthestDef = Fix.Max(farthestDef, (a.Center - start[a.Id]).Length);
                }
            }
            _out.WriteLine($"end {b.Tick} winner {b.Winner} {b.EndReason} flag {b.FlagProgress} closest {closest} defender roam {farthestDef}");
            Assert.True(b.IsOver);
            Assert.True(closest < b.Rules.FlagRadius * 2 || b.EndReason != "time", "attackers reach the flag or decide it earlier");
            Assert.True(farthestDef < b.Rules.FlagGuardRange + b.Rules.FlagAiEngageRange * 2, "defenders do not chase across the map");
        }

        // ---- R-164 화계 · 낙석 ----

        private static Vec? FindCell(CrowdBattle b, Vec from, Fix range, System.Func<Vec, bool> ok)
        {
            var t = b.Terrain;
            for (int r = 0; r < t.Map.Height; r++)
                for (int c = 0; c < t.Map.Width; c++)
                {
                    var p = t.CellCenter(c, r);
                    if ((p - from).Length > range) continue;
                    if (ok(p)) return p;
                }
            return null;
        }

        [Fact]
        public void FireOnlyOnForestGrassAndSpreadsThenBurnsOut()
        {
            var b = Make(Setup(new List<CrowdUnitSpec> { U(1, 1, commander: true) }, new List<CrowdUnitSpec> { U(1, 1, commander: true) }, "crowd_forest"), skills: true);
            var a = b.Commander(CrowdBattle.Ally);
            Assert.True(a.SkillUses.ContainsKey(CrowdBattle.FireAttack));
            var forest = new Vec(Fix.FromInt(4), Fix.FromInt(50));
            Assert.Equal("forest", b.Terrain.TerrainAt(forest));
            Teleport(a, new Vec(Fix.FromRatio(31, 2), Fix.FromInt(50)), spread: Fix.FromRatio(1, 5));
            b.Step();
            var range = b.SkillDef(CrowdBattle.FireAttack).Range ?? Fix.FromInt(12);
            var road = FindCell(b, a.Centroid(), range, p => b.Terrain.TerrainAt(p) == "road");
            Assert.True(road.HasValue);
            Assert.False(b.CanUseSkill(a.Id, CrowdBattle.FireAttack, road.Value), "road does not burn");
            Assert.True(b.UseSkill(a.Id, CrowdBattle.FireAttack, forest));
            int first = b.Burning.Count;
            Assert.True(first > 0);
            int most = first;
            for (int t = 0; t < 100; t++) { b.Step(); most = System.Math.Max(most, b.Burning.Count); }
            _out.WriteLine($"fire: first {first}, most {most}");
            Assert.True(most > first, "spreads to neighbor forest cells");
            for (int t = 0; t < 2000 && b.Burning.Count > 0; t++) b.Step();
            Assert.Empty(b.Burning);
            // 처음 붙은 곳에서 fireSpreadRange (+ 칸 반) 밖은 타지 않음 — 숲 전체가 타지 않게
            Fix limit = b.Rules.FireSpreadRange + b.Terrain.CellSize;
            int w = b.Terrain.Map.Width;
            foreach (int cell in b.BurntCells)
                Assert.True((b.Terrain.CellCenter(cell % w, cell / w) - forest).Length <= limit, "fire stays near where it was lit");
            _out.WriteLine($"fire: burnt {b.BurntCells.Count}");
            b.Terrain.CellOf(forest, out int fc, out int fr);
            Assert.True(b.IsBurnt(fc, fr), "burnt cells do not burn again");
        }

        [Fact]
        public void FireHurtsAndShakesSoldiersInside()
        {
            var b = Make(Setup(new List<CrowdUnitSpec> { U(1, 1, commander: true) }, new List<CrowdUnitSpec> { U(1, 1, commander: true) }, "crowd_forest"), skills: true);
            var a = b.Commander(CrowdBattle.Ally);
            var foe = b.Commander(CrowdBattle.Enemy);
            var forest = new Vec(Fix.FromInt(3), Fix.FromInt(50));
            Teleport(a, forest + new Vec(Fix.FromInt(8), Fix.Zero));
            b.Step();
            Assert.True(b.UseSkill(a.Id, CrowdBattle.FireAttack, forest));
            var m0 = foe.Morale;
            int hp0 = foe.Soldiers.Where(s => !s.IsGeneral).Sum(s => s.Hp);
            for (int t = 0; t < 20; t++) { Teleport(foe, forest, spread: Fix.FromRatio(1, 4)); b.Step(); }
            int hp1 = foe.Soldiers.Where(s => !s.IsGeneral).Sum(s => s.Hp);
            Assert.True(hp1 < hp0, "burns");
            Assert.True(foe.Morale < m0, "fire shakes morale");
        }

        [Fact]
        public void RockfallNeedsHigherGround()
        {
            var plain = Make(Setup(new List<CrowdUnitSpec> { U(1, 1, commander: true) }, new List<CrowdUnitSpec> { U(1, 1, commander: true) }), skills: true);
            var pa = plain.Commander(CrowdBattle.Ally);
            Assert.False(plain.CanUseSkill(pa.Id, CrowdBattle.Rockfall, pa.Centroid() + new Vec(Fix.Zero, Fix.FromInt(5))), "flat ground: no rockfall");

            foreach (var map in new[] { "crowd_highland", "crowd_pass", "crowd_gorge" })
            {
                var b = Make(Setup(new List<CrowdUnitSpec> { U(1, 1, commander: true) }, new List<CrowdUnitSpec> { U(1, 1, commander: true) }, map), skills: true);
                var a = b.Commander(CrowdBattle.Ally);
                var troop = a.Troop;
                // 가장 높은 칸으로 옮기고, 사거리 안 낮은 칸을 찾음
                var t = b.Terrain;
                Vec high = Vec.Zero; Fix hi = Fix.FromInt(-1);
                for (int r = 0; r < t.Map.Height; r++)
                    for (int c = 0; c < t.Map.Width; c++)
                        if (!t.Blocks(troop, c, r) && t.LevelAt(t.CellCenter(c, r)) > hi) { hi = t.LevelAt(t.CellCenter(c, r)); high = t.CellCenter(c, r); }
                Teleport(a, high, spread: Fix.FromRatio(1, 4));
                var range = b.SkillDef(CrowdBattle.Rockfall).Range ?? Fix.FromInt(8);
                var low = FindCell(b, a.Centroid(), range, p => b.CanUseSkill(a.Id, CrowdBattle.Rockfall, p));
                if (!low.HasValue) { _out.WriteLine($"{map}: no drop spot"); continue; }
                Assert.True(b.UseSkill(a.Id, CrowdBattle.Rockfall, low.Value));
                var foe = b.Commander(CrowdBattle.Enemy);
                int hp0 = foe.Soldiers.Where(s => !s.IsGeneral).Sum(s => s.Hp);
                for (int k = 0; k < 40; k++) { Teleport(foe, low.Value, spread: Fix.FromRatio(1, 4)); b.Step(); }
                Assert.NotEmpty(b.RockEvents);
                Assert.True(foe.Soldiers.Where(s => !s.IsGeneral).Sum(s => s.Hp) < hp0, "rocks hit");
                _out.WriteLine($"{map}: rock from {high} to {low}");
                return;
            }
            Assert.Fail("no map with a rockfall spot");
        }

        // ---- R-165 매복 · 함정 ----

        private CrowdBattle AmbushBattle(out CrowdArmy hider, out CrowdArmy walker, out Vec spot)
        {
            var enemy = new List<CrowdUnitSpec> { U(1, 1, commander: true), U(0, 0) };
            var b = Make(Setup(new List<CrowdUnitSpec> { U(1, 1, commander: true) }, enemy, "crowd_forest"));
            hider = b.Armies.First(a => a.Team == CrowdBattle.Enemy && !a.IsCommander);
            walker = b.Commander(CrowdBattle.Ally);
            spot = new Vec(Fix.FromInt(3), Fix.FromInt(50));
            Teleport(hider, spot, spread: Fix.FromRatio(1, 3));
            hider.Ambush = hider.Hidden = true;
            return b;
        }

        [Fact]
        public void AmbushHiddenUntilEnemyWalksInThenShocks()
        {
            var b = AmbushBattle(out var hider, out var walker, out var spot);
            Assert.True(b.HiddenFrom(hider, CrowdBattle.Ally));
            Assert.False(b.HiddenFrom(hider, CrowdBattle.Enemy));
            // 멀리 있으면 그대로 숨음
            Teleport(walker, spot + new Vec(Fix.FromInt(25), Fix.Zero));
            for (int t = 0; t < 10; t++) b.Step();
            Assert.True(hider.Hidden);
            var m0 = walker.Morale;
            // 걸어 들어옴: 드러나며 기습
            for (int t = 0; t < 10 && hider.Hidden; t++) { Teleport(walker, spot + new Vec(Fix.FromInt(5), Fix.Zero)); b.Step(); }
            Assert.False(hider.Hidden);
            Assert.True(b.AmbushHits > 0);
            Assert.True(m0 - walker.Morale >= b.Rules.AmbushMorale - Fix.FromInt(2), $"{m0} -> {walker.Morale}");
        }

        [Fact]
        public void HiddenArmyIsNotTargeted()
        {
            var b = AmbushBattle(out var hider, out var walker, out var spot);
            Teleport(walker, spot + new Vec(Fix.FromInt(15), Fix.Zero));
            // 적 총대장 부대도 멀리 숨김 없이: 가장 가까운 적은 숨은 부대가 아니라 총대장 부대
            Assert.True(b.OrderAttackNearest(walker.Id));
            Assert.NotEqual(hider.Id, walker.OrderTarget);
        }

        [Fact]
        public void AmbushOutsideCoverIsNotHidden()
        {
            var enemy = new List<CrowdUnitSpec> { U(1, 1, commander: true) };
            enemy[0].Ambush = true;
            var b = Make(Setup(new List<CrowdUnitSpec> { U(1, 1, commander: true) }, enemy, "crowd_plain"));
            Assert.False(b.Commander(CrowdBattle.Enemy).Hidden, "plain has no cover");
        }

        [Fact]
        public void PitTrapHitsOnceAndSlowTrapSlows()
        {
            var s = Setup(new List<CrowdUnitSpec> { U(1, 1, commander: true) }, new List<CrowdUnitSpec> { U(1, 1, commander: true) });
            var probe = Make(s);
            var at = probe.Commander(CrowdBattle.Ally).Centroid();
            s.Traps = new List<CrowdTrapSpec> { new CrowdTrapSpec { Team = CrowdBattle.Enemy, Kind = CrowdTrapKinds.Pit, X = at.X, Y = at.Y } };
            var b = Make(s);
            var a = b.Commander(CrowdBattle.Ally);
            int hp0 = a.Soldiers.Where(x => !x.IsGeneral).Sum(x => x.Hp);
            b.Step();
            int hp1 = a.Soldiers.Where(x => !x.IsGeneral).Sum(x => x.Hp);
            Assert.True(hp1 < hp0, "pit hurts");
            Assert.True(b.Traps[0].Used && b.Traps[0].Revealed);
            for (int t = 0; t < 10; t++) b.Step();
            Assert.Equal(hp1, a.Soldiers.Where(x => !x.IsGeneral).Sum(x => x.Hp)); // 한 번만

            // 마름쇠: 같은 거리를 덜 감
            Fix Walked(bool trap)
            {
                var s2 = Setup(new List<CrowdUnitSpec> { U(1, 1, commander: true) }, new List<CrowdUnitSpec> { U(1, 1, commander: true) });
                var p0 = Make(s2).Commander(CrowdBattle.Ally).Centroid();
                if (trap) s2.Traps = new List<CrowdTrapSpec> { new CrowdTrapSpec { Team = CrowdBattle.Enemy, Kind = CrowdTrapKinds.Slow, X = p0.X, Y = p0.Y } };
                var bb = Make(s2);
                var me = bb.Commander(CrowdBattle.Ally);
                bb.OrderMove(me.Id, p0 + new Vec(Fix.Zero, Fix.FromInt(-30)));
                for (int t = 0; t < 15; t++) bb.Step();
                return (me.Centroid() - p0).Length;
            }
            Assert.True(Walked(true) < Walked(false));
        }

        // ---- R-166 명령 기록 · 재현 ----

        private static string Hash(CrowdBattle b)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(b.Tick).Append('|').Append(b.Winner).Append('|').Append(b.EndReason).Append('|').Append(b.FlagProgress.Raw).Append('|');
            foreach (var s in b.Soldiers) sb.Append(s.Pos.X.Raw).Append(',').Append(s.Pos.Y.Raw).Append(',').Append(s.Hp).Append(';');
            foreach (var a in b.Armies) sb.Append(a.Morale.Raw).Append(';');
            return sb.ToString();
        }

        [Fact]
        public void CommandLogReplaysToSameBattle()
        {
            CrowdSetup S() => new CrowdSetup
            {
                AllyUnits = new List<CrowdUnitSpec> { U(0, 1, commander: true), U(0, 0, "archer"), U(1, 2, "cavalry") },
                EnemyUnits = new List<CrowdUnitSpec> { U(1, 1, commander: true), U(0, 0), U(0, 2, "archer") },
                AllyAdvance = false, EnemyAdvance = true, Seed = 23, Terrain = Terrain("crowd_forest"), FlagBattle = true,
                Traps = new List<CrowdTrapSpec> { new CrowdTrapSpec { Team = CrowdBattle.Enemy, Kind = CrowdTrapKinds.Pit, X = Fix.FromInt(30), Y = Fix.FromInt(50) } },
            };
            var b = Make(S(), skills: true);
            Assert.Empty(b.Commands); // 시작 명령(설정)은 기록 아님
            var ids = b.Armies.Where(a => a.Team == CrowdBattle.Ally).Select(a => a.Id).ToArray();
            for (int t = 0; t < 1500 && !b.IsOver; t++)
            {
                if (t == 5) b.OrderAdvance(ids[0]);
                if (t == 10) b.OrderMove(ids[2], new Vec(Fix.FromInt(10), Fix.FromInt(60)), march: true);
                if (t == 12) b.OrderMove(ids[2], new Vec(Fix.FromInt(20), Fix.FromInt(80)), append: true);
                if (t == 40) b.UseSkill(ids[0], CrowdBattle.Rally);
                if (t == 60) b.SetDelegated(ids[1], true);
                if (t == 80) b.SetFormation(ids[0], CrowdFormations.Wedge);
                if (t == 120) b.OrderAttackNearest(ids[2]);
                b.Step();
            }
            _out.WriteLine($"commands {b.Commands.Count}, end {b.Tick} {b.Winner} {b.EndReason}");
            Assert.True(b.Commands.Count >= 7);
            Assert.All(b.Commands, c => Assert.True(c.Tick >= 5)); // AI 명령은 기록 안 됨
            var again = CrowdBattle.Replay(Rules(), S(), Defs.TroopTypes, Defs.TroopCounters, Defs.Skills, b.Commands, b.Tick);
            Assert.Equal(Hash(b), Hash(again));
        }
    }
}
