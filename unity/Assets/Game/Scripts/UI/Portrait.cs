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
        private readonly Image _ring;
        private string _shownId;

        public RectTransform Rect { get; }

        /// <param name="fillParent">레이아웃 없는 자리(그 자리를 직접 정한 경우)면 그 자리를 채운다.</param>
        public Portrait(Transform parent, float size, bool fillParent = false)
        {
            Rect = Ui.Rect(parent, "Portrait");
            Ui.Size(Rect, size, size);
            if (fillParent) Ui.Fill(Rect); // 레이아웃 없는 자리: 그 자리를 채운다 (크기가 기본 100으로 남던 것)
            _back = Ui.Background(Rect, Palette.Card);
            _back.sprite = UiShapes.Circle;
            _picture = Ui.Background(Ui.Rect(Rect, "Picture"), Color.white);
            Ui.Fill(_picture.rectTransform);
            _picture.preserveAspect = true;
            _picture.raycastTarget = false;
            // 수묵: 한지 원판 + 먹 원상(enso) 테두리 + 제목 글꼴 첫 글자
            _ring = Ui.Background(Ui.Rect(Rect, "Ring"), new Color(0.17f, 0.15f, 0.12f, 0.9f));
            Ui.Fill(_ring.rectTransform);
            _ring.sprite = UiArt.Enso;
            _ring.raycastTarget = false;
            _ring.gameObject.SetActive(UiStyle.Paper);
            _letter = Ui.Label(Rect, "", Mathf.RoundToInt(size * 0.5f), TextAnchor.MiddleCenter);
            _letter.font = Ui.TitleFont;
            Ui.Fill(_letter.rectTransform);
        }

        /// <summary>장수 id·이름·진영 색. 같은 장수면 그림을 다시 찾지 않는다.</summary>
        public void Show(string generalId, string name, Color team)
        {
            if (UiStyle.Paper)
            {
                _back.color = Palette.Card;
                _letter.color = new Color(team.r * 0.75f, team.g * 0.75f, team.b * 0.75f, 1f); // 진영 색 먹
            }
            else _back.color = new Color(team.r * 0.55f, team.g * 0.55f, team.b * 0.55f, 1f);
            if (_shownId == generalId) return;
            _shownId = generalId;
            var sprite = string.IsNullOrEmpty(generalId) ? null : Resources.Load<Sprite>("Art/portraits/" + generalId);
            _picture.sprite = sprite;
            _picture.gameObject.SetActive(sprite != null);
            _letter.text = sprite == null && !string.IsNullOrEmpty(name) ? name.Substring(0, 1) : "";
        }
    }

    /// <summary>
    /// 내 프로필 사진 (좌상단 · 내 정보): 구글 사진 주소가 있으면 그 사진을 동그랗게, 없으면(게스트 등) 닉네임 장수 "나" 초상.
    /// 사진은 한 번 받아 앱이 켜져 있는 동안 기억한다 (기기에만, 다른 사람에게 보이지 않음).
    /// </summary>
    public static class ProfilePhoto
    {
        private static string _url;
        private static Texture2D _tex;
        private static bool _loading;
        private static readonly System.Collections.Generic.List<System.Action> Waiting = new System.Collections.Generic.List<System.Action>();

        public static RectTransform Make(Game.Unity.Boot.GameContext ctx, Transform parent, float size)
        {
            var holder = Ui.Rect(parent, "ProfilePhoto");
            Ui.Size(holder, size, size).flexibleWidth = 0;
            new Portrait(holder, size, true).Show(Game.Flow.AvatarGeneral.GeneralId, ctx.GeneralName(Game.Flow.AvatarGeneral.GeneralId), Palette.Seal);
            string url = ctx.PhotoUrl;
            if (url == null) return holder;
            // 동그란 가림 + 사진
            var mask = Ui.Rect(holder, "Mask");
            mask.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Fill(mask);
            var mi = Ui.Background(mask, Color.white);
            mi.sprite = UiShapes.Circle;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var pic = new GameObject("Photo", typeof(RectTransform)).AddComponent<RawImage>();
            pic.transform.SetParent(mask, false);
            Ui.Fill(pic.rectTransform);
            pic.raycastTarget = false;
            mask.gameObject.SetActive(false);
            Load(url, () =>
            {
                if (pic == null || _tex == null) return;
                pic.texture = _tex;
                mask.gameObject.SetActive(true);
            });
            return holder;
        }

        private static void Load(string url, System.Action done)
        {
            if (_url == url && _tex != null) { done(); return; }
            Waiting.Add(done);
            if (_loading && _url == url) return;
            _url = url;
            _loading = true;
            var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url);
            req.timeout = 10;
            req.SendWebRequest().completed += _ =>
            {
                try
                {
                    if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                        _tex = UnityEngine.Networking.DownloadHandlerTexture.GetContent(req);
                }
                finally
                {
                    req.Dispose();
                    _loading = false;
                    var list = new System.Collections.Generic.List<System.Action>(Waiting);
                    Waiting.Clear();
                    foreach (var a in list) a();
                }
            };
        }
    }
}
