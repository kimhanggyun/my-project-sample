using System;
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

        [Fact]
        public void HealingAllClearsTimer()
        {
            var u = new Game.Campaign.Roster.UnitState { UnitId = "u01", Troops = 900, Wounded = 40, WoundedRecoverAtUnixSeconds = 5000 };
            CasualtyService.Heal(u, 40);
            Assert.Equal(0, u.WoundedRecoverAtUnixSeconds);
        }
    }
}
