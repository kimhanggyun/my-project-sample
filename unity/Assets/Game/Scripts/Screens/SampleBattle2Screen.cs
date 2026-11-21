using System.Collections.Generic;
using Game.Battle.Model.Crowd;
using Game.Unity.Battle3D;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 샘플전투2 (R-125, 시험 중): 병사 단위 전투. 우리 · 적 진형을 골라 시작하고, 싸우는 중에 진형 · 전진/정지를 바꾼다.
    /// 기존 샘플전투와 따로 (규칙 CrowdBattle, 그리기 CrowdField3D). 보상 없음, 세이브에 남지 않음.
    /// </summary>
    public sealed class SampleBattle2Screen : GameScreen
    {
        private const float TopHeight = 76f;
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
        private readonly Dictionary<string, Button> _formationButtons = new Dictionary<string, Button>();
        private Button _advanceButton, _speedButton;

        public SampleBattle2Screen(CrowdSetup setup = null, bool startNow = false)
        {
            _setup = setup ?? new CrowdSetup { AllyFormation = CrowdFormations.Wedge, EnemyFormation = CrowdFormations.Line };
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
            gestures.Dragged += (a, b) => _field.Rig.Pan(a, b);
            gestures.Zoomed += f => _field.Rig.Zoom(f);

            _panel = Ui.Rect(Root, "Panel");
            Ui.Background(_panel, Palette.Bar);
            Ui.Column(_panel, 8, 10);
            Ui.Size(_panel, 300);
            BuildPanel();
            UpdateTexts();
        }

        private void BuildPanel()
        {
            Ui.Clear(_panel);
            _formationButtons.Clear();
            if (!_running && _battle.Tick == 0)
            {
                // 시작 전: 우리 진형 · 적 진형 · 적 명령
                FormationRow(T("ui.sample2.ally"), _setup.AllyFormation, id => { _setup.AllyFormation = id; Restart(false); });
                FormationRow(T("ui.sample2.enemy"), _setup.EnemyFormation, id => { _setup.EnemyFormation = id; Restart(false); });
                var row = Ui.Row(_panel, "EnemyOrder", 72, 8);
                Ui.Size(Ui.Label(row, T("ui.sample2.enemyOrder"), 24), width: 150);
                Ui.Size(Ui.Button(row, T("ui.sample2.hold"), () => { _setup.EnemyAdvance = false; Restart(false); }, 72, _setup.EnemyAdvance ? Palette.Button : Palette.ButtonActive, 24), flexibleWidth: 1);
                Ui.Size(Ui.Button(row, T("ui.sample2.advance"), () => { _setup.EnemyAdvance = true; Restart(false); }, 72, _setup.EnemyAdvance ? Palette.ButtonActive : Palette.Button, 24), flexibleWidth: 1);
                Ui.Button(_panel, T("ui.sample2.start"), () => { _running = true; BuildPanel(); }, 80, Palette.ButtonActive, 30);
                return;
            }
            // 싸우는 중: 진형 바꾸기 · 전진/정지 · 다시
            FormationRow(T("ui.sample2.formation"), _battle.Armies[CrowdBattle.Ally].Formation, id =>
            {
                _battle.SetFormation(CrowdBattle.Ally, id);
                foreach (var kv in _formationButtons) Ui.Recolor(kv.Value, kv.Key == id ? Palette.ButtonActive : Palette.Button);
            });
            var row2 = Ui.Row(_panel, "Orders", 80, 8);
            _advanceButton = Ui.Button(row2, "", () =>
            {
                _battle.SetAdvance(CrowdBattle.Ally, !_battle.Armies[CrowdBattle.Ally].Advance);
                UpdateTexts();
            }, 80, Palette.ButtonActive, 28);
            Ui.Size(_advanceButton, flexibleWidth: 1);
            Ui.Size(Ui.Button(row2, T("ui.sample2.again"), () => App.Show(new SampleBattle2Screen(new CrowdSetup
            {
                AllyFormation = _setup.AllyFormation, EnemyFormation = _setup.EnemyFormation, EnemyAdvance = _setup.EnemyAdvance,
            })), 80, Palette.Button, 28), flexibleWidth: 1);
        }

        private void FormationRow(string label, string current, System.Action<string> pick)
        {
            var row = Ui.Row(_panel, "Formation " + label, 72, 6);
            Ui.Size(Ui.Label(row, label, 24), width: 150);
            foreach (var id in CrowdFormations.All)
            {
                string f = id;
                var b = Ui.Button(row, T("ui.sample2.f." + id), () => pick(f), 72, id == current ? Palette.ButtonActive : Palette.Button, 24);
                Ui.Size(b, flexibleWidth: 1);
                _formationButtons[id] = b;
            }
        }

        /// <summary>시작 전 설정을 바꾸면 새 전투로 다시 (같은 화면 안).</summary>
        private void Restart(bool run)
        {
            _field?.Destroy();
            _setup.Seed = (ulong)Ctx.NewSeed();
            _battle = new CrowdBattle(CrowdRules.Load(Ctx.Data), _setup);
            _field = new CrowdField3D(_battle);
            _accumulator = 0f;
            _running = run;
            BuildPanel();
            UpdateTexts();
        }

        private void NextSpeed() { _speed = (_speed + 1) % Speeds.Length; UpdateTexts(); }

        private void UpdateTexts()
        {
            var a = _battle.Armies[CrowdBattle.Ally];
            var e = _battle.Armies[CrowdBattle.Enemy];
            string head = T("ui.sample2.count", a.AliveCount, e.AliveCount, e.General.Hp, e.General.MaxHp);
            if (_battle.IsOver)
                head = T(_battle.Winner == CrowdBattle.Ally ? "ui.sample2.win" : _battle.Winner == CrowdBattle.Enemy ? "ui.sample2.lose" : "ui.sample2.draw")
                    + " · " + T("ui.sample2.end." + _battle.EndReason) + "  " + head;
            _status.text = head;
            _speedButton.GetComponentInChildren<Text>().text = "×" + Speeds[_speed];
            if (_advanceButton != null) _advanceButton.GetComponentInChildren<Text>().text = T(a.Advance ? "ui.sample2.stop" : "ui.sample2.advance");
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
            _accumulator = 0f;
            UpdateTexts();
        }

        /// <summary>캡처 투어: 맞붙은 곳(가장 최근에 친 병사들 가운데)으로 카메라.</summary>
        public void FocusFightForTour(float distance)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var s in _battle.Soldiers)
                if (s.Alive && s.LastAttackTick >= _battle.Tick - 10) { sum += _field.ToWorld(s.Pos); n++; }
            _field.Rig.LookAt(n > 0 ? sum / n : new Vector3(20f, 0f, 28f));
            _field.Rig.SetDistance(distance);
        }

        public bool RunningForTour => _running;

        /// <summary>캡처 투어: 그 편 병사들 가운데 앞쪽으로 카메라.</summary>
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
        public void PauseForTour(bool paused) => _running = !paused;
    }
}
