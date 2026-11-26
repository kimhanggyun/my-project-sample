using Game.Flow;
using Game.Economy;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 꾸미기 (cosmetics.md 6장, 치장 1단계): 병종 계열 하나의 갑옷 한 벌 · 말 색을 고르고 산다.
    /// 위 = 큰 미리보기 (누른 칸을 입혀 본 모습, 전투 병사 인형을 찍은 그림), 아래 = 갑옷 칸 줄 · 말 칸 줄(기병).
    /// 칸 = 기본 · 가진 것([입기]) · 안 가진 것([엽전 n] 사면 바로 입음). 겉모습만 (전투 능력 같음).
    /// </summary>
    public sealed class CosmeticScreen : GameScreen
    {
        private readonly string _line;
        private readonly string _previewArmor, _previewHorse;
        private readonly bool _hasPreview;

        /// <param name="notice">결과 알림 → 공통 토스트 (2026-11-20: 위 글줄 대신).</param>
        public CosmeticScreen(string line, string notice = null, bool bad = false) { _line = line; if (notice != null) Toast.Show(notice, bad); }

        private CosmeticScreen(string line, string notice, string previewArmor, string previewHorse)
        {
            _line = line; if (notice != null) Toast.Show(notice); _previewArmor = previewArmor; _previewHorse = previewHorse; _hasPreview = true;
        }

        public override void Build()
        {
            var svc = Ctx.Cosmetics;
            string wornArmor = CosmeticService.Worn(Ctx.State, _line, CosmeticKinds.Armor);
            string wornHorse = CosmeticService.Worn(Ctx.State, _line, CosmeticKinds.Horse);
            string showArmor = _hasPreview ? _previewArmor : wornArmor, showHorse = _hasPreview ? _previewHorse : wornHorse;
            bool mounted = ContainsLine(CosmeticService.MountedLines, _line);

            var (header, body, _, right) = Ui.TitledPage(Root, T("ui.cos.title", FormationTabView.TroopName(Ctx, _line)), () => App.ShowMain("troop"));
            Ui.Label(right, Ui.Colored(T("resource.gold") + " " + new Wallet(Ctx.State.Wallet).Get(ResourceIds.Gold).ToString("N0"), Palette.TextDim), 24, TextAnchor.MiddleRight);
            InfoButton.Add(Root, T("ui.cos.note"));

            // 큰 미리보기
            var stage = Ui.Card(body, "Preview", 12, 6);
            var picRow = Ui.Row(stage, "Pic", 420, 0);
            picRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var big = Picture(picRow, 420, showArmor, showHorse);
            Ui.Size(big, 420, 420).flexibleWidth = 0;
            string looking = (showArmor == wornArmor && showHorse == wornHorse) ? T("ui.cos.now") : T("ui.cos.trying");
            Ui.Label(stage, looking, 24, TextAnchor.MiddleCenter, Palette.TextDim);

            Ui.Label(stage, T("ui.cos.wearing", wornArmor == null ? T("ui.cos.default") : T(svc.Find(wornArmor)?.NameKey ?? "ui.cos.default")), 22, TextAnchor.MiddleCenter, Palette.TextDim);

            // 가진 세트 목록 · 더 얻기 (2026-11-25 사용자 "산 세트는 갖고 있어 언제든 입고, 여러 개면 목록")
            Grid(body, svc, CosmeticKinds.Armor, T("ui.cos.ownedArmor", svc.OwnedFor(Ctx.State, _line, CosmeticKinds.Armor).Count), true, wornArmor, showArmor, showHorse);
            if (mounted) Grid(body, svc, CosmeticKinds.Horse, T("ui.cos.ownedHorse", svc.OwnedFor(Ctx.State, _line, CosmeticKinds.Horse).Count), true, wornHorse, showArmor, showHorse);
            Grid(body, svc, CosmeticKinds.Armor, T("ui.cos.moreArmor"), false, wornArmor, showArmor, showHorse);
            if (mounted) Grid(body, svc, CosmeticKinds.Horse, T("ui.cos.moreHorse"), false, wornHorse, showArmor, showHorse);
        }

        /// <summary>입어 보기 상태로 열기 (캡처 · 상점에서 "입어 보기").</summary>
        public static CosmeticScreen Trying(string line, string armor, string horse) => new CosmeticScreen(line, null, armor, horse);

        public override bool OnBack()
        {
            App.ShowMain("troop");
            return true;
        }

        private static bool ContainsLine(System.Collections.Generic.IReadOnlyList<string> list, string x)
        {
            foreach (var v in list) if (v == x) return true;
            return false;
        }

        /// <summary>그 모습의 병종 그림 (못 찍으면 미리 그린 PNG).</summary>
        private Image Picture(Transform parent, float size, string armor, string horse)
        {
            var box = Ui.Rect(parent, "Picture");
            var bg = Ui.Background(box, Palette.PictureBack);
            bg.sprite = UiShapes.Rounded(14);
            bg.type = Image.Type.Sliced;
            var img = Ui.Background(Ui.Rect(box, "Image"), Color.white);
            Ui.Fill(img.rectTransform);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.sprite = TroopPictureRenderer.For(Ctx, _line, armor, horse) ?? Resources.Load<Sprite>("Ui/troops/" + _line);
            return bg;
        }

        /// <summary>세트 칸 격자 (셋씩 한 줄): owned = 기본 + 가진 것 ([입기] · 입음), 아니면 안 가진 것 ([사고 입기] · 얻는 길).</summary>
        private void Grid(RectTransform body, CosmeticService svc, string kind, string title, bool owned, string worn, string showArmor, string showHorse)
        {
            var list = owned ? svc.OwnedFor(Ctx.State, _line, kind) : svc.NotOwnedFor(Ctx.State, _line, kind);
            if (!owned && list.Count == 0) return;
            var card = Ui.Card(body, "Section_" + kind + (owned ? "_owned" : "_more"), 16, 8);
            Ui.Label(card, title, 30).fontStyle = FontStyle.Bold;
            if (owned && list.Count == 0) Ui.Label(card, T("ui.cos.ownedNone"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            RectTransform row = null;
            int n = 0;
            void Next() { if (n++ % 3 == 0) row = Ui.Row(card, "Cells", 400, 12); }
            if (owned) { Next(); Cell(row, svc, kind, null, worn, showArmor, showHorse); }
            foreach (var d in list) { Next(); Cell(row, svc, kind, d, worn, showArmor, showHorse); }
            for (int i = n % 3; i > 0 && i < 3; i++) Ui.Size(Ui.Rect(row, "Empty"), 400, flexibleWidth: 1);
        }

        private void Cell(RectTransform row, CosmeticService svc, string kind, CosmeticDef d, string worn, string showArmor, string showHorse)
        {
            string id = d?.Id;
            bool isWorn = worn == id;
            bool owned = id == null || CosmeticService.Owns(Ctx.State, id);
            bool shown = (kind == CosmeticKinds.Armor ? showArmor : showHorse) == id;
            var cell = Ui.Rect(row, "Cell_" + (id ?? "default"));
            Ui.Size(cell, 400, flexibleWidth: 1);
            var bg = Ui.Background(cell, shown ? Palette.CellOn : Palette.Card);
            bg.sprite = UiShapes.Rounded(14);
            bg.type = Image.Type.Sliced;
            Ui.Column(cell, 4, 8);
            // 칸 그림: 그 갑옷(또는 말)을 입힌 모습
            string a = kind == CosmeticKinds.Armor ? id : showArmor, h = kind == CosmeticKinds.Horse ? id : showHorse;
            Ui.Size(Picture(cell, 220, a, h), 220, flexibleWidth: 1); // 칸 너비를 채움 (그림은 비율 유지 · 가운데)
            Ui.Label(cell, d == null ? T("ui.cos.default") : T(d.NameKey), 22, TextAnchor.MiddleCenter);
            Ui.Label(cell, Tier(d), 18, TextAnchor.MiddleCenter, Palette.TextDim);
            string previewArmor = kind == CosmeticKinds.Armor ? id : showArmor, previewHorse = kind == CosmeticKinds.Horse ? id : showHorse;
            var tap = cell.gameObject.AddComponent<Button>();
            tap.targetGraphic = bg;
            tap.onClick.AddListener(() => App.Show(new CosmeticScreen(_line, null, previewArmor, previewHorse)));

            Button btn;
            if (isWorn) { btn = Ui.SecondaryButton(cell, T("ui.cos.worn"), () => { }, 56, 22); Ui.SetEnabled(btn, false); }
            else if (owned) btn = Ui.Button(cell, T("ui.cos.wear"), () => Wear(kind, id), 56, Palette.ButtonActive, 22);
            else if ((d.PriceGold != null || d.PriceGem != null) && !svc.OnSale(d)) { btn = Ui.SecondaryButton(cell, T("ui.cos.notForSale"), () => { }, 56, 22); Ui.SetEnabled(btn, false); } // 표출을 끈 판매 치장 (R-123)
            else if (d.PriceGold != null) btn = Ui.Button(cell, T("ui.cos.price.gold", d.PriceGold.Value.ToString("N0")), () => BuyAndWear(svc, kind, d), 56, Palette.ButtonActive, 22);
            else if (d.PriceGem != null) btn = Ui.Button(cell, T("ui.cos.price.gem", d.PriceGem.Value.ToString("N0")), () => BuyAndWear(svc, kind, d), 56, Palette.ButtonActive, 22);
            else { btn = Ui.SecondaryButton(cell, T("ui.cos.price.shop"), () => { }, 56, 22); Ui.SetEnabled(btn, false); }
            Ui.Size(btn, 56);
        }

        /// <summary>얻는 길 (기본 · 엽전 · 금괴 · 결제 · 플레이 보상).</summary>
        private string Tier(CosmeticDef d)
        {
            if (d == null) return T("ui.looks.tier.default");
            if (d.PriceKRW != null) return T("ui.looks.tier.cash");
            if (d.PriceGem != null) return T("ui.looks.tier.gem");
            if (d.PriceGold != null) return T("ui.looks.tier.gold");
            return T("ui.looks.tier.play");
        }

        private void Wear(string kind, string id)
        {
            bool ok = Ctx.Do(PlayerActionRunner.EquipCosmetic(_line, kind, id));
            App.Show(new CosmeticScreen(_line, ok ? T("ui.cos.wore") : T("ui.cmd.rejected"), !ok));
        }

        private void BuyAndWear(CosmeticService svc, string kind, CosmeticDef d)
        {
            var w = new Wallet(Ctx.State.Wallet);
            if (d.PriceGold != null && w.Get(ResourceIds.Gold) < d.PriceGold.Value) { App.Show(new CosmeticScreen(_line, T("ui.cos.notEnough.gold"), true)); return; }
            if (d.PriceGem != null && w.Get(ResourceIds.Gem) < d.PriceGem.Value) { App.Show(new CosmeticScreen(_line, T("ui.cos.notEnough.gem"), true)); return; }
            Ui.Confirm(Root, T(d.NameKey), T("ui.cos.buyConfirm", d.PriceGold != null ? T("ui.cos.price.gold", d.PriceGold.Value.ToString("N0")) : T("ui.cos.price.gem", d.PriceGem.Value.ToString("N0"))), T("ui.cos.buy"), () =>
            {
                bool ok = Ctx.Do(PlayerActionRunner.BuyCosmetic(d.Id)) && Ctx.Do(PlayerActionRunner.EquipCosmetic(_line, kind, d.Id));
                App.Show(new CosmeticScreen(_line, ok ? T("ui.cos.boughtWore") : T("ui.cmd.rejected"), !ok));
            }, (T("ui.common.cancel"), () => { }));
        }
    }
}
