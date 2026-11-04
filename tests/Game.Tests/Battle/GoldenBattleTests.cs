using System;
using System.Collections.Generic;
using System.IO;
using Game.Battle.Model.Ai;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using static Game.Tests.Battle.BattleKit;

namespace Game.Tests.Battle
{
    /// <summary>
    /// 기준 전투 (verification.md 9장). 고정 시드·편성·명령으로 돌린 결과를 정답 파일(tests/Game.Tests/Golden/*.json)과 비교한다.
    /// 밸런스·규칙을 의도적으로 바꿨다면 환경 변수 UPDATE_GOLDEN=1 로 다시 저장하고, 바뀐 내용을 PR에서 확인한다.
    /// </summary>
    public class GoldenBattleTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var name in Battles.Keys) yield return new object[] { name };
        }

        private static readonly Dictionary<string, Func<(BattleSetup setup, List<BattleCommand> commands)>> Battles =
            new Dictionary<string, Func<(BattleSetup, List<BattleCommand>)>>
            {
                // 평원 1:1 같은 편성 → 대칭이라 무승부 근처
                ["plain_mirror"] = () => (Setup("test_plain", 1001, null, Corps("A", "teamA", "square"), Corps("B", "teamB", "square")), null),
                // 진형 상성 1쌍: 어린(fishScale) > 학익(craneWing)
                ["formation_fishScale_vs_craneWing"] = () => (Setup("test_plain", 1002, null, Corps("A", "teamA", "fishScale"), Corps("B", "teamB", "craneWing")), null),
                // 군량 부족 퇴각
                ["supply_shortage_retreat"] = () => (Setup("test_plain", 1003, null, Corps("A", "teamA", "square", supply: 2000), Corps("B", "teamB", "square")), null),
                // 숲: 보병 vs 기병
                ["forest_infantry_vs_cavalry"] = () => (Setup("test_forest", 1004, null, Corps("A", "teamA", "square", "infantry"), Corps("B", "teamB", "square", "cavalry")), null),
                // 2:2 혼성 + 플레이어 명령 (산 우회 포함)
                ["mountain_2v2_commands"] = () =>
                {
                    var a1 = Mixed("A1", "teamA", "fishScale",
                        ("vanguard", "spearman", 1000), ("center", "infantry", 1200), ("leftWing", "infantry", 800),
                        ("rightWing", "infantry", 800), ("leftGuard", "archer", 600), ("rightGuard", "archer", 600));
                    a1.Controller = Controllers.Player;
                    var a2 = Corps("A2", "teamA", "craneWing", "cavalry", 700, General("ga2", might: 70, temperament: "brave"));
                    a2.Role = "vanguard";
                    var b1 = Mixed("B1", "teamB", "circle",
                        ("vanguard", "spearman", 900), ("center", "archer", 900), ("leftWing", "spearman", 900),
                        ("rightWing", "spearman", 900), ("leftGuard", "archer", 700), ("rightGuard", "archer", 700));
                    b1.AiStance = AiStanceIds.Defensive;
                    var b2 = Corps("B2", "teamB", "arrowhead", "cavalry", 800, General("gb2", leadership: 40, might: 75, temperament: "arrogant"));
                    b2.Role = "vanguard";
                    var commands = new List<BattleCommand>
                    {
                        new BattleCommand { Tick = 0, CorpsId = "A1", Kind = CommandKinds.MoveTo, X = Fix.FromInt(17), Y = Fix.FromInt(20), Mode = MoveModes.AttackMove },
                        new BattleCommand { Tick = 300, CorpsId = "A1", Kind = CommandKinds.Advance },
                        new BattleCommand { Tick = 500, CorpsId = "A1", Kind = CommandKinds.ChangeFormation, FormationId = "square" },
                    };
                    return (Setup("test_mountain_pass", 1005, null, a1, a2, b1, b2), commands);
                },
            };

        [Theory(DisplayName = "기준 전투가 정답 파일과 같다")]
        [MemberData(nameof(Cases))]
        public void MatchesGolden(string name)
        {
            var (setup, commands) = Battles[name]();
            var result = BattleSimulation.Run(setup, commands);
            var actual = Summarize(result);
            string path = Path.Combine(TestData.RepoRoot, "tests", "Game.Tests", "Golden", name + ".json");

            if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
            {
                File.WriteAllText(path, actual.ToString(Formatting.Indented) + "\n");
                return;
            }
            Assert.True(File.Exists(path), $"정답 파일이 없습니다: {path} (UPDATE_GOLDEN=1 로 만든다)");
            var expected = JObject.Parse(File.ReadAllText(path));
            Assert.True(JToken.DeepEquals(expected, actual),
                $"기준 전투 '{name}' 결과가 다릅니다.\n정답:\n{expected}\n실제:\n{actual}");
        }

        [Fact(DisplayName = "기준 전투: 평원 대칭 전투는 무승부 근처 (남은 병력 차 1% 이하)")]
        public void MirrorIsNearDraw()
        {
            var r = BattleSimulation.Run(Battles["plain_mirror"]().setup);
            long a = r.Find("A").RemainingTroops, b = r.Find("B").RemainingTroops;
            Assert.True(Math.Abs(a - b) * 100 <= Math.Max(a, b), $"A {a}, B {b}");
        }

        [Fact(DisplayName = "기준 전투: 상성 1쌍은 상성대로 승패")]
        public void FormationPairWinner() =>
            Assert.Equal("teamA", BattleSimulation.Run(Battles["formation_fishScale_vs_craneWing"]().setup).Winner);

        private static JObject Summarize(BattleResult r)
        {
            var corps = new JArray();
            foreach (var c in r.Corps)
            {
                var units = new JArray();
                foreach (var u in c.Units)
                    units.Add(new JObject { ["slot"] = u.Slot, ["troopType"] = u.TroopType, ["start"] = u.StartTroops, ["remaining"] = u.RemainingTroops });
                corps.Add(new JObject
                {
                    ["id"] = c.Id,
                    ["team"] = c.Team,
                    ["status"] = c.Status.ToString(),
                    ["startTroops"] = c.StartTroops,
                    ["remainingTroops"] = c.RemainingTroops,
                    ["losses"] = c.Losses,
                    ["startSupply"] = c.StartSupply,
                    ["remainingSupply"] = c.RemainingSupply,
                    ["finalMorale"] = c.FinalMorale.ToString(),
                    ["units"] = units,
                });
            }
            return new JObject
            {
                ["winner"] = r.Winner,
                ["endReason"] = r.EndReason,
                ["ticks"] = r.Ticks,
                ["elapsedSeconds"] = r.ElapsedSeconds.ToString(),
                ["commands"] = r.Commands.Count,
                ["stateHash"] = r.StateHash.ToString("x16"),
                ["corps"] = corps,
            };
        }
    }
}
