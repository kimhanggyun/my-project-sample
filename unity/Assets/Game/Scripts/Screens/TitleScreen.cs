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
                Ctx.StartSync(() =>
                {
                    if (status == null || start == null) return; // 동기화가 끝났을 때 타이틀이 이미 닫혔으면 무시 (Unity: 파괴된 객체는 == null)
                    status.text = T("ui.online." + Ctx.OnlineStatus);
                    _ready = true;
                    Ui.SetPrimaryEnabled(start, true);
                    Ctx.LoadProfile(null); // 마스터 계정인지 (R-86, 개발 줄 탭)
                });
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
    public sealed class ErrorScreen : GameScreen
    {
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
