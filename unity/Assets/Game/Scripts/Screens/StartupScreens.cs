using Game.Flow;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>스플래시 (startup.md ①): 로고 글자, 정해진 시간이 지나거나 누르면 다음.</summary>
    public sealed class SplashScreen : GameScreen
    {
        private float _elapsed;
        private bool _done;

        public override void Build()
        {
            Ui.Column(Root, 24, 80).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Label(Root, "", Ui.FontBody), 600);
            Ui.Size(Ui.Label(Root, T("ui.splash.studio"), Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim), 100);
            Ui.Size(Ui.Label(Root, T("ui.title.name"), Ui.FontTitle, TextAnchor.MiddleCenter), 160);
            // 화면 전체를 누르면 건너뛴다
            var skip = Ui.Button(Root, "", Finish, 400, new Color(0, 0, 0, 0));
            Ui.Size(skip, flexibleHeight: 1);
        }

        public override void Tick(float deltaTime)
        {
            _elapsed += deltaTime;
            if (_elapsed * 1000f >= Ctx.Startup.SplashMillis) Finish();
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            App.NextStartup(StartupSteps.Splash);
        }
    }

    /// <summary>로그인 (startup.md ③): 데이터의 로그인 버튼 목록. 준비 중인 수단은 꺼진 버튼.</summary>
    public sealed class SignInScreen : GameScreen
    {
        private readonly string _notice;
        public SignInScreen(string notice = null) { _notice = notice; }

        public override void Build()
        {
            Ui.Column(Root, 24, 80).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Label(Root, "", Ui.FontBody), 300);
            Ui.Size(Ui.Label(Root, T("ui.title.name"), Ui.FontTitle, TextAnchor.MiddleCenter), 160);
            Ui.Size(Ui.Label(Root, T("ui.signIn.note"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim), 120);
            if (_notice != null) Ui.Label(Root, _notice, Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad);
            foreach (var o in Ctx.Startup.SignInOptions)
            {
                if (o.Status == "hidden") continue;
                string id = o.Id;
                var b = Ui.Button(Root, o.IsActive ? T(o.NameKey) : T(o.NameKey) + "  " + T("ui.signIn.soon"), () =>
                    Ctx.SignIn(id, ok => { if (ok) App.NextStartup(StartupSteps.SignIn); else App.Show(new SignInScreen(T("ui.signIn.failed"))); }), 130);
                Ui.SetEnabled(b, o.IsActive);
            }
        }
    }

    /// <summary>
    /// 약관 동의 (startup.md ④): 이용약관·개인정보 처리방침 (둘 다 필수). [보기]로 내용을 펼친다 (문서 작성 전에는 임시 문구, roadmap 4.2).
    /// 약관 판이 오르면 다시 묻는다. 광고 동의(ATT 등)는 광고 단계에서.
    /// </summary>
    public sealed class TermsScreen : GameScreen
    {
        private bool _terms, _privacy;
        private string _open;

        public override void Build()
        {
            Ui.Column(Root, 16, 48);
            Ui.Size(Ui.Label(Root, T("ui.terms.title"), Ui.FontTitle), 120);
            Ui.Label(Root, T("ui.terms.note"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            var list = Ui.ScrollList(Root, "Terms", 12, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            Item(list, "terms", _terms, v => _terms = v);
            Item(list, "privacy", _privacy, v => _privacy = v);
            Ui.Button(Root, T("ui.terms.all"), () => { _terms = _privacy = true; Rebuild(); }, 110);
            var go = Ui.Button(Root, T("ui.terms.accept"), () => { Ctx.AcceptTerms(); App.NextStartup(StartupSteps.Terms); }, 130);
            Ui.SetEnabled(go, _terms && _privacy);
        }

        private void Item(RectTransform list, string id, bool on, System.Action<bool> set)
        {
            var card = Ui.Card(list, id, 20, 8);
            var row = Ui.Row(card, "Row", 96, 8);
            Ui.Size(Ui.Button(row, (on ? "■ " : "□ ") + T("ui.terms." + id), () => { set(!on); Rebuild(); }, 96, on ? Palette.ButtonActive : Palette.Button, 30), flexibleWidth: 3);
            Ui.Size(Ui.Button(row, T(_open == id ? "ui.terms.hide" : "ui.terms.view"), () => { _open = _open == id ? null : id; Rebuild(); }, 96, fontSize: 28), width: 200);
            if (_open == id) Ui.Label(card, T("ui.terms.body." + id), 24, TextAnchor.UpperLeft, Palette.TextDim);
        }

        private void Rebuild()
        {
            var next = new TermsScreen { _terms = _terms, _privacy = _privacy, _open = _open };
            App.Show(next);
        }
    }
}
