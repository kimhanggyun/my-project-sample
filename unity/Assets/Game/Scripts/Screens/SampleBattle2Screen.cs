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
    /// 시작 전: 우리 · 적 진형, 부대 수(하나 · 셋), 적 명령 (캡처 투어용, 테스트 탭에서 열면 바로 시작).
    /// 싸우는 중 (R-132, 아래 3단): ① 부대 줄 [전체][부대…] (좌우로 넘김) · ② 명령 줄 (전체 = 위임 · 진군 · 대기 · 후퇴,
    /// 부대 = 위임 · 진군 · 공격 · 이동 · 스킬 · 진형 · 대기 · 후퇴) · ③ 세부 줄 (누른 명령의 선택지, 없으면 고른 부대 정보).
    /// 전장 지름길: 우리 부대를 누르면 고르기, 적 부대 = 공격, 땅 = 이동. 경로는 손가락으로 길을 그린다. 위쪽 [일시정지].
    /// 기존 샘플전투와 따로 (규칙 CrowdBattle, 그리기 CrowdField3D). 보상 없음, 세이브에 남지 않음.
    /// </summary>
    public sealed class SampleBattle2Screen : GameScreen
    {
        private const float TopHeight = 76f;
        private const int AllCorps = -2;
        private static readonly int[] Speeds = { 1, 2, 4 };
        private const float RowHeight = 80f;

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
        private bool _paused;
        private int _speed;
        /// <summary>고른 우리 부대 (Armies 번호), 전체면 AllCorps.</summary>
        private int _selected = AllCorps;
        private readonly Dictionary<int, Button> _corpsButtons = new Dictionary<int, Button>();
        private readonly Dictionary<int, RectTransform> _moraleFills = new Dictionary<int, RectTransform>();
        private readonly Dictionary<string, Button> _formationButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _commandButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _skillButtons = new Dictionary<string, Button>();
        private Button _speedButton, _pauseButton;
        private CrowdHud _hud;
        /// <summary>② 명령 줄에서 펼친 명령 (③ 세부 줄): null · attack · move · skill · formation.</summary>
        private string _cmd;
        /// <summary>다음 누름 · 끌기가 할 일: null · attack (적 고르기) · move · queue (땅마다 예약) · path (길 그리기) · "skill:id" (범위 스킬 지점).</summary>
        private string _pickMode;
        private RectTransform _detailRow, _commandRow, _corpsRow;
        private Text _infoText;
        /// <summary>그리는 중인 길 (전장 좌표).</summary>
        private readonly List<Vec> _trail = new List<Vec>();
        private RectTransform _result;

        /// <summary>[닫기]로 돌아갈 메인 탭 (테스트 탭에서 열면 "test").</summary>
        private readonly string _returnTab;

        /// <param name="speed">시작 배속 (1 · 2 · 4, 시험 탭에서 고름).</param>
        public SampleBattle2Screen(CrowdSetup setup = null, bool startNow = false, string returnTab = "test", int speed = 1)
        {
            _returnTab = returnTab;
            _speed = System.Math.Max(0, System.Array.IndexOf(Speeds, speed));
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
            _pauseButton = Ui.Button(top, "", TogglePause, TopHeight, fontSize: 24);
            Ui.Size(_pauseButton, width: 130);
            _speedButton = Ui.Button(top, "", NextSpeed, TopHeight, fontSize: 26);
            Ui.Size(_speedButton, width: 100);
            Ui.Size(Ui.Button(top, T("ui.sample2.close"), () => App.ShowMain(_returnTab), TopHeight, fontSize: 26), width: 120);

            App.SetBackdrop(false);
            _field = new CrowdField3D(_battle);
            _fieldArea = Ui.Rect(Root, "Field");
            Ui.Size(_fieldArea, flexibleHeight: 1);
            Ui.Background(_fieldArea, new Color(0, 0, 0, 0));
            _fieldArea.gameObject.AddComponent<RectMask2D>(); // 이름표가 위 줄을 덮지 않게 (캡처 39j)
            var gestures = _fieldArea.gameObject.AddComponent<GestureArea>();
            gestures.Tapped += OnFieldTap;
            gestures.Dragged += OnFieldDrag;
            gestures.DragEnded += OnFieldDragEnd;
            gestures.Zoomed += f => _field.Rig.Zoom(f);
            _hud = new CrowdHud(_battle, _field, _fieldArea, TagName);

            _panel = Ui.Rect(Root, "Panel");
            Ui.Background(_panel, Palette.Bar);
            Ui.Column(_panel, 6, 8);
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
            _moraleFills.Clear();
            _commandButtons.Clear();
            _skillButtons.Clear();
            _infoText = null;
            if (!Started)
            {
                // 시작 전: 우리 진형 · 적 진형 · 부대 수 · 적 명령
                Ui.Size(_panel, 470);
                FormationRow(_panel, T("ui.sample2.ally"), _setup.AllyFormation, id => { _setup.AllyFormation = id; Restart(); });
                FormationRow(_panel, T("ui.sample2.enemy"), _setup.EnemyFormation, id => { _setup.EnemyFormation = id; Restart(); });
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
            // 싸우는 중 (R-132): ③ 세부 줄 · ② 명령 줄 · ① 부대 줄 (아래에서 위로 읽으면 고르기 → 명령 → 선택지)
            Ui.Size(_panel, RowHeight * 2 + 112 + 6 * 2 + 16);
            _detailRow = Ui.Row(_panel, "Detail", RowHeight, 6);
            _commandRow = Ui.Row(_panel, "Commands", RowHeight, 6);
            _corpsRow = Ui.HScrollRow(_panel, "Corps", 112, 8, 4);
            var mine = new List<CrowdArmy>(_battle.TeamArmies(CrowdBattle.Ally));
            if (mine.Count > 1) CorpsButton(AllCorps, null); // 부대가 하나면 [전체]는 필요 없음 (R-139, 2026-11-22 사용자)
            else if (mine.Count == 1 && _selected == AllCorps) _selected = mine[0].Id;
            foreach (var a in mine) CorpsButton(a.Id, a);
            Select(_selected);
        }

        /// <summary>① 부대 줄의 버튼: 이름 (★ 총대장 · 병종) · 사기 숫자 · 아래 사기 막대 (병종 색 띠).</summary>
        private void CorpsButton(int id, CrowdArmy a)
        {
            var b = Ui.Button(_corpsRow, a == null ? T("ui.sample2.all") : CorpsName(a), () => Select(id), 104, Palette.Button, a == null ? 26 : 20);
            Ui.Size(b, 104, a == null ? 150 : 200);
            _corpsButtons[id] = b;
            if (a == null) return;
            var back = Ui.Rect(b.transform, "Morale");
            back.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            back.anchorMin = new Vector2(0f, 0f);
            back.anchorMax = new Vector2(1f, 0f);
            back.pivot = new Vector2(0.5f, 0f);
            back.offsetMin = new Vector2(10f, 6f);
            back.offsetMax = new Vector2(-10f, 14f);
            Ui.Background(back, new Color(0f, 0f, 0f, 0.45f)).raycastTarget = false;
            var fill = Ui.Rect(back, "Fill");
            fill.anchorMin = Vector2.zero; // 왼쪽부터 채움 (캡처: 가운데부터 차 보임)
            Ui.Background(fill, TroopColor(a.Troop.BaseId)).raycastTarget = false;
            Ui.SetGauge(fill, 1f);
            _moraleFills[id] = fill;
        }

        /// <summary>병종 색 (부대 줄 사기 막대): 보병 회갈 · 창병 초록 · 궁병 노랑 · 기병 하늘 · 정찰 보라.</summary>
        private static Color TroopColor(string baseId)
        {
            switch (baseId)
            {
                case "spearman": return new Color(0.45f, 0.78f, 0.45f);
                case "archer": return new Color(0.95f, 0.8f, 0.35f);
                case "cavalry": return new Color(0.45f, 0.75f, 0.95f);
                case "scout": return new Color(0.75f, 0.55f, 0.9f);
                default: return new Color(0.85f, 0.75f, 0.65f);
            }
        }

        private void Choice(RectTransform row, string text, bool on, System.Action act) =>
            Ui.Size(Ui.Button(row, text, act, 68, on ? Palette.ButtonActive : Palette.Button, 22), flexibleWidth: 1);

        /// <summary>장수 이름표: 우리 = "★중군 보병", 적 = "적 중군 창병", 위임이면 "· 자동".</summary>
        private string TagName(CrowdArmy a)
        {
            string s = (a.Team == CrowdBattle.Enemy ? T("ui.sample2.enemyTag") + " " : "") + (a.IsCommander ? "★" : "") + PlaceName(a) + " " + T("troop." + a.Troop.Id)
                + " · " + T("ui.crowd.general." + a.GeneralInfo.Preset); // 장수 성향 (R-138)
            if (a.GeneralEscaping && a.General.Alive) s += " · " + T("ui.crowd.generalEscaping");
            else if (a.GeneralPulledBack && a.General.Alive && !a.Routed) s += " · " + T("ui.crowd.generalCommanding");
            if (a.SiegeTask != null) s += " · " + SiegeLabel(a, a.SiegeTask); // 공성 명령 (R-145)
            if (a.Team == CrowdBattle.Ally && a.Delegated) s += " · " + T("ui.sample2.auto");
            if (a.Marching) s += " · " + T("ui.sample2.marching");
            // 켜져 있는 스킬 (R-130)
            int tick = _battle.Tick;
            if (a.AttackBuffUntil > tick) s += " · " + T("skill.warCry");
            if (a.FortifyUntil > tick) s += " · " + T("skill.holdTheLine");
            if (a.MarchUntil > tick) s += " · " + T("skill.forcedMarch");
            return s;
        }

        // ---- ② 명령 줄 · ③ 세부 줄 (R-132) ----

        private void BuildCommands()
        {
            if (_commandRow == null) return;
            Ui.Clear(_commandRow);
            _commandButtons.Clear();
            bool all = _selected == AllCorps;
            int font = all ? 26 : 22;
            CommandButton("delegate", DelegateLabel(), ToggleDelegate, font);
            _siegeShown = _battle.SiegeActive;
            if (_siegeShown)
            {
                // 공성 명령 줄 (R-145): 성문이 부서지기 전까지 — 부대 병종 · 편에 따라 다름, [전체]는 병종대로 기본 명령
                if (all) CommandButton("siege_default", T("ui.sample2.siege.default"), () => Order(a => _battle.OrderSiege(a.Id, _battle.DefaultSiegeTask(a))), font);
                else
                {
                    var mine = Chosen();
                    if (mine.Count > 0)
                        foreach (var task in _battle.SiegeTasksFor(mine[0].Id))
                        {
                            string t = task;
                            CommandButton("siege_" + t, SiegeLabel(mine[0], t), () => Order(a => _battle.OrderSiege(a.Id, t)), font);
                        }
                    CommandButton("formation", T("ui.sample2.cmd.formation"), () => OpenCommand("formation"), font);
                }
                CommandButton("hold", T("ui.sample2.cmd.hold"), () => Order(a => _battle.OrderHold(a.Id)), font);
                CommandButton("retreat", T("ui.sample2.cmd.retreat"), () => Order(a => _battle.OrderRetreat(a.Id)), font);
                RecolorCommands();
                BuildDetail();
                return;
            }
            CommandButton("advance", T("ui.sample2.cmd.advance"), () => Order(a => _battle.OrderAdvance(a.Id)), font);
            if (!all)
            {
                CommandButton("attack", T("ui.sample2.cmd.attack"), () => OpenCommand("attack"), font);
                CommandButton("move", T("ui.sample2.cmd.move"), () => OpenCommand("move"), font);
                CommandButton("skill", T("ui.sample2.cmd.skill"), () => OpenCommand("skill"), font);
                CommandButton("formation", T("ui.sample2.cmd.formation"), () => OpenCommand("formation"), font);
            }
            CommandButton("hold", T("ui.sample2.cmd.hold"), () => Order(a => _battle.OrderHold(a.Id)), font);
            CommandButton("retreat", T("ui.sample2.cmd.retreat"), () => Order(a => _battle.OrderRetreat(a.Id)), font);
            RecolorCommands();
            BuildDetail();
        }

        /// <summary>공성 명령 이름 (성벽: 궁병은 "성벽 배치", 보병 · 창병은 "성벽 지키기").</summary>
        private string SiegeLabel(CrowdArmy a, string task) =>
            T(task == SiegeTasks.Wall && !a.Troop.Ranged ? "ui.sample2.siege.wallGuard" : "ui.sample2.siege." + task);

        private bool _siegeShown;

        private void CommandButton(string id, string text, System.Action act, int font)
        {
            var b = Ui.Button(_commandRow, text, act, RowHeight, Palette.Button, font);
            Ui.Size(b, flexibleWidth: 1);
            _commandButtons[id] = b;
        }

        private string DelegateLabel()
        {
            var c = Chosen();
            bool on = c.Count > 0 && c.TrueForAll(a => a.Delegated);
            return T("ui.sample2.cmd.delegate") + "\n" + T(on ? "ui.sample2.on" : "ui.sample2.off");
        }

        /// <summary>명령 버튼 색: 펼친 명령 · 위임 켬 · 고른 부대가 모두 그 명령 중이면 밝게.</summary>
        private void RecolorCommands()
        {
            var c = Chosen();
            bool Every(string order) => c.Count > 0 && c.TrueForAll(a => a.Order == order && !a.Delegated);
            foreach (var kv in _commandButtons)
            {
                bool on = kv.Key == _cmd
                    || (kv.Key == "delegate" && c.Count > 0 && c.TrueForAll(a => a.Delegated))
                    || (kv.Key == "advance" && Every(CrowdOrders.Advance))
                    || (kv.Key == "hold" && Every(CrowdOrders.Hold))
                    || (kv.Key == "retreat" && Every(CrowdOrders.Retreat))
                    || (kv.Key.StartsWith("siege_") && c.Count > 0 && c.TrueForAll(a => a.SiegeTask == kv.Key.Substring(6) && !a.Delegated));
                Ui.Recolor(kv.Value, on ? Palette.ButtonActive : Palette.Button);
            }
            if (_commandButtons.TryGetValue("delegate", out var d)) d.GetComponentInChildren<Text>().text = DelegateLabel();
        }

        private void OpenCommand(string cmd)
        {
            EndPick(false);
            _cmd = _cmd == cmd ? null : cmd; // 다시 누르면 접기
            RecolorCommands();
            BuildDetail();
        }

        /// <summary>③ 세부 줄: 고르는 중이면 안내 + [취소], 펼친 명령이면 선택지, 아니면 고른 부대 정보.</summary>
        private void BuildDetail()
        {
            if (_detailRow == null) return;
            Ui.Clear(_detailRow);
            _formationButtons.Clear();
            _skillButtons.Clear();
            _infoText = null;
            var one = _selected >= 0 ? _battle.Armies[_selected] : null;
            if (_pickMode != null)
            {
                string hint = _pickMode == "attack" ? "ui.sample2.pickAttack" : _pickMode == "queue" ? "ui.sample2.queueHint"
                    : _pickMode == "path" ? "ui.sample2.pathHint" : _pickMode == "march" ? "ui.sample2.marchHint" : _pickMode.StartsWith("skill:") ? "ui.sample2.skillHint" : "ui.sample2.moveHint";
                Ui.Size(Ui.Label(_detailRow, T(hint), 22, TextAnchor.MiddleCenter, Palette.Text), flexibleWidth: 3);
                Ui.Size(Ui.Button(_detailRow, T(_pickMode == "queue" ? "ui.sample2.queueDone" : "ui.sample2.cancel"), () => EndPick(), RowHeight,
                    _pickMode == "queue" ? Palette.ButtonActive : Palette.Button, 24), width: 200);
                return;
            }
            if (one != null && _cmd == "attack")
            {
                Detail(T("ui.sample2.attack.pick"), () => BeginPick("attack"));
                Detail(T("ui.sample2.attack.nearest"), () => Order(a => _battle.OrderAttackNearest(a.Id)));
                return;
            }
            if (one != null && _cmd == "move")
            {
                Detail(T("ui.sample2.move.move"), () => BeginPick("move"));
                Detail(T("ui.sample2.queue"), () => BeginPick("queue"));
                Detail(T("ui.sample2.move.path"), () => BeginPick("path"));
                Detail(T("ui.sample2.move.march"), () => BeginPick("march")); // 행군 이동 (R-137): 이번 이동만 줄지어 (좁은 길 · 숲)
                return;
            }
            if (one != null && _cmd == "skill")
            {
                bool any = false;
                foreach (var id in CrowdBattle.KnownSkills)
                {
                    if (!one.SkillUses.TryGetValue(id, out var left)) continue;
                    any = true;
                    string sk = id;
                    var b = Detail(T(_battle.SkillDef(id).NameKey) + "\n" + T("ui.sample2.uses", left), () => UseSkill(sk), 20);
                    _skillButtons[id] = b;
                }
                if (!any) Ui.Label(_detailRow, T("ui.sample2.noSkill"), 22, TextAnchor.MiddleCenter, Palette.TextDim);
                RefreshSkills();
                return;
            }
            if (one != null && _cmd == "formation")
            {
                _battle.Rules.RecommendedFormation.TryGetValue(one.Troop.BaseId, out var rec);
                // 진형 아이콘 11개 (R-141, 이름 없이 모양) — 병종 추천 진형엔 ★
                foreach (var id in CrowdFormations.All)
                {
                    string f = id;
                    var b = Detail("", () => { foreach (var a in Chosen()) _battle.SetFormation(a.Id, f); RecolorFormations(); }, 20);
                    FormationIcon.Add(b.transform, id, new Color(0.85f, 0.18f, 0.16f), 54f, 5f);
                    if (id == rec) Star(b.transform);
                    _formationButtons[id] = b;
                }
                RecolorFormations();
                return;
            }
            _infoText = Ui.Label(_detailRow, "", 22, TextAnchor.MiddleCenter, Palette.Text);
            Ui.Size(_infoText, flexibleWidth: 1);
            RefreshInfo();
        }

        /// <summary>추천 표시 ★ (버튼 오른쪽 위).</summary>
        private static void Star(Transform button)
        {
            var star = Ui.Label(button, "★", 18, TextAnchor.UpperRight, new Color(0.95f, 0.72f, 0.15f));
            star.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var r = star.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(2f, 2f); r.offsetMax = new Vector2(-4f, -2f);
            star.raycastTarget = false;
        }

        private Button Detail(string text, System.Action act, int font = 24)
        {
            var b = Ui.Button(_detailRow, text, act, RowHeight, Palette.Button, font);
            Ui.Size(b, flexibleWidth: 1);
            return b;
        }

        private void RecolorFormations()
        {
            string f = CurrentFormation();
            foreach (var kv in _formationButtons) Ui.Recolor(kv.Value, kv.Key == f ? Palette.ButtonActive : Palette.Button);
        }

        private void RefreshSkills()
        {
            if (_selected < 0) return;
            var a = _battle.Armies[_selected];
            foreach (var kv in _skillButtons)
            {
                Ui.SetEnabled(kv.Value, _battle.CanUseSkill(a.Id, kv.Key));
                a.SkillUses.TryGetValue(kv.Key, out var left);
                kv.Value.GetComponentInChildren<Text>().text = T(_battle.SkillDef(kv.Key).NameKey) + "\n" + T("ui.sample2.uses", left);
            }
        }

        /// <summary>③ 정보: 부대 하나 = 병종 · 병사 · 사기 · 명령 (위임이면 자동), 전체 = 부대 수 · 병사 · 위임 수.</summary>
        private void RefreshInfo()
        {
            if (_infoText == null) return;
            if (_selected >= 0)
            {
                var a = _battle.Armies[_selected];
                string order = a.Delegated ? T("ui.sample2.auto") : T("ui.sample2.order." + a.Order);
                _infoText.text = T("ui.sample2.info", T("troop." + a.Troop.Id), a.AliveCount, a.StartCount, MoraleText(a), order);
                return;
            }
            int n = 0, soldiers = 0, auto = 0;
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
            {
                if (!_battle.Standing(a)) continue;
                n++; soldiers += a.AliveCount;
                if (a.Delegated) auto++;
            }
            _infoText.text = T("ui.sample2.allInfo", n, soldiers, auto);
        }

        /// <summary>고른 부대들에게 명령 (위임은 꺼진다 — 직접 명령이 우선).</summary>
        private void Order(System.Action<CrowdArmy> act)
        {
            EndPick(false);
            foreach (var a in Chosen()) { _battle.SetDelegated(a.Id, false); act(a); }
            _cmd = null;
            RecolorCommands();
            BuildDetail();
        }

        /// <summary>위임 켜기 · 끄기: 하나라도 꺼져 있으면 모두 켬, 다 켜져 있으면 모두 끔.</summary>
        private void ToggleDelegate()
        {
            var c = Chosen();
            bool on = !c.TrueForAll(a => a.Delegated);
            foreach (var a in c) _battle.SetDelegated(a.Id, on);
            RecolorCommands();
            RefreshInfo();
        }

        private void UseSkill(string skill)
        {
            if (_selected < 0) return;
            if (skill == CrowdBattle.ArrowRain) { BeginPick("skill:" + skill); return; }
            if (!_battle.UseSkill(_selected, skill)) Toast.Show(T("ui.sample2.cantSkill"), bad: true);
            RefreshSkills();
        }

        private void BeginPick(string mode)
        {
            _pickMode = mode;
            _trail.Clear();
            _field.Trail.Clear();
            BuildDetail();
        }

        private void EndPick(bool rebuild = true)
        {
            _pickMode = null;
            _trail.Clear();
            _field?.Trail.Clear();
            if (rebuild) BuildDetail();
        }

        private string CorpsName(CrowdArmy a) => PlaceName(a) + (a.IsCommander ? "★" : "") + " " + T("troop." + a.Troop.Id)
            + (a.Delegated ? " · " + T("ui.sample2.auto") : "") + (a.Marching ? " · " + T("ui.sample2.marching") : "") + "\n" + MoraleText(a);

        /// <summary>부대 자리 이름: 좌군 · 중군 · 우군, 3×3 칸이면 "앞 좌" 등 (R-131).</summary>
        private string PlaceName(CrowdArmy a)
        {
            int k = a.Place.IndexOf('_');
            if (k < 0) return T("ui.sample2.place." + a.Place);
            return TestBattleTabView.CellName(Ctx, a.Place.Substring(0, k), a.Place.Substring(k + 1));
        }

        /// <summary>사기 (R-128): 숫자, 흔들림 · 포위 · 무너짐 표시.</summary>
        private string MoraleText(CrowdArmy a)
        {
            if (a.Routed) return Ui.Colored(T("ui.sample2.routed"), Palette.Bad);
            if (a.GeneralEscaping && a.General.Alive) return Ui.Colored(T("ui.crowd.generalEscaping"), Palette.Bad); // 장수 혼자 빠지는 중 (부대는 남음)
            if (!a.General.Alive) return Ui.Colored(T("ui.sample2.noGeneral"), Palette.Bad);
            string m = T("ui.sample2.morale", System.Math.Max(0, (int)a.Morale.FloorToLong()));
            if (a.GeneralPulledBack) m += " " + T("ui.crowd.generalCommanding");
            if (a.Encircled) m += " " + T("ui.sample2.encircled");
            else if (a.Wavering) m += " " + T("ui.sample2.wavering");
            return a.Wavering || a.Encircled ? Ui.Colored(m, Palette.Bad) : m;
        }

        private void FormationRow(RectTransform parent, string label, string current, System.Action<string> pick)
        {
            var row = Ui.Row(parent, "Formation " + label, 68, 6);
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
            bool changed = id != _selected;
            _selected = id;
            _field.SelectedArmy = id == AllCorps ? -1 : id;
            _field.SelectAll = id == AllCorps;
            foreach (var kv in _corpsButtons) Ui.Recolor(kv.Value, kv.Key == id ? Palette.ButtonActive : Palette.Button);
            if (changed || _commandRow == null || _commandRow.childCount == 0)
            {
                _pickMode = null;
                _trail.Clear();
                _field.Trail.Clear();
                if (id == AllCorps) _cmd = null; // 전체는 펼칠 명령이 없다
                BuildCommands();
            }
        }

        /// <summary>전장을 누름: 우리 부대 → 고르기, 적 부대 → 공격, 땅 → 이동 (전체면 부대 사이를 지키며 같이).</summary>
        private void OnFieldTap(Vector2 screen)
        {
            if (!Started || _battle.IsOver) return;
            if (!_field.ScreenToGround(screen, out var world)) return;
            CommandAt(ToField(world));
        }

        private static Vec ToField(Vector3 world) => new Vec(Fix.FromDecimal((decimal)world.x), Fix.FromDecimal((decimal)world.z));

        /// <summary>전장 한 곳을 누른 것과 같다 (화면 누름 · 캡처 투어).</summary>
        private void CommandAt(Vec point)
        {
            var hit = ArmyAt(point);
            if (hit != null && hit.Team == CrowdBattle.Ally && _pickMode == null)
            {
                if (hit.Leading) Select(hit.Id); // 우리 부대를 누르면 고르기
                return;
            }
            if (_pickMode == "path") return; // 길은 끌어서 그린다
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
            if (_pickMode == "attack" && (hit == null || hit.Team == CrowdBattle.Ally)) { Toast.Show(T("ui.sample2.pickAttack")); return; }
            if (hit != null && hit.Team != CrowdBattle.Ally) { int t = hit.Id; Order(a => _battle.OrderAttack(a.Id, t)); return; }
            bool queue = _pickMode == "queue";
            bool march = _pickMode == "march";
            foreach (var a in chosen) _battle.SetDelegated(a.Id, false);
            if (chosen.Count == 1) _battle.OrderMove(chosen[0].Id, point, queue, march);
            else
            {
                // 전체 이동: 총대장 부대(없으면 첫 부대)를 그 자리로, 나머지는 지금 떨어진 만큼 그대로
                var lead = chosen[0];
                foreach (var a in chosen) if (a.IsCommander) lead = a;
                var from = lead.Center;
                foreach (var a in chosen) _battle.OrderMove(a.Id, point + (a.Center - from), queue, march);
            }
            if (!queue) { _pickMode = null; _cmd = null; }
            RecolorCommands();
            BuildDetail();
        }

        // ---- 경로 그리기 (R-132, 기존 전투 경로를 옮김) ----

        /// <summary>전장 끌기: 길 그리는 중이면 손가락 자취를 모으고 (지도는 그대로), 아니면 지도 이동.</summary>
        private void OnFieldDrag(Vector2 a, Vector2 b)
        {
            if (_pickMode != "path") { _field.Rig.Pan(a, b); return; }
            if (_trail.Count == 0 && _field.ScreenToGround(a, out var w0)) AddTrail(ToField(w0));
            if (_field.ScreenToGround(b, out var w)) AddTrail(ToField(w));
        }

        private void AddTrail(Vec p)
        {
            if (_trail.Count > 0 && (p - _trail[_trail.Count - 1]).LengthSquared < Fix.FromRatio(1, 4)) return; // 0.5칸보다 가까우면 건너뜀
            _trail.Add(p);
            _field.Trail.Add(_field.ToWorld(p));
        }

        /// <summary>
        /// 길 그리기를 마침: 자취를 길이대로 고르게 MaxPathPoints 점으로 나눠 그 길로 (손을 떼면 바로).
        /// 끝이 적 부대 위면 마지막 점 대신 도착 뒤 그 부대를 공격.
        /// </summary>
        private void OnFieldDragEnd()
        {
            if (_pickMode != "path") return;
            if (_trail.Count < 2 || _selected < 0) { _trail.Clear(); _field.Trail.Clear(); return; }
            var end = ArmyAt(_trail[_trail.Count - 1]);
            int then = end != null && end.Team != CrowdBattle.Ally ? end.Id : -1;
            var points = Resample(_trail, CrowdBattle.MaxPathPoints);
            if (then >= 0 && points.Count > 1) points.RemoveAt(points.Count - 1);
            int sel = _selected;
            Order(a => _battle.OrderPath(sel, points, then));
        }

        /// <summary>자취를 길이대로 고르게 k점으로 (첫 점 = 처음에서 1/k만큼 간 곳, 끝 점 = 자취 끝).</summary>
        private static List<Vec> Resample(List<Vec> trail, int k)
        {
            var cum = new List<Fix> { Fix.Zero };
            for (int i = 1; i < trail.Count; i++) cum.Add(cum[i - 1] + (trail[i] - trail[i - 1]).Length);
            var total = cum[cum.Count - 1];
            var pts = new List<Vec>();
            if (total < Fix.OneValue) { pts.Add(trail[trail.Count - 1]); return pts; }
            int j = 0;
            for (int s = 1; s <= k; s++)
            {
                var want = total * Fix.FromInt(s) / Fix.FromInt(k);
                while (j < cum.Count - 1 && cum[j] < want) j++;
                if (pts.Count == 0 || !pts[pts.Count - 1].Equals(trail[j])) pts.Add(trail[j]);
            }
            return pts;
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
            _pickMode = null;
            _cmd = null;
            _accumulator = 0f;
            _running = false;
            _selected = AllCorps;
            BuildPanel();
            UpdateTexts();
        }

        private void NextSpeed() { _speed = (_speed + 1) % Speeds.Length; UpdateTexts(); }

        /// <summary>일시정지: 멈춘 채로 명령을 줄 수 있다 (R-132).</summary>
        private void TogglePause() { _paused = !_paused; UpdateTexts(); }

        private void UpdateTexts()
        {
            if (_commandRow != null && _siegeShown != _battle.SiegeActive) BuildCommands(); // 성문이 부서지면 야전 명령 줄로 (R-145)
            var eg = _battle.Commander(CrowdBattle.Enemy).General;
            string head = T("ui.sample2.count", _battle.AliveCount(CrowdBattle.Ally), _battle.AliveCount(CrowdBattle.Enemy), eg.Hp, eg.MaxHp);
            if (_battle.IsOver)
                head = T(_battle.Winner == CrowdBattle.Ally ? "ui.sample2.win" : _battle.Winner == CrowdBattle.Enemy ? "ui.sample2.lose" : "ui.sample2.draw")
                    + " · " + T("ui.sample2.end." + _battle.EndReason) + "  " + head;
            else if (_paused) head = Ui.Colored(T("ui.sample2.paused"), Palette.Bad) + "  " + head;
            _status.text = head + "  " + T("ui.sample2.enemyMorale", System.Math.Max(0, (int)_battle.Commander(CrowdBattle.Enemy).Morale.FloorToLong()));
            foreach (var kv in _corpsButtons)
            {
                if (kv.Key < 0) continue;
                var a = _battle.Armies[kv.Key];
                kv.Value.GetComponentInChildren<Text>().text = CorpsName(a);
                if (!_battle.Standing(a) || a.Routed) Ui.Recolor(kv.Value, Palette.ButtonDisabled); // 무너지거나 쓰러진 부대는 회색
                if (_moraleFills.TryGetValue(kv.Key, out var fill)) Ui.SetGauge(fill, (float)(a.Morale / _battle.Rules.MoraleStart).ToDisplayDouble());
            }
            _speedButton.GetComponentInChildren<Text>().text = "×" + Speeds[_speed];
            _pauseButton.GetComponentInChildren<Text>().text = T(_paused ? "ui.sample2.resume" : "ui.sample2.pause");
            Ui.Recolor(_pauseButton, _paused ? Palette.ButtonActive : Palette.Button);
            RefreshInfo();
            if (_cmd == "skill") RefreshSkills();
            if (_commandButtons.Count > 0) RecolorCommands();
        }

        public override void Tick(float deltaTime)
        {
            if (_battle == null) return;
            bool stepping = _running && !_paused && !_battle.IsOver;
            if (stepping)
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
            if (_battle.IsOver && Started && _result == null) ShowResult();
            _fieldArea.GetWorldCorners(_corners);
            _field.SetViewport(new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y));
            _field.Draw(stepping ? Mathf.Clamp01(_accumulator / _tickSeconds) : 1f, _paused ? 0f : deltaTime); // 멈추면 흙먼지 · 흔들림도 멈춤
            _hud.Tick(deltaTime);
        }

        /// <summary>끝 창 (R-132, [다시] 버튼 대신): 결과 · [같은 설정으로 다시] · [돌아가기].</summary>
        private void ShowResult()
        {
            UpdateTexts();
            _result = Ui.Rect(_fieldArea, "Result");
            _result.anchorMin = _result.anchorMax = new Vector2(0.5f, 0.5f);
            _result.pivot = new Vector2(0.5f, 0.5f);
            _result.sizeDelta = new Vector2(640f, 340f);
            Ui.Background(_result, Palette.PromptBack);
            Ui.Column(_result, 14, 24);
            string title = T(_battle.Winner == CrowdBattle.Ally ? "ui.sample2.win" : _battle.Winner == CrowdBattle.Enemy ? "ui.sample2.lose" : "ui.sample2.draw");
            Ui.Label(_result, title, 44, TextAnchor.MiddleCenter, _battle.Winner == CrowdBattle.Ally ? Palette.Good : Palette.Bad);
            Ui.Label(_result, T("ui.sample2.end." + _battle.EndReason) + " · " + T("ui.sample2.resultCount", _battle.AliveCount(CrowdBattle.Ally), _battle.AliveCount(CrowdBattle.Enemy)),
                24, TextAnchor.MiddleCenter, Palette.PromptText);
            Ui.Button(_result, T("ui.sample2.result.again"), Again, 84, Palette.ButtonActive, 28);
            Ui.Button(_result, T(_returnTab == "test" ? "ui.sample2.result.backTest" : "ui.sample2.result.back"), () => App.ShowMain(_returnTab), 76, Palette.Button, 26);
        }

        /// <summary>같은 설정 · 새 시드로 다시 (바로 시작).</summary>
        private void Again()
        {
            _setup.Seed = 0;
            App.Show(new SampleBattle2Screen(_setup, true, _returnTab));
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

        /// <summary>캡처 투어: 그 자리 우리 부대 장수 가까이로 카메라.</summary>
        public void FocusPlaceGeneralForTour(string place, float distance)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place) { _field.Rig.LookAt(_field.ToWorld(a.General.Pos)); _field.Rig.SetDistance(distance); }
        }

        /// <summary>캡처 투어: 그 자리 우리 부대 장수 체력을 pct%로 (물러나 지휘 장면).</summary>
        public void WoundGeneralForTour(string place, int pct)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place && a.General.Alive) a.General.Hp = System.Math.Max(1, a.General.MaxHp * pct / 100);
        }

        /// <summary>캡처 투어: 그 자리 우리 부대 진형 바꾸기 (진형 버튼과 같음).</summary>
        public void FormationForTour(string place, string formation)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place) _battle.SetFormation(a.Id, formation);
            RecolorFormations();
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

        /// <summary>캡처 투어: 우리 부대를 고르고 명령 줄의 명령을 펼친다 (attack · move · skill · formation, null = 정보).</summary>
        public void OpenCommandForTour(string place, string cmd)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally)) if (a.Place == place) Select(a.Id);
            if (cmd != null) OpenCommand(cmd);
        }

        /// <summary>캡처 투어: 명령 줄 버튼 누르기 (advance · hold · retreat · delegate), place null = 전체.</summary>
        public void PressForTour(string place, string cmd)
        {
            if (place == null) Select(AllCorps);
            else foreach (var a in _battle.TeamArmies(CrowdBattle.Ally)) if (a.Place == place) Select(a.Id);
            if (cmd == "delegate") ToggleDelegate();
            else if (cmd == "advance") Order(a => _battle.OrderAdvance(a.Id));
            else if (cmd == "hold") Order(a => _battle.OrderHold(a.Id));
            else if (cmd == "retreat") Order(a => _battle.OrderRetreat(a.Id));
            else if (cmd == "nearest") Order(a => _battle.OrderAttackNearest(a.Id));
        }

        /// <summary>캡처 투어: 길 그리기 (점들을 끌어 그린 것처럼, 놓기 전 = 그리는 중 모습).</summary>
        public void DrawPathForTour(string place, float[] xy, bool release)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally)) if (a.Place == place) Select(a.Id);
            OpenCommand("move");
            BeginPick("path");
            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                // 점 사이를 촘촘히 (손가락 자취처럼)
                var p = new Vec(Fix.FromDecimal((decimal)xy[i]), Fix.FromDecimal((decimal)xy[i + 1]));
                if (_trail.Count > 0)
                {
                    var from = _trail[_trail.Count - 1];
                    for (int k = 1; k <= 8; k++) AddTrail(from + (p - from) * Fix.FromRatio(k, 8));
                }
                else AddTrail(p);
            }
            if (release) OnFieldDragEnd();
        }

        public void ReleasePathForTour() => OnFieldDragEnd();

        public void PauseForTour(bool paused) { _paused = paused; UpdateTexts(); }

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
                    int t = _battle.NearestEnemyArmy(a.Id);
                    if (t >= 0) { var c = _battle.Armies[t].Centroid(); return new Vector2((float)c.X.ToDisplayDouble(), (float)c.Y.ToDisplayDouble()); }
                }
            return Vector2.zero;
        }

        /// <summary>캡처 투어: 그 자리 우리 부대에 공성 명령 (R-145) — 고른 부대로 만들어 명령 줄도 그 부대 것으로.</summary>
        public void SiegeForTour(string place, string task)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place) { _battle.OrderSiege(a.Id, task); SelectForTour(place); }
            BuildCommands();
        }

        /// <summary>캡처 투어: 첫 사다리 가운데 (발과 꼭대기 사이)로 카메라. 사다리가 없으면 그대로.</summary>
        public void FocusLadderForTour(float distance)
        {
            if (_battle.Ladders.Count == 0) return;
            var l = _battle.Ladders[0];
            _field.Rig.LookAt((_field.ToWorld(l.Foot) + _field.ToWorld(l.Top)) / 2f);
            _field.Rig.SetDistance(distance);
        }

        public bool BreachedForTour => _battle.BreachTick >= 0;

        /// <summary>캡처 투어: 성문을 누가 쳤음.</summary>
        public bool GateHitForTour { get { foreach (var g in _battle.Gates) if (g.LastHitTick >= 0) return true; return false; } }

        /// <summary>캡처 투어: 가장 최근에 맞은 성문(없으면 첫 성문)으로 카메라.</summary>
        public void FocusGateForTour(float distance)
        {
            Game.Battle.Model.Crowd.CrowdGate best = null;
            foreach (var g in _battle.Gates) if (best == null || g.LastHitTick > best.LastHitTick) best = g;
            if (best == null) return;
            _field.Rig.LookAt(_field.ToWorld(best.Center));
            _field.Rig.SetDistance(distance);
        }

        /// <summary>캡처 투어: 성문이 (하나라도) 열림.</summary>
        public bool GateOpenForTour { get { foreach (var g in _battle.Gates) if (g.Open) return true; return false; } }

        /// <summary>캡처 투어: 적 총대장 부대가 무너짐.</summary>
        public bool EnemyRoutedForTour => _battle.Commander(CrowdBattle.Enemy).Routed;

        /// <summary>캡처 투어: 이번 틱에 부딪침이 있음.</summary>
        public bool ImpactForTour => _battle.ImpactsThisTick > 0;

        /// <summary>캡처 투어: 끝났음.</summary>
        public bool OverForTour => _battle.IsOver;

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

        public void MoveForTour(string place, float x, float y, bool march = false)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place) _battle.OrderMove(a.Id, new Vec(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y)), march: march);
        }
    }
}
