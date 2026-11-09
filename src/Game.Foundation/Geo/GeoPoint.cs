using System;
using System.Globalization;

namespace Game.Foundation.Geo
{
    /// <summary>
    /// 위도·경도 (백만분의 1도 정수, 약 0.1m). 규칙 계산(지역 판정)은 정수로만 한다 (float/double 금지, dev-rules).
    /// </summary>
    public readonly struct GeoPoint : IEquatable<GeoPoint>
    {
        public const long Scale = 1_000_000;

        public long LatE6 { get; }
        public long LonE6 { get; }

        public GeoPoint(long latE6, long lonE6)
        {
            LatE6 = latE6;
            LonE6 = lonE6;
        }

        public bool IsValid => LatE6 >= -90 * Scale && LatE6 <= 90 * Scale && LonE6 >= -180 * Scale && LonE6 <= 180 * Scale;

        /// <summary>"37.5665,126.9780" 같은 10진 문자열 (입력 경계에서 한 번만, 백만분의 1도 아래는 버림).</summary>
        public static bool TryParse(string text, out GeoPoint point)
        {
            point = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split(',');
            if (parts.Length != 2) return false;
            if (!decimal.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return false;
            if (!decimal.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return false;
            point = new GeoPoint((long)decimal.Truncate(lat * Scale), (long)decimal.Truncate(lon * Scale));
            return point.IsValid;
        }

        public bool Equals(GeoPoint other) => LatE6 == other.LatE6 && LonE6 == other.LonE6;
        public override bool Equals(object obj) => obj is GeoPoint p && Equals(p);
        public override int GetHashCode() => (LatE6.GetHashCode() * 397) ^ LonE6.GetHashCode();

        public override string ToString() =>
            Format(LatE6) + "," + Format(LonE6);

        private static string Format(long e6)
        {
            string sign = e6 < 0 ? "-" : "";
            long a = System.Math.Abs(e6);
            return sign + (a / Scale).ToString(CultureInfo.InvariantCulture) + "." + (a % Scale).ToString("D6", CultureInfo.InvariantCulture);
        }
    }
}
