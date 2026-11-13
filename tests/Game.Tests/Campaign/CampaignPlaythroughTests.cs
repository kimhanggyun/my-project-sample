using System;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Flow;
using Game.Foundation.Time;
using Xunit;
using Xunit.Abstractions;

namespace Game.Tests.Campaign
{
    /// <summary>
    /// 혼자 하는 진행이 막히지 않는지 (R-61): 새 게임에서 위임 출진만으로, 전투 사이에 자연 회복 시간을 두고
    /// 편성 한도까지 징병 보충(<see cref="TroopService.RefillPlan"/>)하면 모든 스텝을 깬다. 데이터(스텝·수치)를 바꿔 막히면 여기서 알 수 있다.
    /// </summary>
    public class CampaignPlaythroughTests
    {
        private const int TriesPerStep = 5;
        private readonly ITestOutputHelper _out;
        public CampaignPlaythroughTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void DelegatedPlayClearsAllStepsWithRecoveryAndRefill()
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var flow = new StoryFlow(Kit.D, Kit.Source, clock);
            var troops = new TroopService(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            ulong seed = 1;
            foreach (var id in Kit.D.StepIndex.Order)
            {
                for (int t = 0; t < TriesPerStep && !s.ClearedSteps.Contains(id); t++)
                {
                    clock.Advance(TimeSpan.FromMinutes(Kit.D.Balance.WoundedRecoverMinutes));
                    flow.Tick(s);
                    // 다친 장수(일기토 중상 등)가 나을 때까지 기다린다 (플레이어도 그렇게 한다)
                    long healAt = 0;
                    foreach (var g in s.Generals)
                        if (g.Status == GeneralStatus.LightInjury || g.Status == GeneralStatus.SeriousInjury) healAt = Math.Max(healAt, g.StatusUntilUnixSeconds);
                    long now = clock.UtcNow.ToUnixTimeSeconds();
                    if (healAt > now) { clock.Advance(TimeSpan.FromSeconds(healAt - now)); flow.Tick(s); }
                    foreach (var p in troops.RefillPlan(s)) troops.Conscript(s, p.Key, p.Value);
                    var plan = flow.Prepare(s, new SortieRequest { StepId = id, Seed = seed++, Delegate = true });
                    if (!plan.Ok && !flow.IsUnlocked(s, id)) break; // 선택 스텝(구출 등)은 조건이 맞을 때만
                    Assert.True(plan.Ok, id + ": " + string.Join("; ", plan.Errors));
                    Assert.True(flow.Confirm(s, plan));
                    var r = flow.ApplyResult(s, plan, BattleSimulation.Run(plan.Setup));
                    _out.WriteLine($"{id} try{t} won {r.Won}");
                }
            }
            foreach (var id in Kit.D.StepIndex.Order)
                if (flow.IsUnlocked(s, id) || s.ClearedSteps.Contains(id)) Assert.Contains(id, s.ClearedSteps);
            Assert.Contains("step12", s.ClearedSteps);
        }
    }
}
