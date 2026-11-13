using System.Collections.Generic;
using System.Text;
using Game.Battle.Model.Contests;
using Game.Campaign.Roster;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 장수 키우기(대결) 탭 (general-growth.md 2.4, R-59): 장수를 고르고, 일기토·논쟁 전적과 다음 성장 보상까지를 보고,
    /// AI 상대를 골라 연습 대결. 결과는 전투 대결과 같은 전적·성장 보상 + 경험치.
    /// </summary>
    public sealed class PracticeTabView : ITabView
    {
        private static string _selected;

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var svc = new PracticeService(ctx.Defs);
            var list = Ui.ScrollList(root, "Practice", 10, 16);
            Ui.Fill((RectTransform)list.parent.parent);
            Ui.Label(list, ctx.T("ui.practice.note"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            int limit = ctx.Defs.Practice.DailyLimit;
            long now = ctx.Clock.UtcNow.ToUnixTimeSeconds();
            Ui.Label(list, limit > 0 ? ctx.T("ui.practice.today", PracticeService.TodayCount(ctx.State, now), limit) : ctx.T("ui.practice.noLimit"), Ui.FontSmall);
            if (!ctx.Defs.Practice.Enabled) { Ui.Label(list, ctx.T("ui.tab.comingSoon"), Ui.FontSmall); return; }

            // 장수 고르기
            if (_selected == null || ctx.State.FindGeneral(_selected) == null) _selected = ctx.State.Generals.Count > 0 ? ctx.State.Generals[0].GeneralId : null;
            var pick = Ui.Card(list, "Pick", 16, 6);
            Ui.Label(pick, ctx.T("ui.practice.pickGeneral"));
            foreach (var gs in ctx.State.Generals)
            {
                string id = gs.GeneralId;
                var b = Ui.Button(pick, ctx.T("ui.generals.line", ctx.GeneralName(id), gs.Level, ctx.T("ui.generals." + gs.Status)), () => { _selected = id; app.ShowMain("practice"); }, 80,
                    id == _selected ? Palette.ButtonActive : (Color?)null, 28);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            }
            var g = _selected == null ? null : ctx.State.FindGeneral(_selected);
            if (g == null) return;

            // 전적과 다음 보상
            var rec = Ui.Card(list, "Records", 16, 6);
            Ui.Label(rec, ctx.T("ui.practice.records", ctx.GeneralName(g.GeneralId)));
            foreach (var cid in new[] { "duel", "debate" })
            {
                if (!ctx.Defs.Contests.ContainsKey(cid)) continue;
                var r = PracticeService.RecordOf(g, cid);
                Ui.Label(rec, ctx.T("ui.practice.recordLine", ctx.T("ui.contest." + cid), r.wins, r.losses, r.retreats, r.draws, r.streak), Ui.FontSmall);
            }
            foreach (var p in svc.Progress(g))
            {
                string reward = p.rule.RewardStat != null
                    ? (ctx.Defs.Stats.TryGetValue(p.rule.RewardStat, out var sd) ? ctx.T(sd.NameKey) : p.rule.RewardStat) + " +" + p.rule.RewardAdd.FloorToLong()
                    : ctx.T("trait." + p.rule.RewardTrait);
                Ui.Label(rec, ctx.T("ui.practice.progress", ctx.T("ui.practice.counter." + p.rule.Counter), p.value, p.target, reward), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            }

            // 상대 고르기 → 대결
            foreach (var cid in new[] { "duel", "debate" })
            {
                if (!ctx.Defs.Contests.TryGetValue(cid, out var def)) continue;
                var card = Ui.Card(list, "Fight" + cid, 16, 6);
                var mine = CorpsValidator.EffectiveStat(ctx.Defs, g, def.Stat).FloorToLong();
                Ui.Label(card, ctx.T("ui.practice.fightHeader", ctx.T("ui.contest." + cid), ctx.Defs.Stats.TryGetValue(def.Stat, out var st) ? ctx.T(st.NameKey) : def.Stat, mine));
                foreach (var o in ctx.Defs.Practice.Opponents)
                {
                    string oid = o.Id, c = cid;
                    string check = svc.Check(ctx.State, g.GeneralId, cid, oid, now);
                    var b = Ui.Button(card, ctx.T("ui.practice.opponent", ctx.T(o.NameKey), o.Stat.FloorToLong()), () => PracticeScreen.Start(app, g.GeneralId, c, oid), 80, fontSize: 28);
                    Ui.SetEnabled(b, check == PracticeService.Ok);
                }
            }
        }
    }

    /// <summary>
    /// 연습 대결 (R-59): 전투 대결과 같은 화면(ContestScreen)에서 장수가 마주 서서 싸운다.
    /// 끝나면 같은 시드·선택을 행동으로 반영하고 (서버도 같은 규칙) 결과·보상 화면을 보여 준다.
    /// </summary>
    public sealed class PracticeScreen : GameScreen
    {
        private readonly PracticeOutcome _outcome;
        private readonly bool _saved;
        private readonly string _generalId, _contestId;

        private PracticeScreen(string generalId, string contestId, PracticeOutcome outcome, bool saved)
        {
            _generalId = generalId;
            _contestId = contestId;
            _outcome = outcome;
            _saved = saved;
        }

        public static void Start(GameApp app, string generalId, string contestId, string opponentId)
        {
            var ctx = app.Context;
            var svc = new PracticeService(ctx.Defs);
            var match = svc.Start(ctx.State, generalId, contestId, opponentId, ctx.NewSeed(), ctx.Clock.UtcNow.ToUnixTimeSeconds());
            if (match == null) { app.Show(new PracticeScreen(generalId, contestId, null, false)); return; }
            var play = new PracticeContestPlay(ctx.Defs, match);
            app.Show(new ContestScreen(play, () =>
            {
                var action = match.ToAction(ctx.Clock.UtcNow.ToUnixTimeSeconds());
                var copy = SaveJson.Read(SaveJson.Write(ctx.State));
                var outcome = svc.Apply(copy, action); // 화면 표시용 미리보기
                bool saved = outcome != null && ctx.Do(action);
                app.Show(new PracticeScreen(generalId, contestId, outcome, saved));
            }, "ui.practice.finish", false));
        }

        public override void Build()
        {
            Ui.Column(Root, 16, 32);
            Ui.Size(Ui.Label(Root, T("ui.contest." + _contestId) + " · " + Ctx.GeneralName(_generalId), Ui.FontTitle, TextAnchor.MiddleCenter), 120);
            var card = Ui.Card(Root, "Result");
            Ui.Label(card, _outcome == null ? T("ui.practice.cannot") : !_saved ? Ui.Colored(T("ui.practice.notSaved"), Palette.Bad) : ResultText(), Ui.FontBody, TextAnchor.MiddleCenter);
            Ui.Size(Ui.Rect(Root, "Space"), flexibleHeight: 1);
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("practice"), 100, Palette.ButtonActive);
        }

        private string ResultText()
        {
            var sb = new StringBuilder(T("ui.practice.outcome." + _outcome.Outcome));
            sb.Append("  ").Append(T("ui.practice.exp", _outcome.Exp));
            if (_outcome.Level != null && _outcome.Level.LevelsGained > 0) sb.Append("\n").Append(T("ui.practice.levelUp", _outcome.Level.Level));
            foreach (var r in _outcome.Rewards)
                sb.Append("\n").Append(T("ui.practice.reward", r.Stat != null ? (Ctx.Defs.Stats.TryGetValue(r.Stat, out var sd) ? T(sd.NameKey) : r.Stat) + " +" + r.Add.FloorToLong() : T("trait." + r.Trait)));
            return sb.ToString();
        }
    }
}
