using System.Collections.Generic;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

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

    /// <summary>
    /// 로그인 (startup.md ③, 시안 02): 위는 타이틀과 같은 머리, 아래에 데이터 순서대로 로그인 버튼 (구글·Apple은 회사 버튼 모양, 그 밖은 보조 버튼).
    /// 준비 중인 수단은 흐린 꺼진 버튼. 누르면 그 버튼은 "로그인 중…", 나머지는 막는다. 실패하면 버튼 위 빨간 한 줄.
    /// </summary>
    public sealed class SignInScreen : GameScreen
    {
        private readonly string _notice;
        private readonly List<(Button button, SignInOption option)> _buttons = new List<(Button, SignInOption)>();
        public SignInScreen(string notice = null) { _notice = notice; }

        public override void Build()
        {
            int shown = 0;
            foreach (var o in Ctx.Startup.SignInOptions) if (o.Status != "hidden") shown++;
            Ui.Column(Root, 0, 0);
            Ui.TitleHeader(Root, Ctx, 700 - Mathf.Max(0, shown - 3) * 160); // 버튼이 늘면 그림 자리를 줄인다
            Ui.Spacer(Root);
            var error = Ui.ErrorLine(Root, _notice);
            var box = Ui.Inset(Root, "Buttons", 80, 30);
            foreach (var o in Ctx.Startup.SignInOptions)
            {
                if (o.Status == "hidden") continue;
                var option = o;
                Button b = null;
                b = Brand(box, option, () => Press(option, b, error));
                _buttons.Add((b, option));
            }
            var note = Ui.Label(box, T("ui.signIn.note"), 28, TextAnchor.UpperCenter, Palette.TextDim);
            Ui.Size(note, 120);
            Ui.Spacer(Root, 60, 0);
        }

        /// <summary>방식 id별 버튼 모양. 구글 = 흰 바탕·색 G, Apple = 검은 바탕·흰 글자 (공식 버튼 그림이 오면 그것으로 바꾼다), 그 밖 = 보조 버튼.</summary>
        private Button Brand(Transform parent, SignInOption o, System.Action onClick)
        {
            string name = T(o.NameKey);
            if (!o.IsActive) name += "  " + T("ui.signIn.soon");
            Button b;
            switch (o.Id)
            {
                case "google":
                    b = Ui.Button(parent, "<color=#4285F4><b>G</b></color>   " + name, onClick, 130, Color.white);
                    b.GetComponentInChildren<Text>().color = new Color(0.12f, 0.12f, 0.12f);
                    break;
                case "apple":
                    b = Ui.Button(parent, name, onClick, 130, new Color(0.05f, 0.05f, 0.05f));
                    b.GetComponentInChildren<Text>().color = Color.white;
                    break;
                default:
                    b = Ui.SecondaryButton(parent, name, onClick, 130);
                    break;
            }
            if (!o.IsActive) Dim(b, true);
            return b;
        }

        private static void Dim(Button b, bool off)
        {
            b.interactable = !off;
            var g = b.GetComponent<CanvasGroup>();
            if (g == null) g = b.gameObject.AddComponent<CanvasGroup>();
            g.alpha = off ? 0.45f : 1f;
        }

        private void Press(SignInOption option, Button pressed, Text error)
        {
            error.text = "";
            var label = pressed.GetComponentInChildren<Text>();
            string before = label.text;
            label.text = T("ui.signIn.waiting");
            foreach (var (b, _) in _buttons) if (b != pressed) Dim(b, true);
            pressed.interactable = false;
            Ctx.SignIn(option.Id, ok =>
            {
                if (ok) { App.NextStartup(StartupSteps.SignIn); return; }
                if (pressed == null) return; // 화면이 이미 닫혔음 (Unity: 파괴된 객체는 == null)
                label.text = before;
                pressed.interactable = true;
                foreach (var (b, o) in _buttons) if (b != pressed) Dim(b, !o.IsActive);
                error.text = T("ui.signIn.failed");
            });
        }
    }

    /// <summary>
    /// 약관 동의 (startup.md ④, 시안 03): 이용약관·개인정보 처리방침 (둘 다 필수). 목록 맨 위 "전체 동의" 줄, 항목마다 체크 상자와 "보기 ›"(본문 화면).
    /// 약관 판이 오르면 다시 묻고 제목 아래에 알린다. 광고 동의(ATT 등)는 광고 단계에서.
    /// </summary>
    public sealed class TermsScreen : GameScreen
    {
        internal bool Terms, Privacy;

        public override void Build()
        {
            bool all = Terms && Privacy;
            Ui.Column(Root, 14, 50);
            var title = Ui.Label(Root, T("ui.terms.title"), Ui.FontTitle);
            title.font = Ui.TitleFont;
            Ui.Size(title, 110);
            Ui.Size(Ui.Label(Root, T("ui.terms.note"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim), 50);
            if (Ctx.Startup.IsReagree(Ctx.StartupRecord))
                Ui.Size(Ui.Label(Root, T("ui.terms.changed"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Bad), 50);

            var card = Ui.Card(Root, "List", 26, 4);
            Ui.CheckRow(card, T("ui.terms.allRow"), all, () => { bool v = !all; Terms = Privacy = v; Rebuild(); }, big: true);
            Ui.Divider(card);
            Item(card, "terms", Terms, v => Terms = v);
            Item(card, "privacy", Privacy, v => Privacy = v);

            Ui.Spacer(Root);
            if (!all) Ui.Hint(Root, T("ui.terms.needAll"), 50);
            var go = Ui.PrimaryButton(Root, T("ui.terms.accept"), () => { Ctx.AcceptTerms(); App.NextStartup(StartupSteps.Terms); }, 140);
            Ui.SetPrimaryEnabled(go, all);
        }

        private void Item(RectTransform card, string id, bool on, System.Action<bool> set) =>
            Ui.CheckRow(card, T("ui.terms." + id), on, () => { set(!on); Rebuild(); }, link: T("ui.terms.view"),
                onLink: () => App.Show(new TermsBodyScreen(id, Terms, Privacy)));

        private void Rebuild() => App.Show(new TermsScreen { Terms = Terms, Privacy = Privacy });
    }

    /// <summary>약관 본문 (시안 03 본문 보기): 제목 + 스크롤 본문 + [닫기]. 닫으면 체크 상태를 지닌 채 약관 화면으로.</summary>
    public sealed class TermsBodyScreen : GameScreen
    {
        private readonly string _id;
        private readonly bool _terms, _privacy;
        public TermsBodyScreen(string id, bool terms, bool privacy) { _id = id; _terms = terms; _privacy = privacy; }

        public override void Build()
        {
            Ui.Column(Root, 24, 50);
            var title = Ui.Label(Root, T("ui.terms.name." + _id), Ui.FontTitle);
            title.font = Ui.TitleFont;
            Ui.Size(title, 110);
            var box = Ui.Rect(Root, "BodyBox");
            Ui.Round(Ui.Background(box, Palette.Card));
            Ui.Size(box, flexibleHeight: 1);
            var list = Ui.ScrollList(box, "Body", 0, 32);
            Ui.Fill((RectTransform)list.parent.parent);
            Ui.Label(list, T("ui.terms.body." + _id), 30, TextAnchor.UpperLeft);
            Ui.SecondaryButton(Root, T("ui.common.close"), () => App.Show(new TermsScreen { Terms = _terms, Privacy = _privacy }), 140);
        }
    }
}
