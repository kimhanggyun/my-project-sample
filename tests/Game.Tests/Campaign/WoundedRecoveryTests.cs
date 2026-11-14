using System;
using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
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
            var s = Kit.Veteran();
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
            var u = new Game.Campaign.Roster.UnitState { UnitId = "infantry", Troops = 1000, Wounded = 0 };
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

        [Fact(DisplayName = "R-61/R-94 보충: 편성에 적힌 병종 인원 합까지 (부상병 포함), 계급의 병종 최대 병력을 넘지 않는다")]
        public void RefillLevelsLowestFirst()
        {
            var s = Kit.Veteran(); // 보병: 군단 셋에 4,000 + 3,000 + 3,500 = 10,500
            var inf = Army.Pool(s, "infantry");
            inf.Troops = 6000;
            inf.Wounded = 500;
            var svc = new TroopService(Kit.D);
            var plan = svc.RefillPlan(s).ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal(10_500 - 6000 - 500, plan["infantry"]);
            Assert.DoesNotContain("spearman", plan.Keys); // 이미 편성 인원만큼 있다
            // 계급이 낮으면 병종 최대 병력까지만 (Lv6 천부장 8,000, 군단 2개만 셈 → 보병 7,000)
            s.LordLevel = 6;
            plan = svc.RefillPlan(s).ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal(System.Math.Min(4000 + 3000, 8000) - 6500, plan["infantry"]);
        }

        [Fact]
        public void HealingAllClearsTimer()
        {
            var u = new Game.Campaign.Roster.UnitState { UnitId = "infantry", Troops = 900, Wounded = 40, WoundedRecoverAtUnixSeconds = 5000 };
            CasualtyService.Heal(u, 40);
            Assert.Equal(0, u.WoundedRecoverAtUnixSeconds);
        }
    }
}
