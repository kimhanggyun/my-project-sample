using System.Collections.Generic;
using System.Linq;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Economy;
using Game.Flow;
using Game.Foundation.Geo;
using Game.Online;
using Game.Territory;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>용병 배신 (hiring-v2.md 4장, verification R-99).</summary>
    public class BetrayalTests
    {
        private const string Here = "37.5665,126.978";

        /// <summary>등록자("owner")의 닉네임 장수 등록 + 그 등록자가 주인인 깃발(수비 스냅샷).</summary>
        private static (HiredForces hired, TerritoryDefense defense, string flagStep, CampaignState hirer) Setup(string defenseOwner)
        {
            var owner = Kit.Veteran();
            owner.FindGeneral(AvatarGeneral.GeneralId).Level = 3;
            var rules = new HireRules(Kit.D);
            var reg = rules.Register(owner, new HireListingRequest { Kind = HireKinds.General, GeneralId = AvatarGeneral.GeneralId, Price = 300, PayWith = ResourceIds.Gold }, 0);
            Assert.True(reg.Ok, reg.Reason);
            var listing = reg.Listing;
            listing.Id = "L1"; listing.Version = 1; listing.OwnerAccountId = "owner";
            var hirer = Kit.Veteran();
            var hired = rules.Resolve(new[] { new HireUse { ListingId = "L1", Version = 1 } }, id => id == "L1" ? listing : null, "hirer", hirer, out var err);
            Assert.Null(err);
            var defense = TerritoryDefenses.SnapshotGeneral(Kit.D, owner, "general_b", out _);
            defense.Version = 1;
            defense.OwnerAccountId = defenseOwner;
            var defs = TerritoryDefs.Load(Kit.Source);
            GeoPoint.TryParse("37.568,126.978", out var p);
            return (hired, defense, "territory." + FlagDefs.KeyOf(defs.Flags.CellOf(p)), hirer);
        }

        private static SortiePlan Plan(StoryFlow flow, CampaignState hirer, HiredForces hired, TerritoryDefense defense, string step)
        {
            var comps = hirer.Compositions.Select(FormationEditor.Copy).ToList();
            comps[2] = new CorpsComposition { GeneralId = AvatarGeneral.HiredId, Formation = "square", Role = "rear", Picks = { new TroopPick { UnitId = "infantry", Troops = 3500 } } };
            var plan = flow.Prepare(hirer, new SortieRequest { StepId = step, Seed = 3, Delegate = true, Location = Here, Defense = defense, Hired = hired, Compositions = comps });
            Assert.True(plan.Ok, string.Join("\n", plan.Errors));
            return plan;
        }

        [Fact(DisplayName = "R-99 배신 표시: 깃발 주인의 닉네임 장수를 고용해 그 깃발을 치면 그 군단만 BetrayToTeam = 적, 다른 주인이면 없음")]
        public void Marked()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var (hired, defense, step, hirer) = Setup("owner");
            var plan = Plan(flow, hirer, hired, defense, step);
            var traitor = plan.Setup.Corps.Single(c => c.General?.Id == AvatarGeneral.HiredId);
            Assert.Equal(StoryFlow.EnemyTeam, traitor.BetrayToTeam);
            Assert.All(plan.Setup.Corps.Where(c => c != traitor), c => Assert.Null(c.BetrayToTeam));

            var (hired2, defense2, step2, hirer2) = Setup("someone-else");
            var plan2 = Plan(flow, hirer2, hired2, defense2, step2);
            Assert.All(plan2.Setup.Corps, c => Assert.Null(c.BetrayToTeam));
        }

        [Fact(DisplayName = "R-99 배신: 우리 편 첫 교전 직후 적 편 · AI로 돌아서고(설정은 그대로), 같은 설정으로 다시 돌려도 같은 결과, 고용비는 돌려받는다")]
        public void Betrays()
        {
            var flow = new StoryFlow(Kit.D, Kit.Source, Kit.Clock());
            var (hired, defense, step, hirer) = Setup("owner");
            var plan = Plan(flow, hirer, hired, defense, step);
            long gold = new Wallet(hirer.Wallet).Get(ResourceIds.Gold);
            Assert.True(flow.Confirm(hirer, plan));
            Assert.Equal(gold - 300, new Wallet(hirer.Wallet).Get(ResourceIds.Gold));

            var sim = new BattleSimulation(plan.Setup);
            while (!sim.IsOver) sim.Step();
            var ev = sim.Result.Events.FirstOrDefault(e => e.Kind == EventKinds.Betrayed);
            Assert.NotNull(ev);
            var traitorSetup = plan.Setup.Corps.Single(c => c.Id == ev.CorpsId);
            Assert.Equal(AvatarGeneral.HiredId, traitorSetup.General.Id);
            Assert.Equal(StoryFlow.PlayerTeam, traitorSetup.Team); // 설정은 바꾸지 않는다
            Assert.Equal(StoryFlow.PlayerTeam, ev.Data);

            var again = BattleSimulation.Run(plan.Setup);
            Assert.Equal(sim.Result.StateHash, again.StateHash);

            var report = flow.ApplyResult(hirer, plan, sim.Result);
            Assert.Contains(AvatarGeneral.HiredId, report.Betrayed);
            Assert.Equal(300, report.Refunded[ResourceIds.Gold]);
            Assert.Contains("owner", plan.Hired.BetrayedOwners);
        }
    }
}
