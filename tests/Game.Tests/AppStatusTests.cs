using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests
{
    /// <summary>버전 확인 · 점검 안내 판정 (R-118).</summary>
    public class AppStatusTests
    {
        [Fact(DisplayName = "R-118 버전 비교: 자리별 숫자 (모자란 자리 0, -beta 꼬리 무시), 읽을 수 없으면 검사 안 함")]
        public void Compare()
        {
            Assert.True(AppStatus.Compare("1.2.9", "1.10.0") < 0);
            Assert.Equal(0, AppStatus.Compare("1.2", "1.2.0"));
            Assert.True(AppStatus.Compare("2.0.0-beta", "1.9.9") > 0);
            Assert.Equal(0, AppStatus.Compare("abc", "1.0.0"));
            Assert.Equal(0, AppStatus.Compare("1.0.0", null));
        }

        [Fact(DisplayName = "R-118 판정: 최소보다 낮으면 업데이트 필요, 최신보다 낮으면 안내만, 점검은 끝 시각 전까지 (0 = 모름), 응답 없으면 그대로 시작")]
        public void Evaluate()
        {
            var s = new ServerStatusResponse { MinVersion = "1.2.0", LatestVersion = "1.3.0", Maintenance = new MaintenanceInfo { On = true, EndsAtUnixSeconds = 1000 } };
            var old = AppStatus.Evaluate(s, "1.1.5", 500);
            Assert.True(old.UpdateRequired);
            Assert.False(old.UpdateAvailable);
            Assert.True(old.Maintenance);
            Assert.Equal(AppStatus.DefaultMaintenanceKey, old.MessageKey);
            var mid = AppStatus.Evaluate(s, "1.2.3", 1000);
            Assert.Equal((false, true, false), (mid.UpdateRequired, mid.UpdateAvailable, mid.Maintenance)); // 점검 끝 시각 지남
            s.Maintenance.EndsAtUnixSeconds = 0;
            Assert.True(AppStatus.Evaluate(s, "1.3.0", 99999).Maintenance);
            Assert.False(AppStatus.Evaluate(s, "1.3.0", 0).UpdateAvailable);
            var none = AppStatus.Evaluate(null, "0.0.1", 0);
            Assert.False(none.UpdateRequired || none.UpdateAvailable || none.Maintenance);
        }
    }
}
