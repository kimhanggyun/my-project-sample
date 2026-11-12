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

            // 개발 빌드: 서버 연결 고르기 (설정 파일을 고치거나 다시 빌드하지 않고 PC 서버에 붙는다)
            if (Debug.isDebugBuild)
            {
                var server = Ui.Card(body, "Server");
                Ui.Label(server, T("ui.settings.server"));
                string now = T("ui.settings.mode." + Ctx.OnlineMode) + (Ctx.OnlineMode == Game.Online.OnlineConfig.Http ? "  " + Ctx.OnlineUrl : "");
                Ui.Label(server, T("ui.settings.serverNow", now), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
                var url = Ui.TextInput(server, "http://192.168.0.10:5000", 80, 90, 28);
                url.text = Ctx.OnlineOverride?.Url ?? Ctx.OnlineUrl ?? "";
                var row = Ui.Row(server, "Modes", 90, 8);
                Ui.Button(row, T("ui.settings.mode.off"), () => ApplyServer(Game.Online.OnlineConfig.Off, null), 90, fontSize: 26);
                Ui.Button(row, T("ui.settings.mode.fake"), () => ApplyServer(Game.Online.OnlineConfig.Fake, null), 90, fontSize: 26);
                Ui.Button(row, T("ui.settings.mode.http"), () => ApplyServer(Game.Online.OnlineConfig.Http, url.text.Trim()), 90, fontSize: 26);
                _serverNote = Ui.Label(server, T("ui.settings.serverNote"), 24, TextAnchor.MiddleLeft, Palette.TextDim);

                // 화면 스타일 비교 (UI 1단계 임시, Ui/styles.json)
                var style = Ui.Card(body, "Style");
                Ui.Label(style, T("ui.settings.style"));
                var styles = Ui.Row(style, "Styles", 90, 8);
                foreach (var kv in UiStyle.Available)
                {
                    string id = kv.Key;
                    var b = Ui.Button(styles, T(kv.Value), () => { Ctx.SetStyleOverride(id); App.Reboot(); }, 90, id == UiStyle.Current ? Palette.ButtonActive : (Color?)null, 26);
                }
            }

            // 전체 난이도 (ai.md 7장, R-75): 스텝 난이도에서 한 칸 쉽게·어렵게. 세이브에 남아 서버도 같은 난이도로 확인한다
            var difficulty = Ui.Card(body, "Difficulty");
            Ui.Label(difficulty, T("ui.settings.difficulty"));
            var drow = Ui.Row(difficulty, "Levels", 90, 8);
            string current = Ctx.State.Difficulty ?? "normal";
            foreach (var id in Ctx.Defs.Ai.SettingShift.Keys)
            {
                string pick = id;
                Ui.Button(drow, T("ui.difficulty." + id), () => { Ctx.Do(Game.Flow.PlayerActionRunner.SetDifficulty(pick)); App.Show(new SettingsScreen()); },
                    90, id == current ? Palette.ButtonActive : (Color?)null, 28);
            }
            Ui.Label(difficulty, T("ui.settings.difficultyNote." + current), 24, TextAnchor.MiddleLeft, Palette.TextDim);

            var services = Ui.Card(body, "Services");
            Ui.Label(services, T("ui.settings.services"));
            foreach (var kv in Ctx.ServiceChoices) Ui.Label(services, kv.Key + ": " + kv.Value, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            var reset = Ui.Card(body, "Reset");
            Ui.Button(reset, T("ui.settings.reset"), OnReset, color: new Color(0.45f, 0.22f, 0.20f));
            _resetNote = Ui.Label(reset, "", Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain());
        }

        private Text _serverNote;

        /// <summary>고른 서버 연결을 저장하고 처음부터 다시 시작한다.</summary>
        private void ApplyServer(string mode, string url)
        {
            if (mode == Game.Online.OnlineConfig.Http && (string.IsNullOrEmpty(url) || !(url.StartsWith("http://") || url.StartsWith("https://"))))
            {
                _serverNote.text = Ui.Colored(T("ui.settings.serverBadUrl"), Palette.Bad);
                return;
            }
            Ctx.SetOnlineOverride(mode, url);
            App.Reboot();
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
