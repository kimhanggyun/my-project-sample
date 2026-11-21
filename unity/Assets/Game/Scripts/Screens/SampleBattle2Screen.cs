using System.Collections.Generic;
using Game.Battle.Model.Crowd;
using Game.Battle.Model.Geometry;
using Game.Foundation.Math;
using Game.Unity.Battle3D;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 샘플전투2 (R-125 · R-126, 시험 중): 병사 단위 전투.
    /// 시작 전: 우리 · 적 진형, 부대 수(하나 · 셋), 적 명령. 싸우는 중: 부대를 고르고 (아래 버튼 또는 전장에서 눌러)
    /// 땅을 누르면 이동, 적 부대를 누르면 공격 (가까워지면 돌격), 진형 바꾸기 · 정지 · 전체 돌격.
    /// 기존 샘플전투와 따로 (규칙 CrowdBattle, 그리기 CrowdField3D). 보상 없음, 세이브에 남지 않음.
    /// </summary>
    public sealed class SampleBattle2Screen : GameScreen
    {
        private const float TopHeight = 76f;
        private const int AllCorps = -2;
        private static readonly int[] Speeds = { 1, 2, 4 };

        private readonly CrowdSetup _setup;
        private readonly bool _startNow;
        private CrowdBattle _battle;
        private CrowdField3D _field;
        private RectTransform _fieldArea;
        private readonly Vector3[] _corners = new Vector3[4];
        private Text _status;
        private RectTransform _panel;
        private float _accumulator, _tickSeconds;
        private bool _running;
        private int _speed;
        /// <summary>고른 우리 부대 (Armies 번호), 전체면 AllCorps.</summary>
        private int _selected = AllCorps;
        private readonly Dictionary<int, Button> _corpsButtons = new Dictionary<int, Button>();
        private readonly Dictionary<string, Button> _formationButtons = new Dictionary<string, Button>();
        private Button _speedButton;
        private CrowdHud _hud;
        private RadialMenu _radial;
        /// <summary>원형 메뉴로 고른 다음 누를 것: null · "move" (다음 땅 = 이동) · "queue" (땅마다 경유 지점 예약, [예약 끝]까지).</summary>
        private string _pickMode;
        private Button _queueDone;
        private Text _hintText;

        /// <summary>[닫기]로 돌아갈 메인 탭 (테스트 탭에서 열면 "test").</summary>
        private readonly string _returnTab;

        public SampleBattle2Screen(CrowdSetup setup = null, bool startNow = false, string returnTab = "test")
        {
            _returnTab = returnTab;
            _setup = setup ?? new CrowdSetup
            {
                AllyFormation = CrowdFormations.Wedge, EnemyFormation = CrowdFormations.Line, AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false,
                AllyTroops = Preset("mixed"), EnemyTroops = Preset("mixed"),
            };
            _startNow = startNow;
        }

        public override void Build()
        {
            var rules = CrowdRules.Load(Ctx.Data);
            if (_setup.Seed <= 1) _setup.Seed = (ulong)Ctx.NewSeed();
            _battle = NewBattle(rules);
            _tickSeconds = (float)rules.TickSeconds.ToDisplayDouble();
            _running = _startNow;

            Ui.Column(Root, 6, 8);
            var top = Ui.Row(Root, "Top", TopHeight, 8);
            Ui.Background(top, Palette.Bar);
            _status = Ui.Label(top, "", 26, TextAnchor.MiddleLeft);
            Ui.Size(_status, flexibleWidth: 3);
            _speedButton = Ui.Button(top, "", NextSpeed, TopHeight, fontSize: 26);
            Ui.Size(_speedButton, width: 110);
            Ui.Size(Ui.Button(top, T("ui.sample2.close"), () => App.ShowMain(_returnTab), TopHeight, fontSize: 26), width: 130);

            App.SetBackdrop(false);
            _field = new CrowdField3D(_battle);
            _fieldArea = Ui.Rect(Root, "Field");
            Ui.Size(_fieldArea, flexibleHeight: 1);
            Ui.Background(_fieldArea, new Color(0, 0, 0, 0));
            var gestures = _fieldArea.gameObject.AddComponent<GestureArea>();
            gestures.Tapped += OnFieldTap;
            gestures.Dragged += (a, b) => _field.Rig.Pan(a, b);
            gestures.Zoomed += f => _field.Rig.Zoom(f);
            _hud = new CrowdHud(_battle, _field, _fieldArea, TagName);
            _radial = new RadialMenu(_fieldArea); // 부대 원형 메뉴 (기존 전투 battle.md 4.3.3을 옮김, R-129)
            _queueDone = Ui.Button(_fieldArea, T("ui.sample2.queueDone"), EndPick, 72, Palette.ButtonActive, 26);
            var qr = (RectTransform)_queueDone.transform;
            qr.anchorMin = qr.anchorMax = new Vector2(0.5f, 1f);
            qr.pivot = new Vector2(0.5f, 1f);
            qr.anchoredPosition = new Vector2(0f, -12f);
            qr.sizeDelta = new Vector2(260f, 72f);
            _queueDone.gameObject.SetActive(false);

            _panel = Ui.Rect(Root, "Panel");
            Ui.Background(_panel, Palette.Bar);
            Ui.Column(_panel, 8, 10);
            Ui.Size(_panel, _setup.AllyUnits != null && _setup.AllyUnits.Count + 1 > 5 ? 520 : 470);
            BuildPanel();
            UpdateTexts();
        }

        private bool Started => _running || _battle.Tick > 0;

        /// <summary>병종 데이터 · 상성을 넣어 만든다 (R-127).</summary>
        private CrowdBattle NewBattle(CrowdRules rules)
        {
            var b = new CrowdBattle(rules, _setup, Ctx.Defs.TroopTypes, Ctx.Defs.TroopCounters);
            b.UseSkillData(Ctx.Defs.Skills); // 스킬 (R-130): skills.json 기본 스킬 다섯
            return b;
        }

        /// <summary>병종 고르기 (시작 전): 한 병종이면 모든 부대가 그 병종, 혼합 = 좌 기병 · 중 보병 · 우 궁병 (부대 하나면 보병).</summary>
        public static readonly string[] Presets = { "infantry", "spearman", "archer", "cavalry", "mixed" };

        public static string[] Preset(string id) => id == "mixed" ? new[] { "cavalry", "infantry", "archer" } : new[] { id };

        private static string PresetOf(string[] troops) => troops == null || troops.Length == 0 ? "infantry" : troops.Length > 1 ? "mixed" : troops[0];

        private void TroopRow(string label, string[] current, System.Action<string[]> pick)
        {
            var row = Ui.Row(_panel, "Troops " + label, 64, 4);
            Ui.Size(Ui.Label(row, label, 22), width: 150);
            string cur = PresetOf(current);
            foreach (var id in Presets)
            {
                string p = id;
                Choice(row, T(id == "mixed" ? "ui.sample2.mixed" : "troop." + id), id == cur, () => pick(Preset(p)));
            }
        }

        private void BuildPanel()
        {
            Ui.Clear(_panel);
            _formationButtons.Clear();
            _corpsButtons.Clear();
            if (!Started)
            {
                // 시작 전: 우리 진형 · 적 진형 · 부대 수 · 적 명령
                FormationRow(T("ui.sample2.ally"), _setup.AllyFormation, id => { _setup.AllyFormation = id; Restart(); });
                FormationRow(T("ui.sample2.enemy"), _setup.EnemyFormation, id => { _setup.EnemyFormation = id; Restart(); });
                TroopRow(T("ui.sample2.allyTroops"), _setup.AllyTroops, t => { _setup.AllyTroops = t; Restart(); });
                TroopRow(T("ui.sample2.enemyTroops"), _setup.EnemyTroops, t => { _setup.EnemyTroops = t; Restart(); });
                var corps = Ui.Row(_panel, "Corps", 68, 6);
                Ui.Size(Ui.Label(corps, T("ui.sample2.corps"), 24), width: 150);
                Choice(corps, T("ui.sample2.ally1"), _setup.AllyCorps == 1, () => { _setup.AllyCorps = 1; Restart(); });
                Choice(corps, T("ui.sample2.ally3"), _setup.AllyCorps == 3, () => { _setup.AllyCorps = 3; Restart(); });
                Choice(corps, T("ui.sample2.enemy1"), _setup.EnemyCorps == 1, () => { _setup.EnemyCorps = 1; Restart(); });
                Choice(corps, T("ui.sample2.enemy3"), _setup.EnemyCorps == 3, () => { _setup.EnemyCorps = 3; Restart(); });
                var row = Ui.Row(_panel, "EnemyOrder", 68, 8);
                Ui.Size(Ui.Label(row, T("ui.sample2.enemyOrder"), 24), width: 150);
                Choice(row, T("ui.sample2.hold"), !_setup.EnemyAdvance, () => { _setup.EnemyAdvance = false; Restart(); });
                Choice(row, T("ui.sample2.advance"), _setup.EnemyAdvance, () => { _setup.EnemyAdvance = true; Restart(); });
                Ui.Button(_panel, T("ui.sample2.start"), () => { _running = true; BuildPanel(); }, 76, Palette.ButtonActive, 30);
                return;
            }
            // 싸우는 중: 부대 고르기 · 진형 · 정지 · 전체 돌격 · 다시
            // 부대 버튼: 한 줄에 다섯까지 (3×3이면 두 줄)
            var mine = new List<CrowdArmy>(_battle.TeamArmies(CrowdBattle.Ally));
            int perRow = mine.Count + 1 > 5 ? 5 : mine.Count + 1;
            RectTransform pick = null;
            int inRow = 0;
            foreach (var a in mine)
            {
                if (pick == null || inRow >= perRow) { pick = Ui.Row(_panel, "Pick", mine.Count + 1 > 5 ? 72 : 84, 6); inRow = 0; }
                int id = a.Id;
                var b = Ui.Button(pick, CorpsName(a), () => Select(id), mine.Count + 1 > 5 ? 72 : 84, Palette.Button, mine.Count + 1 > 5 ? 18 : 22);
                Ui.Size(b, flexibleWidth: 1);
                _corpsButtons[id] = b;
                inRow++;
            }
            if (inRow >= perRow) pick = Ui.Row(_panel, "Pick", 72, 6);
            var all = Ui.Button(pick, T("ui.sample2.all"), () => Select(AllCorps), 84, Palette.Button, 24);
            Ui.Size(all, flexibleWidth: 1);
            _corpsButtons[AllCorps] = all;
            FormationRow(T("ui.sample2.formation"), CurrentFormation(), id =>
            {
                foreach (var a in Chosen()) _battle.SetFormation(a.Id, id);
                foreach (var kv in _formationButtons) Ui.Recolor(kv.Value, kv.Key == id ? Palette.ButtonActive : Palette.Button);
            });
            var orders = Ui.Row(_panel, "Orders", 76, 8);
            Ui.Size(Ui.Button(orders, T("ui.sample2.stop"), () => { foreach (var a in Chosen()) _battle.OrderHold(a.Id); }, 76, Palette.Button, 26), flexibleWidth: 1);
            Ui.Size(Ui.Button(orders, T("ui.sample2.charge"), ChargeAll, 76, Palette.ButtonActive, 26), flexibleWidth: 1);
            Ui.Size(Ui.Button(orders, T("ui.sample2.again"), () => App.Show(new SampleBattle2Screen(new CrowdSetup
            {
                AllyFormation = _setup.AllyFormation, EnemyFormation = _setup.EnemyFormation, EnemyAdvance = _setup.EnemyAdvance,
                AllyCorps = _setup.AllyCorps, EnemyCorps = _setup.EnemyCorps, AllyAdvance = false,
                AllyTroops = _setup.AllyTroops, EnemyTroops = _setup.EnemyTroops,
            })), 76, Palette.Button, 26), flexibleWidth: 1);
            _hintText = Ui.Label(_panel, T("ui.sample2.hint"), 22, TextAnchor.MiddleCenter, Palette.TextDim);
            Select(_selected);
        }

        private void Choice(RectTransform row, string text, bool on, System.Action act) =>
            Ui.Size(Ui.Button(row, text, act, 68, on ? Palette.ButtonActive : Palette.Button, 22), flexibleWidth: 1);

        /// <summary>장수 이름표: 우리 = "★중군 보병", 적 = "적 중군 창병".</summary>
        private string TagName(CrowdArmy a)
        {
            string s = (a.Team == CrowdBattle.Enemy ? T("ui.sample2.enemyTag") + " " : "") + (a.IsCommander ? "★" : "") + PlaceName(a) + " " + T("troop." + a.Troop.Id);
            // 켜져 있는 스킬 (R-130)
            int tick = _battle.Tick;
            if (a.AttackBuffUntil > tick) s += " · " + T("skill.warCry");
            if (a.FortifyUntil > tick) s += " · " + T("skill.holdTheLine");
            if (a.MarchUntil > tick) s += " · " + T("skill.forcedMarch");
            return s;
        }

        // ---- 부대 원형 메뉴 (R-129): 돌격 · 이동 · 예약 이동 · 정지 · 진형 ----

        private RadialMenu.Item Pick(string label, System.Action act, bool active = false) =>
            new RadialMenu.Item { Label = label, Active = active, OnPick = () => { act(); _radial.Close(); } };

        private void OpenCorpsMenu(CrowdArmy a)
        {
            EndPick();
            Select(a.Id);
            _radial.Open(T("ui.radial.close"), () => _radial.Close(), new List<RadialMenu.Item>
            {
                Pick(T("ui.sample2.charge"), ChargeAll),
                new RadialMenu.Item { Label = T("ui.radial.move"), OnPick = () => BeginPick("move") },
                new RadialMenu.Item { Label = T("ui.sample2.queue"), OnPick = () => BeginPick("queue") },
                Pick(T("ui.sample2.stop"), () => { foreach (var c in Chosen()) _battle.OrderHold(c.Id); }),
                new RadialMenu.Item { Label = T("ui.radial.formation") + " ›", OnPick = () => OpenFormationMenu(a) },
                new RadialMenu.Item { Label = T("ui.radial.skill") + " ›", Enabled = a.SkillUses.Count > 0, OnPick = () => OpenSkillMenu(a) },
            });
            FollowMenu();
        }

        private void OpenFormationMenu(CrowdArmy a)
        {
            var items = new List<RadialMenu.Item>();
            foreach (var id in CrowdFormations.All)
            {
                string f = id;
                items.Add(Pick(T("ui.sample2.f." + id), () => { foreach (var c in Chosen()) _battle.SetFormation(c.Id, f); Select(_selected); }, a.Formation == id));
            }
            _radial.Open(T("ui.radial.back"), () => OpenCorpsMenu(a), items);
            FollowMenu();
        }

        /// <summary>스킬 하위 고리 (R-130): 이름 + 남은 횟수, 못 쓰면 흐리게. 화살비는 다음에 누른 땅에.</summary>
        private void OpenSkillMenu(CrowdArmy a)
        {
            var items = new List<RadialMenu.Item>();
            foreach (var id in CrowdBattle.KnownSkills)
            {
                if (!a.SkillUses.TryGetValue(id, out var left)) continue;
                string sk = id;
                var def = _battle.SkillDef(id);
                bool can = _battle.CanUseSkill(a.Id, id);
                items.Add(new RadialMenu.Item
                {
                    Label = T(def.NameKey) + "\n" + T("ui.sample2.uses", left), Enabled = can,
                    OnPick = () =>
                    {
                        if (sk == CrowdBattle.ArrowRain) { BeginPick("skill:" + sk); return; }
                        _battle.UseSkill(a.Id, sk);
                        _radial.Close();
                    },
                });
            }
            _radial.Open(T("ui.radial.back"), () => OpenCorpsMenu(a), items);
            FollowMenu();
        }

        /// <summary>원형 메뉴를 고른 부대 장수 위에 둔다 (부대가 움직이면 따라감).</summary>
        private void FollowMenu()
        {
            if (!_radial.IsOpen || _selected < 0) return;
            var a = _battle.Armies[_selected];
            var cam = _field.Rig.Camera;
            var sp = cam.WorldToScreenPoint(_field.ToWorld(a.General.Alive ? a.General.Pos : a.Centroid()));
            if (sp.z > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(_fieldArea, sp, null, out var local)) _radial.Follow(local);
        }

        private void BeginPick(string mode)
        {
            _radial.Close();
            _pickMode = mode;
            _queueDone.gameObject.SetActive(mode == "queue");
            if (_hintText != null) _hintText.text = T(mode == "queue" ? "ui.sample2.queueHint" : mode.StartsWith("skill:") ? "ui.sample2.skillHint" : "ui.sample2.moveHint");
        }

        private void EndPick()
        {
            _pickMode = null;
            if (_queueDone != null) _queueDone.gameObject.SetActive(false);
            if (_hintText != null) _hintText.text = T("ui.sample2.hint");
        }

        private string CorpsName(CrowdArmy a) => PlaceName(a) + (a.IsCommander ? "★" : "") + " " + T("troop." + a.Troop.Id) + "\n" + MoraleText(a);

        /// <summary>부대 자리 이름: 좌군 · 중군 · 우군, 3×3 칸이면 "앞 좌" 등 (R-131).</summary>
        private string PlaceName(CrowdArmy a)
        {
            int k = a.Place.IndexOf('_');
            if (k < 0) return T("ui.sample2.place." + a.Place);
            return T("ui.test.row." + a.Place.Substring(0, k)) + " " + T("ui.test.col." + a.Place.Substring(k + 1));
        }

        /// <summary>사기 (R-128): 숫자, 흔들림 · 포위 · 무너짐 표시.</summary>
        private string MoraleText(CrowdArmy a)
        {
            if (a.Routed) return Ui.Colored(T("ui.sample2.routed"), Palette.Bad);
            if (!a.General.Alive) return Ui.Colored(T("ui.sample2.noGeneral"), Palette.Bad);
            string m = T("ui.sample2.morale", (int)a.Morale.FloorToLong());
            if (a.Encircled) m += " " + T("ui.sample2.encircled");
            else if (a.Wavering) m += " " + T("ui.sample2.wavering");
            return a.Wavering || a.Encircled ? Ui.Colored(m, Palette.Bad) : m;
        }

        private void FormationRow(string label, string current, System.Action<string> pick)
        {
            var row = Ui.Row(_panel, "Formation " + label, 68, 6);
            Ui.Size(Ui.Label(row, label, 24), width: 150);
            foreach (var id in CrowdFormations.All)
            {
                string f = id;
                var b = Ui.Button(row, T("ui.sample2.f." + id), () => pick(f), 68, id == current ? Palette.ButtonActive : Palette.Button, 24);
                Ui.Size(b, flexibleWidth: 1);
                _formationButtons[id] = b;
            }
        }

        /// <summary>명령을 받을 우리 부대들 (고른 것, 전체면 장수가 살아 있는 모두).</summary>
        private List<CrowdArmy> Chosen()
        {
            var list = new List<CrowdArmy>();
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Leading && (_selected == AllCorps || _selected == a.Id)) list.Add(a);
            return list;
        }

        private string CurrentFormation()
        {
            var c = Chosen();
            return c.Count > 0 ? c[0].Formation : _setup.AllyFormation;
        }

        private void Select(int id)
        {
            _selected = id;
            _field.SelectedArmy = id == AllCorps ? -1 : id;
            _field.SelectAll = id == AllCorps;
            foreach (var kv in _corpsButtons) Ui.Recolor(kv.Value, kv.Key == id ? Palette.ButtonActive : Palette.Button);
            string f = CurrentFormation();
            foreach (var kv in _formationButtons) Ui.Recolor(kv.Value, kv.Key == f ? Palette.ButtonActive : Palette.Button);
        }

        /// <summary>전체 돌격: 고른 우리 부대마다 가장 가까운 적 부대를 공격.</summary>
        private void ChargeAll()
        {
            foreach (var a in Chosen())
            {
                int best = -1;
                Fix bestD = Fix.Zero;
                foreach (var e in _battle.TeamArmies(CrowdBattle.Enemy))
                {
                    if (!_battle.Standing(e)) continue;
                    Fix d = Vec.DistanceSquared(e.Centroid(), a.Centroid());
                    if (best < 0 || d < bestD) { best = e.Id; bestD = d; }
                }
                if (best >= 0) _battle.OrderAttack(a.Id, best);
            }
        }

        /// <summary>전장을 누름: 우리 부대 → 고르기, 적 부대 → 공격, 땅 → 이동 (전체면 부대 사이를 지키며 같이).</summary>
        private void OnFieldTap(Vector2 screen)
        {
            if (!Started || _battle.IsOver) return;
            if (!_field.Rig.Ground(screen, 0f, out var world)) return;
            CommandAt(new Vec(Fix.FromDecimal((decimal)world.x), Fix.FromDecimal((decimal)world.z)));
        }

        /// <summary>전장 한 곳을 누른 것과 같다 (화면 누름 · 캡처 투어).</summary>
        private void CommandAt(Vec point)
        {
            var hit = ArmyAt(point);
            if (hit != null && hit.Team == CrowdBattle.Ally)
            {
                if (!hit.Leading) return;
                if (_radial.IsOpen && _selected == hit.Id) { _radial.Close(); return; }
                OpenCorpsMenu(hit); // 우리 부대를 누르면 고르고 원형 메뉴
                return;
            }
            if (_radial.IsOpen) { _radial.Close(); return; } // 메뉴가 열려 있으면 바깥 누름 = 닫기
            var chosen = Chosen();
            if (chosen.Count == 0) return;
            if (_pickMode != null && _pickMode.StartsWith("skill:"))
            {
                // 범위 스킬 (화살비): 누른 곳에 (사거리 밖이면 알림)
                string sk = _pickMode.Substring(6);
                bool used = _selected >= 0 && _battle.UseSkill(_selected, sk, point);
                if (!used) Toast.Show(T("ui.sample2.outOfRange"), bad: true);
                EndPick();
                return;
            }
            if (hit != null) { foreach (var a in chosen) _battle.OrderAttack(a.Id, hit.Id); EndPick(); return; }
            bool queue = _pickMode == "queue";
            if (_pickMode == "move") EndPick();
            if (chosen.Count == 1) { _battle.OrderMove(chosen[0].Id, point, queue); return; }
            // 전체 이동: 총대장 부대(없으면 첫 부대)를 그 자리로, 나머지는 지금 떨어진 만큼 그대로
            var lead = chosen[0];
            foreach (var a in chosen) if (a.IsCommander) lead = a;
            var from = lead.Center;
            foreach (var a in chosen) _battle.OrderMove(a.Id, point + (a.Center - from), queue);
        }

        /// <summary>
        /// 그 자리를 덮는 부대: 부대 앞 방향 기준 직사각형 (대열 반 폭 · 반 깊이 + 여유) 안, 가장 가까운 것.
        /// (2026-11-21 캡처: 둥근 판정이면 넓은 일자 대열 바로 앞 땅을 눌러도 그 부대를 고름)
        /// </summary>
        private CrowdArmy ArmyAt(Vec p)
        {
            CrowdArmy best = null;
            Fix bestD = Fix.Zero;
            Fix pad = Fix.FromRatio(8, 10), padBack = Fix.FromInt(2); // 뒤쪽은 장수까지
            foreach (var a in _battle.Armies)
            {
                if (!_battle.Standing(a)) continue;
                var d = p - a.Centroid();
                Fix lx = d.Dot(a.Right), ly = d.Dot(a.Facing);
                if (lx < -(a.HalfWidth + pad) || lx > a.HalfWidth + pad || ly > a.HalfDepth + pad || ly < -(a.HalfDepth + padBack)) continue;
                Fix dist = d.LengthSquared;
                if (best == null || dist < bestD) { best = a; bestD = dist; }
            }
            return best;
        }

        /// <summary>시작 전 설정을 바꾸면 새 전투로 다시 (같은 화면 안).</summary>
        private void Restart()
        {
            _field?.Destroy();
            _setup.Seed = (ulong)Ctx.NewSeed();
            _battle = NewBattle(CrowdRules.Load(Ctx.Data));
            _field = new CrowdField3D(_battle);
            _hud?.Destroy();
            _hud = new CrowdHud(_battle, _field, _fieldArea, TagName);
            _radial.Close();
            EndPick();
            _accumulator = 0f;
            _running = false;
            _selected = AllCorps;
            BuildPanel();
            UpdateTexts();
        }

        private void NextSpeed() { _speed = (_speed + 1) % Speeds.Length; UpdateTexts(); }

        private void UpdateTexts()
        {
            var eg = _battle.Commander(CrowdBattle.Enemy).General;
            string head = T("ui.sample2.count", _battle.AliveCount(CrowdBattle.Ally), _battle.AliveCount(CrowdBattle.Enemy), eg.Hp, eg.MaxHp);
            if (_battle.IsOver)
                head = T(_battle.Winner == CrowdBattle.Ally ? "ui.sample2.win" : _battle.Winner == CrowdBattle.Enemy ? "ui.sample2.lose" : "ui.sample2.draw")
                    + " · " + T("ui.sample2.end." + _battle.EndReason) + "  " + head;
            _status.text = head + "  " + T("ui.sample2.enemyMorale", (int)_battle.Commander(CrowdBattle.Enemy).Morale.FloorToLong());
            foreach (var kv in _corpsButtons)
                if (kv.Key >= 0) kv.Value.GetComponentInChildren<Text>().text = CorpsName(_battle.Armies[kv.Key]);
            _speedButton.GetComponentInChildren<Text>().text = "×" + Speeds[_speed];
        }

        public override void Tick(float deltaTime)
        {
            if (_battle == null) return;
            if (_running && !_battle.IsOver)
            {
                _accumulator += deltaTime * Speeds[_speed];
                int steps = 0;
                while (_accumulator >= _tickSeconds && steps < 8 && !_battle.IsOver)
                {
                    _battle.Step();
                    _accumulator -= _tickSeconds;
                    steps++;
                }
                if (steps >= 8) _accumulator = 0f;
                if (steps > 0) UpdateTexts();
            }
            _fieldArea.GetWorldCorners(_corners);
            _field.SetViewport(new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y));
            _field.Draw(_running && !_battle.IsOver ? Mathf.Clamp01(_accumulator / _tickSeconds) : 1f, deltaTime);
            _hud.Tick(deltaTime);
            _radial.Tick(deltaTime);
            FollowMenu();
        }

        public override void Close()
        {
            _hud?.Destroy();
            _field?.Destroy();
            App.SetBackdrop(true);
        }

        // ---- 캡처 투어 ----

        /// <summary>캡처 투어: 틱을 직접 돌린다 (화면 시간과 무관하게 같은 장면).</summary>
        public void StepForTour(int ticks)
        {
            bool first = _battle.Tick == 0;
            _running = false; // 화면 시간으로는 돌지 않게 (캡처가 정한 틱만)
            for (int i = 0; i < ticks && !_battle.IsOver; i++) _battle.Step();
            if (first) BuildPanel();
            UpdateTexts();
        }

        /// <summary>캡처 투어: 맞붙은 곳(가장 최근에 친 병사들 가운데)으로 카메라.</summary>
        public void FocusFightForTour(float distance)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var s in _battle.Soldiers)
                if (s.Alive && s.LastAttackTick >= _battle.Tick - 10) { sum += _field.ToWorld(s.Pos); n++; }
            _field.Rig.LookAt(n > 0 ? sum / n : new Vector3(30f, 0f, 30f));
            _field.Rig.SetDistance(distance);
        }

        /// <summary>캡처 투어: 그 편 병사들 가운데로 카메라.</summary>
        public void FocusArmyForTour(int team, float distance)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var s in _battle.Soldiers)
                if (s.Alive && s.Team == team && !s.IsGeneral) { sum += _field.ToWorld(s.Pos); n++; }
            if (n > 0) _field.Rig.LookAt(sum / n);
            _field.Rig.SetDistance(distance);
        }

        /// <summary>캡처 투어: 우리 총대장(말 탄 장수) 가까이로 카메라.</summary>
        public void FocusGeneralForTour(float distance)
        {
            var g = _battle.Commander(CrowdBattle.Ally).General;
            _field.Rig.LookAt(_field.ToWorld(g.Pos));
            _field.Rig.SetDistance(distance);
        }

        /// <summary>캡처 투어: 누군가 친 적이 있음 (맞붙음).</summary>
        public bool FightingForTour
        {
            get { foreach (var s in _battle.Soldiers) if (s.LastAttackTick >= 0) return true; return false; }
        }

        /// <summary>캡처 투어: 우리 부대 원형 메뉴 열기 · 예약 이동 (화면 조작과 같은 길).</summary>
        public void OpenMenuForTour(string place)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally)) if (a.Place == place) OpenCorpsMenu(a);
        }

        public void QueueForTour(string place, float[] xy)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally)) if (a.Place == place) Select(a.Id);
            BeginPick("queue");
            for (int i = 0; i + 1 < xy.Length; i += 2) CommandAt(new Vec(Fix.FromDecimal((decimal)xy[i]), Fix.FromDecimal((decimal)xy[i + 1])));
        }

        /// <summary>캡처 투어: 스킬 쓰기 (화면 조작과 같은 규칙 길).</summary>
        public bool SkillForTour(string place, string skill, float x = -1f, float y = -1f)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place)
                    return x < 0 ? _battle.UseSkill(a.Id, skill) : _battle.UseSkill(a.Id, skill, new Vec(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y)));
            return false;
        }

        /// <summary>캡처 투어: 전장 한 곳으로 카메라.</summary>
        public void FocusPointForTour(float x, float y, float distance)
        {
            _field.Rig.LookAt(_field.ToWorld(new Vec(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y))));
            _field.Rig.SetDistance(distance);
        }

        /// <summary>캡처 투어: 그 우리 부대의 가까운 적 부대 가운데 (화살비 지점).</summary>
        public Vector2 EnemyNearForTour(string place)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place)
                {
                    var me = a.Centroid();
                    CrowdArmy best = null; Fix bd = Fix.Zero;
                    foreach (var e in _battle.TeamArmies(CrowdBattle.Enemy))
                    {
                        Fix d = Vec.DistanceSquared(e.Centroid(), me);
                        if (best == null || d < bd) { best = e; bd = d; }
                    }
                    if (best != null) { var c = best.Centroid(); return new Vector2((float)c.X.ToDisplayDouble(), (float)c.Y.ToDisplayDouble()); }
                }
            return Vector2.zero;
        }

        /// <summary>캡처 투어: 적 총대장 부대가 무너짐.</summary>
        public bool EnemyRoutedForTour => _battle.Commander(CrowdBattle.Enemy).Routed;

        /// <summary>캡처 투어: 이번 틱에 부딪침이 있음.</summary>
        public bool ImpactForTour => _battle.ImpactsThisTick > 0;

        /// <summary>캡처 투어: 우리 부대 고르기 · 명령 (화면 조작과 같은 길).</summary>
        public void SelectForTour(string place)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally)) if (a.Place == place) Select(a.Id);
        }

        public void AttackForTour(string place, string enemyPlace)
        {
            CrowdArmy me = null, foe = null;
            foreach (var a in _battle.Armies)
            {
                if (a.Team == CrowdBattle.Ally && a.Place == place) me = a;
                if (a.Team == CrowdBattle.Enemy && a.Place == enemyPlace) foe = a;
            }
            if (me != null && foe != null) _battle.OrderAttack(me.Id, foe.Id);
        }

        /// <summary>캡처 투어: [전체]를 고르고 그 땅을 누름.</summary>
        public void SelectAllAndTapForTour(float x, float y)
        {
            Select(AllCorps);
            CommandAt(new Vec(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y)));
        }

        public void MoveForTour(string place, float x, float y)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place) _battle.OrderMove(a.Id, new Vec(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y)));
        }
    }
}
