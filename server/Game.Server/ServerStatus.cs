using Game.Online;
using Microsoft.Extensions.Configuration;

namespace Game.Server
{
    /// <summary>
    /// 버전 확인 · 점검 안내 (GET /v1/status, R-118). 서버 설정 <c>Game:Status:MinVersion</c> · <c>LatestVersion</c> ·
    /// <c>Maintenance:On</c> · <c>Maintenance:MessageKey</c> · <c>Maintenance:EndsAt</c>(unix 초) 위에 관리자 페이지가 정한 값(저장소)을 덮는다.
    /// 아무것도 없으면 검사 없음 · 점검 아님. 점검은 안내일 뿐 서버가 요청을 막지는 않는다.
    /// </summary>
    public static class ServerStatus
    {
        public static ServerStatusResponse FromConfig(IConfiguration config)
        {
            long.TryParse(config["Game:Status:Maintenance:EndsAt"], out long ends);
            return new ServerStatusResponse
            {
                MinVersion = Blank(config["Game:Status:MinVersion"]),
                LatestVersion = Blank(config["Game:Status:LatestVersion"]),
                Maintenance = new MaintenanceInfo
                {
                    On = string.Equals(config["Game:Status:Maintenance:On"], "true", System.StringComparison.OrdinalIgnoreCase),
                    MessageKey = Blank(config["Game:Status:Maintenance:MessageKey"]),
                    EndsAtUnixSeconds = ends,
                },
            };
        }

        /// <summary>설정 위에 관리자 값: 버전은 있는 칸만, 점검은 관리자 값이 있으면 통째로.</summary>
        public static ServerStatusResponse Current(ServerStatusResponse fromConfig, ServerStatusResponse over, long nowUnixSeconds)
        {
            var r = new ServerStatusResponse
            {
                NowUnixSeconds = nowUnixSeconds,
                MinVersion = Blank(over?.MinVersion) ?? fromConfig?.MinVersion,
                LatestVersion = Blank(over?.LatestVersion) ?? fromConfig?.LatestVersion,
                Maintenance = over?.Maintenance ?? fromConfig?.Maintenance ?? new MaintenanceInfo(),
            };
            return r;
        }

        private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
