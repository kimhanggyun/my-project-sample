using System;
using System.Collections.Generic;
using Game.Unity.Boot;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 디자인 시안의 공통 부품 (docs/design/mockups/공통부품.md). 화면은 이것을 놓기만 하고, 모양을 바꿀 때는 여기만 고친다.
    /// </summary>
    public static partial class Ui
    {
        /// <summary>주홍·짙은 판 위의 밝은 글자.</summary>
        public static readonly Color OnAccent = new Color(0.98f, 0.95f, 0.90f);

        // ---------------- 버튼 ----------------

        /// <summary>주요 버튼: 주홍 바탕 + 밝은 굵은 글자 (시작·동의하고 시작·출진).</summary>
        public static Button PrimaryButton(Transform parent, string text, Action onClick, float height = 150, int fontSize = FontBody + 4)
        {
            var b = Button(parent, text, onClick, height, Palette.ButtonActive, fontSize);
            var t = b.GetComponentInChildren<Text>();
            t.color = OnAccent;
            t.fontStyle = FontStyle.Bold;
            return b;
        }

        /// <summary>주요 버튼 켜기·끄기 (꺼짐 = 회색 바탕 + 흐린 글자).</summary>
        public static void SetPrimaryEnabled(Button b, bool on)
        {
            b.interactable = on;
            if (b.targetGraphic != null) b.targetGraphic.color = on ? Palette.ButtonActive : Palette.ButtonDisabled;
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = on ? OnAccent : Palette.TextDim;
        }

        /// <summary>보조 버튼: 한지색 바탕 + 먹 글자 (게스트로 시작·닫기·모집).</summary>
        public static Button SecondaryButton(Transform parent, string text, Action onClick, float height = ButtonHeight, int fontSize = FontBody) =>
            Button(parent, text, onClick, height, Palette.Button, fontSize);

        /// <summary>아이콘 버튼 (상점 가방·설정 톱니). 그림 `Resources/Ui/icons/&lt;iconId&gt;`이 없으면 글자.</summary>
        public static Button IconButton(Transform parent, string iconId, string fallback, Action onClick, float width = 80, float height = 74)
        {
            var b = Button(parent, "", onClick, height, Palette.Button, 24);
            Size(b, height, width).flexibleWidth = 0;
            var pic = Resources.Load<Sprite>("Ui/icons/" + iconId);
            var label = b.GetComponentInChildren<Text>();
            if (pic == null) { label.text = fallback; return b; }
            label.gameObject.SetActive(false);
            var icon = Rect(b.transform, "Icon");
            icon.anchorMin = new Vector2(0.5f, 0.5f);
            icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(height - 18, height - 18);
            var img = Background(icon, Color.white);
            img.sprite = pic;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return b;
        }

        // ---------------- 글자 ----------------

        /// <summary>안내 문구: 작은 회색 가운데 (관련 버튼 바로 위·아래).</summary>
        public static Text Hint(Transform parent, string text, float height = 70) =>
            SizedLabel(parent, text, FontSmall, TextAnchor.MiddleCenter, Palette.TextDim, height);

        /// <summary>실패 문구: 작은 빨강 가운데 한 줄 (평소에는 빈 글자로 자리만).</summary>
        public static Text ErrorLine(Transform parent, string text, float height = 70) =>
            SizedLabel(parent, text ?? "", FontSmall, TextAnchor.MiddleCenter, Palette.Bad, height);

        /// <summary>구역 머리말: 굵은 회색 ("깬 스텝", "다음 스텝").</summary>
        public static Text SectionHeader(Transform parent, string text)
        {
            var t = SizedLabel(parent, text, 28, TextAnchor.LowerLeft, Palette.TextDim, 56);
            t.fontStyle = FontStyle.Bold;
            return t;
        }

        /// <summary>링크 글자 "보기 ›": 작은 회색, 오른쪽 정렬, 누를 수 있다.</summary>
        public static Text Link(Transform parent, string text, Action onClick, float width = 170, int fontSize = 28)
        {
            var t = Label(parent, text + " ›", fontSize, TextAnchor.MiddleRight, Palette.TextDim);
            Size(t, -1, width).flexibleWidth = 0;
            if (onClick != null)
            {
                t.raycastTarget = true;
                var b = t.gameObject.AddComponent<Button>();
                b.targetGraphic = t;
                b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onClick(); });
            }
            return t;
        }

        private static Text SizedLabel(Transform parent, string text, int size, TextAnchor align, Color color, float height)
        {
            var t = Label(parent, text, size, align, color);
            Size(t, height);
            return t;
        }

        /// <summary>남는 세로 공간을 차지하는 빈 칸 (세로 묶음 안에서).</summary>
        public static RectTransform Spacer(Transform parent, float height = 0, float flexible = 1)
        {
            var rt = Rect(parent, "Spacer");
            var le = Size(rt, height);
            if (flexible > 0) le.flexibleHeight = flexible;
            return rt;
        }

        /// <summary>좌우 여백을 둔 세로 묶음 (버튼을 화면 가장자리에서 띄울 때).</summary>
        public static RectTransform Inset(Transform parent, string name, int side, int spacing = 24)
        {
            var rt = Rect(parent, name);
            var g = Column(rt, spacing, 0);
            g.padding = new RectOffset(side, side, 0, 0);
            return rt;
        }

        // ---------------- 체크 상자 ----------------

        /// <summary>
        /// 체크 상자 줄: 상자 + 글자 (+ 오른쪽 링크). 줄 전체가 누르는 칸. 켜짐 = 주홍 바탕·흰 체크, 꺼짐 = 한지 바탕·먹 테두리.
        /// </summary>
        public static Button CheckRow(Transform parent, string text, bool on, Action onToggle, bool big = false, string link = null, Action onLink = null)
        {
            var row = Rect(parent, "CheckRow");
            Size(row, big ? 110 : 100);
            var hit = Background(row, new Color(0, 0, 0, 0));
            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            if (onToggle != null) btn.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onToggle(); });
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 22;
            h.padding = new RectOffset(8, 8, 0, 0);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            float box = big ? 60 : 48;
            var boxRt = Rect(row, "Box");
            Size(boxRt, box, box).flexibleWidth = 0;
            var boxImg = Background(boxRt, on ? Palette.ButtonActive : Palette.Card);
            boxImg.sprite = UiArt.Frame(8f);
            boxImg.type = Image.Type.Sliced;
            boxImg.raycastTarget = false;
            if (on)
            {
                var check = Label(boxRt, "✓", Mathf.RoundToInt(box * 0.8f), TextAnchor.MiddleCenter, OnAccent);
                check.fontStyle = FontStyle.Bold;
                Fill(check.rectTransform);
            }

            var label = Label(row, text, big ? 44 : 34, TextAnchor.MiddleLeft);
            if (big) label.fontStyle = FontStyle.Bold;
            Size(label, box, -1, -1, 1);
            if (link != null) Link(row, link, onLink);
            return btn;
        }

        /// <summary>옅은 가로 구분선.</summary>
        public static RectTransform Divider(Transform parent, float thickness = 2)
        {
            var rt = Rect(parent, "Divider");
            Size(rt, thickness);
            Background(rt, new Color(Palette.TextDim.r, Palette.TextDim.g, Palette.TextDim.b, 0.3f)).raycastTarget = false;
            return rt;
        }

        // ---------------- 첫 화면 머리 ----------------

        /// <summary>
        /// 첫 화면 머리 (타이틀·로그인): 위 대표 그림 자리(`Resources/Ui/title_art`, 없으면 비움) + 붓 획 위 제목.
        /// </summary>
        public static RectTransform TitleHeader(Transform parent, GameContext ctx, float artHeight = 700)
        {
            var head = Rect(parent, "TitleHeader");
            Column(head, 20, 0);
            var art = Rect(head, "Art");
            Size(art, Mathf.Max(0f, artHeight));
            var pic = Resources.Load<Sprite>("Ui/title_art");
            if (pic != null)
            {
                var img = Background(art, Color.white);
                img.sprite = pic;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            var titleBox = Rect(head, "TitleBox");
            Size(titleBox, 230);
            var stroke = Rect(titleBox, "Brush");
            stroke.anchorMin = new Vector2(0.06f, 0.08f);
            stroke.anchorMax = new Vector2(0.94f, 0.92f);
            stroke.offsetMin = stroke.offsetMax = Vector2.zero;
            var strokeImg = Background(stroke, Palette.IsLight ? new Color(0.17f, 0.15f, 0.12f, 0.92f) : new Color(0.9f, 0.86f, 0.78f, 0.12f));
            strokeImg.sprite = UiArt.Brush;
            strokeImg.raycastTarget = false;
            var title = Label(titleBox, ctx.T("ui.title.name"), FontTitle + 8, TextAnchor.MiddleCenter, Palette.IsLight ? new Color(0.97f, 0.94f, 0.87f) : Palette.Text);
            Fill(title.rectTransform);
            return head;
        }

        // ---------------- 메인 틀 ----------------

        /// <summary>자원 칩: 둥근 칩 안에 아이콘 + 숫자 + 작은 주홍 원 [+].</summary>
        public static RectTransform ResourceChip(GameContext ctx, Transform parent, string id, long value, Action onPlus, float height = 66)
        {
            var chip = Rect(parent, "Chip_" + id);
            var le = Size(chip, height);
            le.flexibleWidth = 1;
            var bg = Background(chip, Palette.Card);
            bg.sprite = UiShapes.Rounded(Mathf.RoundToInt(height / 2));
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            var h = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.padding = new RectOffset(8, 10, 7, 7);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            Icons.Make(ctx, chip, id, height - 14);
            var num = Label(chip, value.ToString("N0"), 28, TextAnchor.MiddleLeft);
            num.horizontalOverflow = HorizontalWrapMode.Overflow;
            Size(num, height - 14, -1, -1, 1);
            var plus = Rect(chip, "Plus");
            Size(plus, 44, 44).flexibleWidth = 0;
            var plusImg = Background(plus, Palette.ButtonActive);
            plusImg.sprite = UiShapes.Circle;
            var plusText = Label(plus, "+", 34, TextAnchor.MiddleCenter, OnAccent);
            plusText.fontStyle = FontStyle.Bold;
            Fill(plusText.rectTransform);
            var b = plus.gameObject.AddComponent<Button>();
            b.targetGraphic = plusImg;
            if (onPlus != null) b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onPlus(); });
            return chip;
        }

        /// <summary>띠 배너 광고 자리 (자원 바 아래). 광고가 꺼졌거나 광고 제거 상품이면 높이 0.</summary>
        public static RectTransform BannerSlot(Transform parent, GameContext ctx)
        {
            var rt = Rect(parent, "BannerSlot");
            float h = ctx.BannerAd != null ? ctx.BannerAd.HeightPixels : 0f;
            if (Game.Flow.PurchaseService.AdFree(ctx.State, ctx.Clock.UtcNow.ToUnixTimeSeconds())) h = 0f;
            var le = Size(rt);
            le.minHeight = le.preferredHeight = h;
            le.flexibleHeight = 0;
            if (h <= 0f) rt.gameObject.SetActive(false);
            return rt;
        }

        // ---------------- 카드·줄 ----------------

        /// <summary>
        /// 강조 카드: 색 테두리(5px) + 왼쪽 위 색 꼬리표 + 내용 세로 묶음 (돌려주는 것에 내용을 넣는다). 다음 도전 = 주홍, 구출 = 빨강.
        /// </summary>
        public static RectTransform FeatureCard(Transform parent, string tag, Color accent, int padding = 34, int spacing = 14)
        {
            var card = Rect(parent, "FeatureCard");
            var border = Background(card, accent);
            border.sprite = UiShapes.Rounded(18);
            border.type = Image.Type.Sliced;
            var inner = Rect(card, "Inner");
            inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Fill(inner);
            inner.offsetMin = new Vector2(5, 5);
            inner.offsetMax = new Vector2(-5, -5);
            var innerImg = Background(inner, Palette.Card);
            innerImg.sprite = UiShapes.Rounded(14);
            innerImg.type = Image.Type.Sliced;
            innerImg.raycastTarget = false;
            Column(card, spacing, padding);
            if (!string.IsNullOrEmpty(tag)) Tag(card, tag, accent);
            return card;
        }

        /// <summary>색 바탕 + 흰 글자 꼬리표 (글 길이만큼). 세로 묶음 안이면 왼쪽에 붙는다.</summary>
        public static RectTransform Tag(Transform parent, string text, Color color, int fontSize = 26, float height = 48)
        {
            var line = Rect(parent, "TagLine");
            Size(line, height);
            var lh = line.gameObject.AddComponent<HorizontalLayoutGroup>();
            lh.childControlWidth = lh.childControlHeight = true;
            lh.childForceExpandWidth = false;
            lh.childForceExpandHeight = true;
            lh.childAlignment = TextAnchor.MiddleLeft;
            var tag = Rect(line, "Tag");
            var img = Background(tag, color);
            img.sprite = UiShapes.Rounded(10);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            var th = tag.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.padding = new RectOffset(18, 18, 4, 4);
            th.childControlWidth = th.childControlHeight = true;
            th.childForceExpandWidth = false; // 펼치면 꼬리표가 줄 전체로 늘어난다
            var t = Label(tag, text, fontSize, TextAnchor.MiddleCenter, OnAccent);
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return line;
        }

        /// <summary>
        /// 목록 줄: 왼쪽 상태 기호("@아이콘id"이면 그림, 기호 색으로 물듦) + 이름 + 오른쪽 링크·안내. dim이면 흐린 바탕·흐린 글자 (잠김).
        /// </summary>
        public static Button ListRow(Transform parent, string mark, Color markColor, string name, string right, Action onClick, bool dim = false, float height = 90)
        {
            var row = Rect(parent, "ListRow");
            Size(row, height);
            var bg = Background(row, dim ? new Color(Palette.Card.r, Palette.Card.g, Palette.Card.b, 0.45f) : Palette.Card);
            Round(bg);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16;
            h.padding = new RectOffset(26, 26, 0, 0);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var pic = mark != null && mark.StartsWith("@") ? Resources.Load<Sprite>("Ui/icons/" + mark.Substring(1)) : null;
            if (pic != null)
            {
                var holder = Rect(row, "Mark");
                Size(holder, height, 44).flexibleWidth = 0;
                var icon = Rect(holder, "Icon");
                icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.sizeDelta = new Vector2(38, 38);
                var img = Background(icon, markColor);
                img.sprite = pic;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                var m = Label(row, mark != null && mark.StartsWith("@") ? "-" : mark, 34, TextAnchor.MiddleCenter, markColor);
                m.fontStyle = FontStyle.Bold;
                Size(m, height, 44).flexibleWidth = 0;
            }
            var n = Label(row, name, 34, TextAnchor.MiddleLeft, dim ? Palette.TextDim : Palette.Text);
            Size(n, height, -1, -1, 1);
            if (!string.IsNullOrEmpty(right))
            {
                var r = Label(row, right, dim ? 24 : 28, TextAnchor.MiddleRight, Palette.TextDim);
                r.horizontalOverflow = HorizontalWrapMode.Overflow;
                Size(r, height, 300).flexibleWidth = 0;
            }
            var b = row.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            if (onClick != null) b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onClick(); });
            else b.interactable = false;
            return b;
        }

        /// <summary>머리 줄: 큰 제목 + 회색 부제 (왼쪽) + 오른쪽 작은 버튼 자리 (돌려주는 것에 버튼을 넣는다).</summary>
        public static RectTransform HeaderRow(Transform parent, string title, string sub, float height = 100)
        {
            var row = Rect(parent, "HeaderRow");
            Size(row, height);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var t = Label(row, title, FontTitle, TextAnchor.MiddleLeft);
            t.font = TitleFont;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Size(t, height).flexibleWidth = 0;
            if (!string.IsNullOrEmpty(sub))
            {
                var s = Label(row, sub, FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
                s.horizontalOverflow = HorizontalWrapMode.Overflow;
                Size(s, height).flexibleWidth = 0;
            }
            var gap = Rect(row, "Gap");
            Size(gap, height, -1, -1, 1);
            return row;
        }

        // ---------------- 작은 ⓘ · 말풍선 · 고르는 메뉴 ----------------

        /// <summary>왼쪽 위 기준으로 자리·크기를 정한다 (레이아웃 밖에서 그리는 카드 안).</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>작은 ⓘ (지름 46, 한지 바탕·회색 테두리·회색 "i"). 페이지 도움말 ⓘ(주홍 원 66)와 구별. 카드 누름과 따로 눌린다.</summary>
        public static Button SmallInfo(Transform parent, Action<RectTransform> onClick, float size = 46)
        {
            var rt = Rect(parent, "SmallInfo");
            Size(rt, size, size).flexibleWidth = 0;
            var ring = Background(rt, Palette.TextDim);
            ring.sprite = UiShapes.Circle;
            var face = Rect(rt, "Face");
            Fill(face);
            face.offsetMin = new Vector2(2.5f, 2.5f);
            face.offsetMax = new Vector2(-2.5f, -2.5f);
            var faceImg = Background(face, Palette.Card);
            faceImg.sprite = UiShapes.Circle;
            faceImg.raycastTarget = false;
            var t = Label(rt, "i", Mathf.RoundToInt(size * 0.62f), TextAnchor.MiddleCenter, Palette.TextDim);
            t.font = TitleFont;
            t.fontStyle = FontStyle.Bold;
            Fill(t.rectTransform);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = ring;
            if (onClick != null) b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onClick(rt); });
            return b;
        }

        /// <summary>
        /// 화면 위에 뜨는 판 하나를 연다: 화면 전체를 덮는 투명 막(누르면 닫힘) 위, 기준 칸 바로 아래(공간 없으면 위)에 판.
        /// 돌려주는 판에 내용을 넣는다. overlayRoot = 화면(탭) 전체 영역.
        /// </summary>
        public static RectTransform Popover(RectTransform overlayRoot, RectTransform anchor, float width, float height, Color back, bool tail = true)
        {
            var blocker = Rect(overlayRoot, "Popover");
            blocker.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Fill(blocker);
            var hit = Background(blocker, new Color(0, 0, 0, 0));
            var close = blocker.gameObject.AddComponent<Button>();
            close.targetGraphic = hit;
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(() => UnityEngine.Object.Destroy(blocker.gameObject));
            blocker.SetAsLastSibling();

            var r = blocker.rect;
            Vector2 below = blocker.InverseTransformPoint(anchor.TransformPoint(new Vector3(anchor.rect.center.x, anchor.rect.yMin)));
            Vector2 above = blocker.InverseTransformPoint(anchor.TransformPoint(new Vector3(anchor.rect.center.x, anchor.rect.yMax)));
            const float gap = 18f, margin = 16f;
            bool down = below.y - gap - height >= r.yMin + margin;
            float x = Mathf.Clamp(below.x, r.xMin + margin + width / 2, r.xMax - margin - width / 2);

            var panel = Rect(blocker, "Panel");
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, down ? 1f : 0f);
            panel.sizeDelta = new Vector2(width, height);
            // blocker 기준점은 가운데: 로컬 좌표 = anchoredPosition (기준점이 rect 가운데일 때)
            Vector2 centerOffset = r.center;
            panel.anchoredPosition = new Vector2(x, down ? below.y - gap : above.y + gap) - centerOffset;
            var img = Background(panel, back);
            img.sprite = UiShapes.Rounded(16);
            img.type = Image.Type.Sliced;
            if (tail)
            {
                var t = Rect(blocker, "Tail");
                t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f);
                t.sizeDelta = new Vector2(26, 26);
                t.localRotation = Quaternion.Euler(0, 0, 45);
                t.anchoredPosition = new Vector2(below.x, down ? below.y - gap : above.y + gap) - centerOffset;
                Background(t, back).raycastTarget = false;
                t.SetSiblingIndex(panel.GetSiblingIndex());
            }
            return panel;
        }

        /// <summary>능력치 말풍선: 짙은 판에 줄마다 아이콘 + 이름 + 숫자(굵게). 바깥 누름·다시 누름이면 닫힘.</summary>
        public static RectTransform StatTip(GameContext ctx, RectTransform overlayRoot, RectTransform anchor, params (string iconId, string name, string value)[] lines)
        {
            const float lineH = 64f, pad = 22f;
            var panel = Popover(overlayRoot, anchor, 620, lines.Length * lineH + pad * 2, new Color(0.23f, 0.2f, 0.16f, 0.96f));
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(30, 30, (int)pad, (int)pad);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandHeight = false;
            foreach (var (iconId, name, value) in lines)
            {
                var row = Row(panel, "Line", lineH, 18);
                var h = row.GetComponent<HorizontalLayoutGroup>();
                h.childForceExpandWidth = false;
                h.childAlignment = TextAnchor.MiddleLeft;
                if (!string.IsNullOrEmpty(iconId)) Icons.Make(ctx, row, iconId, 46);
                var n = Label(row, name, 32, TextAnchor.MiddleLeft, Palette.PromptText);
                Size(n, lineH, -1, -1, 1);
                var val = Label(row, value, 34, TextAnchor.MiddleRight, Palette.PromptText);
                val.fontStyle = FontStyle.Bold;
                Size(val, lineH, 120).flexibleWidth = 0;
            }
            return panel;
        }

        /// <summary>
        /// 고르는 작은 메뉴: 기준 버튼 아래(또는 위)에 줄 목록. 줄마다 글자 (+ 오른쪽 작은 글자), 못 고르는 줄은 흐림. 고르면 닫고 실행.
        /// </summary>
        public static RectTransform PickMenu(RectTransform overlayRoot, RectTransform anchor, float width, params (string label, string right, bool enabled, Action pick)[] items)
        {
            const float lineH = 90f;
            var panel = Popover(overlayRoot, anchor, width, items.Length * lineH + 16, Palette.Card, tail: false);
            var frame = panel.GetComponent<Image>();
            frame.sprite = UiArt.Frame(14f);
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(8, 8, 8, 8);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandHeight = false;
            var blocker = (RectTransform)panel.parent;
            foreach (var it in items)
            {
                var item = it;
                var b = Button(panel, "", () => { UnityEngine.Object.Destroy(blocker.gameObject); item.pick?.Invoke(); }, lineH, Palette.Card, 32);
                var label = b.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.text = item.label;
                label.rectTransform.offsetMin = new Vector2(24, 4);
                if (!string.IsNullOrEmpty(item.right))
                {
                    var r = Label(b.transform, item.right, 26, TextAnchor.MiddleRight, Palette.TextDim);
                    Fill(r.rectTransform);
                    r.rectTransform.offsetMax = new Vector2(-24, 0);
                }
                b.interactable = item.enabled;
                if (!item.enabled) label.color = Palette.TextDim;
            }
            return panel;
        }

        // ---------------- 장수 카드 ----------------

        /// <summary>장수 카드에 그릴 값 (화면이 규칙에서 받아 채운다).</summary>
        public struct GeneralCardData
        {
            public string Id, Name, Grade, GradeName;
            public int Level;
            /// <summary>상태 띠 글자 (출전 가능이면 null), 띠 색.</summary>
            public string StatusText;
            public Color StatusColor;
            public long Leadership, Might, Intellect;
            public bool Attention;
        }

        /// <summary>
        /// 장수 카드 (시안 05): 테두리 = 등급 색 6px, 위 초상 자리(그림 없으면 원+글자) + 왼쪽 위 등급 꼬리표 + 아래 상태 띠 + 오른쪽 위 할 일 점,
        /// 이름·Lv, 능력치 아이콘 줄 + 작은 ⓘ. 크기는 부모(격자 칸)를 따른다 — 기준 505×470.
        /// </summary>
        public static RectTransform GeneralCard(GameContext ctx, Transform parent, GeneralCardData d, Action onOpen, Action<RectTransform> onInfo)
        {
            const float W = 505f, B = 6f;
            var card = Rect(parent, "General_" + d.Id);
            var gradeColor = GradeLabel.ColorOf(d.Grade);
            var border = Background(card, gradeColor);
            border.sprite = UiShapes.Rounded(18);
            border.type = Image.Type.Sliced;
            var open = card.gameObject.AddComponent<Button>();
            open.targetGraphic = border;
            if (onOpen != null) open.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onOpen(); });
            var inner = Rect(card, "Inner");
            Fill(inner);
            inner.offsetMin = new Vector2(B, B);
            inner.offsetMax = new Vector2(-B, -B);
            var innerImg = Background(inner, Palette.Card);
            innerImg.sprite = UiShapes.Rounded(13);
            innerImg.type = Image.Type.Sliced;
            innerImg.raycastTarget = false;
            bool dim = d.StatusText != null;

            // 초상 자리
            var pic = Place(Rect(card, "PortraitBox"), 14, 14, W - 28, 250);
            var picBg = Background(pic, new Color(0.894f, 0.859f, 0.776f));
            picBg.sprite = UiShapes.Rounded(12);
            picBg.type = Image.Type.Sliced;
            picBg.raycastTarget = false;
            var portrait = new Portrait(pic, 180);
            portrait.Rect.anchorMin = portrait.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            portrait.Rect.sizeDelta = new Vector2(180, 180);
            portrait.Show(d.Id, d.Name, Palette.Ally);
            foreach (var g in portrait.Rect.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            if (dim) pic.gameObject.AddComponent<CanvasGroup>().alpha = 0.5f;

            if (!string.IsNullOrEmpty(d.GradeName))
            {
                var tag = Place(Rect(card, "Grade"), 14, 14, 96, 44);
                var tagImg = Background(tag, gradeColor);
                tagImg.sprite = UiShapes.Rounded(10);
                tagImg.type = Image.Type.Sliced;
                tagImg.raycastTarget = false;
                var tt = Label(tag, d.GradeName, 24, TextAnchor.MiddleCenter, OnAccent);
                tt.fontStyle = FontStyle.Bold;
                Fill(tt.rectTransform);
            }
            if (dim)
            {
                var strip = Place(Rect(card, "Status"), 14, 214, W - 28, 50);
                Background(strip, d.StatusColor).raycastTarget = false;
                var st = Label(strip, d.StatusText, 28, TextAnchor.MiddleCenter, OnAccent);
                st.fontStyle = FontStyle.Bold;
                Fill(st.rectTransform);
            }
            if (d.Attention)
            {
                var dot = Place(Rect(card, "AlertDot"), W - 14 - 38, 22, 30, 30);
                var dotImg = Background(dot, Palette.Seal);
                dotImg.sprite = UiShapes.Circle;
                dotImg.raycastTarget = false;
            }

            var textColor = dim ? Palette.TextDim : Palette.Text;
            var name = Label(Place(Rect(card, "NameBox"), 26, 282, 330, 56), d.Name, 34, TextAnchor.MiddleLeft, textColor);
            name.fontStyle = FontStyle.Bold;
            Fill(name.rectTransform);
            var lv = Label(Place(Rect(card, "LvBox"), W - 26 - 140, 282, 140, 56), "Lv." + d.Level, 30, TextAnchor.MiddleRight, Palette.TextDim);
            Fill(lv.rectTransform);

            var stats = Place(Rect(card, "Stats"), 26, 362, W - 52, 56);
            var sh = stats.gameObject.AddComponent<HorizontalLayoutGroup>();
            sh.spacing = 14;
            sh.childAlignment = TextAnchor.MiddleLeft;
            sh.childControlWidth = sh.childControlHeight = true;
            sh.childForceExpandWidth = sh.childForceExpandHeight = false;
            foreach (var (id, v) in new[] { ("leadership", d.Leadership), ("might", d.Might), ("intellect", d.Intellect) })
            {
                Icons.Make(ctx, stats, id, 44);
                var t = Label(stats, v.ToString(), 32, TextAnchor.MiddleLeft, textColor);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                Size(t, 44, 52).flexibleWidth = 0;
            }
            var gap = Rect(stats, "Gap");
            Size(gap, 44, -1, -1, 1);
            SmallInfo(stats, onInfo);
            return card;
        }

        // ---------------- 부대 관리 (시안 06) ----------------

        /// <summary>
        /// 병종 그림 칸: 둥근 칸(바탕 #E4DBC6) 안에 전투 병사 모델 그림 `Resources/Ui/troops/&lt;병종&gt;`.
        /// 승급 병종 그림이 없으면 승급 전 병종 그림, 그것도 없으면 병종 이름 글자.
        /// </summary>
        public static RectTransform TroopPicture(GameContext ctx, Transform parent, string troopType, float size)
        {
            var box = Rect(parent, "TroopPicture");
            Size(box, size, size).flexibleWidth = 0;
            var bg = Background(box, new Color(0.894f, 0.859f, 0.776f));
            bg.sprite = UiShapes.Rounded(14);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            Sprite pic = null;
            string id = troopType;
            for (int guard = 0; guard < 4 && id != null && pic == null; guard++)
            {
                pic = Resources.Load<Sprite>("Ui/troops/" + id);
                if (pic != null) break;
                string from = null;
                foreach (var p in ctx.Defs.TroopGrowth.Promotions) if (p.To == id) { from = p.From; break; }
                id = from;
            }
            if (pic != null)
            {
                var img = Background(Rect(box, "Picture"), Color.white);
                Fill(img.rectTransform);
                img.rectTransform.offsetMin = new Vector2(4, 4);
                img.rectTransform.offsetMax = new Vector2(-4, -4);
                img.sprite = pic;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                string name = ctx.Defs.TroopTypes.TryGetValue(troopType ?? "", out var t) ? ctx.T(t.NameKey) : troopType;
                var l = Label(box, name, Mathf.RoundToInt(size * 0.2f), TextAnchor.MiddleCenter, Palette.TextDim);
                Fill(l.rectTransform);
            }
            return box;
        }

        /// <summary>
        /// 게이지 줄: 이름(회색) → 게이지 → 값 (+ 값 아래 작은 글자). extra = 채움 뒤에 이어 붙이는 반투명 칸 (예: 부상병).
        /// 비율은 화면 표시용 (규칙 계산 아님).
        /// </summary>
        public static RectTransform GaugeRow(Transform parent, string name, float ratio, Color color, string value, string sub = null, float extra = 0f, float barWidth = 270f)
        {
            var row = Rect(parent, "GaugeRow_" + name);
            float rowH = sub == null ? 38f : 54f; // 값 아래 작은 글자가 있으면 줄을 높여 다음 줄과 겹치지 않게
            Size(row, rowH);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var n = Label(row, name, 24, TextAnchor.MiddleLeft, Palette.TextDim);
            Size(n, rowH, 80).flexibleWidth = 0;

            var bar = Rect(row, "Bar");
            Size(bar, 18, barWidth).flexibleWidth = 0;
            var back = Background(bar, new Color(0.839f, 0.808f, 0.745f));
            back.sprite = UiShapes.Rounded(9);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;
            ratio = Mathf.Clamp01(ratio);
            if (extra > 0f) Fillbar(bar, Mathf.Clamp01(ratio + extra), new Color(color.r, color.g, color.b, 0.37f));
            Fillbar(bar, ratio, color);

            var vbox = Rect(row, "Value");
            Size(vbox, rowH, -1, -1, 1);
            var v = Label(vbox, value, 24, sub == null ? TextAnchor.MiddleLeft : TextAnchor.UpperLeft);
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            Fill(v.rectTransform);
            if (sub != null)
            {
                v.rectTransform.offsetMin = new Vector2(0, 24);
                var s = Label(vbox, sub, 19, TextAnchor.LowerLeft, Palette.TextDim);
                s.horizontalOverflow = HorizontalWrapMode.Overflow;
                Fill(s.rectTransform);
                s.rectTransform.offsetMax = new Vector2(0, -30);
            }
            return row;
        }

        private static void Fillbar(RectTransform bar, float ratio, Color color)
        {
            if (ratio <= 0f) return;
            var f = Rect(bar, "Fill");
            f.anchorMin = Vector2.zero;
            f.anchorMax = new Vector2(ratio, 1f);
            f.offsetMin = f.offsetMax = Vector2.zero;
            var img = Background(f, color);
            img.sprite = UiShapes.Rounded(9);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
        }

        /// <summary>
        /// 2단 작은 버튼 (190×100): 위 = 무엇(+양), 아래 = 비용(굵게). 못 하면(비용 모자람) 흐린 바탕 + 빨간 비용, 꺼진 일(가득 등)이면 흐린 바탕 + 회색.
        /// </summary>
        public static Button TwoLineButton(Transform parent, string top, string bottom, Action onClick, bool enabled, bool costShort = false, float width = 190, float height = 100)
        {
            var b = Button(parent, "", onClick, height, enabled ? Palette.Button : Palette.ButtonDisabled, 24);
            Size(b, height, width).flexibleWidth = 0;
            b.interactable = enabled;
            var cb = b.colors; cb.disabledColor = Color.white; b.colors = cb; // 꺼져도 반투명하게 만들지 않는다 (색으로 구별)
            var label = b.GetComponentInChildren<Text>();
            label.text = top;
            label.fontSize = Mathf.RoundToInt(25 * UiStyle.FontScale);
            label.alignment = TextAnchor.UpperCenter;
            label.color = enabled ? Palette.Text : Palette.TextDim;
            label.rectTransform.offsetMax = new Vector2(-6, -10);
            var cost = Label(b.transform, bottom, 26, TextAnchor.LowerCenter, costShort ? Palette.Bad : enabled ? Palette.Text : Palette.TextDim);
            cost.fontStyle = FontStyle.Bold;
            Fill(cost.rectTransform);
            cost.rectTransform.offsetMin = new Vector2(6, 10);
            return b;
        }

        /// <summary>
        /// 수량 조절 줄: 이름 [−] 값 [+] / 최대 [모두]. 누르면 onChange(새 값). minus·plus = 그 버튼이 만들 값 (단위·최소는 화면이 정한다).
        /// 값이 이미 그 끝이면 그 버튼은 흐리게.
        /// </summary>
        public static RectTransform Stepper(Transform parent, string label, long value, long max, string allLabel, Action<long> onChange, long minus, long plus)
        {
            var row = Row(parent, "Stepper", 64, 14);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            var name = Label(row, label, 28, TextAnchor.MiddleLeft, Palette.TextDim);
            Size(name, 64, 170).flexibleWidth = 0;
            Small(row, "−", minus, minus != value, onChange, 64);
            var v = Label(row, value.ToString("N0"), 34, TextAnchor.MiddleCenter);
            v.fontStyle = FontStyle.Bold;
            Size(v, 64, 150).flexibleWidth = 0;
            Small(row, "+", plus, plus != value, onChange, 64);
            var m = Label(row, "/ " + max.ToString("N0"), 28, TextAnchor.MiddleLeft, Palette.TextDim);
            Size(m, 64, 150).flexibleWidth = 0;
            var all = Small(row, allLabel, max, max != value, onChange, 120);
            all.GetComponentInChildren<Text>().fontSize = Mathf.RoundToInt(28 * UiStyle.FontScale);
            return row;
        }

        private static Button Small(Transform row, string text, long target, bool enabled, Action<long> onChange, float width)
        {
            var b = Styled(Button(row, text, () => onChange?.Invoke(target), 56, Palette.Button, 34), "button.small");
            Size(b, 56, width).flexibleWidth = 0;
            b.interactable = enabled;
            if (!enabled) b.GetComponentInChildren<Text>().color = Palette.TextDim;
            return b;
        }

        // ================= 07 부대 편성에서 나온 부품 (공통부품.md 0장: 역할로 부르고 모양은 스타일 데이터) =================

        /// <summary>스타일 데이터의 역할 모양을 버튼에 입힌다 (색·글자색·높이·글자 크기, 정한 것만). 페이지 덮어쓰기는 <see cref="UiStyle.Page"/>.</summary>
        public static Button Styled(Button b, string role)
        {
            var st = UiStyle.Part(role);
            if (st == null) return b;
            if (st.Color.HasValue && b.targetGraphic != null) b.targetGraphic.color = st.Color.Value;
            var t = b.GetComponentInChildren<Text>();
            if (t != null && st.TextColor.HasValue) t.color = st.TextColor.Value;
            if (t != null && st.FontSize > 0) t.fontSize = Mathf.RoundToInt(st.FontSize * UiStyle.FontScale);
            if (st.Height > 0) { var le = b.GetComponent<LayoutElement>(); if (le != null) { le.minHeight = le.preferredHeight = st.Height; } }
            return b;
        }

        /// <summary>가로 스크롤 줄 오른쪽 끝의 "›" 표시 (끝까지 밀면 숨김). 메인 탭 바·편성 보유 병력 줄.</summary>
        public static void MoreHint(RectTransform bar, float h, Color back)
        {
            var hint = Rect(bar, "MoreHint");
            hint.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            hint.anchorMin = new Vector2(1f, 0f);
            hint.anchorMax = new Vector2(1f, 1f);
            hint.pivot = new Vector2(1f, 0.5f);
            hint.sizeDelta = new Vector2(40f, 0f);
            Background(hint, new Color(back.r, back.g, back.b, 0.9f)).raycastTarget = false;
            var t = Label(hint, "›", Mathf.RoundToInt(Mathf.Min(48, h * 0.6f)), TextAnchor.MiddleCenter, Palette.TextDim);
            Fill(t.rectTransform);
            var scroll = bar.GetComponentInChildren<ScrollRect>();
            if (scroll != null) scroll.onValueChanged.AddListener(v => hint.gameObject.SetActive(v.x < 0.98f));
        }

        /// <summary>세그먼트 탭: 나란한 보조 버튼, 고른 것은 주홍·밝은 글자 (역할 tabs.segment).</summary>
        public static RectTransform SegmentTabs(Transform parent, IList<(string id, string label)> items, string selected, Action<string> onSelect, float height = 58, float width = 150)
        {
            var row = Row(parent, "SegmentTabs", height, 8);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            foreach (var it in items)
            {
                var item = it;
                bool on = item.id == selected;
                var b = Button(row, item.label, () => onSelect?.Invoke(item.id), height, on ? Palette.ButtonActive : Palette.Button, 24);
                Size(b, height, width).flexibleWidth = 0;
                if (on) b.GetComponentInChildren<Text>().color = OnAccent;
                Styled(b, on ? "tabs.segment.on" : "tabs.segment");
            }
            return row;
        }

        /// <summary>미니 카드 상태: 보통 / 흐림(다 씀·놓임) / 경고(넘침).</summary>
        public enum MiniState { Normal, Dim, Warn }

        /// <summary>
        /// 미니 카드 (역할 card.mini): 정사각 그림(병종 그림 또는 장수 원) · 이름 · 한 줄 값. 꼬리표("제1군")·상태 띠(경상 등) 선택.
        /// troopType이 있으면 병종 그림, 없으면 장수(generalId·이름 첫 글자).
        /// </summary>
        public static Button MiniCard(GameContext ctx, Transform parent, string troopType, string generalId, string name, string value, MiniState state,
            Action onClick, string tag = null, string status = null, Color statusColor = default, float width = 138, float height = 158)
        {
            var card = Rect(parent, "MiniCard");
            Size(card, height, width).flexibleWidth = 0;
            var bg = Background(card, state == MiniState.Dim ? new Color(Palette.Card.r * 0.93f, Palette.Card.g * 0.93f, Palette.Card.b * 0.93f) : Palette.Card);
            Round(bg);
            var b = card.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            if (onClick != null) b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onClick(); });
            if (state == MiniState.Warn)
            {
                var o = card.gameObject.AddComponent<Outline>();
                o.effectColor = Palette.Bad;
                o.effectDistance = new Vector2(3, -3);
            }
            float pic = Mathf.Min(width - 50, height - 76);
            var picHolder = Place(Rect(card, "Pic"), (width - pic) / 2, 8, pic, pic);
            if (troopType != null) Fill(TroopPicture(ctx, picHolder, troopType, pic));
            else
            {
                var p = new Portrait(picHolder, pic);
                p.Rect.anchorMin = p.Rect.anchorMax = new Vector2(0.5f, 0.5f);
                p.Show(generalId, name, Palette.Ally);
            }
            foreach (var g in card.GetComponentsInChildren<Graphic>()) if (g != bg) g.raycastTarget = false;
            if (state == MiniState.Dim) picHolder.gameObject.AddComponent<CanvasGroup>().alpha = 0.5f;
            var dimText = state == MiniState.Dim ? Palette.TextDim : Palette.Text;
            var n = Label(Place(Rect(card, "Name"), 0, pic + 10, width, 30), name, 22, TextAnchor.MiddleCenter, dimText);
            n.fontStyle = FontStyle.Bold;
            Fill(n.rectTransform);
            var v = Label(Place(Rect(card, "Value"), 0, pic + 38, width, 26), value, 19, TextAnchor.MiddleCenter, state == MiniState.Warn ? Palette.Bad : dimText);
            Fill(v.rectTransform);
            if (status != null)
            {
                var band = Place(Rect(card, "Status"), 0, pic + 8 - 30, width, 30);
                Background(band, statusColor).raycastTarget = false;
                var st = Label(band, status, 19, TextAnchor.MiddleCenter, OnAccent);
                st.fontStyle = FontStyle.Bold;
                Fill(st.rectTransform);
            }
            if (tag != null)
            {
                var tg = Place(Rect(card, "Tag"), width / 2 - 45, height - 28, 90, 24);
                var ti = Background(tg, Palette.TextDim);
                ti.sprite = UiShapes.Rounded(8);
                ti.type = Image.Type.Sliced;
                ti.raycastTarget = false;
                var tt = Label(tg, tag, 17, TextAnchor.MiddleCenter, OnAccent);
                Fill(tt.rectTransform);
            }
            return b;
        }

        /// <summary>
        /// 접는 카드 (역할 card.collapsible): 머리 줄(▾/▸ + 내용, 누르면 접기·펴기) + 몸통(펼쳤을 때만). warn이면 빨간 테두리.
        /// 돌려주는 머리·몸통에 내용을 넣는다.
        /// </summary>
        public static (RectTransform header, RectTransform body) CollapsibleCard(Transform parent, string name, bool expanded, Action onToggle, bool warn = false)
        {
            var card = Rect(parent, name);
            Round(Background(card, Palette.Card));
            if (warn)
            {
                var o = card.gameObject.AddComponent<Outline>();
                o.effectColor = Palette.Bad;
                o.effectDistance = new Vector2(4, -4);
            }
            var col = Column(card, 10, 18);
            var header = Rect(card, "Header");
            var hh = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            hh.spacing = 12;
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.childControlWidth = hh.childControlHeight = true;
            hh.childForceExpandWidth = hh.childForceExpandHeight = false;
            var hit = Background(header, new Color(0, 0, 0, 0));
            var tb = header.gameObject.AddComponent<Button>();
            tb.targetGraphic = hit;
            tb.transition = Selectable.Transition.None;
            if (onToggle != null) tb.onClick.AddListener(() => { Sfx.Play("click", 0.4f); onToggle(); });
            var arrow = Label(header, expanded ? "▾" : "▸", 30, TextAnchor.MiddleCenter, Palette.TextDim);
            Size(arrow, 50, 34).flexibleWidth = 0;
            var body = Rect(card, "Body");
            Column(body, 10, 0);
            body.gameObject.SetActive(expanded);
            return (header, body);
        }

        /// <summary>
        /// 계급 띠 (역할 band.rank): 계급 표식 · 계급 이름 · Lv · 작은 ⓘ(말풍선: 지금 계급·다음 계급) · 군단·칸·병종 최대 · 다음 계급.
        /// </summary>
        public static RectTransform RankBar(GameContext ctx, Transform parent, RectTransform overlayRoot, int usedCorps, int usedPicks)
        {
            var c = ctx.Defs.Campaign;
            int lv = ctx.State.LordLevel;
            var rank = c.RankFor(lv);
            var next = c.NextRank(lv);
            var bar = Rect(parent, "RankBar");
            Size(bar, 84);
            Round(Background(bar, Palette.Card));
            var emblem = Place(Rect(bar, "Emblem"), 14, 10, 64, 64);
            var ei = Background(emblem, new Color(0.788f, 0.588f, 0.169f));
            ei.sprite = UiShapes.Circle;
            string rankName = ctx.T("rank." + rank.Id);
            var et = Label(emblem, rankName.Substring(0, 1), 34, TextAnchor.MiddleCenter, OnAccent);
            et.font = TitleFont;
            Fill(et.rectTransform);
            var title = Label(Place(Rect(bar, "Name"), 92, 2, 220, 50), rankName, 40, TextAnchor.MiddleLeft);
            title.font = TitleFont;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            Fill(title.rectTransform);
            float nameW = title.preferredWidth;
            var lvT = Label(Place(Rect(bar, "Lv"), 100 + nameW, 10, 90, 40), "Lv." + lv, 26, TextAnchor.MiddleLeft, Palette.TextDim);
            Fill(lvT.rectTransform);
            var info = Place(Rect(bar, "Info"), 100 + nameW + 80, 12, 40, 40);
            Fill((RectTransform)SmallInfo(info, anchor =>
            {
                var lines = new List<(string iconId, string name, string value)>
                {
                    (null, ctx.T("ui.rank.now", rankName, rank.MinLevel), ""),
                    (null, ctx.T("ui.rank.limits", rank.Corps, rank.Slots), ""),
                    (null, ctx.T("ui.rank.troopMax", rank.TroopMax.ToString("N0"), c.RecruitQuality(lv)), ""),
                };
                if (next != null)
                {
                    lines.Add((null, ctx.T("ui.rank.next", ctx.T("rank." + next.Id), next.MinLevel), ""));
                    lines.Add((null, ctx.T("ui.rank.nextLimits", next.Corps, next.Slots, next.TroopMax.ToString("N0"), c.RecruitQuality(next.MinLevel)), ""));
                }
                StatTip(ctx, overlayRoot, anchor, lines.ToArray());
            }, 40).transform);
            var sum = Label(Place(Rect(bar, "Summary"), 92, 46, 640, 34), ctx.T("ui.rank.summary", usedCorps, rank.Corps, usedPicks, rank.Slots, rank.TroopMax.ToString("N0")), 22, TextAnchor.MiddleLeft);
            Fill(sum.rectTransform);
            if (next != null)
            {
                var nt = Label(Place(Rect(bar, "Next"), 640, 10, 370, 40), ctx.T("ui.rank.nextShort", ctx.T("rank." + next.Id), next.MinLevel, next.Slots), 22, TextAnchor.MiddleRight, Palette.TextDim);
                Fill(nt.rectTransform);
            }
            return bar;
        }

        /// <summary>
        /// 아래에서 올라오는 판 (역할 pop.sheet): 뒤를 어둡게(누르면 닫힘) + 아래 둥근 판 + 제목. 돌려주는 내용 묶음에 넣고, close로 닫는다.
        /// </summary>
        public static (RectTransform content, Action close) BottomSheet(RectTransform overlayRoot, string title, float height)
        {
            var dim = Rect(overlayRoot, "SheetDim");
            dim.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Fill(dim);
            var di = Background(dim, new Color(0.08f, 0.06f, 0.05f, 0.5f));
            var db = dim.gameObject.AddComponent<Button>();
            db.targetGraphic = di;
            db.transition = Selectable.Transition.None;
            Action close = () => { if (dim != null) UnityEngine.Object.Destroy(dim.gameObject); };
            db.onClick.AddListener(() => close());
            dim.SetAsLastSibling();
            var panel = Rect(dim, "Sheet");
            panel.anchorMin = new Vector2(0, 0);
            panel.anchorMax = new Vector2(1, 0);
            panel.pivot = new Vector2(0.5f, 0);
            panel.sizeDelta = new Vector2(0, height);
            var pi = Background(panel, Palette.Background);
            pi.sprite = UiArt.Frame(22f);
            pi.type = Image.Type.Sliced;
            panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // 판 누름이 뒤로 새지 않게
            Column(panel, 12, 28);
            var t = Label(panel, title, 30, TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            Size(t, 56);
            var content = Rect(panel, "Content");
            Size(content, flexibleHeight: 1);
            return (content, close);
        }

        /// <summary>확인 창 (역할 pop.confirm): 가운데 창 — 제목 · 설명 · 주요 버튼 1 + 보조 버튼들. 아무 버튼이나 누르면 닫는다.</summary>
        public static RectTransform Confirm(RectTransform overlayRoot, string title, string body, string primary, Action onPrimary, params (string label, Action act)[] others)
        {
            var dim = Rect(overlayRoot, "ConfirmDim");
            dim.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Fill(dim);
            Background(dim, new Color(0.08f, 0.06f, 0.05f, 0.55f));
            dim.SetAsLastSibling();
            var box = Rect(dim, "Confirm");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(900, 470);
            var bi = Background(box, Palette.Background);
            bi.sprite = UiArt.Frame(20f);
            bi.type = Image.Type.Sliced;
            Column(box, 16, 34);
            var t = Label(box, title, 44, TextAnchor.MiddleCenter);
            t.font = TitleFont;
            Size(t, 80);
            Size(Label(box, body, 26, TextAnchor.MiddleCenter, Palette.TextDim), 70);
            void Close() { if (dim != null) UnityEngine.Object.Destroy(dim.gameObject); }
            PrimaryButton(box, primary, () => { Close(); onPrimary?.Invoke(); }, 92, 32);
            var row = Row(box, "Others", 92, 16);
            foreach (var o in others) { var x = o; SecondaryButton(row, x.label, () => { Close(); x.act?.Invoke(); }, 92, 28); }
            return box;
        }

        /// <summary>병종 태그로 고른 미리보기 색 (보병 파랑 · 창병 초록 · 궁병 금색 · 기병 빨강 · 그 밖 회색).</summary>
        public static Color TroopColor(GameContext ctx, string troopType)
        {
            if (!ctx.Defs.TroopTypes.TryGetValue(troopType ?? "", out var t)) return Palette.TextDim;
            if (t.HasTag("ranged")) return new Color(0.788f, 0.588f, 0.169f);
            if (t.HasTag("mounted")) return new Color(0.69f, 0.227f, 0.18f);
            if (t.HasTag("polearm")) return new Color(0.243f, 0.478f, 0.251f);
            if (t.HasTag("infantry")) return new Color(0.247f, 0.435f, 0.69f);
            return new Color(0.44f, 0.41f, 0.36f);
        }

        /// <summary>
        /// 진형 미리보기 (역할 diagram.formation): 진형 칸 6개를 진형 좌표대로, 칸마다 병종 색 + 인원, 위에 "▲ 앞". 누르면 onClick (진형 고르기).
        /// </summary>
        public static Button FormationPreview(GameContext ctx, Transform parent, string formationId, IReadOnlyList<Game.Campaign.Roster.SlotTroops> slots, float w, float h, Action onClick)
        {
            var box = Rect(parent, "FormationPreview");
            Size(box, h, w).flexibleWidth = 0;
            var bg = Background(box, new Color(0.894f, 0.859f, 0.776f));
            bg.sprite = UiShapes.Rounded(12);
            bg.type = Image.Type.Sliced;
            var b = box.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            if (onClick != null) b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onClick(); });
            var front = Label(Place(Rect(box, "Front"), 8, 2, 100, 26), ctx.T("ui.formation.front"), 18, TextAnchor.MiddleLeft, Palette.TextDim);
            Fill(front.rectTransform);
            if (!ctx.Defs.Formations.TryGetValue(formationId ?? "", out var f)) return b;
            float minX = 0, maxX = 0, minY = 0, maxY = 0;
            foreach (var kv in f.Slots)
            {
                float x = kv.Value.X.Raw / (float)Game.Foundation.Math.Fix.OneValue.Raw, y = kv.Value.Y.Raw / (float)Game.Foundation.Math.Fix.OneValue.Raw;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            float bw = Mathf.Min(92, w / 4.2f), bh = 40;
            float sx = (w - bw - 20) / Mathf.Max(1f, maxX - minX), sy = (h - bh - 44) / Mathf.Max(1f, maxY - minY);
            foreach (var st in slots)
            {
                if (!f.Slots.TryGetValue(st.Slot, out var off)) continue;
                float x = off.X.Raw / (float)Game.Foundation.Math.Fix.OneValue.Raw, y = off.Y.Raw / (float)Game.Foundation.Math.Fix.OneValue.Raw;
                float cx = 10 + (x - minX) * sx, cy = 30 + (maxY - y) * sy;
                var u = ctx.State.FindUnit(st.UnitId);
                var cell = Place(Rect(box, "Slot_" + st.Slot), cx, cy, bw, bh);
                var ci = Background(cell, TroopColor(ctx, u?.TroopType));
                ci.sprite = UiShapes.Rounded(8);
                ci.type = Image.Type.Sliced;
                ci.raycastTarget = false;
                var n = Label(cell, st.Troops.ToString("N0"), 19, TextAnchor.MiddleCenter, OnAccent);
                n.fontStyle = FontStyle.Bold;
                Fill(n.rectTransform);
            }
            return b;
        }

        /// <summary>
        /// 페이지 틀 (역할 page.default): 고정 머리(아래 그림자) · 스크롤 몸통 · 고정 아래 버튼 줄. root는 탭·화면 전체.
        /// </summary>
        public static (RectTransform header, RectTransform body, RectTransform footer) PageLayout(RectTransform root, float footerHeight = 120)
        {
            Column(root, 0, 0);
            var header = Rect(root, "PageHeader");
            var hc = Column(header, 10, 0);
            hc.padding = new RectOffset(25, 25, 12, 12);
            Background(header, Palette.Background).raycastTarget = false;
            var shadow = Rect(root, "HeaderShadow");
            Size(shadow, 8);
            Background(shadow, new Color(0.16f, 0.13f, 0.1f, 0.12f)).raycastTarget = false;
            var body = ScrollList(root, "PageBody", 14, 25);
            Size(body.parent.parent, flexibleHeight: 1);
            var footer = Row(root, "PageFooter", footerHeight, 12);
            footer.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(25, 25, 14, 14);
            Background(footer, Palette.Background).raycastTarget = false;
            return (header, body, footer);
        }

        /// <summary>놓기 판 (역할 board.drop): rows × cols 점선 칸. 칸 RectTransform 배열을 돌려준다 (놓을 곳 등록·카드 넣기는 화면이).</summary>
        public static RectTransform[,] DropGrid(Transform parent, int rows, int cols, float cellW, float cellH, string header = null)
        {
            var board = Rect(parent, "DropGrid");
            float hh = header == null ? 0 : 50;
            Size(board, rows * cellH + hh + 20, cols * cellW + 20).flexibleWidth = 0;
            var bg = Background(board, new Color(0.84f, 0.8f, 0.7f));
            Round(bg);
            if (header != null)
            {
                var t = Label(Place(Rect(board, "Header"), 0, 4, cols * cellW + 20, 44), header, 26, TextAnchor.MiddleCenter);
                t.fontStyle = FontStyle.Bold;
                Fill(t.rectTransform);
            }
            var cells = new RectTransform[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var cell = Place(Rect(board, "Cell_" + r + "_" + c), 10 + c * cellW + 6, hh + 10 + r * cellH + 6, cellW - 12, cellH - 12);
                    var ci = Background(cell, new Color(1, 1, 1, 0.18f));
                    ci.sprite = UiShapes.Rounded(12);
                    ci.type = Image.Type.Sliced;
                    cells[r, c] = cell;
                }
            return cells;
        }
    }
}
