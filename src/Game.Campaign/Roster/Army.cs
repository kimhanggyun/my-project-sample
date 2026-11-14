using System.Collections.Generic;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Campaign.Roster
{
    /// <summary>군단의 병종 칸 하나 (army-v2.md 4장): 병종 묶음(보유 부대 id)에서 몇 명을 데려가나.</summary>
    public sealed class TroopPick
    {
        public string UnitId { get; set; }
        public long Troops { get; set; }
        /// <summary>고용 부대 칸: 계급의 병종 칸 수에 들지 않는다 (진형 칸 6개 안에서). 출진 준비 때만 생긴다.</summary>
        public bool Hired { get; set; }
    }

    /// <summary>진형 칸 하나에 선 병사 (출진 때 자동 배치 결과).</summary>
    public readonly struct SlotTroops
    {
        public readonly string Slot;
        public readonly string UnitId;
        public readonly long Troops;
        public SlotTroops(string slot, string unitId, long troops) { Slot = slot; UnitId = unitId; Troops = troops; }
    }

    /// <summary>
    /// 군대 규칙 (army-v2.md, 2026-11-14): 병종마다 한 묶음으로 보유, 계급(계정 레벨)이 출진 군단 수·병종 칸 수·병종 최대 병력을 정한다,
    /// 새 병사는 계급의 기본 훈련도·결속으로 들어와 평균, 출진 때 진형 칸 6개에 자동 배치. 예전 세이브(같은 병종 부대 여러 개 + 칸 편성)는 <see cref="Normalize"/>로 바꾼다.
    /// </summary>
    public static class Army
    {
        /// <summary>한 군단의 칸 수 상한 (진형 칸 수). 고용 부대 칸을 더해도 이것을 넘지 않는다.</summary>
        public const int MaxPicksPerCorps = 6;

        /// <summary>계급의 칸 수에 드는 칸 (고용 부대 칸 빼고).</summary>
        public static int OwnPicks(CorpsComposition c) => c.Picks.FindAll(p => !p.Hired).Count;

        /// <summary>근접 병종이 서는 진형 칸 (앞에서부터), 원거리 병종 칸.</summary>
        public static readonly IReadOnlyList<string> MeleeSlots = new[] { SlotIds.Vanguard, SlotIds.LeftWing, SlotIds.RightWing, SlotIds.Center };
        public static readonly IReadOnlyList<string> RangedSlots = new[] { SlotIds.LeftGuard, SlotIds.RightGuard };

        public static RankDef Rank(DefinitionSet defs, CampaignState s) => defs.Campaign.RankFor(s.LordLevel);

        /// <summary>그 병종 묶음이 가질 수 있는 최대 병력 (병력 + 부상병).</summary>
        public static long TroopMax(DefinitionSet defs, CampaignState s) => Rank(defs, s).TroopMax;

        /// <summary>그 병종의 묶음 (보유 부대 id = 병종 id). 없으면 null.</summary>
        public static UnitState Pool(CampaignState s, string troopType) => s.Units.Find(u => u.TroopType == troopType);

        /// <summary>그 병종 묶음을 찾고 없으면 0명으로 만든다 (승급 병종이면 위 병종을 표시용으로 단다).</summary>
        public static UnitState EnsurePool(DefinitionSet defs, CampaignState s, string troopType)
        {
            var u = Pool(s, troopType);
            if (u != null) return u;
            u = new UnitState { UnitId = troopType, TroopType = troopType, Training = Fix.Zero, Cohesion = Fix.Zero, ParentUnitId = ParentType(defs, troopType) };
            s.Units.Add(u);
            return u;
        }

        /// <summary>승급 표에서 그 병종의 위 병종 (없으면 null).</summary>
        public static string ParentType(DefinitionSet defs, string troopType)
        {
            foreach (var p in defs.TroopGrowth.Promotions) if (p.To == troopType) return p.From;
            return null;
        }

        /// <summary>
        /// 새 병사 count명을 묶음에 넣는다 (징병·모병, army-v2.md 3.1): 훈련도·결속 = (기존 × 기존 값 + 새 × 계급 기본값) ÷ 합, 내림.
        /// 병력만 늘린다 (비용·상한은 부르는 쪽).
        /// </summary>
        public static void AddRecruits(DefinitionSet defs, CampaignState s, UnitState u, long count)
        {
            if (count <= 0) return;
            int q = defs.Campaign.RecruitQuality(s.LordLevel);
            var training = Fix.FromRaw(q * (Fix.OneValue.Raw / 100));
            var cohesion = Fix.FromInt(q);
            u.Training = Mix(u.Training, u.Troops, training, count);
            u.Cohesion = Mix(u.Cohesion, u.Troops, cohesion, count);
            u.Troops += count;
        }

        /// <summary>인원 가중 평균 (Fix raw 정수 나눗셈, 내림).</summary>
        public static Fix Mix(Fix a, long na, Fix b, long nb) =>
            na + nb <= 0 ? a : Fix.FromRaw((a.Raw * na + b.Raw * nb) / (na + nb));

        /// <summary>
        /// 출진에 쓸 편성: 계급의 군단 수·칸 수를 넘는 것은 뒤에서부터 빼고, 묶음 병력보다 많이 적힌 병종은 비율대로 줄인다 (army-v2.md 4장).
        /// 저장된 편성은 바꾸지 않고 사본을 돌려준다.
        /// </summary>
        public static List<CorpsComposition> ForSortie(DefinitionSet defs, CampaignState s, IReadOnlyList<CorpsComposition> comps)
        {
            var rank = Rank(defs, s);
            var list = new List<CorpsComposition>();
            for (int i = 0; i < comps.Count && list.Count < rank.Corps; i++)
            {
                var c = comps[i];
                var copy = new CorpsComposition { GeneralId = c.GeneralId, Formation = c.Formation, Role = c.Role };
                int own = 0;
                foreach (var p in c.Picks)
                {
                    if (!p.Hired && own >= rank.Slots) continue;
                    if (!p.Hired) own++;
                    if (copy.Picks.Count < MaxPicksPerCorps) copy.Picks.Add(new TroopPick { UnitId = p.UnitId, Troops = p.Troops, Hired = p.Hired });
                }
                list.Add(copy);
            }
            // 묶음마다 적힌 인원 합이 지금 병력보다 많으면 비율대로 (내림)
            var asked = new Dictionary<string, long>();
            foreach (var c in list) foreach (var p in c.Picks) asked[p.UnitId] = (asked.TryGetValue(p.UnitId, out var a) ? a : 0) + p.Troops;
            foreach (var c in list)
                foreach (var p in c.Picks)
                {
                    var u = s.FindUnit(p.UnitId);
                    if (u == null) continue;
                    long want = asked[p.UnitId];
                    if (want > u.Troops && want > 0) p.Troops = u.Troops * p.Troops / want;
                }
            return list;
        }

        /// <summary>
        /// 진형 칸 6개에 자동 배치 (army-v2.md 5장): 근접만이면 6칸 근접, 원거리만이면 6칸 원거리, 둘 다면 근접 = 선봉·좌익·우익·중군, 원거리 = 좌비·우비
        /// (한쪽 병종 가지 수가 그쪽 칸보다 많으면 다른 쪽 끝 칸을 빌린다). 한 무리 안에서는 병종마다 1칸부터, 남는 칸은 칸당 인원이 가장 많은 병종에.
        /// 칸 인원 = 병종 인원 ÷ 받은 칸 수 (나머지는 앞 칸부터 1명씩). 0명 칸은 만들지 않는다.
        /// </summary>
        public static List<SlotTroops> Layout(DefinitionSet defs, IReadOnlyList<(string unitId, string troopType, long troops)> picks)
        {
            var melee = new List<(string id, long n)>();
            var ranged = new List<(string id, long n)>();
            foreach (var p in picks)
            {
                if (p.troops <= 0) continue;
                bool isRanged = defs.TroopTypes.TryGetValue(p.troopType ?? "", out var t) && t.HasTag("ranged");
                (isRanged ? ranged : melee).Add((p.unitId, p.troops));
            }
            var meleeSlots = new List<string>();
            var rangedSlots = new List<string>();
            if (ranged.Count == 0) meleeSlots.AddRange(SlotIds.All);
            else if (melee.Count == 0) rangedSlots.AddRange(SlotIds.All);
            else
            {
                meleeSlots.AddRange(MeleeSlots);
                rangedSlots.AddRange(RangedSlots);
                while (rangedSlots.Count < ranged.Count && meleeSlots.Count > melee.Count) { rangedSlots.Insert(0, meleeSlots[meleeSlots.Count - 1]); meleeSlots.RemoveAt(meleeSlots.Count - 1); }
                while (meleeSlots.Count < melee.Count && rangedSlots.Count > ranged.Count) { meleeSlots.Add(rangedSlots[0]); rangedSlots.RemoveAt(0); }
            }
            var result = new List<SlotTroops>();
            Fill(melee, meleeSlots, result);
            Fill(ranged, rangedSlots, result);
            return result;
        }

        private static void Fill(List<(string id, long n)> group, List<string> slots, List<SlotTroops> result)
        {
            if (group.Count == 0 || slots.Count == 0) return;
            // 큰 병종부터 (같으면 넣은 차례)
            var order = new List<int>();
            for (int i = 0; i < group.Count; i++) order.Add(i);
            order.Sort((a, b) => group[a].n != group[b].n ? group[b].n.CompareTo(group[a].n) : a.CompareTo(b));
            var count = new int[group.Count];
            int left = slots.Count;
            foreach (var i in order) if (left > 0) { count[i] = 1; left--; }
            while (left > 0)
            {
                int best = -1;
                foreach (var i in order)
                    if (count[i] > 0 && (best < 0 || group[i].n * count[best] > group[best].n * count[i])) best = i;
                count[best]++;
                left--;
            }
            int slot = 0;
            foreach (var i in order)
            {
                long n = group[i].n;
                int k = count[i];
                for (int j = 0; j < k; j++)
                {
                    long each = n / k + (j < n % k ? 1 : 0);
                    if (each > 0) result.Add(new SlotTroops(slots[slot], group[i].id, each));
                    slot++;
                }
            }
        }

        /// <summary>
        /// 예전 세이브 바꾸기 (army-v2.md 7장): 같은 병종 부대를 병종 묶음 하나로 합치고 (병력·부상병 더함, 훈련도·결속은 인원 가중 평균),
        /// 편성의 칸(부대 id)을 병종 칸(병종 묶음, 그 부대 병력)으로 바꾼다. 이미 바뀐 세이브면 아무것도 안 한다. defs는 없어도 된다 (위 병종은 예전 승급 부대에서).
        /// </summary>
        public static bool Normalize(DefinitionSet defs, CampaignState s)
        {
            bool changed = false;
            if (s.LordLevel < 1) { s.LordLevel = 1; changed = true; }
            var map = new Dictionary<string, string>();
            var oldTroops = new Dictionary<string, long>();
            bool needMerge = false;
            var seen = new HashSet<string>();
            foreach (var u in s.Units)
            {
                oldTroops[u.UnitId] = u.Troops;
                if (!seen.Add(u.TroopType ?? "") || u.UnitId != u.TroopType) needMerge = true;
            }
            if (needMerge)
            {
                var pools = new List<UnitState>();
                foreach (var u in s.Units)
                {
                    var pool = pools.Find(p => p.TroopType == u.TroopType);
                    if (pool == null)
                    {
                        pool = new UnitState { UnitId = u.TroopType, TroopType = u.TroopType, Training = u.Training, Cohesion = u.Cohesion, Troops = 0, Wounded = 0 };
                        pools.Add(pool);
                    }
                    else
                    {
                        long na = pool.Troops + pool.Wounded, nb = u.Troops + u.Wounded;
                        pool.Training = Mix(pool.Training, na, u.Training, nb);
                        pool.Cohesion = Mix(pool.Cohesion, na, u.Cohesion, nb);
                    }
                    pool.Troops += u.Troops;
                    pool.Wounded += u.Wounded;
                    if (u.Wounded > 0) pool.WoundedRecoverAtUnixSeconds = System.Math.Max(pool.WoundedRecoverAtUnixSeconds, u.WoundedRecoverAtUnixSeconds);
                    map[u.UnitId] = u.TroopType;
                }
                // 위 병종: 예전 승급 부대의 위 부대 → 그 병종, 없으면 승급 표 (defs가 있을 때)
                var oldParent = new Dictionary<string, string>();
                foreach (var u in s.Units) if (u.ParentUnitId != null && map.TryGetValue(u.ParentUnitId, out var pt) && pt != u.TroopType) oldParent[u.TroopType] = pt;
                foreach (var p in pools)
                {
                    string parent = oldParent.TryGetValue(p.TroopType, out var op) ? op : defs == null ? null : ParentType(defs, p.TroopType);
                    p.ParentUnitId = parent != null && pools.Exists(x => x.TroopType == parent) ? parent : null;
                }
                s.Units = pools;
                changed = true;
            }
            foreach (var u in s.Units) map[u.UnitId] = u.UnitId;
            void Convert(List<CorpsComposition> comps)
            {
                if (comps == null) return;
                foreach (var c in comps)
                {
                    if (c.Slots == null || c.Slots.Count == 0) continue;
                    foreach (var slotId in SlotIds.All)
                    {
                        if (!c.Slots.TryGetValue(slotId, out var oldId) || !map.TryGetValue(oldId, out var poolId)) continue;
                        long n = oldTroops.TryGetValue(oldId, out var t) ? t : 0;
                        var pick = c.Picks.Find(p => p.UnitId == poolId);
                        if (pick == null) c.Picks.Add(new TroopPick { UnitId = poolId, Troops = n });
                        else pick.Troops += n;
                    }
                    c.Slots.Clear();
                    changed = true;
                }
            }
            Convert(s.Compositions);
            if (s.CompositionPresets != null) foreach (var kv in s.CompositionPresets) Convert(kv.Value);
            return changed;
        }
    }
}
