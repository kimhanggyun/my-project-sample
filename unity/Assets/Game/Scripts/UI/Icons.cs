using System.Collections.Generic;
using Game.Unity.Boot;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 자원·능력치 아이콘 (main-screen.md 1장, 2026-11-13): 그림이 있으면 `Resources/Ui/icons/&lt;id&gt;` (Sprite),
    /// 없으면 낙관 판에 한 글자 (문구 `ui.icon.&lt;id&gt;`: 糧·錢·玉·統·武·智). id: supply, gold, gem, leadership, might, intellect.
    /// </summary>
    public static class Icons
    {
        private static readonly Dictionary<string, Sprite> Loaded = new Dictionary<string, Sprite>();

        /// <summary>낙관 바탕색 (그림이 없을 때). 자원은 인주색, 능력치는 저마다.</summary>
        private static Color SealColor(string id)
        {
            switch (id)
            {
                case "leadership": return new Color(0.2f, 0.36f, 0.6f);
                case "might": return new Color(0.72f, 0.2f, 0.16f);
                case "intellect": return new Color(0.22f, 0.5f, 0.36f);
                default: return Palette.Seal;
            }
        }

        private static Sprite Picture(string id)
        {
            if (Loaded.TryGetValue(id, out var s)) return s;
            return Loaded[id] = Resources.Load<Sprite>("Ui/icons/" + id);
        }

        /// <summary>아이콘 하나 (정사각형 size).</summary>
        public static RectTransform Make(GameContext ctx, Transform parent, string id, float size)
        {
            var rt = Ui.Rect(parent, "Icon_" + id);
            var le = Ui.Size(rt, size, size);
            le.flexibleWidth = 0;
            le.minWidth = size;
            var pic = Picture(id);
            var img = Ui.Background(rt, pic != null ? Color.white : SealColor(id));
            img.raycastTarget = false;
            if (pic != null) { img.sprite = pic; img.preserveAspect = true; return rt; }
            img.sprite = UiArt.Seal;
            var glyph = Ui.Label(rt, ctx.T("ui.icon." + id), Mathf.RoundToInt(size * 0.58f), TextAnchor.MiddleCenter, new Color(0.98f, 0.94f, 0.86f));
            glyph.font = Ui.TitleFont; // 한자는 제목 글꼴(본명조)에 있다
            glyph.raycastTarget = false;
            Ui.Fill(glyph.rectTransform);
            return rt;
        }

        /// <summary>아이콘 + 값 한 쌍 (가로).</summary>
        public static RectTransform Value(GameContext ctx, Transform parent, string id, string value, float size = 40f, int fontSize = 28, Color? color = null)
        {
            var row = Ui.Row(parent, "Value_" + id, size, 6);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Make(ctx, row, id, size);
            var t = Ui.Label(row, value, fontSize, TextAnchor.MiddleLeft, color);
            Ui.Size(t, size, -1);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return row;
        }

        /// <summary>통솔·무력·지력 아이콘 줄.</summary>
        public static RectTransform Stats(GameContext ctx, Transform parent, long leadership, long might, long intellect, float size = 40f, int fontSize = 28)
        {
            var row = Ui.Row(parent, "Stats", size, 22);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            Value(ctx, row, "leadership", leadership.ToString(), size, fontSize);
            Value(ctx, row, "might", might.ToString(), size, fontSize);
            Value(ctx, row, "intellect", intellect.ToString(), size, fontSize);
            return row;
        }
    }

    /// <summary>
    /// 화면 오른쪽 위 ⓘ 버튼 (2026-11-13): 화면 설명 글은 평소에 숨기고 누르면 카드로 보인다 (다시 누르거나 카드를 누르면 닫힘).
    /// root는 화면(또는 탭) 전체 영역. 레이아웃에 끼지 않는다.
    /// </summary>
    public static class InfoButton
    {
        public const float Size = 64f;

        public static Button Add(RectTransform root, string text)
        {
            var holder = Ui.Rect(root, "InfoButton");
            holder.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            holder.anchorMin = holder.anchorMax = new Vector2(1f, 1f);
            holder.pivot = new Vector2(1f, 1f);
            holder.sizeDelta = new Vector2(Size, Size);
            holder.anchoredPosition = new Vector2(-14f, -10f);
            var img = Ui.Background(holder, Palette.ButtonActive);
            img.sprite = UiShapes.Circle;
            var label = Ui.Label(holder, "i", 38, TextAnchor.MiddleCenter, Palette.TextOn(Palette.ButtonActive));
            label.font = Ui.TitleFont;
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            Ui.Fill(label.rectTransform);
            var button = holder.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            RectTransform card = null;
            button.onClick.AddListener(() =>
            {
                if (card != null) { Object.Destroy(card.gameObject); card = null; return; }
                card = Ui.Rect(root, "InfoCard");
                card.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                card.anchorMin = new Vector2(0f, 1f);
                card.anchorMax = new Vector2(1f, 1f);
                card.pivot = new Vector2(0.5f, 1f);
                card.offsetMin = new Vector2(24f, 0f);
                card.offsetMax = new Vector2(-24f, 0f);
                card.anchoredPosition = new Vector2(0f, -Size - 18f);
                var bg = Ui.Background(card, Palette.PromptBack);
                bg.sprite = UiArt.Frame(18f);
                bg.type = Image.Type.Sliced;
                var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
                v.padding = new RectOffset(28, 28, 22, 22);
                v.childControlHeight = v.childControlWidth = true;
                v.childForceExpandHeight = false;
                card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var t = Ui.Label(card, text, 28, TextAnchor.UpperLeft, Palette.PromptText);
                t.raycastTarget = false;
                var close = card.gameObject.AddComponent<Button>();
                close.targetGraphic = bg;
                var self = card;
                close.onClick.AddListener(() => { if (self != null) Object.Destroy(self.gameObject); card = null; });
                holder.SetAsLastSibling();
            });
            holder.SetAsLastSibling();
            return button;
        }
    }
}
