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
        /// <summary>뒤로 키: 맨 위 화면이라 종료 확인 창.</summary>
        public override bool OnBack() => false;

        /// <summary>위 · 아래 바 바탕을 안전 영역 밖(상태바 · 홈 표시줄)까지 늘려 칠하는 길이.</summary>
        internal const float ChromeBleed = 400f;
        private const float TopBarHeight = 110f;
        private const float TabBarHeight = 104f;
        private const float MinTabWidth = 160f;
        private const int VisibleTabs = 6; // 배포 탭 6개(도감 포함, 2026-11-16)가 한 줄에 다 보이게 (더 많으면 가로 스크롤)
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
            { "gacha", () => new ScreenTabView("gacha", () => new GachaScreen()) },
            { "codex", () => new ScreenTabView("codex", () => new CodexScreen()) },
            { "replay", () => new ScreenTabView("replay", () => new ReplayListScreen()) },
            { "mail", () => new ScreenTabView("mail", () => new MailScreen()) },
            { "alliance", () => new ScreenTabView("alliance", () => new AllianceScreen()) },
            { "settings", () => new ScreenTabView("settings", () => new SettingsScreen()) },
            { "master", () => new MasterTabView() },
            { "parts", () => new PartsTabView() }, // 부품 미리보기 (공통부품.md 0장)
            { "sampleBattle", () => new SampleBattleTabView() },
            { "test", () => new TestBattleTabView() }, // 테스트 탭 (R-131): 샘플전투2를 3×3 칸으로 짜서
            { "looks", () => new LooksTabView() }, // 병사꾸미기 탭 (R-149 · R-150, 개발용): 병종 겉모습을 부품으로 입혀 봄 → 샘플전투2 우리 편
            { "generalLooks", () => new LooksTabView(general: true) }, // 장수꾸미기 탭 (R-150, 개발용) → 샘플전투2 우리 장수
            { "map", () => new MapTabView() },
            // 묶은 탭 (2026-11-15 사용자 결정): 군대 = 장수 · 병력 · 편성, 모집 = 장수 · 용병
            { "army", () => new GroupTabView("army", ("general", "ui.seg.general", "fig.general"), ("troop", "ui.seg.troop", "fig.troop5"), ("formation", "ui.seg.formation", "fig.command")) },
            { "recruit", () => new GroupTabView("recruit", ("gacha", "ui.seg.gacha", "fig.general"), ("hiring", "ui.seg.hiring", "fig.merc")) },
        };

        /// <summary>묶은 탭 안의 탭 id → (묶음, 세그먼트). ShowMain("troop") 같은 예전 호출도 묶음 안 그 세그먼트로 연다.</summary>
        internal static readonly Dictionary<string, (string group, string seg)> Grouped = new Dictionary<string, (string, string)>
        {
            { "general", ("army", "general") }, { "troop", ("army", "troop") }, { "formation", ("army", "formation") },
            { "gacha", ("recruit", "gacha") }, { "hiring", ("recruit", "hiring") },
        };

        internal static bool TryView(string id, out ITabView view)
        {
            view = Views.TryGetValue(id, out var make) ? make() : null;
            return view != null;
        }

        private readonly string _initial;
        private RectTransform _content;
        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
        /// <summary>그림 · 아이콘 탭 칸의 고름 표시 (부품 꾸미기 tabs.bottom).</summary>
        private readonly Dictionary<string, System.Action<bool>> _tabMarks = new Dictionary<string, System.Action<bool>>();

        /// <param name="initialTab">처음 열 탭 id (없으면 첫 번째 사용 탭).</param>
        public MainScreen(string initialTab = null)
        {
            // 묶음 안의 탭이면 묶음을 열고 그 세그먼트로 (세그먼트를 바꾸면 화면은 ShowMain(묶음)으로 다시 그린다)
            if (initialTab != null && Grouped.TryGetValue(initialTab, out var g)) { GroupTabView.Selected[g.group] = g.seg; initialTab = g.group; }
            _initial = initialTab;
        }

        public override void Build()
        {
            Ctx.EnsureAvatar(); // 닉네임 장수 (R-98, 예전 세이브)
            Ctx.Flow.Tick(Ctx.State); // 화면을 열 때마다 시간 경과 처리 (부상 회복·구출 기한)
            Ui.Column(Root, 0);

            TopBar(App, Root);
            // 띠 배너 광고 자리 (광고가 꺼졌거나 광고 제거 상품이면 0)
            Ui.BannerSlot(Root, Ctx);

            // 가운데: 탭 내용
            _content = Ui.Rect(Root, "Content");
            Ui.Size(_content, flexibleHeight: 1);

            // 아래: 탭 바 두 줄 (main-screen.md 3장). 위 줄 = 개발용(개발 빌드·마스터만), 아래 줄 = 배포 탭. 줄마다 가로 스크롤.
            var tabs = new List<MainTabEntry>();
            var alerts = Game.Flow.TabAlerts.Compute(Ctx.Defs, Ctx.State, Ctx.Flow, Ctx.Clock.UtcNow.ToUnixTimeSeconds());
            bool dev = Ctx.DevToolsVisible;
            if (Ctx.CodexHasNew) alerts.Add("codex"); // 도감 NEW (13 시안)
            foreach (var row in new[] { MainTabEntry.RowDev, MainTabEntry.RowRelease })
            {
                if (row == MainTabEntry.RowDev && !dev) continue;
                var rowTabs = new List<MainTabEntry>();
                foreach (var t in Ctx.MainTabs)
                    if (t.Status != MainTabEntry.Hidden && (t.Row == row || (row == MainTabEntry.RowRelease && t.Row != MainTabEntry.RowDev && t.Row != MainTabEntry.RowTop))) rowTabs.Add(t);
                if (rowTabs.Count == 0) continue;
                if (row == MainTabEntry.RowRelease) tabs.InsertRange(0, rowTabs); else tabs.AddRange(rowTabs); // 처음 열 탭은 배포 줄에서 먼저 찾는다
                bool small = row == MainTabEntry.RowDev;
                float h = small ? DevTabBarHeight : TabBarHeight;
                var barBg = Ui.Rect(Root, small ? "DevTabBar" : "TabBar");
                Ui.Background(barBg, small ? Palette.Card : Palette.Bar);
                if (!small) UiKit.Chrome(barBg, 0f, ChromeBleed, "bg.tabbar"); // 화면 맨 아래(홈 표시줄)까지 같은 바탕
                string tabVariant = small ? null : UiKit.Spec("tabs.bottom").Variant;
                if (!small && tabVariant != null && tabVariant != "button") h = UiKit.Spec("tabs.bottom").Num("height", h);
                Ui.Size(barBg, h);
                var bar = Ui.HScrollRow(barBg, small ? "DevTabs" : "Tabs", h, 6, 8);
                Ui.Fill((RectTransform)bar.parent.parent);
                int visible = small ? VisibleDevTabs : VisibleTabs;
                float width = Mathf.Max(small ? MinDevTabWidth : MinTabWidth, (GameApp.ReferenceWidth - 16f) / Mathf.Min(visible, rowTabs.Count) - 6f);
                if (tabVariant != null && tabVariant != "button")
                {
                    // 아이콘 · 밑줄 탭 (부품 꾸미기 tabs.bottom, ui-kit.md 4.3)
                    var items = new List<(string, string)>();
                    foreach (var t in rowTabs) items.Add((t.Id, T(t.NameKey)));
                    var cells = UiKit.TabStrip(bar, items, null, id => { Toast.Hide(); var e = rowTabs.Find(x => x.Id == id); if (e != null) Select(e); }, "tabs.bottom", "iconText", h - 16, width, 6,
                        id => UiKit.TabIcons.TryGetValue(id, out var ic) ? ic : null);
                    foreach (var t in rowTabs)
                    {
                        var c = cells[t.Id];
                        _tabButtons[t.Id] = c.button;
                        _tabMarks[t.Id] = c.mark;
                        bool alert = alerts.Contains(t.Id);
                        foreach (var kv in Grouped) if (kv.Value.group == t.Id && alerts.Contains(kv.Key)) alert = true;
                        if (alert && t.Status == MainTabEntry.Active) Dot(c.button);
                    }
                    if (rowTabs.Count > visible) Ui.MoreHint(barBg, h, Palette.Bar);
                    continue;
                }
                foreach (var t in rowTabs)
                {
                    var tab = t;
                    var b = Ui.Button(bar, T(t.NameKey), () => { Toast.Hide(); Select(tab); }, h - 16, fontSize: small ? 22 : 28);
                    Ui.Size(b, width: width);
                    if (t.Status != MainTabEntry.Active) b.GetComponentInChildren<Text>().color = Palette.TextDim;
                    _tabButtons[t.Id] = b;
                    bool alert = alerts.Contains(t.Id);
                    foreach (var kv in Grouped) if (kv.Value.group == t.Id && alerts.Contains(kv.Key)) alert = true; // 묶음 안 탭의 할 일
                    if (alert && t.Status == MainTabEntry.Active) Dot(b); // 할 일이 있는 탭 (E21)
                }
                if (rowTabs.Count > visible) Ui.MoreHint(barBg, h, small ? Palette.Card : Palette.Bar); // 넘치는 줄: 밀어서 더 본다는 표시
            }

            MainTabEntry first = null;
            foreach (var t in tabs)
            {
                if (t.Id == _initial) { first = t; break; }
                if (first == null && t.Status == MainTabEntry.Active) first = t;
            }
            // 줄에 없는 탭(위 아이콘 줄 상점 · 우편 · 설정, 개발 줄이 안 보일 때 개발 탭)도 id로 열면 연다
            if ((first == null || first.Id != _initial) && _initial != null)
                foreach (var t in Ctx.MainTabs)
                    if (t.Id == _initial && t.Status == MainTabEntry.Active) { first = t; break; }
            if (first != null) Select(first);
        }

        /// <summary>
        /// 위 자원 바 (시안 04): 내 사진 · 닉네임 + 자원 칩 3개(누르면 상점) + 우편 · 상점 · 설정. 메인 화면과 따로 여는 화면(병종 꾸미기 등)이 같이 쓴다
        /// (2026-11-27 사용자: 화면마다 "엽전 n" 글자를 따로 두지 말고 이 바를). 바탕은 화면 맨 위(상태바)까지.
        /// </summary>
        internal static RectTransform TopBar(GameApp app, RectTransform parent)
        {
            var ctx = app.Context;
            var top = Ui.Row(parent, "TopBar", TopBarHeight, 16);
            Ui.Background(top, Palette.Bar);
            UiKit.Chrome(top, ChromeBleed, 0f); // 부품 꾸미기 bg.header, 화면 맨 위(상태바)까지 같은 바탕 (2026-11-27 사용자)
            var topRow = top.GetComponent<HorizontalLayoutGroup>();
            topRow.padding = new RectOffset(20, 20, 18, 18);
            topRow.childForceExpandWidth = false;
            topRow.childForceExpandHeight = false;
            topRow.childAlignment = TextAnchor.MiddleLeft;
            // 왼쪽: 내 사진 + 닉네임 (누르면 내 정보)
            var me = Ui.Rect(top, "Me");
            Ui.Size(me, 84, 92).flexibleWidth = 0;
            var meCol = Ui.Column(me, 0, 0);
            meCol.childAlignment = TextAnchor.UpperCenter;
            meCol.childForceExpandWidth = false;
            ProfilePhoto.Make(ctx, me, 60);
            // 닉네임이 없으면 장수 이름("나") 대신 "닉네임 정하기" (계정마다 같은 "나"로 보여 중복처럼 보이던 것, 2026-11-17)
            var nick = Ui.Label(me, ctx.Nickname ?? ctx.T("ui.top.setNickname"), 18, TextAnchor.MiddleCenter, ctx.Nickname == null ? Palette.Seal : Palette.Text);
            nick.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(nick, 22, 92);
            Ui.Background(me, new Color(0, 0, 0, 0));
            me.gameObject.AddComponent<Button>().onClick.AddListener(() => app.Show(new ProfileScreen()));
            foreach (var id in new[] { ResourceIds.Supply, ResourceIds.Gold, ResourceIds.Gem })
                Ui.ResourceChip(ctx, top, id, ctx.Resource(id), () => app.ShowMain("shop"));
            // 우편 (종): 받지 않은 우편 수
            var mail = Ui.IconButton(top, "bell", ctx.T("ui.top.mail"), () => app.Show(new MailScreen(null, null)));
            if (ctx.OnlineEnabled)
                ctx.LoadMail(list => { if (mail != null && list != null && list.Count > 0) Badge(mail, list.Count); });
            Ui.IconButton(top, "shop", ctx.T("ui.top.shop"), () => app.ShowMain("shop"));
            Ui.IconButton(top, "settings", ctx.T("ui.top.settings"), () => app.Show(new SettingsScreen()));
            return top;
        }

        /// <summary>아이콘 오른쪽 위 빨간 숫자 (우편 수, 99+).</summary>
        private static void Badge(Button b, int n)
        {
            var dot = Ui.Rect(b.transform, "Badge");
            dot.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            dot.anchorMin = dot.anchorMax = new Vector2(1f, 1f);
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(n > 9 ? 44f : 34f, 34f);
            dot.anchoredPosition = new Vector2(-6f, -6f);
            var img = Ui.Background(dot, Palette.Seal);
            img.sprite = UiShapes.Circle;
            img.raycastTarget = false;
            var t = Ui.Label(dot, n > 99 ? "99+" : n.ToString(), 20, TextAnchor.MiddleCenter, Color.white);
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            Ui.Fill(t.rectTransform);
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
            foreach (var kv in _tabButtons)
                if (_tabMarks.TryGetValue(kv.Key, out var mark)) mark(kv.Key == tab.Id);
                else Ui.Recolor(kv.Value, kv.Key == tab.Id ? Palette.ButtonActive : Palette.Button);
            var root = Ui.Panel(_content, tab.Id);
            ITabView view = tab.Status == MainTabEntry.Active && Views.TryGetValue(tab.Id, out var make) ? make() : new ComingSoonTabView(tab);
            _view = view;
            _viewId = tab.Id;
            view.Build(App, root);
            if (tab.Status == MainTabEntry.Active) GuideOverlay.Show(App, Root, "tab." + tab.Id); // 처음 안내 (R-62)
        }
    }

    /// <summary>
    /// 묶은 탭 (군대 · 모집): 위에 세그먼트, 아래에 고른 탭의 내용 (기존 탭 화면 그대로). 고른 세그먼트는 앱이 켜져 있는 동안 기억.
    /// 편성처럼 저장 안 한 것이 있는 탭이면 세그먼트를 바꿀 때 · 다른 탭으로 갈 때 묻는다.
    /// </summary>
    public sealed class GroupTabView : ITabView, ILeaveGuard
    {
        internal static readonly Dictionary<string, string> Selected = new Dictionary<string, string>();
        private readonly string _id;
        private readonly (string id, string labelKey, string art)[] _segments;
        private ITabView _child;

        public GroupTabView(string id, params (string id, string labelKey, string art)[] segments) { _id = id; _segments = segments; }

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            if (!Selected.TryGetValue(_id, out var seg) || System.Array.FindIndex(_segments, s => s.id == seg) < 0) seg = _segments[0].id;
            Ui.Column(root, 0, 0);
            // 대분류 세그먼트 (위 바와 같은 바탕 띠, 칸마다 그림 — 2026-11-27 모집도 군대처럼)
            var items = new List<(string, string, string)>();
            foreach (var s in _segments) items.Add((s.id, ctx.T(s.labelKey), s.art));
            Ui.MajorTabs(root, items, seg, id =>
            {
                if (id == seg) return;
                void Go() { Selected[_id] = id; app.ShowMain(_id); }
                if (_child is ILeaveGuard g && g.HasUnsaved(app)) MainScreen.AskLeave(app, g, Go); else Go();
            });
            var body = Ui.Rect(root, "Body");
            Ui.Size(body, flexibleHeight: 1);
            var panel = Ui.Panel(body, seg);
            if (MainScreen.TryView(seg, out _child)) _child.Build(app, panel);
        }

        public bool HasUnsaved(GameApp app) => _child is ILeaveGuard g && g.HasUnsaved(app);
        public void SaveForLeave(GameApp app) { if (_child is ILeaveGuard g) g.SaveForLeave(app); }
        public void Discard(GameApp app) { if (_child is ILeaveGuard g) g.Discard(app); }
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
        private readonly string _id;
        private readonly System.Func<GameScreen> _make;
        public ScreenTabView(string id, System.Func<GameScreen> make) { _id = id; _make = make; }

        /// <summary>GameScreen.Replace가 넘긴 다음 내용 (같은 프레임 안에서 그 탭이 다시 그려질 때 한 번 씀).</summary>
        private static string _handTab;
        private static GameScreen _handNext;
        private static int _handFrame = -1;
        internal static void Hand(string tab, GameScreen next) { _handTab = tab; _handNext = next; _handFrame = Time.frameCount; }

        public void Build(GameApp app, RectTransform root)
        {
            GameScreen screen;
            if (_handNext != null && _handTab == _id && _handFrame == Time.frameCount) { screen = _handNext; }
            else screen = _make();
            _handNext = null; _handTab = null;
            screen.HostTab = _id;
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
        /// <summary>장수 목록에서 고른 정렬 (장수 화면 좌우 넘기기도 같은 순서, 2026-11-20).</summary>
        internal static string Sort => _sort;

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
                Ui.Size(Ui.SecondaryButton(head, ctx.T("ui.generals.recruitShort"), () => app.ShowMain("gacha"), 70, 26), 70, 130).flexibleWidth = 0; // [모집] 탭으로
            Ui.Size(Ui.SecondaryButton(head, ctx.T("ui.generals.codex"), () => app.ShowMain("codex"), 70, 26), 70, 130).flexibleWidth = 0; // 도감 (장수 탭으로 열림)
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
