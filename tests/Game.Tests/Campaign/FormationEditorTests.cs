using System.Linq;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>부대 편성 편집 (formation.md 4장, R-5)과 병력 관리 (4.1, R-10·R-33).</summary>
    public class FormationEditorTests
    {
        private static CampaignState Fresh() => StartingKitService.CreateNewGame(Kit.D);

        [Fact(DisplayName = "편성 편집: 사본만 고치고, 검사를 통과해야 저장된다 (R-5)")]
        public void EditAndApply()
        {
            var state = Fresh();
            var ed = new FormationEditor(Kit.D, state);
            Assert.Empty(ed.Errors());
            Assert.False(ed.Changed());

            ed.SetFormation(0, state.LearnedFormations.Last());
            Assert.True(ed.Changed());
            Assert.NotEqual(state.Compositions[0].Formation, ed.Draft[0].Formation); // 아직 저장 전
            Assert.True(ed.Apply());
            Assert.Equal(state.LearnedFormations.Last(), state.Compositions[0].Formation);

            // 한도를 넘기면 저장 안 됨 (R-5): 칸 6개를 최대 병력 부대로 채우면 통솔 한도(최고 20,000)를 넘는다
            var ed2 = new FormationEditor(Kit.D, state);
            ed2.SetFormation(0, state.LearnedFormations.First());
            var units = state.Units.Take(SlotIds.All.Count).ToList();
            for (int i = 0; i < units.Count; i++) { units[i].Troops = Kit.D.Balance.UnitMaxTroops; ed2.Assign(0, SlotIds.All[i], units[i].UnitId); }
            Assert.True(ed2.Troops(0) > ed2.Cap(0));
            Assert.Contains(ed2.Errors(), e => e.Contains("한도"));
            Assert.False(ed2.Apply());
            Assert.Equal(state.LearnedFormations.Last(), state.Compositions[0].Formation); // 저장된 편성은 그대로
        }

        [Fact(DisplayName = "한 부대는 한 곳에만: 다른 군단의 부대를 넣으면 거기서 빠진다, 장수를 바꾸면 맞바꾼다")]
        public void MoveUnitAndSwapGeneral()
        {
            var state = Fresh();
            var ed = new FormationEditor(Kit.D, state);
            var (slot, unit) = ed.Draft[1].Slots.First();
            ed.Assign(0, SlotIds.Vanguard, unit);
            Assert.Equal(unit, ed.Draft[0].Slots[SlotIds.Vanguard]);
            Assert.DoesNotContain(unit, ed.Draft[1].Slots.Values);
            Assert.Equal(0, ed.CorpsOf(unit));
            ed.Assign(0, SlotIds.Vanguard, null);
            Assert.Equal(-1, ed.CorpsOf(unit));

            string g0 = ed.Draft[0].GeneralId, g1 = ed.Draft[1].GeneralId;
            ed.SetGeneral(0, g1);
            Assert.Equal((g1, g0), (ed.Draft[0].GeneralId, ed.Draft[1].GeneralId));
        }

        [Fact(DisplayName = "군단 추가·삭제: 군단 수 상한, 남은 장수가 있어야 추가, 포로 장수는 넣을 수 없다")]
        public void AddRemoveCorps()
        {
            var state = Fresh();
            var ed = new FormationEditor(Kit.D, state);
            int n = ed.Draft.Count;
            Assert.Null(ed.AddCorps()); // 장수 3명 모두 편성됨
            ed.RemoveCorps(n - 1);
            Assert.Single(ed.FreeGenerals());
            var added = ed.AddCorps();
            Assert.NotNull(added);
            Assert.Equal(n, ed.Draft.Count);

            state.FindGeneral(added.GeneralId).Status = GeneralStatus.Captured;
            Assert.NotEmpty(ed.Errors());
            ed.RemoveCorps(n - 1);
            Assert.Empty(ed.FreeGenerals());
        }

        [Fact(DisplayName = "R-33 징병·모병은 부대 최대 병력(병력 + 부상병)까지, 비용이 모자라면 아무것도 바뀌지 않는다")]
        public void R33()
        {
            var state = Fresh();
            var troops = new TroopService(Kit.D);
            var u = state.Units[0];
            long max = Kit.D.Balance.UnitMaxTroops;
            u.Troops = max - 300;
            u.Wounded = 100;
            Assert.Equal(200, troops.Room(u));

            long supply = state.Wallet[ResourceIds.Supply];
            Assert.Equal(200, troops.Conscript(state, u.UnitId, 1000));
            Assert.Equal(max - 100, u.Troops);
            Assert.Equal(supply - troops.ConscriptCost(200), state.Wallet[ResourceIds.Supply]);
            Assert.Equal(0, troops.Recruit(state, u.UnitId, 10)); // 자리가 없다

            u.Troops = 0;
            u.Wounded = 0;
            state.Wallet[ResourceIds.Gold] = troops.RecruitCost(500) - 1;
            Assert.Equal(0, troops.Recruit(state, u.UnitId, 500));
            Assert.Equal(0, u.Troops);
            Assert.Equal(troops.RecruitCost(500) - 1, state.Wallet[ResourceIds.Gold]);
        }

        [Fact(DisplayName = "R-10 치료: 부상병만큼만, 고른 자원으로 징병·모병 비용의 절반")]
        public void Heal()
        {
            var state = Fresh();
            var troops = new TroopService(Kit.D);
            var u = state.Units[0];
            u.Wounded = 400;
            long troopsBefore = u.Troops, supply = state.Wallet[ResourceIds.Supply];
            Assert.Equal(400, troops.Heal(state, u.UnitId, 1000, ResourceIds.Supply));
            Assert.Equal(troopsBefore + 400, u.Troops);
            Assert.Equal(0, u.Wounded);
            Assert.Equal(supply - HealCostCalculator.HealCostInSupply(Kit.D.Balance, 400), state.Wallet[ResourceIds.Supply]);
        }
    }
}
