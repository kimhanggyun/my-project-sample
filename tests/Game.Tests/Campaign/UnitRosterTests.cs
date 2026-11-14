using System.Linq;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>병종 묶음 만들기·해산 (formation.md 4.1, army-v2.md 3장, verification R-39).</summary>
    public class UnitRosterTests
    {
        [Fact(DisplayName = "R-39 창설: 아직 없는 기본 병종의 묶음만 (병종마다 하나), 병력 0으로 생기고 징병으로 채울 수 있다")]
        public void Create()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var troops = new TroopService(Kit.D);
            Assert.Contains("catapult", troops.CreatableTroopTypes());
            Assert.Null(troops.Create(state, "archer")); // 이미 있다
            int before = state.Units.Count;
            var u = troops.Create(state, "catapult");
            Assert.NotNull(u);
            Assert.Equal(before + 1, state.Units.Count);
            Assert.Equal(("catapult", 0L), (u.UnitId, u.Troops));
            Assert.Null(troops.Create(state, "nope"));
            Assert.True(troops.Conscript(state, u.UnitId, 500) > 0);
        }

        [Fact(DisplayName = "R-39 묶음 수 상한을 넘으면 만들 수 없다")]
        public void Cap()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var troops = new TroopService(Kit.D);
            while (state.Units.Count < Kit.D.Campaign.UnitCountMax) state.Units.Add(new UnitState { UnitId = "x" + state.Units.Count, TroopType = "x" + state.Units.Count });
            Assert.False(troops.CanCreate(state));
            Assert.Null(troops.Create(state, "catapult"));
        }

        [Fact(DisplayName = "R-39 해산: 기본·상황별 어느 편성에도 없는 묶음만, 병력은 사라진다")]
        public void Disband()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var troops = new TroopService(Kit.D);
            var used = state.Compositions[0].Picks.First().UnitId;
            Assert.False(troops.Disband(state, used));

            var ed = new FormationEditor(Kit.D, state, "field");
            var spare = troops.Create(state, "catapult");
            troops.Conscript(state, spare.UnitId, 500);
            ed.SetPick(0, used, 0);
            Assert.True(ed.SetPick(0, spare.UnitId, 500));
            Assert.True(ed.Apply());
            Assert.False(troops.CanDisband(state, spare.UnitId)); // 야전 편성에 있다

            Assert.True(ed.RemovePreset());
            Assert.True(troops.Disband(state, spare.UnitId));
            Assert.Null(state.FindUnit(spare.UnitId));
            Assert.False(troops.Disband(state, "missing"));
        }
    }
}
