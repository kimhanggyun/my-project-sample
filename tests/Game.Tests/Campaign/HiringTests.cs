using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>고용 (hiring.md, verification R-24~R-27, R-43): 등록 스냅샷·가격, 고용 출진, 서버 재현·등록자 보상 내역.</summary>
    public class HiringTests
    {
        private static HireRules Rules() => new HireRules(Kit.D);

        /// <summary>등록자: 새 게임 + 다른 장수 하나 (고용하는 쪽이 갖지 않은 장수).</summary>
        private static CampaignState Owner()
        {
            var s = Kit.Veteran();
            s.FindGeneral(AvatarGeneral.GeneralId).Level = 3; // 닉네임 장수 (R-98: 용병 장수는 이것뿐)
            return s;
        }

        private static HireListing Listed(CampaignState owner, HireListingRequest req, string id, string ownerAccount = "owner")
        {
            var r = Rules().Register(owner, req, 0);
            Assert.True(r.Ok, r.Reason);
            r.Listing.Id = id;
            r.Listing.Version = 1;
            r.Listing.OwnerAccountId = ownerAccount;
            return r.Listing;
        }

        [Fact(DisplayName = "R-27 등록: 추천 가격(장수 기본 + 레벨당, 부대 1명당) × 상한 배수까지, 내 것·출전 가능한 것만, 장수는 닉네임 장수만(R-98), 종류별 수 상한")]
        public void Register()
        {
            var owner = Owner();
            var rules = Rules();
            var g = owner.FindGeneral(AvatarGeneral.GeneralId);
            Assert.Equal(200 + 50 * 2, rules.RecommendedPrice(g, ResourceIds.Gold));
            Assert.Equal((200 + 50 * 2) * 5, rules.RecommendedPrice(g, ResourceIds.Supply));
            Assert.Equal(1, rules.RecommendedPrice(owner.FindUnit("infantry"), ResourceIds.Gold)); // 1단계, 훈련도 0

            var ok = rules.Register(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = g.GeneralId, Price = 600, PayWith = ResourceIds.Gold }, 0);
            Assert.True(ok.Ok);
            Assert.Equal((AvatarGeneral.GeneralId, 3, g.GeneralId), (ok.Listing.GeneralId, ok.Listing.Level, ok.Listing.SourceId));
            Assert.Equal(HireReasons.NotAvatar, rules.Register(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = "general_a", Price = 100, PayWith = ResourceIds.Gold }, 0).Reason); // 정의 장수는 등록 못 함 (R-98)
            Assert.Equal(HireReasons.BadPrice, rules.Register(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = g.GeneralId, Price = 601, PayWith = ResourceIds.Gold }, 0).Reason);
            Assert.Equal(HireReasons.TooMany, rules.Register(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = g.GeneralId, Price = 100, PayWith = ResourceIds.Gold }, 3).Reason);
            Assert.Equal(HireReasons.NotOwned, rules.Register(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = "enemy_general_01", Price = 1, PayWith = ResourceIds.Gold }, 0).Reason);
            Assert.Equal(HireReasons.BadRequest, rules.Register(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = g.GeneralId, Price = 1, PayWith = "gem" }, 0).Reason);
            g.Status = GeneralStatus.SeriousInjury;
            Assert.Equal(HireReasons.NotAvailable, rules.Register(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = g.GeneralId, Price = 1, PayWith = ResourceIds.Gold }, 0).Reason);

            var unit = rules.Register(owner, new HireListingRequest { Kind = HireKinds.Unit, UnitId = "cavalry", Price = 2, PayWith = ResourceIds.Supply }, 0);
            Assert.True(unit.Ok);
            Assert.Equal(("cavalry", 6000L), (unit.Listing.TroopType, unit.Listing.Troops)); // 병종 묶음 전부 등록
        }

        /// <summary>고용한 쪽 편성: general_a 군단의 정찰 칸(u05 500명)을 고용 부대 500명으로, general_c 대신 고용 장수(u11만).</summary>
        private static List<CorpsComposition> HiredComps(CampaignState hirer, string unitListing, string general)
        {
            var comps = hirer.Compositions.Select(FormationEditor.Copy).ToList();
            comps[0].Picks.Add(new TroopPick { UnitId = HireRules.UnitIdOf(unitListing), Troops = 500, Hired = true }); // 고용 부대는 계급 칸 밖 (army-v2.md 6장)
            comps[2] = new CorpsComposition { GeneralId = general, Formation = "square", Role = "rear", Picks = { new TroopPick { UnitId = "infantry", Troops = 3500 } } };
            return comps;
        }

        [Fact(DisplayName = "R-43 고용 출진: 비용을 출진 확정 때 내고, 전투 결과는 내 장수·부대에만 (고용분은 원본·내 세이브 어디에도 남지 않음)")]
        public void HireSortie()
        {
            var owner = Owner();
            var listings = new Dictionary<string, HireListing>
            {
                ["L1"] = Listed(owner, new HireListingRequest { Kind = HireKinds.Unit, UnitId = "cavalry", Price = 2, PayWith = ResourceIds.Gold }, "L1"),
                ["L2"] = Listed(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = AvatarGeneral.GeneralId, Price = 300, PayWith = ResourceIds.Supply }, "L2"),
            };
            var hirer = Kit.Veteran();
            var uses = new List<HireUse> { new HireUse { ListingId = "L1", Version = 1, Count = 500 }, new HireUse { ListingId = "L2", Version = 1 } };
            var hired = Rules().Resolve(uses, id => listings.TryGetValue(id, out var l) ? l : null, "hirer", hirer, out var error);
            Assert.Null(error);
            Assert.Equal(1000, hired.Costs[ResourceIds.Gold]); // 2 × 500 (R-25)
            Assert.Equal(300, hired.Costs[ResourceIds.Supply]);

            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var plan = flow.Prepare(hirer, new SortieRequest { StepId = "step01", Seed = 2, Delegate = true, Hired = hired, Compositions = HiredComps(hirer, "L1", AvatarGeneral.HiredId) });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            Assert.Contains(plan.Setup.Corps, c => c.General.Id == AvatarGeneral.HiredId);
            Assert.Contains(plan.Setup.Corps.SelectMany(c => c.Units), u => StoryFlow.PoolIdOf(u.UnitId) == "hire.L1");
            Assert.Equal(500, plan.Setup.Corps.SelectMany(c => c.Units).Where(u => StoryFlow.PoolIdOf(u.UnitId) == "hire.L1").Sum(u => u.Troops));
            Assert.Null(hirer.FindGeneral(AvatarGeneral.HiredId)); // 준비는 세이브를 바꾸지 않는다

            long gold = new Wallet(hirer.Wallet).Get(ResourceIds.Gold), supply = new Wallet(hirer.Wallet).Get(ResourceIds.Supply);
            Assert.True(flow.Confirm(hirer, plan));
            Assert.Equal(gold - 1000, new Wallet(hirer.Wallet).Get(ResourceIds.Gold));
            Assert.Equal(supply - plan.SupplyTaken - 300, new Wallet(hirer.Wallet).Get(ResourceIds.Supply));

            var result = BattleSimulation.Run(plan.Setup, new List<Game.Battle.Model.Commands.BattleCommand>());
            flow.ApplyResult(hirer, plan, result);
            Assert.Null(hirer.FindGeneral(AvatarGeneral.HiredId));
            Assert.Null(hirer.FindUnit("hire.L1"));
            Assert.Equal(6000, owner.FindUnit("cavalry").Troops); // 원본 그대로
        }

        [Fact(DisplayName = "R-43 고용 해석 거절: 판이 바뀜, 내 등록, 등록 인원 초과, 같은 등록 두 번, 돈 모자람")]
        public void ResolveRejects()
        {
            var owner = Owner();
            var unit = Listed(owner, new HireListingRequest { Kind = HireKinds.Unit, UnitId = "cavalry", Price = 2, PayWith = ResourceIds.Gold }, "L1");
            var mine = Listed(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = AvatarGeneral.GeneralId, Price = 100, PayWith = ResourceIds.Gold }, "L3");
            var all = new Dictionary<string, HireListing> { ["L1"] = unit, ["L3"] = mine };
            HireListing Find(string id) => all.TryGetValue(id, out var l) ? l : null;
            var hirer = Kit.Veteran();
            var rules = Rules();

            Assert.Null(rules.Resolve(new[] { new HireUse { ListingId = "L1", Version = 2, Count = 10 } }, Find, "hirer", hirer, out _));
            Assert.Null(rules.Resolve(new[] { new HireUse { ListingId = "L1", Version = 1, Count = 10 } }, Find, "owner", hirer, out _));
            // 남의 닉네임 장수는 내 닉네임 장수가 있어도 고용된다 (avatar_hired 틀로, R-98)
            var av = rules.Resolve(new[] { new HireUse { ListingId = "L3", Version = 1 } }, Find, "hirer", hirer, out var e1);
            Assert.Null(e1);
            Assert.Equal(AvatarGeneral.HiredId, av.Generals[0].GeneralId);
            Assert.Null(rules.Resolve(new[] { new HireUse { ListingId = "L1", Version = 1, Count = 6001 } }, Find, "hirer", hirer, out _));
            Assert.Null(rules.Resolve(new[] { new HireUse { ListingId = "L1", Version = 1, Count = 1 }, new HireUse { ListingId = "L1", Version = 1, Count = 1 } }, Find, "hirer", hirer, out _));

            hirer.Wallet[ResourceIds.Gold] = 10;
            var hired = rules.Resolve(new[] { new HireUse { ListingId = "L1", Version = 1, Count = 500 } }, Find, "hirer", hirer, out _);
            var plan = new StoryFlow(Kit.D, Kit.Source, Kit.Clock()).Prepare(hirer, new SortieRequest { StepId = "step01", Seed = 1, Hired = hired, Compositions = HiredComps(hirer, "L1", "general_c") });
            Assert.False(plan.Ok);
            Assert.Contains(plan.Errors, x => x.Contains("고용 비용"));
        }

        [Fact(DisplayName = "R-26·R-43 서버 재현: 같은 등록을 두 유저가 동시에 고용해도 등록은 그대로, 둘 다 맞고 등록자 보상 내역이 나온다")]
        public void ServerVerifyTwoHirers()
        {
            var owner = Owner();
            var listing = Listed(owner, new HireListingRequest { Kind = HireKinds.Unit, UnitId = "cavalry", Price = 2, PayWith = ResourceIds.Gold }, "L1");
            HireListing Find(string id) => id == "L1" ? listing : null;
            long now = Kit.Clock().UtcNow.ToUnixTimeSeconds();

            foreach (var who in new[] { "hirerA", "hirerB" })
            {
                var hirer = Kit.Veteran();
                var save = new CloudSave { Revision = 1, Json = SaveJson.Write(hirer), SavedAtUnixSeconds = now };
                var hired = Rules().Resolve(new[] { new HireUse { ListingId = "L1", Version = 1, Count = 500 } }, Find, who, hirer, out _);
                var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
                var plan = flow.Prepare(hirer, new SortieRequest { StepId = "step01", Seed = 8, Delegate = true, Hired = hired, Compositions = HiredComps(hirer, "L1", "general_c") });
                Assert.True(plan.Ok, string.Join("\n", plan.Errors));
                var sim = new BattleSimulation(plan.Setup);
                while (!sim.IsOver) sim.Step();
                var sub = CloudSync.SubmissionOf(plan, sim.CommandLog, sim.Result, 1);
                Assert.Single(sub.Hires);
                Assert.NotNull(sub.Compositions);

                var v = BattleVerifier.VerifySave(Kit.D, Kit.Source, save, sub, now, out var json, out _, out var charges, Find, who);
                Assert.True(v.Valid, v.Reason);
                Assert.Equal(1000, charges.Charges.Single().Cost);
                Assert.Equal(700, Rules().OwnerReward(1000)); // × 0.7
                // 서버 결과 = 기기에서 확정(비용 지불)·반영한 결과
                Assert.True(flow.Confirm(hirer, plan));
                flow.ApplyResult(hirer, plan, sim.Result);
                Assert.Equal(new Wallet(hirer.Wallet).Get(ResourceIds.Gold), new Wallet(SaveJson.Read(json).Wallet).Get(ResourceIds.Gold));

                // 등록자 계정으로 제출하면 거절 (내 등록은 고용 못 함), 목록을 못 찾아도 거절
                Assert.Equal(VerdictReasons.BadSortie, BattleVerifier.VerifySave(Kit.D, Kit.Source, save, sub, now, out _, out _, out _, Find, "owner").Reason);
                Assert.Equal(VerdictReasons.BadSortie, BattleVerifier.VerifySave(Kit.D, Kit.Source, save, sub, now, out _, out _, out _).Reason);
            }
            Assert.Equal(6000, listing.Troops); // R-26
        }

        [Fact(DisplayName = "우편 받기 행동: 받는 자원·양만큼 지갑에 (서버는 우편이 있는지 먼저 확인)")]
        public void MailClaim()
        {
            var state = Kit.Veteran();
            long gold = new Wallet(state.Wallet).Get(ResourceIds.Gold);
            var a = PlayerActionRunner.ClaimMail(new Mail { Id = "m1", Resource = ResourceIds.Gold, Amount = 70 });
            Assert.True(new PlayerActionRunner(Kit.D).Apply(state, a));
            Assert.Equal(gold + 70, new Wallet(state.Wallet).Get(ResourceIds.Gold));
            Assert.False(new PlayerActionRunner(Kit.D).Apply(state, PlayerActionRunner.ClaimMail(new Mail { Id = "m2", Resource = "gem", Amount = 5 })));

            var save = new CloudSave { Revision = 1, Json = SaveJson.Write(Kit.Veteran()) };
            var v = ActionVerifier.Apply(new PlayerActionRunner(Kit.D), save, new ActionSubmission { BaseRevision = 1, Actions = { a } }, 0, out _, out _, x => false);
            Assert.Equal(VerdictReasons.BadAction, v.Reason);
        }
    }
}
