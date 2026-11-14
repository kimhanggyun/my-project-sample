using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Geo;
using Newtonsoft.Json.Linq;

namespace Game.Territory
{
    /// <summary>
    /// 영토 깃발 데이터 (data/Territory/flags.json, territory-v2.md 2장, verification R-96·R-97).
    /// 좌표 칸 맞추기 · 거리 계산은 정수만 (dev-rules: float/double 금지).
    /// </summary>
    public sealed class FlagDefs
    {
        public const string File = "Territory/flags.json";
        /// <summary>깃발 지역 키 앞머리: "flag:위도E6,경도E6" (칸 가운데).</summary>
        public const string KeyPrefix = "flag:";

        public long CellLatE6 { get; private set; }
        public long CellLonE6 { get; private set; }
        public long MetersPerDegreeLat { get; private set; }
        public long MetersPerDegreeLon { get; private set; }
        public long ProtectSeconds { get; private set; }
        public long IncomeCapHours { get; private set; }
        public IReadOnlyDictionary<string, long> IncomePerHour { get; private set; }
        /// <summary>공성전 전투 틀 (스텝 형식).</summary>
        public JObject Battle { get; private set; }

        public static FlagDefs Load(IDataSource source)
        {
            var o = JsonUtil.ParseObject(source.ReadText(File));
            var d = new FlagDefs
            {
                CellLatE6 = JsonUtil.RequireLong(o, "cellLatE6", File),
                CellLonE6 = JsonUtil.RequireLong(o, "cellLonE6", File),
                MetersPerDegreeLat = JsonUtil.RequireLong(o, "metersPerDegreeLat", File),
                MetersPerDegreeLon = JsonUtil.RequireLong(o, "metersPerDegreeLon", File),
                ProtectSeconds = (o.Value<long?>("protectMinutes") ?? 30) * 60,
                IncomeCapHours = o.Value<long?>("incomeCapHours") ?? 24,
                Battle = JsonUtil.RequireObject(o, "battle", File),
            };
            var inc = new Dictionary<string, long>();
            foreach (var p in (o["incomePerHour"] as JObject ?? new JObject()).Properties())
            {
                long v = JsonUtil.RequireLong((JObject)o["incomePerHour"], p.Name, File + ".incomePerHour");
                if (v < 0) throw new DataException($"{File}.incomePerHour.{p.Name}: 0 이상이어야 합니다.");
                inc[p.Name] = v;
            }
            d.IncomePerHour = inc;
            if (d.CellLatE6 < 1 || d.CellLonE6 < 1 || d.MetersPerDegreeLat < 1 || d.MetersPerDegreeLon < 1 || d.ProtectSeconds < 0 || d.IncomeCapHours < 1)
                throw new DataException($"{File}: 칸 크기 · 1도당 미터 · 보호 시간 · 수입 상한이 잘못되었습니다.");
            if (!(d.Battle["enemy"] is JArray en) || en.Count == 0) throw new DataException($"{File}.battle: 적(enemy)이 있어야 합니다.");
            return d;
        }

        /// <summary>좌표가 든 칸의 가운데 (음수 좌표도 아래로 내림해 같은 칸).</summary>
        public GeoPoint CellOf(GeoPoint p) => new GeoPoint(Center(p.LatE6, CellLatE6), Center(p.LonE6, CellLonE6));

        private static long Center(long v, long size)
        {
            long q = v / size;
            if (v % size != 0 && v < 0) q--;
            return q * size + size / 2;
        }

        /// <summary>칸 가운데 → 깃발 키.</summary>
        public static string KeyOf(GeoPoint cell) => KeyPrefix + cell.LatE6 + "," + cell.LonE6;

        public static bool IsFlagKey(string key) => key != null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);

        /// <summary>깃발 키 → 칸 가운데. 틀리면 false.</summary>
        public static bool TryParseKey(string key, out GeoPoint cell)
        {
            cell = default;
            if (!IsFlagKey(key)) return false;
            var parts = key.Substring(KeyPrefix.Length).Split(',');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var lat) || !long.TryParse(parts[1], out var lon)) return false;
            cell = new GeoPoint(lat, lon);
            return cell.IsValid;
        }

        /// <summary>두 점 사이 거리의 제곱 (m²). 짧은 거리라 평면 근사, 정수만.</summary>
        public long DistanceSquaredM(GeoPoint a, GeoPoint b)
        {
            long dy = (a.LatE6 - b.LatE6) * MetersPerDegreeLat / GeoPoint.Scale;
            long dx = (a.LonE6 - b.LonE6) * MetersPerDegreeLon / GeoPoint.Scale;
            return dx * dx + dy * dy;
        }

        /// <summary>b가 a에서 반지름 r(m) 안 (경계 포함).</summary>
        public bool Within(GeoPoint a, GeoPoint b, long radiusM) => DistanceSquaredM(a, b) <= radiusM * radiusM;

        /// <summary>깃발 하나의 쌓인 수입 (R-97): 자원마다 시간당 양 × 쌓인 초 ÷ 3600 (내림), 최대 incomeCapHours.</summary>
        public Dictionary<string, long> Accrued(long sinceUnixSeconds, long nowUnixSeconds)
        {
            var result = new Dictionary<string, long>();
            long seconds = System.Math.Max(0, System.Math.Min(nowUnixSeconds - sinceUnixSeconds, IncomeCapHours * 3600));
            foreach (var kv in IncomePerHour)
            {
                long v = kv.Value * seconds / 3600;
                if (v > 0) result[kv.Key] = v;
            }
            return result;
        }
    }
}
