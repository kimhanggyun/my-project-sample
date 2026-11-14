using System.Collections.Generic;
using System.Linq;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Campaign.Save;
using Game.Data.Defs;
using Game.Flow;
using Game.Foundation.Math;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>군대 개편 (army-v2.md, verification R-94): 계급, 병종 묶음, 진형 칸 자동 배치, 예전 세이브 바꾸기.</summary>
    public class ArmyTests
    {
        private static List<SlotTroops> Layout(params (string type, long n)[] picks) =>
            Army.Layout(Kit.D, picks.Select(p => (p.type, p.type, p.n)).ToList());

        [Fact(DisplayName = "R-94 계급표: 레벨 → 계급 (1 십부장, 3 백부장, 6 천부장, 10 교위, 15 장군, 21 대장군), 다음 계급")]
        public void Ranks()
        {
            var c = Kit.D.Campaign;
            Assert.Equal(new[] { "squad", "squad", "century", "thousand", "colonel", "general", "grandGeneral", "grandGeneral" },
                new[] { 1, 2, 3, 6, 10, 15, 21, 30 }.Select(l => c.RankFor(l).Id));
            Assert.Equal((1, 1, 3000L), (c.RankFor(1).Corps, c.RankFor(1).Slots, c.RankFor(1).TroopMax));
            Assert.Equal("century", c.NextRank(1).Id);
            Assert.Null(c.NextRank(21));
        }

        [Fact(DisplayName = "R-94 새 게임: 병종마다 묶음 하나, 출진은 장수 A · 보병 3,000 한 칸 (십부장)")]
        public void NewGame()
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            Assert.Equal(1, s.LordLevel);
            Assert.Equal(s.Units.Count, s.Units.Select(u => u.TroopType).Distinct().Count());
            Assert.All(s.Units, u => Assert.Equal(u.TroopType, u.UnitId));
            var c = Assert.Single(s.Compositions);
            var p = Assert.Single(c.Picks);
            Assert.Equal(("infantry", 3000L), (p.UnitId, p.Troops));
            Assert.Empty(c.Slots);
            Assert.Empty(CorpsValidator.Validate(Kit.D, s, s.Compositions));
        }

        [Fact(DisplayName = "R-94 자동 배치: 근접 하나면 6칸 모두 (3,000 → 500씩), 근접+원거리면 앞 4칸·뒤 2칸")]
        public void LayoutSlots()
        {
            var one = Layout(("infantry", 3000));
            Assert.Equal(6, one.Count);
            Assert.All(one, x => Assert.Equal(500, x.Troops));
            Assert.Equal(SlotIds.All.OrderBy(x => x), one.Select(x => x.Slot).OrderBy(x => x));

            var mixed = Layout(("infantry", 2000), ("archer", 1001));
            Assert.Equal(new[] { "vanguard", "leftWing", "rightWing", "center" }, mixed.Where(x => x.UnitId == "infantry").Select(x => x.Slot));
            Assert.Equal(new[] { "leftGuard", "rightGuard" }, mixed.Where(x => x.UnitId == "archer").Select(x => x.Slot));
            Assert.Equal(new long[] { 501, 500 }, mixed.Where(x => x.UnitId == "archer").Select(x => x.Troops)); // 나머지는 앞 칸부터
            Assert.Equal(3001, mixed.Sum(x => x.Troops));

            // 근접 둘: 4칸을 인원 비율로 (큰 병종부터 1칸씩, 남는 칸은 칸당 인원이 많은 쪽)
            var two = Layout(("infantry", 3000), ("cavalry", 1000), ("archer", 600));
            Assert.Equal(3, two.Count(x => x.UnitId == "infantry"));
            Assert.Equal(1, two.Count(x => x.UnitId == "cavalry"));
            Assert.Equal(2, two.Count(x => x.UnitId == "archer"));

            // 원거리 셋 + 근접 하나: 뒤 칸이 모자라면 근접 쪽 끝 칸을 빌린다
            var ranged = Layout(("infantry", 1000), ("archer", 900), ("crossbowman", 800), ("catapult", 700));
            Assert.Equal(3, ranged.Count(x => x.UnitId == "infantry"));
            Assert.Equal(6, ranged.Count);
            Assert.Contains(ranged, x => x.Slot == "center" && x.UnitId != "infantry");

            Assert.Empty(Layout(("infantry", 0)));
            Assert.Equal(3, Layout(("infantry", 3)).Count); // 0명 칸은 만들지 않는다
        }

        [Fact(DisplayName = "R-94 출진: 병종 칸이 진형 칸 6개로 나뉘어 전투에 들어가고, 결과는 병종 묶음으로 모인다")]
        public void SortieAndResult()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var s = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(s, new SortieRequest { StepId = "step01", Seed = 1, Delegate = true });
            Assert.True(plan.Ok, string.Join("; ", plan.Errors));
            var mine = plan.Setup.Corps.Single(c => c.Team == StoryFlow.PlayerTeam);
            Assert.Equal(6, mine.Units.Count);
            Assert.All(mine.Units, u => Assert.Equal("infantry", StoryFlow.PoolIdOf(u.UnitId)));
            Assert.Equal(3000, mine.Units.Sum(u => u.Troops));
            Assert.True(flow.Confirm(s, plan));
            var result = Game.Battle.Model.Simulation.BattleSimulation.Run(plan.Setup);
            var report = flow.ApplyResult(s, plan, result);
            var inf = Army.Pool(s, "infantry");
            var line = Assert.Single(report.Units);
            Assert.Equal("infantry", line.UnitId);
            Assert.Equal(3000 - line.Losses, inf.Troops); // 병력은 손실만큼 줄었다 (칸 6개 손실을 한 줄로)
            Assert.Equal(line.Wounded, inf.Wounded);
        }

        [Fact(DisplayName = "R-94 예전 세이브 바꾸기: 같은 병종 부대를 합치고(훈련도·결속 가중 평균), 칸 편성을 병종 칸으로")]
        public void Normalize()
        {
            var s = new CampaignState { LordLevel = 0 };
            s.Units.Add(new UnitState { UnitId = "u01", TroopType = "infantry", Troops = 3000, Wounded = 0, Training = Fix.FromRaw(6000), Cohesion = Fix.FromInt(10) });
            s.Units.Add(new UnitState { UnitId = "u02", TroopType = "infantry", Troops = 1000, Wounded = 0, Training = Fix.FromRaw(2000), Cohesion = Fix.FromInt(2) });
            s.Units.Add(new UnitState { UnitId = "u03", TroopType = "archer", Troops = 2000 });
            s.Units.Add(new UnitState { UnitId = "u04", TroopType = "shieldbearer", Troops = 500, ParentUnitId = "u02" });
            var comp = new CorpsComposition { GeneralId = "general_a", Formation = "square" };
            comp.Slots["vanguard"] = "u01";
            comp.Slots["center"] = "u02";
            comp.Slots["leftGuard"] = "u03";
            s.Compositions.Add(comp);

            Assert.True(Army.Normalize(null, s));
            Assert.Equal(1, s.LordLevel);
            Assert.Equal(new[] { "infantry", "archer", "shieldbearer" }, s.Units.Select(u => u.UnitId));
            var inf = s.FindUnit("infantry");
            Assert.Equal((4000L, 5000L, 8L), (inf.Troops, inf.Training.Raw, inf.Cohesion.FloorToLong())); // (0.6×3000 + 0.2×1000)/4000 = 0.5
            Assert.Equal("infantry", s.FindUnit("shieldbearer").ParentUnitId);
            Assert.Empty(comp.Slots);
            Assert.Equal(new[] { ("infantry", 4000L), ("archer", 2000L) }, comp.Picks.Select(p => (p.UnitId, p.Troops)));
            Assert.False(Army.Normalize(null, s)); // 두 번째는 할 일이 없다

            // 세이브를 읽으면 저절로 바뀐다
            var store = new Game.Foundation.Save.InMemorySaveStore();
            var old = new CampaignState();
            old.Units.Add(new UnitState { UnitId = "u01", TroopType = "infantry", Troops = 100 });
            old.Units.Add(new UnitState { UnitId = "u07", TroopType = "infantry", Troops = 200 });
            new CampaignSaveService(store).Save(old);
            var back = new CampaignSaveService(store).Load();
            Assert.Equal(300L, Assert.Single(back.Units).Troops);
        }

        [Fact(DisplayName = "R-94 마스터 dev.lordLevel: 계급 레벨 ± (1 ~ 최대 안), 바뀔 것이 없으면 거절")]
        public void DevLordLevel()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            Assert.True(runner.Apply(s, PlayerActionRunner.DevLordLevel(5)));
            Assert.Equal(6, s.LordLevel);
            Assert.True(runner.Apply(s, PlayerActionRunner.DevLordLevel(-100)));
            Assert.Equal(1, s.LordLevel);
            Assert.False(runner.Apply(s, PlayerActionRunner.DevLordLevel(-1)));
            Assert.True(runner.Apply(s, PlayerActionRunner.DevLordLevel(1000)));
            Assert.Equal(Kit.D.Campaign.LordMaxLevel, s.LordLevel);
        }
    }
}
