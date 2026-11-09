using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>(임시) 타이틀. 누르면 메인 화면으로.</summary>
    public sealed class TitleScreen : GameScreen
    {
        public override void Build()
        {
            Ui.Column(Root, 40, 80).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Label(Root, "", Ui.FontBody), 500);
            Ui.Size(Ui.Label(Root, T("ui.title.name"), Ui.FontTitle, TextAnchor.MiddleCenter), 160);
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
