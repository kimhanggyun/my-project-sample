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
        public static bool EnemyAdvance;
        public static string Map = "plain";
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
            sb.Append(EnemyAdvance ? "1" : "0").Append('|').Append(Map);
            foreach (var (team, list) in new[] { ("a", Ally), ("e", Enemy) })
                foreach (var u in list)
                    sb.Append('|').Append(team).Append(',').Append(u.Row).Append(',').Append(u.Col).Append(',').Append(u.Troop).Append(',')
                      .Append(u.Formation).Append(',').Append(u.Count).Append(',').Append(u.Commander ? 1 : 0);
            try { PlayerPrefs.SetString(Key, sb.ToString()); PlayerPrefs.Save(); } catch { }
        }

        private static bool Parse(string s)
        {
            try
            {
                var parts = s.Split('|');
                EnemyAdvance = parts[0] == "1";
                Map = parts[1];
                Ally.Clear(); Enemy.Clear();
                for (int i = 2; i < parts.Length; i++)
                {
                    var f = parts[i].Split(',');
                    var u = new CrowdUnitSpec { Row = int.Parse(f[1]), Col = int.Parse(f[2]), Troop = f[3], Formation = f[4], Count = int.Parse(f[5]), Commander = f[6] == "1" };
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
        private static readonly string[] Maps = { "plain" };

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
            Ui.Fill((RectTransform)list.parent.parent);
            InfoButton.Add(_root, T("ui.test.note"));

            Grid(list, false);
            // 맵: 좌우로 넘기는 카드 (지금은 평지 하나)
            Ui.Label(list, T("ui.test.map"), Ui.FontBody);
            var maps = Ui.HScrollRow(list, "Maps", 110);
            foreach (var m in Maps)
            {
                string id = m;
                var b = Ui.Button(maps, T("ui.test.map." + id), () => { TestBattleSetup.Map = id; TestBattleSetup.Save(); Redraw(); }, 100,
                    TestBattleSetup.Map == id ? Palette.ButtonActive : Palette.Button, 26);
                Ui.Size(b, 100, 220);
            }
            Grid(list, true);

            var order = Ui.Row(list, "EnemyOrder", 72, 8);
            Ui.Size(Ui.Label(order, T("ui.sample2.enemyOrder"), 24), width: 160);
            Ui.Size(Ui.Button(order, T("ui.sample2.hold"), () => { TestBattleSetup.EnemyAdvance = false; TestBattleSetup.Save(); Redraw(); }, 72,
                TestBattleSetup.EnemyAdvance ? Palette.Button : Palette.ButtonActive, 24), flexibleWidth: 1);
            Ui.Size(Ui.Button(order, T("ui.sample2.advance"), () => { TestBattleSetup.EnemyAdvance = true; TestBattleSetup.Save(); Redraw(); }, 72,
                TestBattleSetup.EnemyAdvance ? Palette.ButtonActive : Palette.Button, 24), flexibleWidth: 1);

            bool ready = TestBattleSetup.Ally.Count > 0 && TestBattleSetup.Enemy.Count > 0;
            var start = Ui.Button(list, T("ui.test.start"), Start, 96, Palette.ButtonActive, 32);
            if (!ready) Ui.SetEnabled(start, false);
            if (!ready) Ui.Label(list, T("ui.test.needBoth"), 22, TextAnchor.MiddleCenter, Palette.Bad);
        }

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
                        : (u.Commander ? "★ " : "") + T("troop." + u.Troop) + "\n" + T("ui.sample2.f." + u.Formation) + " · " + u.Count;
                    var b = Ui.Button(line, text, () => OnCell(ally, row, c), 116, u == null ? Palette.Card : (ally ? new Color(0.24f, 0.36f, 0.6f) : new Color(0.58f, 0.24f, 0.2f)), u == null ? 40 : 24);
                    Ui.Size(b, flexibleWidth: 1);
                }
            }
            Ui.Label(card, T(ally ? "ui.test.allyHint" : "ui.test.enemyHint"), 20, TextAnchor.MiddleCenter, Palette.TextDim);
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
            string title = T(ally ? "ui.test.sheetAlly" : "ui.test.sheetEnemy", T("ui.test.row." + CrowdUnitSpec.RowIds[u.Row]) + " " + T("ui.test.col." + CrowdUnitSpec.ColIds[u.Col]));
            var (content, close) = Ui.BottomSheet(_root, title, 640);
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

        private void Start()
        {
            if (TestBattleSetup.Ally.Count == 0 || TestBattleSetup.Enemy.Count == 0) return;
            _app.Show(new SampleBattle2Screen(Setup(), startNow: true, returnTab: "test"));
        }

        /// <summary>지금 칸 설정으로 전투 설정 (사본 — 전투 중 설정 화면을 바꿔도 영향 없음).</summary>
        public static CrowdSetup Setup()
        {
            List<CrowdUnitSpec> Copy(List<CrowdUnitSpec> l)
            {
                var r = new List<CrowdUnitSpec>();
                foreach (var u in l) r.Add(new CrowdUnitSpec { Row = u.Row, Col = u.Col, Troop = u.Troop, Formation = u.Formation, Count = u.Count, Commander = u.Commander });
                return r;
            }
            return new CrowdSetup { AllyUnits = Copy(TestBattleSetup.Ally), EnemyUnits = Copy(TestBattleSetup.Enemy), AllyAdvance = false, EnemyAdvance = TestBattleSetup.EnemyAdvance };
        }
    }
}
