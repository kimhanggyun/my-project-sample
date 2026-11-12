using System;
using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Troops;
using Game.Flow;
using Game.Foundation.Time;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-61 부상병 자연 회복: 마지막으로 부상병이 생긴 때부터 woundedRecoverMinutes 지나면 그 부대의 부상병이 전부 병력으로.</summary>
    public class WoundedRecoveryTests
    {
        [Fact]
        public void WoundedReturnAfterRecoverMinutes()
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var flow = new StoryFlow(Kit.D, Kit.Source, clock);
            var s = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(s, new SortieRequest { StepId = "step01", Seed = 1, Delegate = true });
            Assert.True(plan.Ok);
            Assert.True(flow.Confirm(s, plan));
            flow.ApplyResult(s, plan, BattleSimulation.Run(plan.Setup));

            var hurt = s.Units.Where(u => u.Wounded > 0).ToList();
            Assert.NotEmpty(hurt);
            long minutes = Kit.D.Balance.WoundedRecoverMinutes;
            Assert.Equal(30, minutes);
            var before = hurt.ToDictionary(u => u.UnitId, u => (u.Troops, u.Wounded));

            clock.Advance(TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(1));
            flow.Tick(s);
            Assert.All(hurt, u => Assert.Equal(before[u.UnitId].Wounded, u.Wounded));

            clock.Advance(TimeSpan.FromSeconds(1));
            flow.Tick(s);
            foreach (var u in hurt)
            {
                Assert.Equal(0, u.Wounded);
                Assert.Equal(before[u.UnitId].Troops + before[u.UnitId].Wounded, u.Troops);
                Assert.Equal(0, u.WoundedRecoverAtUnixSeconds);
            }
        }

        [Fact]
        public void NewWoundedResetsTimer()
        {
            var u = new Game.Campaign.Roster.UnitState { UnitId = "u01", Troops = 1000, Wounded = 0 };
            var b = Kit.D.Balance;
            CasualtyService.Apply(b, Kit.D.ResolveFlags(), u, 100);
            CasualtyService.ScheduleRecovery(b, u, 1000);
            Assert.Equal(1000 + 30 * 60, u.WoundedRecoverAtUnixSeconds);
            Assert.Equal(0, CasualtyService.RecoverDue(u, 1000 + 20 * 60));
            CasualtyService.Apply(b, Kit.D.ResolveFlags(), u, 100); // 20분 뒤 또 부상 → 시각을 다시 잡는다
            CasualtyService.ScheduleRecovery(b, u, 1000 + 20 * 60);
            Assert.Equal(0, CasualtyService.RecoverDue(u, 1000 + 30 * 60));
            Assert.Equal(80, CasualtyService.RecoverDue(u, 1000 + 50 * 60));
            Assert.Equal(880, u.Troops);
        }

        [Fact(DisplayName = "R-61 보충: 한도 안에서 가장 적은 부대부터 고르게 채운다 (앞줄 칸이 비지 않는다)")]
        public void RefillLevelsLowestFirst()
        {
            var s = StartingKitService.CreateNewGame(Kit.D);
            var comp = s.Compositions[0];
            var units = comp.Slots.Values.Select(id => s.FindUnit(id)).ToList();
            foreach (var u in units) u.Troops = 0; // 모두 잃음
            var svc = new TroopService(Kit.D);
            var plan = svc.RefillPlan(s).Where(p => units.Any(u => u.UnitId == p.Key)).ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal(units.Count, plan.Count);
            long min = plan.Values.Min(), max = plan.Values.Max();
            Assert.True(max - min <= 1, $"고르지 않음: {min}~{max}");
            var vanguard = s.FindUnit(comp.Slots["vanguard"]);
            Assert.Equal(max, plan[vanguard.UnitId]); // 남는 한 명은 앞줄부터

            // 한 부대만 많이 남아 있으면 나머지를 먼저 채운다
            foreach (var u in units) u.Troops = 100;
            var big = units.First(u => u != vanguard);
            big.Troops = 4000;
            var plan2 = svc.RefillPlan(s).Where(p => units.Any(u => u.UnitId == p.Key)).ToDictionary(p => p.Key, p => p.Value);
            Assert.True(plan2.GetValueOrDefault(vanguard.UnitId) > plan2.GetValueOrDefault(big.UnitId));
        }

        [Fact]
        public void HealingAllClearsTimer()
        {
            var u = new Game.Campaign.Roster.UnitState { UnitId = "u01", Troops = 900, Wounded = 40, WoundedRecoverAtUnixSeconds = 5000 };
            CasualtyService.Heal(u, 40);
            Assert.Equal(0, u.WoundedRecoverAtUnixSeconds);
        }
    }
}
