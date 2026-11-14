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
            { "parts", () => new PartsTabView() }, // 부품 미리보기 (공통부품.md 0장)
            { "sampleBattle", () => new SampleBattleTabView() },
            { "map", () => new MapTabView() },
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

            // 위: 자원 칩 3개 + 상점·설정 아이콘 버튼 (시안 04). 자원마다 [+] → 상점 (main-screen.md 1장, economy.md 3.1)
            var top = Ui.Row(Root, "TopBar", TopBarHeight, 16);
            Ui.Background(top, Palette.Bar);
            var topRow = top.GetComponent<HorizontalLayoutGroup>();
            topRow.padding = new RectOffset(20, 20, 18, 18);
            topRow.childForceExpandWidth = false;
            topRow.childForceExpandHeight = false;
            topRow.childAlignment = TextAnchor.MiddleLeft;
            foreach (var id in new[] { ResourceIds.Supply, ResourceIds.Gold, ResourceIds.Gem })
                Ui.ResourceChip(Ctx, top, id, Ctx.Resource(id), () => App.ShowMain("shop"));
            Ui.IconButton(top, "shop", T("ui.top.shop"), () => App.ShowMain("shop"));
            Ui.IconButton(top, "settings", T("ui.top.settings"), () => App.Show(new SettingsScreen()));
            // 띠 배너 광고 자리 (광고가 꺼졌거나 광고 제거 상품이면 0)
            Ui.BannerSlot(Root, Ctx);

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
                if (rowTabs.Count > visible) Ui.MoreHint(barBg, h, small ? Palette.Card : Palette.Bar); // 넘치는 줄: 밀어서 더 본다는 표시
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

        private ITabView _view;
        private string _viewId;

        /// <summary>저장 안 한 것을 버리게 되면 먼저 묻는다 (확인 창: 저장하고 나가기 · 저장 안 하고 나가기 · 취소).</summary>
        public static void AskLeave(GameApp app, ILeaveGuard guard, System.Action go)
        {
            var ctx = app.Context;
            var root = app.CurrentRoot;
            Ui.Confirm(root, ctx.T("ui.leave.title"), ctx.T("ui.leave.body"), ctx.T("ui.leave.save"), () => { guard.SaveForLeave(app); go(); },
                (ctx.T("ui.leave.discard"), () => { guard.Discard(app); go(); }), (ctx.T("ui.common.cancel"), null));
        }

        private void Select(MainTabEntry tab)
        {
            if (_view is ILeaveGuard g && _viewId != tab.Id && g.HasUnsaved(App)) { AskLeave(App, g, () => { _view = null; Select(tab); }); return; }
            UiStyle.Page = null;
            Ui.Clear(_content);
            foreach (var kv in _tabButtons) Ui.Recolor(kv.Value, kv.Key == tab.Id ? Palette.ButtonActive : Palette.Button);
            var root = Ui.Panel(_content, tab.Id);
            ITabView view = tab.Status == MainTabEntry.Active && Views.TryGetValue(tab.Id, out var make) ? make() : new ComingSoonTabView(tab);
            _view = view;
            _viewId = tab.Id;
            view.Build(App, root);
            if (tab.Status == MainTabEntry.Active) GuideOverlay.Show(App, Root, "tab." + tab.Id); // 처음 안내 (R-62)
        }
    }

    /// <summary>저장 안 한 것이 있는 탭 (편성): 다른 탭으로 갈 때 묻는다.</summary>
    public interface ILeaveGuard
    {
        bool HasUnsaved(GameApp app);
        void SaveForLeave(GameApp app);
        void Discard(GameApp app);
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

    /// <summary>
    /// 장수 탭 (시안 05): 머리 줄("장수 n명" · [정렬 ▾] · [모집] · ⓘ) + 두 줄 격자 장수 카드. 카드 = 장수 화면, 카드의 작은 ⓘ = 능력치 말풍선.
    /// </summary>
    public sealed class GeneralsTabView : ITabView
    {
        /// <summary>고른 정렬 (앱을 켜 둔 동안 기억).</summary>
        private static string _sort = Game.Flow.GeneralSorts.Grade;

        private static long Stat(GameContext ctx, GeneralState g, string id) => CorpsValidator.EffectiveStat(ctx.Defs, g, id).FloorToLong();

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List", 22, 25);
            Ui.Fill((RectTransform)list.parent.parent);

            var head = Ui.HeaderRow(list, ctx.T("ui.tab.general"), ctx.T("ui.generals.count", ctx.State.Generals.Count));
            var sort = Ui.SecondaryButton(head, ctx.T("ui.generals.sort", ctx.T("ui.generals.sort." + _sort)) + " ▾", null, 70, 26);
            Ui.Size(sort, 70, 230).flexibleWidth = 0;
            var sortRt = (RectTransform)sort.transform;
            sort.onClick.AddListener(() =>
            {
                var items = new List<(string, string, bool, System.Action)>();
                foreach (var s in Game.Flow.GeneralSorts.All)
                {
                    string by = s;
                    items.Add((ctx.T("ui.generals.sort." + by), by == _sort ? "✓" : null, true, () => { _sort = by; Ui.Clear(root); Build(app, root); }));
                }
                Ui.PickMenu(root, sortRt, 300, items.ToArray());
            });
            if (ctx.Defs.Gacha.Banners.Count > 0)
                Ui.Size(Ui.SecondaryButton(head, ctx.T("ui.generals.recruitShort"), () => app.Show(new GachaScreen()), 70, 26), 70, 150).flexibleWidth = 0;
            Ui.Size(Ui.Rect(head, "InfoGap"), 70, 66).flexibleWidth = 0; // 오른쪽 끝 ⓘ 자리
            var info = InfoButton.Add(root, ctx.T("ui.tab.generalNote"));
            ((RectTransform)info.transform).anchoredPosition = new Vector2(-25f, -(25f + 50f - InfoButton.Size / 2f));

            var grid = Ui.Rect(list, "Grid");
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(505, 470);
            g.spacing = new Vector2(20, 22);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            g.childAlignment = TextAnchor.UpperCenter;

            foreach (var gs in Game.Flow.GeneralRoster.Sorted(ctx.Defs, ctx.State.Generals, _sort))
            {
                var gen = gs;
                string id = gen.GeneralId;
                ctx.Defs.Generals.TryGetValue(id, out var gdef);
                string grade = gdef?.Grade;
                var d = new Ui.GeneralCardData
                {
                    Id = id,
                    Name = ctx.GeneralName(id),
                    Grade = grade,
                    GradeName = string.IsNullOrEmpty(grade) ? null : ctx.T("grade." + grade),
                    Level = gen.Level,
                    StatusText = gen.Status == GeneralStatus.Available ? null : ctx.T("ui.generals." + gen.Status),
                    StatusColor = StatusColor(gen.Status),
                    Leadership = Stat(ctx, gen, "leadership"),
                    Might = Stat(ctx, gen, "might"),
                    Intellect = Stat(ctx, gen, "intellect"),
                    Attention = Game.Flow.GeneralRoster.NeedsAttention(ctx.Defs, ctx.State, id),
                };
                Ui.GeneralCard(ctx, grid, d, () => app.Show(new GeneralScreen(id)), anchor =>
                    Ui.StatTip(ctx, root, anchor,
                        ("leadership", ctx.T("stat.leadership"), d.Leadership.ToString()),
                        ("might", ctx.T("stat.might"), d.Might.ToString()),
                        ("intellect", ctx.T("stat.intellect"), d.Intellect.ToString())));
            }
        }

        /// <summary>상태 띠 색: 경상 노랑 / 중상 주황 / 포로 빨강.</summary>
        private static Color StatusColor(string s)
        {
            switch (s)
            {
                case GeneralStatus.LightInjury: return new Color(0.851f, 0.651f, 0.180f);
                case GeneralStatus.SeriousInjury: return new Color(0.851f, 0.463f, 0.180f);
                case GeneralStatus.Captured: return new Color(0.690f, 0.227f, 0.180f);
                default: return Palette.Good;
            }
        }
    }
}
