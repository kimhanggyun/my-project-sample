using Game.Services;
using Game.Territory;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 영토 탭 첫 단계 (territory.md 3·7장): [내 위치 확인] → 지금 지역(시·도 → … → 가장 작은 지역)과 공격할 수 있는 지역(단계마다 지금 지역 + 이웃).
    /// 위치는 이 버튼을 누를 때만 쓴다 (계속 추적하지 않음). 영토 전투·연합·지도는 다음 단계.
    /// </summary>
    public sealed class TerritoryTabView : ITabView
    {
        /// <summary>마지막 확인 결과 (탭을 다시 열어도 보이게, 위치를 다시 묻지 않는다).</summary>
        private static LocationResult _last;

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            Ui.Column(root, 12, 16);
            if (ctx.Territory == null || ctx.Location == null || !ctx.Location.IsAvailable)
            {
                Ui.Label(root, ctx.T("ui.territory.off"), Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim);
                return;
            }
            Ui.Label(root, ctx.T("ui.territory.note"), 26, TextAnchor.MiddleLeft, Palette.TextDim);
            var status = Ui.Label(root, "", Ui.FontSmall);
            Ui.Button(root, ctx.T("ui.territory.locate"), () =>
            {
                status.text = ctx.T("ui.territory.locating");
                ctx.Location.Request(r => { _last = r; if (root != null) app.ShowMain("territory"); });
            }, 100);

            var list = Ui.ScrollList(root, "Regions", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            if (_last == null) return;
            if (!_last.Ok) { status.text = Ui.Colored(ctx.T("ui.territory.failed", _last.Reason), Palette.Bad); return; }

            var locator = new RegionLocator(ctx.Territory);
            var path = locator.Locate(_last.Point);
            if (path.Count == 0) { status.text = ctx.T("ui.territory.outside", _last.Point.ToString()); return; }
            var names = new System.Collections.Generic.List<string>();
            foreach (var r in path) names.Add(ctx.T(r.NameKey));
            status.text = ctx.T("ui.territory.here", string.Join(" › ", names));

            Ui.Label(list, ctx.T("ui.territory.attackable"), Ui.FontBody);
            var here = new System.Collections.Generic.HashSet<string>();
            foreach (var r in path) here.Add(r.Id);
            foreach (var r in locator.Attackable(_last.Point))
            {
                var card = Ui.Card(list, r.Id, 20, 6);
                var level = ctx.Territory.Levels[r.Level];
                string tags = (here.Contains(r.Id) ? ctx.T("ui.territory.current") : "") + (level.SeasonOnly ? ctx.T("ui.territory.season") : "");
                Ui.Label(card, ctx.T("ui.territory.regionLine", ctx.T(r.NameKey), ctx.T("ui.territory.level." + r.Level), ctx.Territory.RequiredLegions(r), tags), Ui.FontSmall);
                Ui.Label(card, ctx.T("ui.territory.owner"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            }
        }
    }
}
