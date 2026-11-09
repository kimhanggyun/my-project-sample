using System.Collections.Generic;
using System.Text;
using Game.Battle.Model.Events;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// (임시) 전투 화면: 3D 전장 전까지 글자로 상황을 보여준다. 전투 계산은 <see cref="BattleSimulation"/>이 고정 틱으로 하고,
    /// 이 화면은 실제 시간에 맞춰 틱을 몇 번 돌릴지만 정한다 (화면 속도가 결과를 바꾸지 않는다, R-29).
    /// </summary>
    public sealed class BattleScreen : GameScreen
    {
        /// <summary>한 프레임에 돌릴 최대 틱 (느린 기기에서 멈춘 듯 보이지 않게).</summary>
        private const int MaxTicksPerFrame = 40;
        private const int LogLines = 14;
        private static readonly int[] Speeds = { 1, 2, 4 };

        private readonly SortiePlan _plan;
        private BattleSimulation _sim;
        private SpeechPlanner _speech;
        private float _tickSeconds;
        private float _accumulator;
        private int _speedIndex;
        private bool _paused;
        private int _logRead;
        private bool _finished;

        private Text _clock;
        private Text _logText;
        private Button _pauseButton;
        private Button _speedButton;
        private readonly List<(CorpsState corps, Text text)> _rows = new List<(CorpsState, Text)>();
        private readonly LinkedList<string> _log = new LinkedList<string>();

        public BattleScreen(SortiePlan plan) { _plan = plan; }

        public override void Build()
        {
            _sim = new BattleSimulation(_plan.Setup);
            var ctx = _sim.Context;
            _tickSeconds = (float)ctx.Combat.TickSeconds.ToDisplayDouble();
            _speech = new SpeechPlanner(ctx.Defs, ctx.Flags, ctx.Combat.TickSeconds,
                id => { var c = ctx.FindCorps(id); return c != null && c.Setup.Controller == Controllers.Ai; });

            Ui.Column(Root, 12, 24);
            Ui.Size(Ui.Label(Root, T(_plan.Step.NameKey), Ui.FontBody, TextAnchor.MiddleCenter), 80);
            _clock = Ui.Label(Root, "", Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Size(_clock, 56);

            var corpsCard = Ui.Card(Root, "Corps", 20, 6);
            foreach (var team in new[] { StoryFlow.PlayerTeam, StoryFlow.EnemyTeam })
            {
                bool ally = team == StoryFlow.PlayerTeam;
                Ui.Label(corpsCard, Ui.Colored(T(ally ? "ui.battle.ally" : "ui.battle.enemy"), ally ? Palette.Ally : Palette.Enemy), Ui.FontSmall);
                foreach (var c in ctx.Corps)
                    if (c.Team == team) _rows.Add((c, Ui.Label(corpsCard, "", 28)));
            }

            var logCard = Ui.Card(Root, "Log", 20, 6);
            Ui.Size(logCard, flexibleHeight: 1);
            Ui.Label(logCard, T("ui.battle.log"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            _logText = Ui.Label(logCard, "", 28, TextAnchor.UpperLeft);

            var row = Ui.Row(Root, "Controls", Ui.ButtonHeight);
            _pauseButton = Ui.Button(row, T("ui.battle.pause"), TogglePause);
            _speedButton = Ui.Button(row, "", NextSpeed);
            Ui.Button(row, T("ui.battle.skip"), SkipToEnd);

            ReadNewEvents();
            Refresh();
        }

        public override void Tick(float deltaTime)
        {
            if (_finished || _paused) return;
            _accumulator += deltaTime * Speeds[_speedIndex];
            int steps = 0;
            while (_accumulator >= _tickSeconds && steps < MaxTicksPerFrame && !_sim.IsOver)
            {
                _sim.Step();
                _accumulator -= _tickSeconds;
                steps++;
            }
            if (steps >= MaxTicksPerFrame) _accumulator = 0f; // 밀린 시간은 버린다 (따라잡으려다 더 느려지지 않게)
            if (steps > 0)
            {
                ReadNewEvents();
                Refresh();
            }
            if (_sim.IsOver) Finish();
        }

        private void TogglePause()
        {
            _paused = !_paused;
            Refresh();
        }

        private void NextSpeed()
        {
            _speedIndex = (_speedIndex + 1) % Speeds.Length;
            Refresh();
        }

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

        /// <summary>전투 기록에 새로 쌓인 이벤트 → 말풍선·전황 글.</summary>
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

        private string CorpsName(string corpsId)
        {
            var c = corpsId == null ? null : _sim.Context.FindCorps(corpsId);
            if (c == null) return corpsId ?? "";
            string name = c.Setup.General != null ? Ctx.GeneralName(c.Setup.General.Id) : c.Id;
            return Ui.Colored(name, c.Team == StoryFlow.PlayerTeam ? Palette.Ally : Palette.Enemy);
        }

        private void Refresh()
        {
            var ctx = _sim.Context;
            long elapsed = (ctx.Combat.TickSeconds * ctx.Tick).FloorToLong();
            long max = (_plan.Setup.MaxMinutes * 60).FloorToLong();
            _clock.text = T("ui.battle.time", elapsed, max);

            foreach (var (corps, text) in _rows)
            {
                string status = T("ui.corps." + StatusKey(corps.Status));
                string line = T("ui.battle.corpsLine", CorpsName(corps.Id), corps.Troops.ToString("N0"), corps.StartTroops.ToString("N0"),
                    corps.Morale.FloorToLong(), corps.Supply.ToString("N0"));
                line += "  [" + status + "]";
                if (corps.Delegated) line += " " + T("ui.battle.delegated");
                text.text = corps.InBattle ? line : Ui.Colored(line, Palette.TextDim);
            }

            var sb = new StringBuilder();
            foreach (var l in _log) sb.AppendLine(l);
            _logText.text = sb.ToString();

            _pauseButton.GetComponentInChildren<Text>().text = _paused ? T("ui.battle.resume") : T("ui.battle.pause");
            _speedButton.GetComponentInChildren<Text>().text = T("ui.battle.speed", Speeds[_speedIndex]);
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
