using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Unity.Boot;
using Game.Unity.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 부품 꾸미기 (개발 줄 [부품] 탭, docs/design/ui-kit.md 3장): 위 세그먼트 = 종류 (data/Ui/uiKit.json categories),
    /// 부품 고르기 → 미리보기 (데이터 값 A · 지금 개발 값 B) → 조절판 (변형 · 색 · 숫자 · 선택 · 그림). 바꾸면 이 기기에 바로 저장 (UiKit 개발 값),
    /// [데이터로 내보내기] = styles.json (에디터) / 클립보드 (기기). 개발용이라 문구는 한글 그대로.
    /// </summary>
    public sealed class PartsTabView : ITabView
    {
        public static string Category = "button";
        public static string RoleId;
        private static float _scroll = 1f;
        /// <summary>가로 칩 줄의 스크롤 위치 (부품.값 → 0~1): 칩을 눌러 다시 그려도 그 자리 그대로 (2026-11-27 사용자).</summary>
        private static readonly Dictionary<string, float> HScroll = new Dictionary<string, float>();
        private readonly List<(ScrollRect sr, string key)> _hRows = new List<(ScrollRect, string)>();

        private GameApp _app;
        private RectTransform _root;

        /// <summary>종류 · 부품 고르기 (캡처 투어).</summary>
        public static void Pick(string category, string role) { Category = category; RoleId = role; _scroll = 1f; }

        public void Build(GameApp app, RectTransform root)
        {
            _app = app;
            _root = root;
            UiStyle.Page = null;
            var list = Ui.ScrollList(root, "Kit", 14, 25);
            Ui.Fill((RectTransform)list.parent.parent);
            var scroll = list.GetComponentInParent<ScrollRect>();
            Ui.SectionHeader(list, "부품 꾸미기 — 바꾸면 이 기기에 바로 저장 · 바뀐 값 " + UiKit.DevCount + "개");
            var cats = new List<(string, string)>(UiKit.Categories);
            if (cats.Count == 0) cats.Add(("list", "부품 목록"));
            Ui.SegmentTabs(list, cats, Category, id => { Pick(id, null); Redraw(); }, 58, (GameApp.ReferenceWidth - 90f) / cats.Count - 8f);
            if (Category == "list") PartsListView.Fill(app, root, list);
            else
            {
                if (Category == "theme") ThemePanel(list);
                else RolePanel(list);
                Actions(list);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            Canvas.ForceUpdateCanvases();
            foreach (var (sr, key) in _hRows)
            {
                if (sr == null) continue;
                if (HScroll.TryGetValue(key, out var x)) sr.horizontalNormalizedPosition = x;
                string k = key;
                sr.onValueChanged.AddListener(v => HScroll[k] = v.x);
            }
            _hRows.Clear();
            if (scroll != null)
            {
                scroll.verticalNormalizedPosition = _scroll;
                scroll.onValueChanged.AddListener(v => _scroll = v.y);
            }
        }

        private void Redraw()
        {
            _app.ApplyBackdrop();
            _app.ShowMain("parts");
        }

        // ---------------- 부품 ----------------

        private void RolePanel(RectTransform list)
        {
            var roles = UiKit.Roles.FindAll(r => r.Category == Category);
            if (roles.Count == 0) { Ui.Hint(list, "이 종류는 다음 PR에서 (ui-kit.md 7장)", 60); return; }
            if (RoleId == null || !roles.Exists(r => r.Id == RoleId)) RoleId = roles[0].Id;
            var chips = new List<(string, string)>();
            foreach (var r in roles) chips.Add((r.Id, r.Label + (UiKit.HasDev(r.Id) ? " ●" : "")));
            Chips(list, chips, RoleId, id => { RoleId = id; Redraw(); }, Category == "card" ? 4 : 3);

            var role = UiKit.RoleOf(RoleId);
            if (!UiKit.Kinds.TryGetValue(role.Kind, out var kind)) return;
            Ui.SectionHeader(list, "미리보기 — " + role.Label + " (" + role.Id + ")");
            if (UiKit.HasDev(role.Id))
            {
                Ui.Hint(list, "A · 데이터 값 (styles.json)", 40);
                UiKit.DataOnly = true;
                try { Preview(list, role.Id); }
                finally { UiKit.DataOnly = false; }
                Ui.Hint(list, "B · 지금 (바꾼 값)", 40);
            }
            foreach (var w in Preview(list, role.Id)) Ui.ErrorLine(list, "⚠ " + w, 44);

            var cur = UiKit.Spec(role.Id);
            Ui.SectionHeader(list, "모양 (변형) = " + (cur.Variant ?? "(기본)") + (UiKit.HasDev(role.Id, "variant") ? " ●" : ""));
            var vs = new List<(string, string)> { ("", "기본") };
            foreach (var v in kind.Variants) vs.Add((v, v));
            Chips(list, vs, cur.Variant ?? "", v => Set(role.Id, "variant", v == "" ? null : new JValue(v)), 4);
            foreach (var f in kind.Fields) FieldControl(list, role.Id, f, cur);
        }

        private void Set(string role, string key, JToken value)
        {
            UiKit.SetDev(role, key, value);
            Redraw();
        }

        private void FieldControl(RectTransform list, string role, UiKit.Field f, PartSpec cur)
        {
            var raw = cur.Raw(f.Name);
            string shown = raw == null ? "(없음)" : raw.Type == JTokenType.Float ? ((float)raw).ToString("0.##", CultureInfo.InvariantCulture) : raw.ToString();
            Ui.SectionHeader(list, f.Label + " (" + f.Name + ") = " + shown + (UiKit.HasDev(role, f.Name) ? " ●" : ""));
            switch (f.Type)
            {
                case "color":
                    {
                        var row = Ui.HScrollRow(list, "Colors_" + f.Name, 72, 6, 4);
                        Chip(row, "기본", Palette.Button, raw == null, () => Set(role, f.Name, null), 110);
                        foreach (var n in UiKit.ThemeColors)
                        {
                            string name = n;
                            var c = UiKit.ThemeColor(n, Color.magenta);
                            Chip(row, n, c.a < 0.1f ? Palette.Card : c, raw != null && (string)raw == n, () => Set(role, f.Name, new JValue(name)), 150);
                        }
                        _hRows.Add((row.GetComponentInParent<ScrollRect>(), role + "." + f.Name));
                        var hexRow = Ui.Row(list, "Hex_" + f.Name, 60, 10);
                        hexRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                        Ui.Size(Ui.Label(hexRow, "직접 #RRGGBB", 24, TextAnchor.MiddleLeft, Palette.TextDim), 60, 220);
                        var input = Ui.TextInput(hexRow, "#RRGGBB", 9, 60, 26);
                        Ui.Size(input, 60, 260);
                        if (raw != null && ((string)raw).StartsWith("#")) input.text = (string)raw;
                        input.onEndEdit.AddListener(t =>
                        {
                            t = t.Trim();
                            if (!t.StartsWith("#")) t = "#" + t;
                            if (ColorUtility.TryParseHtmlString(t, out _)) Set(role, f.Name, new JValue(t.ToUpperInvariant()));
                        });
                        break;
                    }
                case "num":
                    {
                        var row = Ui.Row(list, "Num_" + f.Name, 64, 10);
                        row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                        float now = cur.Num(f.Name, (f.Min + f.Max) / 2f);
                        void Go(float v)
                        {
                            v = Mathf.Clamp(Mathf.Round(v / f.Step) * f.Step, f.Min, f.Max);
                            Set(role, f.Name, new JValue(Math.Round(v, 3)));
                        }
                        Ui.Size(Ui.SecondaryButton(row, "기본", () => Set(role, f.Name, null), 64, 24), 64, 110);
                        Ui.Size(Ui.SecondaryButton(row, "−−", () => Go(now - f.Step * 5), 64, 26), 64, 90);
                        Ui.Size(Ui.SecondaryButton(row, "−", () => Go(now - f.Step), 64, 30), 64, 80);
                        Ui.Size(Ui.Label(row, raw == null ? "-" : now.ToString("0.##", CultureInfo.InvariantCulture), 30, TextAnchor.MiddleCenter), 64, 140);
                        Ui.Size(Ui.SecondaryButton(row, "+", () => Go(now + f.Step), 64, 30), 64, 80);
                        Ui.Size(Ui.SecondaryButton(row, "++", () => Go(now + f.Step * 5), 64, 26), 64, 90);
                        Ui.Size(Ui.Label(row, f.Min.ToString("0.##", CultureInfo.InvariantCulture) + " ~ " + f.Max.ToString("0.##", CultureInfo.InvariantCulture), 22, TextAnchor.MiddleLeft, Palette.TextDim), 64, 200);
                        break;
                    }
                case "choice":
                    {
                        var items = new List<(string, string)> { ("", "기본") };
                        foreach (var o in f.Options) items.Add((o, o));
                        Chips(list, items, raw == null ? "" : (string)raw, v => Set(role, f.Name, v == "" ? null : new JValue(v)), 4);
                        break;
                    }
                case "art":
                    {
                        var items = new List<(string, string)> { ("", "기본") };
                        foreach (var a in UiKit.Arts) if (a.Group == f.Group) items.Add((a.Id, a.Label));
                        Chips(list, items, raw == null ? "" : (string)raw, v =>
                        {
                            // 그림을 고르면 모양도 그림(scene)으로 — 모양이 단색이면 그림이 안 보여서 (2026-11-27 사용자)
                            var kind = UiKit.KindOf(role);
                            if (v != "" && f.Group == "scene" && kind != null && kind.Variants.Contains("scene") && cur.Variant != "scene") UiKit.SetDev(role, "variant", new JValue("scene"));
                            Set(role, f.Name, v == "" ? null : new JValue(v));
                        }, 3);
                        break;
                    }
            }
        }

        /// <summary>부품을 실제 크기로 그리고 경고 (글자 대비 · 누르기 칸 크기)를 돌려준다.</summary>
        private List<string> Preview(RectTransform list, string role)
        {
            var warn = new List<string>();
            var ctx = _app.Context;
            void Check(Button b, bool tall)
            {
                if (b == null) return;
                var img = b.targetGraphic as Image;
                var t = b.GetComponentInChildren<Text>();
                if (img != null && t != null && t.enabled && img.color.a > 0.5f && Mathf.Abs(UiArt.Luma(img.color) - UiArt.Luma(t.color)) < 0.3f) warn.Add("글자와 바탕 대비가 낮아 잘 안 읽힘");
                var le = b.GetComponent<LayoutElement>();
                if (tall && le != null && le.preferredHeight > 0 && le.preferredHeight < 88) warn.Add("누르기 칸이 작음 (높이 88 미만)");
            }
            RectTransform Line(float h)
            {
                var r = Ui.Row(list, "PreviewRow", h, 12);
                r.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                return r;
            }
            if (role.StartsWith("card.")) { CardPreview(list, role); return warn; }
            switch (role)
            {
                case "head.top":
                    {
                        var r = Line(110);
                        Ui.ResourceChip(ctx, r, Game.Economy.ResourceIds.Supply, 1100000, null);
                        Ui.ResourceChip(ctx, r, Game.Economy.ResourceIds.Gem, 300, null);
                        Ui.IconButton(r, "bell", "우편", null);
                        Ui.IconButton(r, "shop", "상점", null);
                        Ui.IconButton(r, "settings", "설정", null);
                        return warn;
                    }
                case "head.title":
                    {
                        var hdr = Ui.Rect(list, "TitlePreview");
                        Ui.Size(hdr, Ui.TitleHeight);
                        Ui.Column(hdr, 0, 0);
                        Ui.TitleBar(hdr, "꾸미기 · 보병", () => { });
                        Ui.HeaderRow(list, "부대 관리", "5종");
                        return warn;
                    }
                case "pop.confirm":
                case "pop.sheet":
                case "pop.toast":
                    {
                        // 화면 위에 뜨는 판을 이 자리에 그대로 한 번 (캡처용) + [실제로 띄우기]
                        var box = Ui.Rect(list, "PanelPreview");
                        Ui.Size(box, role == "pop.toast" ? 90 : 300);
                        var bi = Ui.Background(box, role == "pop.toast" ? new Color(0.16f, 0.13f, 0.1f, 0.88f) : Palette.Background);
                        bi.sprite = UiArt.Frame(20f); bi.type = Image.Type.Sliced;
                        UiKit.DressPanel(box, bi, null, role);
                        foreach (var ap in box.GetComponents<KitAppear>()) UnityEngine.Object.Destroy(ap);
                        Ui.Column(box, 12, 28);
                        if (role == "pop.toast") Ui.Label(box, "알림 한 줄 — 저장했습니다", 24, TextAnchor.MiddleCenter, Color.white);
                        else
                        {
                            Ui.Label(box, role == "pop.confirm" ? "저장하지 않고 나갈까요?" : "진형 고르기", 36, TextAnchor.MiddleCenter);
                            Ui.Label(box, "설명 한 줄", 24, TextAnchor.MiddleCenter, Palette.TextDim);
                            Ui.PrimaryButton(box, "확인", null, 90, 30);
                        }
                        Ui.SecondaryButton(list, "실제로 띄우기", () =>
                        {
                            if (role == "pop.confirm") Ui.Confirm(_root, "확인 창", "설명 한 줄", "확인", null, ("취소", null));
                            else if (role == "pop.sheet") { var (c, close) = Ui.BottomSheet(_root, "아래에서 올라오는 판", 420); Ui.Column(c, 10, 0); Ui.SecondaryButton(c, "닫기", close, 80, 26); }
                            else Toast.Show("알림 한 줄 — 저장했습니다");
                        }, 90, 28);
                        return warn;
                    }
                case "anim.loading":
                    {
                        var holder = Ui.Rect(list, "LoadingPreview");
                        Ui.Size(holder, 220);
                        LoadingView.BuildCard(holder);
                        return warn;
                    }
                case "anim.press":
                    {
                        var r = Line(130);
                        Ui.Size(Ui.PrimaryButton(r, "눌러 보기", null, 120), 120, 420);
                        Ui.Size(Ui.SecondaryButton(r, "보조 버튼", null, 110), 110, 300);
                        return warn;
                    }
            }
            switch (role)
            {
                case "bg.page":
                case "bg.header":
                case "bg.tabbar":
                    {
                        bool page = role == "bg.page";
                        var box = Ui.Rect(list, "BgBox");
                        Ui.Size(box, page ? 460 : 150);
                        var img = Ui.Background(box, page ? Palette.Background : Palette.Bar);
                        UiKit.Backdrop(box, role, img.color);
                        Ui.Column(box, 12, 24);
                        Ui.Label(box, page ? "화면 제목" : "위 · 아래 띠", page ? 56 : 34, TextAnchor.MiddleLeft);
                        if (page)
                        {
                            var card = Ui.Strip(Ui.Card(box, "SampleCard", 20, 6));
                            Ui.Label(card, "카드 — 바탕 위에서 읽히는지", 28);
                            Ui.Label(card, "흐린 글자 · 숫자 1,234", 24, TextAnchor.MiddleLeft, Palette.TextDim);
                            Ui.PrimaryButton(box, "주요 버튼", null, 110);
                        }
                        break;
                    }
                case "button.primary":
                    {
                        var r = Line(130);
                        var on = Ui.PrimaryButton(r, "출진 준비", null, 120);
                        Ui.Size(on, 120, 420);
                        var off = Ui.PrimaryButton(r, "꺼짐", null, 120);
                        Ui.Size(off, 120, 300);
                        Ui.SetPrimaryEnabled(off, false);
                        Check(on, true);
                        break;
                    }
                case "button.secondary":
                    {
                        var r = Line(130);
                        var a = Ui.SecondaryButton(r, "보조", null, 110);
                        Ui.Size(a, 110, 300);
                        Ui.Size(Ui.SecondaryButton(r, "모집 · 도감", null, 110), 110, 420);
                        Check(a, true);
                        break;
                    }
                case "button.small":
                case "button.small.compact":
                    {
                        var r = Line(70);
                        var a = Ui.Styled(Ui.SecondaryButton(r, "−", null, 56, 30), role);
                        Ui.Size(a, 56, 56);
                        Ui.Size(Ui.Styled(Ui.SecondaryButton(r, "+", null, 56, 30), role), 56, 56);
                        Ui.Size(Ui.Styled(Ui.SecondaryButton(r, "최대", null, 56, 22), role), 56, 100);
                        Check(a, false);
                        break;
                    }
                case "button.icon":
                    {
                        var r = Line(90);
                        Check(Ui.IconButton(r, "bell", "우편", null), false);
                        Ui.IconButton(r, "shop", "상점", null);
                        Ui.IconButton(r, "settings", "설정", null);
                        break;
                    }
                case "button.back":
                    {
                        var hdr = Ui.Rect(list, "TitlePreview");
                        Ui.Size(hdr, Ui.TitleHeight);
                        Ui.Column(hdr, 0, 0);
                        Ui.TitleBar(hdr, "꾸미기 · 보병", () => { });
                        break;
                    }
                case "button.sortie":
                    Check(Ui.SortieButton(list, "출진", null, 130), true);
                    break;
                case "tabs.art":
                    Ui.MajorTabs(list, new List<(string, string, string)> { ("general", "장수", "fig.general"), ("troop", "병력", "fig.troop5"), ("formation", "편성", "fig.command") }, "general", null);
                    Ui.MajorTabs(list, new List<(string, string, string)> { ("map", "지도", "fig.globe"), ("flags", "내 깃발", "fig.flags") }, "map", null);
                    break;
                case "tabs.segment":
                case "tabs.segment.on":
                    Ui.MidTabs(list, new List<(string, string)> { ("a", "기본"), ("b", "야전"), ("c", "성새전"), ("d", "수상전") }, "a", null);
                    break;
                case "tabs.minor":
                    Ui.MinorTabs(list, new List<(string, string)> { ("a", "승률순"), ("b", "레벨순"), ("c", "싼 순") }, "a", null);
                    Ui.MinorTabs(list, new List<(string, string)> { ("a", "장수+부대"), ("b", "장수만"), ("c", "부대만") }, "b", null);
                    break;
                case "tabs.bottom":
                    {
                        var spec = UiKit.Spec("tabs.bottom");
                        float h = spec.Num("height", 104);
                        var barBg = Ui.Rect(list, "TabBarPreview");
                        Ui.Size(barBg, h);
                        Ui.Background(barBg, Palette.Bar);
                        UiKit.Backdrop(barBg, "bg.tabbar", Palette.Bar);
                        var bar = Ui.HScrollRow(barBg, "Tabs", h, 6, 8);
                        Ui.Fill((RectTransform)bar.parent.parent);
                        var items = new List<(string, string)>();
                        foreach (var t in ctx.MainTabs)
                            if (t.Status != MainTabEntry.Hidden && t.Row != MainTabEntry.RowDev && t.Row != MainTabEntry.RowTop) items.Add((t.Id, ctx.T(t.NameKey)));
                        UiKit.TabStrip(bar, items, items.Count > 1 ? items[1].Item1 : null, null, "tabs.bottom", "button", h - 16, (GameApp.ReferenceWidth - 16f) / Mathf.Min(6, Mathf.Max(1, items.Count)) - 6f, 6,
                            id => UiKit.TabIcons.TryGetValue(id, out var ic) ? ic : null);
                        break;
                    }
            }
            return warn;
        }

        /// <summary>카드 미리보기: 고른 밀도를 크게, 아래에 같은 내용의 다른 밀도 셋 (ui-kit.md 4.4).</summary>
        private void CardPreview(RectTransform list, string role)
        {
            var parts = role.Split('.');
            string content = parts.Length > 1 ? parts[1] : "troop";
            Cards.Density DensityOf(string s) => s == "mini" ? Cards.Density.Mini : s == "compact" ? Cards.Density.Compact : s == "standard" ? Cards.Density.Standard : Cards.Density.Detail;
            var chosen = DensityOf(parts.Length > 2 ? parts[2] : "standard");
            OneCard(list, content, chosen);
            Ui.SectionHeader(list, "같은 내용 · 다른 밀도");
            foreach (var d in new[] { Cards.Density.Mini, Cards.Density.Compact, Cards.Density.Standard, Cards.Density.Detail })
            {
                if (d == chosen) continue;
                Ui.Hint(list, Cards.Role(content, d), 36);
                OneCard(list, content, d);
            }
        }

        private void OneCard(RectTransform list, string content, Cards.Density d)
        {
            var ctx = _app.Context;
            Transform host = list;
            if (d == Cards.Density.Mini)
            {
                var r = Ui.Row(list, "MiniRow", 190, 12);
                r.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                host = r;
            }
            else if ((content == "general" && d == Cards.Density.Standard) || (content == "item" && d == Cards.Density.Standard))
            {
                var grid = Ui.Rect(list, "Grid");
                var g = grid.gameObject.AddComponent<GridLayoutGroup>();
                g.cellSize = content == "general" ? new Vector2(505, 470) : new Vector2(320, 380);
                g.spacing = new Vector2(20, 20);
                g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                g.constraintCount = 2;
                Ui.Size(grid, content == "general" ? 470 : 380);
                host = grid;
            }
            switch (content)
            {
                case "general":
                    {
                        if (ctx.State.Generals.Count == 0) { Ui.Hint(list, "장수 없음", 40); return; }
                        Cards.General(ctx, host, Cards.InfoOf(ctx, ctx.State.Generals[0]), d);
                        break;
                    }
                case "troop":
                    {
                        if (ctx.State.Units.Count == 0) { Ui.Hint(list, "병력 없음", 40); return; }
                        var u = ctx.State.Units[0];
                        Cards.Troop(ctx, host, Cards.InfoOf(ctx, u, u.Troops + 3000), d);
                        break;
                    }
                case "corps":
                    {
                        var comp = ctx.State.Compositions.Find(c => c.Picks.Count > 0);
                        if (comp == null && ctx.State.Generals.Count > 0)
                        {
                            comp = new Game.Campaign.Roster.CorpsComposition { GeneralId = ctx.State.Generals[0].GeneralId, Formation = "square", Commander = true };
                            for (int i = 0; i < ctx.State.Units.Count && i < 3; i++) comp.Picks.Add(new Game.Campaign.Roster.TroopPick { UnitId = ctx.State.Units[i].UnitId, Troops = 1000 * (i + 1) });
                        }
                        if (comp == null) { Ui.Hint(list, "군단 없음", 40); return; }
                        Cards.Corps(ctx, host, Cards.InfoOf(ctx, comp), d);
                        break;
                    }
                default:
                    Cards.Item(ctx, host, new Cards.ItemInfo { TroopType = "infantry", Name = "갈색 가죽 갑옷", Sub = "갑옷", Price = "엽전 500", State = d == Cards.Density.Mini || d == Cards.Density.Compact ? null : "입음" }, d);
                    break;
            }
        }

        // ---------------- 테마 ----------------

        private void ThemePanel(RectTransform list)
        {
            Ui.Hint(list, "테마 색: 모든 부품이 이 색 이름을 씁니다. hex를 넣고 Enter, 또는 밝게 · 어둡게. 강조색이 맵(초록 · 갈색) · 팀(파랑 · 빨강)과 겹치면 경고.", 80);
            Ui.Label(list, "지금 스타일: " + UiStyle.Current, 24, TextAnchor.MiddleLeft, Palette.TextDim);
            foreach (var n in UiKit.ThemeColors)
            {
                if (!UiKit.Editable(n)) continue;
                string name = n;
                var c = UiKit.ThemeColor(n, Color.magenta);
                var row = Ui.Row(list, "Theme_" + n, 72, 10);
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                var sw = Ui.Rect(row, "Swatch");
                Ui.Size(sw, 64, 64).flexibleWidth = 0;
                var swi = Ui.Background(sw, c);
                swi.sprite = UiShapes.Rounded(10); swi.type = Image.Type.Sliced;
                Ui.Size(Ui.Label(row, n + (UiKit.HasDevColor(n) ? " ●" : ""), 26, TextAnchor.MiddleLeft), 64, 240);
                var input = Ui.TextInput(row, "#RRGGBB", 9, 60, 24);
                Ui.Size(input, 60, 200);
                input.text = "#" + (c.a < 0.999f ? ColorUtility.ToHtmlStringRGBA(c) : ColorUtility.ToHtmlStringRGB(c));
                input.onEndEdit.AddListener(t =>
                {
                    t = t.Trim();
                    if (!t.StartsWith("#")) t = "#" + t;
                    if (ColorUtility.TryParseHtmlString(t, out var nc)) { UiKit.SetDevColor(name, nc); Redraw(); }
                });
                void Shift(float dv)
                {
                    Color.RGBToHSV(c, out var hh, out var ss, out var vv);
                    var nc = Color.HSVToRGB(hh, ss, Mathf.Clamp01(vv + dv));
                    nc.a = c.a;
                    UiKit.SetDevColor(name, nc);
                    Redraw();
                }
                Ui.Size(Ui.SecondaryButton(row, "밝게", () => Shift(0.06f), 60, 22), 60, 90);
                Ui.Size(Ui.SecondaryButton(row, "어둡게", () => Shift(-0.06f), 60, 22), 60, 100);
                if (UiKit.HasDevColor(n)) Ui.Size(Ui.SecondaryButton(row, "기본", () => { UiKit.ResetDevColor(name); Redraw(); }, 60, 22), 60, 90);
                if ((n == "buttonActive" || n == "accentLight") && Clashes(c, out var what)) Ui.ErrorLine(list, "⚠ " + n + ": " + what + " 색과 겹침", 40);
            }
        }

        /// <summary>강조색이 맵 · 팀 색 계열과 겹치나 (2026-11-26 사용자: 강조는 맵 · 팀 · 갑옷 · 말 색과 겹치지 않게).</summary>
        private static bool Clashes(Color c, out string what)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            float deg = h * 360f;
            what = null;
            if (s < 0.2f || v < 0.15f) { what = "회색 · 검정 (맵 · 갑옷)"; return true; }
            if (deg < 15f || deg >= 345f) what = "빨강 (적 팀)";
            else if (deg < 45f) what = "갈색 · 주황 (맵 · 말 · 갑옷)";
            else if (deg >= 80f && deg < 160f) what = "초록 (맵)";
            else if (deg >= 180f && deg < 240f) what = "파랑 (우리 팀 · 물)";
            return what != null;
        }

        // ---------------- 아래 버튼 ----------------

        private void Actions(RectTransform list)
        {
            Ui.SectionHeader(list, "저장 · 내보내기 (바뀐 값 " + UiKit.DevCount + "개)");
            var row = Ui.Row(list, "Actions", 90, 10);
            if (Category != "theme" && RoleId != null)
                Ui.SecondaryButton(row, "이 부품 되돌리기", () => { UiKit.ResetDev(RoleId); Redraw(); }, 90, 26);
            Ui.SecondaryButton(row, "모두 되돌리기", () => Ui.Confirm(_root, "모두 되돌리기", "바뀐 값 " + UiKit.DevCount + "개를 지우고 데이터 값으로 돌아갑니다.", "지우기",
                () => { UiKit.ResetAll(); Redraw(); }, ("취소", null)), 90, 26);
            var ex = Ui.PrimaryButton(row, "데이터로 내보내기", () =>
            {
                bool file = UiKit.Export();
                Toast.Show(file ? "styles.json에 썼습니다 — \"PR·머지\"하면 반영" : "바뀐 값을 클립보드에 복사했습니다 (로그에도)");
                Redraw();
            }, 90, 28);
            Ui.SetPrimaryEnabled(ex, UiKit.DevCount > 0);
            Ui.Hint(list, "에디터: data/Ui/styles.json에 씁니다. 폰: 클립보드로 복사 → 데스크탑에 붙여 넣기.", 60);
        }

        // ---------------- 칩 ----------------

        private static void Chips(RectTransform list, List<(string id, string label)> items, string selected, Action<string> onPick, int perRow)
        {
            RectTransform row = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (i % perRow == 0) row = Ui.Row(list, "Chips", 60, 8);
                var it = items[i];
                bool on = it.id == selected;
                var b = Ui.Button(row, it.label, () => onPick(it.id), 60, on ? Palette.ButtonActive : Palette.Button, 22);
                if (on) b.GetComponentInChildren<Text>().color = Ui.OnAccent;
            }
            if (row != null)
                for (int k = items.Count % perRow; k > 0 && k < perRow; k++) Ui.Size(Ui.Rect(row, "Pad"), 60); // 마지막 줄 칸 너비 맞춤
        }

        private static void Chip(RectTransform row, string label, Color color, bool on, Action onPick, float width)
        {
            var b = Ui.Button(row, label, onPick, 60, color, 18);
            Ui.Size(b, 60, width).flexibleWidth = 0;
            var t = b.GetComponentInChildren<Text>();
            t.color = Palette.TextOn(color);
            if (on)
            {
                var ring = Ui.Fill(Ui.Rect(b.transform, "On"));
                var ri = ring.gameObject.AddComponent<Image>();
                ri.sprite = UiKit.Ring(10, 4); ri.type = Image.Type.Sliced; ri.color = Palette.AccentLight; ri.raycastTarget = false;
            }
        }
    }
}
