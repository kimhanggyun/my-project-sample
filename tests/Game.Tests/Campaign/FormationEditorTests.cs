using System.Linq;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Economy;
using Game.Flow;
using Game.Foundation.Math;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>부대 편성 편집 (formation.md 4장, army-v2.md 4장, R-5)과 병력 관리 (4.1, R-10·R-33).</summary>
    public class FormationEditorTests
    {
        private static CampaignState Fresh() => StartingKitService.CreateNewGame(Kit.D);

        [Fact(DisplayName = "편성 편집: 사본만 고치고, 검사를 통과해야 저장된다 (R-5, 군단 병력 ≤ 통솔 한도)")]
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

            // 한도를 넘기면 저장 안 됨: 대장군(칸 4)에서 두 병종 15,000씩 = 30,000 > 통솔 한도(최고 20,000)
            state.LordLevel = 21;
            Army.Pool(state, "infantry").Troops = 15_000;
            Army.Pool(state, "spearman").Troops = 15_000;
            var ed2 = new FormationEditor(Kit.D, state);
            ed2.SetFormation(0, state.LearnedFormations.First());
            Assert.True(ed2.SetPick(0, "infantry", 15_000));
            Assert.True(ed2.SetPick(0, "spearman", 15_000));
            Assert.True(ed2.Troops(0) > ed2.Cap(0));
            Assert.Contains(ed2.Errors(), e => e.Contains("한도"));
            Assert.False(ed2.Apply());
            Assert.Equal(state.LearnedFormations.Last(), state.Compositions[0].Formation); // 저장된 편성은 그대로
        }

        [Fact(DisplayName = "R-94 계급이 군단 수·병종 칸 수를 정한다 (십부장 1·1, 천부장 2·2), 장수를 바꾸면 맞바꾼다")]
        public void RankLimits()
        {
            var state = Fresh();
            var ed = new FormationEditor(Kit.D, state);
            Assert.Equal((1, 1), (ed.MaxCorps, ed.MaxPicks));
            Assert.Null(ed.AddCorps());                       // 십부장: 군단 1개뿐
            Assert.False(ed.SetPick(0, "archer", 500));       // 칸 1개뿐
            Assert.True(ed.SetPick(0, "infantry", 2000));     // 같은 병종은 인원만 바뀜

            state.LordLevel = 6; // 천부장: 군단 2 · 칸 2
            ed = new FormationEditor(Kit.D, state);
            Assert.Equal((2, 2), (ed.MaxCorps, ed.MaxPicks));
            var added = ed.AddCorps();
            Assert.NotNull(added);
            Assert.True(ed.SetPick(0, "archer", 500));
            Assert.True(ed.SetPick(1, "cavalry", 1000));
            Assert.Empty(ed.Errors());
            Assert.True(ed.Apply());

            string g0 = ed.Draft[0].GeneralId, g1 = ed.Draft[1].GeneralId;
            ed.SetGeneral(0, g1);
            Assert.Equal((g1, g0), (ed.Draft[0].GeneralId, ed.Draft[1].GeneralId));

            // 계급이 내려가면 저장 검사는 막고, 출진은 넘치는 군단·칸을 뒤에서부터 뺀다
            state.LordLevel = 1;
            Assert.NotEmpty(CorpsValidator.Validate(Kit.D, state, state.Compositions));
            var sortie = Army.ForSortie(Kit.D, state, state.Compositions);
            Assert.Single(sortie);
            Assert.Single(sortie[0].Picks);
        }

        [Fact(DisplayName = "R-94 같은 병종을 여러 군단이 나눠 쓰면 합이 보유 병력을 넘을 수 없다, 한 군단에 같은 병종 두 칸 금지")]
        public void PoolShared()
        {
            var state = Fresh();
            state.LordLevel = 6;
            var ed = new FormationEditor(Kit.D, state);
            ed.AddCorps();
            ed.SetPick(0, "infantry", 2000);
            ed.SetPick(1, "infantry", 2000); // 합 4,000 > 보병 3,000
            Assert.Equal(2000, ed.UsedElsewhere(0, "infantry"));
            Assert.Contains(ed.Errors(), e => e.Contains("보유 병력"));
            ed.SetPick(1, "infantry", 1000);
            Assert.Empty(ed.Errors());
            ed.Draft[1].Picks.Add(new TroopPick { UnitId = "infantry", Troops = 1 });
            Assert.Contains(ed.Errors(), e => e.Contains("두 칸"));
        }

        [Fact(DisplayName = "R-33 징병·모병은 계급의 병종 최대 병력(병력 + 부상병)까지, 비용이 모자라면 아무것도 바뀌지 않는다")]
        public void R33()
        {
            var state = Fresh();
            var troops = new TroopService(Kit.D);
            var u = Army.Pool(state, "infantry");
            long max = Army.TroopMax(Kit.D, state); // 십부장 3,000
            Assert.Equal(3000, max);
            u.Troops = max - 300;
            u.Wounded = 100;
            Assert.Equal(200, troops.Room(state, u));

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

        [Fact(DisplayName = "R-94 새 병사: 계급 기본 훈련도·결속(Lv1 50, 레벨마다 +2, 최대 90)으로 섞인다 — 보병 2,000(훈련도 80) + 500 → 74")]
        public void RecruitQuality()
        {
            var state = Fresh();
            var troops = new TroopService(Kit.D);
            var u = Army.Pool(state, "infantry");
            u.Troops = 2000;
            u.Training = Fix.FromRaw(8000);
            u.Cohesion = Fix.FromInt(80);
            Assert.Equal(500, troops.Conscript(state, u.UnitId, 500));
            Assert.Equal((7400L, 74L), (u.Training.Raw, u.Cohesion.FloorToLong()));
            Assert.Equal((50, 58, 68, 90, 90), (Kit.D.Campaign.RecruitQuality(1), Kit.D.Campaign.RecruitQuality(5), Kit.D.Campaign.RecruitQuality(10), Kit.D.Campaign.RecruitQuality(21), Kit.D.Campaign.RecruitQuality(30)));
            // 치료는 값을 바꾸지 않는다
            u.Wounded = 100;
            var before = u.Training;
            troops.Heal(state, u.UnitId, 100, ResourceIds.Supply);
            Assert.Equal(before, u.Training);
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
