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
    /// 전투 직전 대결 (일기토·논쟁, battle.md 2.7). 라운드마다:
    /// ① 3·2·1 카운트 동안 행동을 고른다 (바꿀 수 있음, 상대 선택은 안 보임). 못 고르면 무작위(시드).
    /// ② 0이 되면 두 선택을 동시에 공개하고, 그 라운드에 일어난 일(피해·회심·기운)을 보여준다.
    /// 끝나면 결과와 전투에 주는 영향(2.7.5·2.7.6)을 보여준다. 계산은 규칙 쪽(<see cref="ContestPlay"/>), 고른 기록이 전투에 그대로 들어간다.
    /// </summary>
    public sealed class ContestScreen : GameScreen
    {
        /// <summary>고르는 시간 (설계 2.7.1: 3·2·1 카운트).</summary>
        private const float ChooseSeconds = 3f;
        /// <summary>공개 결과를 보여주는 시간.</summary>
        private const float RevealSeconds = 2.2f;
        private const int ShownRounds = 3;

        private enum Phase { Choosing, Reveal, Over }

        private readonly SortiePlan _plan;
        private readonly int _index;
        private ContestPlay _play;
        private Phase _phase;
        private float _left;
        private string _pending;

        private Text _round, _big, _detail, _log, _resultText;
        private Text _mine, _theirs;
        private RectTransform _mineBar, _theirsBar, _mineCharge, _theirsCharge;
        private RectTransform _actions;
        private readonly Dictionary<string, Button> _actionButtons = new Dictionary<string, Button>();
        private Button _autoButton, _nextButton;

        public ContestScreen(SortiePlan plan, int index) { _plan = plan; _index = index; }

        public override void Build()
        {
            _play = new ContestPlay(_plan.Setup, _index);
            var def = _play.Def;
            Ui.Column(Root, 12, 28);
            Ui.Size(Ui.Label(Root, T("ui.contest." + def.Id), Ui.FontTitle, TextAnchor.MiddleCenter), 96);

            // 두 장수 (나 왼쪽 · 상대 오른쪽)
            var fighters = Ui.Row(Root, "Fighters", 210, 12);
            (_mine, _mineBar, _mineCharge) = Fighter(fighters, Ctx.GeneralName(GeneralOf(_play.Setup.CorpsA)), Palette.Ally);
            (_theirs, _theirsBar, _theirsCharge) = Fighter(fighters, Ctx.GeneralName(GeneralOf(_play.Setup.CorpsB)), Palette.Enemy);

            // 가운데 무대: 카운트 → 공개 → 일어난 일
            var stage = Ui.Card(Root, "Stage", 20, 6);
            Ui.Size(stage, flexibleHeight: 1);
            _round = Ui.Label(stage, "", Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Size(_round, 50);
            _big = Ui.Label(stage, "", 96, TextAnchor.MiddleCenter);
            Ui.Size(_big, 150);
            _detail = Ui.Label(stage, "", 34, TextAnchor.UpperCenter);
            Ui.Size(_detail, flexibleHeight: 1);
            _resultText = Ui.Label(stage, "", 30, TextAnchor.MiddleCenter);
            Ui.Size(_resultText, 120);

            _actions = Ui.Rect(Root, "Actions");
            var grid = _actions.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(320, 104);
            grid.spacing = new Vector2(12, 12);
            grid.childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(_actions, 230);

            _log = Ui.Label(Root, "", 26, TextAnchor.UpperLeft, Palette.TextDim);
            Ui.Size(_log, 110);

            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            _autoButton = Ui.Button(row, T("ui.contest.auto"), () => { _play.AutoFinish(); ShowOver(); });
            _nextButton = Ui.Button(row, T("ui.contest.next"), Next, color: Palette.ButtonActive);
            BeginChoosing();
        }

        public override void Tick(float deltaTime)
        {
            if (_play == null || _phase == Phase.Over) return;
            _left -= deltaTime;
            if (_phase == Phase.Choosing)
            {
                if (_left <= 0f) { Reveal(); return; }
                _big.text = Mathf.CeilToInt(_left).ToString();
            }
            else if (_left <= 0f)
            {
                if (_play.IsOver) ShowOver(); else BeginChoosing();
            }
        }

        private void BeginChoosing()
        {
            _phase = Phase.Choosing;
            _left = ChooseSeconds;
            _pending = null;
            int number = _play.Rounds().Count + 1;
            _round.text = T("ui.contest.roundOf", number, _play.Def.MaxRounds);
            _big.text = Mathf.CeilToInt(_left).ToString();
            _big.color = Palette.Text;
            _detail.text = T(number == 1 ? "ui.contest.start" : "ui.contest.pick");
            _resultText.text = "";
            BuildActions();
            RefreshBars();
            RefreshLog();
            Ui.SetEnabled(_autoButton, true);
            Ui.SetEnabled(_nextButton, false);
        }

        private void BuildActions()
        {
            Ui.Clear(_actions);
            _actionButtons.Clear();
            var def = _play.Def;
            foreach (var id in _play.Available())
            {
                string a = id;
                var b = Ui.Button(_actions, ActionName(a), () => Pick(a), 104, def.Action(a).RequiresFullCharge ? Palette.ButtonActive : (Color?)null, 34);
                _actionButtons[a] = b;
            }
        }

        /// <summary>고르기 (카운트가 끝나기 전까지 바꿀 수 있다).</summary>
        private void Pick(string action)
        {
            if (_phase != Phase.Choosing) return;
            _pending = action;
            foreach (var kv in _actionButtons)
            {
                bool special = _play.Def.Action(kv.Key).RequiresFullCharge;
                kv.Value.targetGraphic.color = kv.Key == action ? Palette.Good : special ? Palette.ButtonActive : Palette.Button;
            }
            _detail.text = T("ui.contest.chosen", ActionName(action));
        }

        /// <summary>동시 공개: 고른 것(없으면 무작위)으로 라운드를 돌리고, 일어난 일을 보여준다.</summary>
        private void Reveal()
        {
            var before = _play.Rounds();
            if (_pending == null || !_play.Choose(_pending)) _play.ChooseRandom(_plan.Setup.Seed);
            var rounds = _play.Rounds();
            _phase = Phase.Reveal;
            _left = RevealSeconds;
            foreach (var b in _actionButtons.Values) Ui.SetEnabled(b, false);
            if (rounds.Count == before.Count) { _left = 0f; return; }
            var r = rounds[rounds.Count - 1];
            _big.text = Ui.Colored(ActionName(r.ActionA), Palette.Ally) + "  <size=56>vs</size>  " + Ui.Colored(ActionName(r.ActionB), Palette.Enemy);
            _detail.text = (_pending == null ? Ui.Colored(T("ui.contest.timeout"), Palette.TextDim) + "\n" : "") + Happened(before.Count > 0 ? before[before.Count - 1] : null, r);
            RefreshBars();
            RefreshLog();
        }

        private void ShowOver()
        {
            _phase = Phase.Over;
            Ui.Clear(_actions);
            _actionButtons.Clear();
            RefreshBars();
            RefreshLog();
            var r = _play.Result;
            _round.text = "";
            _big.text = ResultText(r);
            _resultText.text = Ui.Colored(EffectText(r), Palette.TextDim);
            Ui.SetEnabled(_autoButton, false);
            Ui.SetEnabled(_nextButton, true);
        }

        private void Next()
        {
            if (!_play.IsOver) return;
            if (_index + 1 < _plan.Setup.Contests.Count) App.Show(new ContestScreen(_plan, _index + 1));
            else App.Show(new BattleScreen(_plan));
        }

        /// <summary>그 라운드에 일어난 일: 체력(평정심) 감소·회심·기운(통찰) 변화를 양쪽 다.</summary>
        private string Happened(ContestRound prev, ContestRound r)
        {
            var def = _play.Def;
            Fix resA0 = prev?.ResourceA ?? def.Start, resB0 = prev?.ResourceB ?? def.Start;
            Fix chA0 = prev?.ChargeA ?? Fix.Zero, chB0 = prev?.ChargeB ?? Fix.Zero;
            var sb = new StringBuilder();
            void Side(string who, Color color, Fix res0, Fix res1, Fix ch0, Fix ch1, bool crit)
            {
                long dmg = (res0 - res1).FloorToLong();
                if (dmg > 0) sb.AppendLine(Ui.Colored(T("ui.contest.lostRes", who, T(def.ResourceKey), dmg), color) + (crit ? "  " + Ui.Colored(T("ui.contest.crit"), Palette.Bad) : ""));
                long dc = (ch1 - ch0).FloorToLong();
                if (dc > 0) sb.AppendLine(T("ui.contest.gainCharge", who, T(def.ChargeKey), dc));
                else if (dc < 0) sb.AppendLine(T("ui.contest.spentCharge", who, T(def.ChargeKey)));
            }
            Side(T("ui.contest.me"), Palette.Bad, resA0, r.ResourceA, chA0, r.ChargeA, r.CritOnA);
            Side(T("ui.contest.them"), Palette.Good, resB0, r.ResourceB, chB0, r.ChargeB, r.CritOnB);
            if (sb.Length == 0) sb.AppendLine(T("ui.contest.nothing"));
            return sb.ToString();
        }

        private void RefreshBars()
        {
            var def = _play.Def;
            var rounds = _play.Rounds();
            var last = rounds.Count > 0 ? rounds[rounds.Count - 1] : null;
            SetFighter(_mine, _mineBar, _mineCharge, last?.ResourceA ?? def.Start, last?.ChargeA ?? Fix.Zero);
            SetFighter(_theirs, _theirsBar, _theirsCharge, last?.ResourceB ?? def.Start, last?.ChargeB ?? Fix.Zero);
        }

        private void RefreshLog()
        {
            var rounds = _play.Rounds();
            var sb = new StringBuilder();
            for (int i = System.Math.Max(0, rounds.Count - ShownRounds); i < rounds.Count; i++)
            {
                var r = rounds[i];
                sb.AppendLine(T("ui.contest.roundLine", r.Number, ActionName(r.ActionA), ActionName(r.ActionB)));
            }
            _log.text = sb.ToString();
        }

        private string ResultText(ContestResult r)
        {
            if (r.End == ContestEnds.Retreat) return Ui.Colored(T(r.Retreater == "A" ? "ui.contest.iRetreated" : "ui.contest.theyRetreated"), Palette.TextDim);
            if (r.Winner == null) return T("ui.contest.draw");
            bool won = r.Winner == "A";
            string key = r.End == ContestEnds.Fall ? (won ? "ui.contest.wonFall" : "ui.contest.lostFall") : (won ? "ui.contest.won" : "ui.contest.lost");
            return Ui.Colored(T(key), won ? Palette.Good : Palette.Bad);
        }

        /// <summary>전투에 주는 영향 (battle.md 2.7.5 일기토, 2.7.6 논쟁).</summary>
        private string EffectText(ContestResult r)
        {
            string id = _play.Def.Id;
            string outcome;
            if (r.End == ContestEnds.Retreat) outcome = r.Retreater == "A" ? "iRetreated" : "theyRetreated";
            else if (r.Winner == null) outcome = "draw";
            else outcome = (r.Winner == "A" ? "won" : "lost") + (r.End == ContestEnds.Fall ? "Fall" : "");
            string key = "ui.contest.effect." + id + "." + outcome;
            return Ctx.Defs.Strings.ContainsKey(key) ? T(key) : "";
        }

        private string ActionName(string id) => T(_play.Def.Action(id).NameKey);

        private string GeneralOf(string corpsId)
        {
            foreach (var c in _plan.Setup.Corps) if (c.Id == corpsId) return c.General?.Id;
            return null;
        }

        private (Text label, RectTransform bar, RectTransform charge) Fighter(Transform parent, string name, Color color)
        {
            var card = Ui.Card(parent, "Fighter", 16, 6);
            Ui.Size(card, flexibleWidth: 1);
            var label = Ui.Label(card, Ui.Colored(name, color), 30);
            var bar = Bar(card, color, 26);
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
