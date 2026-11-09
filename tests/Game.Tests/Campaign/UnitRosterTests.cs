using System.Linq;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Flow;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>부대 창설·해산 (formation.md 4.1, verification R-39).</summary>
    public class UnitRosterTests
    {
        [Fact(DisplayName = "R-39 창설: 기본 병종만, 병력 0으로 생기고 새 번호를 받으며, 징병으로 채울 수 있다")]
        public void Create()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var troops = new TroopService(Kit.D);
            Assert.Contains("archer", troops.CreatableTroopTypes());
            int before = state.Units.Count;
            var u = troops.Create(state, "archer");
            Assert.NotNull(u);
            Assert.Equal(before + 1, state.Units.Count);
            Assert.Equal(0, u.Troops);
            Assert.DoesNotContain(state.Units.Where(x => x != u), x => x.UnitId == u.UnitId);
            Assert.Equal("u14", u.UnitId); // 초기 지급 u01~u13 다음
            Assert.Null(troops.Create(state, "nope"));
            Assert.True(troops.Conscript(state, u.UnitId, 500) > 0);
        }

        [Fact(DisplayName = "R-39 부대 수 상한을 넘으면 만들 수 없다")]
        public void Cap()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var troops = new TroopService(Kit.D);
            while (troops.CanCreate(state)) Assert.NotNull(troops.Create(state, "infantry"));
            Assert.Equal(Kit.D.Campaign.UnitCountMax, state.Units.Count);
            Assert.Null(troops.Create(state, "infantry"));
        }

        [Fact(DisplayName = "R-39 해산: 기본·상황별 어느 편성에도 없는 부대만, 병력은 사라진다")]
        public void Disband()
        {
            var state = StartingKitService.CreateNewGame(Kit.D);
            var troops = new TroopService(Kit.D);
            var used = state.Compositions[0].Slots.Values.First();
            Assert.False(troops.Disband(state, used));

            var ed = new FormationEditor(Kit.D, state, "field");
            var spare = troops.Create(state, "infantry");
            troops.Conscript(state, spare.UnitId, 500);
            ed.Assign(0, Game.Data.Defs.SlotIds.Vanguard, spare.UnitId);
            Assert.True(ed.Apply());
            Assert.False(troops.CanDisband(state, spare.UnitId)); // 야전 편성에 있다

            Assert.True(ed.RemovePreset());
            Assert.True(troops.Disband(state, spare.UnitId));
            Assert.Null(state.FindUnit(spare.UnitId));
            Assert.False(troops.Disband(state, "missing"));
        }
    }
}
