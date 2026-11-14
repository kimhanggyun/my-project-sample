using System.Collections.Generic;
using System.Linq;
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
        private const float StatusHeight = 40f;
        private const float HeadHeight = 56f;
        private const float PortraitSize = 104f;
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
        /// <summary>깃발 아래 사기 막대 (R-93 보이기): 채움, 채움 그림, 퇴각 기준 눈금.</summary>
        private readonly Dictionary<CorpsState, (RectTransform fill, Image img, RectTransform mark)> _moraleBars = new Dictionary<CorpsState, (RectTransform, Image, RectTransform)>();
        private readonly Vector3[] _corners = new Vector3[4];
        private RadialMenu _radial;
        // 경유 표식 (4.3.5, R-70): 번호, 누르면 그 예약 취소, 끌면 옮김
        private readonly List<(Button button, Text text)> _marks = new List<(Button, Text)>();
        private readonly List<int> _markIndex = new List<int>();
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
        // 상단 바 둘째 줄 (battle.md 4장 ①): 군량 게이지, 바람, 날씨·시간대
        private Text _supplyText;
        private RectTransform _supplyFill;
        private Text _envText;
        private Button _mapButton;
        private Button _collapseButton;
        private Button _queueButton;
        private RectTransform _prompt;
        // 스킬 지정 막대 (4.2): 무엇을 지정 중인지, [여기에 쓰기] [취소]
        private RectTransform _aimBar;
        // 경고 빠른 버튼 (battle.md 4.4, D20): 경고 말풍선이 뜨면 그 군단에 바로 내릴 명령
        private RectTransform _quickBar;
        private Text _quickText;
        private RectTransform _quickButtons;
        private float _quickLeft;
        private const float QuickSeconds = 6f;
        private Text _aimText;
        private Button _aimConfirm;
        private Text _promptText;
        private string _promptCorps;
        // 적이 건 일기토 (R-79): 응함/거절. 뜨는 동안 전투를 멈춘다 (기한은 전투 시간)
        private RectTransform _duelPrompt;
        private Text _duelNoText, _duelYesText;
        /// <summary>true면 지금 창은 마주침 일기토 기회 (R-91: 신청할까?), false면 받은 신청 (응할까?).</summary>
        private bool _duelPromptIsChance;
        /// <summary>true면 지금 창은 퇴각 선택 (R-93: [버틴다]/[퇴각한다]).</summary>
        private bool _promptIsRetreat;
        private Text _duelPromptText;
        private string _duelOfferCorps;
        private bool _pausedForOffer;
        private RectTransform _panelBody;
        private bool _collapsed;
        private Text _selectedInfo;
        // ③ 선택 군단 (battle.md 4장): 초상, 병력·사기·책략 게이지, 진형 목록, 적 정보
        private Portrait _portrait;
        private RectTransform _troopGauge, _moraleGauge, _pointGauge;
        private RectTransform _formationRow;
        private string _formationRowKey;
        private Text _enemyInfo;
        private CorpsState _inspected;
        private Text _notice;
        private Button _pauseButton;
        private Button _speedButton;
        private Button _delegateButton;
        private Button _moveModeButton;
        private RectTransform _skillRow;
        private readonly List<(CorpsState corps, Button button, Text text)> _cards = new List<(CorpsState, Button, Text)>();
        private readonly List<Button> _orderButtons = new List<Button>();
        private string _skillRowKey;

        // ---- 19 시안: 군단 카드 위 명령 줄 · 여러 군단 고르기 · 대상 고르기 · 작전 시간 (18) ----
        private RectTransform _cmdBar;
        private string _cmdMenu = "root";
        private string _cmdBarKey;
        private readonly List<string> _multi = new List<string>();
        /// <summary>대상 고르는 중: enemy / point / ally / path (null = 아님).</summary>
        private string _pickMode;
        private string _pickKind;
        private string _lureTarget;
        private readonly List<Vec> _path = new List<Vec>();
        private bool _planning;
        private Button _advanceAllButton;
        private readonly Dictionary<CorpsState, (RectTransform troop, RectTransform morale, Text lost, Outline outline)> _cardGauges = new Dictionary<CorpsState, (RectTransform, RectTransform, Text, Outline)>();

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

        /// <summary>샘플전투 (R-87): 세이브 사본으로 만든 계획. 저장·이어하기·결과 반영을 하지 않는다. 끝나면 샘플 결과 화면.</summary>
        public static BattleScreen Sample(SortiePlan plan, string stepId) => new BattleScreen(plan) { _sampleStep = stepId };
        private string _sampleStep;
        private bool IsSample => _sampleStep != null;
        /// <param name="autoFinish">true면 모든 아군 군단을 위임하고 끝까지 돌린다 ([자동으로 끝내기]).</param>
        /// <param name="startPaused">이어 할 때 멈춘 채로 시작 (일기토 화면에서 돌아올 때는 false).</param>
        public BattleScreen(ResumedBattle resumed, bool autoFinish = false, bool startPaused = true) { _plan = resumed.Plan; _resumedSim = resumed.Simulation; _autoFinish = autoFinish; _startPaused = startPaused; }
        private readonly bool _startPaused = true;
        // 전투 중 일기토 (R-71): 내가 건 일기토를 상대가 받으면 대결 화면을 연다
        private Game.Battle.Model.Contests.DuelState _duelToPlay;
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
            if (_replaySim == null) _speedIndex = System.Math.Max(0, System.Array.IndexOf(Speeds, Ctx.DefaultBattleSpeed)); // 설정의 시작 배속 (E26)
            if (!IsReplay && !IsSample && _resumedSim == null)
            {
                // 가져간 군량을 쓴 세이브를 먼저 남기고, 이어하기 기록을 남긴다 (전투 중 꺼져도 이어서)
                Ctx.Save();
                Ctx.SuspendBattle(_plan, _sim);
            }
            if (_resumedSim != null && !_autoFinish && _startPaused) _paused = true; // 이어 할 때는 멈춘 채로 시작
            var ctx = _sim.Context;
            _ctl = new BattleControl(_sim, _viewTeam ?? StoryFlow.PlayerTeam) { RevealAll = _revealAll, ConfirmPointSkills = true }; // 지점 스킬은 범위를 보고 확인
            _ctl.Refresh();
            _tickSeconds = (float)ctx.Combat.TickSeconds.ToDisplayDouble();
            _speech = new SpeechPlanner(ctx.Defs, ctx.Flags, ctx.Combat.TickSeconds,
                id => { var c = ctx.FindCorps(id); return c != null && c.Setup.Controller == Controllers.Ai; });

            Ui.Column(Root, 6, 8);

            // ① 상단: 시간 + 일시정지·배속·끝까지 (얇게)
            var top = Ui.Row(Root, "Top", TopHeight, 8);
            Ui.Background(top, Palette.Bar); // 3D 전장일 때 화면 배경이 꺼져 검은 바탕에 먹색 글자가 안 보였다 (데스크탑 플레이 테스트 2026-11-14)
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
            _mapButton = Ui.Button(top, "", ToggleMiniMap, TopHeight, fontSize: 26);
            Ui.Button(top, T(IsReplay ? "ui.replay.stop" : "ui.battle.skip"), IsReplay ? (System.Action)(() => App.Show(new ReplayListScreen())) : SkipToEnd, TopHeight, fontSize: 28);

            var status = Ui.Row(Root, "Status", StatusHeight, 10);
            Ui.Background(status, Palette.Bar);
            _supplyText = Ui.Label(status, "", 22, TextAnchor.MiddleLeft);
            Ui.Size(_supplyText, width: 150);
            var gauge = Ui.Rect(status, "SupplyGauge");
            Ui.Size(gauge, StatusHeight - 16, flexibleWidth: 2);
            Ui.Background(gauge, new Color(0f, 0f, 0f, 0.45f));
            _supplyFill = Ui.Rect(gauge, "Fill");
            _supplyFill.anchorMin = Vector2.zero;
            _supplyFill.anchorMax = new Vector2(1f, 1f);
            _supplyFill.offsetMin = _supplyFill.offsetMax = Vector2.zero;
            Ui.Background(_supplyFill, Palette.Good);
            _envText = Ui.Label(status, "", 22, TextAnchor.MiddleRight);
            Ui.Size(_envText, flexibleWidth: 3);

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
                var teamCol = c.Team == _ctl.Team ? Palette.Ally : Palette.Enemy;
                var label = Ui.Label(_fieldArea, GeneralOf(c), 24, TextAnchor.LowerCenter, Palette.IsLight ? Color.Lerp(teamCol, Color.white, 0.55f) : teamCol); // 전장 위라 밝게
                Ui.Outlined(label);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.pivot = new Vector2(0.5f, 0f);
                label.rectTransform.sizeDelta = new Vector2(240, 32);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                _flagLabels[c] = label;
                _moraleBars[c] = MoraleBar(label.rectTransform);
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
            Ui.Background(_prompt, Palette.PromptBack);
            Ui.Column(_prompt, 8, 12);
            _promptText = Ui.Label(_prompt, "", 28, TextAnchor.MiddleCenter, Palette.PromptText);
            var pr = Ui.Row(_prompt, "Buttons", 90, 10);
            Ui.Button(pr, T("ui.cmd.keepWaiting"), () => { Report(_ctl.KeepCoordinating(_promptCorps)); ClosePrompt(); }, 90, fontSize: 28);
            Ui.Button(pr, T("ui.cmd.attackNow"), () => { Report(_ctl.AttackNow(_promptCorps)); ClosePrompt(); }, 90, Palette.ButtonActive, 28);
            _prompt.gameObject.SetActive(false);

            _duelPrompt = Ui.Rect(_fieldArea, "DuelPrompt");
            _duelPrompt.anchorMin = new Vector2(0.05f, 0.5f);
            _duelPrompt.anchorMax = new Vector2(0.95f, 0.5f);
            _duelPrompt.pivot = new Vector2(0.5f, 0.5f);
            _duelPrompt.sizeDelta = new Vector2(0f, 230f);
            Ui.Background(_duelPrompt, Color.Lerp(Palette.PromptBack, Palette.Seal, 0.35f));
            Ui.Column(_duelPrompt, 8, 14);
            _duelPromptText = Ui.Label(_duelPrompt, "", 30, TextAnchor.MiddleCenter, Palette.PromptText);
            var dr = Ui.Row(_duelPrompt, "Buttons", 96, 12);
            _duelNoText = Ui.Button(dr, T("ui.duel.refuse"), () => AnswerDuel(false), 96, fontSize: 28).GetComponentInChildren<Text>();
            _duelYesText = Ui.Button(dr, T("ui.duel.accept"), () => AnswerDuel(true), 96, Palette.ButtonActive, 30).GetComponentInChildren<Text>();
            _duelPrompt.gameObject.SetActive(false);

            _aimBar = Ui.Rect(_fieldArea, "AimBar");
            _aimBar.anchorMin = new Vector2(0.05f, 0f);
            _aimBar.anchorMax = new Vector2(0.95f, 0f);
            _aimBar.pivot = new Vector2(0.5f, 0f);
            _aimBar.sizeDelta = new Vector2(0f, 170f);
            _aimBar.anchoredPosition = new Vector2(0f, 16f);
            Ui.Background(_aimBar, Palette.PromptBack);
            Ui.Column(_aimBar, 8, 10);
            _aimText = Ui.Label(_aimBar, "", 26, TextAnchor.MiddleCenter, Palette.PromptText);
            var ar = Ui.Row(_aimBar, "Buttons", 84, 10);
            _aimConfirm = Ui.Button(ar, T("ui.aim.confirm"), () => { Notice(_ctl.ConfirmAim() ? T("ui.cmd.done.skill") : T("ui.cmd.rejected")); _dirty = true; }, 84, Palette.ButtonActive, 28);
            Ui.Button(ar, T("ui.aim.cancel"), () => { _ctl.CancelTargeting(); Notice(T("ui.cmd.cancelled")); _dirty = true; }, 84, fontSize: 28);
            _aimBar.gameObject.SetActive(false);
            _notice = Ui.Label(_fieldArea, "", 28, TextAnchor.UpperCenter, Palette.PromptText);
            Ui.Outlined(_notice);
            _notice.rectTransform.anchorMin = new Vector2(0f, 1f);
            _notice.rectTransform.anchorMax = new Vector2(1f, 1f);
            _notice.rectTransform.pivot = new Vector2(0.5f, 1f);
            _notice.rectTransform.sizeDelta = new Vector2(0f, 48f);
            _notice.rectTransform.anchoredPosition = new Vector2(0f, -8f);

            _quickBar = Ui.Rect(_fieldArea, "QuickBar");
            _quickBar.anchorMin = new Vector2(0.05f, 1f);
            _quickBar.anchorMax = new Vector2(0.95f, 1f);
            _quickBar.pivot = new Vector2(0.5f, 1f);
            _quickBar.sizeDelta = new Vector2(0f, 150f);
            _quickBar.anchoredPosition = new Vector2(0f, -60f);
            Ui.Background(_quickBar, Color.Lerp(Palette.PromptBack, Palette.Seal, 0.3f));
            Ui.Column(_quickBar, 6, 10);
            _quickText = Ui.Label(_quickBar, "", 26, TextAnchor.MiddleCenter, Palette.PromptText);
            _quickButtons = Ui.Row(_quickBar, "Buttons", 76, 8);
            _quickBar.gameObject.SetActive(false);

            // ③④ 선택 군단 정보 + 명령 (접기·펴기, battle.md 4장: 전장이 가려지지 않게)
            var panel = Ui.Card(Root, "Selected", 10, 6);
            var head = Ui.Row(panel, "Head", PortraitSize, 10);
            _portrait = new Portrait(head, PortraitSize);
            Ui.Size(_portrait.Rect, PortraitSize, PortraitSize, flexibleWidth: 0);
            var info = Ui.Rect(head, "Info");
            Ui.Size(info, flexibleWidth: 5);
            Ui.Column(info, 3);
            _selectedInfo = Ui.Label(info, "", 22);
            Ui.Size(_selectedInfo, 56);
            _troopGauge = Ui.Gauge(info, Palette.Good, 12);
            _moraleGauge = Ui.Gauge(info, new Color(0.95f, 0.65f, 0.25f), 10);
            _pointGauge = Ui.Gauge(info, new Color(0.45f, 0.65f, 0.95f), 10);
            _collapseButton = Ui.Button(head, "", ToggleCollapse, PortraitSize, fontSize: 24);
            Ui.Size(_collapseButton, width: 130);
            _panelBody = Ui.Rect(panel, "Body");
            Ui.Column(_panelBody, 6);
            var orders = Ui.Row(_panelBody, "Orders", RowHeight, 6);
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.advance"), () => Report(_ctl.Advance()), RowHeight, fontSize: 26));
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.hold"), () => Report(_ctl.Hold()), RowHeight, fontSize: 26));
            _orderButtons.Add(Ui.Button(orders, T("ui.cmd.fallBack"), () => Report(_ctl.FallBack()), RowHeight, fontSize: 26)); // 뒤로 곧게 (R-60)
            _delegateButton = Ui.Button(orders, "", ToggleDelegate, RowHeight, fontSize: 26);
            _orderButtons.Add(_delegateButton);
            _moveModeButton = Ui.Button(orders, "", NextMoveMode, RowHeight, fontSize: 24);
            var plans = Ui.Row(_panelBody, "Plans", RowHeight, 6);
            _queueButton = Ui.Button(plans, "", () => { _ctl.SetQueueing(!_ctl.Queueing); _dirty = true; }, RowHeight, fontSize: 24);
            _orderButtons.Add(Ui.Button(plans, T("ui.cmd.rest"), () => Report(_ctl.Rest()), RowHeight, fontSize: 24));
            _orderButtons.Add(Ui.Button(plans, T("ui.cmd.coordinate"), () => Report(_ctl.Coordinate()), RowHeight, fontSize: 24));
            _orderButtons.Add(Ui.Button(plans, T("ui.cmd.withdraw"), () => Report(_ctl.Retreat()), RowHeight, Palette.ButtonDisabled, 24)); // 전장 이탈 (퇴각)
            _skillRow = Ui.Row(_panelBody, "Skills", RowHeight, 6);
            _formationRow = Ui.Row(_panelBody, "Formations", RowHeight, 6); // 배운 진형 목록, 지금 진형 강조
            _enemyInfo = Ui.Label(_panelBody, "", 22, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Size(_enemyInfo, 32);

            // 19 시안: 명령 버튼 패널은 숨기고 (전장을 덜 가리게) 군단 카드 위 명령 줄로
            panel.gameObject.SetActive(false);
            _cmdBar = Ui.Row(Root, "CommandBar", 92, 6);
            Ui.Background(_cmdBar, Palette.Bar);
            _cmdBar.gameObject.SetActive(false);

            // ⑤ 하단: 전군 진군(작전 시간엔 전투 시작) + 작은 군단 카드 (병력 게이지 · 잃은 비율 · 사기)
            var bottom = Ui.Row(Root, "Cards", CardHeight + 40, 6);
            _advanceAllButton = Ui.Button(bottom, T("ui.cmd.advanceAll"), () => { if (_planning) StartBattle(); else Report(_ctl.AdvanceAll() > 0); }, CardHeight + 40, Palette.ButtonActive, 26);
            Ui.Size(_advanceAllButton, width: 170);
            foreach (var c in _ctl.OwnCorps())
            {
                var corps = c;
                var b = Ui.Button(bottom, "", () => OnCard(corps), CardHeight + 40, fontSize: 20);
                var text = b.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.UpperLeft;
                text.rectTransform.offsetMin = new Vector2(10, 34);
                text.rectTransform.offsetMax = new Vector2(-6, -4);
                _cards.Add((corps, b, text));
                _cardGauges[corps] = CardGauges((RectTransform)b.transform);
            }

            if (IsReplay)
            {
                // 재생: 명령 패널·군단 카드는 숨기고 "녹화 재생 중" 표시
                panel.gameObject.SetActive(false);
                bottom.gameObject.SetActive(false);
                Ui.Size(Ui.Label(Root, T("ui.replay.playing"), 26, TextAnchor.MiddleCenter, Palette.TextDim), 48);
            }

            if (_resumedSim != null || _replaySim != null) _logRead = _sim.Context.Log.Count; // 이어 볼 때 지난 알림은 다시 띄우지 않는다
            // 처음엔 아군 전체가 한 화면에 (가장자리 군단이 잘리지 않게). 이어 하기·적 시점은 고른 군단·그 팀 첫 군단
            if (_resumedSim == null && _replaySim == null) _field.Frame(_ctl.OwnCorps());
            else _field.LookAt(_ctl.Selected ?? FirstOfViewTeam());
            ReadNewEvents();
            // 18 시안: 새 전투는 승패 조건 카드 → 멈춘 채 작전 시간 → [전투 시작]
            if (!IsReplay && !_autoFinish && _resumedSim == null && _sim.Tick == 0)
            {
                _planning = true;
                _paused = true;
                ShowConditions();
            }
            Refresh();
            // 처음 안내 (R-62): 직접 지휘할 때만, 읽는 동안 멈춘다 (작전 시간이면 조건 카드를 닫은 뒤)
            if (!_planning && !IsReplay && !_autoFinish && !_paused && GuideOverlay.Show(App, Root, "screen.battle", () => { _paused = false; _dirty = true; }, "ui.guide.continue"))
            {
                _paused = true;
                _dirty = true;
            }
        }

        public override void Close()
        {
            _field?.Destroy();
            Sfx.StopAmbience();
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
            if (_quickLeft > 0 && (_quickLeft -= deltaTime) <= 0) _quickBar.gameObject.SetActive(false);
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
            BattleSounds(deltaTime);
            _fxDelta = _paused ? 0f : deltaTime * Speeds[_speedIndex];
            if (steps > 0 || _dirty) Refresh();
            UpdateFieldView();
            if (_sim.IsOver) { Finish(); return; }
            if (_duelToPlay != null) { OpenDuel(); return; }
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
            if (IsSample || _sim.IsOver || _sim.Tick == _suspendedTick) return;
            _suspendedTick = _sim.Tick;
            Ctx.SuspendBattle(_plan, _sim);
        }

        // ---- 입력 ----

        private void OnFieldTap(Vector2 screen)
        {
            if (_finished || IsReplay) return;
            if (!_field.ScreenToField(screen, out var point)) { if (_radial.IsOpen) CloseCorpsMenu(); return; }
            if (_pickMode != null) { PickAt(point); return; } // 19 시안: 명령 줄에서 고른 명령의 대상
            // 이동 지점 고르는 중: 아군 군단이 아니면 그 자리에 방식 고리 (다른 곳을 누르면 그리로 옮김)
            if (_pickingPoint && _ctl.CanCommand(_ctl.Selected))
            {
                var hit = _ctl.Pick(point);
                if (hit == null || hit.Team != _ctl.Team) { OpenMethodMenu(point); return; }
                EndPicking();
            }
            // 메뉴 바깥을 누르면 메뉴를 닫고 그 누름은 그대로 (빠른 조작: 빈 땅 = 이동, 적 = 공격, 4.3.3)
            if (_radial.IsOpen) CloseCorpsMenu();
            var hitEnemy = _ctl.Pick(point);
            if (hitEnemy != null && hitEnemy.Team != _ctl.Team) _inspected = hitEnemy; // 적 정보 줄에 보인다
            var r = _ctl.Tap(point);
            OnTap(r);
            if (r == TapResult.Selected) SelectOnly(_ctl.Selected); // 아군 군단을 누르면 그 군단만 고르고 명령 줄 (19 시안)
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

        // ---- 경유 표식 ----

        private void UpdateWaypointMarks()
        {
            var points = !IsReplay && _ctl.CanCommand(_ctl.Selected) ? _ctl.QueuedPoints() : new List<(int Index, Vec Point, string Kind)>();
            var cam = _field.Rig.Camera;
            int shown = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(_field.ToWorld(points[i].Point) + Vector3.up * 0.3f);
                if (sp.z <= 0 || !cam.pixelRect.Contains(sp) || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_fieldArea, sp, null, out var local)) continue;
                if (shown >= _marks.Count) _marks.Add(NewMark(_marks.Count));
                var (button, text) = _marks[shown];
                while (_markIndex.Count <= shown) _markIndex.Add(0);
                _markIndex[shown] = points[i].Index;
                button.gameObject.SetActive(true);
                ((RectTransform)button.transform).anchoredPosition = local;
                // 번호 = 가는 차례, 스킬 예약이면 스킬 이름 첫 글자를 붙인다
                string label = (i + 1).ToString();
                if (points[i].Kind != Game.Battle.Model.Commands.CommandKinds.MoveTo && _sim.Context.Defs.Skills.TryGetValue(points[i].Kind, out var sd))
                    label += T(sd.NameKey).Substring(0, 1);
                text.text = label;
                shown++;
            }
            for (int i = shown; i < _marks.Count; i++) _marks[i].button.gameObject.SetActive(false);
        }

        private (Button, Text) NewMark(int slot)
        {
            var b = Ui.Button(_fieldArea, "", () => RemoveMark(slot), 64, new Color(0.75f, 0.85f, 1f, 0.95f), 26);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(64, 64);
            var img = (Image)b.targetGraphic;
            img.sprite = UiShapes.Circle;
            img.type = Image.Type.Simple;
            var text = b.GetComponentInChildren<Text>();
            text.color = new Color(0.1f, 0.1f, 0.15f);
            var drag = b.gameObject.AddComponent<DragItem>();
            drag.GhostParent = _fieldArea;
            drag.Dropped += (_, screen) => DropMark(slot, screen);
            return (b, text);
        }

        private void RemoveMark(int slot)
        {
            if (slot >= _markIndex.Count) return;
            bool ok = _ctl.RemoveQueued(_markIndex[slot]);
            Notice(ok ? T("ui.waypoint.removed") : T("ui.cmd.rejected"));
            _dirty = true;
        }

        private void DropMark(int slot, Vector2 screen)
        {
            if (slot >= _markIndex.Count || !_field.ScreenToField(screen, out var point)) return;
            bool ok = _ctl.MoveQueued(_markIndex[slot], point);
            Notice(ok ? T("ui.waypoint.moved") : T("ui.cmd.rejected"));
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

        /// <summary>
        /// 전투 중 일기토 화면 (R-71): 전투는 멈춘 채, 대결을 마치면 선택 기록을 명령으로 넣고 같은 전투로 돌아온다.
        /// 기한 안에 넣으므로 결과는 화면이 보여 준 그대로다 (기록·리플레이·서버 검증도 같은 명령).
        /// </summary>
        private void OpenDuel()
        {
            var duel = _duelToPlay;
            _duelToPlay = null;
            if (duel.Result != null) return;
            var sim = _sim;
            var plan = _plan;
            Suspend();
            var play = new BattleDuelPlay(sim, duel);
            App.Show(new ContestScreen(play, () =>
            {
                sim.Issue(play.Command());
                App.Show(new BattleScreen(ResumedBattle.Of(plan, sim), startPaused: false));
            }));
        }

        /// <summary>경고 빠른 버튼: 그 군단을 고르고 바로 명령. 6초 뒤 저절로 닫힌다 (실제 시간).</summary>
        private void ShowQuick(CorpsState corps, string line, IReadOnlyList<string> actions)
        {
            Ui.Clear(_quickButtons);
            _quickText.text = GeneralOf(corps) + ": \"" + line + "\"";
            foreach (var a in actions)
            {
                string act = a;
                bool can = act != "regroup" || (corps.KnownSkills != null && corps.KnownSkills.Any(k => k.Id == "regroup"));
                var b = Ui.Button(_quickButtons, T("ui.quick." + act), () =>
                {
                    _quickBar.gameObject.SetActive(false);
                    if (!_ctl.Select(corps.Id)) return;
                    bool ok = act == "fallBack" ? _ctl.FallBack()
                        : act == "rest" ? _ctl.Rest()
                        : act == "hold" ? _ctl.Hold()
                        : act == "withdraw" ? _ctl.Retreat()
                        : act == "regroup" && _ctl.PressSkill("regroup");
                    Report(ok);
                }, 76, Palette.ButtonActive, 26);
                if (!can) Ui.SetEnabled(b, false);
            }
            Ui.Button(_quickButtons, T("ui.quick.close"), () => _quickBar.gameObject.SetActive(false), 76, fontSize: 26);
            _quickBar.gameObject.SetActive(true);
            _quickLeft = QuickSeconds;
        }

        private void ToggleMiniMap()
        {
            _miniMap.SetVisible(!_miniMap.Visible);
            _dirty = true;
        }

        /// <summary>상단 둘째 줄: 아군 군량(남은/가져온, 게이지 색은 적을수록 붉게), 바람(화면 기준 방향 화살표 + 세기), 날씨·시간대.</summary>
        private void RefreshStatus()
        {
            long have = 0, start = 0;
            foreach (var c in _ctl.OwnCorps()) { have += c.Supply; start += c.StartSupply; }
            float ratio = start > 0 ? Mathf.Clamp01((float)have / start) : 1f;
            _supplyText.text = T("ui.battle.supply", Mathf.RoundToInt(ratio * 100f));
            _supplyFill.anchorMax = new Vector2(ratio, 1f);
            _supplyFill.GetComponent<Image>().color = ratio > 0.5f ? Palette.Good : ratio > 0.2f ? new Color(0.9f, 0.7f, 0.25f) : Palette.Bad;

            var env = _sim.Context.Environment;
            string wind = T("ui.battle.windNone");
            var strength = env.WindStrength;
            if (strength > Game.Foundation.Math.Fix.Zero) wind = T("ui.battle.wind", WindArrow(env.WindDirection), T("ui.battle.windLevel." + System.Math.Min(3, strength.CeilToLong())));
            string weather = env.EffectiveWeather.Id != null && Ctx.Defs.Strings.ContainsKey("weather." + env.EffectiveWeather.Id) ? T("weather." + env.EffectiveWeather.Id) : "";
            string time = env.EffectiveTime.Id != null && Ctx.Defs.Strings.ContainsKey("timeOfDay." + env.EffectiveTime.Id) ? T("timeOfDay." + env.EffectiveTime.Id) : "";
            _envText.text = string.Join("  ", new[] { ObjectiveText(), wind, weather, time }.Where(x => x.Length > 0));
            SetLabel(_mapButton, T(_miniMap.Visible ? "ui.battle.mapHide" : "ui.battle.mapShow"));
        }

        /// <summary>목표 (R-78): 버티기 남은 시간, 점령 구역 진행 (아군 목표면 아군 색, 적 목표면 적 색).</summary>
        private string ObjectiveText()
        {
            var ctx = _sim.Context;
            var parts = new List<string>();
            var h = ctx.Setup.HoldOut;
            if (h != null && ctx.Setup.DefeatConditions.Contains(Game.Battle.Model.Victory.VictoryConditionIds.HoldOut))
            {
                long left = System.Math.Max(0, (h.Seconds - ctx.Combat.TickSeconds * ctx.Tick).CeilToLong());
                parts.Add(Ui.Colored(T(h.Team == _ctl.Team ? "ui.objective.holdOut" : "ui.objective.enemyHoldOut", left / 60, (left % 60).ToString("00")), h.Team == _ctl.Team ? Palette.Ally : Palette.Enemy));
            }
            if (ctx.Setup.DefeatConditions.Contains(Game.Battle.Model.Victory.VictoryConditionIds.Zone))
                foreach (var o in ctx.Objects)
                {
                    if (o.Setup.Type != Game.Battle.Model.Victory.MapObjectSetup.Zone) continue;
                    long held = (ctx.Combat.TickSeconds * o.HeldTicks).FloorToLong();
                    bool mine = o.Setup.Team == _ctl.Team;
                    parts.Add(Ui.Colored(T(mine ? "ui.objective.zone" : "ui.objective.enemyZone", held, o.Setup.Seconds.FloorToLong()), mine ? Palette.Ally : Palette.Enemy));
                }
            return string.Join("  ", parts);
        }

        /// <summary>바람이 불어 가는 방향을 지금 카메라 화면 기준 8방향 화살표로.</summary>
        private string WindArrow(Vec dir)
        {
            var cam = _field.Rig.Camera;
            var center = new Vec(Game.Foundation.Math.Fix.FromInt(_field.Width / 2), Game.Foundation.Math.Fix.FromInt(_field.Height / 2));
            Vector3 a = cam.WorldToScreenPoint(_field.ToWorld(center));
            Vector3 b = cam.WorldToScreenPoint(_field.ToWorld(new Vec(center.X + dir.X * Game.Foundation.Math.Fix.FromInt(4), center.Y + dir.Y * Game.Foundation.Math.Fix.FromInt(4))));
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg; // 0 = 오른쪽, 반시계
            string[] arrows = { "→", "↗", "↑", "↖", "←", "↙", "↓", "↘" };
            int i = Mathf.RoundToInt(((ang % 360f) + 360f) % 360f / 45f) % 8;
            return arrows[i];
        }

        private void TogglePause()
        {
            if (_planning) { StartBattle(); return; }
            _paused = !_paused;
            _dirty = true;
        }
        /// <summary>개발 도구용 (PlayTestTour): 조작·시뮬레이션·배속.</summary>
        internal BattleControl DebugControl => _ctl;
        internal BattleSimulation DebugSim => _sim;
        internal void DebugSetSpeed(int index) { _speedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1); _dirty = true; }

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
            if (IsSample) { App.Show(new SampleResultScreen(_sampleStep, result)); return; }
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
                if (corps == null || _ctl.Sees(corps)) EventSound(e.Kind);
                if (!IsReplay && e.Kind == EventKinds.CoordinateWaiting && corps != null && corps.Team == _ctl.Team && corps.Setup.Controller == Controllers.Player && !corps.Delegated)
                {
                    _promptCorps = corps.Id;
                    _promptText.text = T("ui.cmd.coordinatePrompt", GeneralOf(corps));
                    _prompt.gameObject.SetActive(true);
                }
                if (!IsReplay && !_autoFinish && e.Kind == EventKinds.DuelStarted)
                {
                    // 건 쪽이든 받은 쪽(R-79)이든 내가 직접 지휘하는 군단이면 대결 화면
                    var other = e.Data == null ? null : _sim.Context.FindCorps(e.Data);
                    var mine = corps != null && _ctl.CanCommand(corps) && !corps.Delegated ? corps
                        : other != null && _ctl.CanCommand(other) && !other.Delegated ? other : null;
                    if (mine != null) _duelToPlay = Game.Battle.Model.Contests.InBattleDuels.PendingFor(_sim.Context, mine.Id);
                }
                if (!IsReplay && !_autoFinish && e.Kind == EventKinds.RetreatAsked && corps != null && _ctl.CanCommand(corps) && !corps.Delegated)
                    ShowRetreatAsk(corps, e.Data);
                if (!IsReplay && !_autoFinish && e.Kind == EventKinds.DuelChance && corps != null && _ctl.CanCommand(corps) && !corps.Delegated)
                {
                    var target = e.Data == null ? null : _sim.Context.FindCorps(e.Data);
                    if (target != null) ShowDuelChance(corps, target);
                }
                if (!IsReplay && !_autoFinish && e.Kind == EventKinds.DuelOffered && corps != null && _ctl.CanCommand(corps) && !corps.Delegated)
                {
                    var from = e.Data == null ? null : _sim.Context.FindCorps(e.Data);
                    if (from != null) ShowDuelOffer(corps, from);
                }
                if (e.Kind == EventKinds.SkillUsed && corps != null && _ctl.Sees(corps))
                    _field.Effects.SkillUsed(_field.ToWorld(corps.Anchor), corps.Team == _ctl.Team ? Palette.Ally : Palette.Enemy);
                if (e.Kind == EventKinds.Dialogue) { Add(string.IsNullOrEmpty(who) ? T(e.Data) : who + ": " + T(e.Data)); Bubble(corps, T(e.Data)); continue; } // 말한 군단이 없으면(지문) 이름 없이
                var sp = _speech.Feed(e);
                if (sp != null) { Add(who + ": \"" + T(sp.LineKey) + "\""); Bubble(corps, T(sp.LineKey)); }
                if (sp != null && sp.Kind == "warning" && sp.QuickActions.Count > 0 && !IsReplay && !_autoFinish && corps != null && _ctl.CanCommand(corps) && !corps.Delegated
                    && corps.RetreatAskReason == null && !(_promptIsRetreat && _duelOfferCorps == corps.Id)) // 퇴각 선택 중에는 같은 말을 또 하지 않는다 (B5)
                    ShowQuick(corps, T(sp.LineKey), sp.QuickActions);
                string sysKey = "ui.event." + e.Kind;
                if (e.Data != null && Ctx.Defs.Strings.ContainsKey(sysKey + "." + e.Data)) sysKey += "." + e.Data; // 까닭별 문구 (퇴각 이유 등, R-93)
                if (Ctx.Defs.Strings.ContainsKey(sysKey))
                {
                    // {0} = 이 이벤트의 군단, {1} = 값이 군단 id면 그 군단 (일기토의 상대 등)
                    string other = e.Data != null && _sim.Context.FindCorps(e.Data) != null ? CorpsName(e.Data) : (e.Data ?? "");
                    string line = T(sysKey, who, other);
                    bool big = e.Kind == EventKinds.RetreatStarted || e.Kind == EventKinds.StandFast; // 퇴각·패주·버티기는 눈에 띄게 + 깃발 말풍선 (B6)
                    Add(Ui.Colored(line, big ? (corps != null && corps.Team == _ctl.Team ? Palette.Bad : Palette.Good) : Palette.TextDim));
                    if (big && corps != null) Bubble(corps, line);
                }
            }
        }

        private void Bubble(CorpsState corps, string text)
        {
            if (corps != null) _bubbles[corps] = (text, BubbleSeconds);
        }

        /// <summary>전황 한 줄: 전장 위 알림으로 잠깐 보여준다 (대사·말풍선은 깃발 위).</summary>
        private void Add(string line) => Notice(line);

        private float _clashTimer, _arrowTimer;
        private bool _startDrum;

        /// <summary>
        /// 전장 소리 (임시 합성음, Sfx): 시작 북, 보이는 접전 수만큼 커지는 함성, 접전마다 칼 부딪힘, 원거리 교전에 화살.
        /// 멈추면 소음도 줄어든다. 규칙에는 영향 없음.
        /// </summary>
        private void BattleSounds(float deltaTime)
        {
            if (!_startDrum) { _startDrum = true; Sfx.Play("drum"); }
            int melee = 0, ranged = 0;
            if (!_paused)
                foreach (var c in _sim.Context.Corps)
                {
                    if (!c.InBattle || !_ctl.Sees(c)) continue;
                    if (c.MeleeContact) melee++;
                    else if (c.Engaged) ranged++;
                }
            Sfx.Ambience(_paused ? 0f : 0.15f + 0.12f * Mathf.Min(melee, 5), deltaTime);
            float speed = _paused ? 0f : Speeds[_speedIndex];
            if (melee > 0 && (_clashTimer -= deltaTime * speed) <= 0f)
            {
                _clashTimer = UnityEngine.Random.Range(0.25f, 0.6f) / Mathf.Min(melee, 3);
                Sfx.Play("clash", UnityEngine.Random.Range(0.25f, 0.55f));
            }
            if (ranged > 0 && (_arrowTimer -= deltaTime * speed) <= 0f)
            {
                _arrowTimer = UnityEngine.Random.Range(0.6f, 1.2f);
                Sfx.Play("arrow", 0.4f);
            }
        }

        /// <summary>전투 이벤트 → 효과음 (보이는 것만).</summary>
        private static void EventSound(string kind)
        {
            switch (kind)
            {
                case EventKinds.SkillUsed: Sfx.Play("horn", 0.6f, 0.8f); break;
                case EventKinds.RetreatStarted:
                case EventKinds.Destroyed:
                case EventKinds.Captured: Sfx.Play("gong", 0.7f, 0.5f); break;
                case EventKinds.AmbushSprung:
                case EventKinds.Ambushed: Sfx.Play("drum"); Sfx.Play("cheer", 0.6f); break;
                case EventKinds.FireHit: Sfx.Play("fire", 0.6f, 0.8f); break;
                case EventKinds.SupplyLow:
                case EventKinds.MoraleCritical:
                case EventKinds.HiddenFound:
                case EventKinds.DuelOffered: Sfx.Play("alert", 0.7f, 0.3f); break;
                case EventKinds.DuelChance: Sfx.Play("alert", 0.7f, 0.3f); break;
                case EventKinds.RetreatAsked: Sfx.Play("alert", 0.7f, 0.3f); break;
                case EventKinds.DuelStarted: Sfx.Play("duel"); break;
                case EventKinds.DuelEnded: Sfx.Play("clash", 0.9f); break;
                case EventKinds.TrapSprung:
                case EventKinds.RockfallHit: Sfx.Play("drum"); Sfx.Play("clash", 0.8f); break;
                case EventKinds.FeignSprung: Sfx.Play("horn", 0.8f); Sfx.Play("cheer", 0.6f); break;
                case EventKinds.Discorded:
                case EventKinds.CounterIntrigued:
                case EventKinds.SupplyRaided: Sfx.Play("alert", 0.7f, 0.3f); break;
                case EventKinds.FirstAid: Sfx.Play("cheer", 0.4f); break;
                case EventKinds.PalisadeBuilt: Sfx.Play("drum", 0.5f); break;
                case EventKinds.FireShipHit: Sfx.Play("fire", 0.9f); Sfx.Play("cheer", 0.5f); break;
                case EventKinds.Rammed: Sfx.Play("drum"); Sfx.Play("clash", 0.9f); break;
                case EventKinds.DamBroken: Sfx.Play("drum"); Sfx.Play("arrow", 1f); Sfx.Play("cheer", 0.7f); break;
                case EventKinds.BridgeDestroyed:
                case EventKinds.PalisadeBurned: Sfx.Play("gong", 0.6f); break;
                case EventKinds.TrapFound:
                case EventKinds.DecoyExposed: Sfx.Play("alert", 0.6f, 0.3f); break;
            }
        }

        /// <summary>적의 일기토 신청 (R-79): 전투를 멈추고 응함/거절을 묻는다.</summary>
        /// <summary>퇴각 선택 (R-93): 내 군단이 퇴각 조건. [버틴다]/[퇴각한다]. 답이 없으면 퇴각.</summary>
        private void ShowRetreatAsk(CorpsState mine, string reason)
        {
            _quickBar.gameObject.SetActive(false); // 조언 창과 겹치지 않게 (B5)
            _duelOfferCorps = mine.Id;
            _promptIsRetreat = true;
            _duelPromptIsChance = false;
            _duelPromptText.text = T(reason == "troops" ? "ui.retreatAsk.troops" : "ui.retreatAsk.morale", GeneralOf(mine), mine.Troops.ToString("N0"), mine.Morale.FloorToLong());
            _duelNoText.text = T("ui.retreatAsk.hold");
            _duelYesText.text = T("ui.retreatAsk.retreat");
            _duelPrompt.gameObject.SetActive(true);
            if (!_paused) { _paused = true; _pausedForOffer = true; }
            _dirty = true;
        }

        /// <summary>마주침 일기토 기회 (R-91): 내 군단이 거는 쪽. [넘어가기]/[일기토 신청].</summary>
        private void ShowDuelChance(CorpsState mine, CorpsState target)
        {
            _duelOfferCorps = mine.Id;
            _duelPromptIsChance = true;
            _promptIsRetreat = false;
            _duelPromptText.text = T("ui.duel.chance", GeneralOf(mine), GeneralOf(target));
            _duelNoText.text = T("ui.duel.pass");
            _duelYesText.text = T("ui.duel.challenge");
            _duelPrompt.gameObject.SetActive(true);
            if (!_paused) { _paused = true; _pausedForOffer = true; }
            _dirty = true;
        }

        private void ShowDuelOffer(CorpsState mine, CorpsState challenger)
        {
            _duelOfferCorps = mine.Id;
            _duelPromptIsChance = false;
            _promptIsRetreat = false;
            _duelNoText.text = T("ui.duel.refuse");
            _duelYesText.text = T("ui.duel.accept");
            _duelPromptText.text = T("ui.duel.offer", GeneralOf(challenger), GeneralOf(mine));
            _duelPrompt.gameObject.SetActive(true);
            if (!_paused) { _paused = true; _pausedForOffer = true; }
            _dirty = true;
        }

        private void AnswerDuel(bool accept)
        {
            // 퇴각 선택에서 "예"(오른쪽)는 퇴각, "아니오"(왼쪽)는 버틴다
            if (_duelOfferCorps != null) Report(_promptIsRetreat ? _ctl.AnswerRetreat(_duelOfferCorps, accept)
                : _duelPromptIsChance ? _ctl.AnswerDuelChance(_duelOfferCorps, accept) : _ctl.AnswerDuel(_duelOfferCorps, accept));
            CloseDuelOffer();
        }

        private void CloseDuelOffer()
        {
            _duelOfferCorps = null;
            _duelPrompt.gameObject.SetActive(false);
            if (_pausedForOffer) { _paused = false; _pausedForOffer = false; }
            _dirty = true;
        }

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

        /// <summary>깃발 이름표 아래(이름표 원점 = 깃발 꼭대기) 가는 사기 막대. 눈금 = 퇴각 기준 사기.</summary>
        private static (RectTransform fill, Image img, RectTransform mark) MoraleBar(RectTransform label)
        {
            var bar = Ui.Rect(label, "Morale");
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(84f, 9f);
            bar.anchoredPosition = new Vector2(0f, -2f);
            var bg = Ui.Background(bar, new Color(0f, 0f, 0f, 0.55f));
            bg.raycastTarget = false;
            var fill = Ui.Rect(bar, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMin = new Vector2(1f, 1f);
            fill.offsetMax = new Vector2(-1f, -1f);
            var img = Ui.Background(fill, Palette.Good);
            img.raycastTarget = false;
            var mark = Ui.Rect(bar, "Threshold");
            mark.anchorMin = new Vector2(0f, 0f);
            mark.anchorMax = new Vector2(0f, 1f);
            mark.pivot = new Vector2(0.5f, 0.5f);
            mark.sizeDelta = new Vector2(2f, 4f);
            var mi = Ui.Background(mark, Color.white);
            mi.raycastTarget = false;
            return (fill, img, mark);
        }

        private static void UpdateMoraleBar(CorpsState c, (RectTransform fill, Image img, RectTransform mark) mb)
        {
            float m = Mathf.Clamp01((float)c.Morale.ToDisplayDouble() / 100f);
            float thr = c.Temperament != null ? Mathf.Clamp01((float)c.Temperament.RetreatMorale.ToDisplayDouble() / 100f) : 0f;
            mb.fill.anchorMax = new Vector2(m, 1f);
            // 기준까지 남은 여유로 색: 넉넉 = 초록, 기준 + 15 안 = 노랑, 기준 밑(버티는 중) = 빨강
            mb.img.color = m <= thr ? Palette.Bad : m <= thr + 0.15f ? new Color(0.95f, 0.75f, 0.2f) : Palette.Good;
            mb.mark.anchorMin = new Vector2(thr, 0f);
            mb.mark.anchorMax = new Vector2(thr, 1f);
            mb.mark.anchoredPosition = Vector2.zero;
        }

        /// <summary>깃발 이름 (D20): 아군은 군단 번호(①②…), 총대장은 ★, 그다음 장수 이름.</summary>
        private string FlagName(CorpsState c)
        {
            string mark = "";
            if (c.Team == _ctl.Team)
            {
                int n = 0;
                foreach (var o in _sim.Context.Corps) { if (o.Team != c.Team) continue; if (o == c) break; n++; }
                mark = n < 20 ? ((char)('\u2460' + n)).ToString() : (n + 1) + ".";
            }
            if (c.Setup.Role == Game.Battle.Model.Simulation.CorpsRoles.Commander) mark = "★" + mark;
            return mark + GeneralOf(c);
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
            // 전투명 + 주차 (1분 = 1주, battle.md 4장 ①)
            _clock.text = T("ui.battle.top", T(_plan.Step.NameKey), elapsed / 60 + 1, System.Math.Max(1, max / 60));
            RefreshStatus();
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
            RefreshHead(sel);
            RefreshFormations(can);
            RefreshEnemyInfo(sel);
            RefreshSkills(can);

            RefreshCards(sel);
            RefreshCommandBar();
            if (_advanceAllButton != null) SetLabel(_advanceAllButton, _planning ? T("ui.battle.startBattle") : T("ui.cmd.advanceAll"));

            RefreshAimBar();
            SetLabel(_collapseButton, _collapsed ? T("ui.cmd.expand") : T("ui.cmd.collapse"));
            SetLabel(_queueButton, _ctl.Queueing ? T("ui.cmd.queueOn", _ctl.QueueCount, ctx.Combat.OrderQueueMax) : T("ui.cmd.queueOff", _ctl.QueueCount));
            Ui.Recolor(_queueButton, _ctl.Queueing ? Palette.ButtonActive : Palette.Button);
            if (_duelOfferCorps != null && (_promptIsRetreat
                    ? ctx.FindCorps(_duelOfferCorps)?.RetreatAskReason == null
                    : _duelPromptIsChance
                    ? Game.Battle.Model.Contests.InBattleDuels.ChanceFor(ctx, _duelOfferCorps) == null
                    : Game.Battle.Model.Contests.InBattleDuels.OfferTo(ctx, _duelOfferCorps) == null)) CloseDuelOffer();
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
            UpdateWaypointMarks();
            var ctx = _sim.Context;
            var cam = _field.Rig.Camera;
            foreach (var kv in _flagLabels)
            {
                var c = kv.Key;
                bool shown = c.InBattle && _ctl.Sees(c);
                Vector3 sp = shown ? cam.WorldToScreenPoint(_field.FlagPoint(c)) : Vector3.zero;
                shown = shown && sp.z > 0 && cam.pixelRect.Contains(sp);
                kv.Value.gameObject.SetActive(shown);
                if (!shown) continue;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_fieldArea, sp, null, out var local))
                    kv.Value.rectTransform.anchoredPosition = local;
                string name = FlagName(c);
                if (_bubbles.TryGetValue(c, out var bubble))
                {
                    float left = bubble.left - _fxDelta;
                    if (left <= 0f) _bubbles.Remove(c);
                    else { _bubbles[c] = (bubble.text, left); name = Ui.Colored("\"" + bubble.text + "\"", Palette.Text) + "\n" + name; }
                }
                kv.Value.text = name;
                kv.Value.fontStyle = c == _ctl.Selected ? FontStyle.Bold : FontStyle.Normal;
                if (_moraleBars.TryGetValue(c, out var mb)) UpdateMoraleBar(c, mb);
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
                if (_ctl.TargetingSkill == id) Ui.Recolor(b, Palette.ButtonActive);
            }
        }

        /// <summary>초상과 게이지: 병력(남은/처음), 사기(0~100), 책략 점수(처음 점수나 100 중 큰 것 기준).</summary>
        private void RefreshHead(CorpsState sel)
        {
            bool on = sel != null;
            _portrait.Rect.gameObject.SetActive(on);
            foreach (var g in new[] { _troopGauge, _moraleGauge, _pointGauge }) g.parent.gameObject.SetActive(on);
            if (!on) return;
            _portrait.Show(sel.Setup.General?.Id, GeneralOf(sel), sel.Team == StoryFlow.PlayerTeam ? Palette.Ally : Palette.Enemy);
            Ui.SetGauge(_troopGauge, sel.StartTroops > 0 ? (float)sel.Troops / sel.StartTroops : 0f);
            Ui.SetGauge(_moraleGauge, (float)sel.Morale.ToDisplayDouble() / 100f);
            float scale = Mathf.Max(100f, (float)sel.SkillPoints.Start.ToDisplayDouble());
            Ui.SetGauge(_pointGauge, (float)sel.SkillPoints.Points.ToDisplayDouble() / scale);
        }

        /// <summary>진형 목록: 누르면 그 진형으로 (바꾸는 중이면 바뀔 진형을 강조). 선택·진형이 바뀔 때만 다시 만든다.</summary>
        private void RefreshFormations(bool can)
        {
            var sel = _ctl.Selected;
            var list = can ? _ctl.Formations() : new List<string>();
            string now = sel == null ? "" : (sel.PendingFormation ?? sel.Formation).Id;
            string key = (sel?.Id ?? "-") + "|" + now + "|" + string.Join(",", list);
            if (key == _formationRowKey) return;
            _formationRowKey = key;
            Ui.Clear(_formationRow);
            _formationRow.gameObject.SetActive(list.Count > 1);
            foreach (var f in list)
            {
                string id = f;
                string name = _sim.Context.Defs.Formations.TryGetValue(id, out var fd) ? T(fd.NameKey) : id;
                var b = Ui.Button(_formationRow, name, () => Report(_ctl.ChangeFormation(id)), RowHeight, id == now ? Palette.ButtonActive : Palette.Button, 24);
                if (id == now) b.interactable = false;
            }
        }

        /// <summary>
        /// 적 정보: 선택 군단이 치는 적, 없으면 마지막으로 누른 적. 보이면 병력(백 단위 어림)·진형·사기, 안 보이면 이름만.
        /// </summary>
        private void RefreshEnemyInfo(CorpsState sel)
        {
            var ctx = _sim.Context;
            CorpsState enemy = null;
            if (sel != null && sel.Order.Kind == OrderKinds.Attack) enemy = ctx.FindCorps(sel.Order.TargetCorpsId);
            if (enemy == null) enemy = _inspected;
            if (enemy == null || !enemy.InBattle) { _enemyInfo.text = T("ui.enemyInfo.none"); return; }
            if (!_ctl.Sees(enemy)) { _enemyInfo.text = T("ui.enemyInfo.hidden", CorpsName(enemy.Id)); return; }
            string formation = ctx.Defs.Formations.TryGetValue(enemy.Formation.Id, out var f) ? T(f.NameKey) : enemy.Formation.Id;
            long troops = (enemy.Troops + 50) / 100 * 100;
            _enemyInfo.text = T("ui.enemyInfo.line", CorpsName(enemy.Id), troops.ToString("N0"), formation, enemy.Morale.FloorToLong());
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

        // ======== 18 · 19 시안 ========

        /// <summary>승패 조건 카드 (18 시안): 이기려면 (초록) · 지면 (빨강) · [작전 시작 ›].</summary>
        private void ShowConditions()
        {
            var ctx = _sim.Context;
            var dim = Ui.Rect(Root, "CondDim");
            dim.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Fill(dim);
            Ui.Background(dim, new Color(0f, 0f, 0f, 0.55f));
            var box = Ui.Rect(dim, "Cond");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(960, 860);
            Ui.Round(Ui.Background(box, Palette.Background));
            Ui.Column(box, 14, 34);
            Ui.Size(Ui.Label(box, T(_plan?.Step?.NameKey ?? "ui.battle.plan"), 46, TextAnchor.MiddleCenter), 80);
            var win = new List<string>();
            var lose = new List<string>();
            foreach (var id in ctx.Setup.DefeatConditions)
            {
                if (id == Game.Battle.Model.Victory.VictoryConditionIds.HoldOut && ctx.Setup.HoldOut != null)
                {
                    long min = ctx.Setup.HoldOut.Seconds.CeilToLong() / 60;
                    if (ctx.Setup.HoldOut.Team == _ctl.Team) win.Add(T("ui.battle.cond.holdOut.win", min)); else lose.Add(T("ui.battle.cond.holdOut.lose", min));
                    continue;
                }
                if (id == Game.Battle.Model.Victory.VictoryConditionIds.Zone)
                {
                    foreach (var o in ctx.Objects)
                        if (o.Setup.Type == Game.Battle.Model.Victory.MapObjectSetup.Zone)
                        {
                            if (o.Setup.Team == _ctl.Team) win.Add(T("ui.battle.cond.zone.win", o.Setup.Seconds.FloorToLong())); else lose.Add(T("ui.battle.cond.zone.lose", o.Setup.Seconds.FloorToLong()));
                        }
                    continue;
                }
                if (Ctx.Defs.Strings.ContainsKey("ui.battle.cond." + id + ".win")) win.Add(T("ui.battle.cond." + id + ".win"));
                if (Ctx.Defs.Strings.ContainsKey("ui.battle.cond." + id + ".lose")) lose.Add(T("ui.battle.cond." + id + ".lose"));
            }
            lose.Add(T("ui.battle.cond.time", (Setup.MaxMinutes).FloorToLong()));
            CondCard(box, T("ui.battle.cond.win"), win, new Color(0.86f, 0.93f, 0.84f), Palette.Good);
            CondCard(box, T("ui.battle.cond.lose"), lose, new Color(0.96f, 0.86f, 0.83f), Palette.Bad);
            Ui.Spacer(box, 0, 1);
            Ui.Styled(Ui.PrimaryButton(box, T("ui.battle.cond.start"), () =>
            {
                Object.Destroy(dim.gameObject);
                Notice(T("ui.battle.planNote"));
                GuideOverlay.Show(App, Root, "screen.battle", () => { _dirty = true; }, "ui.guide.continue");
                _dirty = true;
            }, 110), "button.primary");
        }

        private static void CondCard(RectTransform box, string head, List<string> lines, Color back, Color accent)
        {
            var card = Ui.Card(box, head, 18, 4);
            card.GetComponent<Image>().color = back;
            Ui.Label(card, Ui.Colored(head, accent), 30);
            foreach (var l in lines) Ui.Label(card, "· " + l, 26, TextAnchor.MiddleLeft, Palette.Text);
        }

        /// <summary>작전 시간 끝 → 전투 시작 (멈춤 풀기).</summary>
        private void StartBattle()
        {
            _planning = false;
            _paused = false;
            Notice(T("ui.battle.started"));
            _dirty = true;
        }

        /// <summary>군단 카드 아래 병력 게이지(남은 초록 · 잃은 옅은 빨강) · 사기 막대 · 잃은 비율 글자, 고른 표시 테두리.</summary>
        private static (RectTransform troop, RectTransform morale, Text lost, Outline outline) CardGauges(RectTransform card)
        {
            RectTransform Bar(float y, float h, Color back)
            {
                var bar = Ui.Rect(card, "Bar");
                bar.anchorMin = new Vector2(0f, 0f);
                bar.anchorMax = new Vector2(1f, 0f);
                bar.pivot = new Vector2(0.5f, 0f);
                bar.offsetMin = new Vector2(10, y);
                bar.offsetMax = new Vector2(-10, y + h);
                var bg = Ui.Background(bar, back);
                bg.raycastTarget = false;
                var fill = Ui.Rect(bar, "Fill");
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = Vector2.one;
                fill.offsetMin = fill.offsetMax = Vector2.zero;
                return fill;
            }
            var troop = Bar(18, 12, new Color(0.85f, 0.45f, 0.4f, 0.55f));
            Ui.Background(troop, Palette.Good).raycastTarget = false;
            var morale = Bar(6, 6, new Color(0f, 0f, 0f, 0.35f));
            Ui.Background(morale, new Color(0.95f, 0.65f, 0.25f)).raycastTarget = false;
            var lost = Ui.Label(card, "", 18, TextAnchor.LowerRight, Palette.Bad);
            lost.raycastTarget = false;
            lost.rectTransform.anchorMin = new Vector2(0f, 0f);
            lost.rectTransform.anchorMax = new Vector2(1f, 0f);
            lost.rectTransform.pivot = new Vector2(0.5f, 0f);
            lost.rectTransform.offsetMin = new Vector2(8, 30);
            lost.rectTransform.offsetMax = new Vector2(-8, 54);
            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.Bad;
            outline.effectDistance = new Vector2(4, -4);
            outline.enabled = false;
            return (troop, morale, lost, outline);
        }

        private void RefreshCards(CorpsState sel)
        {
            foreach (var (corps, button, text) in _cards)
            {
                bool picked = _multi.Contains(corps.Id);
                string mark = (corps.Setup.Role == Game.Battle.Model.Simulation.CorpsRoles.Commander ? "★" : "") + (picked ? "✓ " : "");
                string line = mark + GeneralOf(corps) + (corps.Delegated ? " " + T("ui.battle.delegated") : "") + "\n" + corps.Troops.ToString("N0") + "/" + corps.StartTroops.ToString("N0");
                text.text = corps.InBattle ? line : Ui.Colored(line + "\n" + T("ui.corps." + StatusKey(corps.Status)), Palette.TextDim);
                Ui.Recolor(button, picked || corps == sel ? Palette.ButtonActive : corps.InBattle ? Palette.Button : Palette.ButtonDisabled);
                if (!_cardGauges.TryGetValue(corps, out var g)) continue;
                float ratio = corps.StartTroops <= 0 ? 0f : Mathf.Clamp01((float)corps.Troops / corps.StartTroops);
                g.troop.anchorMax = new Vector2(ratio, 1f);
                g.morale.anchorMax = new Vector2(Mathf.Clamp01((float)corps.Morale.ToDisplayDouble() / 100f), 1f);
                long lostN = corps.StartTroops - corps.Troops;
                g.lost.text = lostN > 0 ? T("ui.battle.lost", lostN.ToString("N0"), corps.StartTroops <= 0 ? 0 : lostN * 100 / corps.StartTroops) : "";
                g.outline.enabled = picked || (ratio < 0.3f && corps.InBattle); // 고름 = 빨간 테두리, 병력이 적어도 빨간 테두리 (위험)
            }
        }

        /// <summary>군단 카드: 누를 때마다 고르기 켜고 끄기 (여러 군단, 19 시안). 엄호 대상 고르는 중이면 그 군단을 대상으로.</summary>
        private void OnCard(CorpsState corps)
        {
            if (IsReplay) return;
            if (_pickMode == "ally") { FinishAlly(corps); return; }
            if (_multi.Contains(corps.Id)) _multi.Remove(corps.Id);
            else if (_ctl.CanCommand(corps)) _multi.Add(corps.Id);
            if (_multi.Count > 0) { _ctl.Select(_multi[_multi.Count - 1]); _field.LookAt(_ctl.Selected); }
            else _ctl.ClearSelection();
            _cmdMenu = "root";
            CancelPick();
            _dirty = true;
        }

        private void SelectOnly(CorpsState corps)
        {
            _multi.Clear();
            if (corps != null && _ctl.CanCommand(corps)) _multi.Add(corps.Id);
            _cmdMenu = "root";
            CancelPick();
            _dirty = true;
        }

        /// <summary>지금 명령을 받을 군단 (여러 군단 고름, 없으면 고른 군단 하나).</summary>
        private List<string> Chosen()
        {
            var list = new List<string>();
            foreach (var id in _multi) { var c = _sim.Context.FindCorps(id); if (_ctl.CanCommand(c)) list.Add(id); }
            if (list.Count == 0 && _ctl.CanCommand(_ctl.Selected)) list.Add(_ctl.Selected.Id);
            return list;
        }

        private void CancelPick()
        {
            _pickMode = null;
            _pickKind = null;
            _lureTarget = null;
            _path.Clear();
        }

        private void BeginPick(string mode, string kind, string hintKey)
        {
            _pickMode = mode;
            _pickKind = kind;
            _path.Clear();
            Notice(T(hintKey));
            _dirty = true;
        }

        private void Done(int n, string kindKey)
        {
            Notice(n > 0 ? (n > 1 ? T("ui.battle.together", n) + " · " : "") + T("ui.battle.cmd." + kindKey) : T("ui.cmd.rejected"));
            CancelPick();
            _cmdMenu = "root";
            _dirty = true;
        }

        /// <summary>대상 고르는 중의 누름 (적 · 땅 · 아군 · 경로 점).</summary>
        private void PickAt(Vec point)
        {
            var ids = Chosen();
            if (ids.Count == 0) { CancelPick(); _dirty = true; return; }
            var hit = _ctl.Pick(point);
            bool enemy = hit != null && hit.Team != _ctl.Team && _ctl.Sees(hit);
            switch (_pickMode)
            {
                case "enemy":
                    if (!enemy) { Notice(T("ui.battle.pickTarget")); return; }
                    if (_pickKind == AttackModes.Lure) { _lureTarget = hit.Id; BeginPick("point", "lureBack", "ui.battle.pickBack"); return; }
                    if (_pickKind == AttackModes.Normal && ids.Count > 1) { Done(_ctl.Volley(ids, hit.Id), "volley"); return; }
                    string mode = _pickKind;
                    Done(_ctl.ForEach(ids, () => _ctl.AttackWith(hit.Id, mode)), mode == AttackModes.Normal ? "attack" : mode);
                    return;
                case "point":
                    if (_pickKind == "lureBack") { string t = _lureTarget; Done(_ctl.ForEach(ids, () => _ctl.Lure(t, point)), "lure"); return; }
                    if (_pickKind == "rally") { Done(_ctl.Rally(ids, point), "rally"); return; }
                    if (_pickKind == "guard") { Done(_ctl.ForEach(ids, () => _ctl.Guard(point)), "guard"); return; }
                    string mm = _pickKind;
                    Done(_ctl.ForEach(ids, () => _ctl.MoveTo(point, mm)), mm == MoveModes.Normal ? "move" : mm);
                    return;
                case "ally":
                    if (hit != null && hit.Team == _ctl.Team) FinishAlly(hit);
                    else Notice(T("ui.battle.pickAlly"));
                    return;
                case "path":
                    if (enemy) { FinishPath(hit.Id); return; }
                    if (_path.Count >= _sim.Context.Combat.OrderQueueMax) { Notice(T("ui.battle.pathFull")); return; }
                    _path.Add(point);
                    Notice(T("ui.battle.pathPoint", _path.Count));
                    _dirty = true;
                    return;
            }
        }

        private void FinishAlly(CorpsState ally)
        {
            var ids = Chosen();
            ids.Remove(ally.Id);
            Done(_ctl.ForEach(ids, () => _ctl.Escort(ally.Id)), "escort");
        }

        private void FinishPath(string endTarget)
        {
            if (_path.Count == 0 && endTarget == null) { CancelPick(); _dirty = true; return; }
            var pts = new List<Vec>(_path);
            if (pts.Count == 0 && endTarget != null) { var t = _sim.Context.FindCorps(endTarget); if (t != null) pts.Add(t.Anchor); }
            string mode = _pickKind ?? MoveModes.Normal;
            int n = 0;
            _ctl.ForEach(Chosen().GetRange(0, 1), () => (n = _ctl.Path(pts, mode, endTarget)) > 0);
            Done(n > 0 ? 1 : 0, "path");
        }

        /// <summary>명령 줄 (19 시안): 고른 군단에 맞는 것만, 하위는 그 자리에서 바뀜 ([‹] 뒤로), 대상 고르는 중엔 안내 + [취소], 오른쪽 끝 위임 · 예약.</summary>
        private void RefreshCommandBar()
        {
            if (_cmdBar == null) return;
            var ids = IsReplay ? new List<string>() : Chosen();
            var corps = new List<CorpsState>();
            foreach (var id in ids) corps.Add(_sim.Context.FindCorps(id));
            var key = new StringBuilder(_cmdMenu).Append('|').Append(_pickMode).Append('|').Append(_path.Count).Append('|').Append(_ctl.Queueing).Append('|').Append(_planning);
            foreach (var c in corps) key.Append('|').Append(c.Id).Append(c.Delegated);
            if (_cmdMenu == "skill") foreach (var s in _ctl.Skills()) key.Append('|').Append(s.SkillId).Append(s.Affordable);
            if (key.ToString() == _cmdBarKey) return;
            _cmdBarKey = key.ToString();
            Ui.Clear(_cmdBar);
            _cmdBar.gameObject.SetActive(ids.Count > 0);
            if (ids.Count == 0) return;
            bool single = ids.Count == 1;
            bool delegated = corps.TrueForAll(c => c.Delegated);

            if (_pickMode != null)
            {
                string hint = _pickMode == "enemy" ? "ui.battle.pickTarget" : _pickMode == "ally" ? "ui.battle.pickAlly" : _pickMode == "path" ? "ui.battle.drawPath" : _pickKind == "lureBack" ? "ui.battle.pickBack" : "ui.battle.pickPoint";
                Ui.Size(Ui.Label(_cmdBar, (single ? "" : T("ui.battle.together", ids.Count) + " · ") + T(hint) + (_pickMode == "path" ? "  " + _path.Count : ""), 26, TextAnchor.MiddleLeft, Palette.PromptText), flexibleWidth: 1);
                if (_pickMode == "path") Ui.Size(Ui.Button(_cmdBar, T("ui.battle.thisPath"), () => FinishPath(null), 80, Palette.ButtonActive, 24), width: 170);
                Ui.Size(Ui.Button(_cmdBar, T("ui.aim.cancel"), () => { CancelPick(); Notice(T("ui.cmd.cancelled")); _dirty = true; }, 80, fontSize: 24), width: 130);
                return;
            }

            var row = Ui.HScrollRow(_cmdBar, "Commands", 92, 6, 4);
            Ui.Size(row.parent.parent, 92, -1, -1, 1);
            void Btn(string label, System.Action act, bool enabled = true, bool sub = false)
            {
                var b = Ui.Button(row, label + (sub ? " ›" : ""), () => { act(); _dirty = true; }, 84, fontSize: 24);
                Ui.Size(b, 84, 124);
                Ui.SetEnabled(b, enabled && !delegated);
            }
            void Menu(string m) { _cmdMenu = m; _dirty = true; }
            if (_cmdMenu != "root") Btn("‹", () => Menu("root"));
            bool allRanged = corps.TrueForAll(c => c.Units.Exists(u => u.Alive && u.Troop.HasTag("ranged")));
            switch (_cmdMenu)
            {
                case "attack":
                    Btn(T(single ? "ui.battle.cmd.attack" : "ui.battle.cmd.volley"), () => BeginPick("enemy", AttackModes.Normal, "ui.battle.pickTarget"));
                    Btn(T("ui.battle.cmd.charge"), () => BeginPick("enemy", AttackModes.Charge, "ui.battle.pickTarget"));
                    Btn(T("ui.battle.cmd.standoff"), () => BeginPick("enemy", AttackModes.Standoff, "ui.battle.pickTarget"), allRanged);
                    Btn(T("ui.battle.cmd.lure"), () => BeginPick("enemy", AttackModes.Lure, "ui.battle.pickTarget"), single);
                    break;
                case "move":
                    Btn(T("ui.battle.cmd.move"), () => BeginPick("point", MoveModes.Normal, "ui.battle.pickPoint"));
                    Btn(T("ui.battle.cmd.forced"), () => BeginPick("point", MoveModes.Forced, "ui.battle.pickPoint"));
                    Btn(T("ui.battle.cmd.stealth"), () => BeginPick("point", MoveModes.Stealth, "ui.battle.pickPoint"));
                    Btn(T("ui.battle.cmd.rally"), () => BeginPick("point", "rally", "ui.battle.pickPoint"), !single);
                    Btn(T("ui.battle.cmd.path"), () => BeginPick("path", MoveModes.Normal, "ui.battle.drawPath"), single);
                    Btn(T("ui.battle.cmd.pathStealth"), () => BeginPick("path", MoveModes.Stealth, "ui.battle.drawPath"), single);
                    break;
                case "skill":
                    foreach (var sk in _ctl.Skills())
                    {
                        string id = sk.SkillId;
                        Btn(T(sk.NameKey) + " " + sk.Cost.FloorToLong(), () => PressSkill(id), sk.Affordable);
                    }
                    break;
                case "formation":
                    foreach (var f in _ctl.Formations())
                    {
                        string id = f;
                        bool now = corps.TrueForAll(c => c.Formation.Id == id);
                        Btn(_sim.Context.Defs.Formations.TryGetValue(id, out var fd) ? T(fd.NameKey) : id, () => Done(_ctl.ForEach(ids, () => _ctl.ChangeFormation(id)), "formation"), !now);
                    }
                    break;
                case "wait":
                    Btn(T("ui.battle.cmd.hold"), () => Done(_ctl.ForEach(ids, () => _ctl.Hold()), "hold"));
                    Btn(T("ui.battle.cmd.rest"), () => Done(_ctl.ForEach(ids, () => _ctl.Rest()), "rest"));
                    Btn(T("ui.battle.cmd.coordinate"), () => Done(_ctl.ForEach(ids, () => _ctl.Coordinate()), "coordinate"));
                    Btn(T("ui.battle.cmd.guard"), () => BeginPick("point", "guard", "ui.battle.pickPoint"));
                    Btn(T("ui.battle.cmd.escort"), () => BeginPick("ally", "escort", "ui.battle.pickAlly"), _ctl.OwnCorps().Count > ids.Count);
                    break;
                case "retreat":
                    Btn(T("ui.battle.cmd.fallBack"), () => Done(_ctl.ForEach(ids, () => _ctl.FallBack()), "fallBack"));
                    Btn(T("ui.battle.cmd.withdraw"), () => Ui.Confirm(Root, T("ui.battle.cmd.withdraw"), T("ui.battle.withdrawConfirm"), T("ui.battle.cmd.withdraw"),
                        () => Done(_ctl.ForEach(ids, () => _ctl.Retreat()), "withdraw"), (T("ui.common.cancel"), () => { })));
                    break;
                default:
                    Btn(T("ui.battle.cmd.attack"), () => Menu("attack"), true, true);
                    Btn(T("ui.battle.cmd.move"), () => Menu("move"), true, true);
                    if (single && _ctl.Skills().Count > 0) Btn(T("ui.battle.cmd.skill"), () => Menu("skill"), true, true);
                    if (_ctl.Formations().Count > 1) Btn(T("ui.battle.cmd.formation"), () => Menu("formation"), true, true);
                    Btn(T("ui.battle.cmd.wait"), () => Menu("wait"), true, true);
                    Btn(T("ui.battle.cmd.retreat"), () => Menu("retreat"), true, true);
                    break;
            }
            // 오른쪽 끝 고정: 위임 · 예약
            var toggles = Ui.Rect(_cmdBar, "Toggles");
            Ui.Size(toggles, 92, 136);
            Ui.Column(toggles, 4, 2);
            var dlg = Ui.Button(toggles, T("ui.battle.delegateShort"), () =>
            {
                bool on = !delegated;
                _ctl.ForEach(ids, () => _ctl.SetDelegate(on));
                _dirty = true;
            }, 42, delegated ? Palette.ButtonActive : (Color?)null, 20);
            var q = Ui.Button(toggles, T("ui.battle.queueShort", _ctl.QueueCount), () => { _ctl.SetQueueing(!_ctl.Queueing); _dirty = true; }, 42, _ctl.Queueing ? Palette.ButtonActive : (Color?)null, 20);
            Ui.SetEnabled(q, !delegated);
        }

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
