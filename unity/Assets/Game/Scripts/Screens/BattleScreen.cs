using System.Collections.Generic;
using System.Text;
using Game.Battle.Model.Control;
using Game.Battle.Model.Events;
using Game.Battle.Model.Geometry;
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
        private const float TopHeight = 64f;
        private const float HeadHeight = 56f;
        private const float RowHeight = 72f;
        private const float CardHeight = 92f;
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
        private RadialMenu _radial;
        private CorpsState _radialCorps;
        // 이동 지점 고르기 (4.3.5): 메뉴 "이동" → 땅을 누르면 그 자리에 방식 고리
        private bool _pickingPoint;
        private int _waypoints;
        private Vec? _ringPoint;
        private float _tickSeconds;
        private float _accumulator;
        private int _speedIndex;
        private bool _paused;
        private int _logRead;
        private bool _finished;
        private float _noticeLeft;
        private bool _dirty = true;
        private float _fxDelta;
        /// <summary>깃발 위 말풍선: 군단 → (대사, 남은 초).</summary>
        private readonly Dictionary<CorpsState, (string text, float left)> _bubbles = new Dictionary<CorpsState, (string, float)>();
        private const float BubbleSeconds = 3f;

        private Text _clock;
        private Button _collapseButton;
        private Button _queueButton;
        private RectTransform _prompt;
        // 스킬 지정 막대 (4.2): 무엇을 지정 중인지, [여기에 쓰기] [취소]
        private RectTransform _aimBar;
        private Text _aimText;
        private Button _aimConfirm;
        private Text _promptText;
        private string _promptCorps;
        private RectTransform _panelBody;
        private bool _collapsed;
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

        /// <summary>녹화 재생 (replay.md 3장): 명령은 기록대로, 화면 입력은 받지 않는다. 끝나면 내 녹화 목록으로.</summary>
        private readonly ReplayPlayback _playback;
        private int _nextCommand;
        private bool IsReplay => _playback != null;
        private BattleSetup Setup => IsReplay ? _playback.Setup : _plan.Setup;

        /// <summary>이어하기 (R-55): 남긴 틱까지 돌려 둔 전투. 몇 초마다·앱이 뒤로 갈 때 기록을 새로 남긴다.</summary>
        private readonly BattleSimulation _resumedSim;
        private bool _autoFinish;
        private float _suspendTimer;
        private long _suspendedTick = -1;
        private const float SuspendEverySeconds = 5f;

        public BattleScreen(SortiePlan plan) { _plan = plan; }
        /// <param name="autoFinish">true면 모든 아군 군단을 위임하고 끝까지 돌린다 ([자동으로 끝내기]).</param>
        public BattleScreen(ResumedBattle resumed, bool autoFinish = false) { _plan = resumed.Plan; _resumedSim = resumed.Simulation; _autoFinish = autoFinish; }
        public BattleScreen(ReplayPlayback playback) { _playback = playback; }

        // 녹화 재생 보기 바꾸기 (replay.md 3장): 같은 전투를 이어서, 시점(아군·적군)·안개만 바꿔 다시 그린다
        private readonly BattleSimulation _replaySim;
        private readonly string _viewTeam;
        private readonly bool _revealAll;
        private BattleScreen(ReplayPlayback playback, BattleSimulation sim, int nextCommand, string viewTeam, bool revealAll, int speedIndex)
        {
            _playback = playback;
            _replaySim = sim;
            _nextCommand = nextCommand;
            _viewTeam = viewTeam;
            _revealAll = revealAll;
            _speedIndex = speedIndex;
        }

        private CorpsState FirstOfViewTeam()
        {
            foreach (var c in _sim.Context.Corps) if (c.Team == _ctl.Team && c.InBattle) return c;
            return null;
        }

        private void ReplayView(string team, bool reveal) =>
            App.Show(new BattleScreen(_playback, _sim, _nextCommand, team, reveal, _speedIndex));

        public override void Build()
        {
            _sim = _resumedSim ?? _replaySim ?? new BattleSimulation(Setup);
            if (!IsReplay && _resumedSim == null)
            {
                // 가져간 군량을 쓴 세이브를 먼저 남기고, 이어하기 기록을 남긴다 (전투 중 꺼져도 이어서)
                Ctx.Save();
                Ctx.SuspendBattle(_plan, _sim);
            }
            if (_resumedSim != null && !_autoFinish) _paused = true; // 이어 할 때는 멈춘 채로 시작
            var ctx = _sim.Context;
            _ctl = new BattleControl(_sim, _viewTeam ?? StoryFlow.PlayerTeam) { RevealAll = _revealAll, ConfirmPointSkills = true }; // 지점 스킬은 범위를 보고 확인
            _ctl.Refresh();
            _tickSeconds = (float)ctx.Combat.TickSeconds.ToDisplayDouble();
            _speech = new SpeechPlanner(ctx.Defs, ctx.Flags, ctx.Combat.TickSeconds,
                id => { var c = ctx.FindCorps(id); return c != null && c.Setup.Controller == Controllers.Ai; });

            Ui.Column(Root, 6, 8);

            // ① 상단: 시간 + 일시정지·배속·끝까지 (얇게)
            var top = Ui.Row(Root, "Top", TopHeight, 8);
            _clock = Ui.Label(top, "", 28, TextAnchor.MiddleLeft);
            Ui.Size(_clock, flexibleWidth: 2);
            _pauseButton = Ui.Button(top, "", TogglePause, TopHeight, fontSize: 28);
            _speedButton = Ui.Button(top, "", NextSpeed, TopHeight, fontSize: 28);
            if (IsReplay)
            {
                // 시점 전환·안개 끄기 (녹화 재생만, 전투 결과에는 영향 없음)
                bool enemyView = _ctl.Team != StoryFlow.PlayerTeam;
                Ui.Button(top, T(enemyView ? "ui.replay.viewAlly" : "ui.replay.viewEnemy"), () => ReplayView(enemyView ? StoryFlow.PlayerTeam : StoryFlow.EnemyTeam, _revealAll), TopHeight, fontSize: 26);
                Ui.Button(top, T(_revealAll ? "ui.replay.fogOn" : "ui.replay.fogOff"), () => ReplayView(_ctl.Team, !_revealAll), TopHeight, fontSize: 26);
            }
            Ui.Button(top, T(IsReplay ? "ui.replay.stop" : "ui.battle.skip"), IsReplay ? (System.Action)(() => App.Show(new ReplayListScreen())) : SkipToEnd, TopHeight, fontSize: 28);

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
            _radial = new RadialMenu(_fieldArea); // 군단 원형 메뉴 (battle.md 4.3.3)

            // 협공 대기 물음 (battle.md 4.3.4): [계속 대기] [공격]
            _prompt = Ui.Rect(_fieldArea, "Prompt");
            _prompt.anchorMin = new Vector2(0.05f, 0f);
            _prompt.anchorMax = new Vector2(0.95f, 0f);
            _prompt.pivot = new Vector2(0.5f, 0f);
            _prompt.sizeDelta = new Vector2(0f, 190f);
            _prompt.anchoredPosition = new Vector2(0f, 16f);
            Ui.Background(_prompt, new Color(0.1f, 0.1f, 0.12f, 0.9f));
            Ui.Column(_prompt, 8, 12);
            _promptText = Ui.Label(_prompt, "", 28, TextAnchor.MiddleCenter);
            var pr = Ui.Row(_prompt, "Buttons", 90, 10);
            Ui.Button(pr, T("ui.cmd.keepWaiting"), () => { Report(_ctl.KeepCoordinating(_promptCorps)); ClosePrompt(); }, 90, fontSize: 28);
            Ui.Button(pr, T("ui.cmd.attackNow"), () => { Report(_ctl.AttackNow(_promptCorps)); ClosePrompt(); }, 90, Palette.ButtonActive, 28);
            _prompt.gameObject.SetActive(false);

            _aimBar = Ui.Rect(_fieldArea, "AimBar");
            _aimBar.anchorMin = new Vector2(0.05f, 0f);
            _aimBar.anchorMax = new Vector2(0.95f, 0f);
            _aimBar.pivot = new Vector2(0.5f, 0f);
            _aimBar.sizeDelta = new Vector2(0f, 170f);
            _aimBar.anchoredPosition = new Vector2(0f, 16f);
            Ui.Background(_aimBar, new Color(0.1f, 0.1f, 0.12f, 0.9f));
            Ui.Column(_aimBar, 8, 10);
            _aimText = Ui.Label(_aimBar, "", 26, TextAnchor.MiddleCenter);
            var ar = Ui.Row(_aimBar, "Buttons", 84, 10);
            _aimConfirm = Ui.Button(ar, T("ui.aim.confirm"), () => { Notice(_ctl.ConfirmAim() ? T("ui.cmd.done.skill") : T("ui.cmd.rejected")); _dirty = true; }, 84, Palette.ButtonActive, 28);
            Ui.Button(ar, T("ui.aim.cancel"), () => { _ctl.CancelTargeting(); Notice(T("ui.cmd.cancelled")); _dirty = true; }, 84, fontSize: 28);
            _aimBar.gameObject.SetActive(false);
            _notice = Ui.Label(_fieldArea, "", 28, TextAnchor.UpperCenter, Palette.Text);
            _notice.rectTransform.anchorMin = new Vector2(0f, 1f);
            _notice.rectTransform.anchorMax = new Vector2(1f, 1f);
            _notice.rectTransform.pivot = new Vector2(0.5f, 1f);
            _notice.rectTransform.sizeDelta = new Vector2(0f, 48f);
            _notice.rectTransform.anchoredPosition = new Vector2(0f, -8f);

            // ③④ 선택 군단 정보 + 명령 (접기·펴기, battle.md 4장: 전장이 가려지지 않게)
            var panel = Ui.Card(Root, "Selected", 10, 6);
            var head = Ui.Row(panel, "Head", HeadHeight, 8);
            _selectedInfo = Ui.Label(head, "", 24);
            Ui.Size(_selectedInfo, flexibleWidth: 5);
            _collapseButton = Ui.Button(head, "", ToggleCollapse, HeadHeight, fontSize: 24);
            Ui.Size(_collapseButton, width: 150);
            _panelBody = Ui.Rect(panel, "Body");
            Ui.Column(_panelBody, 6);
            var orders = Ui.Row(_panelBody, "Orders", RowHeight, 6);
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.advance"), () => Report(_ctl.Advance()), RowHeight, fontSize: 26));
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.hold"), () => Report(_ctl.Hold()), RowHeight, fontSize: 26));
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.fallBack"), () => Report(_ctl.FallBack()), RowHeight, fontSize: 26)); // 뒤로 곧게 (R-60)
            _delegateButton = Ui.Button(orders, "", ToggleDelegate, RowHeight, fontSize: 26);
            _orderButtons.Add(_delegateButton);
            _moveModeButton = Ui.Button(orders, "", NextMoveMode, RowHeight, fontSize: 24);
            _formationButton = Ui.Button(orders, "", () => Report(_ctl.NextFormation()), RowHeight, fontSize: 24);
            _orderButtons.Add(_formationButton);
            var plans = Ui.Row(_panelBody, "Plans", RowHeight, 6);
            _queueButton = Ui.Button(plans, "", () => { _ctl.SetQueueing(!_ctl.Queueing); _dirty = true; }, RowHeight, fontSize: 24);
            _orderButtons.Add(Ui.Button(plans, T("ui.cmd.rest"), () => Report(_ctl.Rest()), RowHeight, fontSize: 24));
            _orderButtons.Add(Ui.Button(plans, T("ui.cmd.coordinate"), () => Report(_ctl.Coordinate()), RowHeight, fontSize: 24));
            _orderButtons.Add(Ui.Button(plans, T("ui.cmd.withdraw"), () => Report(_ctl.Retreat()), RowHeight, Palette.ButtonDisabled, 24)); // 전장 이탈 (퇴각)
            _skillRow = Ui.Row(_panelBody, "Skills", RowHeight, 6);

            // ⑤ 하단: 전군 진군 + 군단 카드
            var bottom = Ui.Row(Root, "Cards", CardHeight, 6);
            Ui.Size(Ui.Button(bottom, T("ui.cmd.advanceAll"), () => Report(_ctl.AdvanceAll() > 0), CardHeight, Palette.ButtonActive, 26), width: 180);
            foreach (var c in _ctl.OwnCorps())
            {
                var corps = c;
                var b = Ui.Button(bottom, "", () => { if (_ctl.Select(corps.Id)) { _field.LookAt(corps); OpenCorpsMenu(corps); } _dirty = true; }, CardHeight, fontSize: 22);
                _cards.Add((corps, b, b.GetComponentInChildren<Text>()));
            }

            if (IsReplay)
            {
                // 재생: 명령 패널·군단 카드는 숨기고 "녹화 재생 중" 표시
                panel.gameObject.SetActive(false);
                bottom.gameObject.SetActive(false);
                Ui.Size(Ui.Label(Root, T("ui.replay.playing"), 26, TextAnchor.MiddleCenter, Palette.TextDim), 48);
            }

            if (_resumedSim != null || _replaySim != null) _logRead = _sim.Context.Log.Count; // 이어 볼 때 지난 알림은 다시 띄우지 않는다
            _field.LookAt(_ctl.Selected ?? FirstOfViewTeam()); // 적군 시점이면 고른 군단이 없어 그 팀 첫 군단으로
            ReadNewEvents();
            Refresh();
            // 처음 안내 (R-62): 직접 지휘할 때만, 읽는 동안 멈춘다
            if (!IsReplay && !_autoFinish && !_paused && GuideOverlay.Show(App, Root, "screen.battle", () => { _paused = false; _dirty = true; }, "ui.guide.continue"))
            {
                _paused = true;
                _dirty = true;
            }
        }

        public override void Close()
        {
            _field?.Destroy();
            App.SetBackdrop(true);
        }

        public override void Tick(float deltaTime)
        {
            if (_finished) return;
            if (_autoFinish)
            {
                _autoFinish = false;
                foreach (var c in _ctl.OwnCorps())
                    if (!c.Delegated) _sim.Issue(new Game.Battle.Model.Commands.BattleCommand { CorpsId = c.Id, Kind = Game.Battle.Model.Commands.CommandKinds.Delegate, Mode = "on" });
                SkipToEnd();
                return;
            }
            if (_noticeLeft > 0 && (_noticeLeft -= deltaTime) <= 0) _notice.text = "";
            _radial.Tick(deltaTime);
            int steps = 0;
            if (!_paused)
            {
                _accumulator += deltaTime * Speeds[_speedIndex];
                while (_accumulator >= _tickSeconds && steps < MaxTicksPerFrame && !_sim.IsOver)
                {
                    if (IsReplay) _nextCommand = _playback.IssueDue(_sim, _nextCommand);
                    _sim.Step();
                    _accumulator -= _tickSeconds;
                    steps++;
                }
                if (steps >= MaxTicksPerFrame) _accumulator = 0f; // 밀린 시간은 버린다
            }
            if (steps > 0)
            {
                _field.OnTicked();
                _ctl.Refresh();
                ReadNewEvents();
            }
            _fxDelta = _paused ? 0f : deltaTime * Speeds[_speedIndex];
            if (steps > 0 || _dirty) Refresh();
            UpdateFieldView();
            if (_sim.IsOver) { Finish(); return; }
            if (!IsReplay && (_suspendTimer += deltaTime) >= SuspendEverySeconds)
            {
                _suspendTimer = 0f;
                Suspend();
            }
        }

        public override void OnAppPause(bool paused)
        {
            if (!paused || IsReplay || _finished) return;
            _paused = true; // 앱이 뒤로 가면 전투를 멈춘다
            _dirty = true;
            Suspend();
        }

        private void Suspend()
        {
            if (_sim.IsOver || _sim.Tick == _suspendedTick) return;
            _suspendedTick = _sim.Tick;
            Ctx.SuspendBattle(_plan, _sim);
        }

        // ---- 입력 ----

        private void OnFieldTap(Vector2 screen)
        {
            if (_finished || IsReplay) return;
            if (!_field.ScreenToField(screen, out var point)) { if (_radial.IsOpen) CloseCorpsMenu(); return; }
            // 이동 지점 고르는 중: 아군 군단이 아니면 그 자리에 방식 고리 (다른 곳을 누르면 그리로 옮김)
            if (_pickingPoint && _ctl.CanCommand(_ctl.Selected))
            {
                var hit = _ctl.Pick(point);
                if (hit == null || hit.Team != _ctl.Team) { OpenMethodMenu(point); return; }
                EndPicking();
            }
            // 메뉴 바깥을 누르면 메뉴를 닫고 그 누름은 그대로 (빠른 조작: 빈 땅 = 이동, 적 = 공격, 4.3.3)
            if (_radial.IsOpen) CloseCorpsMenu();
            var r = _ctl.Tap(point);
            OnTap(r);
            if (r == TapResult.Selected) OpenCorpsMenu(_ctl.Selected); // 아군 군단을 누르면 원형 메뉴
        }

        private void OnTap(TapResult r)
        {
            switch (r)
            {
                case TapResult.Selected: break;
                case TapResult.Attack: Notice(T("ui.cmd.done.attack")); break;
                case TapResult.Move: Notice(T("ui.cmd.done.move")); break;
                case TapResult.Skill: Notice(T("ui.cmd.done.skill")); break;
                case TapResult.Aimed: break; // 지정 막대가 안내
                case TapResult.Rejected: Notice(T("ui.cmd.rejected")); break;
                default: if (_ctl.Selected == null) Notice(T("ui.cmd.selectFirst")); break;
            }
            _dirty = true;
        }

        // ---- 군단 원형 메뉴 (battle.md 4.3.3): 진군·대기·이동·스킬·진형·후퇴·위임 ----

        private void OpenCorpsMenu(CorpsState corps)
        {
            EndPicking();
            if (corps == null || IsReplay || !_ctl.CanCommand(corps)) { CloseCorpsMenu(); return; }
            _radialCorps = corps;
            bool delegated = corps.Delegated;
            _radial.Open(T("ui.radial.close"), CloseCorpsMenu, new List<RadialMenu.Item>
            {
                Pick(T("ui.cmd.advance"), () => _ctl.Advance(), !delegated),
                Sub(T("ui.radial.wait"), OpenWaitMenu, !delegated),
                new RadialMenu.Item { Label = T("ui.radial.move"), Enabled = !delegated, OnPick = StartPicking },
                Sub(T("ui.radial.skill"), OpenSkillMenu, !delegated && _ctl.Skills().Count > 0),
                Sub(T("ui.radial.formation"), OpenFormationMenu, !delegated && _ctl.Formations().Count > 1),
                Pick(T("ui.cmd.fallBack"), () => _ctl.FallBack(), !delegated),
                new RadialMenu.Item { Label = T(delegated ? "ui.cmd.delegateOff" : "ui.cmd.delegateOn"), Active = delegated, OnPick = () => { CloseCorpsMenu(); ToggleDelegate(); } },
            });
            FollowCorps();
        }

        private void OpenWaitMenu() => OpenSub(new List<RadialMenu.Item>
        {
            Pick(T("ui.cmd.rest"), () => _ctl.Rest()),
            Pick(T("ui.cmd.coordinate"), () => _ctl.Coordinate()),
            Pick(T("ui.radial.stay"), () => _ctl.Hold()),
        });

        /// <summary>이동: 먼저 갈 곳을 누르고, 그 자리에서 방식을 고른다 (4.3.5).</summary>
        private void StartPicking()
        {
            var corps = _radialCorps;
            CloseCorpsMenu();
            _radialCorps = corps;
            _pickingPoint = true;
            _waypoints = 0;
            Notice(T("ui.radial.pickPoint"));
        }

        private void EndPicking()
        {
            _pickingPoint = false;
            _waypoints = 0;
            _ringPoint = null;
        }

        /// <summary>누른 지점의 방식 고리: 이동·강행·공격 이동·경유·도착 후 스킬. 가운데 = 취소.</summary>
        private void OpenMethodMenu(Vec point)
        {
            _radialCorps = _ctl.Selected;
            bool queued = _waypoints > 0; // 경유 뒤의 지점은 앞 지점 다음에 예약
            var items = new List<RadialMenu.Item>();
            foreach (var m in MoveModesInOrder)
            {
                string mode = m;
                items.Add(new RadialMenu.Item { Label = T("ui.moveMode." + mode), OnPick = () => FinishPicking(_ctl.MoveTo(point, mode, queued)) });
            }
            items.Add(new RadialMenu.Item
            {
                Label = T("ui.radial.via"),
                Enabled = _ctl.QueueCount + _waypoints < _sim.Context.Combat.OrderQueueMax,
                OnPick = () =>
                {
                    bool ok = _ctl.MoveTo(point, MoveModes.Normal, queued);
                    _radial.Close();
                    _ringPoint = null;
                    if (!ok) { FinishPicking(false); return; }
                    _waypoints++;
                    Notice(T("ui.radial.nextPoint", _waypoints));
                    _dirty = true;
                },
            });
            bool anySkill = false;
            foreach (var sk in _ctl.Skills()) anySkill |= BattleControl.UsableOnArrival(sk.TargetType);
            items.Add(new RadialMenu.Item { Label = T("ui.radial.thenSkill") + " ›", Enabled = anySkill, OnPick = () => OpenArrivalSkillMenu(point, queued) });
            _radial.Open(T("ui.radial.cancel"), () => { CloseCorpsMenu(); EndPicking(); Notice(T("ui.radial.moveCancelled")); }, items);
            _ringPoint = point;
            FollowCorps();
        }

        private void OpenArrivalSkillMenu(Vec point, bool queued)
        {
            var items = new List<RadialMenu.Item>();
            foreach (var sk in _ctl.Skills())
            {
                if (!BattleControl.UsableOnArrival(sk.TargetType)) continue;
                string id = sk.SkillId;
                items.Add(new RadialMenu.Item
                {
                    Label = T(sk.NameKey) + "\n" + sk.Cost.FloorToLong(), Enabled = sk.Affordable,
                    OnPick = () => FinishPicking(_ctl.MoveThenSkill(point, MoveModes.Normal, id, queued)),
                });
            }
            _radial.Open(T("ui.radial.back"), () => OpenMethodMenu(point), items);
            _ringPoint = point;
            FollowCorps();
        }

        private void FinishPicking(bool ok)
        {
            CloseCorpsMenu();
            EndPicking();
            Report(ok);
        }

        private void OpenSkillMenu()
        {
            var items = new List<RadialMenu.Item>();
            foreach (var s in _ctl.Skills())
            {
                string id = s.SkillId;
                items.Add(new RadialMenu.Item
                {
                    Label = T(s.NameKey) + "\n" + s.Cost.FloorToLong(), Enabled = s.Affordable, Active = _ctl.TargetingSkill == id,
                    OnPick = () => { CloseCorpsMenu(); PressSkill(id); },
                });
            }
            OpenSub(items);
        }

        private void OpenFormationMenu()
        {
            var items = new List<RadialMenu.Item>();
            var defs = _sim.Context.Defs;
            foreach (var f in _ctl.Formations())
            {
                string id = f;
                bool now = _radialCorps.Formation.Id == id || (_radialCorps.Reforming && _radialCorps.PendingFormation.Id == id);
                items.Add(new RadialMenu.Item
                {
                    Label = defs.Formations.TryGetValue(id, out var fd) ? T(fd.NameKey) : id, Active = now, Enabled = !now,
                    OnPick = () => { CloseCorpsMenu(); Report(_ctl.ChangeFormation(id)); },
                });
            }
            OpenSub(items);
        }

        /// <summary>하위 고리: 가운데 버튼은 "뒤로".</summary>
        private void OpenSub(List<RadialMenu.Item> items)
        {
            var corps = _radialCorps;
            _radial.Open(T("ui.radial.back"), () => OpenCorpsMenu(corps), items);
            FollowCorps();
        }

        private RadialMenu.Item Pick(string label, System.Func<bool> order, bool enabled = true) =>
            new RadialMenu.Item { Label = label, Enabled = enabled, OnPick = () => { CloseCorpsMenu(); Report(order()); } };

        private static RadialMenu.Item Sub(string label, System.Action open, bool enabled) =>
            new RadialMenu.Item { Label = label + " ›", Enabled = enabled, OnPick = open };

        private void CloseCorpsMenu()
        {
            _radial.Close();
            _ringPoint = null;
            if (!_pickingPoint) _radialCorps = null;
        }

        /// <summary>메뉴가 군단을 따라간다. 군단이 지휘할 수 없게 되거나(퇴각·위임 해제 아님) 선택이 바뀌면 닫는다.</summary>
        private void FollowCorps()
        {
            if (!_radial.IsOpen) return;
            var c = _radialCorps;
            if (c == null || c != _ctl.Selected || !_ctl.CanCommand(c)) { CloseCorpsMenu(); EndPicking(); return; }
            var cam = _field.Rig.Camera;
            Vector3 sp = cam.WorldToScreenPoint(_field.ToWorld(_ringPoint ?? c.Anchor)); // 방식 고리는 누른 땅에
            if (sp.z > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(_fieldArea, sp, null, out var local))
                _radial.Follow(local);
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
            if (_finished || IsReplay) return;
            _sim.RunToEnd();
            ReadNewEvents();
            Finish();
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            var result = _sim.Result;
            if (IsReplay) { App.Show(new ReplayListScreen(T("ui.replay.ended"))); return; }
            Ctx.ClearSuspendedBattle(); // 결과를 반영하기 전에 지운다 (두 번 반영되지 않게)
            var report = Ctx.Flow.ApplyResult(Ctx.State, _plan, result);
            Ctx.Save();
            if (!Ctx.OnlineEnabled) { App.Show(new ResultScreen(_plan, result, report)); return; }
            // 온라인: 서버가 다시 돌려 확인한 세이브를 받아 쓴다 (R-40)
            Notice(T("ui.online.verifying"));
            Ctx.SubmitBattle(_plan, _sim.CommandLog, result, r => App.Show(new ResultScreen(_plan, result, report, r?.Outcome)));
        }

        // ---- 표시 ----

        private void ReadNewEvents()
        {
            var events = _sim.Context.Log;
            for (; _logRead < events.Count; _logRead++)
            {
                var e = events[_logRead];
                string who = CorpsName(e.CorpsId);
                var corps = e.CorpsId == null ? null : _sim.Context.FindCorps(e.CorpsId);
                if (!IsReplay && e.Kind == EventKinds.CoordinateWaiting && corps != null && corps.Team == _ctl.Team && corps.Setup.Controller == Controllers.Player && !corps.Delegated)
                {
                    _promptCorps = corps.Id;
                    _promptText.text = T("ui.cmd.coordinatePrompt", GeneralOf(corps));
                    _prompt.gameObject.SetActive(true);
                }
                if (e.Kind == EventKinds.SkillUsed && corps != null && _ctl.Sees(corps))
                    _field.Effects.SkillUsed(_field.ToWorld(corps.Anchor), corps.Team == _ctl.Team ? Palette.Ally : Palette.Enemy);
                if (e.Kind == EventKinds.Dialogue) { Add(string.IsNullOrEmpty(who) ? T(e.Data) : who + ": " + T(e.Data)); Bubble(corps, T(e.Data)); continue; } // 말한 군단이 없으면(지문) 이름 없이
                var sp = _speech.Feed(e);
                if (sp != null) { Add(who + ": \"" + T(sp.LineKey) + "\""); Bubble(corps, T(sp.LineKey)); }
                string sysKey = "ui.event." + e.Kind;
                if (Ctx.Defs.Strings.ContainsKey(sysKey)) Add(Ui.Colored(T(sysKey, who), Palette.TextDim));
            }
        }

        private void Bubble(CorpsState corps, string text)
        {
            if (corps != null) _bubbles[corps] = (text, BubbleSeconds);
        }

        /// <summary>전황 한 줄: 전장 위 알림으로 잠깐 보여준다 (대사·말풍선은 깃발 위).</summary>
        private void Add(string line) => Notice(line);

        private void ClosePrompt()
        {
            _promptCorps = null;
            _prompt.gameObject.SetActive(false);
        }

        private void ToggleCollapse()
        {
            _collapsed = !_collapsed;
            _panelBody.gameObject.SetActive(!_collapsed);
            _dirty = true;
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
            long max = (Setup.MaxMinutes * 60).FloorToLong();
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
                if (sel.Reforming) formation = T("ui.battle.reforming", formation, T(sel.PendingFormation.NameKey)); // 규율만큼 전환 시간 (R-56)
                string status = T("ui.corps." + StatusKey(sel.Status));
                _selectedInfo.text = T(_collapsed ? "ui.cmd.selectedShort" : "ui.cmd.selectedLine", CorpsName(sel.Id), formation, sel.Troops.ToString("N0"), sel.StartTroops.ToString("N0"),
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

            RefreshAimBar();
            SetLabel(_collapseButton, _collapsed ? T("ui.cmd.expand") : T("ui.cmd.collapse"));
            SetLabel(_queueButton, _ctl.Queueing ? T("ui.cmd.queueOn", _ctl.QueueCount, ctx.Combat.OrderQueueMax) : T("ui.cmd.queueOff", _ctl.QueueCount));
            _queueButton.targetGraphic.color = _ctl.Queueing ? Palette.ButtonActive : Palette.Button;
            if (_promptCorps != null)
            {
                var pc = ctx.FindCorps(_promptCorps);
                if (pc == null || !pc.Coordinating) ClosePrompt();
            }
        }

        /// <summary>매 프레임: 카메라 화면 영역, 병사 그리기, 깃발 위 이름.</summary>
        private void UpdateFieldView()
        {
            if (_field == null) return;
            _fieldArea.GetWorldCorners(_corners); // 오버레이 캔버스라 화면 픽셀 좌표
            _field.SetViewport(new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y));
            _field.Draw(Mathf.Clamp01(_accumulator / _tickSeconds), _fxDelta);
            _miniMap.Refresh();
            FollowCorps();
            var ctx = _sim.Context;
            var cam = _field.Rig.Camera;
            foreach (var kv in _flagLabels)
            {
                var c = kv.Key;
                bool shown = c.InBattle && _ctl.Sees(c);
                Vector3 sp = shown ? cam.WorldToScreenPoint(_field.ToWorld(c.Anchor) + Vector3.up * 2.2f) : Vector3.zero;
                shown = shown && sp.z > 0 && cam.pixelRect.Contains(sp);
                kv.Value.gameObject.SetActive(shown);
                if (!shown) continue;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_fieldArea, sp, null, out var local))
                    kv.Value.rectTransform.anchoredPosition = local;
                string name = GeneralOf(c);
                if (_bubbles.TryGetValue(c, out var bubble))
                {
                    float left = bubble.left - _fxDelta;
                    if (left <= 0f) _bubbles.Remove(c);
                    else { _bubbles[c] = (bubble.text, left); name = Ui.Colored("\"" + bubble.text + "\"", Palette.Text) + "\n" + name; }
                }
                kv.Value.text = name;
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
                var b = Ui.Button(_skillRow, label, () => PressSkill(id), RowHeight, fontSize: 24);
                Ui.SetEnabled(b, s.Affordable);
                if (_ctl.TargetingSkill == id) b.targetGraphic.color = Palette.ButtonActive;
            }
        }

        /// <summary>스킬 지정 중일 때만: 무엇을, 어디에 (사거리 밖이면 다가가서 씀, 자리를 먼저 고르는 스킬은 이동 후).</summary>
        private void RefreshAimBar()
        {
            string id = _ctl.TargetingSkill;
            bool on = id != null && !IsReplay && _ctl.CanCommand(_ctl.Selected) && _sim.Context.Defs.Skills.ContainsKey(id);
            _aimBar.gameObject.SetActive(on);
            if (!on) return;
            var skill = _sim.Context.Defs.Skills[id];
            string name = T(skill.NameKey);
            bool corpsTarget = skill.TargetType == "enemyCorps" || skill.TargetType == "allyCorps";
            string line;
            if (_ctl.AimPoint == null) line = T(corpsTarget ? "ui.aim.pickCorps" : skill.MoveFirst ? "ui.aim.pickPlace" : "ui.aim.pickPoint", name);
            else if (skill.MoveFirst) line = T("ui.aim.placeChosen", name);
            else line = T(_ctl.AimInRange() ? "ui.aim.inRange" : "ui.aim.outOfRange", name);
            _aimText.text = line;
            _aimConfirm.gameObject.SetActive(_ctl.AimPoint != null);
        }

        private string OrderName(CorpsState c)
        {
            string name = c.PendingSkill != null ? T("ui.order.skill")
                : c.Resting ? T("ui.order.rest")
                : c.Coordinating ? T("ui.order.coordinate")
                : T("ui.order." + c.Order.Kind);
            return c.Queue.Count > 0 ? name + " " + T("ui.order.queued", c.Queue.Count) : name;
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
