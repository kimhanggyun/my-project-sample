using System.Collections.Generic;
using Game.Battle.Model.Crowd;
using Game.Foundation.Math;
using Game.Online;
using Game.Territory;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 깃발전 전장 맵 (R-168): 분류 [야전 · 성새전 · 수상전] (시험 탭과 같은 나눔) + 미리보기 카드 가로 줄. 꽂기 판 · 깃발 카드가 같이 쓴다.
    /// </summary>
    internal static class FlagMaps
    {
        private static string _cat;

        public static string Name(GameContext ctx, string mapId) => TestBattleTabView.MapName(ctx, mapId);

        /// <summary>미리보기 그림 (위 = 수비 · 깃발 쪽).</summary>
        public static RawImage Thumb(GameContext ctx, Transform parent, string mapId, float w, float h)
        {
            var pic = Ui.Rect(parent, "Preview");
            Ui.Size(pic, h, w).flexibleWidth = 0;
            var raw = pic.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            try { raw.texture = Game.Unity.Battle3D.FieldTexture.Preview(Game.Data.Defs.MapDef.Load(ctx.Data, mapId)); }
            catch (System.Exception) { raw.color = new Color(0.45f, 0.58f, 0.3f); }
            return raw;
        }

        /// <summary>맵 고르기: 분류 소분류 + 고른 분류의 맵 카드. 누르면 onPick (판을 다시 그리는 건 부르는 쪽).</summary>
        public static void Picker(GameContext ctx, Transform parent, string selected, System.Action<string> onPick)
        {
            var fd = ctx.Territory?.Flags;
            var maps = fd?.Maps ?? new List<string> { "crowd_plain" };
            string cat = _cat ?? TestBattleTabView.CategoryOf(selected);
            var cats = new List<(string, string)>();
            foreach (var c in TestBattleTabView.MapCategories) cats.Add((c, ctx.T("situation." + c)));
            Ui.MinorTabs(parent, cats, cat, id => { _cat = id; onPick(selected); });
            var shown = new List<string>();
            foreach (var m in maps) if (TestBattleTabView.CategoryOf(m) == cat) shown.Add(m);
            if (shown.Count == 0) { Ui.EmptyNote(parent, ctx.T("ui.test.noMaps", ctx.T("situation." + cat)), 150); return; }
            var row = Ui.HScrollRow(parent, "Maps", 168, 10, 4);
            foreach (var m in shown)
            {
                string id = m;
                var b = Ui.Button(row, Name(ctx, id), () => { Sfx.Play("click", 0.5f); onPick(id); }, 160, selected == id ? Palette.ButtonActive : Palette.Button, 20);
                Ui.Size(b, 160, 140);
                var label = b.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.LowerCenter;
                var lr = label.rectTransform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(1f, 0f); lr.pivot = new Vector2(0.5f, 0f);
                lr.offsetMin = new Vector2(4f, 4f); lr.offsetMax = new Vector2(-4f, 34f);
                var pic = Thumb(ctx, b.transform, id, 66, 110);
                var pr = pic.rectTransform;
                pic.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f);
                pr.pivot = new Vector2(0.5f, 1f);
                pr.anchoredPosition = new Vector2(0f, -6f);
                pr.sizeDelta = new Vector2(66f, 110f);
            }
        }
    }

    /// <summary>
    /// 수비판 (R-167, 2026-11-28 사용자): 내 깃발의 수비를 미리 짠다 — 주인은 오프라인이고 AI가 이대로 싸운다.
    /// 위 = 전장 미리보기 (위가 깃발 쪽) 위에 함정 (누르면 놓기 · 다시 누르면 빼기, 최대 boardTrapsMax), 아래 = 장수마다 칸 (3 × 3) · 자세 · 매복.
    /// [저장] = 서버에 (판 + 1).
    /// </summary>
    public sealed class FlagBoardScreen : GameScreen
    {
        private readonly TerritoryOwner _flag;
        private readonly DefenseBoard _board;
        private string _trapKind = CrowdTrapKinds.Pit;
        private RectTransform _mapBox;
        private Fix _fieldW, _fieldD;

        public FlagBoardScreen(TerritoryOwner flag, DefenseBoard draft = null)
        {
            _flag = flag;
            var src = draft ?? flag?.Defense?.Board;
            _board = new DefenseBoard();
            if (src != null)
            {
                foreach (var p in src.Places) _board.Places.Add(new CrowdBoardPlace { GeneralId = p.GeneralId, Row = p.Row, Col = p.Col, Stance = p.Stance, Ambush = p.Ambush });
                foreach (var t in src.Traps) _board.Traps.Add(new BoardTrap { Kind = t.Kind, X = t.X, Y = t.Y });
            }
        }

        private CrowdBoardPlace PlaceOf(string gid, int index)
        {
            var p = _board.Places.Find(x => x.GeneralId == gid);
            if (p != null) return p;
            // 처음: 앞줄 가운데부터 빈 칸
            var used = new HashSet<int>();
            foreach (var q in _board.Places) used.Add(q.Row * 3 + q.Col);
            int[] order = { 1, 0, 2, 4, 3, 5, 7, 6, 8 };
            int cell = 1;
            foreach (int c in order) if (!used.Contains(c)) { cell = c; break; }
            p = new CrowdBoardPlace { GeneralId = gid, Row = cell / 3, Col = cell % 3, Stance = CrowdStances.Guard };
            _board.Places.Add(p);
            return p;
        }

        public override void Build()
        {
            var ctx = Ctx;
            var fd = ctx.Territory?.Flags;
            string map = Game.Flow.FlagBattles.MapOf(fd, _flag);
            var (_, body, footer, _) = Ui.TitledPage(Root, T("ui.defBoard.title"), () => App.ShowMain("territory"), 120);
            var list = body; // 페이지 몸통이 이미 스크롤 목록 (2026-11-28 캡처: 안에 목록을 또 넣으면 빈 화면)
            InfoButton.Add(Root, T("ui.defBoard.note"));

            // ---- 전장 · 함정 ----
            var mapCard = Ui.Card(list, "Map", 16, 8);
            Ui.Label(mapCard, T("ui.defBoard.mapLine", FlagMaps.Name(ctx, map), _board.Traps.Count, fd?.BoardTrapsMax ?? 4), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            var kinds = Ui.Row(mapCard, "TrapKinds", 64, 8);
            foreach (var k in new[] { CrowdTrapKinds.Pit, CrowdTrapKinds.Slow })
            {
                string kind = k;
                Ui.Size(Ui.Button(kinds, T("ui.test.traps." + kind), () => { _trapKind = kind; Rebuild(); }, 64, _trapKind == kind ? Palette.ButtonActive : Palette.Button, 22), flexibleWidth: 1);
            }
            Ui.Size(Ui.Button(kinds, T("ui.defBoard.clearTraps"), () => { _board.Traps.Clear(); Rebuild(); }, 64, Palette.Button, 22), flexibleWidth: 1);
            var holder = Ui.Rect(mapCard, "MapHolder");
            Ui.Size(holder, 560);
            _mapBox = Ui.Rect(holder, "MapBox");
            _mapBox.anchorMin = _mapBox.anchorMax = new Vector2(0.5f, 0.5f);
            _mapBox.sizeDelta = new Vector2(336f, 560f);
            var raw = _mapBox.gameObject.AddComponent<RawImage>();
            try
            {
                var md = Game.Data.Defs.MapDef.Load(ctx.Data, map);
                raw.texture = Game.Unity.Battle3D.FieldTexture.Preview(md);
                var rules = CrowdRules.Load(ctx.Data);
                var cs = CrowdTerrain.CellSizeFor(md, rules.MapFieldWidth);
                _fieldW = cs * md.Width; _fieldD = cs * md.Height;
                _mapBox.sizeDelta = new Vector2(560f * md.Width / md.Height, 560f);
            }
            catch (System.Exception) { raw.color = new Color(0.45f, 0.58f, 0.3f); _fieldW = Fix.FromInt(60); _fieldD = Fix.FromInt(100); }
            var tap = _mapBox.gameObject.AddComponent<TapCatcher>();
            tap.OnTap = OnMapTap;
            // 깃발 쪽 (위) 표시 · 놓은 함정
            var flagMark = Ui.Label(_mapBox, "⚑ " + T("ui.defBoard.flagSide"), 20, TextAnchor.UpperCenter, Color.white);
            flagMark.rectTransform.anchorMin = new Vector2(0, 1); flagMark.rectTransform.anchorMax = Vector2.one;
            flagMark.rectTransform.offsetMin = new Vector2(0, -34); flagMark.rectTransform.offsetMax = Vector2.zero;
            flagMark.raycastTarget = false;
            foreach (var t in _board.Traps) TrapDot(t);

            // ---- 장수 자리 ----
            var gen = Ui.Card(list, "Generals", 16, 10);
            Ui.Label(gen, T("ui.defBoard.generals"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            var ids = Game.Flow.TerritoryFlags.GeneralsOf(_flag);
            _board.Places.RemoveAll(p => !ids.Contains(p.GeneralId));
            for (int i = 0; i < ids.Count; i++)
            {
                var p = PlaceOf(ids[i], i);
                GeneralRow(gen, p);
            }

            var save = Ui.PrimaryButton(footer, T("ui.defBoard.save"), Save, 96, 30);
            Ui.Size(save, flexibleWidth: 1);
            Ui.SetPrimaryEnabled(save, Ctx.OnlineEnabled);
        }

        private void GeneralRow(RectTransform parent, CrowdBoardPlace p)
        {
            var card = Ui.Card(parent, "G_" + p.GeneralId, 12, 8);
            var head = Ui.Row(card, "Head", 56, 8);
            var n = Ui.Label(head, Ctx.GeneralName(p.GeneralId), 26, TextAnchor.MiddleLeft);
            n.fontStyle = FontStyle.Bold;
            Ui.Size(n, 56, -1, -1, 1);
            Ui.Size(Ui.Label(head, T("ui.board.spot." + p.Row + p.Col), 22, TextAnchor.MiddleRight, Palette.TextDim), 56, 200).flexibleWidth = 0;
            // 3 × 3 칸 (위 줄 = 앞, 공격이 오는 쪽)
            var grid = Ui.Rect(card, "Grid");
            Ui.Size(grid, 3 * 52 + 2 * 6);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(96, 52); gl.spacing = new Vector2(6, 6); gl.childAlignment = TextAnchor.MiddleCenter;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 3;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    int row = r, col = c;
                    var other = _board.Places.Find(x => x != p && x.Row == row && x.Col == col);
                    bool here = p.Row == row && p.Col == col;
                    string label = here ? "★" : other != null ? Ctx.GeneralName(other.GeneralId).Substring(0, 1) : "";
                    var b = Ui.Button(grid, label, () =>
                    {
                        if (other != null) { other.Row = p.Row; other.Col = p.Col; } // 자리 바꾸기
                        p.Row = row; p.Col = col;
                        Rebuild();
                    }, 52, here ? Palette.ButtonActive : other != null ? Palette.Button : new Color(Palette.Button.r, Palette.Button.g, Palette.Button.b, 0.45f), 22);
                }
            var st = Ui.Row(card, "Stance", 60, 8);
            Ui.Size(Ui.Label(st, T("ui.defBoard.stance"), 22), width: 110);
            foreach (var s in CrowdStances.All)
            {
                string v = s;
                Ui.Size(Ui.Button(st, T("ui.defBoard.stance." + v), () => { p.Stance = v; Rebuild(); }, 60, p.Stance == v ? Palette.ButtonActive : Palette.Button, 22), flexibleWidth: 1);
            }
            var am = Ui.Row(card, "Ambush", 60, 8);
            Ui.Size(Ui.Label(am, T("ui.test.ambush"), 22), width: 110);
            Ui.Size(Ui.Button(am, T("ui.test.ambush.off"), () => { p.Ambush = false; Rebuild(); }, 60, p.Ambush ? Palette.Button : Palette.ButtonActive, 22), flexibleWidth: 1);
            Ui.Size(Ui.Button(am, T("ui.test.ambush.on"), () => { p.Ambush = true; Rebuild(); }, 60, p.Ambush ? Palette.ButtonActive : Palette.Button, 22), flexibleWidth: 1);
        }

        private void Rebuild() => App.Show(new FlagBoardScreen(_flag, _board));

        private void TrapDot(BoardTrap t)
        {
            if (_fieldW <= Fix.Zero || _fieldD <= Fix.Zero) return;
            float u = (float)(t.X / _fieldW).ToDisplayDouble(), v = (float)(t.Y / _fieldD).ToDisplayDouble();
            var dot = Ui.Rect(_mapBox, "Trap");
            dot.anchorMin = dot.anchorMax = new Vector2(u, v);
            dot.sizeDelta = new Vector2(30f, 30f);
            var img = Ui.Background(dot, t.Kind == CrowdTrapKinds.Pit ? new Color(0.25f, 0.16f, 0.08f) : new Color(0.75f, 0.78f, 0.82f));
            img.sprite = UiShapes.Circle;
            img.raycastTarget = false;
            var l = Ui.Label(dot, t.Kind == CrowdTrapKinds.Pit ? "○" : "✱", 20, TextAnchor.MiddleCenter, Color.white);
            Ui.Fill(l.rectTransform);
            l.raycastTarget = false;
        }

        private void OnMapTap(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapBox, screen, null, out var local)) return;
            var r = _mapBox.rect;
            float u = Mathf.Clamp01((local.x - r.xMin) / r.width), v = Mathf.Clamp01((local.y - r.yMin) / r.height);
            var x = Fix.FromDecimal((decimal)System.Math.Round(u * (float)_fieldW.ToDisplayDouble(), 1));
            var y = Fix.FromDecimal((decimal)System.Math.Round(v * (float)_fieldD.ToDisplayDouble(), 1));
            // 가까운 함정을 누르면 빼기
            var near = _board.Traps.Find(t => (Abs(t.X - x) + Abs(t.Y - y)) < Fix.FromInt(3));
            if (near != null) { _board.Traps.Remove(near); Rebuild(); return; }
            int max = Ctx.Territory?.Flags?.BoardTrapsMax ?? 4;
            if (_board.Traps.Count >= max) { Toast.Show(T("ui.defBoard.trapsFull", max), bad: true); return; }
            _board.Traps.Add(new BoardTrap { Kind = _trapKind, X = x, Y = y });
            Rebuild();
        }

        private static Fix Abs(Fix f) => f < Fix.Zero ? -f : f;

        private void Save()
        {
            var end = Loading.Begin();
            Ctx.SetFlagBoard(new FlagBoardRequest { FlagKey = _flag.RegionId, Board = _board }, r =>
            {
                end();
                if (r == null) { Toast.Show(T("ui.hire.failed"), bad: true); return; }
                if (!r.Ok) { Toast.Show(T("ui.terr.plant." + r.Reason), bad: true); return; }
                if (r.Flag != null && TerritoryTabView.Owners != null) TerritoryTabView.Owners[r.Flag.RegionId] = r.Flag;
                TerritoryTabView.Notice = T("ui.defBoard.saved");
                App.ShowMain("territory");
            });
        }

        /// <summary>그림 위 누름 (함정 놓기).</summary>
        private sealed class TapCatcher : MonoBehaviour, IPointerClickHandler
        {
            public System.Action<Vector2> OnTap;
            public void OnPointerClick(PointerEventData e) { if (!e.dragging) OnTap?.Invoke(e.position); }
        }
    }
}
