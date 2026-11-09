using Game.Flow;
using Game.Foundation.Save;
using Xunit;

namespace Game.Tests
{
    /// <summary>앱 시작 순서 (startup.md, data Ui/startup.json).</summary>
    public class StartupFlowTests
    {
        private static StartupFlow Flow() => StartupFlow.Load(Campaign.Kit.Source);

        [Fact(DisplayName = "처음 실행: 스플래시 → 로그인 → 약관 → 타이틀 (서버가 없으면 버전 확인은 건너뜀)")]
        public void FirstRun()
        {
            var f = Flow();
            var r = new StartupRecord();
            Assert.Equal(StartupSteps.Splash, f.Next(null, r, false));
            Assert.Equal(StartupSteps.SignIn, f.Next(StartupSteps.Splash, r, false));
            Assert.Equal(StartupSteps.VersionCheck, f.Next(StartupSteps.Splash, r, true));
            Assert.Equal(StartupSteps.Terms, f.Next(StartupSteps.SignIn, r, false));
            Assert.Equal(StartupSteps.Title, f.Next(StartupSteps.Terms, r, false));
            Assert.Equal(StartupSteps.Title, f.Next(StartupSteps.Title, r, false));
        }

        [Fact(DisplayName = "다음 실행: 전에 쓴 로그인 수단이 있으면 자동, 지금 판 약관에 동의했으면 건너뜀. 약관 판이 오르면 다시 묻는다")]
        public void Returning()
        {
            var f = Flow();
            var r = new StartupRecord { SignInMethod = "guest", TermsVersion = f.TermsVersion };
            Assert.Equal(StartupSteps.Title, f.Next(StartupSteps.Splash, r, false));
            r.TermsVersion = f.TermsVersion - 1;
            Assert.Equal(StartupSteps.Terms, f.Next(StartupSteps.Splash, r, false));
            r.SignInMethod = "google"; // 아직 준비 중인 수단 → 다시 로그인 화면
            Assert.Equal(StartupSteps.SignIn, f.Next(StartupSteps.Splash, r, false));
        }

        [Fact(DisplayName = "로그인 버튼은 데이터에서: 게스트는 사용, 구글·Apple은 준비 중. 기록은 기기 저장소에")]
        public void OptionsAndRecord()
        {
            var f = Flow();
            Assert.True(f.SignInOptions.Find(o => o.Id == "guest").IsActive);
            Assert.False(f.SignInOptions.Find(o => o.Id == "apple").IsActive);
            Assert.Equal(1500, f.SplashMillis);
            var store = new InMemorySaveStore();
            Assert.Null(StartupFlow.ReadRecord(store).SignInMethod);
            StartupFlow.WriteRecord(store, new StartupRecord { SignInMethod = "guest", TermsVersion = 1, TermsAcceptedAtUnixSeconds = 5 });
            var back = StartupFlow.ReadRecord(store);
            Assert.Equal(("guest", 1, 5L), (back.SignInMethod, back.TermsVersion, back.TermsAcceptedAtUnixSeconds));
        }
    }
}
