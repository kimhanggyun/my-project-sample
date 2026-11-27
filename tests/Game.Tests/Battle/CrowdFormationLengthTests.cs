using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Crowd;
using Game.Data.Json;
using Game.Foundation.Math;
using Xunit;

namespace Game.Tests.Battle
{
    /// <summary>
    /// R-157 진형 길이 (2026-11-27 사용자): 진형은 줄 수가 아니라 정해진 길이 — 기준 앞줄 formationFront명(50),
    /// 사람이 늘면 뒤로 두꺼워짐 (일자 formationRows(5)줄 · 두툼 길이 ½ · 10줄 · 더 두툼 길이 ⅓ · 15줄까지, 넘으면 옆으로).
    /// 3×3 칸 간격 · 평지 전장은 부대 크기에 맞춰 넓어진다 (테스트 병사 수 1000).
    /// </summary>
    public class CrowdFormationLengthTests
    {
        private static readonly Fix Sp = Fix.FromRatio(45, 100);

        private static CrowdRules Rules() => CrowdRules.Load(new FileDataSource(TestData.DataRoot));

        private static (int rows, int front) Shape(string id, int count)
        {
            var r = Rules();
            var slots = CrowdFormations.Slots(id, count, Sp, r.FormationFront, r.FormationRows);
            var ys = slots.Select(p => p.Y).Distinct().OrderByDescending(y => y).ToList();
            int front = slots.Count(p => p.Y == ys[0]);
            return (ys.Count, front);
        }

        [Fact(DisplayName = "R-157 일자: 앞줄 50명, 사람이 늘면 5줄까지 두꺼워지고 넘으면 옆으로")]
        public void LineGrowsDeepThenWide()
        {
            Assert.Equal((1, 40), Shape(CrowdFormations.Line, 40));
            Assert.Equal((2, 50), Shape(CrowdFormations.Line, 100)); // R-154 비율 측정과 같은 모양 (2줄)
            Assert.Equal((2, 40), Shape(CrowdFormations.Line, 80));  // 줄마다 고르게
            Assert.Equal((5, 50), Shape(CrowdFormations.Line, 250));
            Assert.Equal((5, 80), Shape(CrowdFormations.Line, 400)); // 5줄을 넘으면 옆으로 넓어짐
        }

        [Fact(DisplayName = "R-157 두툼 = 길이 ½ · 10줄까지, 더 두툼 = 길이 ⅓ · 15줄까지")]
        public void ThickAndThickerAreShorterAndDeeper()
        {
            Assert.Equal((4, 25), Shape(CrowdFormations.Thick, 100));
            Assert.Equal((10, 25), Shape(CrowdFormations.Thick, 250));
            Assert.Equal((10, 60), Shape(CrowdFormations.Thick, 600));
            Assert.Equal((7, 15), Shape(CrowdFormations.Thicker, 100));
            Assert.Equal((15, 20), Shape(CrowdFormations.Thicker, 300));
            Assert.Contains(CrowdFormations.Thicker, CrowdFormations.All);
        }

        [Fact(DisplayName = "R-157 쐐기는 폭 ½에서 멈추고 뒤로, V자 팔은 5줄까지 두꺼워짐")]
        public void WedgeAndVeeKeepTheirLength()
        {
            var r = Rules();
            Fix Width(List<Game.Battle.Model.Geometry.Vec> s) => s.Max(p => p.X) - s.Min(p => p.X);
            // 쐐기: 줄마다 2명씩 넓어지다 25명(홀수)에서 멈춤 → 1000명이어도 폭 25명
            var wedge = CrowdFormations.Slots(CrowdFormations.Wedge, 1000, Sp, r.FormationFront, r.FormationRows);
            Assert.True(Width(wedge) <= Sp * 25, "wedge width capped");
            // V자: 100명 = 팔마다 50명, 팔 길이 25 → 2줄 두께. 1000명이어도 팔은 5줄까지만 두꺼워지고 길어짐
            var vee100 = CrowdFormations.Slots(CrowdFormations.Vee, 100, Sp, r.FormationFront, r.FormationRows);
            var vee1000 = CrowdFormations.Slots(CrowdFormations.Vee, 1000, Sp, r.FormationFront, r.FormationRows);
            Assert.Equal(100, vee100.Count);
            Assert.True(Width(vee1000) > Width(vee100), "vee arms get longer past 5 rows");
        }

        [Fact(DisplayName = "R-157 테스트 3×3: 1000명 일자 셋을 나란히 놓으면 칸 간격 · 전장이 넓어져 겹치지 않음")]
        public void GridMakesRoomForBigUnits()
        {
            var defs = TestData.Real();
            var rules = Rules();
            Assert.Equal(1000, rules.GridMaxCount);
            var setup = new CrowdSetup
            {
                AllyUnits = new List<CrowdUnitSpec>
                {
                    new CrowdUnitSpec { Row = 0, Col = 0, Count = 1000, Commander = true },
                    new CrowdUnitSpec { Row = 0, Col = 1, Count = 1000 },
                    new CrowdUnitSpec { Row = 0, Col = 2, Count = 1000 },
                },
                EnemyUnits = new List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Count = 1000, Commander = true } },
            };
            var b = new CrowdBattle(rules, setup, defs.TroopTypes, defs.TroopCounters);
            Assert.True(b.Rules.FieldWidth > rules.FieldWidth, $"field widened ({b.Rules.FieldWidth})");
            var allies = b.TeamArmies(CrowdBattle.Ally).OrderBy(a => a.Center.X).ToList();
            Assert.Equal(3, allies.Count);
            for (int i = 0; i + 1 < allies.Count; i++)
                Assert.True(allies[i + 1].Center.X - allies[i].Center.X > allies[i].HalfWidth + allies[i + 1].HalfWidth, "units do not overlap");
            foreach (var a in allies) Assert.True(a.Center.X - a.HalfWidth > Fix.Zero && a.Center.X + a.HalfWidth < b.Rules.FieldWidth, "inside the field");
            // 작은 부대 하나 (R-154 비율 측정)는 전장 그대로
            var small = new CrowdBattle(rules, new CrowdSetup
            {
                AllyUnits = new List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Count = 100, Commander = true } },
                EnemyUnits = new List<CrowdUnitSpec> { new CrowdUnitSpec { Row = 0, Col = 1, Count = 100, Commander = true } },
            }, defs.TroopTypes, defs.TroopCounters);
            Assert.Equal(rules.FieldWidth, small.Rules.FieldWidth);
        }
    }
}
