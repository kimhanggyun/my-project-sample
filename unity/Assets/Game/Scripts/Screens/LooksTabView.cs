using System.Collections.Generic;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.Battle3D;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// [병사꾸미기] · [장수꾸미기] 탭 — 개발용 (R-149 · R-150, 2026-11-24 사용자 "[테스트] 탭처럼 개발 단계에서만, 나중엔 병과 · 갑옷 세트를 사면 자동 적용",
    /// "병사 하나 놓고 투구 · 갑옷 · 창 · 방패를 바꿔 보기", "부품마다 번호 — 마음에 안 들면 번호로 지움").
    /// 병종(장수는 성향)을 고르고, 부위 칸 (세트 · 투구 · 갑옷 · 등 · 무기 묶음 · 칼 · 창 · 활 · 방패 · 말 · 색)에서 번호 붙은 부품을 골라 입혀 본다 (이 기기에만, <see cref="DevLooks"/>).
    /// 고른 모습은 샘플전투2의 우리 편 · 우리 장수에 그대로 나온다. [세트로 저장] = 지금 차림을 번호로 복사 (상점 세트 후보).
    /// </summary>
    public sealed class LooksTabView : ITabView
    {
        /// <summary>고를 병종 (1단계 + 승급 — 기본 차림이 다른 것).</summary>
        public static readonly string[] Troops = { "infantry", "shieldbearer", "guard", "spearman", "pikeman", "archer", "crossbowman", "cavalry", "heavyCavalry", "ironCavalry", "scout", "engineer" };
        public static readonly string[] Presets = { "warrior", "standard", "strategist" };
        /// <summary>부위 칸: 세트 · H 투구 · A 갑옷 · C 등 · L 무기 묶음 · S 칼·도끼 · P 창 · B 활 · D 방패 · M 말 · 색.</summary>
        public static readonly string[] Categories = { "set", "H", "A", "C", "L", "S", "P", "B", "D", "M", "color" };

        private readonly bool _general;
        private static string _troop = "infantry", _preset = "warrior", _catSoldier = "H", _catGeneral = "H";
        private static bool _commander = true;
        /// <summary>캡처 투어: 목록 스크롤 (1 = 맨 위, 0 = 맨 아래, 음수 = 그대로) — 부품이 부위마다 20개라 아래 카드까지 찍음.</summary>
        private static float _tourScroll = -1f;
        private GameApp _app;
        private RectTransform _root;

        public LooksTabView(bool general = false) { _general = general; }

        public void Build(GameApp app, RectTransform root)
        {
            _app = app;
            _root = root;
            Redraw();
        }

        private string T(string key, params object[] args) => _app.Context.T(key, args);
        private string Cat { get => _general ? _catGeneral : _catSoldier; set { if (_general) _catGeneral = value; else _catSoldier = value; } }
        private string DevKey => _general ? DevLooks.GeneralKey(_preset) : _troop;

        /// <summary>캡처 투어: 병종 · 세트를 골라 다시 그림.</summary>
        public static void PickForTour(string troop, string armor) { _troop = troop; DevLooks.SetArmor(troop, armor); }
        /// <summary>캡처 투어: 보는 칸 · 병종 · 장수 성향.</summary>
        public static void ShowForTour(string troop, string preset, string soldierCat, string generalCat, float scroll = -1f)
        {
            _tourScroll = scroll;
            if (troop != null) _troop = troop;
            if (preset != null) _preset = preset;
            if (soldierCat != null) _catSoldier = soldierCat;
            if (generalCat != null) _catGeneral = generalCat;
        }

        private SoldierLook Current() => _general ? DevLooks.ResolveGeneral(_app.Context, _preset, _commander) : DevLooks.Resolve(_app.Context, _troop);

        private void Redraw()
        {
            Ui.Clear(_root);
            var ctx = _app.Context;
            var list = Ui.ScrollList(_root, "List", 14, 20);
            Ui.Fill((RectTransform)list.parent.parent); // 탭 전체를 채움 ([테스트] 탭과 같음 — 빠지면 목록 크기가 0)
            InfoButton.Add(_root, T(_general ? "ui.glooks.note" : "ui.looks.note"));
            var look = Current();
            var lib = look.Library;
            // 고르기: 병종 (다섯씩 줄) · 장수 성향 + 총대장
            RectTransform row = null;
            int n = 0;
            if (_general)
            {
                row = Ui.Row(list, "Presets", 68, 8);
                foreach (var p in Presets)
                {
                    string id = p;
                    Ui.Size(Ui.Button(row, T("ui.crowd.general." + id), () => { _preset = id; Redraw(); }, 68, _preset == id ? Palette.ButtonActive : Palette.Button, 24), flexibleWidth: 1);
                }
                Ui.Size(Ui.Button(row, T("ui.looks.commander") + (_commander ? " ✓" : ""), () => { _commander = !_commander; Redraw(); }, 68, _commander ? Palette.ButtonActive : Palette.Button, 22), flexibleWidth: 1);
            }
            else
                foreach (var id in Troops)
                {
                    if (!ctx.Defs.TroopTypes.ContainsKey(id)) continue;
                    if (n++ % 6 == 0) row = Ui.Row(list, "Troops", 64, 8);
                    string t = id;
                    Ui.Size(Ui.Button(row, T("troop." + id), () => { _troop = t; Redraw(); }, 64, _troop == id ? Palette.ButtonActive : Palette.Button, 20), flexibleWidth: 1);
                }
            // 지금 모습 (앞 · 옆) + 번호 요약
            var now = Ui.Card(list, "Now", 16, 8);
            string who = _general ? T("ui.crowd.general." + _preset) : T("troop." + _troop);
            Ui.Label(now, T("ui.looks.now", who), 28).fontStyle = FontStyle.Bold;
            var pics = Ui.Row(now, "Pictures", 380, 12);
            Ui.Size(Picture(pics, look, false), 380, 340);
            Ui.Size(Picture(pics, look, true), 380, 340);
            Ui.Label(now, Summary(look), 20, TextAnchor.MiddleLeft, Palette.TextDim);
            var acts = Ui.Row(now, "Actions", 60, 12);
            Ui.Size(Ui.Button(acts, T("ui.looks.save"), () =>
            {
                string json = DevLooks.Export(DevKey, Current());
                GUIUtility.systemCopyBuffer = json;
                Debug.Log("꾸미기 세트: " + json);
                Toast.Show(T("ui.looks.saved"));
            }, 60, Palette.Button, 22), flexibleWidth: 1);
            Ui.Size(Ui.Button(acts, T("ui.looks.reset"), () => { DevLooks.Reset(DevKey); Redraw(); }, 60, Palette.Button, 22), flexibleWidth: 1);
            // 부위 칸
            n = 0;
            foreach (var c in Categories)
            {
                if (c == "M" && !look.Mounted) continue;
                if (n++ % 6 == 0) row = Ui.Row(list, "Cats", 60, 8);
                string cat = c;
                Ui.Size(Ui.Button(row, T("ui.looks.cat." + c), () => { Cat = cat; Redraw(); }, 60, Cat == c ? Palette.ButtonActive : Palette.Button, 20), flexibleWidth: 1);
            }
            var body = Ui.Card(list, "Body", 16, 8);
            if (Cat == "set") Sets(body, look);
            else if (Cat == "color") ColorsPanel(body, look);
            else if (Cat == "L") Loadouts(body, look, lib);
            else Parts(body, look, lib, Cat);
            if (_tourScroll >= 0f)
            {
                var sr = _root.GetComponentInChildren<ScrollRect>();
                if (sr != null)
                {
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(sr.content);
                    sr.verticalNormalizedPosition = _tourScroll;
                }
            }
        }

        /// <summary>번호 요약: "H01 삿갓 · A01 무명 옷 · …" (2026-11-24 사용자 "번호를 말하면 지움").</summary>
        private static string Summary(SoldierLook look)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in LookPartLibrary.Slots)
            {
                if (s == LookPartLibrary.SlotTack && !look.Mounted) continue;
                var p = look.Library?.Find(look.Of(s));
                if (p == null) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(p.Id).Append(' ').Append(p.Name);
            }
            return sb.ToString();
        }

        private Image Picture(Transform parent, SoldierLook look, bool side)
        {
            var box = Ui.Rect(parent, "Picture");
            var bg = Ui.Background(box, Palette.PictureBack);
            bg.sprite = UiShapes.Rounded(14);
            bg.type = Image.Type.Sliced;
            var img = Ui.Background(Ui.Rect(box, "Image"), Color.white);
            Ui.Fill(img.rectTransform);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.sprite = TroopPictureRenderer.ForLook(look, side);
            if (img.sprite == null) img.color = new Color(1f, 1f, 1f, 0f);
            return bg;
        }

        /// <summary>카드 격자: 넷씩 한 줄.</summary>
        /// <summary>마지막 줄을 빈 칸으로 채움 (카드 한 장이 줄 전체로 늘어나던 것 — 2026-11-24 캡처 50b).</summary>
        private static void FillRow(RectTransform row, int n)
        {
            if (row == null) return;
            for (int i = n % 4; i > 0 && i < 4; i++) Ui.Size(Ui.Rect(row, "Empty"), 330, flexibleWidth: 1);
        }

        private static RectTransform GridRow(RectTransform parent, ref int n, ref RectTransform row)
        {
            if (n++ % 4 == 0) row = Ui.Row(parent, "Cells", 330, 10);
            return row;
        }

        private void Cell(RectTransform row, SoldierLook preview, string title, string sub, bool on, System.Action pick)
        {
            var cell = Ui.Rect(row, "Cell");
            Ui.Size(cell, 330, flexibleWidth: 1);
            var bg = Ui.Background(cell, on ? Palette.CellOn : Palette.Card);
            bg.sprite = UiShapes.Rounded(14);
            bg.type = Image.Type.Sliced;
            Ui.Column(cell, 2, 6);
            Ui.Size(Picture(cell, preview, preview.Mounted && Cat == "M"), 210);
            Ui.Label(cell, title, 20, TextAnchor.MiddleCenter).fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
            Ui.Label(cell, sub, 17, TextAnchor.MiddleCenter, Palette.TextDim);
            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => { pick(); Redraw(); });
        }

        private string Grade(string g) => T("ui.looks.grade." + (g ?? "basic"));

        /// <summary>그 부위의 부품들 (번호 차례) + "없음".</summary>
        private void Parts(RectTransform body, SoldierLook look, LookPartLibrary lib, string letter)
        {
            string slot = LookPartLibrary.SlotOf(letter + "00");
            int n = 0;
            RectTransform row = null;
            string worn = look.Of(slot);
            if (slot != LookPartLibrary.SlotArmor)
            {
                var bare = look.Clone();
                bare.Set(slot, null);
                Cell(GridRow(body, ref n, ref row), bare, T("ui.looks.none"), "", worn == null, () => DevLooks.SetPart(DevKey, slot, "-"));
            }
            foreach (var p in lib.Ordered)
            {
                if (p.Id[0] != letter[0]) continue;
                var pv = look.Clone();
                pv.Set(slot, p.Id);
                string id = p.Id;
                Cell(GridRow(body, ref n, ref row), pv, p.Id + " " + p.Name, Grade(p.Grade), worn == p.Id, () => DevLooks.SetPart(DevKey, slot, id));
            }
            FillRow(row, n);
            if (n <= 1) Ui.Label(body, T("ui.looks.empty"), 20, TextAnchor.MiddleCenter, Palette.TextDim);
        }

        private void Loadouts(RectTransform body, SoldierLook look, LookPartLibrary lib)
        {
            int n = 0;
            RectTransform row = null;
            foreach (var l in lib.OrderedLoadouts)
            {
                var pv = look.Clone();
                pv.ApplyLoadout(l.Id);
                bool on = look.Of(LookPartLibrary.SlotRight) == l.Right && look.Of(LookPartLibrary.SlotLeft) == l.Left;
                var lo = l;
                Cell(GridRow(body, ref n, ref row), pv, l.Id + " " + l.Name, string.Join(" + ", new[] { l.Right, l.Left, l.Back }.Where0()), on, () => DevLooks.SetLoadout(DevKey, lo));
            }
            FillRow(row, n);
        }

        /// <summary>갑옷 세트 (사지 않고 입혀 봄) · 말 색.</summary>
        private void Sets(RectTransform body, SoldierLook look)
        {
            var ctx = _app.Context;
            var svc = new CosmeticService(ctx.Defs);
            string line = _general ? null : CosmeticService.LineOf(ctx.Defs, _troop) ?? _troop;
            var e = DevLooks.Get(DevKey);
            Ui.Label(body, T("ui.looks.sets"), 26).fontStyle = FontStyle.Bold;
            int n = 0;
            RectTransform row = null;
            SetCell(GridRow(body, ref n, ref row), null, e.Armor == null, true);
            foreach (var d in svc.Items)
                if (d.Kind == CosmeticKinds.Armor && (line == null || CosmeticService.CanWear(d, line))) SetCell(GridRow(body, ref n, ref row), d, e.Armor == d.Id, true);
            FillRow(row, n);
            if (!look.Mounted) return;
            Ui.Label(body, T("ui.looks.horses"), 26).fontStyle = FontStyle.Bold;
            n = 0;
            SetCell(GridRow(body, ref n, ref row), null, e.Horse == null, false);
            foreach (var d in svc.Items)
                if (d.Kind == CosmeticKinds.Horse) SetCell(GridRow(body, ref n, ref row), d, e.Horse == d.Id, false);
            FillRow(row, n);
        }

        private void SetCell(RectTransform row, CosmeticDef d, bool on, bool armor)
        {
            var e = DevLooks.Get(DevKey);
            string keepA = e.Armor, keepH = e.Horse, id = d?.Id;
            // 그 세트를 입은 모습을 잠깐 만들어 그림
            if (armor) e.Armor = id; else e.Horse = id;
            var pv = Current();
            e.Armor = keepA;
            e.Horse = keepH;
            Cell(row, pv, d == null ? T("ui.cos.default") : T(d.NameKey), Tier(d), on, () => { if (armor) DevLooks.SetArmor(DevKey, id); else DevLooks.SetHorse(DevKey, id); });
        }

        /// <summary>색 자리마다 색 칸 (기본 + 이름 있는 색).</summary>
        private void ColorsPanel(RectTransform body, SoldierLook look)
        {
            var e = DevLooks.Get(DevKey);
            foreach (var slot in SoldierLook.ColorSlots)
            {
                if (slot == "horse" && !look.Mounted) continue;
                Ui.Label(body, T("ui.looks.color." + slot), 22).fontStyle = FontStyle.Bold;
                var r = Ui.HScrollRow(body, "Swatches_" + slot, 74, 6, 2);
                e.Colors.TryGetValue(slot, out var cur);
                Swatch(r, null, cur == null, slot);
                foreach (var name in CosmeticLooks.Names) Swatch(r, name, cur == name, slot);
            }
        }

        private void Swatch(RectTransform row, string name, bool on, string slot)
        {
            Color c = name != null && CosmeticLooks.TryNamed(name, out var nc) ? nc : new Color(0.8f, 0.8f, 0.8f);
            var b = Ui.Button(row, name == null ? T("ui.looks.color.default") : "", () => { DevLooks.SetColor(DevKey, slot, name); Redraw(); }, 64, c, 18);
            Ui.Size(b, 64, name == null ? 96 : 64).flexibleWidth = 0;
            if (on)
            {
                var o = b.gameObject.AddComponent<Outline>();
                o.effectColor = Color.black;
                o.effectDistance = new Vector2(4f, 4f);
            }
        }

        /// <summary>얻는 길 (엽전 · 금괴 · 결제 · 플레이).</summary>
        private string Tier(CosmeticDef d)
        {
            if (d == null) return T("ui.looks.tier.default");
            if (d.PriceKRW != null) return T("ui.looks.tier.cash");
            if (d.PriceGem != null) return T("ui.looks.tier.gem");
            if (d.PriceGold != null) return T("ui.looks.tier.gold");
            return T("ui.looks.tier.play");
        }
    }

    internal static class LooksTabExt
    {
        public static IEnumerable<string> Where0(this IEnumerable<string> xs) { foreach (var x in xs) if (!string.IsNullOrEmpty(x)) yield return x; }
    }
}
