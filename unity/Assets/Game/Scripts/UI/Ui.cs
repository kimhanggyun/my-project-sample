using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>임시 화면용 색 (화면 디자인 전까지).</summary>
    public static class Palette
    {
        public static readonly Color Background = new Color(0.10f, 0.11f, 0.13f);
        public static readonly Color Bar = new Color(0.16f, 0.17f, 0.20f);
        public static readonly Color Card = new Color(0.20f, 0.21f, 0.25f);
        public static readonly Color Button = new Color(0.28f, 0.33f, 0.45f);
        public static readonly Color ButtonActive = new Color(0.55f, 0.42f, 0.20f);
        public static readonly Color ButtonDisabled = new Color(0.22f, 0.22f, 0.24f);
        public static readonly Color Text = new Color(0.93f, 0.93f, 0.90f);
        public static readonly Color TextDim = new Color(0.62f, 0.63f, 0.66f);
        public static readonly Color Ally = new Color(0.55f, 0.75f, 1.00f);
        public static readonly Color Enemy = new Color(1.00f, 0.55f, 0.50f);
        public static readonly Color Good = new Color(0.55f, 0.90f, 0.55f);
        public static readonly Color Bad = new Color(1.00f, 0.45f, 0.40f);
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.6f);
    }

    /// <summary>
    /// 코드로 uGUI를 만드는 도우미 (뼈대 단계: 프리팹·아트 없이 화면 흐름만 확인). 기준 해상도 1080×1920 세로.
    /// </summary>
    public static class Ui
    {
        public const int FontBody = 40;
        public const int FontSmall = 32;
        public const int FontTitle = 64;
        public const int ButtonHeight = 120;

        private static Font _font;
        /// <summary>내장 폰트. 한글은 OS 글꼴로 대체되어 보인다 (정식 글꼴은 화면 디자인 단계에서).</summary>
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>부모를 꽉 채운다.</summary>
        public static RectTransform Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Background(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>부모를 채우는 판 (색이 있으면 배경).</summary>
        public static RectTransform Panel(Transform parent, string name, Color? color = null)
        {
            var rt = Fill(Rect(parent, name));
            if (color.HasValue) Background(rt, color.Value);
            return rt;
        }

        /// <summary>레이아웃 안에서의 크기. 0 이하 값은 지정하지 않음.</summary>
        public static LayoutElement Size(Component c, float height = -1, float width = -1, float flexibleHeight = -1, float flexibleWidth = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (height > 0) { le.minHeight = height; le.preferredHeight = height; }
            if (width > 0) { le.minWidth = width; le.preferredWidth = width; }
            if (flexibleHeight > 0) le.flexibleHeight = flexibleHeight;
            if (flexibleWidth > 0) le.flexibleWidth = flexibleWidth;
            return le;
        }

        /// <summary>세로로 쌓는 묶음. 자식 높이는 각자의 LayoutElement·글 길이로.</summary>
        public static VerticalLayoutGroup Column(RectTransform rt, int spacing = 16, int padding = 0)
        {
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return g;
        }

        /// <summary>가로로 나란히 (자식 너비를 똑같이).</summary>
        public static RectTransform Row(Transform parent, string name, float height, int spacing = 12)
        {
            var rt = Rect(parent, name);
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = true;
            // 줄 높이는 고정: 자식을 세로로 채우게 하면(force expand) 줄 자체가 남는 세로 공간을 나눠 가져
            // 버튼 줄·상단 막대가 커지고 전장 같은 flexible 영역이 작아진다
            Size(rt, height).flexibleHeight = 0;
            return rt;
        }

        public static Text Label(Transform parent, string text, int size = FontBody, TextAnchor align = TextAnchor.MiddleLeft, Color? color = null)
        {
            var rt = Rect(parent, "Label");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? Palette.Text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        public static Button Button(Transform parent, string text, Action onClick, float height = ButtonHeight, Color? color = null, int fontSize = FontBody)
        {
            var rt = Rect(parent, "Button");
            var img = Background(rt, color ?? Palette.Button);
            var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var label = Label(rt, text, fontSize, TextAnchor.MiddleCenter);
            Fill(label.rectTransform);
            Size(rt, height);
            return b;
        }

        /// <summary>한 줄 글자 입력 (안내 문구, 최대 글자 수). 폰에서는 누르면 키보드가 뜬다.</summary>
        public static InputField TextInput(Transform parent, string placeholder, int maxLength, float height = ButtonHeight, int fontSize = FontBody)
        {
            var rt = Rect(parent, "Input");
            var img = Background(rt, Palette.Card);
            var input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = img;
            var text = Label(rt, "", fontSize, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            Fill(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(24, 0);
            text.rectTransform.offsetMax = new Vector2(-24, 0);
            var hint = Label(rt, placeholder, fontSize, TextAnchor.MiddleLeft, Palette.TextDim);
            Fill(hint.rectTransform);
            hint.rectTransform.offsetMin = new Vector2(24, 0);
            hint.rectTransform.offsetMax = new Vector2(-24, 0);
            input.textComponent = text;
            input.placeholder = hint;
            input.characterLimit = maxLength;
            Size(rt, height);
            return input;
        }

        public static void SetEnabled(Button b, bool enabled)
        {
            b.interactable = enabled;
            if (b.targetGraphic != null) b.targetGraphic.color = enabled ? Palette.Button : Palette.ButtonDisabled;
        }

        /// <summary>세로 스크롤 목록. 돌려주는 content 아래에 줄을 넣는다.</summary>
        public static RectTransform ScrollList(Transform parent, string name, int spacing = 16, int padding = 24)
        {
            var root = Rect(parent, name);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var viewport = Panel(root, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            // 빈 곳을 끌어도 스크롤되도록 투명한 배경을 둔다.
            Background(viewport, new Color(0, 0, 0, 0));

            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            Column(content, spacing, padding);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        /// <summary>가로 스크롤 줄 (탭 바 등). 돌려주는 content 아래에 너비를 정한 항목을 넣는다.</summary>
        public static RectTransform HScrollRow(Transform parent, string name, float height, int spacing = 8, int padding = 8)
        {
            var root = Rect(parent, name);
            Size(root, height);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var viewport = Panel(root, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            Background(viewport, new Color(0, 0, 0, 0));

            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 0);
            content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 0.5f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var g = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = true;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        /// <summary>카드 (배경 + 세로 묶음).</summary>
        public static RectTransform Card(Transform parent, string name, int padding = 24, int spacing = 8)
        {
            var rt = Rect(parent, name);
            Background(rt, Palette.Card);
            Column(rt, spacing, padding);
            return rt;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string Colored(string text, Color c) => "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + text + "</color>";
    }
}
