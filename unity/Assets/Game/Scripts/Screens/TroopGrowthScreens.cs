using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 부대 키우기 탭 (troop-growth.md, R-52): 보유 부대마다 병종·병력·훈련도, [훈련], 갈 수 있는 승급 (조건·비용, 안 되면 꺼짐).
    /// 바꾸는 것은 모두 행동(unit.train·unit.promote)이라 온라인이면 서버도 같은 규칙으로 적용한다.
    /// </summary>
    public sealed class TroopGrowthTabView : ITabView
    {
        private static string _notice;

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var svc = new TroopGrowthService(ctx.Defs);
            var list = Ui.ScrollList(root, "Units", 10, 16);
            Ui.Fill((RectTransform)list.parent.parent);
            Ui.Label(list, ctx.T("ui.troops.note"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Label(list, ctx.T("resource.gold") + " " + ctx.Resource(Game.Economy.ResourceIds.Gold).ToString("N0"), Ui.FontSmall);
            if (_notice != null) { Ui.Label(list, _notice, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good); _notice = null; }
            foreach (var u in ctx.State.Units)
            {
                string id = u.UnitId;
                var card = Ui.Card(list, id, 20, 6);
                Ui.Label(card, ctx.T("ui.troops.line", id, FormationTabView.TroopName(ctx, u.TroopType), u.Troops.ToString("N0"), (u.Training.Raw / 100).ToString()), Ui.FontSmall);
                var train = Ui.Button(card, ctx.T("ui.troops.train", ctx.Defs.TroopGrowth.TrainingStep.Raw / 100, svc.TrainCost(u).ToString("N0")),
                    () => Do(app, PlayerActionRunner.TrainUnit(id), svc.CheckTrain(ctx.State, id)), 72, fontSize: 24);
                Ui.SetEnabled(train, svc.CheckTrain(ctx.State, id) == TroopGrowthService.Ok);
                foreach (var p in svc.PromotionsFor(u))
                {
                    string to = p.To;
                    string step = p.Step == null ? ctx.T("ui.troops.anyStep") : ctx.T("ui.troops.stepCleared", ctx.T(ctx.Flow.LoadStep(p.Step).NameKey));
                    string check = svc.CheckPromote(ctx.State, id, to);
                    var b = Ui.Button(card, ctx.T("ui.troops.promote", FormationTabView.TroopName(ctx, to), p.Training.Raw / 100, step, p.Gold.ToString("N0")),
                        () => Do(app, PlayerActionRunner.PromoteUnit(id, to), svc.CheckPromote(ctx.State, id, to)), 72, fontSize: 22);
                    Ui.SetEnabled(b, check == TroopGrowthService.Ok);
                    Ui.Label(card, ctx.T("troop.desc." + to), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                }
            }
        }

        private static void Do(GameApp app, Game.Online.PlayerAction action, string check)
        {
            var ctx = app.Context;
            _notice = ctx.T("ui.troops.result." + (check == TroopGrowthService.Ok && ctx.Do(action) ? TroopGrowthService.Ok : check));
            app.ShowMain("troop");
        }
    }
}
