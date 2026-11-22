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
        public static bool EnemyAdvance => AllyDefends;
        /// <summary>처음 고른 맵 = 평원 (모두 평지, 60 × 100 — 2026-11-22 사용자: 60 × 60 평지 카드 뺌).</summary>
        public const string DefaultMap = "crowd_plain";
        public static string Map = DefaultMap;
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
            sb.Append(AllyDefends ? "1" : "0").Append('|').Append(Map);
            foreach (var (team, list) in new[] { ("a", Ally), ("e", Enemy) })
                foreach (var u in list)
                    sb.Append('|').Append(team).Append(',').Append(u.Row).Append(',').Append(u.Col).Append(',').Append(u.Troop).Append(',')
                      .Append(u.Formation).Append(',').Append(u.Count).Append(',').Append(u.Commander ? 1 : 0).Append(',').Append(u.General);
            try { PlayerPrefs.SetString(Key, sb.ToString()); PlayerPrefs.Save(); } catch { }
        }

        private static bool Parse(string s)
        {
            try
            {
                var parts = s.Split('|');
                AllyDefends = parts[0] == "1";
                Map = System.Array.IndexOf(TestBattleTabView.Maps, parts[1]) >= 0 ? parts[1] : DefaultMap; // 예전 "plain"(60 × 60)은 평원으로
                Ally.Clear(); Enemy.Clear();
                for (int i = 2; i < parts.Length; i++)
                {
                    var f = parts[i].Split(',');
                    var u = new CrowdUnitSpec { Row = int.Parse(f[1]), Col = int.Parse(f[2]), Troop = f[3], Formation = f[4], Count = int.Parse(f[5]), Commander = f[6] == "1", General = f.Length > 7 ? f[7] : "standard" };
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
        public static readonly string[] Maps = { "crowd_plain", "crowd_highland", "crowd_gorge", "crowd_river", "crowd_marsh", "crowd_forest", "crowd_pass", "crowd_siege" };

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
            scroll.offsetMin = new Vector2(0f, StartBarHeight); // 아래는 [전투 시작] 자리 (늘 보임, 2026-11-21 사용자)
            InfoButton.Add(_root, T("ui.test.note"));

            Grid(list, false);
            // 공수 (R-139): 우리 [공격][수비] — 수비면 맵을 뒤집어 우리가 지키는 쪽, 적이 전진해 옴 (적 칸과 맵 사이)
            var order = Ui.Row(list, "AttackDefend", 72, 8);
            Ui.Size(Ui.Label(order, T("ui.test.side"), 24), width: 160);
            Ui.Size(Ui.Button(order, T("ui.test.attack"), () => { TestBattleSetup.AllyDefends = false; TestBattleSetup.Save(); Redraw(); }, 72,
                TestBattleSetup.AllyDefends ? Palette.Button : Palette.ButtonActive, 24), flexibleWidth: 1);
            Ui.Size(Ui.Button(order, T("ui.test.defend"), () => { TestBattleSetup.AllyDefends = true; TestBattleSetup.Save(); Redraw(); }, 72,
                TestBattleSetup.AllyDefends ? Palette.ButtonActive : Palette.Button, 24), flexibleWidth: 1);
            // 맵: 좌우로 넘기는 카드 (지금은 평지 하나)
            // 맵 제목 + [보기]: 고른 맵을 부대 없이 둘러보기 (2026-11-22 사용자)
            var mapHead = Ui.Row(list, "MapHead", 64, 12);
            Ui.Size(Ui.Label(mapHead, T("ui.test.map"), Ui.FontBody), flexibleWidth: 1);
            Ui.Size(Ui.Button(mapHead, T("ui.test.view"), () => _app.Show(new MapViewScreen(TestBattleSetup.Map, TestBattleSetup.AllyDefends)), 64, Palette.Button, 24), width: 160);
            // 맵 카드 (R-133): 위 = 적 쪽 미리보기 + 이름
            var maps = Ui.HScrollRow(list, "Maps", 196);
            foreach (var m in Maps)
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
                var line = Ui.Row(card, "Row" + row, 116, 8);
                for (int col = 0; col < 3; col++)
                {
                    int c = col;
                    var u = TestBattleSetup.At(ally, row, c);
                    string text = u == null ? "+"
                        : (u.Commander ? "★ " : "") + T("troop." + u.Troop) + " · " + T("ui.crowd.general." + u.General) + "\n" + T("ui.sample2.f." + u.Formation) + " · " + u.Count;
                    var b = Ui.Button(line, text, () => OnCell(ally, row, c), 116, u == null ? Palette.Card : (ally ? new Color(0.24f, 0.36f, 0.6f) : new Color(0.58f, 0.24f, 0.2f)), u == null ? 40 : 24);
                    Ui.Size(b, flexibleWidth: 1);
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
            var (content, close) = Ui.BottomSheet(_root, title, 740);
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
                Ui.Size(Ui.Button(fr, T("ui.sample2.f." + id), () => { u.Formation = f; Changed(); }, 84, u.Formation == id ? Palette.ButtonActive : Palette.Button, 26), flexibleWidth: 1);
            }
            // 장수 성향 (R-138): 맹장 = 선봉에서 직접 싸움 · 장수 = 대열 뒤 · 책사 = 멀리 뒤 (나중에 캠페인 장수 · 병종 적성)
            var gr = Ui.Row(content, "General", 84, 8);
            Ui.Size(Ui.Label(gr, T("ui.test.general"), 26), width: 160);
            foreach (var id in GeneralPresets)
            {
                string g = id;
                Ui.Size(Ui.Button(gr, T("ui.crowd.general." + id), () => { u.General = g; Changed(); }, 84, u.General == id ? Palette.ButtonActive : Palette.Button, 26), flexibleWidth: 1);
            }
            int max = CrowdRules.Load(_app.Context.Data).GridMaxCount;
            var cr = Ui.Row(content, "Count", 84, 8);
            Ui.Size(Ui.Label(cr, T("ui.test.count"), 26), width: 160);
            Ui.Size(Ui.Button(cr, "−10", () => { u.Count = Mathf.Max(10, u.Count - 10); Changed(); }, 84, Palette.Button, 26), flexibleWidth: 1);
            Ui.Size(Ui.Label(cr, u.Count.ToString(), 32, TextAnchor.MiddleCenter), flexibleWidth: 1);
            Ui.Size(Ui.Button(cr, "+10", () => { u.Count = Mathf.Min(max, u.Count + 10); Changed(); }, 84, Palette.Button, 26), flexibleWidth: 1);
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
            _app.Show(new SampleBattle2Screen(Setup(_app.Context), startNow: true, returnTab: "test"));
        }

        /// <summary>지금 칸 설정으로 전투 설정 (사본 — 전투 중 설정 화면을 바꿔도 영향 없음).</summary>
        public static CrowdSetup Setup(Game.Unity.Boot.GameContext ctx)
        {
            List<CrowdUnitSpec> Copy(List<CrowdUnitSpec> l)
            {
                var r = new List<CrowdUnitSpec>();
                foreach (var u in l) r.Add(new CrowdUnitSpec { Row = u.Row, Col = u.Col, Troop = u.Troop, Formation = u.Formation, Count = u.Count, Commander = u.Commander, General = u.General });
                return r;
            }
            return new CrowdSetup { AllyUnits = Copy(TestBattleSetup.Ally), EnemyUnits = Copy(TestBattleSetup.Enemy), AllyAdvance = false, EnemyAdvance = TestBattleSetup.EnemyAdvance,
                Terrain = TerrainOf(ctx, TestBattleSetup.Map, TestBattleSetup.AllyDefends) };
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
