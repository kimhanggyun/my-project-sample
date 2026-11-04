using System.Collections.Generic;
using Game.Battle.Model.Ai;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Math;

namespace Game.Tests.Battle
{
    /// <summary>전투 테스트용 설정 만들기.</summary>
    internal static class BattleKit
    {
        private static DefinitionSet _defs;
        public static DefinitionSet Defs => _defs ?? (_defs = TestData.Real());

        public static MapDef Map(string id) => MapDef.Load(new FileDataSource(TestData.DataRoot), id);

        public static BattleSetup Setup(string mapId, ulong seed, string flagsJson = null, params CorpsSetup[] corps)
        {
            var d = Defs;
            return new BattleSetup
            {
                Defs = d,
                Map = Map(mapId),
                Balance = d.Balance,
                Combat = d.Combat,
                Flags = TestData.Flags(d, flagsJson),
                Seed = seed,
                Corps = new List<CorpsSetup>(corps),
            };
        }

        public static GeneralSnapshot General(string id, int leadership = 50, int might = 50, int intellect = 50, string temperament = "calm") =>
            new GeneralSnapshot { Id = id, Leadership = Fix.FromInt(leadership), Might = Fix.FromInt(might), Intellect = Fix.FromInt(intellect), Temperament = temperament };

        /// <summary>여섯 칸 모두 같은 병종.</summary>
        public static CorpsSetup Corps(string id, string team, string formation, string troopType = "infantry", long troopsPerUnit = 1000,
            GeneralSnapshot general = null, string controller = Controllers.Ai, string stance = AiStanceIds.Aggressive, long supply = 1_000_000)
        {
            var c = new CorpsSetup
            {
                Id = id,
                Team = team,
                Role = CorpsRoles.Commander,
                Controller = controller,
                AiStance = stance,
                General = general ?? General("g_" + id),
                Formation = formation,
                Supply = supply,
            };
            foreach (var slot in SlotIds.All)
                c.Units.Add(new UnitSetup { UnitId = id + "_" + slot, Slot = slot, TroopType = troopType, Troops = troopsPerUnit });
            return c;
        }

        /// <summary>칸별 병종 지정.</summary>
        public static CorpsSetup Mixed(string id, string team, string formation, params (string slot, string troop, long troops)[] units)
        {
            var c = Corps(id, team, formation);
            c.Units.Clear();
            foreach (var u in units) c.Units.Add(new UnitSetup { UnitId = id + "_" + u.slot, Slot = u.slot, TroopType = u.troop, Troops = u.troops });
            return c;
        }
    }
}
