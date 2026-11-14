using System.Collections.Generic;
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

    /// <summary>
    /// 설정 (15 시안): 한 페이지 + 구역 머리. 내 장수 · 계정(로그인 · 계정 번호 복사 · 로그아웃 · 계정 삭제 R-102) · 게임(난이도 · 시작 배속) · 소리(배경음 · 효과음) ·
    /// 알림(종류별) · 위치 · 구매(광고 제거 · 구매 복원) · 도움(안내 다시 · 문의 · 약관 · 버전) · 개발용(개발 빌드 · 마스터만: 서버 · 화면 스타일 · 세이브 위치 · 초기화).
    /// </summary>
    public sealed class SettingsScreen : GameScreen
    {
        private static readonly string[] NotifyKinds = { "attacked", "allyHelp", "incomeFull" };
        private readonly string _notice;
        private bool _confirmReset;
        private Text _resetNote;
        private Text _serverNote;

        public SettingsScreen(string notice = null) { _notice = notice; }

        public override void Build()
        {
            var (header, body, _) = Ui.PageLayout(Root, 0);
            var top = Ui.Row(header, "Title", 90, 12);
            Ui.Size(Ui.Button(top, "‹", () => App.ShowMain(), 84, fontSize: 40), width: 90);
            Ui.Size(Ui.Label(top, T("ui.settings.header"), Ui.FontTitle - 6), flexibleWidth: 1);
            if (_notice != null) Ui.Label(header, _notice, 24, TextAnchor.MiddleLeft, Palette.Good);

            BuildMe(body);
            BuildAccount(body);
            BuildGame(body);
            BuildSound(body);
            BuildNotify(body);
            BuildLocation(body);
            BuildPurchase(body);
            BuildHelp(body);
            if (Ctx.DevToolsVisible) BuildDev(body);
        }

        private RectTransform Section(RectTransform body, string key)
        {
            Ui.Label(body, T("ui.settings.sec." + key), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            return Ui.Card(body, key, 16, 6);
        }

        /// <summary>설정 줄: 아이콘 원 · 이름 (+ 작은 설명) · 오른쪽 칸.</summary>
        private RectTransform Line(RectTransform card, string icon, string name, string sub = null, Color? nameColor = null)
        {
            var row = Ui.Row(card, name, 90, 12);
            var dot = Ui.Rect(row, "Icon");
            Ui.Size(dot, 56, 56);
            Ui.Background(dot, new Color(0.9f, 0.86f, 0.78f)).sprite = UiShapes.Circle;
            var glyph = Ui.Label(dot, icon, 26, TextAnchor.MiddleCenter, Palette.Text);
            glyph.font = Ui.TitleFont; // 한자 아이콘은 제목 글꼴(본명조)에 있다
            Ui.Fill(glyph.rectTransform);
            var col = Ui.Rect(row, "Name");
            Ui.Size(col, flexibleWidth: 1);
            Ui.Column(col, 0, 0).childAlignment = TextAnchor.MiddleLeft;
            Ui.Label(col, name, 28, TextAnchor.MiddleLeft, nameColor);
            if (sub != null) Ui.Label(col, sub, 20, TextAnchor.MiddleLeft, Palette.TextDim);
            return row;
        }

        private Button Toggle(RectTransform row, bool on, System.Action flip)
        {
            var b = Ui.Button(row, T(on ? "ui.settings.on" : "ui.settings.off"), flip, 56, on ? Palette.Good : (Color?)null, 24);
            Ui.Size(b, width: 120);
            return b;
        }

        private void Arrow(RectTransform row, string text, System.Action go, bool enabled = true)
        {
            var b = Ui.Button(row, text, go, 60, fontSize: 24);
            Ui.Size(b, width: 210);
            Ui.SetEnabled(b, enabled);
        }

        // ---- 내 장수 ----
        private void BuildMe(RectTransform body)
        {
            var card = Ui.Card(body, "Me", 18, 6);
            var row = Ui.Row(card, "Row", 150, 16);
            var pic = Ui.Rect(row, "Pic");
            Ui.Size(pic, 140, 140);
            var me = Ctx.State.FindGeneral(Game.Flow.AvatarGeneral.GeneralId);
            new Portrait(pic, 140).Show(Game.Flow.AvatarGeneral.GeneralId, Ctx.GeneralName(Game.Flow.AvatarGeneral.GeneralId), Palette.Seal);
            var col = Ui.Rect(row, "Name");
            Ui.Size(col, flexibleWidth: 1);
            Ui.Column(col, 6, 0).childAlignment = TextAnchor.MiddleLeft;
            var name = Ui.Label(col, Ctx.Nickname ?? T("ui.settings.noNickname"), 40);
            var tags = Ui.Row(col, "Tags", 44, 8);
            Ui.Size(Ui.Tag(tags, T("ui.settings.rank", Ctx.State.LordLevel), Palette.Ally, 20, 40), width: 150);
            if (me != null) Ui.Size(Ui.Tag(tags, "Lv." + me.Level, new Color(0.16f, 0.13f, 0.1f), 20, 40), width: 110);
            var change = Ui.Button(row, T("ui.settings.change"), () => App.Show(new NicknameScreen()), 70, fontSize: 24);
            Ui.Size(change, width: 210);
            Ui.SetEnabled(change, Ctx.OnlineEnabled);
            if (Ctx.OnlineEnabled) Ctx.LoadProfile(p => { if (name != null && p != null && p.Nickname != null) name.text = p.Nickname; });
        }

        // ---- 계정 ----
        private void BuildAccount(RectTransform body)
        {
            var card = Section(body, "account");
            string method = Ctx.StartupRecord?.SignInMethod ?? "guest";
            var signIn = Line(card, method == "google" ? "G" : method == "apple" ? "A" : "客", T("ui.signIn.method." + method));
            Ui.Size(Ui.Tag(signIn, method == "guest" ? T("ui.settings.guest") : T("ui.settings.linked"), method == "guest" ? Palette.TextDim : Palette.Good, 20, 44), width: 160);
            if (Ctx.OnlineEnabled && Ctx.AccountId != null)
            {
                var id = Line(card, "番", T("ui.settings.accountId"), Ctx.AccountId);
                Arrow(id, T("ui.settings.copy"), () => { GUIUtility.systemCopyBuffer = Ctx.AccountId; App.Show(new SettingsScreen(T("ui.settings.copied"))); });
            }
            var logout = Line(card, "出", T("ui.settings.logout"));
            Arrow(logout, "›", () => Ui.Confirm(Root, T("ui.settings.logout"), T("ui.settings.logoutBody"), T("ui.settings.logout"), () => { Ctx.SignOut(); App.Reboot(); }, (T("ui.common.cancel"), () => { })));
            var del = Line(card, "削", T("ui.settings.deleteAccount"), T("ui.settings.irreversible"), Palette.Bad);
            Arrow(del, "›", DeleteDialog, Ctx.OnlineEnabled);
        }

        /// <summary>계정 삭제 확인 창: 사라지는 것 · 7일 유예 · "삭제"를 입력해야 [삭제] 켜짐 (R-102).</summary>
        private void DeleteDialog()
        {
            var dim = Ui.Rect(Root, "DeleteDim");
            dim.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Fill(dim);
            Ui.Background(dim, new Color(0.08f, 0.06f, 0.05f, 0.6f));
            dim.SetAsLastSibling();
            var box = Ui.Rect(dim, "Box");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(960, 820);
            Ui.Round(Ui.Background(box, Palette.Background));
            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.Bad;
            outline.effectDistance = new Vector2(4, -4);
            Ui.Column(box, 14, 36);
            Ui.Size(Ui.Label(box, T("ui.settings.delete.title"), 44, TextAnchor.MiddleCenter, Palette.Bad), 80);
            Ui.Label(box, T("ui.settings.delete.lost"), 24, TextAnchor.MiddleLeft, Palette.Text);
            Ui.Label(box, T("ui.settings.delete.grace", Game.Online.AccountDeletion.GraceSeconds / 86400), 24, TextAnchor.MiddleLeft, Palette.Good);
            Ui.Label(box, T("ui.settings.delete.type"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            var input = Ui.TextInput(box, T("ui.settings.delete.word"), 10, 90, 30);
            var row = Ui.Row(box, "Buttons", 96, 16);
            Ui.Button(row, T("ui.common.cancel"), () => Object.Destroy(dim.gameObject), 96, fontSize: 28);
            var go = Ui.Button(row, T("ui.settings.delete.button"), () =>
            {
                Ctx.RequestAccountDeletion(r =>
                {
                    if (r == null || !r.Ok) { App.Show(new SettingsScreen(T("ui.hire.failed"))); return; }
                    Ctx.SignOut();
                    App.Reboot();
                });
            }, 96, Palette.Danger, 28);
            Ui.SetEnabled(go, false);
            input.onValueChanged.AddListener(v => Ui.SetEnabled(go, v.Trim() == T("ui.settings.delete.word")));
        }

        // ---- 게임 ----
        private void BuildGame(RectTransform body)
        {
            var card = Section(body, "game");
            var diff = Line(card, "難", T("ui.settings.difficulty"));
            string current = Ctx.State.Difficulty ?? "normal";
            var levels = new List<(string, string)>();
            foreach (var id in Ctx.Defs.Ai.SettingShift.Keys) levels.Add((id, T("ui.difficulty." + id)));
            Ui.Size(Ui.SegmentTabs(diff, levels, current, id => { Ctx.Do(Game.Flow.PlayerActionRunner.SetDifficulty(id)); App.Show(new SettingsScreen()); }, 56, 120), width: 420);
            Ui.Label(card, T("ui.settings.difficultyNote." + current), 20, TextAnchor.MiddleLeft, Palette.TextDim);
            var speed = Line(card, "速", T("ui.settings.battleSpeed"));
            Ui.Size(Ui.SegmentTabs(speed, new[] { ("1", "×1"), ("2", "×2"), ("4", "×4") }, Ctx.DefaultBattleSpeed.ToString(), id => { Ctx.SetDefaultBattleSpeed(int.Parse(id)); App.Show(new SettingsScreen()); }, 56, 110), width: 380);
        }

        // ---- 소리 ----
        private void BuildSound(RectTransform body)
        {
            var card = Section(body, "sound");
            Volume(card, "樂", T("ui.settings.bgm"), Ctx.BgmVolume, Ctx.SetBgmVolume);
            Volume(card, "音", T("ui.settings.sfx"), Ctx.SfxVolume, v => { Ctx.SetSfxVolume(v); Sfx.Play("click"); });
        }

        /// <summary>막대 조절: 아이콘 = 음소거, − 막대 + , 값.</summary>
        private void Volume(RectTransform card, string icon, string name, int value, System.Action<int> set)
        {
            var row = Ui.Row(card, name, 80, 10);
            var mute = Ui.Button(row, icon, () => { set(value == 0 ? 100 : 0); App.Show(new SettingsScreen()); }, 64, value == 0 ? Palette.Card : (Color?)null, 28);
            Ui.Size(mute, width: 70);
            var muteText = mute.GetComponentInChildren<Text>();
            if (muteText != null) { muteText.font = Ui.TitleFont; if (value == 0) muteText.color = Palette.TextDim; }
            Ui.Size(Ui.Label(row, name, 26), width: 140);
            Ui.Size(Ui.Button(row, "−", () => { set(value - 10); App.Show(new SettingsScreen()); }, 60, fontSize: 30), width: 70);
            var g = Ui.Gauge(row, Palette.Ally, 22);
            Ui.Size(g, flexibleWidth: 1);
            Ui.SetGauge((RectTransform)g.GetChild(0), value / 100f);
            Ui.Size(Ui.Button(row, "+", () => { set(value + 10); App.Show(new SettingsScreen()); }, 60, fontSize: 30), width: 70);
            Ui.Size(Ui.Label(row, value.ToString(), 24, TextAnchor.MiddleRight), width: 60);
        }

        // ---- 알림 ----
        private void BuildNotify(RectTransform body)
        {
            var card = Section(body, "notify");
            foreach (var k in NotifyKinds)
            {
                string kind = k;
                bool on = Ctx.NotifyOn(kind);
                var row = Line(card, "鈴", T("ui.settings.notify." + kind), T("ui.settings.notifySub." + kind));
                Toggle(row, on, () => { Ctx.SetNotify(kind, !on); App.Show(new SettingsScreen()); });
            }
            Ui.Label(card, T("ui.settings.notifyNote"), 20, TextAnchor.MiddleLeft, Palette.TextDim);
        }

        // ---- 위치 ----
        private void BuildLocation(RectTransform body)
        {
            var card = Section(body, "location");
            bool available = Ctx.Location != null && Ctx.Location.IsAvailable;
            var row = Line(card, "地", T("ui.settings.locationPerm"), T("ui.settings.locationSub"));
            Ui.Size(Ui.Tag(row, T(available ? "ui.settings.allowed" : "ui.settings.off"), available ? Palette.Good : Palette.TextDim, 20, 44), width: 150);
        }

        // ---- 구매 ----
        private void BuildPurchase(RectTransform body)
        {
            var card = Section(body, "purchase");
            long now = Ctx.Clock.UtcNow.ToUnixTimeSeconds();
            bool adFree = Ctx.State.AdFreeForever || Ctx.State.AdFreeUntilUnixSeconds > now;
            var ad = Line(card, "広", T("ui.settings.adFree"));
            if (adFree) Ui.Size(Ui.Tag(ad, T("ui.settings.inUse"), Palette.Good, 20, 44), width: 160);
            else Arrow(ad, T("ui.settings.toShop"), () => App.ShowMain("shop"));
            var restore = Line(card, "復", T("ui.settings.restore"));
            Arrow(restore, "›", () =>
            {
                if (Ctx.Store == null) return;
                Ctx.Store.RestorePurchases(list => App.Show(new SettingsScreen(T("ui.settings.restored", list?.Count ?? 0))));
            }, Ctx.Store != null && Ctx.Store.IsAvailable);
        }

        // ---- 도움 ----
        private void BuildHelp(RectTransform body)
        {
            var card = Section(body, "help");
            var guides = Line(card, "導", T("ui.settings.guidesAgain"));
            Arrow(guides, "›", () =>
            {
                bool ok = Ctx.Do(Game.Flow.PlayerActionRunner.ResetGuides());
                App.Show(new SettingsScreen(T(ok ? "ui.settings.guidesReset" : "ui.settings.guidesNone")));
            });
            foreach (var k in new[] { "contact", "terms", "privacy", "locationTerms" })
            {
                string key = k;
                Ctx.Startup.Links.TryGetValue(key, out var url);
                var row = Line(card, key == "contact" ? "問" : "約", T("ui.settings." + key));
                Arrow(row, string.IsNullOrEmpty(url) ? T("ui.settings.soon") : "›", () =>
                {
                    if (key == "contact" && Ctx.AccountId != null) GUIUtility.systemCopyBuffer = Ctx.AccountId; // 문의에 계정 번호가 같이 가게
                    Application.OpenURL(url);
                }, !string.IsNullOrEmpty(url));
            }
            var ver = Line(card, "版", T("ui.settings.versionLine"));
            Ui.Size(Ui.Label(ver, Application.version, 24, TextAnchor.MiddleRight, Palette.TextDim), width: 200);
        }

        // ---- 개발용 (개발 빌드 · 마스터만) ----
        private void BuildDev(RectTransform body)
        {
            var card = Section(body, "dev");
            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.TextDim;
            outline.effectDistance = new Vector2(3, -3);
            Ui.Size(Ui.Tag(card, T("ui.settings.devOnly"), Palette.TextDim, 20, 40), width: 360);

            string now = T("ui.settings.mode." + Ctx.OnlineMode) + (Ctx.OnlineMode == Game.Online.OnlineConfig.Http ? "  " + Ctx.OnlineUrl : "");
            Ui.Label(card, T("ui.settings.server") + "  " + Ui.Colored(now, Palette.TextDim), 24);
            var url = Ui.TextInput(card, "http://192.168.0.10:5000", 80, 80, 26);
            url.text = Ctx.OnlineOverride?.Url ?? Ctx.OnlineUrl ?? "";
            var modes = Ui.Row(card, "Modes", 80, 8);
            Ui.Button(modes, T("ui.settings.mode.off"), () => ApplyServer(Game.Online.OnlineConfig.Off, null), 80, fontSize: 24);
            Ui.Button(modes, T("ui.settings.mode.fake"), () => ApplyServer(Game.Online.OnlineConfig.Fake, null), 80, fontSize: 24);
            Ui.Button(modes, T("ui.settings.mode.http"), () => ApplyServer(Game.Online.OnlineConfig.Http, url.text.Trim()), 80, fontSize: 24);
            _serverNote = Ui.Label(card, T("ui.settings.serverNote"), 20, TextAnchor.MiddleLeft, Palette.TextDim);

            Ui.Label(card, T("ui.settings.style"), 24);
            var styles = Ui.Row(card, "Styles", 80, 8);
            foreach (var kv in UiStyle.Available)
            {
                string id = kv.Key;
                Ui.Button(styles, T(kv.Value), () => { Ctx.SetStyleOverride(id); App.Reboot(); }, 80, id == UiStyle.Current ? Palette.ButtonActive : (Color?)null, 24);
            }

            var save = Line(card, "存", T("ui.settings.savePath"), Application.persistentDataPath);
            Arrow(save, T("ui.settings.copy"), () => { GUIUtility.systemCopyBuffer = Application.persistentDataPath; App.Show(new SettingsScreen(T("ui.settings.copied"))); });
            foreach (var kv in Ctx.ServiceChoices) Ui.Label(card, kv.Key + ": " + kv.Value, 20, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Button(card, T("ui.settings.reset"), OnReset, 80, Palette.Danger, 26);
            _resetNote = Ui.Label(card, "", 22, TextAnchor.MiddleLeft, Palette.TextDim);
        }

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
