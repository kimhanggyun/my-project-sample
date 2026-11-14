using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Foundation.Math;
using Game.Online;

namespace Game.Flow
{
    /// <summary>이 전투에 고용한 것 (출진 준비에 넘긴다, R-43).</summary>
    public sealed class HiredForces
    {
        /// <summary>고용 장수 (GeneralId = 장수 정의 id).</summary>
        public List<GeneralState> Generals { get; } = new List<GeneralState>();
        /// <summary>고용 부대 (UnitId = "hire." + 등록 id, Troops = 고용 인원).</summary>
        public List<UnitState> Units { get; } = new List<UnitState>();
        /// <summary>자원 id → 고용 비용 합계 (출진 확정 때 낸다).</summary>
        public Dictionary<string, long> Costs { get; } = new Dictionary<string, long>();
        /// <summary>등록마다 낸 비용 (서버가 등록자 보상을 만든다).</summary>
        public List<HireCharge> Charges { get; } = new List<HireCharge>();
        /// <summary>고용 장수 id → 등록자 계정 (배신 판정, R-99).</summary>
        public Dictionary<string, string> GeneralOwners { get; } = new Dictionary<string, string>();
        /// <summary>전투에서 배신한 용병의 등록자 (R-99): 그 등록자의 고용비는 돌려받고 등록자 보상은 없다.</summary>
        public HashSet<string> BetrayedOwners { get; } = new HashSet<string>();
        /// <summary>제출 메시지에 그대로 실을 고용 목록.</summary>
        public List<HireUse> Uses { get; } = new List<HireUse>();

        /// <summary>서버 재현이 맞은 뒤: 고용마다 전투 결과 (이력용, R-44).</summary>
        public List<HireOutcome> Outcomes { get; } = new List<HireOutcome>();
        /// <summary>그 전투의 스텝 id (이력용).</summary>
        public string StepId { get; internal set; }

        public bool IsEmpty => Generals.Count == 0 && Units.Count == 0;
    }

    public sealed class HireCharge
    {
        public string ListingId { get; set; }
        public string OwnerAccountId { get; set; }
        public string Resource { get; set; }
        public long Cost { get; set; }
    }

    /// <summary>
    /// 고용 규칙 (hiring.md, verification R-24~R-27, R-43): 등록 스냅샷, 추천 가격·상한, 고용 해석(비용), 등록자 보상.
    /// 기기·서버가 같이 쓴다. 등록 목록 저장은 서버 쪽.
    /// </summary>
    public sealed class HireRules
    {
        public const string UnitPrefix = "hire.";
        private readonly DefinitionSet _defs;

        public HireRules(DefinitionSet defs) { _defs = defs; }

        public static string UnitIdOf(string listingId) => UnitPrefix + listingId;
        public static bool IsHiredUnit(string unitId) => unitId != null && unitId.StartsWith(UnitPrefix, StringComparison.Ordinal);

        private static bool IsPayResource(string r) => r == ResourceIds.Gold || r == ResourceIds.Supply;

        /// <summary>장수 1회 추천 가격 (돈 기준, 군량이면 × supplyPerGold).</summary>
        public long RecommendedPrice(GeneralState g, string payWith)
        {
            var b = _defs.Balance;
            long gold = checked(b.HireGeneralBasePrice + b.HireGeneralPricePerLevel * Math.Max(0, g.Level - 1));
            return payWith == ResourceIds.Supply ? checked(gold * b.HireSupplyPerGold) : gold;
        }

        /// <summary>부대 1명당 추천 가격: 기본 + 단계당(1단계 위) + 훈련도 × 계수 (내림).</summary>
        public long RecommendedPrice(UnitState u, string payWith)
        {
            var b = _defs.Balance;
            int tier = _defs.TroopTypes.TryGetValue(u.TroopType ?? "", out var t) ? t.Tier : 1;
            long gold = checked(b.HireUnitPricePerSoldier + b.HireUnitPricePerTier * Math.Max(0, tier - 1) + (b.HireUnitPricePerTraining * u.Training).FloorToLong());
            return payWith == ResourceIds.Supply ? checked(gold * b.HireSupplyPerGold) : gold;
        }

        /// <summary>등록자 보상 = 고용 비용 × 비율 (내림, R-25).</summary>
        public long OwnerReward(long cost) => (_defs.Balance.HireOwnerRewardRatio * cost).FloorToLong();

        /// <summary>
        /// 등록: 등록자 세이브(서버 세이브)에서 스냅샷을 만든다. 출전할 수 있는 장수, 병력이 있는 부대만. 가격은 0 이상, 추천 × 상한 배수 이하 (R-27).
        /// 등록 수 상한은 종류별 (existingOfKind = 이미 등록한 수, 같은 대상 갱신이면 세지 않는다).
        /// </summary>
        public HireListingResponse Register(CampaignState owner, HireListingRequest req, int existingOfKind)
        {
            if (owner == null || req == null || !IsPayResource(req.PayWith)) return Fail(HireReasons.BadRequest);
            var listing = new HireListing { Kind = req.Kind, Price = req.Price, PayWith = req.PayWith, SourceId = req.Kind == HireKinds.General ? req.GeneralId : req.UnitId };
            long recommended;
            if (req.Kind == HireKinds.General)
            {
                var g = owner.FindGeneral(req.GeneralId);
                if (g == null) return Fail(HireReasons.NotOwned);
                if (g.GeneralId != AvatarGeneral.GeneralId) return Fail(HireReasons.NotAvatar); // 정의 장수는 여러 유저가 같이 가져서 닉네임 장수만 (R-98)
                if (!GeneralStatus.CanDeploy(g.Status)) return Fail(HireReasons.NotAvailable);
                var snap = Copy(g);
                snap.Status = GeneralStatus.Available;
                snap.StatusUntilUnixSeconds = 0;
                snap.CampId = null;
                snap.CapturedAtUnixSeconds = 0;
                snap.RescueDeadlineUnixSeconds = 0;
                listing.SnapshotJson = OnlineJson.Write(snap);
                listing.GeneralId = g.GeneralId;
                listing.Level = g.Level;
                recommended = RecommendedPrice(g, req.PayWith);
            }
            else if (req.Kind == HireKinds.Unit)
            {
                var u = owner.FindUnit(req.UnitId);
                if (u == null) return Fail(HireReasons.NotOwned);
                if (u.Troops <= 0) return Fail(HireReasons.NotAvailable);
                var snap = Copy(u);
                snap.Wounded = 0;
                snap.WoundedRecoverAtUnixSeconds = 0;
                listing.SnapshotJson = OnlineJson.Write(snap);
                listing.TroopType = u.TroopType;
                listing.Troops = u.Troops; // 부대원 전부 등록 (hiring.md 2.2)
                listing.TrainingRaw = u.Training.Raw;
                recommended = RecommendedPrice(u, req.PayWith);
            }
            else return Fail(HireReasons.BadRequest);

            if (existingOfKind >= _defs.Balance.HireMaxListingsPerKind) return Fail(HireReasons.TooMany, recommended);
            if (!HireCalculator.IsPriceAllowed(_defs.Balance, req.Price, recommended)) return Fail(HireReasons.BadPrice, recommended);
            return new HireListingResponse { Ok = true, Reason = HireReasons.Ok, Listing = listing, RecommendedPrice = recommended };
        }

        private static HireListingResponse Fail(string reason, long recommended = 0) => new HireListingResponse { Ok = false, Reason = reason, RecommendedPrice = recommended };

        /// <summary>
        /// 고용 해석: 등록을 찾아 판이 같은지, 내 등록이 아닌지, 이미 가진 장수가 아닌지, 부대 인원이 등록 인원 안인지 보고 비용을 계산한다.
        /// 군량·장수 한도(R-24)와 비용 지불은 출진 준비·확정이 본다. 문제가 있으면 error.
        /// </summary>
        public HiredForces Resolve(IReadOnlyList<HireUse> uses, Func<string, HireListing> find, string hirerAccountId, CampaignState hirer, out string error)
        {
            error = null;
            var forces = new HiredForces();
            if (uses == null) return forces;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var use in uses)
            {
                var l = use?.ListingId == null ? null : find(use.ListingId);
                if (l == null || l.Version != use.Version) { error = $"고용 등록 '{use?.ListingId}'이 없거나 바뀌었습니다."; return null; }
                if (!seen.Add(l.Id)) { error = $"같은 등록 '{l.Id}'을 두 번 고용했습니다."; return null; }
                if (hirerAccountId != null && l.OwnerAccountId == hirerAccountId) { error = "내 등록은 고용할 수 없습니다."; return null; }
                if (!IsPayResource(l.PayWith)) { error = "등록의 지불 자원이 잘못되었습니다."; return null; }
                long cost;
                if (l.Kind == HireKinds.General)
                {
                    var g = Read<GeneralState>(l.SnapshotJson);
                    if (g != null && g.GeneralId == AvatarGeneral.GeneralId) g.GeneralId = AvatarGeneral.HiredId; // 남의 닉네임 장수는 내 닉네임 장수와 다른 틀로 (R-98)
                    if (g == null || !_defs.Generals.ContainsKey(g.GeneralId ?? "")) { error = "고용 장수 스냅샷을 읽을 수 없습니다."; return null; }
                    if (hirer?.FindGeneral(g.GeneralId) != null) { error = $"이미 가진 장수 '{g.GeneralId}'는 고용할 수 없습니다."; return null; }
                    foreach (var other in forces.Generals)
                        if (other.GeneralId == g.GeneralId) { error = $"같은 장수 '{g.GeneralId}'를 둘 고용했습니다."; return null; }
                    forces.Generals.Add(g);
                    forces.GeneralOwners[g.GeneralId] = l.OwnerAccountId;
                    cost = l.Price;
                    forces.Uses.Add(new HireUse { ListingId = l.Id, Version = l.Version });
                }
                else if (l.Kind == HireKinds.Unit)
                {
                    var u = Read<UnitState>(l.SnapshotJson);
                    if (u == null || !_defs.TroopTypes.ContainsKey(u.TroopType ?? "")) { error = "고용 부대 스냅샷을 읽을 수 없습니다."; return null; }
                    if (use.Count <= 0 || use.Count > l.Troops) { error = $"고용 인원 {use.Count}이 등록 인원 {l.Troops} 밖입니다."; return null; }
                    u.UnitId = UnitIdOf(l.Id);
                    u.Troops = use.Count;
                    u.Wounded = 0;
                    forces.Units.Add(u);
                    cost = HireCalculator.UnitHireCost(l.Price, use.Count);
                    forces.Uses.Add(new HireUse { ListingId = l.Id, Version = l.Version, Count = use.Count });
                }
                else { error = "모르는 고용 종류."; return null; }
                forces.Costs.TryGetValue(l.PayWith, out var sum);
                forces.Costs[l.PayWith] = checked(sum + cost);
                forces.Charges.Add(new HireCharge { ListingId = l.Id, OwnerAccountId = l.OwnerAccountId, Resource = l.PayWith, Cost = cost });
            }
            return forces;
        }

        private static GeneralState Copy(GeneralState g) => Read<GeneralState>(OnlineJson.Write(g));
        private static UnitState Copy(UnitState u) => Read<UnitState>(OnlineJson.Write(u));

        private static T Read<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return OnlineJson.Read<T>(json); } catch (Exception) { return null; }
        }
    }
}
