using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 타이틀 (시안 01): 위 대표 그림 자리 + 붓 획 제목, 연결 문구, 주홍 [시작]. 온라인이면 서버 세이브를 받는 동안 [시작]을 막는다.
    /// 준비되면 화면 아무 곳이나 눌러도 시작.
    /// </summary>
    public sealed class TitleScreen : GameScreen
    {
        /// <summary>뒤로 키: 맨 위 화면이라 종료 확인 창.</summary>
        public override bool OnBack() => false;

        private bool _ready;

        public override void Build()
        {
            // 화면 전체 누름 = 시작 (준비됐을 때만). 다른 것보다 뒤에 깐다.
            var anywhere = Ui.Rect(Root, "TapAnywhere");
            anywhere.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Fill(anywhere);
            var hit = Ui.Background(anywhere, new Color(0, 0, 0, 0));
            var tap = anywhere.gameObject.AddComponent<Button>();
            tap.targetGraphic = hit;
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(Start);

            Ui.Column(Root, 0, 0);
            Ui.TitleHeader(Root, Ctx);
            Ui.Spacer(Root);
            var status = Ui.Hint(Root, "", 120);
            Ui.Spacer(Root, 30, 0);
            var bottom = Ui.Inset(Root, "Bottom", 80);
            var start = Ui.PrimaryButton(bottom, T("ui.title.start"), Start, 160);
            Ui.Spacer(Root, 310, 0);

            _ready = !Ctx.OnlineEnabled;
            Ui.SetPrimaryEnabled(start, _ready);
            // 온라인이면 서버 세이브를 받은 뒤 시작 (docs/impl/server.md 5장)
            if (Ctx.OnlineEnabled)
            {
                status.text = T("ui.online.connecting");
                Button retry = null;
                void Synced()
                {
                    if (status == null || start == null) return; // 동기화가 끝났을 때 타이틀이 이미 닫혔으면 무시 (Unity: 파괴된 객체는 == null)
                    status.text = T("ui.online." + Ctx.OnlineStatus);
                    _ready = true;
                    Ui.SetPrimaryEnabled(start, true);
                    if (Ctx.OnlineStatus == Game.Unity.Boot.GameContext.SignInFailedStatus)
                    {
                        // 구글 자동 로그인 실패: 게스트로 들어가지 않고, 다시 시도하거나 이 기기 세이브로 (오프라인) 시작
                        if (retry == null)
                            retry = Ui.SecondaryButton(bottom, T("ui.online.retrySignIn"), () =>
                            {
                                status.text = T("ui.online.connecting");
                                _ready = false;
                                Ui.SetPrimaryEnabled(start, false);
                                Ctx.RetrySignIn(Synced);
                            }, 110);
                        return;
                    }
                    if (retry != null) { Object.Destroy(retry.gameObject); retry = null; }
                    Ctx.LoadProfile(null); // 마스터 계정인지 (R-86, 개발 줄 탭)
                }
                Ctx.StartSync(Synced);
            }
        }

        private void Start()
        {
            if (!_ready) return;
            _ready = false; // 두 번 누름 막기
            App.EnterGame();
        }
    }

    /// <summary>부팅·화면 오류. 규칙 데이터가 잘못되었을 때 원인을 화면에 보여준다.</summary>
    /// <summary>
    /// 시작 때 버전 · 점검 안내 (R-118): 업데이트 필요 = [스토어로]만 (닫을 수 없음), 점검 = 문구 · 끝 시각 · [다시 확인] (개발 빌드는 [그래도 들어가기]),
    /// 새 버전 = [업데이트] [나중에]. 점검이 업데이트보다 먼저 보이지는 않는다 (업데이트 필요가 앞).
    /// </summary>
    public sealed class AppStatusScreen : GameScreen
    {
        private readonly Game.Flow.AppStatusResult _r;
        private readonly string _step;
        public AppStatusScreen(Game.Flow.AppStatusResult r, string step) { _r = r; _step = step; }

        public override bool OnBack() => false; // 맨 위 화면: 뒤로 = 종료 확인

        public override void Build()
        {
            Ui.Column(Root, 24, 48).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Rect(Root, "Top"), flexibleHeight: 1);
            string kind = _r.UpdateRequired ? "updateRequired" : _r.Maintenance ? "maintenance" : "updateAvailable";
            Ui.Size(Ui.Label(Root, T("ui.status." + kind + ".title"), Ui.FontTitle, TextAnchor.MiddleCenter, _r.UpdateAvailable && !_r.Maintenance ? Palette.Text : Palette.Bad), 140);
            string body = _r.Maintenance && !_r.UpdateRequired ? T(_r.MessageKey ?? Game.Flow.AppStatus.DefaultMaintenanceKey) : T("ui.status." + kind + ".body");
            if (_r.Maintenance && !_r.UpdateRequired && _r.MaintenanceEndsAtUnixSeconds > 0)
                body += "\n\n" + T("ui.status.endsAt", System.DateTimeOffset.FromUnixTimeSeconds(_r.MaintenanceEndsAtUnixSeconds).ToLocalTime().ToString("MM.dd HH:mm"));
            var card = Ui.Card(Root, "Message", 24, 8);
            Ui.Label(card, body, Ui.FontBody, TextAnchor.MiddleCenter);
            Ui.Label(Root, T("ui.status.version", Application.version), 22, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Size(Ui.Rect(Root, "Bottom"), flexibleHeight: 1);
            if (_r.UpdateRequired) Ui.PrimaryButton(Root, T("ui.status.toStore"), OpenStore, 110, 34);
            else if (_r.Maintenance)
            {
                Ui.PrimaryButton(Root, T("ui.status.retry"), () => App.NextStartup(Game.Flow.StartupSteps.Splash), 110, 34); // 다시 버전 · 점검 확인
                if (Debug.isDebugBuild) Ui.SecondaryButton(Root, T("ui.status.enterAnyway"), () => App.NextStartup(_step), 90, 26);
            }
            else
            {
                Ui.PrimaryButton(Root, T("ui.status.update"), OpenStore, 110, 34);
                Ui.SecondaryButton(Root, T("ui.status.later"), () => App.NextStartup(_step), 96, 28);
            }
        }

        /// <summary>스토어의 이 앱 (안드로이드 market://, 안 되면 웹 주소).</summary>
        private static void OpenStore()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Application.OpenURL("market://details?id=" + Application.identifier);
#else
            Application.OpenURL("https://play.google.com/store/apps/details?id=" + Application.identifier);
#endif
        }
    }

    public sealed class ErrorScreen : GameScreen
    {
        /// <summary>뒤로 키: 맨 위 화면이라 종료 확인 창.</summary>
        public override bool OnBack() => false;

        private readonly string _message;
        public ErrorScreen(string message) { _message = message; }

        public override void Build()
        {
            Ui.Column(Root, 24, 48);
            Ui.Size(Ui.Label(Root, App.Context != null ? T("ui.error.header") : "오류", Ui.FontTitle, TextAnchor.MiddleCenter, Palette.Bad), 140);
            var list = Ui.ScrollList(Root, "Message");
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            Ui.Label(list, _message, Ui.FontSmall, TextAnchor.UpperLeft);
            if (App.Context != null) Ui.Button(Root, T("ui.error.retry"), () => App.ShowTitle());
        }
    }
}
