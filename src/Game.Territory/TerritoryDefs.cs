using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Geo;
using Newtonsoft.Json.Linq;

namespace Game.Territory
{
    /// <summary>지역 단계 (territory.md 2장): 큰 것부터 시·도 → 시·군·구 → 읍·면·동 → 리(도시의 동은 격자 구역).</summary>
    public static class RegionLevels
    {
        public const string Sido = "sido";
        public const string Sigungu = "sigungu";
        public const string Eupmyeondong = "eupmyeondong";
        /// <summary>리 또는 도시 동의 격자 구역 (혼자 차지하는 단계).</summary>
        public const string Ri = "ri";

        /// <summary>큰 것 → 작은 것 순서.</summary>
        public static readonly IReadOnlyList<string> Order = new[] { Sido, Sigungu, Eupmyeondong, Ri };

        /// <summary>깊이 (시·도 0 … 리 3), 모르는 단계는 -1.</summary>
        public static int Depth(string level)
        {
            for (int i = 0; i < Order.Count; i++) if (Order[i] == level) return i;
            return -1;
        }
    }

    /// <summary>단계별 기본값 (필요한 군단 수, 시즌 행사만 여부).</summary>
    public sealed class RegionLevelDef
    {
        public string Id { get; internal set; }
        public int RequiredLegions { get; internal set; }
        /// <summary>상시가 아니라 시즌 행사로만 (territory.md 2장, 제안).</summary>
        public bool SeasonOnly { get; internal set; }
        /// <summary>주인 없는 지역의 AI 수비군 (스텝과 같은 JSON 형식: map·maxMinutes·camp·enemy …).</summary>
        public JObject Defense { get; internal set; }
        /// <summary>차지한 동안 시간당 쌓이는 자원 (자원 id → 양, R-47).</summary>
        public IReadOnlyDictionary<string, long> IncomePerHour { get; internal set; }
    }

    /// <summary>지역 하나: 경계(다각형), 상위 지역, 이웃, 필요 군단 수(덮어쓰기), 전투 템플릿.</summary>
    public sealed class RegionDef
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        public string Level { get; internal set; }
        public string Parent { get; internal set; }
        /// <summary>경계 꼭짓점 (순서대로, 닫지 않는다). 개발용은 사각형 몇 곳, 실제는 공개 행정구역 경계 자료.</summary>
        public IReadOnlyList<GeoPoint> Boundary { get; internal set; }
        public IReadOnlyList<string> Neighbors { get; internal set; }
        /// <summary>지역별 덮어쓰기 (없으면 단계 기본값).</summary>
        public int? RequiredLegionsOverride { get; internal set; }
        /// <summary>영토 전투에 쓸 지형 (data/Maps id). 없으면 수비군 데이터의 map.</summary>
        public string BattleMap { get; internal set; }
        /// <summary>지역별 수비군 덮어쓰기 (단계 기본값 위에 합친다). 없으면 null.</summary>
        public JObject DefenseOverride { get; internal set; }
    }

    /// <summary>
    /// 영토 데이터 (data/Territory/regions.json, territory.md 2·3장). 읽을 때 검사: 단계·상위·이웃(서로)·경계(꼭짓점 3개 이상)·필요 군단 수.
    /// </summary>
    public sealed class TerritoryDefs
    {
        public const string File = "Territory/regions.json";

        public IReadOnlyDictionary<string, RegionLevelDef> Levels { get; private set; }
        public IReadOnlyDictionary<string, RegionDef> Regions { get; private set; }
        /// <summary>파일 순서 (같은 단계에서 겹치면 앞의 것).</summary>
        public IReadOnlyList<RegionDef> Ordered { get; private set; }
        /// <summary>받지 않고 쌓이는 최대 시간 (그 뒤로는 더 쌓이지 않는다).</summary>
        public long IncomeCapHours { get; private set; }

        private static Dictionary<string, long> ReadIncome(JObject o, string ctx)
        {
            var d = new Dictionary<string, long>();
            if (o == null) return d;
            foreach (var p in o.Properties())
            {
                if (p.Name.StartsWith("_")) continue;
                long v = JsonUtil.RequireLong(o, p.Name, ctx + ".income");
                if (v < 0) throw new DataException($"{ctx}.income.{p.Name}: 0 이상이어야 합니다.");
                d[p.Name] = v;
            }
            return d;
        }

        public static TerritoryDefs Load(IDataSource source)
        {
            var root = JsonUtil.ParseObject(source.ReadText(File));
            var levels = new Dictionary<string, RegionLevelDef>();
            foreach (var p in JsonUtil.RequireObject(root, "levels", File).Properties())
            {
                if (RegionLevels.Depth(p.Name) < 0) throw new DataException($"{File}: 모르는 지역 단계 '{p.Name}'.");
                var o = (JObject)p.Value;
                levels[p.Name] = new RegionLevelDef
                {
                    Id = p.Name,
                    RequiredLegions = (int)JsonUtil.RequireLong(o, "requiredLegions", File + ".levels." + p.Name),
                    SeasonOnly = o.Value<bool?>("seasonOnly") ?? false,
                    Defense = o["defense"] as JObject,
                    IncomePerHour = ReadIncome(o["income"] as JObject, File + ".levels." + p.Name),
                };
                if (levels[p.Name].Defense == null || !(levels[p.Name].Defense["enemy"] is JArray en) || en.Count == 0)
                    throw new DataException($"{File}.levels.{p.Name}: 수비군(defense.enemy)이 있어야 합니다.");
            }
            foreach (var l in RegionLevels.Order)
                if (!levels.ContainsKey(l)) throw new DataException($"{File}: 단계 '{l}'의 기본값이 없습니다.");

            var regions = new Dictionary<string, RegionDef>();
            var ordered = new List<RegionDef>();
            foreach (JObject o in (JArray)root["regions"] ?? new JArray())
            {
                string id = JsonUtil.RequireString(o, "id", File + ".regions");
                string ctx = File + ".regions." + id;
                if (regions.ContainsKey(id)) throw new DataException($"{ctx}: 같은 id가 두 번.");
                var boundary = new List<GeoPoint>();
                foreach (var pt in (JArray)o["boundary"] ?? new JArray())
                {
                    var arr = (JArray)pt;
                    if (arr.Count != 2 || !GeoPoint.TryParse(arr[0].ToString(Newtonsoft.Json.Formatting.None) + "," + arr[1].ToString(Newtonsoft.Json.Formatting.None), out var g))
                        throw new DataException($"{ctx}: 경계 꼭짓점은 [위도, 경도].");
                    boundary.Add(g);
                }
                if (boundary.Count < 3) throw new DataException($"{ctx}: 경계 꼭짓점이 3개 이상이어야 합니다.");
                var r = new RegionDef
                {
                    Id = id,
                    NameKey = o.Value<string>("nameKey") ?? "region." + id,
                    Level = JsonUtil.RequireString(o, "level", ctx),
                    Parent = o.Value<string>("parent"),
                    Boundary = boundary,
                    Neighbors = JsonUtil.StringList(o, "neighbors"),
                    RequiredLegionsOverride = o.Value<int?>("requiredLegions"),
                    BattleMap = o.Value<string>("battleMap"),
                    DefenseOverride = o["defense"] as JObject,
                };
                if (!levels.ContainsKey(r.Level)) throw new DataException($"{ctx}: 모르는 단계 '{r.Level}'.");
                if (r.RequiredLegionsOverride.HasValue && r.RequiredLegionsOverride.Value < 1) throw new DataException($"{ctx}: requiredLegions는 1 이상.");
                regions[id] = r;
                ordered.Add(r);
            }
            foreach (var r in ordered)
            {
                if (r.Parent != null)
                {
                    if (!regions.TryGetValue(r.Parent, out var parent)) throw new DataException($"{File}: '{r.Id}'의 상위 지역 '{r.Parent}'이 없습니다.");
                    if (RegionLevels.Depth(parent.Level) >= RegionLevels.Depth(r.Level)) throw new DataException($"{File}: '{r.Id}'의 상위 지역 '{r.Parent}'은 더 큰 단계여야 합니다.");
                }
                else if (r.Level != RegionLevels.Sido) throw new DataException($"{File}: 시·도가 아닌 '{r.Id}'에 상위 지역이 없습니다.");
                foreach (var n in r.Neighbors)
                {
                    if (!regions.TryGetValue(n, out var other)) throw new DataException($"{File}: '{r.Id}'의 이웃 '{n}'이 없습니다.");
                    if (other.Level != r.Level) throw new DataException($"{File}: '{r.Id}'와 이웃 '{n}'은 같은 단계여야 합니다.");
                    if (!((List<string>)other.Neighbors).Contains(r.Id)) throw new DataException($"{File}: 이웃은 서로여야 합니다 ('{r.Id}' → '{n}').");
                }
            }
            long cap = root.Value<long?>("incomeCapHours") ?? 24;
            if (cap < 1) throw new DataException($"{File}: incomeCapHours는 1 이상.");
            return new TerritoryDefs { Levels = levels, Regions = regions, Ordered = ordered, IncomeCapHours = cap };
        }

        /// <summary>그 지역을 차지하려면 필요한 군단 수: 지역별 값, 없으면 단계 기본값.</summary>
        public int RequiredLegions(RegionDef r) => r.RequiredLegionsOverride ?? Levels[r.Level].RequiredLegions;

        /// <summary>
        /// 쌓인 수입 (R-47): 자원마다 시간당 양 × 쌓인 초 ÷ 3600 (내림). 쌓인 초 = 지금 − 마지막으로 받은(또는 차지한) 시각, 최대 incomeCapHours.
        /// </summary>
        public Dictionary<string, long> Accrued(RegionDef r, long sinceUnixSeconds, long nowUnixSeconds)
        {
            var result = new Dictionary<string, long>();
            long seconds = System.Math.Max(0, System.Math.Min(nowUnixSeconds - sinceUnixSeconds, IncomeCapHours * 3600));
            foreach (var kv in Levels[r.Level].IncomePerHour)
            {
                long v = kv.Value * seconds / 3600;
                if (v > 0) result[kv.Key] = v;
            }
            return result;
        }

        /// <summary>그 지역의 AI 수비군 (단계 기본값 + 지역 덮어쓰기, 사본).</summary>
        public JObject DefenseFor(RegionDef r)
        {
            var d = (JObject)Levels[r.Level].Defense.DeepClone();
            if (r.DefenseOverride != null) d = JsonUtil.DeepMerge(d, r.DefenseOverride);
            if (r.BattleMap != null) d["map"] = r.BattleMap;
            return d;
        }
    }
}
