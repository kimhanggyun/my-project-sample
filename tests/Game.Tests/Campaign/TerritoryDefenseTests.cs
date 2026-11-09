using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Online;
using Game.Services;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>수비 편성 (territory.md 5장, verification R-48): 주인이 등록한 편성과 싸운다, 판이 같아야 서버가 받는다.</summary>
    public class TerritoryDefenseTests
    {
        private static readonly string Here = FakeLocationService.Default.ToString();

        [Fact(DisplayName = "R-48 수비 스냅샷: 주인 세이브의 편성(검사 통과)에서, 장수는 출전 가능 상태로, 부대는 부상병 없이")]
        public void Snapshot()
        {
            var owner = StartingKitService.CreateNewGame(Kit.D);
            owner.Units[0].Wounded = 50;
            owner.Generals[0].Status = Game.Campaign.Roster.GeneralStatus.LightInjury;
            var d = TerritoryDefenses.Snapshot(Kit.D, owner, null, out var error);
            Assert.Null(error);
            Assert.Equal(owner.Compositions.Count, d.Corps.Count);
            Assert.Equal(owner.Compositions[0].Slots.Count, d.Corps[0].Units.Count);
            Assert.DoesNotContain("lightInjury", d.Corps[0].GeneralJson);
            Assert.DoesNotContain(d.Corps.SelectMany(c => c.Units), u => u.UnitJson.Contains("\"Wounded\":50"));

            var empty = StartingKitService.CreateNewGame(Kit.D);
            empty.Compositions.Clear();
            Assert.Null(TerritoryDefenses.Snapshot(Kit.D, empty, null, out var e2));
            Assert.Equal(TerritoryDefenses.BadFormation, e2);
        }

        [Fact(DisplayName = "R-48 수비 편성과 싸우기: 적 군단 = 주인 편성(같은 장수가 있어도), 결과는 내 장수에게만, 수비 장수는 포로로 오지 않는다")]
        public void FightOwnerDefense()
        {
            var defense = TerritoryDefenses.Snapshot(Kit.D, StartingKitService.CreateNewGame(Kit.D), null, out _);
            defense.Version = 3;
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var state = StartingKitService.CreateNewGame(Kit.D);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "territory.dev_zone_a1", Seed = 3, Delegate = true, Location = Here, Defense = defense });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            var enemies = plan.Setup.Corps.Where(c => c.Team == StoryFlow.EnemyTeam).ToList();
            Assert.Equal(new[] { "D1", "D2", "D3" }, enemies.Select(c => c.Id));
            Assert.Equal(new[] { "general_a", "general_b", "general_c" }, enemies.Select(c => c.General.Id));
            Assert.All(enemies, c => Assert.Equal(Game.Battle.Model.Ai.AiStanceIds.Defensive, c.AiStance));
            Assert.Same(defense, plan.Defense);

            Assert.True(flow.Confirm(state, plan));
            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            var report = flow.ApplyResult(state, plan, sim.Result);
            Assert.Empty(report.NewPrisoners);
            Assert.Empty(state.Prisoners);
            Assert.Equal(3, CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, 1).DefenseVersion);
        }

        [Fact(DisplayName = "R-48 장부: 주인만 등록, 다시 등록하면 판 +1, 빼앗기면 수비 편성은 사라진다")]
        public void Ledger()
        {
            var owners = new Dictionary<string, TerritoryOwner>();
            var d = TerritoryDefenses.Snapshot(Kit.D, StartingKitService.CreateNewGame(Kit.D), null, out _);
            Assert.Null(TerritoryLedger.RegisterDefense(owners, "dev_zone_a1", "A", d, 0)); // 주인 없음
            TerritoryLedger.Capture(null, owners, "dev_zone_a1", "A", 0, (_, __) => { });
            Assert.Null(TerritoryLedger.RegisterDefense(owners, "dev_zone_a1", "B", d, 0));
            Assert.Equal(1, TerritoryLedger.RegisterDefense(owners, "dev_zone_a1", "A", d, 10).Version);
            Assert.Equal(2, TerritoryLedger.RegisterDefense(owners, "dev_zone_a1", "A", TerritoryDefenses.Snapshot(Kit.D, StartingKitService.CreateNewGame(Kit.D), null, out _), 20).Version);
            Assert.NotNull(TerritoryLedger.DefenseOf(owners, "dev_zone_a1"));
            TerritoryLedger.Capture(null, owners, "dev_zone_a1", "B", 30, (_, __) => { });
            Assert.Null(TerritoryLedger.DefenseOf(owners, "dev_zone_a1"));
        }

        [Fact(DisplayName = "R-48 가짜 서버: 차지 → 수비 등록 → 그 편성과 싸운 제출은 받고, 판이 다르면 거절")]
        public void FakeVersionCheck()
        {
            var clock = new ServerClock(Kit.Clock());
            var fake = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var sync = new CloudSync(fake, clock);
            SyncResult start = null;
            sync.Start(null, 0, r => start = r);
            var flow = new StoryFlow(Kit.D, Kit.Source, clock);

            SyncResult Fight(CampaignState from, long rev, TerritoryDefense defense, ulong seed, System.Action<BattleSubmission> tamper = null)
            {
                var state = SaveJson.Read(SaveJson.Write(from));
                var plan = flow.Prepare(state, new SortieRequest { StepId = "territory.dev_zone_a1", Seed = seed, Delegate = true, Location = Here, Defense = defense });
                Assert.True(flow.Confirm(state, plan));
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                flow.ApplyResult(state, plan, sim.Result);
                var sub = CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, rev);
                tamper?.Invoke(sub);
                SyncResult r = null;
                sync.SubmitBattle(sub, state, x => r = x);
                return r;
            }

            DefenseResponse reg = null;
            fake.RegisterDefense(new DefenseRequest { RegionId = "dev_zone_a1" }, x => reg = x);
            Assert.Equal(TerritoryDefenses.NotOwner, reg.Reason); // 아직 내 지역 아님

            SyncResult cur = start;
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var r = Fight(cur.State, cur.Revision, null, seed);
                cur = r;
                TerritoryResponse v0 = null;
                fake.GetTerritory(x => v0 = x);
                if (v0.Owners.Count > 0) break;
            }
            fake.RegisterDefense(new DefenseRequest { RegionId = "dev_zone_a1" }, x => reg = x);
            Assert.True(reg.Ok);
            TerritoryResponse view = null;
            fake.GetTerritory(x => view = x);
            var defense = view.Owners.Single().Defense;
            Assert.Equal(1, defense.Version);

            var ok = Fight(cur.State, cur.Revision, defense, 20);
            Assert.Equal(SyncOutcomes.Accepted, ok.Outcome);
            var stale = Fight(ok.State, ok.Revision, null, 21); // 수비 편성이 있는데 AI 수비군(판 0)으로 싸움
            Assert.Equal(SyncOutcomes.Rejected, stale.Outcome);
            Assert.Equal(VerdictReasons.BadSortie, stale.Verdict.Reason);
        }
    }
}
