using Game.Data.Defs;
using Game.Flow;
using Game.Unity.Boot;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 처음 안내 (R-62) 화면 쪽: 화면이 자기 자리 id로 부르면, 안 본 안내가 있을 때 화면 위에 어두운 판 + 문장 카드를 띄운다.
    /// [다음]으로 한 줄씩, 마지막 줄의 버튼을 누르면 행동 guide.seen을 보내고 닫는다. 문장·자리는 데이터(Rules/guides.json).
    /// </summary>
    public static class GuideOverlay
    {
        /// <returns>안내를 띄웠으면 true. 닫힐 때 onClosed를 부른다.</returns>
        /// <summary>켜 두면 안내를 띄우지 않는다 (캡처 도구가 전투 진행 화면을 찍을 때). 세이브의 "본 안내"는 바뀌지 않는다.</summary>
        public static bool Suppressed { get; set; }

        public static bool Show(GameApp app, RectTransform root, string place, System.Action onClosed = null, string lastButtonKey = "ui.guide.close")
        {
            if (Suppressed) return false;
            var ctx = app.Context;
            GuideDef guide = new GuideService(ctx.Defs).Next(ctx.State, place);
            if (guide == null) return false;

            var overlay = Ui.Panel(root, "Guide", Palette.Overlay);
            overlay.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            overlay.SetAsLastSibling();
            // 카드는 문장 길이만큼만 (가운데, 가로 88%). 빈 공간이 크게 남지 않게
            var card = Ui.Card(overlay, "Card", 32, 16);
            card.anchorMin = new Vector2(0.06f, 0.5f);
            card.anchorMax = new Vector2(0.94f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = Ui.Label(card, "", Ui.FontBody, TextAnchor.UpperLeft);
            var count = Ui.Label(card, "", Ui.FontSmall, TextAnchor.MiddleRight, Palette.TextDim);
            Ui.Size(count, 40);
            var row = Ui.Row(card, "Buttons", Ui.ButtonHeight);
            int line = 0;
            Button next = null;
            void ShowLine()
            {
                text.text = ctx.T(guide.Lines[line]);
                count.text = (line + 1) + " / " + guide.Lines.Count;
                next.GetComponentInChildren<Text>().text = ctx.T(line + 1 < guide.Lines.Count ? "ui.guide.next" : lastButtonKey);
            }
            next = Ui.Button(row, "", () =>
            {
                if (++line < guide.Lines.Count) { ShowLine(); return; }
                ctx.Do(PlayerActionRunner.SeeGuide(guide.Id));
                Object.Destroy(overlay.gameObject);
                onClosed?.Invoke();
            }, color: Palette.ButtonActive);
            ShowLine();
            return true;
        }
    }
}
