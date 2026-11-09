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
            var comps = owner?.CompositionsFor(situation);
            if (comps == null || comps.Count == 0 || CorpsValidator.Validate(defs, owner, comps).Count > 0) { error = BadFormation; return null; }
            var d = new TerritoryDefense();
            foreach (var c in comps)
            {
                var g = OnlineJson.Read<GeneralState>(OnlineJson.Write(owner.FindGeneral(c.GeneralId)));
                g.Status = GeneralStatus.Available;
                g.StatusUntilUnixSeconds = 0;
                var dc = new DefenseCorps { GeneralJson = OnlineJson.Write(g), Formation = c.Formation };
                foreach (var slot in SlotIds.All)
                {
                    if (!c.Slots.TryGetValue(slot, out var unitId)) continue;
                    var u = OnlineJson.Read<UnitState>(OnlineJson.Write(owner.FindUnit(unitId)));
                    u.Wounded = 0;
                    u.WoundedRecoverAtUnixSeconds = 0;
                    if (u.Troops > 0) dc.Units.Add(new DefenseUnit { Slot = slot, UnitJson = OnlineJson.Write(u) });
                }
                if (dc.Units.Count > 0) d.Corps.Add(dc);
            }
            if (d.Corps.Count == 0) { error = BadFormation; return null; }
            return d;
        }
    }
}
