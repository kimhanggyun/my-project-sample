using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>닉네임 정하기 (공통 계정, 2~10자·글자·숫자·밑줄, 겹치면 안 됨).</summary>
    public sealed class NicknameScreen : GameScreen
    {
        private readonly string _notice;
        public NicknameScreen(string notice = null) { _notice = notice; }

        public override void Build()
        {
            Ui.Column(Root, 16, 48);
            Ui.Size(Ui.Label(Root, T("ui.nickname.title"), Ui.FontTitle), 120);
            Ui.Label(Root, T("ui.nickname.note"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            if (_notice != null) Ui.Label(Root, _notice, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Bad);
            var input = Ui.TextInput(Root, Ctx.Nickname ?? T("ui.nickname.placeholder"), Game.Online.NicknameRules.Max, 110, 34);
            Ui.Size(Ui.Label(Root, "", Ui.FontSmall), flexibleHeight: 1);
            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(row, T("ui.common.back"), () => App.Show(new SettingsScreen()));
            Ui.Button(row, T("ui.nickname.save"), () => Ctx.SetNickname(input.text, r =>
            {
                if (Root == null) return;
                if (r != null && r.Ok) App.Show(new SettingsScreen());
                else App.Show(new NicknameScreen(r == null ? T("ui.hire.failed") : T("ui.nickname." + r.Reason)));
            }));
        }
    }

    /// <summary>설정 (임시): 버전, 닉네임, 외부 서비스 설정, 세이브 초기화.</summary>
    public sealed class SettingsScreen : GameScreen
    {
        private bool _confirmReset;
        private Text _resetNote;

        public override void Build()
        {
            Ui.Column(Root, 16, 32);
            Ui.Size(Ui.Label(Root, T("ui.settings.header"), Ui.FontTitle), 120);

            var body = Ui.ScrollList(Root, "Body", 12, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);

            var info = Ui.Card(body, "Info");
            Ui.Label(info, T("ui.settings.version", Application.version, Application.unityVersion), Ui.FontSmall);
            Ui.Label(info, T("ui.settings.save", Application.persistentDataPath), 26, TextAnchor.MiddleLeft, Palette.TextDim);

            if (Ctx.OnlineEnabled)
            {
                var account = Ui.Card(body, "Account");
                var line = Ui.Label(account, T("ui.settings.nickname", Ctx.Nickname ?? T("ui.settings.noNickname")), Ui.FontSmall);
                Ui.Button(account, T("ui.settings.changeNickname"), () => App.Show(new NicknameScreen()), 90, fontSize: 28);
                Ctx.LoadProfile(p => { if (line != null && p != null) line.text = T("ui.settings.nickname", p.Nickname ?? T("ui.settings.noNickname")); });
            }

            var services = Ui.Card(body, "Services");
            Ui.Label(services, T("ui.settings.services"));
            foreach (var kv in Ctx.ServiceChoices) Ui.Label(services, kv.Key + ": " + kv.Value, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            var reset = Ui.Card(body, "Reset");
            Ui.Button(reset, T("ui.settings.reset"), OnReset, color: new Color(0.45f, 0.22f, 0.20f));
            _resetNote = Ui.Label(reset, "", Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain());
        }

        private void OnReset()
        {
            if (!_confirmReset)
            {
                _confirmReset = true;
                _resetNote.text = T("ui.settings.resetConfirm");
                return;
            }
            Ctx.ResetSave();
            _confirmReset = false;
            _resetNote.text = T("ui.settings.resetDone");
        }
    }
}
