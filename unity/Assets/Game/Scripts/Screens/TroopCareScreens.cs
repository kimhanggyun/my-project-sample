using Game.Economy;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 부대 정비 탭 (R-61): 부대마다 병력·부상병·자연 회복까지 남은 시간, 한 번에 [모두 치료(군량)]·[편성 한도까지 보충(군량)].
    /// 실제 변경은 기존 행동(치료·징병)을 부대마다 보낸다 → 서버도 같은 행동으로 재현 (R-41). 채택 안 하면 이 파일과 등록 2줄만 지운다.
    /// </summary>
    public sealed class TroopCareTabView : ITabView
    {
        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var svc = new TroopService(ctx.Defs);
            var b = ctx.Defs.Balance;
            long now = ctx.Time.UtcNow.ToUnixTimeSeconds();
            var list = Ui.ScrollList(root, "List", 12, 16);
            Ui.Fill((RectTransform)list.parent.parent);

            long supply = ctx.Resource(ResourceIds.Supply);
            Ui.Label(list, ctx.T("ui.care.header", supply.ToString("N0"), b.WoundedRecoverMinutes), Ui.FontBody);

            long wounded = 0;
            foreach (var u in ctx.State.Units) wounded += u.Wounded;
            long healCost = HealCostCalculator.HealCostInSupply(b, wounded);
            var refill = svc.RefillPlan(ctx.State);
            long refillCount = 0;
            foreach (var p in refill) refillCount += p.Value;
            long refillCost = svc.ConscriptCost(refillCount);

            var row = Ui.Row(list, "Buttons", 110, 8);
            var heal = Ui.Button(row, ctx.T("ui.care.healAll", wounded.ToString("N0"), healCost.ToString("N0")), () =>
            {
                var hurt = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, long>>();
                foreach (var u in ctx.State.Units) if (u.Wounded > 0) hurt.Add(new System.Collections.Generic.KeyValuePair<string, long>(u.UnitId, u.Wounded));
                foreach (var h in hurt) if (!ctx.Do(PlayerActionRunner.Heal(h.Key, h.Value, ResourceIds.Supply))) break;
                app.ShowMain("care");
            }, 110, fontSize: 26);
            Ui.SetEnabled(heal, wounded > 0 && supply >= healCost);
            var fill = Ui.Button(row, ctx.T("ui.care.refill", refillCount.ToString("N0"), refillCost.ToString("N0")), () =>
            {
                foreach (var p in svc.RefillPlan(ctx.State))
                    if (!ctx.Do(PlayerActionRunner.Conscript(p.Key, p.Value))) break;
                app.ShowMain("care");
            }, 110, fontSize: 26);
            Ui.SetEnabled(fill, refillCount > 0 && supply >= refillCost);

            foreach (var u in ctx.State.Units)
            {
                string recover = u.Wounded <= 0 ? "" : u.WoundedRecoverAtUnixSeconds <= 0
                    ? ctx.T("ui.care.noRecover")
                    : ctx.T("ui.care.recoverIn", System.Math.Max(1, (u.WoundedRecoverAtUnixSeconds - now + 59) / 60));
                var card = Ui.Card(list, u.UnitId, 16, 4);
                Ui.Label(card, ctx.T("ui.care.unitLine", ctx.UnitLabel(u.UnitId), FormationTabView.TroopName(ctx, u.TroopType), u.Troops.ToString("N0"),
                    Ui.Colored(u.Wounded.ToString("N0"), u.Wounded > 0 ? Palette.Bad : Palette.TextDim), recover), 26);
            }
        }
    }
}
