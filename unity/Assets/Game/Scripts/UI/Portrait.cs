using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 장수 초상 (battle.md 4장 ③): Resources/Art/portraits/{장수 id} 그림이 있으면 그것, 없으면 진영 색 원 안에 이름 첫 글자 (수묵 도장 느낌).
    /// 그림이 생기면 파일만 넣으면 된다 (코드 수정 없음).
    /// </summary>
    public sealed class Portrait
    {
        private readonly Image _back;
        private readonly Image _picture;
        private readonly Text _letter;
        private string _shownId;

        public RectTransform Rect { get; }

        public Portrait(Transform parent, float size)
        {
            Rect = Ui.Rect(parent, "Portrait");
            Ui.Size(Rect, size, size);
            _back = Ui.Background(Rect, Palette.Card);
            _back.sprite = UiShapes.Circle;
            _picture = Ui.Background(Ui.Rect(Rect, "Picture"), Color.white);
            Ui.Fill(_picture.rectTransform);
            _picture.preserveAspect = true;
            _picture.raycastTarget = false;
            _letter = Ui.Label(Rect, "", Mathf.RoundToInt(size * 0.5f), TextAnchor.MiddleCenter);
            Ui.Fill(_letter.rectTransform);
        }

        /// <summary>장수 id·이름·진영 색. 같은 장수면 그림을 다시 찾지 않는다.</summary>
        public void Show(string generalId, string name, Color team)
        {
            _back.color = new Color(team.r * 0.55f, team.g * 0.55f, team.b * 0.55f, 1f);
            if (_shownId == generalId) return;
            _shownId = generalId;
            var sprite = string.IsNullOrEmpty(generalId) ? null : Resources.Load<Sprite>("Art/portraits/" + generalId);
            _picture.sprite = sprite;
            _picture.gameObject.SetActive(sprite != null);
            _letter.text = sprite == null && !string.IsNullOrEmpty(name) ? name.Substring(0, 1) : "";
        }
    }
}
