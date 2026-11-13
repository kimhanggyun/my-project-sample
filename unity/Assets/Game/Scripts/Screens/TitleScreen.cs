using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>(임시) 타이틀. 누르면 메인 화면으로.</summary>
    public sealed class TitleScreen : GameScreen
    {
        public override void Build()
        {
            Ui.Column(Root, 40, 80).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Label(Root, "", Ui.FontBody), 460);
            // 제목: 먹 붓 획 위에 글자, 오른쪽 아래에 낙관 (art-direction.md 3장)
            var titleBox = Ui.Rect(Root, "TitleBox");
            Ui.Size(titleBox, 220);
            var stroke = Ui.Rect(titleBox, "Brush");
            stroke.anchorMin = new Vector2(0.02f, 0.08f);
            stroke.anchorMax = new Vector2(0.98f, 0.92f);
            stroke.offsetMin = stroke.offsetMax = Vector2.zero;
            var strokeImg = Ui.Background(stroke, Palette.IsLight ? new Color(0.17f, 0.15f, 0.12f, 0.92f) : new Color(0.9f, 0.86f, 0.78f, 0.12f));
            strokeImg.sprite = UiArt.Brush;
            strokeImg.raycastTarget = false;
            var title = Ui.Label(titleBox, T("ui.title.name"), Ui.FontTitle + 8, TextAnchor.MiddleCenter, Palette.IsLight ? new Color(0.97f, 0.94f, 0.87f) : Palette.Text);
            Ui.Fill(title.rectTransform);
            var stamp = Ui.Rect(titleBox, "Seal");
            stamp.anchorMin = stamp.anchorMax = new Vector2(0.86f, 0.06f);
            stamp.sizeDelta = new Vector2(92f, 92f);
            var stampImg = Ui.Background(stamp, Palette.Seal);
            stampImg.sprite = UiArt.Seal;
            stampImg.raycastTarget = false;
            var stampText = Ui.Label(stamp, T("ui.title.seal"), 40, TextAnchor.MiddleCenter, new Color(0.98f, 0.94f, 0.86f));
            stampText.font = Ui.TitleFont;
            Ui.Fill(stampText.rectTransform);
            Ui.Size(Ui.Label(Root, T("ui.title.tapToStart"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim), 80);
            var status = Ui.Label(Root, "", Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Size(status, 220);
            var start = Ui.Button(Root, T("ui.title.start"), () => App.EnterGame(), 160);
            // 온라인이면 서버 세이브를 받은 뒤 시작 (docs/impl/server.md 5장)
            if (Ctx.OnlineEnabled)
            {
                Ui.SetEnabled(start, false);
                status.text = T("ui.online.connecting");
                Ctx.StartSync(() =>
                {
                    if (status == null || start == null) return; // 동기화가 끝났을 때 타이틀이 이미 닫혔으면 무시 (Unity: 파괴된 객체는 == null)
                    status.text = T("ui.online." + Ctx.OnlineStatus);
                    Ui.SetEnabled(start, true);
                });
            }
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
