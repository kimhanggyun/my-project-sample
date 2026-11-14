using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Online;

namespace Game.Flow
{
    /// <summary>
    /// 수비 편성 (territory.md 5장, verification R-48): 지역 주인이 자기 편성을 등록하면, 그 지역을 공격하는 사람은 그 스냅샷과 싸운다 (AI가 지휘).
    /// 스냅샷이라 수비 결과는 주인의 원본 장수·부대에 영향이 없다 (고용과 같다).
    /// </summary>
    public static class TerritoryDefenses
    {
        public const string NotOwner = "notOwner";
        public const string BadFormation = "badFormation";

        /// <summary>
        /// 주인 세이브의 편성(상황, null = 기본)으로 스냅샷. 편성 검사(R-5)를 통과하고 군단이 하나 이상이어야 한다. 장수는 출전 가능 상태로, 부대는 부상병 없이.
        /// </summary>
        public static TerritoryDefense Snapshot(DefinitionSet defs, CampaignState owner, string situation, out string error)
        {
            error = null;
            return SnapshotOf(defs, owner, owner == null ? null : owner.CompositionsFor(situation), out error);
        }

        /// <summary>깃발 수비 (territory-v2.md 2.1, R-96): 기본 편성에서 그 장수가 이끄는 군단 하나만 스냅샷.</summary>
        public static TerritoryDefense SnapshotGeneral(DefinitionSet defs, CampaignState owner, string generalId, out string error)
        {
            var c = TerritoryFlags.CorpsOf(owner, generalId);
            return SnapshotOf(defs, owner, c == null ? null : new List<CorpsComposition> { c }, out error);
        }

        private static TerritoryDefense SnapshotOf(DefinitionSet defs, CampaignState owner, List<CorpsComposition> source, out string error)
        {
            error = null;
            var comps = owner == null || source == null ? null : Army.ForSortie(defs, owner, source);
            if (comps == null || comps.Count == 0 || CorpsValidator.Validate(defs, owner, comps).Count > 0) { error = BadFormation; return null; }
            var d = new TerritoryDefense();
            foreach (var c in comps)
            {
                var g = OnlineJson.Read<GeneralState>(OnlineJson.Write(owner.FindGeneral(c.GeneralId)));
                g.Status = GeneralStatus.Available;
                g.StatusUntilUnixSeconds = 0;
                var dc = new DefenseCorps { GeneralJson = OnlineJson.Write(g), Formation = c.Formation };
                // 병종 칸 → 진형 칸 6개 자동 배치 (army-v2.md 5장), 칸마다 그 병종 묶음의 훈련도·결속
                var picks = new List<(string unitId, string troopType, long troops)>();
                foreach (var p in c.Picks) { var pu = owner.FindUnit(p.UnitId); if (pu != null) picks.Add((p.UnitId, pu.TroopType, p.Troops)); }
                foreach (var st in Army.Layout(defs, picks))
                {
                    var u = OnlineJson.Read<UnitState>(OnlineJson.Write(owner.FindUnit(st.UnitId)));
                    u.Troops = st.Troops;
                    u.Wounded = 0;
                    u.WoundedRecoverAtUnixSeconds = 0;
                    dc.Units.Add(new DefenseUnit { Slot = st.Slot, UnitJson = OnlineJson.Write(u) });
                }
                if (dc.Units.Count > 0) d.Corps.Add(dc);
            }
            if (d.Corps.Count == 0) { error = BadFormation; return null; }
            return d;
        }
    }
}
