using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.Battle3D;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// [꾸미기] 탭 — 개발용 (R-149, 2026-11-24 사용자 "[테스트] 탭처럼 개발 단계에서만, 나중엔 병과 · 갑옷 세트를 사면 자동 적용").
    /// 병종을 고르고 갑옷 세트 · 말 색을 사지 않고도 입혀 본다 (이 기기에만 저장, <see cref="DevLooks"/>). 고른 모습은 샘플전투2의 우리 편에 그대로 나온다.
    /// </summary>
    public sealed class LooksTabView : ITabView
    {
        /// <summary>고를 병종 (1단계 + 승급 — 병종 기본 모양이 다른 것: 방패병 · 근위의 긴 방패, 투구 …).</summary>
        public static readonly string[] Troops = { "infantry", "shieldbearer", "guard", "spearman", "pikeman", "archer", "crossbowman", "cavalry", "heavyCavalry", "ironCavalry" };
        private static string _troop = "infantry";
        private GameApp _app;
        private RectTransform _root;

        public void Build(GameApp app, RectTransform root)
        {
            _app = app;
            _root = root;
            Redraw();
        }

        private string T(string key, params object[] args) => _app.Context.T(key, args);

        /// <summary>캡처 투어: 병종 · 세트를 골라 다시 그림.</summary>
        public static void PickForTour(string troop, string armor) { _troop = troop; DevLooks.SetArmor(troop, armor); }

        private void Redraw()
        {
            Ui.Clear(_root);
            var ctx = _app.Context;
            var list = Ui.ScrollList(_root, "List", 14, 20);
            Ui.Fill((RectTransform)list.parent.parent); // 탭 전체를 채움 ([테스트] 탭과 같음 — 빠지면 목록 크기가 0)
            InfoButton.Add(_root, T("ui.looks.note"));
            // 병종
            // 병종: 다섯씩 두 줄 (가로 칸이 좁아 글자가 안 보이던 것 — 2026-11-24 캡처)
            RectTransform troops = null;
            int n = 0;
            foreach (var id in Troops)
            {
                if (!ctx.Defs.TroopTypes.ContainsKey(id)) continue;
                if (n++ % 5 == 0) troops = Ui.Row(list, "Troops", 68, 8);
                string t = id;
                var b = Ui.Button(troops, T("troop." + id), () => { _troop = t; Redraw(); }, 68, _troop == id ? Palette.ButtonActive : Palette.Button, 22);
                Ui.Size(b, flexibleWidth: 1);
            }
            string line = CosmeticService.LineOf(ctx.Defs, _troop) ?? _troop;
            var (armor, horse) = DevLooks.Pick(_troop);
            // 지금 모습 (크게)
            var now = Ui.Card(list, "Now", 16, 8);
            Ui.Label(now, T("ui.looks.now", T("troop." + _troop)), 28).fontStyle = FontStyle.Bold;
            var pic = Picture(now, _troop, line, armor, horse);
            Ui.Size(pic, 360, 400).flexibleWidth = 0;
            // 갑옷 세트 (사지 않고 입혀 봄)
            var svc = new CosmeticService(ctx.Defs);
            Section(list, svc, CosmeticKinds.Armor, T("ui.looks.sets"), line, armor, horse);
            if (line == "cavalry") Section(list, svc, CosmeticKinds.Horse, T("ui.looks.horses"), line, armor, horse);
        }

        private Image Picture(Transform parent, string troop, string line, string armor, string horse)
        {
            var box = Ui.Rect(parent, "Picture");
            var bg = Ui.Background(box, new Color(0.894f, 0.859f, 0.776f));
            bg.sprite = UiShapes.Rounded(14);
            bg.type = Image.Type.Sliced;
            var img = Ui.Background(Ui.Rect(box, "Image"), Color.white);
            Ui.Fill(img.rectTransform);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.sprite = TroopPictureRenderer.ForTroop(_app.Context, troop, line, armor, horse) ?? Resources.Load<Sprite>("Ui/troops/" + line);
            return bg;
        }

        private void Section(RectTransform list, CosmeticService svc, string kind, string title, string line, string armor, string horse)
        {
            var card = Ui.Card(list, "Section_" + kind, 16, 8);
            Ui.Label(card, title, 28).fontStyle = FontStyle.Bold;
            var row = Ui.HScrollRow(card, "Cells", 330, 12, 4);
            Cell(row, kind, null, line, armor, horse);
            foreach (var d in svc.Items)
                if (d.Kind == kind && CosmeticService.CanWear(d, line)) Cell(row, kind, d, line, armor, horse);
        }

        private void Cell(RectTransform row, string kind, CosmeticDef d, string line, string armor, string horse)
        {
            string id = d?.Id;
            bool on = (kind == CosmeticKinds.Armor ? armor : horse) == id;
            var cell = Ui.Rect(row, "Cell_" + (id ?? "default"));
            Ui.Size(cell, 300, 320).flexibleWidth = 0;
            var bg = Ui.Background(cell, on ? new Color(0.98f, 0.93f, 0.82f) : Palette.Card);
            bg.sprite = UiShapes.Rounded(14);
            bg.type = Image.Type.Sliced;
            Ui.Column(cell, 4, 8);
            string a = kind == CosmeticKinds.Armor ? id : armor, h = kind == CosmeticKinds.Horse ? id : horse;
            Ui.Size(Picture(cell, _troop, line, a, h), 170, 190);
            Ui.Label(cell, d == null ? T("ui.cos.default") : T(d.NameKey), 22, TextAnchor.MiddleCenter);
            Ui.Label(cell, Tier(d), 18, TextAnchor.MiddleCenter, Palette.TextDim);
            var btn = Ui.Button(cell, on ? T("ui.looks.on") : T("ui.looks.wear"), () =>
            {
                if (kind == CosmeticKinds.Armor) DevLooks.SetArmor(_troop, id); else DevLooks.SetHorse(_troop, id);
                Redraw();
            }, 52, on ? Palette.ButtonActive : Palette.Button, 22);
            Ui.Size(btn, 52);
        }

        /// <summary>얻는 길 (엽전 · 금괴 · 결제 · 플레이) — 모양 등급을 알아보게.</summary>
        private string Tier(CosmeticDef d)
        {
            if (d == null) return T("ui.looks.tier.default");
            if (d.PriceKRW != null) return T("ui.looks.tier.cash");
            if (d.PriceGem != null) return T("ui.looks.tier.gem");
            if (d.PriceGold != null) return T("ui.looks.tier.gold");
            return T("ui.looks.tier.play");
        }
    }
}
