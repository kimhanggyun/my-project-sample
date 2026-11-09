using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Campaign.Troops;
using Game.Data.Defs;
using Game.Online;

namespace Game.Flow
{
    /// <summary>출진 화면에서 고른 고용 하나: 등록, 넣을 군단(편성 순번), 부대면 인원.</summary>
    public sealed class HirePick
    {
        public HireListing Listing { get; set; }
        /// <summary>넣을 군단: 그 상황의 편성 순번. 장수 고용이면 그 군단의 지휘관을 바꾸고(부대는 그대로), 부대 고용이면 첫 빈 칸에 넣는다.</summary>
        public int CorpsIndex { get; set; }
        public long Count { get; set; }
    }

    /// <summary>
    /// 출진 전 고용 고르기 (hiring.md 6장 "출진 준비"): 고른 것을 편성 사본에 넣고, 고용 해석(<see cref="HireRules.Resolve"/>)까지 해서
    /// 출진 요청에 넘길 편성·고용을 만든다. 화면은 이것만 부른다 (R-43).
    /// </summary>
    public sealed class HireDraft
    {
        private readonly DefinitionSet _defs;
        public List<HirePick> Picks { get; } = new List<HirePick>();

        public HireDraft(DefinitionSet defs) { _defs = defs; }

        public bool IsEmpty => Picks.Count == 0;

        /// <summary>이미 고른 등록인지.</summary>
        public bool Has(string listingId) => Picks.Exists(p => p.Listing?.Id == listingId);

        public void Remove(string listingId) => Picks.RemoveAll(p => p.Listing?.Id == listingId);

        /// <summary>
        /// 편성 사본 + 고용 해석. 문제가 있으면 error (그때 나머지는 null).
        /// </summary>
        /// <param name="battleType">스텝의 전장 유형 (상황별 편성, R-35).</param>
        public (List<CorpsComposition> compositions, HiredForces hired) Build(CampaignState state, string battleType, string hirerAccountId, out string error)
        {
            error = null;
            var comps = new List<CorpsComposition>();
            foreach (var c in state.CompositionsFor(battleType))
                comps.Add(new CorpsComposition { GeneralId = c.GeneralId, Formation = c.Formation, Role = c.Role, Slots = new Dictionary<string, string>(c.Slots) });
            if (IsEmpty) return (comps, null);

            var byId = new Dictionary<string, HireListing>();
            var uses = new List<HireUse>();
            foreach (var p in Picks)
            {
                var l = p.Listing;
                if (l == null || p.CorpsIndex < 0 || p.CorpsIndex >= comps.Count) { error = "고용을 넣을 군단이 없습니다."; return (null, null); }
                byId[l.Id] = l;
                var comp = comps[p.CorpsIndex];
                if (l.Kind == HireKinds.General)
                {
                    comp.GeneralId = l.GeneralId;
                    uses.Add(new HireUse { ListingId = l.Id, Version = l.Version });
                }
                else
                {
                    string slot = null;
                    foreach (var s in SlotIds.All) if (!comp.Slots.ContainsKey(s)) { slot = s; break; }
                    if (slot == null) { error = "고용 부대를 넣을 빈 칸이 없습니다."; return (null, null); }
                    comp.Slots[slot] = HireRules.UnitIdOf(l.Id);
                    uses.Add(new HireUse { ListingId = l.Id, Version = l.Version, Count = p.Count });
                }
            }
            var hired = new HireRules(_defs).Resolve(uses, id => byId.TryGetValue(id, out var x) ? x : null, hirerAccountId, state, out error);
            return hired == null ? (null, null) : (comps, hired);
        }

        /// <summary>
        /// 부대 고용 기본 인원 (R-24 일부): 등록 인원과 그 군단 장수 한도의 남은 자리 중 작은 쪽.
        /// 군량으로 먹일 수 있는지는 출진 준비가 본다 (군량이 모자라면 출진 불가로 보인다).
        /// </summary>
        public long DefaultCount(CampaignState state, string battleType, HireListing unitListing, int corpsIndex)
        {
            var (comps, hired) = Build(state, battleType, null, out var error);
            if (comps == null || corpsIndex < 0 || corpsIndex >= comps.Count) return 0;
            var comp = comps[corpsIndex];
            var general = hired?.Generals.Find(g => g.GeneralId == comp.GeneralId) ?? state.FindGeneral(comp.GeneralId);
            if (general == null) return 0;
            long cap = TroopCapCalculator.Cap(_defs.Balance, CorpsValidator.EffectiveStat(_defs, general, "leadership"));
            long used = 0;
            foreach (var kv in comp.Slots)
            {
                var u = state.FindUnit(kv.Value) ?? hired?.Units.Find(x => x.UnitId == kv.Value);
                if (u != null) used += u.Troops;
            }
            return System.Math.Max(0, System.Math.Min(unitListing.Troops, cap - used));
        }
    }
}
