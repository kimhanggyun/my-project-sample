using System.Collections.Generic;
using System.Text;
using Game.Battle.Model.Contests;
using Game.Flow;
using Game.Foundation.Math;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 전투 직전 대결 (일기토·논쟁, battle.md 2.7): 라운드마다 행동을 고른다 (상대 선택은 공개 전까지 안 보임).
    /// 시간 안에 못 고르면 무작위(시드)로 고른다. 결과 계산은 규칙 쪽(<see cref="ContestPlay"/>)이 하고, 고른 기록이 전투에 그대로 들어간다.
    /// </summary>
    public sealed class ContestScreen : GameScreen
    {
        /// <summary>한 라운드에 고를 시간 (설계 2.7.1의 3·2·1 카운트보다 조금 넉넉하게, 화면 값).</summary>
        private const float RoundSeconds = 5f;
        private const int ShownRounds = 4;

        private readonly SortiePlan _plan;
        private readonly int _index;
        private ContestPlay _play;
        private float _left;

        private Text _timer;
        private Text _mine, _theirs, _log, _resultText;
        private RectTransform _mineBar, _theirsBar, _mineCharge, _theirsCharge;
        private RectTransform _actions;
        private Button _autoButton, _nextButton;

        public ContestScreen(SortiePlan plan, int index) { _plan = plan; _index = index; }

        public override void Build()
        {
            _play = new ContestPlay(_plan.Setup, _index);
            var def = _play.Def;
            Ui.Column(Root, 14, 32);
            Ui.Size(Ui.Label(Root, T("ui.contest." + def.Id), Ui.FontTitle, TextAnchor.MiddleCenter), 110);
            _timer = Ui.Label(Root, "", Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Size(_timer, 60);

            (_mine, _mineBar, _mineCharge) = Fighter(Ctx.GeneralName(_play.Setup.CorpsA == null ? null : GeneralOf(_play.Setup.CorpsA)), Palette.Ally);
            (_theirs, _theirsBar, _theirsCharge) = Fighter(Ctx.GeneralName(GeneralOf(_play.Setup.CorpsB)), Palette.Enemy);

            var logCard = Ui.Card(Root, "Log", 20, 6);
            Ui.Size(logCard, flexibleHeight: 1);
            _log = Ui.Label(logCard, "", 30, TextAnchor.UpperLeft);
            _resultText = Ui.Label(Root, "", Ui.FontBody, TextAnchor.MiddleCenter);
            Ui.Size(_resultText, 70);

            _actions = Ui.Rect(Root, "Actions");
            var grid = _actions.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(320, 110);
            grid.spacing = new Vector2(12, 12);
            grid.childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(_actions, 250);

            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            _autoButton = Ui.Button(row, T("ui.contest.auto"), () => { _play.AutoFinish(); Refresh(); });
            _nextButton = Ui.Button(row, T("ui.contest.next"), Next, color: Palette.ButtonActive);
            Refresh();
        }

        public override void Tick(float deltaTime)
        {
            if (_play == null || _play.IsOver) return;
            _left -= deltaTime;
            if (_left <= 0f)
            {
                _play.ChooseRandom(_plan.Setup.Seed);
                Refresh();
                return;
            }
            _timer.text = T("ui.contest.timer", Mathf.CeilToInt(_left));
        }

        private void Pick(string action)
        {
            if (_play.Choose(action)) Refresh();
        }

        private void Next()
        {
            if (!_play.IsOver) return;
            if (_index + 1 < _plan.Setup.Contests.Count) App.Show(new ContestScreen(_plan, _index + 1));
            else App.Show(new BattleScreen(_plan));
        }

        private void Refresh()
        {
            _left = RoundSeconds;
            var def = _play.Def;
            var rounds = _play.Rounds();
            Fix resA = rounds.Count > 0 ? rounds[rounds.Count - 1].ResourceA : def.Start;
            Fix resB = rounds.Count > 0 ? rounds[rounds.Count - 1].ResourceB : def.Start;
            Fix chA = rounds.Count > 0 ? rounds[rounds.Count - 1].ChargeA : Fix.Zero;
            Fix chB = rounds.Count > 0 ? rounds[rounds.Count - 1].ChargeB : Fix.Zero;
            SetFighter(_mine, _mineBar, _mineCharge, resA, chA);
            SetFighter(_theirs, _theirsBar, _theirsCharge, resB, chB);

            var sb = new StringBuilder();
            for (int i = System.Math.Max(0, rounds.Count - ShownRounds); i < rounds.Count; i++)
            {
                var r = rounds[i];
                sb.AppendLine(T("ui.contest.roundLine", r.Number, ActionName(r.ActionA), ActionName(r.ActionB)));
            }
            if (rounds.Count == 0) sb.AppendLine(T("ui.contest.start"));
            _log.text = sb.ToString();

            Ui.Clear(_actions);
            foreach (var id in _play.Available())
            {
                string a = id;
                Ui.Button(_actions, ActionName(a), () => Pick(a), 110, def.Action(a).RequiresFullCharge ? Palette.ButtonActive : (Color?)null, 34);
            }

            bool over = _play.IsOver;
            _timer.gameObject.SetActive(!over);
            Ui.SetEnabled(_autoButton, !over);
            Ui.SetEnabled(_nextButton, over);
            _resultText.text = over ? ResultText(_play.Result) : "";
        }

        private string ResultText(ContestResult r)
        {
            if (r.End == ContestEnds.Retreat) return Ui.Colored(T(r.Retreater == "A" ? "ui.contest.iRetreated" : "ui.contest.theyRetreated"), Palette.TextDim);
            if (r.Winner == null) return T("ui.contest.draw");
            bool won = r.Winner == "A";
            string key = r.End == ContestEnds.Fall ? (won ? "ui.contest.wonFall" : "ui.contest.lostFall") : (won ? "ui.contest.won" : "ui.contest.lost");
            return Ui.Colored(T(key), won ? Palette.Good : Palette.Bad);
        }

        private string ActionName(string id) => T(_play.Def.Action(id).NameKey);

        private string GeneralOf(string corpsId)
        {
            foreach (var c in _plan.Setup.Corps) if (c.Id == corpsId) return c.General?.Id;
            return null;
        }

        private (Text label, RectTransform bar, RectTransform charge) Fighter(string name, Color color)
        {
            var card = Ui.Card(Root, "Fighter", 16, 6);
            var label = Ui.Label(card, Ui.Colored(name, color), 32);
            var bar = Bar(card, color, 28);
            var charge = Bar(card, new Color(0.95f, 0.8f, 0.3f), 14);
            return (label, bar, charge);
        }

        private static RectTransform Bar(Transform parent, Color color, float height)
        {
            var back = Ui.Rect(parent, "Bar");
            Ui.Background(back, new Color(0f, 0f, 0f, 0.5f));
            Ui.Size(back, height);
            var fill = Ui.Rect(back, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Ui.Background(fill, color);
            return fill;
        }

        private void SetFighter(Text label, RectTransform bar, RectTransform charge, Fix resource, Fix ch)
        {
            var def = _play.Def;
            float r = Mathf.Clamp01((float)(resource.ToDisplayDouble() / Mathf.Max(1f, (float)def.Start.ToDisplayDouble())));
            float c = Mathf.Clamp01((float)(ch.ToDisplayDouble() / Mathf.Max(1f, (float)def.SpecialCharge.ToDisplayDouble())));
            bar.anchorMax = new Vector2(r, 1f);
            charge.anchorMax = new Vector2(c, 1f);
            string name = label.text.Split('\n')[0];
            label.text = name + "\n" + Ui.Colored(T(def.ResourceKey) + " " + resource.FloorToLong() + "   " + T(def.ChargeKey) + " " + ch.FloorToLong() + "%", Palette.TextDim);
        }
    }
}
