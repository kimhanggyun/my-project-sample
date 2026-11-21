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

        public SampleBattle2Screen(CrowdSetup setup = null, bool startNow = false)
        {
            _setup = setup ?? new CrowdSetup { AllyFormation = CrowdFormations.Wedge, EnemyFormation = CrowdFormations.Line, AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false };
            _startNow = startNow;
        }

        public override void Build()
        {
            var rules = CrowdRules.Load(Ctx.Data);
            if (_setup.Seed <= 1) _setup.Seed = (ulong)Ctx.NewSeed();
            _battle = new CrowdBattle(rules, _setup);
            _tickSeconds = (float)rules.TickSeconds.ToDisplayDouble();
            _running = _startNow;

            Ui.Column(Root, 6, 8);
            var top = Ui.Row(Root, "Top", TopHeight, 8);
            Ui.Background(top, Palette.Bar);
            _status = Ui.Label(top, "", 26, TextAnchor.MiddleLeft);
            Ui.Size(_status, flexibleWidth: 3);
            _speedButton = Ui.Button(top, "", NextSpeed, TopHeight, fontSize: 26);
            Ui.Size(_speedButton, width: 110);
            Ui.Size(Ui.Button(top, T("ui.sample2.close"), () => App.ShowMain("sample"), TopHeight, fontSize: 26), width: 130);

            App.SetBackdrop(false);
            _field = new CrowdField3D(_battle);
            _fieldArea = Ui.Rect(Root, "Field");
            Ui.Size(_fieldArea, flexibleHeight: 1);
            Ui.Background(_fieldArea, new Color(0, 0, 0, 0));
            var gestures = _fieldArea.gameObject.AddComponent<GestureArea>();
            gestures.Tapped += OnFieldTap;
            gestures.Dragged += (a, b) => _field.Rig.Pan(a, b);
            gestures.Zoomed += f => _field.Rig.Zoom(f);

            _panel = Ui.Rect(Root, "Panel");
            Ui.Background(_panel, Palette.Bar);
            Ui.Column(_panel, 8, 10);
            Ui.Size(_panel, 400);
            BuildPanel();
            UpdateTexts();
        }

        private bool Started => _running || _battle.Tick > 0;

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
            var pick = Ui.Row(_panel, "Pick", 72, 6);
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
            {
                int id = a.Id;
                var b = Ui.Button(pick, CorpsName(a), () => Select(id), 72, Palette.Button, 24);
                Ui.Size(b, flexibleWidth: 1);
                _corpsButtons[id] = b;
            }
            var all = Ui.Button(pick, T("ui.sample2.all"), () => Select(AllCorps), 72, Palette.Button, 24);
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
            })), 76, Palette.Button, 26), flexibleWidth: 1);
            Ui.Label(_panel, T("ui.sample2.hint"), 22, TextAnchor.MiddleCenter, Palette.TextDim);
            Select(_selected);
        }

        private void Choice(RectTransform row, string text, bool on, System.Action act) =>
            Ui.Size(Ui.Button(row, text, act, 68, on ? Palette.ButtonActive : Palette.Button, 22), flexibleWidth: 1);

        private string CorpsName(CrowdArmy a) => T("ui.sample2.place." + a.Place) + (a.IsCommander ? "★" : "");

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
            var point = new Vec(Fix.FromDecimal((decimal)world.x), Fix.FromDecimal((decimal)world.z));
            var hit = ArmyAt(point);
            if (hit != null && hit.Team == CrowdBattle.Ally) { if (hit.Leading) Select(hit.Id); return; }
            var chosen = Chosen();
            if (chosen.Count == 0) return;
            if (hit != null) { foreach (var a in chosen) _battle.OrderAttack(a.Id, hit.Id); return; }
            if (chosen.Count == 1) { _battle.OrderMove(chosen[0].Id, point); return; }
            // 전체 이동: 총대장 부대(없으면 첫 부대)를 그 자리로, 나머지는 지금 떨어진 만큼 그대로
            var lead = chosen[0];
            foreach (var a in chosen) if (a.IsCommander) lead = a;
            var from = lead.Center;
            foreach (var a in chosen) _battle.OrderMove(a.Id, point + (a.Center - from));
        }

        /// <summary>그 자리를 덮는 부대 (살아 있는 병사 가운데에서 대열 크기 + 1칸 안, 가장 가까운 것).</summary>
        private CrowdArmy ArmyAt(Vec p)
        {
            CrowdArmy best = null;
            Fix bestD = Fix.Zero;
            foreach (var a in _battle.Armies)
            {
                if (!_battle.Standing(a)) continue;
                Fix d = (a.Centroid() - p).Length;
                Fix r = Fix.Max(a.HalfWidth, a.HalfDepth) + Fix.OneValue;
                if (d <= r && (best == null || d < bestD)) { best = a; bestD = d; }
            }
            return best;
        }

        /// <summary>시작 전 설정을 바꾸면 새 전투로 다시 (같은 화면 안).</summary>
        private void Restart()
        {
            _field?.Destroy();
            _setup.Seed = (ulong)Ctx.NewSeed();
            _battle = new CrowdBattle(CrowdRules.Load(Ctx.Data), _setup);
            _field = new CrowdField3D(_battle);
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
            _status.text = head;
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
        }

        public override void Close()
        {
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

        /// <summary>캡처 투어: 누군가 친 적이 있음 (맞붙음).</summary>
        public bool FightingForTour
        {
            get { foreach (var s in _battle.Soldiers) if (s.LastAttackTick >= 0) return true; return false; }
        }

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

        public void MoveForTour(string place, float x, float y)
        {
            foreach (var a in _battle.TeamArmies(CrowdBattle.Ally))
                if (a.Place == place) _battle.OrderMove(a.Id, new Vec(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y)));
        }
    }
}
