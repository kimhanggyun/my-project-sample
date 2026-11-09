using System.Collections.Generic;
using Game.Data.Defs;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Game.Services;
using Game.Territory;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 영토 탭 (territory.md 3·5·7장): [내 위치 확인] → 지금 지역(시·도 → … → 가장 작은 지역)과 공격할 수 있는 지역(단계마다 지금 지역 + 이웃),
    /// 지역마다 주인(나 / 다른 유저 / 없음). 혼자 차지할 수 있는 지역은 [공격] → 영토 출진 (AI 수비군, R-46).
    /// 위치는 버튼을 누를 때만 쓴다 (계속 추적하지 않음). 연합·지도·수비 편성은 다음 단계.
    /// </summary>
    public sealed class TerritoryTabView : ITabView
    {
        /// <summary>마지막 확인 결과 (탭을 다시 열어도 보이게, 위치를 다시 묻지 않는다).</summary>
        internal static LocationResult Last;

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
                ctx.Location.Request(r => { Last = r; if (root != null) app.ShowMain("territory"); });
            }, 100);

            var list = Ui.ScrollList(root, "Regions", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            if (Last == null) return;
            if (!Last.Ok) { status.text = Ui.Colored(ctx.T("ui.territory.failed", Last.Reason), Palette.Bad); return; }

            var locator = new RegionLocator(ctx.Territory);
            var path = locator.Locate(Last.Point);
            if (path.Count == 0) { status.text = ctx.T("ui.territory.outside", Last.Point.ToString()); return; }
            var names = new List<string>();
            foreach (var r in path) names.Add(ctx.T(r.NameKey));
            status.text = ctx.T("ui.territory.here", string.Join(" › ", names));
            if (!ctx.OnlineEnabled) Ui.Label(list, ctx.T("ui.territory.needOnline"), 26, TextAnchor.MiddleLeft, Palette.TextDim);

            Ui.Label(list, ctx.T("ui.territory.attackable"), Ui.FontBody);
            var here = new HashSet<string>();
            foreach (var r in path) here.Add(r.Id);
            var ownerLabels = new Dictionary<string, UnityEngine.UI.Text>();
            foreach (var r in locator.Attackable(Last.Point))
            {
                var card = Ui.Card(list, r.Id, 20, 6);
                var level = ctx.Territory.Levels[r.Level];
                string tags = (here.Contains(r.Id) ? ctx.T("ui.territory.current") : "") + (level.SeasonOnly ? ctx.T("ui.territory.season") : "");
                Ui.Label(card, ctx.T("ui.territory.regionLine", ctx.T(r.NameKey), ctx.T("ui.territory.level." + r.Level), ctx.Territory.RequiredLegions(r), tags), Ui.FontSmall);
                ownerLabels[r.Id] = Ui.Label(card, ctx.OnlineEnabled ? ctx.T("ui.hire.loading") : ctx.T("ui.territory.ownerUnknown"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
                string err = TerritoryBattles.CheckSortie(ctx.Territory, r.Id, Last.Point.ToString());
                if (err == null)
                {
                    string id = r.Id;
                    var b = Ui.Button(card, ctx.T("ui.territory.attack"), () => app.Show(new TerritorySortieScreen(id, Last.Point.ToString())), 72, fontSize: 26);
                    Ui.SetEnabled(b, ctx.OnlineEnabled);
                }
                else Ui.Label(card, err, 22, TextAnchor.MiddleLeft, Palette.TextDim);
            }
            if (!ctx.OnlineEnabled) return;
            ctx.LoadTerritory(res =>
            {
                if (list == null) return;
                var owners = new Dictionary<string, TerritoryOwner>();
                if (res != null) foreach (var o in res.Owners) owners[o.RegionId] = o;
                foreach (var kv in ownerLabels)
                {
                    if (kv.Value == null) continue;
                    kv.Value.text = res == null ? ctx.T("ui.hire.failed")
                        : !owners.TryGetValue(kv.Key, out var o) ? ctx.T("ui.territory.owner")
                        : o.OwnerAccountId == ctx.AccountId ? Ui.Colored(ctx.T("ui.territory.mine"), Palette.Good)
                        : Ui.Colored(ctx.T("ui.territory.others"), Palette.Enemy);
                }
            });
        }
    }

    /// <summary>
    /// 영토 출진 (R-46): 지역의 수비군을 보여주고 [직접 지휘] / [위임]. 스토리 출진과 같은 준비·확정·전투·결과 (결과는 서버가 재현해 맞으면 차지).
    /// </summary>
    public sealed class TerritorySortieScreen : GameScreen
    {
        private readonly string _regionId;
        private readonly string _location;

        public TerritorySortieScreen(string regionId, string location) { _regionId = regionId; _location = location; }

        private string StepId => TerritoryBattles.StepIdOf(_regionId);

        public override void Build()
        {
            var step = Ctx.Flow.LoadStep(StepId);
            Ui.Column(Root, 16, 32);
            Ui.Size(Ui.Label(Root, T(step.NameKey), Ui.FontTitle), 120);
            var body = Ui.ScrollList(Root, "Info", 12, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);
            var info = Ui.Card(body, "Defense");
            Ui.Label(info, T("ui.territory.defense"));
            foreach (var e in step.Enemy)
            {
                long troops = 0;
                foreach (var u in e.Units) troops += u.Troops;
                Ui.Label(info, Ui.Colored(Ctx.GeneralName(e.General), Palette.Enemy) + "  " + troops.ToString("N0"), Ui.FontSmall);
            }
            Ui.Label(info, T("ui.step.maxMinutes", step.MaxMinutes), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Label(info, T("ui.territory.captureNote"), 24, TextAnchor.MiddleLeft, Palette.TextDim);

            var plan = Ctx.Flow.Prepare(Ctx.State, Request(false));
            var sortie = Ui.Card(body, "Sortie");
            Ui.Label(sortie, T("ui.step.supply", plan.SupplyTaken.ToString("N0"), Ctx.Resource(ResourceIds.Supply).ToString("N0")), Ui.FontSmall);
            foreach (var err in plan.Errors) Ui.Label(sortie, Ui.Colored(err, Palette.Bad), Ui.FontSmall);

            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(row, T("ui.common.back"), () => App.ShowMain("territory"));
            var direct = Ui.Button(row, T("ui.step.directSortie"), () => Sortie(step, false));
            var delegated = Ui.Button(row, T("ui.step.delegateSortie"), () => Sortie(step, true));
            Ui.SetEnabled(direct, plan.Ok && Ctx.OnlineEnabled);
            Ui.SetEnabled(delegated, plan.Ok && Ctx.OnlineEnabled);
        }

        private SortieRequest Request(bool delegate_) => new SortieRequest { StepId = StepId, Seed = Ctx.NewSeed(), Delegate = delegate_, Location = _location };

        private void Sortie(StepDef step, bool delegate_)
        {
            var plan = Ctx.Flow.Prepare(Ctx.State, Request(delegate_));
            if (!Ctx.Flow.Confirm(Ctx.State, plan)) { App.Show(new TerritorySortieScreen(_regionId, _location)); return; }
            App.Show(new DialogueScreen(plan, "dialogue." + step.Id + ".start", !delegate_));
        }
    }
}
