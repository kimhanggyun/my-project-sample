using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>출진 전 고용 고르기 (HireDraft)와 가짜 서버의 고용 시장 (online = fake).</summary>
    public class HireDraftTests
    {
        private static System.Collections.Generic.List<HireListing> Market(LocalOnlineService fake)
        {
            HireListResponse r = null;
            fake.GetListings(null, x => r = x);
            return r.Listings;
        }

        [Fact(DisplayName = "가짜 시장: 예시 등록(장수 2·부대 2)이 보이고, 내 것을 등록·갱신·해제할 수 있다")]
        public void FakeMarket()
        {
            var fake = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var market = Market(fake);
            Assert.Equal(2, market.Count(l => l.Kind == HireKinds.General));
            Assert.Equal(2, market.Count(l => l.Kind == HireKinds.Unit));
            Assert.All(market, l => Assert.Equal(LocalOnlineService.NpcAccount, l.OwnerAccountId));

            HireListingResponse reg = null;
            fake.RegisterHire(new HireListingRequest { Kind = HireKinds.Unit, UnitId = "u06", Price = 2, PayWith = "gold" }, x => reg = x);
            Assert.True(reg.Ok);
            fake.RegisterHire(new HireListingRequest { Kind = HireKinds.Unit, UnitId = "u06", Price = 1, PayWith = "gold" }, x => reg = x);
            Assert.Equal(2, reg.Listing.Version); // 같은 부대 → 갱신
            Assert.Equal(5, Market(fake).Count);
            bool? gone = null;
            fake.Unregister(reg.Listing.Id, x => gone = x);
            Assert.True(gone);
            fake.Unregister("npc-1", x => gone = x);
            Assert.False(gone); // 남의 것
        }

        [Fact(DisplayName = "고용 고르기: 장수는 고른 군단의 지휘관으로, 부대는 첫 빈 칸에. 기본 인원은 등록 인원·장수 한도 남은 자리 중 작은 쪽")]
        public void DraftBuild()
        {
            var fake = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var market = Market(fake);
            var general = market.First(l => l.Kind == HireKinds.General && l.GeneralId == "enemy_general_04");
            var unit = market.First(l => l.Kind == HireKinds.Unit && l.TroopType == "cavalry");
            var state = StartingKitService.CreateNewGame(Kit.D);
            var draft = new HireDraft(Kit.D);

            long room = draft.DefaultCount(state, "field", unit, 1);
            Assert.InRange(room, 0, unit.Troops);
            draft.Picks.Add(new HirePick { Listing = general, CorpsIndex = 2 });
            draft.Picks.Add(new HirePick { Listing = unit, CorpsIndex = 1, Count = 100 });
            var (comps, hired) = draft.Build(state, "field", "local", out var error);
            Assert.Null(error);
            Assert.Equal("enemy_general_04", comps[2].GeneralId);
            Assert.Equal(HireRules.UnitIdOf(unit.Id), comps[1].Slots.Values.Last());
            Assert.Equal(state.Compositions[2].Slots, comps[2].Slots); // 지휘관만 바뀜
            Assert.Equal(2, hired.Uses.Count);
            Assert.Equal("general_c", state.Compositions[2].GeneralId); // 세이브 편성은 그대로

            // 빈 칸이 없는 군단 (u06 넣기 전 general_a 군단은 5칸 → 하나 더 넣으면 꽉 참)
            var full = new HireDraft(Kit.D);
            full.Picks.Add(new HirePick { Listing = unit, CorpsIndex = 0, Count = 10 });
            full.Picks.Add(new HirePick { Listing = market.First(l => l.Kind == HireKinds.Unit && l.TroopType == "archer"), CorpsIndex = 0, Count = 10 });
            Assert.Null(full.Build(state, "field", "local", out var e2).compositions);
            Assert.Contains("빈 칸", e2);
        }

        [Fact(DisplayName = "가짜 서버에서 고용 출진 → 같은 시장으로 재현해 반영된다")]
        public void FakeHireBattle()
        {
            var clock = new ServerClock(Kit.Clock());
            var fake = new LocalOnlineService(Kit.D, Kit.Source, Kit.Clock());
            var sync = new CloudSync(fake, clock);
            SyncResult start = null;
            sync.Start(null, 0, r => start = r);
            var state = start.State;
            var unit = Market(fake).First(l => l.Kind == HireKinds.Unit && l.TroopType == "cavalry");
            var draft = new HireDraft(Kit.D);
            draft.Picks.Add(new HirePick { Listing = unit, CorpsIndex = 0, Count = 300 });
            var step = Game.Data.Defs.StepDef.Load(Kit.Source, "step01");
            var (comps, hired) = draft.Build(state, step.BattleType, fake.AccountId, out var error);
            Assert.Null(error);

            var flow = new StoryFlow(Kit.D, Kit.Source, clock);
            var plan = flow.Prepare(state, new SortieRequest { StepId = "step01", Seed = 4, Delegate = true, Compositions = comps, Hired = hired });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.True(flow.Confirm(state, plan));
            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            flow.ApplyResult(state, plan, sim.Result);
            SyncResult r = null;
            sync.SubmitBattle(CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, start.Revision), state, x => r = x);
            Assert.Equal(SyncOutcomes.Accepted, r.Outcome);
            Assert.Equal(state.Wallet["gold"], r.State.Wallet["gold"]); // 고용 비용(2 × 300)까지 같게
        }
    }
}
