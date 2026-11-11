using System;
using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Captivity;
using Game.Campaign.Progress;
using Game.Campaign.Roster;
using Game.Campaign.Save;
using Game.Campaign.Troops;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Data.Validation;
using Game.Economy;
using Game.Flow;
using Game.Foundation.Math;
using Game.Foundation.Random;
using Game.Foundation.Save;
using Game.Foundation.Time;
using Game.Progression;
using Xunit;
using static Game.Tests.TestData;

namespace Game.Tests.Campaign
{
    internal static class Kit
    {
        private static DefinitionSet _d;
        public static DefinitionSet D => _d ?? (_d = Real());
        public static FileDataSource Source => new FileDataSource(DataRoot);
        public static ManualTimeProvider Clock() => new ManualTimeProvider(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>스텝 데이터·해금 (story.md 1장, 2장).</summary>
    public class StepTests
    {
        [Fact(DisplayName = "스텝 데이터는 검사를 통과한다 (순서·구출 전투 모두)")]
        public void StepsValid()
        {
            var ids = Kit.D.StepIndex.Order.Concat(Kit.D.StepIndex.RescueSteps.Values);
            foreach (var id in ids)
            {
                var s = StepDef.Load(Kit.Source, id);
                Assert.Empty(DataValidator.ValidateStep(Kit.D, s, MapDef.Load(Kit.Source, s.Map)));
            }
        }

        [Fact(DisplayName = "차례 해금: 첫 스텝은 열려 있고, 앞 스텝을 깨야 다음이 열린다. 깬 스텝은 다시 할 수 있다")]
        public void Unlock()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var none = new List<string>();
            Assert.True(StepProgress.IsUnlocked(Kit.D.StepIndex, state, "step01", none));
            Assert.False(StepProgress.IsUnlocked(Kit.D.StepIndex, state, "step02", none));
            Assert.True(StepProgress.MarkCleared(state, "step01"));
            Assert.False(StepProgress.MarkCleared(state, "step01")); // 두 번째는 처음이 아님
            Assert.True(StepProgress.IsUnlocked(Kit.D.StepIndex, state, "step02", none));
            Assert.True(StepProgress.IsUnlocked(Kit.D.StepIndex, state, "step01", none));
        }

        [Fact(DisplayName = "구출 전투는 그 수용소에 아군 장수가 갇혀 있을 때만 열린다")]
        public void RescueUnlock()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            Assert.False(StepProgress.IsUnlocked(Kit.D.StepIndex, state, "rescue_camp_north", new List<string>()));
            Assert.True(StepProgress.IsUnlocked(Kit.D.StepIndex, state, "rescue_camp_north", new List<string> { "camp_north" }));
        }

        [Fact(DisplayName = "탐색 기억: 새로 탐색한 칸을 합치고, 탐색한 칸은 줄지 않는다")]
        public void ExploredMerge()
        {
            var state = new CampaignState();
            StepProgress.MergeExplored(state, "s", new[] { true, false, false });
            StepProgress.MergeExplored(state, "s", new[] { false, false, true });
            Assert.Equal(new[] { true, false, true }, StepProgress.Explored(state, "s", 3));
        }
    }

    /// <summary>출진 → 전투 → 결과 반영 (story.md 4·5장, flow.md 3장).</summary>
    public class StoryFlowTests
    {
        private static StoryFlow Flow(ManualTimeProvider clock = null) => new StoryFlow(Kit.D, Kit.Source, clock ?? Kit.Clock());

        [Fact(DisplayName = "출진 준비: 편성 3군단 + 스텝 적군, 군량 자동 계산(R-7), 탐색 기억 전달")]
        public void Prepare()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var plan = Flow().Prepare(state, new SortieRequest { StepId = "step01", Seed = 1 });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.Equal(3, plan.Setup.Corps.Count(c => c.Team == StoryFlow.PlayerTeam));
            Assert.Equal(StepDef.Load(Kit.Source, "step01").Enemy.Count, plan.Setup.Corps.Count(c => c.Team == StoryFlow.EnemyTeam));
            long daily = state.Units.Sum(u => u.Troops * Kit.D.TroopTypes[u.TroopType].SupplyPerDay);
            Assert.Equal(SupplyCalculator.AutoSortieSupply(Kit.D.Balance, daily), plan.AutoSupply);
            Assert.Equal(plan.SupplyTaken, plan.Setup.Corps.Where(c => c.Team == StoryFlow.PlayerTeam).Sum(c => c.Supply));
            Assert.True(plan.Setup.InitialExplored.ContainsKey(StoryFlow.PlayerTeam));
        }

        [Fact(DisplayName = "출진 준비 오류: 잠긴 스텝, 군단 수 초과, 군량 부족, 출전 불가 장수")]
        public void PrepareErrors()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            Assert.Contains(Flow().Prepare(state, new SortieRequest { StepId = "step02" }).Errors, e => e.Contains("열리지"));
            Assert.Contains(Flow().Prepare(state, new SortieRequest { StepId = "step01", Supply = 999_999_999 }).Errors, e => e.Contains("군량이 모자"));
            state.Generals[0].Status = GeneralStatus.SeriousInjury;
            Assert.Contains(Flow().Prepare(state, new SortieRequest { StepId = "step01" }).Errors, e => e.Contains("출전할 수 없는"));
            var four = new List<CorpsComposition>(state.Compositions) { state.Compositions[0] };
            Assert.Contains(Flow().Prepare(StartingKitService.CreateNewGame(Kit.D), new SortieRequest { StepId = "step01", Compositions = four }).Errors, e => e.Contains("한도"));
        }

        [Fact(DisplayName = "한 판 전체: 출진 → 전투 → 손실·부상(R-9)·군량 반환·포획(R-8)·결속·클리어 보상, 두 번째 클리어는 보상 없음")]
        public void FullLoop()
        {
            var clock = Kit.Clock();
            var flow = Flow(clock);
            var state = StartingKitService.CreateNewGame(Kit.D);
            long supply0 = state.Wallet[ResourceIds.Supply], gold0 = state.Wallet[ResourceIds.Gold];
            var troops0 = state.Units.ToDictionary(u => u.UnitId, u => u.Troops);

            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 5, Delegate = true });
            Assert.True(flow.Confirm(state, plan));
            Assert.Equal(supply0 - plan.SupplyTaken, state.Wallet[ResourceIds.Supply]);

            var result = BattleSimulation.Run(plan.Setup);
            var report = flow.ApplyResult(state, plan, result);
            Assert.True(report.Won, "초기 편성(위임 출진)이 step01에서 졌습니다 (목표 승률 90~95%, 이 시드는 승).");
            Assert.True(report.FirstClear);

            foreach (var ur in report.Units)
            {
                var unit = state.FindUnit(ur.UnitId);
                Assert.Equal(troops0[ur.UnitId] - ur.Losses, unit.Troops);
                Assert.Equal((Kit.D.Balance.WoundedRatio * ur.Losses).FloorToLong(), ur.Wounded);
                Assert.Equal(ur.Wounded, unit.Wounded);
                Assert.Equal(Kit.D.Campaign.CohesionWinGain, unit.Cohesion);
            }
            long supplyReward = plan.Step.RewardResources.TryGetValue(ResourceIds.Supply, out var sr) ? sr : 0; // 첫 클리어 군량 보상
            Assert.Equal(supply0 - plan.SupplyTaken + report.SupplyReturned + report.SupplyCaptured + supplyReward, state.Wallet[ResourceIds.Supply]);
            Assert.True(report.SupplyCaptured > 0, "이겼는데 포획한 군량이 없습니다.");
            Assert.Contains("fishScale", state.LearnedFormations);
            Assert.Equal(gold0 + 200, state.Wallet[ResourceIds.Gold]);
            Assert.Contains("step01", state.ClearedSteps);
            Assert.Contains('1', state.ExploredBySteps["step01"]);
            Assert.True(flow.IsUnlocked(state, "step02"));

            var plan2 = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 6, Delegate = true });
            Assert.True(flow.Confirm(state, plan2), string.Join("\n", plan2.Errors));
            var report2 = flow.ApplyResult(state, plan2, BattleSimulation.Run(plan2.Setup));
            Assert.False(report2.FirstClear);
            Assert.Empty(report2.RewardResources);
            Assert.Equal(gold0 + 200, state.Wallet[ResourceIds.Gold]);
        }

        [Fact(DisplayName = "패배: 손실은 반영, 남은 군량은 돌려받고 포획 없음, 클리어 없음, 결속 감소")]
        public void Loss()
        {
            var flow = Flow();
            var state = StartingKitService.CreateNewGame(Kit.D);
            foreach (var u in state.Units) u.Troops = 50; // 아주 약하게
            foreach (var u in state.Units) u.Cohesion = Fix.FromInt(5);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 2 });
            Assert.True(flow.Confirm(state, plan), string.Join("\n", plan.Errors));
            var report = flow.ApplyResult(state, plan, BattleSimulation.Run(plan.Setup));
            Assert.False(report.Won);
            Assert.Equal(0, report.SupplyCaptured);
            Assert.DoesNotContain("step01", state.ClearedSteps);
            Assert.All(report.Units, u => Assert.Equal(Fix.FromInt(5) - Kit.D.Campaign.CohesionLossPenalty, u.CohesionAfter));
        }

        [Fact(DisplayName = "결과 반영은 결정적: 같은 시드 → 같은 상태")]
        public void Deterministic()
        {
            string Run()
            {
                var flow = Flow();
                var state = StartingKitService.CreateNewGame(Kit.D);
                var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 9 });
                flow.Confirm(state, plan);
                flow.ApplyResult(state, plan, BattleSimulation.Run(plan.Setup));
                var store = new InMemorySaveStore();
                new CampaignSaveService(store).Save(state);
                return store.Read(CampaignSaveService.DefaultKey);
            }
            Assert.Equal(Run(), Run());
        }
    }

    /// <summary>포로·구출·등용 (captivity.md).</summary>
    public class CaptivityTests
    {
        private static CaptivityService Svc(ManualTimeProvider clock, string flags = null) => new CaptivityService(Kit.D, Flags(Kit.D, flags), clock);

        [Fact(DisplayName = "아군 장수 포로: 출전 불가, 구출 기한 30일 지나면 자동 석방")]
        public void OwnCapturedDeadline()
        {
            var clock = Kit.Clock();
            var state = StartingKitService.CreateNewGame(Kit.D);
            var svc = Svc(clock);
            svc.CaptureOwn(state, "general_a", "camp_north");
            var g = state.FindGeneral("general_a");
            Assert.Equal(GeneralStatus.Captured, g.Status);
            Assert.False(GeneralStatus.CanDeploy(g.Status));
            Assert.Equal(new List<string> { "camp_north" }, svc.CampsWithCaptives(state));
            clock.Advance(TimeSpan.FromDays(29));
            Assert.Empty(svc.ReleaseExpired(state));
            clock.Advance(TimeSpan.FromDays(2));
            Assert.Equal(new List<string> { "general_a" }, svc.ReleaseExpired(state));
            Assert.Equal(GeneralStatus.Available, g.Status);
        }

        [Fact(DisplayName = "구출 기한 스위치를 끄면 구출해야만 돌아오고, 구출 전투 승리로 되찾는다")]
        public void RescueOnly()
        {
            var clock = Kit.Clock();
            var state = StartingKitService.CreateNewGame(Kit.D);
            var svc = Svc(clock, "{\"rescueDeadline\": false}");
            svc.CaptureOwn(state, "general_b", "camp_north");
            clock.Advance(TimeSpan.FromDays(60));
            Assert.Empty(svc.ReleaseExpired(state));
            Assert.Equal(new List<string> { "general_b" }, svc.Rescue(state, "camp_north"));
            Assert.Equal(GeneralStatus.Available, state.FindGeneral("general_b").Status);
        }

        [Fact(DisplayName = "포로 스위치(capture)를 끄면 잡히지 않는다")]
        public void CaptureOff()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            Svc(Kit.Clock(), "{\"capture\": false}").CaptureOwn(state, "general_a", "camp_north");
            Assert.Equal(GeneralStatus.Available, state.FindGeneral("general_a").Status);
        }

        [Fact(DisplayName = "등용 성공률 = 기본 + 성향 + 세뇌 보너스")]
        public void RecruitChance()
        {
            var svc = Svc(Kit.Clock());
            var r = Kit.D.Campaign;
            var p = new PrisonerState { GeneralId = "enemy_general_01" }; // 오만
            Assert.Equal(r.RecruitBaseChance + r.TemperamentRecruitBonus["arrogant"], svc.RecruitChance(p));
            p.BrainwashBonus = true;
            Assert.Equal(r.RecruitBaseChance + r.TemperamentRecruitBonus["arrogant"] + r.BrainwashBonus, svc.RecruitChance(p));
        }

        [Fact(DisplayName = "등용: 실패하면 계속 감금되고 재시도 대기 3시간, 성공하면 아군 장수(기본 스킬 포함)")]
        public void RecruitRetry()
        {
            var clock = Kit.Clock();
            var state = StartingKitService.CreateNewGame(Kit.D);
            var svc = Svc(clock);
            svc.AddPrisoner(state, "enemy_general_01", false);
            var rng = new SeededRandom(1);
            int attempts = 0;
            RecruitOutcome o;
            do
            {
                o = svc.TryRecruit(state, "enemy_general_01", rng);
                attempts++;
                if (o == RecruitOutcome.FailedKept)
                {
                    Assert.False(svc.CanAttemptRecruit(state, "enemy_general_01"));
                    Assert.Equal(RecruitOutcome.NotAllowed, svc.TryRecruit(state, "enemy_general_01", rng));
                    clock.Advance(TimeSpan.FromHours(3));
                }
            } while (o != RecruitOutcome.Recruited && attempts < 100);
            Assert.Equal(RecruitOutcome.Recruited, o);
            var g = state.FindGeneral("enemy_general_01");
            Assert.NotNull(g);
            Assert.Contains("warCry", g.LearnedSkills);
            Assert.Null(state.FindPrisoner("enemy_general_01"));
        }

        [Fact(DisplayName = "등용 불가 장수·등용 스위치 꺼짐 → 시도할 수 없고 해방만")]
        public void NotRecruitable()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var svc = Svc(Kit.Clock());
            svc.AddPrisoner(state, "enemy_general_03", false); // recruitable: false
            Assert.Equal(RecruitOutcome.NotAllowed, svc.TryRecruit(state, "enemy_general_03", new SeededRandom(1)));
            var off = Svc(Kit.Clock(), "{\"recruitment\": false}");
            off.AddPrisoner(state, "enemy_general_01", false);
            Assert.Equal(RecruitOutcome.NotAllowed, off.TryRecruit(state, "enemy_general_01", new SeededRandom(1)));
        }

        [Fact(DisplayName = "재시도 스위치를 끄면 실패 시 해방, 해방하면 보상 (스위치를 끄면 보상 없음)")]
        public void ReleaseAndReward()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            long gold = state.Wallet[ResourceIds.Gold];
            var svc = Svc(Kit.Clock(), "{\"recruitRetry\": false}");
            svc.AddPrisoner(state, "enemy_general_01", false);
            var rng = new SeededRandom(3);
            RecruitOutcome o;
            do
            {
                if (state.FindPrisoner("enemy_general_01") == null) svc.AddPrisoner(state, "enemy_general_01", false);
                if (state.FindGeneral("enemy_general_01") != null) state.Generals.RemoveAll(x => x.GeneralId == "enemy_general_01");
                o = svc.TryRecruit(state, "enemy_general_01", rng);
            } while (o != RecruitOutcome.FailedReleased);
            Assert.Null(state.FindPrisoner("enemy_general_01"));
            Assert.True(state.Wallet[ResourceIds.Gold] > gold);

            var s2 = StartingKitService.CreateNewGame(Kit.D);
            var noReward = Svc(Kit.Clock(), "{\"releaseReward\": false}");
            noReward.AddPrisoner(s2, "enemy_general_02", false);
            Assert.Empty(noReward.Release(s2, "enemy_general_02"));
            Assert.Equal(StartingKitService.CreateNewGame(Kit.D).Wallet[ResourceIds.Gold], s2.Wallet[ResourceIds.Gold]);
        }
    }

    /// <summary>장수 부상·부상병 (battle.md 2.5.3, R-9).</summary>
    public class InjuryCasualtyTests
    {
        [Fact(DisplayName = "중상은 출전 불가, 경상은 능력치 × 0.9, 시간이 지나면 회복, 돈으로 즉시 치료")]
        public void Injuries()
        {
            var clock = Kit.Clock();
            var state = StartingKitService.CreateNewGame(Kit.D);
            var svc = new InjuryService(Kit.D, Flags(Kit.D), clock);
            var a = state.FindGeneral("general_a");
            Fix lead = CorpsValidator.EffectiveStat(Kit.D, a, "leadership");
            svc.Apply(state, "general_a", InjuryKinds.Light);
            Assert.True(GeneralStatus.CanDeploy(a.Status));
            Assert.Equal(lead * Kit.D.Balance.LightInjuryStatMultiplier, CorpsValidator.EffectiveStat(Kit.D, a, "leadership"));
            svc.Apply(state, "general_a", InjuryKinds.Heavy);
            Assert.False(GeneralStatus.CanDeploy(a.Status));
            svc.Apply(state, "general_a", InjuryKinds.Light); // 중상을 경상으로 덮어쓰지 않음
            Assert.Equal(GeneralStatus.SeriousInjury, a.Status);
            Assert.Equal(6 * Kit.D.Campaign.HealGoldPerHourLeft, svc.HealCost(a));
            clock.Advance(TimeSpan.FromHours(6));
            svc.Recover(state);
            Assert.Equal(GeneralStatus.Available, a.Status);

            svc.Apply(state, "general_b", InjuryKinds.Heavy);
            long gold = state.Wallet[ResourceIds.Gold];
            Assert.True(svc.HealNow(state, "general_b"));
            Assert.Equal(GeneralStatus.Available, state.FindGeneral("general_b").Status);
            Assert.Equal(gold - 6 * Kit.D.Campaign.HealGoldPerHourLeft, state.Wallet[ResourceIds.Gold]);
        }

        [Fact(DisplayName = "부상 스위치를 끄면 부상 없음")]
        public void InjuryOff()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            new InjuryService(Kit.D, Flags(Kit.D, "{\"generalInjury\": false}"), Kit.Clock()).Apply(state, "general_a", InjuryKinds.Heavy);
            Assert.Equal(GeneralStatus.Available, state.FindGeneral("general_a").Status);
        }

        [Fact(DisplayName = "N-9 손실 1,000 → 부상 400·사망 600, 치료하면 병력으로 돌아온다")]
        public void Casualties()
        {
            var unit = new UnitState { UnitId = "u", TroopType = "infantry", Troops = 3000 };
            var (wounded, dead) = CasualtyService.Apply(Kit.D.Balance, Flags(Kit.D), unit, 1000);
            Assert.Equal((400L, 600L), (wounded, dead));
            Assert.Equal(2000, unit.Troops);
            Assert.Equal(400, unit.Wounded);
            Assert.Equal(400, CasualtyService.Heal(unit, 1000));
            Assert.Equal(2400, unit.Troops);
            Assert.Equal(0, unit.Wounded);
        }
    }

    /// <summary>성장: 기록 카운터·보상 규칙 엔진, 부대 결속 (general-growth.md 2.3, battle.md 2.19.1).</summary>
    public class GrowthTests
    {
        [Fact(DisplayName = "일기토 100승마다 무력 +1, 승패 차 50 이상이면 특성 한 번만")]
        public void Rewards()
        {
            var counters = new Dictionary<string, long>();
            var granted = new Dictionary<string, long>();
            var flags = Flags(Kit.D);
            for (int i = 0; i < 99; i++) GrowthEngine.RecordContest(counters, "duel", "win");
            var at99 = GrowthEngine.Evaluate(Kit.D.GrowthTracks, flags, counters, granted);
            Assert.DoesNotContain(at99, r => r.Stat == "might");
            Assert.Single(at99, r => r.Trait == "fierce"); // 승패 차 99 ≥ 50
            GrowthEngine.RecordContest(counters, "duel", "win");
            var rewards = GrowthEngine.Evaluate(Kit.D.GrowthTracks, flags, counters, granted);
            Assert.Single(rewards, r => r.Stat == "might" && r.Add == Fix.FromInt(1));
            Assert.DoesNotContain(rewards, r => r.Trait == "fierce"); // 한 번만
            Assert.Empty(GrowthEngine.Evaluate(Kit.D.GrowthTracks, flags, counters, granted)); // 두 번 주지 않음
            Assert.Equal(100, counters["duelStreak"]);
            GrowthEngine.RecordContest(counters, "duel", "loss");
            Assert.Equal(0, counters["duelStreak"]);
            Assert.Equal(99, counters["duelWinLossDiff"]);
        }

        [Fact(DisplayName = "성장 스위치(duelGrowth)를 끄면 보상 없음")]
        public void GrowthOff()
        {
            var counters = new Dictionary<string, long> { ["duelWins"] = 300 };
            Assert.Empty(GrowthEngine.Evaluate(Kit.D.GrowthTracks, Flags(Kit.D, "{\"duelGrowth\": false}"), counters, new Dictionary<string, long>()));
        }

        [Fact(DisplayName = "결속: 승리 +, 패배 −, 0~최대, 스위치를 끄면 그대로")]
        public void CohesionRules()
        {
            var r = Kit.D.Campaign;
            var on = Flags(Kit.D);
            Assert.Equal(r.CohesionWinGain, Cohesion.After(r, on, Fix.Zero, true));
            Assert.Equal(Fix.Zero, Cohesion.After(r, on, Fix.Zero, false));
            Assert.Equal(r.CohesionMax, Cohesion.After(r, on, r.CohesionMax, true));
            Assert.Equal(Fix.FromInt(7), Cohesion.After(r, Flags(Kit.D, "{\"cohesionGain\": false}"), Fix.FromInt(7), true));
        }
    }

    public class SaveV2Tests
    {
        [Fact(DisplayName = "v1 세이브는 v2로 바뀌고 새 칸은 빈 값으로 채워진다")]
        public void V1ToV2()
        {
            var store = new InMemorySaveStore();
            store.Write("campaign", "{\"version\":1,\"campaign\":{\"Generals\":[{\"GeneralId\":\"general_a\"}],\"Wallet\":{\"supply\":5}}}");
            var loaded = new CampaignSaveService(store).Load();
            Assert.Equal(5, loaded.Wallet["supply"]);
            Assert.Empty(loaded.Prisoners);
            Assert.Empty(loaded.ExploredBySteps);
            Assert.Empty(loaded.Generals[0].Records);
        }

        [Fact(DisplayName = "포로·탐색 기억·기록이 세이브에 남는다")]
        public void RoundTripNewFields()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            state.Prisoners.Add(new PrisonerState { GeneralId = "enemy_general_01", BrainwashBonus = true, CapturedAtUnixSeconds = 10 });
            state.ExploredBySteps["step01"] = "0101";
            state.Generals[0].Records["duelWins"] = 3;
            var svc = new CampaignSaveService(new InMemorySaveStore());
            svc.Save(state);
            var loaded = svc.Load();
            Assert.True(loaded.Prisoners[0].BrainwashBonus);
            Assert.Equal("0101", loaded.ExploredBySteps["step01"]);
            Assert.Equal(3, loaded.Generals[0].Records["duelWins"]);
        }
    }
}
