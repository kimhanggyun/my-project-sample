using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>고용 이력·제한 (hiring.md 3·4장, verification R-44).</summary>
    public class HireBookTests
    {
        [Fact(DisplayName = "R-44 고용 제한: 지난 24시간 고용 수 + 이번 고용 ≤ 하루 상한, 같은 등록은 대기시간(30분) 뒤에")]
        public void Limits()
        {
            var b = Kit.D.Balance;
            long now = 1_000_000;
            var log = new List<HireLogEntry>();
            Assert.Null(HireBook.CheckLimits(b, log, new[] { new HireUse { ListingId = "a" } }, now));
            log.Add(new HireLogEntry { ListingId = "a", AtUnixSeconds = now - 29 * 60 });
            Assert.NotNull(HireBook.CheckLimits(b, log, new[] { new HireUse { ListingId = "a" } }, now)); // 대기 중
            Assert.Null(HireBook.CheckLimits(b, log, new[] { new HireUse { ListingId = "b" } }, now));
            Assert.Null(HireBook.CheckLimits(b, log, new[] { new HireUse { ListingId = "a" } }, now + 60)); // 30분 지남

            var full = Enumerable.Range(0, b.HireDailyMax).Select(i => new HireLogEntry { ListingId = "x" + i, AtUnixSeconds = now - 3600 * 23 }).ToList();
            Assert.NotNull(HireBook.CheckLimits(b, full, new[] { new HireUse { ListingId = "z" } }, now));
            Assert.Null(HireBook.CheckLimits(b, full, new[] { new HireUse { ListingId = "z" } }, now + 3600)); // 24시간 지남
            var (used, cooldowns) = HireBook.Status(b, log, now);
            Assert.Equal(1, used);
            Assert.Equal(now - 29 * 60 + 30 * 60, cooldowns["a"]);
        }

        [Fact(DisplayName = "이력: 결과별 횟수·잃은 병력이 쌓이고, 최근 전투는 새것이 앞에 최대 recentMax개")]
        public void Record()
        {
            var l = new HireListing { Id = "L" };
            for (int i = 0; i < 12; i++)
                HireBook.Record(l, new HireOutcome { ListingId = "L", Result = i % 3 == 0 ? HireResults.Win : HireResults.Retreat, TroopsStart = 100, TroopsLost = 10 }, "step01", 1000 + i, Kit.D.Balance.HireRecentMax);
            Assert.Equal((12L, 4L, 8L, 120L, 1200L), (l.Stats.Hires, l.Stats.Wins, l.Stats.Retreats, l.Stats.TroopsLost, l.Stats.TroopsHired));
            Assert.Equal(10, l.Recent.Count);
            Assert.Equal(1011, l.Recent[0].AtUnixSeconds);
        }

        [Fact(DisplayName = "R-44 가짜 서버: 고용 전투가 맞으면 그 등록에 이력이 쌓이고, 바로 다시 고용하면 대기시간으로 거절")]
        public void FakeHistoryAndCooldown()
        {
            var clock = new ServerClock(Kit.Clock());
            var fake = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var sync = new CloudSync(fake, clock);
            SyncResult start = null;
            sync.Start(null, 0, r => start = r);
            var state = start.State;
            long rev = start.Revision;

            HireListing Unit()
            {
                HireListResponse r = null;
                fake.GetListings(HireKinds.Unit, x => r = x);
                return r.Listings.First(l => l.TroopType == "cavalry");
            }

            SyncResult Play(ulong seed)
            {
                var draft = new HireDraft(Kit.D);
                draft.Picks.Add(new HirePick { Listing = Unit(), CorpsIndex = 0, Count = 200 });
                var (comps, hired) = draft.Build(state, "field", fake.AccountId, out var error);
                Assert.Null(error);
                var flow = new StoryFlow(Kit.D, Kit.Source, clock);
                var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = seed, Delegate = true, Compositions = comps, Hired = hired });
                Assert.True(plan.Ok, string.Join("\n", plan.Errors));
                var copy = SaveJson.Read(SaveJson.Write(state));
                Assert.True(flow.Confirm(copy, plan));
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                flow.ApplyResult(copy, plan, sim.Result);
                SyncResult r = null;
                sync.SubmitBattle(CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, rev), copy, x => r = x);
                return r;
            }

            var first = Play(3);
            Assert.Equal(SyncOutcomes.Accepted, first.Outcome);
            state = first.State;
            rev = first.Revision;
            var after = Unit();
            Assert.Equal(1, after.Stats.Hires);
            Assert.Single(after.Recent);
            Assert.Equal(200, after.Recent[0].TroopsStart);
            Assert.Contains(after.Recent[0].Result, new[] { HireResults.Win, HireResults.Loss, HireResults.Retreat, HireResults.Captured, HireResults.Destroyed });
            HireListResponse status = null;
            fake.GetListings(null, x => status = x);
            Assert.Equal(1, status.DailyUsed);
            Assert.True(status.CooldownUntil.ContainsKey(after.Id));

            var again = Play(4);
            Assert.Equal(SyncOutcomes.Rejected, again.Outcome);
            Assert.Equal(VerdictReasons.HireLimit, again.Verdict.Reason);
        }
    }
}
