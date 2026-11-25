using System.Collections.Generic;
using Game.Battle.Model.Crowd;
using Game.Data.Json;
using Xunit;
using Xunit.Abstractions;

namespace Game.Tests.Battle
{
    /// <summary>
    /// R-154 병종 뼈대 비율 (2026-11-26 사용자): 평지 · 스킬 없음 · 서로 진군 정면 대결에서
    /// 상성 쪽 80명 ≈ 상대 100명 (창병 > 기병 > 궁병 > 창병), 기병 90 ≈ 보병 100, 보병 90 ≈ 창병 100 · 궁병 100.
    /// 장수는 승패에 끼지 않게 (뒤에 서는 책사, 쓰러지지 않음) — 병사끼리 퇴각 · 전멸로 끝남.
    /// "≈" = 시드 여러 판에서 적은 쪽 승률이 한쪽으로 쏠리지 않음. 그리고 같은 수(100 대 100)면 상성 쪽이 크게 이김.
    /// </summary>
    public class CrowdTroopRatioTests
    {
        private readonly ITestOutputHelper _out;
        public CrowdTroopRatioTests(ITestOutputHelper o) { _out = o; }

        private const int Seeds = 12;

        /// <summary>a병종 na명 vs b병종 nb명: a의 승리 수 (편을 번갈아 바꿈), a · b 평균 손실.</summary>
        internal static (int wins, int games, double aLost, double bLost) Fight(string a, int na, string b, int nb, int seeds = Seeds)
        {
            var defs = TestData.Real();
            var rules = CrowdRules.Load(new FileDataSource(TestData.DataRoot));
            int wins = 0;
            double al = 0, bl = 0;
            for (int k = 0; k < seeds; k++)
            {
                bool aAlly = k % 2 == 0;
                var ua = new CrowdUnitSpec { Row = 0, Col = 1, Troop = a, Count = na, Commander = true, General = "strategist" };
                var ub = new CrowdUnitSpec { Row = 0, Col = 1, Troop = b, Count = nb, Commander = true, General = "strategist" };
                var battle = new CrowdBattle(rules, new CrowdSetup
                {
                    AllyUnits = new List<CrowdUnitSpec> { aAlly ? ua : ub },
                    EnemyUnits = new List<CrowdUnitSpec> { aAlly ? ub : ua },
                    AllyAdvance = true, EnemyAdvance = true, Seed = (ulong)(101 + k),
                }, defs.TroopTypes, defs.TroopCounters);
                // 병사끼리의 비율만 (2026-11-26 사용자 "병사 100명 기준"): 장수는 뒤에 서는 책사 · 쓰러지지 않게 — 승패는 병사 퇴각(5%) · 전멸로
                foreach (var army in battle.Armies) { army.General.MaxHp = 1000000; army.General.Hp = 1000000; }
                for (int t = 0; t < 20000 && !battle.IsOver; t++) battle.Step();
                int aTeam = aAlly ? CrowdBattle.Ally : CrowdBattle.Enemy, bTeam = 1 - aTeam;
                if (battle.Winner == aTeam) wins++;
                al += na - System.Math.Min(na, battle.AliveCount(aTeam));
                bl += nb - System.Math.Min(nb, battle.AliveCount(bTeam));
            }
            return (wins, seeds, al / seeds, bl / seeds);
        }

        public static readonly (string a, int na, string b, int nb, string why)[] Targets =
        {
            ("spearman", 80, "cavalry", 100, "상성 창 > 기병"),
            ("cavalry", 80, "archer", 100, "상성 기병 > 궁병"),
            ("archer", 80, "spearman", 100, "상성 궁병 > 창"),
            ("cavalry", 90, "infantry", 100, "기병이 보병보다 조금 셈"),
            ("infantry", 90, "spearman", 100, "보병이 창병보다 조금 셈"),
            ("infantry", 90, "archer", 100, "보병이 궁병보다 조금 셈"),
        };

        [Fact(DisplayName = "R-154 병종 뼈대 비율: 상성 80 ≈ 100, 보병 90 ≈ 100 (적은 쪽이 3 ~ 9판 / 12판 이김)")]
        public void BreakEvenRatios()
        {
            var bad = new List<string>();
            foreach (var t in Targets)
            {
                var r = Fight(t.a, t.na, t.b, t.nb);
                _out.WriteLine($"RATIO {t.a} {t.na} vs {t.b} {t.nb}: {t.a} wins {r.wins}/{r.games}, lost {r.aLost:0} / {r.bLost:0} — {t.why}");
                if (r.wins < 3 || r.wins > 9) bad.Add(t.a + " " + t.na + " vs " + t.b + " " + t.nb + ": " + r.wins);
            }
            Assert.Empty(bad);
        }

        [Fact(DisplayName = "R-154 같은 수(100 대 100)면 상성 쪽이 크게 이김 (10판 이상 / 12판)")]
        public void CountersWinAtEvenOdds()
        {
            var bad = new List<string>();
            foreach (var (a, b) in new[] { ("spearman", "cavalry"), ("cavalry", "archer"), ("archer", "spearman") })
            {
                var r = Fight(a, 100, b, 100);
                _out.WriteLine($"EVEN {a} vs {b}: {a} wins {r.wins}/{r.games}, lost {r.aLost:0} / {r.bLost:0}");
                if (r.wins < 10) bad.Add(a + " vs " + b + ": " + r.wins);
            }
            Assert.Empty(bad);
        }
    }
}
