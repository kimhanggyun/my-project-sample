using System.Collections.Generic;
using Game.Data.Json;
using Newtonsoft.Json.Linq;

namespace Game.Territory
{
    /// <summary>지도 바탕 타일 하나 (10도 칸): 육지 · 호수 고리, 강 선. 좌표는 1e-4도 정수, [경도, 위도, 경도, 위도, …] (절대값).</summary>
    public sealed class GeoTile
    {
        public List<int[]> Land { get; } = new List<int[]>();
        public List<int[]> Lake { get; } = new List<int[]>();
        public List<int[]> River { get; } = new List<int[]>();
    }

    /// <summary>
    /// 영토 지도 바탕 (Natural Earth 1:10m, data/Territory/geo, tools/naturalearth_tiles.js가 만든다). 그림만 쓰는 자료라 규칙 계산에는 들어가지 않는다.
    /// 칸 = 위도 · 경도 10도, 파일이 없으면 그 칸은 바다. 좌표는 정수(1e-4도, 약 11m)라 소수 없이 다룬다.
    /// </summary>
    public static class GeoTiles
    {
        public const string Folder = "Territory/geo/";
        /// <summary>좌표 단위: 1도 = 10,000.</summary>
        public const int Scale = 10_000;
        public const int TileDegrees = 10;

        public static string FileOf(int latIndex, int lonIndex) => Folder + "ne_" + latIndex + "_" + lonIndex + ".json";

        /// <summary>위도 · 경도(1e-6도) → 칸 번호.</summary>
        public static (int lat, int lon) IndexOf(long latE6, long lonE6) =>
            ((int)FloorDiv(latE6 + 90_000_000L, TileDegrees * 1_000_000L), (int)FloorDiv(lonE6 + 180_000_000L, TileDegrees * 1_000_000L));

        private static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>칸 하나 (파일이 없으면 빈 칸 = 바다).</summary>
        public static GeoTile Load(IDataSource source, int latIndex, int lonIndex)
        {
            var t = new GeoTile();
            string file = FileOf(latIndex, lonIndex);
            if (source == null || !source.Exists(file)) return t;
            var o = JsonUtil.ParseObject(source.ReadText(file));
            Read(o["land"] as JArray, t.Land);
            Read(o["lake"] as JArray, t.Lake);
            Read(o["river"] as JArray, t.River);
            return t;
        }

        /// <summary>보이는 범위(1e-6도)가 걸치는 칸들.</summary>
        public static List<GeoTile> ForView(IDataSource source, long minLatE6, long minLonE6, long maxLatE6, long maxLonE6)
        {
            var list = new List<GeoTile>();
            var (a0, b0) = IndexOf(minLatE6, minLonE6);
            var (a1, b1) = IndexOf(maxLatE6, maxLonE6);
            for (int a = System.Math.Max(0, a0); a <= System.Math.Min(17, a1); a++)
                for (int b = System.Math.Max(0, b0); b <= System.Math.Min(35, b1); b++)
                    list.Add(Load(source, a, b));
            return list;
        }

        /// <summary>그 점이 육지인가 (육지 고리 안이고 호수 고리 밖, 짝홀 규칙).</summary>
        public static bool OnLand(GeoTile t, long latE6, long lonE6)
        {
            long x = lonE6 / 100, y = latE6 / 100; // 1e-4도
            bool land = false;
            foreach (var r in t.Land) if (Inside(r, x, y)) land = !land;
            if (!land) return false;
            foreach (var r in t.Lake) if (Inside(r, x, y)) return false;
            return true;
        }

        private static bool Inside(int[] ring, long x, long y)
        {
            bool inside = false;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                long xi = ring[2 * i], yi = ring[2 * i + 1], xj = ring[2 * j], yj = ring[2 * j + 1];
                if ((yi > y) == (yj > y)) continue;
                // x < xi + (y - yi) * (xj - xi) / (yj - yi), 정수로 (분모 부호 맞춤)
                long num = (y - yi) * (xj - xi), den = yj - yi;
                if (den < 0) { num = -num; den = -den; }
                if ((x - xi) * den < num) inside = !inside;
            }
            return inside;
        }

        private static void Read(JArray a, List<int[]> into)
        {
            if (a == null) return;
            foreach (JArray part in a)
            {
                var pts = new int[part.Count];
                int px = 0, py = 0;
                for (int i = 0; i + 1 < part.Count; i += 2)
                {
                    int dx = part[i].Value<int>(), dy = part[i + 1].Value<int>();
                    px = i == 0 ? dx : px + dx;
                    py = i == 0 ? dy : py + dy;
                    pts[i] = px;
                    pts[i + 1] = py;
                }
                into.Add(pts);
            }
        }
    }
}
