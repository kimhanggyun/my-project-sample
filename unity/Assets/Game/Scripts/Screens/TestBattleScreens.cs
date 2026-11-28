using System.Collections.Generic;
using System.Text;
using Game.Battle.Model.Crowd;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>테스트 탭 설정 (기기에 기억 — 다시 들어와도 이어서). 편마다 3×3 칸의 부대, 적 명령, 맵.</summary>
    public static class TestBattleSetup
    {
        private const string Key = "testBattle.setup.v1";
        public static readonly List<CrowdUnitSpec> Ally = new List<CrowdUnitSpec>();
        public static readonly List<CrowdUnitSpec> Enemy = new List<CrowdUnitSpec>();
        /// <summary>
        /// 우리가 수비 (R-139 공수 교대, 2026-11-22 사용자): 맵을 위아래로 뒤집어 우리가 지키는 쪽(고지 · 강 건너편)에 서고, 적이 전진해 온다.
        /// 공격이면 맵 그대로 · 적은 제자리. (예전 "적 명령 [제자리][전진]"을 합침 — 저장 값도 같은 자리)
        /// </summary>
        public static bool AllyDefends;
        /// <summary>시작 배속 1 · 2 · 4 (2026-11-23 사용자).</summary>
        public static int Speed = 1;
        /// <summary>처음 고른 맵 = 평원 (모두 평지, 60 × 100 — 2026-11-22 사용자: 60 × 60 평지 카드 뺌).</summary>
        public const string DefaultMap = "crowd_plain";
        public static string Map = DefaultMap;
        /// <summary>깃발전 (R-163, 2026-11-28): 수비 편 총대장 자리에 진지 깃발 — 점령 · 시간 끝 = 수비 승. 끄면 예전처럼 전멸 · 무너짐까지.</summary>
        public static bool FlagBattle;
        /// <summary>수비 함정 (R-165): none · pit 구덩이 · slow 마름쇠 · both 둘 다 (수비 총대장 앞에 자동으로 놓음).</summary>
        public static string Traps = "none";
        public static readonly string[] TrapModes = { "none", "pit", "slow", "both" };
        private static bool _loaded;

        public static List<CrowdUnitSpec> Of(bool ally) => ally ? Ally : Enemy;

        public static void Load(int defaultCount)
        {
            if (_loaded) return;
            _loaded = true;
            string s = null;
            try { s = PlayerPrefs.GetString(Key, null); } catch { }
            if (!string.IsNullOrEmpty(s) && Parse(s)) return;
            Ally.Clear(); Enemy.Clear();
            Ally.Add(new CrowdUnitSpec { Row = 1, Col = 1, Commander = true, Count = defaultCount });
            Enemy.Add(new CrowdUnitSpec { Row = 1, Col = 1, Commander = true, Count = defaultCount });
        }

        public static void Save()
        {
            var sb = new StringBuilder();
            sb.Append(AllyDefends ? "1" : "0").Append(':').Append(Speed).Append(':').Append(FlagBattle ? "1" : "0").Append(':').Append(Traps).Append('|').Append(Map);
            foreach (var (team, list) in new[] { ("a", Ally), ("e", Enemy) })
                foreach (var u in list)
                    sb.Append('|').Append(team).Append(',').Append(u.Row).Append(',').Append(u.Col).Append(',').Append(u.Troop).Append(',')
                      .Append(u.Formation).Append(',').Append(u.Count).Append(',').Append(u.Commander ? 1 : 0).Append(',').Append(u.General).Append(',').Append(u.Behavior).Append(',').Append(u.Ambush ? 1 : 0);
            try { PlayerPrefs.SetString(Key, sb.ToString()); PlayerPrefs.Save(); } catch { }
        }

        private static bool Parse(string s)
        {
            try
            {
                var parts = s.Split('|');
                var head = parts[0].Split(':');
                AllyDefends = head[0] == "1";
                Speed = head.Length > 1 && int.TryParse(head[1], out var sp) && (sp == 1 || sp == 2 || sp == 4) ? sp : 1;
                FlagBattle = head.Length > 2 && head[2] == "1";
                Traps = head.Length > 3 && System.Array.IndexOf(TrapModes, head[3]) >= 0 ? head[3] : "none";
                Map = System.Array.IndexOf(TestBattleTabView.Maps, parts[1]) >= 0 ? parts[1] : DefaultMap; // 예전 "plain"(60 × 60)은 평원으로
                Ally.Clear(); Enemy.Clear();
                for (int i = 2; i < parts.Length; i++)
                {
                    var f = parts[i].Split(',');
                    var u = new CrowdUnitSpec { Row = int.Parse(f[1]), Col = int.Parse(f[2]), Troop = f[3], Formation = f[4], Count = int.Parse(f[5]), Commander = f[6] == "1", General = f.Length > 7 ? f[7] : "standard",
                        Behavior = f.Length > 8 && !string.IsNullOrEmpty(f[8]) ? f[8] : CrowdUnitSpec.Behaviors.Hold, Ambush = f.Length > 9 && f[9] == "1" };
                    (f[0] == "a" ? Ally : Enemy).Add(u);
                }
                return Ally.Count > 0 && Enemy.Count > 0;
            }
            catch { Ally.Clear(); Enemy.Clear(); return false; }
        }

        public static CrowdUnitSpec At(bool ally, int row, int col) => Of(ally).Find(u => u.Row == row && u.Col == col);

        /// <summary>총대장이 꼭 하나 (빼거나 옮긴 뒤 맞춤).</summary>
        public static void FixCommander(bool ally)
        {
            var list = Of(ally);
            int n = 0;
            foreach (var u in list) if (u.Commander) { if (n++ > 0) u.Commander = false; }
            if (n == 0 && list.Count > 0)
            {
                // 가운데 칸 → 앞줄 가운데 → 첫 부대
                var pick = list.Find(u => u.Row == 1 && u.Col == 1) ?? list.Find(u => u.Col == 1) ?? list[0];
                pick.Commander = true;
            }
        }
    }

    /// <summary>
    /// 테스트 탭 (R-131, 2026-11-21 사용자): 샘플전투2(병사 단위 전투)를 3×3 칸으로 짜서 싸워 본다.
    /// 위 = 적 (뒷줄이 위, 앞줄이 아래 — 전장처럼 마주 봄), 가운데 = 맵 (좌우로 넘김), 아래 = 아군 (앞줄이 위).
    /// 빈 칸 [+]를 누르면 기본 부대(보병 · 일자 · 60명)를 넣고 선택 판, 부대 칸을 누르면 선택 판: 병종 · 진형 · 병사 수 · 총대장 · 빼기.
    /// 양쪽에 부대가 하나 이상이면 [전투 시작].
    /// </summary>
    public sealed class TestBattleTabView : ITabView
    {
        private GameApp _app;
        private RectTransform _root;
        private static readonly string[] Troops = { "infantry", "spearman", "archer", "cavalry" };
        /// <summary>맵 카드 (R-133): 평지(맵 없음) + 기존 전장 맵 (수상전 맵은 뺌).</summary>
        /// <summary>샘플전투2 맵 (R-135, 48 × 80 — tools/crowd_maps.js로 만듦). 옛 전투 맵은 옛 전투 · 시험이 그대로 씀.</summary>
        public static readonly string[] Maps = { "crowd_plain", "crowd_highland", "crowd_gorge", "crowd_river", "crowd_marsh", "crowd_forest", "crowd_pass", "crowd_siege", "crowd_siege_plain", "crowd_siege_river", "crowd_siege_mountain" };

        /// <summary>
        /// 맵 분류 (2026-11-28 사용자: [야전 · 성새전 · 수상전]으로 나눠 고르기). 맵 데이터 battleType은 성 맵도 field라 여기서 나눔 (전투 규칙과 상관없음).
        /// 수상전 맵은 아직 없음 → 그 분류는 빈 목록.
        /// </summary>
        public static readonly string[] MapCategories = { "field", "siege", "naval" };
        public static string CategoryOf(string mapId) => mapId != null && mapId.StartsWith("crowd_siege") ? "siege" : "field";

        /// <summary>고른 분류 (null = 지금 맵의 분류) · 화면을 다시 그려도 그 자리인 스크롤 (세로 · 분류마다 가로).</summary>
        private static string _mapCat;
        /// <summary>캡처 투어: 맵 분류 고르기 (null = 지금 맵의 분류).</summary>
        internal static string MapCategory { set => _mapCat = value; }
        private static float _scrollY = 1f;
        private static readonly Dictionary<string, float> _mapScrollX = new Dictionary<string, float>();

        public static string MapName(Game.Unity.Boot.GameContext ctx, string id) => id == "plain" ? ctx.T("ui.test.map.plain") : ctx.T("map." + id);

        public void Build(GameApp app, RectTransform root)
        {
            _app = app;
            _root = root;
            var ctx = app.Context;
            TestBattleSetup.Load(CrowdRules.Load(ctx.Data).GridDefaultCount);
            Redraw();
        }

        private string T(string key, params object[] args) => _app.Context.T(key, args);

        private void Redraw()
        {
            Ui.Clear(_root);
            var list = Ui.ScrollList(_root, "List", 14, 20);
            var scroll = Ui.Fill((RectTransform)list.parent.parent);
            var vScroll = scroll.GetComponent<ScrollRect>();
            scroll.offsetMin = new Vector2(0f, StartBarHeight); // 아래는 [전투 시작] 자리 (늘 보임, 2026-11-21 사용자)
            InfoButton.Add(_root, T("ui.test.note"));

            // 맨 위 (2026-11-23 사용자): 우리 편 [공격][수비], 그 아래 시작 배속 [1배속][2배속][4배속]
            // 공수 (R-139): 수비면 맵을 뒤집어 우리가 지키는 쪽
            var order = Ui.Row(list, "AttackDefend", 72, 8);
            Ui.Size(Ui.Label(order, T("ui.test.side"), 24), width: 160);
            Ui.Size(Ui.Button(order, T("ui.test.attack"), () => { TestBattleSetup.AllyDefends = false; TestBattleSetup.Save(); Redraw(); }, 72,
                TestBattleSetup.AllyDefends ? Palette.Button : Palette.ButtonActive, 24), flexibleWidth: 1);
            Ui.Size(Ui.Button(order, T("ui.test.defend"), () => { TestBattleSetup.AllyDefends = true; TestBattleSetup.Save(); Redraw(); }, 72,
                TestBattleSetup.AllyDefends ? Palette.ButtonActive : Palette.Button, 24), flexibleWidth: 1);
            var speed = Ui.Row(list, "Speed", 72, 8);
            Ui.Size(Ui.Label(speed, T("ui.test.speed"), 24), width: 160);
            foreach (int sp in new[] { 1, 2, 4 })
            {
                int v = sp;
                Ui.Size(Ui.Button(speed, T("ui.test.speedN", v), () => { TestBattleSetup.Speed = v; TestBattleSetup.Save(); Redraw(); }, 72,
                    TestBattleSetup.Speed == v ? Palette.ButtonActive : Palette.Button, 24), flexibleWidth: 1);
            }
            // 전투 방식 (R-163): [전멸전][깃발전] · 깃발전이면 수비 함정 [없음][구덩이][마름쇠][둘 다]
            var mode = Ui.Row(list, "Mode", 72, 8);
            Ui.Size(Ui.Label(mode, T("ui.test.mode"), 24), width: 160);
            Ui.Size(Ui.Button(mode, T("ui.test.mode.rout"), () => { TestBattleSetup.FlagBattle = false; TestBattleSetup.Save(); Redraw(); }, 72,
                TestBattleSetup.FlagBattle ? Palette.Button : Palette.ButtonActive, 24), flexibleWidth: 1);
            Ui.Size(Ui.Button(mode, T("ui.test.mode.flag"), () => { TestBattleSetup.FlagBattle = true; TestBattleSetup.Save(); Redraw(); }, 72,
                TestBattleSetup.FlagBattle ? Palette.ButtonActive : Palette.Button, 24), flexibleWidth: 1);
            var traps = Ui.Row(list, "Traps", 72, 8);
            Ui.Size(Ui.Label(traps, T("ui.test.traps"), 24), width: 160);
            foreach (var tm in TestBattleSetup.TrapModes)
            {
                string v = tm;
                Ui.Size(Ui.Button(traps, T("ui.test.traps." + v), () => { TestBattleSetup.Traps = v; TestBattleSetup.Save(); Redraw(); }, 72,
                    TestBattleSetup.Traps == v ? Palette.ButtonActive : Palette.Button, 22), flexibleWidth: 1);
            }
            Grid(list, false);
            // 맵: 좌우로 넘기는 카드 (지금은 평지 하나)
            // 맵 제목 + [보기]: 고른 맵을 부대 없이 둘러보기 (2026-11-22 사용자)
            var mapHead = Ui.Row(list, "MapHead", 64, 12);
            Ui.Size(Ui.Label(mapHead, T("ui.test.map"), Ui.FontBody), flexibleWidth: 1);
            Ui.Size(Ui.Button(mapHead, T("ui.test.view"), () => _app.Show(new MapViewScreen(TestBattleSetup.Map, TestBattleSetup.AllyDefends)), 64, Palette.Button, 24), width: 160);
            // 맵 분류 [야전 · 성새전 · 수상전] (2026-11-28 사용자) — 소분류, 고른 분류의 맵만
            string cat = _mapCat ?? CategoryOf(TestBattleSetup.Map);
            var cats = new List<(string, string)>();
            foreach (var c in MapCategories) cats.Add((c, T("situation." + c)));
            Ui.MinorTabs(list, cats, cat, id => { _mapCat = id; Redraw(); });
            // 맵 카드 (R-133): 위 = 적 쪽 미리보기 + 이름
            var shown = new List<string>();
            foreach (var m in Maps) if (CategoryOf(m) == cat) shown.Add(m);
            if (shown.Count == 0) Ui.EmptyNote(list, T("ui.test.noMaps", T("situation." + cat)), 196);
            var maps = shown.Count == 0 ? null : Ui.HScrollRow(list, "Maps", 196);
            foreach (var m in shown)
            {
                string id = m;
                var b = Ui.Button(maps, MapName(_app.Context, id), () => { TestBattleSetup.Map = id; TestBattleSetup.Save(); Redraw(); }, 180,
                    TestBattleSetup.Map == id ? Palette.ButtonActive : Palette.Button, 22);
                Ui.Size(b, 180, 150);
                var label = b.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.LowerCenter;
                var lr = label.rectTransform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(1f, 0f); lr.pivot = new Vector2(0.5f, 0f);
                lr.offsetMin = new Vector2(4f, 6f); lr.offsetMax = new Vector2(-4f, 40f);
                var pic = Ui.Rect(b.transform, "Preview");
                pic.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                pic.anchorMin = pic.anchorMax = new Vector2(0.5f, 1f);
                pic.pivot = new Vector2(0.5f, 1f);
                pic.anchoredPosition = new Vector2(0f, -8f);
                pic.sizeDelta = new Vector2(76f, 124f);
                var raw = pic.gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                if (id == "plain") raw.color = new Color(0.45f, 0.58f, 0.3f);
                else
                {
                    try { raw.texture = Game.Unity.Battle3D.FieldTexture.Preview(Game.Data.Defs.MapDef.Load(_app.Context.Data, id)); }
                    catch (System.Exception) { raw.color = Color.gray; }
                }
            }
            Grid(list, true);

            bool ready = TestBattleSetup.Ally.Count > 0 && TestBattleSetup.Enemy.Count > 0;
            if (!ready) Ui.Label(list, T("ui.test.needBoth"), 22, TextAnchor.MiddleCenter, Palette.Bad);
            // [전투 시작]: 목록과 따로 맨 아래에 붙여 둔다 (스크롤해도 늘 보임)
            var bar = Ui.Rect(_root, "StartBar");
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.offsetMin = new Vector2(20f, 10f);
            bar.offsetMax = new Vector2(-20f, StartBarHeight - 10f);
            var start = Ui.Button(bar, T("ui.test.start"), Start, StartBarHeight - 20f, Palette.ButtonActive, 32);
            Ui.Fill((RectTransform)start.transform);
            if (!ready) Ui.SetEnabled(start, false);

            // 스크롤 자리 그대로 (2026-11-28 사용자: 맵을 누르면 줄이 맨 앞으로 돌아감 — 화면을 통째로 다시 그려서)
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            Canvas.ForceUpdateCanvases();
            if (vScroll != null)
            {
                vScroll.verticalNormalizedPosition = _scrollY;
                vScroll.onValueChanged.AddListener(v => _scrollY = v.y);
            }
            var hScroll = maps != null ? maps.GetComponentInParent<ScrollRect>() : null;
            if (hScroll != null)
            {
                if (_mapScrollX.TryGetValue(cat, out var x)) hScroll.horizontalNormalizedPosition = x;
                else
                {
                    // 처음: 고른 맵이 보이게
                    int at = shown.IndexOf(TestBattleSetup.Map);
                    hScroll.horizontalNormalizedPosition = at <= 0 || shown.Count <= 1 ? 0f : Mathf.Clamp01((float)at / (shown.Count - 1));
                }
                string key = cat;
                hScroll.onValueChanged.AddListener(v => _mapScrollX[key] = v.x);
            }
        }

        private const float StartBarHeight = 116f;

        /// <summary>한 편의 3×3 칸. 적은 뒷줄이 위 (앞줄이 아래 = 우리 쪽), 아군은 앞줄이 위 (적 쪽).</summary>
        private void Grid(RectTransform list, bool ally)
        {
            var card = Ui.Card(list, ally ? "AllyGrid" : "EnemyGrid", 16, 8);
            var units = TestBattleSetup.Of(ally);
            int soldiers = 0;
            foreach (var u in units) soldiers += u.Count;
            Ui.Label(card, T(ally ? "ui.test.ally" : "ui.test.enemy", units.Count, soldiers), Ui.FontBody, TextAnchor.MiddleLeft, ally ? Palette.Ally : Palette.Enemy);
            for (int r = 0; r < 3; r++)
            {
                int row = ally ? r : 2 - r;
                var line = Ui.Row(card, "Row" + row, 116, 12);
                for (int col = 0; col < 3; col++)
                {
                    int c = col;
                    var u = TestBattleSetup.At(ally, row, c);
                    string text = u == null ? "+"
                        : (u.Commander ? "★ " : "") + T("troop." + u.Troop) + " · " + T("ui.crowd.general." + u.General) + "\n" + u.Count
                          + (!ally && u.Behavior != CrowdUnitSpec.Behaviors.Hold ? " · " + T("ui.test.behavior." + u.Behavior) : "") // 진형은 아이콘 (R-141), 적 시작 행동
                          + (u.Ambush ? " · " + T("ui.test.ambushShort") : ""); // 매복 (R-165)
                    var team = ally ? new Color(0.24f, 0.36f, 0.6f) : new Color(0.58f, 0.24f, 0.2f);
                    // 빈 칸 = 팀 색을 아주 옅게 + 얇은 같은 색 테두리 (2026-11-27 사용자: 9칸 영역이 구분 안 됨)
                    var b = Ui.Button(line, text, () => OnCell(ally, row, c), 116, u == null ? new Color(team.r, team.g, team.b, 0.14f) : team, u == null ? 40 : 24);
                    Ui.Size(b, flexibleWidth: 1);
                    if (u == null)
                    {
                        b.GetComponentInChildren<Text>().color = new Color(team.r, team.g, team.b, 0.75f);
                        var ring = Ui.Fill(Ui.Rect(b.transform, "Ring"));
                        var ri = ring.gameObject.AddComponent<Image>();
                        ri.sprite = UiKit.Ring(Mathf.Max(4, Mathf.RoundToInt(UiStyle.CornerRadius)), 3);
                        ri.type = Image.Type.Sliced; ri.fillCenter = false; ri.raycastTarget = false;
                        ri.color = new Color(team.r, team.g, team.b, 0.38f);
                    }
                    if (u != null)
                    {
                        // 칸 오른쪽에 진형 아이콘 (R-141)
                        var ic = FormationIcon.Add(b.transform, u.Formation, new Color(1f, 0.92f, 0.86f), 44f, 4f);
                        ic.anchorMin = ic.anchorMax = new Vector2(1f, 0.5f);
                        ic.pivot = new Vector2(1f, 0.5f);
                        ic.anchoredPosition = new Vector2(-8f, 0f);
                    }
                }
            }
            Ui.Label(card, T(ally ? "ui.test.allyHint" : "ui.test.enemyHint"), 20, TextAnchor.MiddleCenter, Palette.TextDim);
        }

        /// <summary>칸 이름: 가운데 줄 = 좌군 · 중군 · 우군, 앞 · 뒤 줄 = "앞 좌군" · "뒤 우군" (캡처: "중 중"이 어색).</summary>
        public static string CellName(Game.Unity.Boot.GameContext ctx, string row, string col)
        {
            string name = ctx.T("ui.sample2.place." + col);
            return row == "middle" ? name : ctx.T("ui.test.row." + row) + " " + name;
        }

        private void OnCell(bool ally, int row, int col)
        {
            var u = TestBattleSetup.At(ally, row, col);
            if (u == null)
            {
                if (TestBattleSetup.Of(ally).Count >= 9) return;
                u = new CrowdUnitSpec { Row = row, Col = col, Count = CrowdRules.Load(_app.Context.Data).GridDefaultCount };
                TestBattleSetup.Of(ally).Add(u);
                TestBattleSetup.FixCommander(ally);
                TestBattleSetup.Save();
                Redraw();
            }
            OpenSheet(ally, u);
        }

        /// <summary>선택 판: 병종 · 진형 · 병사 수 · 총대장 · 빼기.</summary>
        private void OpenSheet(bool ally, CrowdUnitSpec u)
        {
            string title = T(ally ? "ui.test.sheetAlly" : "ui.test.sheetEnemy", CellName(_app.Context, CrowdUnitSpec.RowIds[u.Row], CrowdUnitSpec.ColIds[u.Col]));
            var (content, close) = Ui.BottomSheet(_root, title, ally ? 840 : 940);
            Ui.Column(content, 12, 0);
            void Changed() { TestBattleSetup.Save(); close(); Redraw(); OpenSheet(ally, u); }

            var tr = Ui.Row(content, "Troops", 84, 8);
            foreach (var id in Troops)
            {
                string t = id;
                Ui.Size(Ui.Button(tr, T("troop." + id), () => { u.Troop = t; Changed(); }, 84, u.Troop == id ? Palette.ButtonActive : Palette.Button, 26), flexibleWidth: 1);
            }
            var fr = Ui.Row(content, "Formations", 84, 8);
            foreach (var id in CrowdFormations.All)
            {
                string f = id;
                var fb = Ui.Button(fr, "", () => { u.Formation = f; Changed(); }, 84, u.Formation == id ? Palette.ButtonActive : Palette.Button, 26);
                Ui.Size(fb, flexibleWidth: 1);
                FormationIcon.Add(fb.transform, id, new Color(0.85f, 0.18f, 0.16f), 56f, 5f); // 진형 아이콘 (R-141)
            }
            // 장수 성향 (R-138): 맹장 = 선봉에서 직접 싸움 · 장수 = 대열 뒤 · 책사 = 멀리 뒤 (나중에 캠페인 장수 · 병종 적성)
            var gr = Ui.Row(content, "General", 84, 8);
            Ui.Size(Ui.Label(gr, T("ui.test.general"), 26), width: 160);
            foreach (var id in GeneralPresets)
            {
                string g = id;
                Ui.Size(Ui.Button(gr, T("ui.crowd.general." + id), () => { u.General = g; Changed(); }, 84, u.General == id ? Palette.ButtonActive : Palette.Button, 26), flexibleWidth: 1);
            }
            if (!ally)
            {
                // 적 시작 행동 (2026-11-23 사용자): [위임][진군][대기], 기본 대기
                var br = Ui.Row(content, "Behavior", 84, 8);
                Ui.Size(Ui.Label(br, T("ui.test.behavior"), 26), width: 160);
                foreach (var id in new[] { CrowdUnitSpec.Behaviors.Delegate, CrowdUnitSpec.Behaviors.Advance, CrowdUnitSpec.Behaviors.Hold })
                {
                    string b = id;
                    Ui.Size(Ui.Button(br, T("ui.test.behavior." + id), () => { u.Behavior = b; Changed(); }, 84, u.Behavior == id ? Palette.ButtonActive : Palette.Button, 26), flexibleWidth: 1);
                }
            }
            // 매복 (R-165): 숲 · 풀에 서면 적이 가까이 올 때까지 안 보이고, 걸어 들어오면 기습
            var am = Ui.Row(content, "Ambush", 84, 8);
            Ui.Size(Ui.Label(am, T("ui.test.ambush"), 26), width: 160);
            Ui.Size(Ui.Button(am, T("ui.test.ambush.off"), () => { u.Ambush = false; Changed(); }, 84, u.Ambush ? Palette.Button : Palette.ButtonActive, 26), flexibleWidth: 1);
            Ui.Size(Ui.Button(am, T("ui.test.ambush.on"), () => { u.Ambush = true; Changed(); }, 84, u.Ambush ? Palette.ButtonActive : Palette.Button, 26), flexibleWidth: 1);
            int max = CrowdRules.Load(_app.Context.Data).GridMaxCount;
            var cr = Ui.Row(content, "Count", 84, 8);
            Ui.Size(Ui.Label(cr, T("ui.test.count"), 26), width: 160);
            Ui.Size(Ui.Button(cr, "−100", () => { u.Count = Mathf.Max(10, u.Count - 100); Changed(); }, 84, Palette.Button, 26), flexibleWidth: 1); // 1000명까지 (R-157)
            Ui.Size(Ui.Button(cr, "−10", () => { u.Count = Mathf.Max(10, u.Count - 10); Changed(); }, 84, Palette.Button, 26), flexibleWidth: 1);
            Ui.Size(Ui.Label(cr, u.Count.ToString(), 32, TextAnchor.MiddleCenter), flexibleWidth: 1);
            Ui.Size(Ui.Button(cr, "+10", () => { u.Count = Mathf.Min(max, u.Count + 10); Changed(); }, 84, Palette.Button, 26), flexibleWidth: 1);
            Ui.Size(Ui.Button(cr, "+100", () => { u.Count = Mathf.Min(max, u.Count + 100); Changed(); }, 84, Palette.Button, 26), flexibleWidth: 1);
            var ar = Ui.Row(content, "Actions", 84, 8);
            var cmd = Ui.Button(ar, u.Commander ? T("ui.test.isCommander") : T("ui.test.makeCommander"), () =>
            {
                foreach (var o in TestBattleSetup.Of(ally)) o.Commander = false;
                u.Commander = true;
                Changed();
            }, 84, u.Commander ? Palette.ButtonActive : Palette.Button, 26);
            Ui.Size(cmd, flexibleWidth: 1);
            Ui.Size(Ui.Button(ar, T("ui.test.remove"), () =>
            {
                TestBattleSetup.Of(ally).Remove(u);
                TestBattleSetup.FixCommander(ally);
                TestBattleSetup.Save();
                close();
                Redraw();
            }, 84, Palette.Danger, 26), flexibleWidth: 1);
            Ui.Size(Ui.Button(ar, T("ui.radial.close"), close, 84, Palette.Button, 26), flexibleWidth: 1);
        }

        /// <summary>장수 성향 고르기 (crowd.json generalPresets).</summary>
        private static readonly string[] GeneralPresets = { "warrior", "standard", "strategist" };

        private void Start()
        {
            if (TestBattleSetup.Ally.Count == 0 || TestBattleSetup.Enemy.Count == 0) return;
            _app.Show(new SampleBattle2Screen(Setup(_app.Context), startNow: true, returnTab: "test", speed: TestBattleSetup.Speed));
        }

        /// <summary>지금 칸 설정으로 전투 설정 (사본 — 전투 중 설정 화면을 바꿔도 영향 없음).</summary>
        public static CrowdSetup Setup(Game.Unity.Boot.GameContext ctx)
        {
            List<CrowdUnitSpec> Copy(List<CrowdUnitSpec> l)
            {
                var r = new List<CrowdUnitSpec>();
                foreach (var u in l) r.Add(new CrowdUnitSpec { Row = u.Row, Col = u.Col, Troop = u.Troop, Formation = u.Formation, Count = u.Count, Commander = u.Commander, General = u.General, Behavior = u.Behavior, Ambush = u.Ambush });
                return r;
            }
            var setup = new CrowdSetup { AllyUnits = Copy(TestBattleSetup.Ally), EnemyUnits = Copy(TestBattleSetup.Enemy), AllyAdvance = false, EnemyAdvance = false, // 적은 부대마다 시작 행동으로
                Terrain = TerrainOf(ctx, TestBattleSetup.Map, TestBattleSetup.AllyDefends) };
            // 깃발전 (R-163): 수비 편 = [수비]면 우리, 아니면 적
            int defender = TestBattleSetup.AllyDefends ? CrowdBattle.Ally : CrowdBattle.Enemy;
            setup.FlagBattle = TestBattleSetup.FlagBattle;
            setup.FlagDefender = defender;
            if (TestBattleSetup.Traps != "none" && ctx != null) setup.Traps = TrapsBefore(ctx, setup, defender, TestBattleSetup.Traps);
            return setup;
        }

        /// <summary>
        /// 수비 함정 자동 배치 (테스트 탭, R-165): 수비 총대장 앞 (공격 쪽으로) 줄에 셋 — 구덩이는 14칸 앞, 마름쇠는 20칸 앞 (둘 다면 두 줄).
        /// 영지 수비판에서는 주인이 자리를 고른다.
        /// </summary>
        public static List<CrowdTrapSpec> TrapsBefore(Game.Unity.Boot.GameContext ctx, CrowdSetup setup, int defender, string mode)
        {
            var list = new List<CrowdTrapSpec>();
            CrowdBattle probe;
            try { probe = new CrowdBattle(CrowdRules.Load(ctx.Data), setup, ctx.Defs.TroopTypes, ctx.Defs.TroopCounters); }
            catch (System.Exception) { return list; }
            var c = probe.Commander(defender).Center;
            var foe = probe.Commander(1 - defender).Center;
            var dir = foe - c;
            var len = dir.Length;
            if (len <= Game.Foundation.Math.Fix.Zero) return list;
            dir = dir.Normalized();
            var right = new Game.Battle.Model.Geometry.Vec(dir.Y, -dir.X);
            void Line(string kind, int ahead)
            {
                foreach (int side in new[] { -8, 0, 8 })
                {
                    var p = c + dir * Game.Foundation.Math.Fix.FromInt(ahead) + right * Game.Foundation.Math.Fix.FromInt(side);
                    list.Add(new CrowdTrapSpec { Team = defender, Kind = kind, X = p.X, Y = p.Y });
                }
            }
            if (mode == "pit" || mode == "both") Line(CrowdTrapKinds.Pit, 14);
            if (mode == "slow" || mode == "both") Line(CrowdTrapKinds.Slow, 20);
            return list;
        }

        /// <summary>맵 id → 지형 (R-133, 평지 · 없는 맵이면 null = 평지 60 × 60).</summary>
        public static CrowdTerrain TerrainOf(Game.Unity.Boot.GameContext ctx, string mapId, bool flipped = false)
        {
            if (ctx == null || string.IsNullOrEmpty(mapId) || mapId == "plain") return null;
            try
            {
                var map = Game.Data.Defs.MapDef.Load(ctx.Data, mapId);
                return new CrowdTerrain(map, ctx.Defs.TerrainRules, ctx.Defs.TerrainCombat, CrowdTerrain.CellSizeFor(map, CrowdRules.Load(ctx.Data).MapFieldWidth), flipped);
            }
            catch (System.Exception) { return null; }
        }
    }
}
