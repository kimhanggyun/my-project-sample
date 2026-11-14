using System;
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
    }
}
