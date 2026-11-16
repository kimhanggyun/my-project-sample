using Game.Online;

namespace Game.Flow
{
    /// <summary>시작 때 버전 · 점검 판정 결과 (R-118).</summary>
    public sealed class AppStatusResult
    {
        /// <summary>앱이 최소 버전보다 낮다 → 스토어로 (들어가지 못함).</summary>
        public bool UpdateRequired { get; set; }
        /// <summary>최신이 아니다 → 안내만 (닫고 들어갈 수 있음).</summary>
        public bool UpdateAvailable { get; set; }
        /// <summary>점검 중 → 안내 (마스터는 들어갈 수 있게 할지 화면에서).</summary>
        public bool Maintenance { get; set; }
        public string MessageKey { get; set; }
        public long MaintenanceEndsAtUnixSeconds { get; set; }
    }

    /// <summary>
    /// 버전 확인 · 점검 안내 (R-118, docs/impl/server.md). 서버 응답이 없으면(오프라인 · 서버 꺼짐 · 닿지 않음) 모두 false = 그대로 시작.
    /// 버전은 "주.부.수" 숫자 비교 (모자란 자리는 0, 숫자가 아닌 꼬리 "-beta" 등은 무시). 버전을 읽을 수 없으면 검사하지 않는다.
    /// </summary>
    public static class AppStatus
    {
        public const string DefaultMaintenanceKey = "ui.status.maintenance";

        public static AppStatusResult Evaluate(ServerStatusResponse s, string appVersion, long nowUnixSeconds)
        {
            var r = new AppStatusResult();
            if (s == null) return r;
            r.UpdateRequired = Compare(appVersion, s.MinVersion) < 0;
            r.UpdateAvailable = !r.UpdateRequired && Compare(appVersion, s.LatestVersion) < 0;
            var m = s.Maintenance;
            if (m != null && m.On && (m.EndsAtUnixSeconds <= 0 || nowUnixSeconds < m.EndsAtUnixSeconds))
            {
                r.Maintenance = true;
                r.MessageKey = string.IsNullOrEmpty(m.MessageKey) ? DefaultMaintenanceKey : m.MessageKey;
                r.MaintenanceEndsAtUnixSeconds = m.EndsAtUnixSeconds;
            }
            return r;
        }

        /// <summary>a &lt; b → 음수, 같으면 0, 크면 양수. 어느 쪽이든 읽을 수 없으면 0 (검사 안 함).</summary>
        public static int Compare(string a, string b)
        {
            var x = Parse(a);
            var y = Parse(b);
            if (x == null || y == null) return 0;
            int n = System.Math.Max(x.Length, y.Length);
            for (int i = 0; i < n; i++)
            {
                long p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }

        private static long[] Parse(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            string core = v.Trim();
            int cut = core.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) core = core.Substring(0, cut);
            var parts = core.Split('.');
            var r = new long[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!long.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out r[i])) return null;
            return r;
        }
    }
}
