using Game.Battle.Model.Crowd;
using Game.Unity.Battle3D;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 맵 보기 (2026-11-22 사용자): 테스트 탭에서 고른 맵을 부대 없이 둘러본다 (끌기 = 이동, 두 손가락 · 휠 = 확대).
    /// 땅은 전투와 같은 CrowdGround (높낮이 · 물 · 다리 · 나무 · 바위 — 바위 · 나무 자리도 규칙과 같음).
    /// </summary>
    public sealed class MapViewScreen : GameScreen
    {
        private const float TopHeight = 76f;
        private readonly string _mapId;
        private GameObject _root;
        private CrowdGround _ground;
        private BattleCameraRig _rig;
        private RectTransform _fieldArea;
        private readonly Vector3[] _corners = new Vector3[4];

        public MapViewScreen(string mapId) { _mapId = mapId; }

        public override void Build()
        {
            var terrain = TestBattleTabView.TerrainOf(Ctx, _mapId);
            var rules = CrowdRules.Load(Ctx.Data);

            Ui.Column(Root, 6, 8);
            var top = Ui.Row(Root, "Top", TopHeight, 8);
            Ui.Background(top, Palette.Bar);
            var title = Ui.Label(top, T("ui.test.mapView", TestBattleTabView.MapName(Ctx, _mapId)), 28, TextAnchor.MiddleLeft);
            Ui.Size(title, flexibleWidth: 1);
            Ui.Size(Ui.Button(top, T("ui.sample2.close"), Back, TopHeight, fontSize: 26), width: 130);

            App.SetBackdrop(false);
            _fieldArea = Ui.Rect(Root, "Field");
            Ui.Size(_fieldArea, flexibleHeight: 1);
            Ui.Background(_fieldArea, new Color(0, 0, 0, 0));
            var gestures = _fieldArea.gameObject.AddComponent<GestureArea>();
            gestures.Dragged += (a, b) => _rig.Pan(a, b);
            gestures.Zoomed += f => _rig.Zoom(f);
            var hint = Ui.Label(Root, T("ui.test.mapViewHint"), 22, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Size(hint, 44);

            // 땅 · 카메라 · 해 (전투 화면 CrowdField3D와 같은 설정)
            _root = new GameObject("MapView");
            var camGo = new GameObject("MapCamera");
            camGo.transform.SetParent(_root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.66f, 0.74f, 0.80f);
            cam.depth = 1;
            cam.cullingMask = ~(1 << 5);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            cam.fieldOfView = 40f;
            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(_root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;

            var width = terrain != null ? terrain.Width : rules.FieldWidth;
            var depth = terrain != null ? terrain.Depth : rules.FieldDepth;
            var obstacles = terrain != null ? terrain.MakeObstacles(rules.Obstacles) : null;
            _ground = new CrowdGround(_root.transform, terrain, width, depth, rules.Obstacles, obstacles, CrowdField3D.NewMaterial);
            float w = (float)width.ToDisplayDouble(), d = (float)depth.ToDisplayDouble();
            _rig = new BattleCameraRig(cam, w, d);
            // 처음엔 실제 전투와 같은 거리 (우리 쪽), 손가락으로 줄이면 맵 전체가 한눈에 (2026-11-22 사용자)
            _rig.MaxZoomOut = 175f;
            _rig.SetDistance(36f);
            _rig.LookAt(new Vector3(w / 2f, 0f, d * 0.2f));
        }

        private void Back() => App.ShowMain("test");

        public override bool OnBack()
        {
            Back();
            return true;
        }

        public override void Tick(float deltaTime)
        {
            if (_rig == null) return;
            _fieldArea.GetWorldCorners(_corners);
            var px = new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y);
            if (px.width > 1 && px.height > 1 && _rig.Camera.pixelRect != px) { _rig.Camera.pixelRect = px; _rig.Refresh(); }
            _ground.Draw(_rig.Camera);
        }

        public override void Close()
        {
            _ground?.Destroy();
            if (_root != null) Object.Destroy(_root);
            App.SetBackdrop(true);
        }
    }
}
