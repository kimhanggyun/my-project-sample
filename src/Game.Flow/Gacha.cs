using System.Collections.Generic;
using Game.Campaign;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Foundation.Math;
using Game.Foundation.Random;
using Game.Online;

namespace Game.Flow
{
    /// <summary>모집 한 번의 결과: 장수, 등급, 이미 있어 조각이 되었는지.</summary>
    public sealed class GachaPull
    {
        public string GeneralId { get; internal set; }
        public string Grade { get; internal set; }
        public bool Duplicate { get; internal set; }
    }

    public sealed class GachaResult
    {
        public bool Ok { get; internal set; }
        /// <summary>noBanner / badCount / notEnough.</summary>
        public string Reason { get; internal set; }
        public List<GachaPull> Pulls { get; } = new List<GachaPull>();
    }

    /// <summary>장수마다 공개 확률 (확률 공개 의무: 화면은 이것을 그대로 보여준다).</summary>
    public sealed class GachaRate
    {
        public string GeneralId { get; internal set; }
        public string Grade { get; internal set; }
        /// <summary>한 번 모집에서 이 장수가 나올 확률 (등급 확률 ÷ 그 등급 장수 수).</summary>
        public Fix Rate { get; internal set; }
    }

    /// <summary>
    /// 장수 모집 (generals.md 5장, verification R-51): 재화를 내고 1회·10회. 등급을 확률로 고르고 그 등급 안에서 장수를 고르게 고른다.
    /// 10회는 보장 등급 이상 1명(마지막 한 번), 천장 등급이 정해진 수 동안 안 나오면 다음은 그 등급. 이미 있는 장수는 조각 1개.
    /// 시드로만 정해진다 (같은 세이브·같은 시드면 같은 결과, 서버 단계에서는 서버가 시드를 정한다).
    /// </summary>
    public sealed class GachaService
    {
        public const string NoBanner = "noBanner";
        /// <summary>기간 배너가 아직 안 열렸거나 끝남 (R-121).</summary>
        public const string Closed = "closed";
        public const string BadCount = "badCount";
        public const string NotEnough = "notEnough";
        public const string NoFree = "noFree";

        private readonly DefinitionSet _defs;
        public GachaService(DefinitionSet defs) { _defs = defs; }

        public long Cost(GachaBannerDef b, int count) => count == 10 ? b.CostTen : count == 1 ? b.CostOne : -1;

        /// <summary>운영 켜고 끄기 (R-122): 관리자가 끈 배너는 닫힘, 기간 덮어쓰기는 데이터 기간 대신. null이면 데이터 그대로.</summary>
        public ContentToggles Toggles { get; set; }

        /// <summary>실제로 쓰는 기간 (덮어쓰기가 있으면 그것).</summary>
        public (long starts, long ends) PeriodOf(GachaBannerDef b)
        {
            var t = Toggles?.Find(ContentKinds.Banner, b.Id);
            return (t?.StartsAtUnixSeconds ?? b.StartsAtUnixSeconds, t?.EndsAtUnixSeconds ?? b.EndsAtUnixSeconds);
        }

        /// <summary>기간 배너인가 (덮어쓴 기간 포함).</summary>
        public bool IsLimited(GachaBannerDef b) { var (s, e) = PeriodOf(b); return s > 0 || e > 0; }

        /// <summary>그 시각에 열려 있다: 관리자가 끄지 않았고 (시작 ≤ 지금 &lt; 끝).</summary>
        public bool IsOpen(GachaBannerDef b, long nowUnixSeconds)
        {
            if (b == null || Toggles?.IsOff(ContentKinds.Banner, b.Id) == true) return false;
            var (s, e) = PeriodOf(b);
            return (s <= 0 || nowUnixSeconds >= s) && (e <= 0 || nowUnixSeconds < e);
        }

        /// <summary>지금 열린 배너 (상시 먼저 데이터 순서, 기간 배너는 끝나는 순). 관리자가 끈 배너는 빠진다 (R-122).</summary>
        public List<GachaBannerDef> OpenBanners(long nowUnixSeconds)
        {
            var list = new List<GachaBannerDef>();
            foreach (var b in _defs.Gacha.Banners.Values) if (IsOpen(b, nowUnixSeconds)) list.Add(b);
            var order = new List<GachaBannerDef>(list);
            list.Sort((x, y) =>
            {
                bool lx = IsLimited(x), ly = IsLimited(y);
                if (lx != ly) return lx ? 1 : -1;
                if (lx) return PeriodOf(x).ends.CompareTo(PeriodOf(y).ends);
                return order.IndexOf(x).CompareTo(order.IndexOf(y));
            });
            return list;
        }

        /// <summary>기간 배너가 끝날 때까지 남은 날 (올림, 하루 안이면 1). 끝이 없으면 -1. 덮어쓴 기간 포함.</summary>
        public int DaysLeftOf(GachaBannerDef b, long nowUnixSeconds)
        {
            if (b == null) return -1;
            long end = PeriodOf(b).ends;
            if (end <= 0) return -1;
            long left = System.Math.Max(0, end - nowUnixSeconds);
            return (int)((left + 86399) / 86400);
        }

        /// <summary>기간 배너가 끝날 때까지 남은 날 (데이터 기간만). 끝이 없으면 -1.</summary>
        public static int DaysLeft(GachaBannerDef b, long nowUnixSeconds) => new GachaService(null).DaysLeftOf(b, nowUnixSeconds);

        /// <summary>닫힘: 관리자가 껐거나 (R-122), 기간 배너인데 행동 시각이 기간 밖이거나 시각이 없음 (R-121). 상시 배너는 시각을 보지 않는다.</summary>
        private bool Closes(GachaBannerDef b, long nowUnixSeconds) =>
            Toggles?.IsOff(ContentKinds.Banner, b.Id) == true || (IsLimited(b) && (nowUnixSeconds <= 0 || !IsOpen(b, nowUnixSeconds)));

        public GachaResult Draw(CampaignState state, string bannerId, int count, ulong seed, long nowUnixSeconds = 0)
        {
            var r = new GachaResult();
            if (bannerId == null || !_defs.Gacha.Banners.TryGetValue(bannerId, out var b)) { r.Reason = NoBanner; return r; }
            if (Closes(b, nowUnixSeconds)) { r.Reason = Closed; return r; }
            long cost = Cost(b, count);
            if (cost < 0) { r.Reason = BadCount; return r; }
            if (!new Wallet(state.Wallet).TrySpend(b.Currency, cost)) { r.Reason = NotEnough; return r; }
            return Pull(state, b, count, seed, r);
        }

        /// <summary>오늘 남은 무료 모집 수 (R-100). 날이 바뀌면 다시 freeDaily.</summary>
        public static int FreeLeft(CampaignState state, GachaBannerDef b, long nowUnixSeconds)
        {
            if (b == null || b.FreeDaily <= 0) return 0;
            int used = 0;
            if (state.GachaFreeDay == PracticeService.DayOf(nowUnixSeconds)) state.GachaFreeUsed.TryGetValue(b.Id, out used);
            return System.Math.Max(0, b.FreeDaily - used);
        }

        /// <summary>무료 1회 모집 (R-100): 재화 없이 1회 모집과 같게 (천장도 센다), 오늘 남은 무료가 없으면 아무것도 바뀌지 않는다.</summary>
        public GachaResult DrawFree(CampaignState state, string bannerId, ulong seed, long nowUnixSeconds)
        {
            var r = new GachaResult();
            if (bannerId == null || !_defs.Gacha.Banners.TryGetValue(bannerId, out var b)) { r.Reason = NoBanner; return r; }
            if (Closes(b, nowUnixSeconds)) { r.Reason = Closed; return r; }
            if (FreeLeft(state, b, nowUnixSeconds) <= 0) { r.Reason = NoFree; return r; }
            long day = PracticeService.DayOf(nowUnixSeconds);
            if (state.GachaFreeDay != day) { state.GachaFreeDay = day; state.GachaFreeUsed.Clear(); }
            state.GachaFreeUsed.TryGetValue(b.Id, out var n);
            state.GachaFreeUsed[b.Id] = n + 1;
            return Pull(state, b, 1, seed, r);
        }

        private GachaResult Pull(CampaignState state, GachaBannerDef b, int count, ulong seed, GachaResult r)
        {
            var rng = new SeededRandom(seed);
            var guarantee = b.TenGuarantee == null ? null : _defs.Gacha.Grade(b.TenGuarantee);
            var pityGrade = b.PityGrade == null ? null : _defs.Gacha.Grade(b.PityGrade);
            state.GachaPity.TryGetValue(b.Id, out int sincePity);
            bool metGuarantee = false;
            for (int i = 0; i < count; i++)
            {
                GachaGradeDef grade;
                if (pityGrade != null && sincePity >= b.PityCount - 1) grade = pityGrade; // 천장
                else if (count == 10 && i == count - 1 && guarantee != null && !metGuarantee) grade = AtLeast(rng, guarantee); // 10회 보장
                else grade = Roll(rng, _defs.Gacha.Grades);
                if (guarantee != null && grade.Rank >= guarantee.Rank) metGuarantee = true;
                sincePity = pityGrade != null && grade.Rank >= pityGrade.Rank ? 0 : sincePity + 1;

                var pool = PoolOf(b, grade.Id);
                string id = pool[rng.NextInt(pool.Count)];
                bool dup = state.FindGeneral(id) != null;
                if (dup)
                {
                    state.GeneralShards.TryGetValue(id, out var n);
                    state.GeneralShards[id] = n + 1;
                }
                else state.Generals.Add(GeneralState.Join(_defs.Generals[id])); // 기본 스킬·특성과 함께 (등용·합류와 같게)
                r.Pulls.Add(new GachaPull { GeneralId = id, Grade = grade.Id, Duplicate = dup });
            }
            state.GachaPity[b.Id] = sincePity;
            r.Ok = true;
            return r;
        }

        /// <summary>장수마다 공개 확률 (천장·10회 보장은 따로 안내).</summary>
        public List<GachaRate> Rates(string bannerId)
        {
            var list = new List<GachaRate>();
            if (bannerId == null || !_defs.Gacha.Banners.TryGetValue(bannerId, out var b)) return list;
            foreach (var g in _defs.Gacha.Grades)
            {
                var pool = PoolOf(b, g.Id);
                foreach (var id in pool) list.Add(new GachaRate { GeneralId = id, Grade = g.Id, Rate = g.Rate / pool.Count });
            }
            return list;
        }

        private List<string> PoolOf(GachaBannerDef b, string grade)
        {
            var list = new List<string>();
            foreach (var id in b.Pool) if (_defs.Generals[id].Grade == grade) list.Add(id);
            return list;
        }

        /// <summary>등급 확률로 하나 (누적 비교, 0 이상 1 미만 난수).</summary>
        private static GachaGradeDef Roll(SeededRandom rng, IReadOnlyList<GachaGradeDef> grades)
        {
            Fix x = rng.NextFix01(), acc = Fix.Zero;
            foreach (var g in grades)
            {
                acc += g.Rate;
                if (x < acc) return g;
            }
            return grades[grades.Count - 1];
        }

        /// <summary>그 등급 이상만 남겨 확률 비율대로 하나.</summary>
        private GachaGradeDef AtLeast(SeededRandom rng, GachaGradeDef min)
        {
            var top = new List<GachaGradeDef>();
            Fix sum = Fix.Zero;
            foreach (var g in _defs.Gacha.Grades) if (g.Rank >= min.Rank && g.Rate > Fix.Zero) { top.Add(g); sum += g.Rate; }
            Fix x = rng.NextFix01() * sum, acc = Fix.Zero;
            foreach (var g in top)
            {
                acc += g.Rate;
                if (x < acc) return g;
            }
            return top[top.Count - 1];
        }
    }

    /// <summary>
    /// 장수 한계 돌파 (generals.md 5장, R-57): 모집에서 겹친 장수의 조각으로 그 장수의 레벨 상한을 올린다.
    /// 단계마다 드는 조각 수·오르는 상한은 gacha.json limitBreak (수치는 예시).
    /// </summary>
    public sealed class LimitBreakService
    {
        public const string Ok = "ok";
        public const string NoGeneral = "noGeneral";
        public const string MaxStage = "maxStage";
        public const string NotEnoughShards = "notEnoughShards";

        private readonly DefinitionSet _defs;
        public LimitBreakService(DefinitionSet defs) { _defs = defs; }

        /// <summary>이 장수의 레벨 상한 = 기본 상한 + 단계 × 단계당 상한.</summary>
        public static int MaxLevelOf(DefinitionSet defs, GeneralState g) =>
            defs.Levels.MaxLevel + (g == null ? 0 : g.LimitBreak) * defs.Gacha.LevelCapPerStage;

        public static long Shards(CampaignState state, string generalId) =>
            state.GeneralShards.TryGetValue(generalId, out var n) ? n : 0;

        /// <summary>다음 단계에 드는 조각 (끝 단계면 null).</summary>
        public long? NextCost(GeneralState g)
        {
            var list = _defs.Gacha.LimitBreakShards;
            return g == null || g.LimitBreak >= list.Count ? (long?)null : list[g.LimitBreak];
        }

        public string Check(CampaignState state, string generalId)
        {
            var g = generalId == null ? null : state.FindGeneral(generalId);
            if (g == null) return NoGeneral;
            var cost = NextCost(g);
            if (cost == null) return MaxStage;
            return Shards(state, generalId) >= cost.Value ? Ok : NotEnoughShards;
        }

        public bool Apply(CampaignState state, string generalId)
        {
            if (Check(state, generalId) != Ok) return false;
            var g = state.FindGeneral(generalId);
            long left = Shards(state, generalId) - NextCost(g).Value;
            if (left > 0) state.GeneralShards[generalId] = left; else state.GeneralShards.Remove(generalId);
            g.LimitBreak++;
            return true;
        }
    }
}
