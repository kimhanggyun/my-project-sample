using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 메인 화면 (main-screen.md): 위 자원 막대 + 아래 탭 바(가로 스크롤). 탭 목록·순서·상태는 데이터(`Ui/mainTabs.json`),
    /// 탭 내용은 <see cref="ITabView"/> 구현을 탭 id로 등록한다. 등록이 없거나 상태가 tobe면 "준비 중" 화면.
    /// </summary>
    public sealed class MainScreen : GameScreen
    {
        private const float TopBarHeight = 110f;
        private const float TabBarHeight = 104f;
        private const float MinTabWidth = 160f;
        private const int VisibleTabs = 5; // 배포 탭 5개가 한 줄에 다 보이게 (더 많으면 가로 스크롤)
        private const float DevTabBarHeight = 72f;
        private const float MinDevTabWidth = 120f;
        private const int VisibleDevTabs = 7;

        /// <summary>탭 id → 내용. 새 탭은 구현을 만들고 여기에 한 줄 + mainTabs.json에 한 줄.</summary>
        private static readonly Dictionary<string, System.Func<ITabView>> Views = new Dictionary<string, System.Func<ITabView>>
        {
            { "story", () => new StoryTabView() },
            { "general", () => new GeneralsTabView() },
            { "prisoner", () => new PrisonerTabView() },
            { "formation", () => new FormationTabView() },
            { "hiring", () => new HiringTabView() },
            { "troop", () => new TroopGrowthTabView() },
            { "territory", () => new TerritoryTabView() },
            // 따로 열던 화면도 탭으로 (나중에 필요 없는 탭은 mainTabs.json에서 hidden으로)
            { "practice", () => new PracticeTabView() },
            { "care", () => new TroopCareTabView() },
            { "formationDrag", () => new FormationDragTabView() },
            { "shop", () => new ShopTabView() },
            { "gacha", () => new ScreenTabView(() => new GachaScreen()) },
            { "codex", () => new ScreenTabView(() => new CodexScreen()) },
            { "replay", () => new ScreenTabView(() => new ReplayListScreen()) },
            { "mail", () => new ScreenTabView(() => new MailScreen()) },
            { "alliance", () => new ScreenTabView(() => new AllianceScreen()) },
            { "settings", () => new ScreenTabView(() => new SettingsScreen()) },
            { "master", () => new MasterTabView() },
        };

        private readonly string _initial;
        private RectTransform _content;
        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();

        /// <param name="initialTab">처음 열 탭 id (없으면 첫 번째 사용 탭).</param>
        public MainScreen(string initialTab = null) { _initial = initialTab; }

        public override void Build()
        {
            Ctx.Flow.Tick(Ctx.State); // 화면을 열 때마다 시간 경과 처리 (부상 회복·구출 기한)
            Ui.Column(Root, 0);

            // 위: 자원 + 설정
            var top = Ui.Row(Root, "TopBar", TopBarHeight, 16);
            Ui.Background(top, Palette.Bar);
            top.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(24, 24, 14, 14);
            // 자원마다 [+] → 상점 (main-screen.md 1장, economy.md 3.1), 상점·설정 버튼
            foreach (var id in new[] { ResourceIds.Supply, ResourceIds.Gold, ResourceIds.Gem })
            {
                // 자원 표식: 낙관(붉은 도장)에 한 글자 (糧·錢·玉, 문구 데이터) + 수량
                var seal = Ui.Rect(top, "Seal");
                var sealImg = Ui.Background(seal, Palette.Seal);
                sealImg.sprite = UiArt.Seal;
                sealImg.raycastTarget = false;
                Ui.Size(seal, 52, 52).flexibleWidth = 0;
                var glyph = Ui.Label(seal, T("ui.top.seal." + id), 30, TextAnchor.MiddleCenter, new Color(0.98f, 0.94f, 0.86f));
                glyph.font = Ui.TitleFont; // 한자는 제목 글꼴(본명조)에 있다
                Ui.Fill(glyph.rectTransform);
                Ui.Size(Ui.Label(top, Ctx.Resource(id).ToString("N0"), 26, TextAnchor.MiddleLeft), flexibleWidth: 2);
                Ui.Size(Ui.Button(top, "+", () => App.ShowMain("shop"), 64, Palette.ButtonActive, 30), width: 56);
            }
            Ui.Size(Ui.Button(top, T("ui.top.shop"), () => App.ShowMain("shop"), 80, fontSize: 26), width: 110);
            Ui.Size(Ui.Button(top, T("ui.top.settings"), () => App.Show(new SettingsScreen()), 80, fontSize: 26), width: 110);

            // 가운데: 탭 내용
            _content = Ui.Rect(Root, "Content");
            Ui.Size(_content, flexibleHeight: 1);

            // 아래: 탭 바 두 줄 (main-screen.md 3장). 위 줄 = 개발용(개발 빌드·마스터만), 아래 줄 = 배포 탭. 줄마다 가로 스크롤.
            var tabs = new List<MainTabEntry>();
            var alerts = Game.Flow.TabAlerts.Compute(Ctx.Defs, Ctx.State, Ctx.Flow, Ctx.Clock.UtcNow.ToUnixTimeSeconds());
            bool dev = Ctx.DevToolsVisible;
            foreach (var row in new[] { MainTabEntry.RowDev, MainTabEntry.RowRelease })
            {
                if (row == MainTabEntry.RowDev && !dev) continue;
                var rowTabs = new List<MainTabEntry>();
                foreach (var t in Ctx.MainTabs)
                    if (t.Status != MainTabEntry.Hidden && (t.Row == row || (row == MainTabEntry.RowRelease && t.Row != MainTabEntry.RowDev))) rowTabs.Add(t);
                if (rowTabs.Count == 0) continue;
                if (row == MainTabEntry.RowRelease) tabs.InsertRange(0, rowTabs); else tabs.AddRange(rowTabs); // 처음 열 탭은 배포 줄에서 먼저 찾는다
                bool small = row == MainTabEntry.RowDev;
                float h = small ? DevTabBarHeight : TabBarHeight;
                var barBg = Ui.Rect(Root, small ? "DevTabBar" : "TabBar");
                Ui.Background(barBg, small ? Palette.Card : Palette.Bar);
                Ui.Size(barBg, h);
                var bar = Ui.HScrollRow(barBg, small ? "DevTabs" : "Tabs", h, 6, 8);
                Ui.Fill((RectTransform)bar.parent.parent);
                int visible = small ? VisibleDevTabs : VisibleTabs;
                float width = Mathf.Max(small ? MinDevTabWidth : MinTabWidth, (GameApp.ReferenceWidth - 16f) / Mathf.Min(visible, rowTabs.Count) - 6f);
                foreach (var t in rowTabs)
                {
                    var tab = t;
                    var b = Ui.Button(bar, T(t.NameKey), () => Select(tab), h - 16, fontSize: small ? 22 : 28);
                    Ui.Size(b, width: width);
                    if (t.Status != MainTabEntry.Active) b.GetComponentInChildren<Text>().color = Palette.TextDim;
                    _tabButtons[t.Id] = b;
                    if (alerts.Contains(t.Id) && t.Status == MainTabEntry.Active) Dot(b); // 할 일이 있는 탭 (E21)
                }
            }

            MainTabEntry first = null;
            foreach (var t in tabs)
            {
                if (t.Id == _initial) { first = t; break; }
                if (first == null && t.Status == MainTabEntry.Active) first = t;
            }
            if (first != null) Select(first);
        }

        /// <summary>탭 버튼 오른쪽 위 빨간 점.</summary>
        private static void Dot(Button b)
        {
            var dot = Ui.Rect(b.transform, "AlertDot");
            dot.anchorMin = dot.anchorMax = new Vector2(1f, 1f);
            dot.pivot = new Vector2(1f, 1f);
            dot.sizeDelta = new Vector2(22f, 22f);
            dot.anchoredPosition = new Vector2(-6f, -6f);
            var img = Ui.Background(dot, Palette.Seal);
            img.sprite = UiShapes.Circle;
            img.raycastTarget = false;
            var le = dot.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        private void Select(MainTabEntry tab)
        {
            Ui.Clear(_content);
            foreach (var kv in _tabButtons) Ui.Recolor(kv.Value, kv.Key == tab.Id ? Palette.ButtonActive : Palette.Button);
            var root = Ui.Panel(_content, tab.Id);
            ITabView view = tab.Status == MainTabEntry.Active && Views.TryGetValue(tab.Id, out var make) ? make() : new ComingSoonTabView(tab);
            view.Build(App, root);
            if (tab.Status == MainTabEntry.Active) GuideOverlay.Show(App, Root, "tab." + tab.Id); // 처음 안내 (R-62)
        }
    }

    /// <summary>메인 화면의 탭 내용 하나.</summary>
    public interface ITabView
    {
        void Build(GameApp app, RectTransform root);
    }

    /// <summary>원래 한 화면이던 것(모집·도감·녹화·우편·연합·설정)을 탭 안에 그린다. 화면의 버튼으로 다른 화면을 열면 그 화면으로 넘어간다.</summary>
    public sealed class ScreenTabView : ITabView
    {
        private readonly System.Func<GameScreen> _make;
        public ScreenTabView(System.Func<GameScreen> make) { _make = make; }

        public void Build(GameApp app, RectTransform root)
        {
            var screen = _make();
            screen.Attach(app, root);
            screen.Build();
        }
    }

    /// <summary>준비 중인 탭 (main-screen.md 2장: TO-BE 탭은 자리를 두고 "준비 중" 화면).</summary>
    public sealed class ComingSoonTabView : ITabView
    {
        private readonly MainTabEntry _tab;
        public ComingSoonTabView(MainTabEntry tab) { _tab = tab; }

        public void Build(GameApp app, RectTransform root)
        {
            Ui.Column(root, 24, 48).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Label(root, app.Context.T(_tab.NameKey), Ui.FontTitle, TextAnchor.MiddleCenter), 120);
            Ui.Label(root, app.Context.T("ui.tab.comingSoon"), Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim);
        }
    }

    /// <summary>장수 키우기 (일부): 지금은 보유 장수 목록. 스킬북·특성서·대결 전적 보상은 다음 단계.</summary>
    public sealed class GeneralsTabView : ITabView
    {
        private static long Stat(GameContext ctx, GeneralState g, string id) => CorpsValidator.EffectiveStat(ctx.Defs, g, id).FloorToLong();

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List");
            Ui.Fill((RectTransform)list.parent.parent);
            Ui.Label(list, ctx.T("ui.tab.generalNote"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            if (ctx.Defs.Gacha.Banners.Count > 0) Ui.Button(list, ctx.T("ui.gacha.button"), () => app.Show(new GachaScreen()), 100, Palette.ButtonActive);
            foreach (var g in ctx.State.Generals)
            {
                var color = g.Status == GeneralStatus.Available ? Palette.Good : g.Status == GeneralStatus.Captured ? Palette.Bad : Palette.TextDim;
                string id = g.GeneralId;
                string stats = ctx.T("ui.generals.stats", Stat(ctx, g, "leadership"), Stat(ctx, g, "might"), Stat(ctx, g, "intellect"));
                string grade = ctx.Defs.Generals.TryGetValue(id, out var gdef) ? GradeLabel.Tag(ctx, gdef.Grade) : ""; // 등급 (E25)
                var b = Ui.Button(list, grade + ctx.T("ui.generals.line", ctx.GeneralName(id), g.Level, Ui.Colored(ctx.T("ui.generals." + g.Status), color)) + "\n" + Ui.Colored(stats, Palette.TextDim),
                    () => app.Show(new GeneralScreen(id)), 124, Palette.Card, 32);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
        }
    }
}
