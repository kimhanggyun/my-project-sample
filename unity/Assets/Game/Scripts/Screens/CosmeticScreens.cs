using System.Collections.Generic;
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
        /// <summary>목록 스크롤 위치 (입기 · 사기 뒤 다시 그려도 그 자리, 2026-11-27 사용자 "누르면 첫 번째로 올라감"). 새로 열면 맨 위.</summary>
        private static float _scrollY = 1f;
        private static bool _keepScroll;
        // 입어 보기는 화면을 다시 만들지 않고 그 자리에서 바꾼다 (미리보기 그림 · 글 · 고른 칸 표시)
        private string _tryArmor, _tryHorse, _wornArmor, _wornHorse;
        private Image _bigPicture;
        private Text _lookingText;
        private readonly List<(string kind, string id, Image bg)> _cells = new List<(string, string, Image)>();

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

            _wornArmor = wornArmor; _wornHorse = wornHorse; _tryArmor = showArmor; _tryHorse = showHorse;
            _cells.Clear();
            // 위 = 공통 자원 바 (엽전 글자 대신, 2026-11-27 사용자), 아래 = 제목 · 고정 미리보기 · 스크롤 목록
            Ui.Column(Root, 0, 0);
            MainScreen.TopBar(App, Root);
            var page = Ui.Rect(Root, "Page");
            Ui.Size(page, flexibleHeight: 1);
            var (header, body, _, right) = Ui.TitledPage(page, T("ui.cos.title", FormationTabView.TroopName(Ctx, _line)), () => App.ShowMain("troop"));
            InfoButton.Add(Root, T("ui.cos.note"));

            // 큰 미리보기: 머리(고정)에 — 아래 목록을 내려도 올라가지 않음 (2026-11-27 사용자)
            var stage = Ui.Card(header, "Preview", 10, 4);
            var picRow = Ui.Row(stage, "Pic", 340, 0);
            picRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var big = Picture(picRow, 340, showArmor, showHorse);
            Ui.Size(big, 340, 340).flexibleWidth = 0;
            _bigPicture = big.transform.Find("Image")?.GetComponent<Image>();
            _lookingText = Ui.Label(stage, "", 22, TextAnchor.MiddleCenter, Palette.TextDim);
            UpdateLooking();

            // 가진 세트 목록 · 더 얻기 (2026-11-25 사용자 "산 세트는 갖고 있어 언제든 입고, 여러 개면 목록")
            Grid(body, svc, CosmeticKinds.Armor, T("ui.cos.ownedArmor", svc.OwnedFor(Ctx.State, _line, CosmeticKinds.Armor).Count), true, wornArmor, showArmor, showHorse);
            if (mounted) Grid(body, svc, CosmeticKinds.Horse, T("ui.cos.ownedHorse", svc.OwnedFor(Ctx.State, _line, CosmeticKinds.Horse).Count), true, wornHorse, showArmor, showHorse);
            Grid(body, svc, CosmeticKinds.Armor, T("ui.cos.moreArmor"), false, wornArmor, showArmor, showHorse);
            if (mounted) Grid(body, svc, CosmeticKinds.Horse, T("ui.cos.moreHorse"), false, wornHorse, showArmor, showHorse);

            // 스크롤 위치: 입기 · 사기 뒤에는 그 자리, 새로 열면 맨 위
            var scroll = body.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                if (!_keepScroll) _scrollY = 1f;
                _keepScroll = false;
                LayoutRebuilder.ForceRebuildLayoutImmediate(body);
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = _scrollY;
                scroll.onValueChanged.AddListener(v => _scrollY = v.y);
            }
        }

        /// <summary>미리보기 아래 글: 지금 모습 · 입어 보는 중 + 입은 세트 이름.</summary>
        private void UpdateLooking()
        {
            if (_lookingText == null) return;
            var svc = Ctx.Cosmetics;
            string looking = (_tryArmor == _wornArmor && _tryHorse == _wornHorse) ? T("ui.cos.now") : T("ui.cos.trying");
            _lookingText.text = looking + "\n" + T("ui.cos.wearing", _wornArmor == null ? T("ui.cos.default") : T(svc.Find(_wornArmor)?.NameKey ?? "ui.cos.default"));
            Ui.Size(_lookingText, 64);
        }

        /// <summary>캡처 투어: 목록을 내린 뒤 n번째 갑옷 칸 입어 보기 (스크롤 · 미리보기 고정 확인).</summary>
        internal void ScrollAndTryForTour(float scrollY, int index)
        {
            var sr = Root.GetComponentsInChildren<ScrollRect>();
            foreach (var s in sr) if (s.vertical) s.verticalNormalizedPosition = scrollY;
            int n = 0;
            foreach (var (k, id, _) in _cells) if (k == CosmeticKinds.Armor && n++ == index) { Try(k, id); return; }
        }

        /// <summary>입어 보기: 화면을 다시 만들지 않고 미리보기 · 고른 칸 표시만 바꾼다 (스크롤 그대로).</summary>
        private void Try(string kind, string id)
        {
            if (kind == CosmeticKinds.Armor) _tryArmor = id; else _tryHorse = id;
            if (_bigPicture != null)
                _bigPicture.sprite = TroopPictureRenderer.For(Ctx, _line, _tryArmor, _tryHorse) ?? Resources.Load<Sprite>("Ui/troops/" + _line);
            foreach (var (k, cid, bg) in _cells)
                if (bg != null && k == kind) bg.color = cid == id ? Palette.CellOn : Palette.Card;
            UpdateLooking();
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
            UiKit.PictureWell(bg); // 움푹 들어간 그림 칸 (card.picture)
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
            UiKit.DressCard(cell, bg, "card.item.standard", null); // 카드 체계: 아이템 · 보통 (ui-kit.md 4.4)
            Ui.Column(cell, 4, 8);
            // 칸 그림: 그 갑옷(또는 말)을 입힌 모습
            string a = kind == CosmeticKinds.Armor ? id : showArmor, h = kind == CosmeticKinds.Horse ? id : showHorse;
            Ui.Size(Picture(cell, 220, a, h), 220, flexibleWidth: 1); // 칸 너비를 채움 (그림은 비율 유지 · 가운데)
            // 이름 줄: 얻는 재화 아이콘(엽전 · 금괴) + 이름 (2026-11-27 사용자), 그 밖(기본 · 결제 · 보상)은 아래 작은 글
            var nameRow = Ui.Row(cell, "Name", 34, 6);
            var nh = nameRow.GetComponent<HorizontalLayoutGroup>();
            nh.childAlignment = TextAnchor.MiddleCenter;
            nh.childForceExpandWidth = false;
            string cur = d == null ? null : d.PriceGem != null ? ResourceIds.Gem : d.PriceGold != null && d.PriceKRW == null ? ResourceIds.Gold : null;
            if (cur != null) Icons.Make(Ctx, nameRow, cur, 30);
            var nm = Ui.Label(nameRow, d == null ? T("ui.cos.default") : T(d.NameKey), 22, cur != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            nm.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(nm, 34, Mathf.Max(60, (nm.text.Length) * 22f));
            Ui.Label(cell, cur != null ? "" : Tier(d), 18, TextAnchor.MiddleCenter, Palette.TextDim);
            _cells.Add((kind, id, bg));
            var tap = cell.gameObject.AddComponent<Button>();
            tap.targetGraphic = bg;
            string tryKind = kind, tryId = id;
            tap.onClick.AddListener(() => { Sfx.Play("click", 0.5f); Try(tryKind, tryId); });

            Button btn;
            if (isWorn) { btn = Ui.SecondaryButton(cell, T("ui.cos.worn"), () => { }, 56, 22); Ui.SetEnabled(btn, false); }
            else if (owned) btn = Ui.Button(cell, T("ui.cos.wear"), () => Wear(kind, id), 56, Palette.ButtonActive, 22);
            else if ((d.PriceGold != null || d.PriceGem != null) && !svc.OnSale(d)) { btn = Ui.SecondaryButton(cell, T("ui.cos.notForSale"), () => { }, 56, 22); Ui.SetEnabled(btn, false); } // 표출을 끈 판매 치장 (R-123)
            else if (d.PriceGold != null) btn = PriceButton(cell, ResourceIds.Gold, d.PriceGold.Value, () => BuyAndWear(svc, kind, d));
            else if (d.PriceGem != null) btn = PriceButton(cell, ResourceIds.Gem, d.PriceGem.Value, () => BuyAndWear(svc, kind, d));
            else { btn = Ui.SecondaryButton(cell, T("ui.cos.price.shop"), () => { }, 56, 22); Ui.SetEnabled(btn, false); }
            Ui.Size(btn, 56);
        }

        /// <summary>사기 버튼: 재화 아이콘 + 값 (엽전 · 금괴 글자 대신, 2026-11-27 사용자).</summary>
        private Button PriceButton(RectTransform cell, string resource, long price, System.Action buy)
        {
            var b = Ui.Button(cell, price.ToString("N0"), buy, 56, Palette.ButtonActive, 24);
            var label = b.GetComponentInChildren<Text>();
            label.color = Ui.OnAccent;
            label.rectTransform.offsetMin = new Vector2(44f, label.rectTransform.offsetMin.y);
            var ic = Icons.Make(Ctx, b.transform, resource, 36);
            ic.gameObject.GetComponent<LayoutElement>().ignoreLayout = true;
            ic.anchorMin = ic.anchorMax = new Vector2(0.5f, 0.5f);
            ic.pivot = new Vector2(0.5f, 0.5f);
            ic.sizeDelta = new Vector2(36, 36);
            ic.anchoredPosition = new Vector2(-12f - price.ToString("N0").Length * 7f, 0f);
            return b;
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
            _keepScroll = true;
            App.Show(new CosmeticScreen(_line, ok ? T("ui.cos.wore") : T("ui.cmd.rejected"), !ok));
        }

        private void BuyAndWear(CosmeticService svc, string kind, CosmeticDef d)
        {
            var w = new Wallet(Ctx.State.Wallet);
            // 모자라면 알림만 (화면을 다시 그리지 않음 — 스크롤 · 입어 보기 그대로)
            if (d.PriceGold != null && w.Get(ResourceIds.Gold) < d.PriceGold.Value) { Toast.Show(T("ui.cos.notEnough.gold"), true); return; }
            if (d.PriceGem != null && w.Get(ResourceIds.Gem) < d.PriceGem.Value) { Toast.Show(T("ui.cos.notEnough.gem"), true); return; }
            Ui.Confirm(Root, T(d.NameKey), T("ui.cos.buyConfirm", d.PriceGold != null ? T("ui.cos.price.gold", d.PriceGold.Value.ToString("N0")) : T("ui.cos.price.gem", d.PriceGem.Value.ToString("N0"))), T("ui.cos.buy"), () =>
            {
                bool ok = Ctx.Do(PlayerActionRunner.BuyCosmetic(d.Id)) && Ctx.Do(PlayerActionRunner.EquipCosmetic(_line, kind, d.Id));
                _keepScroll = true;
                App.Show(new CosmeticScreen(_line, ok ? T("ui.cos.boughtWore") : T("ui.cmd.rejected"), !ok));
            }, (T("ui.common.cancel"), () => { }));
        }
    }
}
