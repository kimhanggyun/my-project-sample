using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    public enum MainTab { Story, Generals, Units, Compositions }

    /// <summary>
    /// 메인 화면: 위 자원 막대 + 아래 탭. 탭 내용은 <see cref="ITabView"/>로 갈아 끼운다 (새 탭은 구현 추가 + 등록).
    /// </summary>
    public sealed class MainScreen : GameScreen
    {
        private readonly MainTab _initial;
        private RectTransform _content;
        private readonly Dictionary<MainTab, Button> _tabButtons = new Dictionary<MainTab, Button>();

        public MainScreen(MainTab initial = MainTab.Story) { _initial = initial; }

        /// <summary>탭 등록표: 탭 → (이름 키, 내용 만들기).</summary>
        private static readonly List<(MainTab tab, string nameKey, System.Func<ITabView> make)> Tabs = new List<(MainTab, string, System.Func<ITabView>)>
        {
            (MainTab.Story, "ui.tab.story", () => new StoryTabView()),
            (MainTab.Generals, "ui.tab.generals", () => new GeneralsTabView()),
            (MainTab.Units, "ui.tab.units", () => new UnitsTabView()),
            (MainTab.Compositions, "ui.tab.compositions", () => new CompositionsTabView()),
        };

        public override void Build()
        {
            Ctx.Flow.Tick(Ctx.State); // 화면을 열 때마다 시간 경과 처리 (부상 회복·구출 기한)
            var col = Ui.Column(Root, 0);
            col.childForceExpandHeight = false;

            // 위: 자원 + 설정
            var top = Ui.Row(Root, "TopBar", 140, 16);
            Ui.Background(top, Palette.Bar);
            top.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(24, 24, 16, 16);
            string res = T("resource.supply") + " " + Ctx.Resource(ResourceIds.Supply).ToString("N0") + "   "
                       + T("resource.gold") + " " + Ctx.Resource(ResourceIds.Gold).ToString("N0") + "   "
                       + T("resource.gem") + " " + Ctx.Resource(ResourceIds.Gem).ToString("N0");
            Ui.Size(Ui.Label(top, res, Ui.FontSmall), flexibleWidth: 4);
            Ui.Size(Ui.Button(top, T("ui.top.settings"), () => App.Show(new SettingsScreen()), 108, fontSize: Ui.FontSmall), width: 180);

            // 가운데: 탭 내용
            _content = Ui.Rect(Root, "Content");
            Ui.Size(_content, flexibleHeight: 1);

            // 아래: 탭 버튼
            var bottom = Ui.Row(Root, "Tabs", 160, 8);
            Ui.Background(bottom, Palette.Bar);
            bottom.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 12, 12);
            foreach (var t in Tabs)
            {
                var tab = t.tab;
                _tabButtons[tab] = Ui.Button(bottom, T(t.nameKey), () => Select(tab), 136);
            }
            Select(_initial);
        }

        private void Select(MainTab tab)
        {
            Ui.Clear(_content);
            foreach (var kv in _tabButtons) kv.Value.targetGraphic.color = kv.Key == tab ? Palette.ButtonActive : Palette.Button;
            foreach (var t in Tabs)
                if (t.tab == tab)
                {
                    var view = t.make();
                    var root = Ui.Panel(_content, tab.ToString());
                    view.Build(App, root);
                }
        }
    }

    /// <summary>메인 화면의 탭 내용 하나.</summary>
    public interface ITabView
    {
        void Build(Boot.GameApp app, RectTransform root);
    }

    /// <summary>장수 목록 (읽기 전용, 육성 화면은 다음 단계).</summary>
    public sealed class GeneralsTabView : ITabView
    {
        public void Build(Boot.GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List");
            Ui.Fill((RectTransform)list.parent.parent);
            foreach (var g in ctx.State.Generals)
            {
                var card = Ui.Card(list, g.GeneralId);
                var color = g.Status == GeneralStatus.Available ? Palette.Good : g.Status == GeneralStatus.Captured ? Palette.Bad : Palette.TextDim;
                Ui.Label(card, ctx.T("ui.generals.line", ctx.GeneralName(g.GeneralId), g.Level, Ui.Colored(ctx.T("ui.generals." + g.Status), color)));
            }
            Ui.Label(list, ctx.T("ui.tab.comingSoon"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
        }
    }

    /// <summary>부대 목록 (읽기 전용).</summary>
    public sealed class UnitsTabView : ITabView
    {
        public void Build(Boot.GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List");
            Ui.Fill((RectTransform)list.parent.parent);
            foreach (var u in ctx.State.Units)
            {
                string troop = ctx.Defs.TroopTypes.TryGetValue(u.TroopType, out var tt) ? ctx.T(tt.NameKey) : u.TroopType;
                var card = Ui.Card(list, u.UnitId);
                Ui.Label(card, ctx.T("ui.units.line", u.UnitId, troop, u.Troops.ToString("N0"), u.Wounded.ToString("N0"), u.Cohesion.FloorToLong()), Ui.FontSmall);
            }
            Ui.Label(list, ctx.T("ui.tab.comingSoon"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
        }
    }

    /// <summary>군단 편성 목록 (읽기 전용, 편성 바꾸기는 다음 단계).</summary>
    public sealed class CompositionsTabView : ITabView
    {
        public void Build(Boot.GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List");
            Ui.Fill((RectTransform)list.parent.parent);
            foreach (var c in ctx.State.Compositions)
            {
                string formation = c.Formation != null && ctx.Defs.Formations.TryGetValue(c.Formation, out FormationDef f) ? ctx.T(f.NameKey) : c.Formation;
                var card = Ui.Card(list, c.GeneralId);
                Ui.Label(card, ctx.T("ui.compositions.line", ctx.GeneralName(c.GeneralId), formation, c.Slots.Count));
            }
            Ui.Label(list, ctx.T("ui.tab.comingSoon"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
        }
    }
}
