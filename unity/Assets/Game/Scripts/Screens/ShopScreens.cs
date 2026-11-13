using Game.Economy;
using Game.Flow;
using Game.Services;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 상점(테스트) 탭 (R-64): 가격이 있는 상품을 보여주고, 스토어 서비스로 결제한 뒤 성공하면 행동 store.purchase를 보낸다 (서버가 영수증 검증).
    /// 가격·구성은 임시 테스트 값 (products.json). 개발 빌드는 가짜 스토어라 바로 성공한다. 채택 안 하면 이 파일과 등록 2줄만 지운다.
    /// </summary>
    public sealed class ShopTabView : ITabView
    {
        private const string TabId = "shop";

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List", 12, 16);
            Ui.Fill((RectTransform)list.parent.parent);
            InfoButton.Add(root, ctx.T("ui.shop.note"));
            long now = ctx.Time.UtcNow.ToUnixTimeSeconds();
            if (PurchaseService.AdFree(ctx.State, now))
                Ui.Label(list, ctx.State.AdFreeForever ? ctx.T("ui.shop.adFreeForever")
                    : ctx.T("ui.shop.adFreeUntil", System.DateTimeOffset.FromUnixTimeSeconds(ctx.State.AdFreeUntilUnixSeconds).ToLocalTime().ToString("yyyy-MM-dd")), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good);
            if (!ctx.Store.IsAvailable) Ui.Label(list, Ui.Colored(ctx.T("ui.shop.unavailable"), Palette.Bad), Ui.FontSmall);

            foreach (var p in new PurchaseService(ctx.Defs).Listed())
            {
                string id = p.Id;
                var card = Ui.Card(list, id, 16, 6);
                var row = Ui.Row(card, "Row", 96, 8);
                string what = "";
                foreach (var kv in p.Grants) what += ctx.T("resource." + kv.Key) + " " + kv.Value.ToString("N0") + "  ";
                if (p.AdFreeDays.HasValue) what += p.AdFreeDays.Value <= 0 ? ctx.T("ui.shop.adFreeForeverItem") : ctx.T("ui.shop.adFreeDaysItem", p.AdFreeDays.Value);
                Ui.Size(Ui.Label(row, ctx.T("product." + id) + "\n" + Ui.Colored(what, Palette.TextDim), 26), flexibleWidth: 3);
                var buy = Ui.Button(row, ctx.T("ui.shop.buy", p.PriceKrw.Value.ToString("N0")), () => Buy(app, id), 96, Palette.ButtonActive, 26);
                Ui.Size(buy, width: 300);
                Ui.SetEnabled(buy, ctx.Store.IsAvailable);
            }
        }

        private static void Buy(GameApp app, string productId)
        {
            var ctx = app.Context;
            ctx.Store.Purchase(productId, outcome =>
            {
                // 실제 스토어는 영수증을 돌려준다 (백엔드를 정한 뒤 IStoreService에 더함). 지금은 개발용 가짜 영수증.
                if (outcome == PurchaseOutcome.Succeeded)
                    ctx.Do(PlayerActionRunner.Purchase(productId, ReceiptCheck.FakePrefix + productId + ":" + ctx.NewSeed()));
                app.ShowMain(TabId);
            });
        }
    }
}
