using System.Collections.Generic;
using System.Text;
using Game.Battle.Model.Control;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Game.Unity.Battle3D;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 전투 화면: 3D 전장(첫 단계, 단순 도형) + 명령 버튼 (battle.md 4장의 배치를 단순화).
    /// 전투 계산은 <see cref="BattleSimulation"/>이 고정 틱으로 하고, 이 화면은 실제 시간에 맞춰 틱을 몇 번 돌릴지만 정한다 (R-29).
    /// 입력은 모두 <see cref="BattleControl"/>을 거쳐 명령으로 들어간다 (일시정지 중에도 명령 가능, 다음 틱에 적용).
    /// </summary>
    public sealed class BattleScreen : GameScreen
    {
        private const int MaxTicksPerFrame = 40;
        private const int LogLines = 3;
        private const float NoticeSeconds = 2.5f;
        private static readonly int[] Speeds = { 1, 2, 4 };
        private static readonly string[] MoveModesInOrder = { MoveModes.Normal, MoveModes.Forced, MoveModes.AttackMove };

        private readonly SortiePlan _plan;
        private BattleSimulation _sim;
        private BattleControl _ctl;
        private SpeechPlanner _speech;
        private BattleField3D _field;
        private MiniMapView _miniMap;
        private RectTransform _fieldArea;
        private readonly Dictionary<CorpsState, Text> _flagLabels = new Dictionary<CorpsState, Text>();
        private readonly Vector3[] _corners = new Vector3[4];
        private float _tickSeconds;
        private float _accumulator;
        private int _speedIndex;
        private bool _paused;
        private int _logRead;
        private bool _finished;
        private float _noticeLeft;
        private bool _dirty = true;

        private Text _clock;
        private Text _logText;
        private Text _selectedInfo;
        private Text _notice;
        private Button _pauseButton;
        private Button _speedButton;
        private Button _delegateButton;
        private Button _moveModeButton;
        private Button _formationButton;
        private RectTransform _skillRow;
        private readonly List<(CorpsState corps, Button button, Text text)> _cards = new List<(CorpsState, Button, Text)>();
        private readonly List<Button> _orderButtons = new List<Button>();
        private string _skillRowKey;
        private readonly LinkedList<string> _log = new LinkedList<string>();

        public BattleScreen(SortiePlan plan) { _plan = plan; }

        public override void Build()
        {
            _sim = new BattleSimulation(_plan.Setup);
            var ctx = _sim.Context;
            _ctl = new BattleControl(_sim, StoryFlow.PlayerTeam);
            _ctl.Refresh();
            _tickSeconds = (float)ctx.Combat.TickSeconds.ToDisplayDouble();
            _speech = new SpeechPlanner(ctx.Defs, ctx.Flags, ctx.Combat.TickSeconds,
                id => { var c = ctx.FindCorps(id); return c != null && c.Setup.Controller == Controllers.Ai; });

            Ui.Column(Root, 8, 16);

            // ① 상단: 시간 + 일시정지·배속·끝까지
            var top = Ui.Row(Root, "Top", 96, 8);
            _clock = Ui.Label(top, "", Ui.FontSmall, TextAnchor.MiddleLeft);
            Ui.Size(_clock, flexibleWidth: 2);
            _pauseButton = Ui.Button(top, "", TogglePause, 96, fontSize: Ui.FontSmall);
            _speedButton = Ui.Button(top, "", NextSpeed, 96, fontSize: Ui.FontSmall);
            Ui.Button(top, T("ui.battle.skip"), SkipToEnd, 96, fontSize: Ui.FontSmall);

            // ② 전장 (3D 카메라가 이 영역에 그린다. 위에 깔린 투명 판이 누름·끌기·확대를 받는다)
            App.SetBackdrop(false);
            _field = new BattleField3D(_ctl);
            _fieldArea = Ui.Rect(Root, "Field");
            Ui.Size(_fieldArea, flexibleHeight: 1);
            Ui.Background(_fieldArea, new Color(0, 0, 0, 0));
            var gestures = _fieldArea.gameObject.AddComponent<GestureArea>();
            gestures.Tapped += OnFieldTap;
            gestures.Dragged += (a, b) => { _field.Rig.Pan(a, b); _dirty = true; };
            gestures.Zoomed += f => { _field.Rig.Zoom(f); _dirty = true; };
            foreach (var c in ctx.Corps)
            {
                var label = Ui.Label(_fieldArea, GeneralOf(c), 24, TextAnchor.LowerCenter, c.Team == _ctl.Team ? Palette.Ally : Palette.Enemy);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.pivot = new Vector2(0.5f, 0f);
                label.rectTransform.sizeDelta = new Vector2(240, 32);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                _flagLabels[c] = label;
            }
            _miniMap = new MiniMapView(_fieldArea, _ctl, _field, 220f);
            _notice = Ui.Label(_fieldArea, "", 28, TextAnchor.UpperCenter, Palette.Text);
            _notice.rectTransform.anchorMin = new Vector2(0f, 1f);
            _notice.rectTransform.anchorMax = new Vector2(1f, 1f);
            _notice.rectTransform.pivot = new Vector2(0.5f, 1f);
            _notice.rectTransform.sizeDelta = new Vector2(0f, 48f);
            _notice.rectTransform.anchoredPosition = new Vector2(0f, -8f);

            // ③④ 선택 군단 정보 + 명령
            var panel = Ui.Card(Root, "Selected", 16, 8);
            _selectedInfo = Ui.Label(panel, "", 28);
            var orders = Ui.Row(panel, "Orders", 96, 8);
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.advance"), () => Report(_ctl.Advance()), 96, fontSize: Ui.FontSmall));
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.hold"), () => Report(_ctl.Hold()), 96, fontSize: Ui.FontSmall));
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.retreat"), () => Report(_ctl.Retreat()), 96, fontSize: Ui.FontSmall));
            _delegateButton = Ui.Button(orders, "", ToggleDelegate, 96, fontSize: Ui.FontSmall);
            _orderButtons.Add(_delegateButton);
            var row2 = Ui.Row(panel, "Modes", 96, 8);
            _moveModeButton = Ui.Button(row2, "", NextMoveMode, 96, fontSize: Ui.FontSmall);
            _formationButton = Ui.Button(row2, "", () => Report(_ctl.NextFormation()), 96, fontSize: Ui.FontSmall);
            _orderButtons.Add(_formationButton);
            _skillRow = Ui.Row(panel, "Skills", 96, 8);

            // 전황 (말풍선·대사)
            var logCard = Ui.Card(Root, "Log", 12, 4);
            Ui.Size(logCard, 120);
            _logText = Ui.Label(logCard, "", 26, TextAnchor.UpperLeft);

            // ⑤ 하단: 전군 진군 + 군단 카드
            var bottom = Ui.Row(Root, "Cards", 120, 8);
            Ui.Size(Ui.Button(bottom, T("ui.cmd.advanceAll"), () => Report(_ctl.AdvanceAll() > 0), 120, Palette.ButtonActive, Ui.FontSmall), width: 200);
            foreach (var c in _ctl.OwnCorps())
            {
                var corps = c;
                var b = Ui.Button(bottom, "", () => { if (_ctl.Select(corps.Id)) _field.LookAt(corps); _dirty = true; }, 120, fontSize: 24);
                _cards.Add((corps, b, b.GetComponentInChildren<Text>()));
            }

            _field.LookAt(_ctl.Selected);
            ReadNewEvents();
            Refresh();
        }

        public override void Close()
        {
            _field?.Destroy();
            App.SetBackdrop(true);
        }

        public override void Tick(float deltaTime)
        {
            if (_finished) return;
            if (_noticeLeft > 0 && (_noticeLeft -= deltaTime) <= 0) _notice.text = "";
            int steps = 0;
            if (!_paused)
            {
                _accumulator += deltaTime * Speeds[_speedIndex];
                while (_accumulator >= _tickSeconds && steps < MaxTicksPerFrame && !_sim.IsOver)
                {
                    _sim.Step();
                    _accumulator -= _tickSeconds;
                    steps++;
                }
                if (steps >= MaxTicksPerFrame) _accumulator = 0f; // 밀린 시간은 버린다
            }
            if (steps > 0)
            {
                _ctl.Refresh();
                ReadNewEvents();
            }
            if (steps > 0 || _dirty) Refresh();
            UpdateFieldView();
            if (_sim.IsOver) Finish();
        }

        // ---- 입력 ----

        private void OnFieldTap(Vector2 screen)
        {
            if (_finished || !_field.ScreenToField(screen, out var point)) return;
            OnTap(_ctl.Tap(point));
        }

        private void OnTap(TapResult r)
        {
            switch (r)
            {
                case TapResult.Selected: break;
                case TapResult.Attack: Notice(T("ui.cmd.done.attack")); break;
                case TapResult.Move: Notice(T("ui.cmd.done.move")); break;
                case TapResult.Skill: Notice(T("ui.cmd.done.skill")); break;
                case TapResult.Rejected: Notice(T("ui.cmd.rejected")); break;
                default: if (_ctl.Selected == null) Notice(T("ui.cmd.selectFirst")); break;
            }
            _dirty = true;
        }

        private void Report(bool accepted)
        {
            Notice(accepted ? T("ui.cmd.accepted") : T("ui.cmd.rejected"));
            _dirty = true;
        }

        private void Notice(string text)
        {
            _notice.text = text;
            _noticeLeft = NoticeSeconds;
        }

        private void ToggleDelegate()
        {
            if (_ctl.Selected == null) return;
            Report(_ctl.SetDelegate(!_ctl.Selected.Delegated));
        }

        private void NextMoveMode()
        {
            int i = System.Array.IndexOf(MoveModesInOrder, _ctl.MoveMode);
            _ctl.SetMoveMode(MoveModesInOrder[(i + 1) % MoveModesInOrder.Length]);
            _dirty = true;
        }

        private void PressSkill(string skillId)
        {
            bool ok = _ctl.PressSkill(skillId);
            if (_ctl.TargetingSkill != null) Notice(T("ui.cmd.pickTarget"));
            else if (ok) Notice(T("ui.cmd.accepted"));
            else Notice(T("ui.cmd.cancelled"));
            _dirty = true;
        }

        private void TogglePause() { _paused = !_paused; _dirty = true; }
        private void NextSpeed() { _speedIndex = (_speedIndex + 1) % Speeds.Length; _dirty = true; }

        private void SkipToEnd()
        {
            if (_finished) return;
            _sim.RunToEnd();
            ReadNewEvents();
            Finish();
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            var result = _sim.Result;
            var report = Ctx.Flow.ApplyResult(Ctx.State, _plan, result);
            Ctx.Save();
            App.Show(new ResultScreen(_plan, result, report));
        }

        // ---- 표시 ----

        private void ReadNewEvents()
        {
            var events = _sim.Context.Log;
            for (; _logRead < events.Count; _logRead++)
            {
                var e = events[_logRead];
                string who = CorpsName(e.CorpsId);
                if (e.Kind == EventKinds.Dialogue) { Add(who + ": " + T(e.Data)); continue; }
                var sp = _speech.Feed(e);
                if (sp != null) Add(who + ": \"" + T(sp.LineKey) + "\"");
                string sysKey = "ui.event." + e.Kind;
                if (Ctx.Defs.Strings.ContainsKey(sysKey)) Add(Ui.Colored(T(sysKey, who), Palette.TextDim));
            }
        }

        private void Add(string line)
        {
            _log.AddLast(line);
            while (_log.Count > LogLines) _log.RemoveFirst();
        }

        private string GeneralOf(CorpsState c) => c.Setup.General != null ? Ctx.GeneralName(c.Setup.General.Id) : c.Id;

        private string CorpsName(string corpsId)
        {
            var c = corpsId == null ? null : _sim.Context.FindCorps(corpsId);
            if (c == null) return corpsId ?? "";
            return Ui.Colored(GeneralOf(c), c.Team == StoryFlow.PlayerTeam ? Palette.Ally : Palette.Enemy);
        }

        private void Refresh()
        {
            _dirty = false;
            var ctx = _sim.Context;
            long elapsed = (ctx.Combat.TickSeconds * ctx.Tick).FloorToLong();
            long max = (_plan.Setup.MaxMinutes * 60).FloorToLong();
            _clock.text = T("ui.battle.time", elapsed, max);
            SetLabel(_pauseButton, _paused ? T("ui.battle.resume") : T("ui.battle.pause"));
            SetLabel(_speedButton, T("ui.battle.speed", Speeds[_speedIndex]));

            _field.Refresh();

            var sel = _ctl.Selected;
            bool can = _ctl.CanCommand(sel);
            if (sel == null) _selectedInfo.text = T("ui.cmd.selectFirst");
            else
            {
                string formation = ctx.Defs.Formations.TryGetValue(sel.Formation.Id, out var f) ? T(f.NameKey) : sel.Formation.Id;
                string status = T("ui.corps." + StatusKey(sel.Status));
                _selectedInfo.text = T("ui.cmd.selectedLine", CorpsName(sel.Id), formation, sel.Troops.ToString("N0"), sel.StartTroops.ToString("N0"),
                    sel.Morale.FloorToLong(), sel.SkillPoints.Points.FloorToLong(), status, OrderName(sel));
            }
            foreach (var b in _orderButtons) Ui.SetEnabled(b, can);
            SetLabel(_delegateButton, sel != null && sel.Delegated ? T("ui.cmd.delegateOff") : T("ui.cmd.delegateOn"));
            SetLabel(_moveModeButton, T("ui.cmd.moveMode", T("ui.moveMode." + _ctl.MoveMode)));
            SetLabel(_formationButton, T("ui.cmd.formation"));
            if (can && _ctl.Formations().Count < 2) Ui.SetEnabled(_formationButton, false);
            RefreshSkills(can);

            foreach (var (corps, button, text) in _cards)
            {
                string line = GeneralOf(corps) + "\n" + corps.Troops.ToString("N0") + (corps.Delegated ? " " + T("ui.battle.delegated") : "");
                text.text = corps.InBattle ? line : Ui.Colored(line + "\n" + T("ui.corps." + StatusKey(corps.Status)), Palette.TextDim);
                button.targetGraphic.color = corps == sel ? Palette.ButtonActive : corps.InBattle ? Palette.Button : Palette.ButtonDisabled;
            }

            var sb = new StringBuilder();
            foreach (var l in _log) sb.AppendLine(l);
            _logText.text = sb.ToString();
        }

        /// <summary>매 프레임: 카메라 화면 영역, 병사 그리기, 깃발 위 이름.</summary>
        private void UpdateFieldView()
        {
            if (_field == null) return;
            _fieldArea.GetWorldCorners(_corners); // 오버레이 캔버스라 화면 픽셀 좌표
            _field.SetViewport(new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y));
            _field.Draw();
            _miniMap.Refresh();
            var ctx = _sim.Context;
            var cam = _field.Rig.Camera;
            foreach (var kv in _flagLabels)
            {
                var c = kv.Key;
                bool shown = c.InBattle && ctx.IsVisibleTo(_ctl.Team, c);
                Vector3 sp = shown ? cam.WorldToScreenPoint(_field.ToWorld(c.Anchor) + Vector3.up * 2.2f) : Vector3.zero;
                shown = shown && sp.z > 0 && cam.pixelRect.Contains(sp);
                kv.Value.gameObject.SetActive(shown);
                if (!shown) continue;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_fieldArea, sp, null, out var local))
                    kv.Value.rectTransform.anchoredPosition = local;
                kv.Value.fontStyle = c == _ctl.Selected ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        /// <summary>스킬 버튼은 선택 군단·점수·지정 모드가 바뀔 때만 다시 만든다.</summary>
        private void RefreshSkills(bool can)
        {
            var skills = can ? _ctl.Skills() : new List<SkillOption>();
            var key = new StringBuilder(_ctl.Selected?.Id ?? "-").Append('|').Append(_ctl.TargetingSkill);
            foreach (var s in skills) key.Append('|').Append(s.SkillId).Append(s.Affordable).Append(s.UsesLeft);
            if (key.ToString() == _skillRowKey) return;
            _skillRowKey = key.ToString();
            Ui.Clear(_skillRow);
            if (skills.Count == 0)
            {
                Ui.Label(_skillRow, T("ui.cmd.noSkills"), 26, TextAnchor.MiddleLeft, Palette.TextDim);
                return;
            }
            foreach (var s in skills)
            {
                string id = s.SkillId;
                string label = T(s.NameKey) + " " + s.Cost.FloorToLong() + "·" + s.UsesLeft;
                var b = Ui.Button(_skillRow, label, () => PressSkill(id), 96, fontSize: 26);
                Ui.SetEnabled(b, s.Affordable);
                if (_ctl.TargetingSkill == id) b.targetGraphic.color = Palette.ButtonActive;
            }
        }

        private string OrderName(CorpsState c)
        {
            if (c.PendingSkill != null) return T("ui.order.skill");
            return T("ui.order." + c.Order.Kind);
        }

        private static void SetLabel(Button b, string text) => b.GetComponentInChildren<Text>().text = text;

        private static string StatusKey(CorpsStatus s)
        {
            switch (s)
            {
                case CorpsStatus.Active: return "active";
                case CorpsStatus.Retreating: return "retreating";
                case CorpsStatus.Retreated: return "retreated";
                case CorpsStatus.Destroyed: return "destroyed";
                case CorpsStatus.Captured: return "captured";
                default: return "reserve";
            }
        }
    }
}
