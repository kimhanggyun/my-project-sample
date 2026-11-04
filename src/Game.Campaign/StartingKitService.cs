using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Campaign
{
    /// <summary>새 게임 초기 지급 (formation.md 3장). startingKit.json대로 상태를 만든다.</summary>
    public static class StartingKitService
    {
        public static CampaignState CreateNewGame(DefinitionSet defs)
        {
            var kit = defs.StartingKit;
            var state = new CampaignState();
            foreach (var g in kit.Generals)
            {
                var def = defs.Generals[g];
                var gs = new GeneralState { GeneralId = g };
                gs.LearnedSkills.AddRange(def.InnateSkills);
                gs.LearnedTraits.AddRange(def.Traits);
                state.Generals.Add(gs);
            }
            foreach (var u in kit.Units)
                state.Units.Add(new UnitState { UnitId = u.Id, TroopType = u.TroopType, Troops = u.Troops, Training = Fix.Zero, Cohesion = Fix.Zero });
            foreach (var c in kit.Compositions)
            {
                var comp = new CorpsComposition { GeneralId = c.General, Formation = c.Formation, Role = c.Role };
                foreach (var s in c.Slots) comp.Slots[s.Key] = s.Value;
                state.Compositions.Add(comp);
            }
            state.LearnedFormations.AddRange(kit.Formations);
            foreach (var w in kit.Wallet) state.Wallet[w.Key] = w.Value;
            return state;
        }
    }
}
