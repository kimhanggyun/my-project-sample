using System.Collections.Generic;
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
            var (_, list, _, _) = Ui.TitledPage(root, ctx.T("ui.tab.shop"), null); // 공통 타이틀 (탭이라 뒤로 없음)
            InfoButton.Add(root, ctx.T("ui.shop.note"));
            long now = ctx.Time.UtcNow.ToUnixTimeSeconds();
            if (PurchaseService.AdFree(ctx.State, now))
                Ui.Label(list, ctx.State.AdFreeForever ? ctx.T("ui.shop.adFreeForever")
                    : ctx.T("ui.shop.adFreeUntil", System.DateTimeOffset.FromUnixTimeSeconds(ctx.State.AdFreeUntilUnixSeconds).ToLocalTime().ToString("yyyy-MM-dd")), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good);
            if (!ctx.Store.IsAvailable) Ui.Label(list, Ui.Colored(ctx.T("ui.shop.unavailable"), Palette.Bad), Ui.FontSmall);

            if (_notice != null) { Ui.Label(list, _notice, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good); _notice = null; }
            CosmeticSection(app, list);
            ExchangeSection(app, list);
            Ui.Label(list, ctx.T("ui.shop.paid"), 28).fontStyle = FontStyle.Bold;
            foreach (var p in new PurchaseService(ctx.Defs).Listed())
            {
                string id = p.Id;
                var card = Ui.Card(list, id, 16, 6);
                var row = Ui.Row(card, "Row", 96, 8);
                string what = "";
                foreach (var kv in p.Grants) what += ctx.T("resource." + kv.Key) + " " + kv.Value.ToString("N0") + "  ";
                if (p.AdFreeDays.HasValue) what += p.AdFreeDays.Value <= 0 ? ctx.T("ui.shop.adFreeForeverItem") : ctx.T("ui.shop.adFreeDaysItem", p.AdFreeDays.Value);
                if (p.CosmeticId != null) what += ctx.T("ui.shop.cosmeticItem", ctx.T("cos." + p.CosmeticId)) + (CosmeticService.Owns(ctx.State, p.CosmeticId) ? "  " + ctx.T("ui.shop.owned") : "");
                Ui.Size(Ui.Label(row, ctx.T("product." + id) + "\n" + Ui.Colored(what, Palette.TextDim), 26), flexibleWidth: 3);
                var buy = Ui.Button(row, ctx.T("ui.shop.buy", p.PriceKrw.Value.ToString("N0")), () => Buy(app, id), 96, Palette.ButtonActive, 26);
                Ui.Size(buy, width: 300);
                Ui.SetEnabled(buy, ctx.Store.IsAvailable);
            }
        }

        private static string _notice;

        /// <summary>[치장] 묶음 (R-111): 엽전 · 금괴로 사는 갑옷 · 말 색 (결제 치장은 아래 결제 상품에). 산 것은 꾸미기에서 입힌다.</summary>
        private static void CosmeticSection(GameApp app, RectTransform list)
        {
            var ctx = app.Context;
            var svc = new CosmeticService(ctx.Defs);
            var items = new List<CosmeticDef>();
            foreach (var d in svc.Items) if (d.PriceGold != null || d.PriceGem != null) items.Add(d);
            if (items.Count == 0) return;
            Ui.Label(list, ctx.T("ui.shop.cosmetics"), 28).fontStyle = FontStyle.Bold;
            foreach (var d in items)
            {
                var card = Ui.Card(list, d.Id, 16, 6);
                var row = Ui.Row(card, "Row", 84, 8);
                string kind = ctx.T(d.Kind == CosmeticKinds.Horse ? "ui.cos.horse" : "ui.cos.armor");
                Ui.Size(Ui.Label(row, ctx.T(d.NameKey) + "\n" + Ui.Colored(kind, Palette.TextDim), 26), flexibleWidth: 3);
                string id = d.Id;
                if (CosmeticService.Owns(ctx.State, id)) { Ui.Size(Ui.Label(row, ctx.T("ui.shop.owned"), 24, TextAnchor.MiddleCenter, Palette.Good), width: 300); continue; }
                bool gold = d.PriceGold != null;
                long price = gold ? d.PriceGold.Value : d.PriceGem.Value;
                var buy = Ui.Button(row, ctx.T(gold ? "ui.cos.price.gold" : "ui.cos.price.gem", price.ToString("N0")), () =>
                {
                    bool ok = ctx.Do(PlayerActionRunner.BuyCosmetic(id));
                    _notice = ok ? ctx.T("ui.shop.cosmeticBought", ctx.T(d.NameKey)) : ctx.T(gold ? "ui.cos.notEnough.gold" : "ui.cos.notEnough.gem");
                    app.ShowMain(TabId);
                }, 84, Palette.ButtonActive, 24);
                Ui.Size(buy, width: 300);
                Ui.SetEnabled(buy, ctx.Resource(gold ? ResourceIds.Gold : ResourceIds.Gem) >= price);
            }
        }

        /// <summary>[바꾸기] 묶음 (R-112): 비율마다 한 묶음 · 열 묶음, 받는 양(손실 뒤) · 오늘 남은 한도. 시각은 서버 시각.</summary>
        private static void ExchangeSection(GameApp app, RectTransform list)
        {
            var ctx = app.Context;
            var econ = new DailyEconomy(ctx.Defs);
            if (econ.Rates.Count == 0) return;
            Ui.Label(list, ctx.T("ui.shop.exchange"), 28).fontStyle = FontStyle.Bold;
            long now = ctx.Clock.UtcNow.ToUnixTimeSeconds();
            foreach (var rate in econ.Rates)
            {
                var card = Ui.Card(list, rate.Key, 16, 6);
                long left = econ.LeftToday(ctx.State, rate, now);
                string info = ctx.T("ui.shop.exchangeLine", ctx.T("resource." + rate.From), ctx.T("resource." + rate.To), rate.Give.ToString("N0"), DailyEconomy.Output(rate, rate.Give).ToString("N0"))
                    + (rate.LossPct > 0 ? "  " + ctx.T("ui.shop.exchangeLoss", rate.LossPct) : "")
                    + (left == long.MaxValue ? "" : "  " + ctx.T("ui.shop.exchangeLeft", left.ToString("N0")));
                Ui.Label(card, info, 22, TextAnchor.MiddleLeft, Palette.TextDim);
                var row = Ui.Row(card, "Buttons", 72, 8);
                foreach (int k in new[] { 1, 10 })
                {
                    long amount = rate.Give * k, get = DailyEconomy.Output(rate, amount);
                    string from = rate.From, to = rate.To;
                    var b = Ui.Button(row, ctx.T("resource." + from) + " " + Ui.Compact(amount) + " → " + ctx.T("resource." + to) + " " + Ui.Compact(get), () =>
                    {
                        bool ok = ctx.Do(PlayerActionRunner.Exchange(from, to, amount, ctx.Clock.UtcNow.ToUnixTimeSeconds()));
                        _notice = ok ? ctx.T("ui.shop.exchanged") : ctx.T("ui.shop.exchangeFailed");
                        app.ShowMain(TabId);
                    }, 72, fontSize: 22);
                    Ui.SetEnabled(b, get > 0 && amount <= left && ctx.Resource(from) >= amount);
                }
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
