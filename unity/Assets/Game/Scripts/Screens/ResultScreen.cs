using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>전투 결과 (story.md 5장). 보여줄 내용은 규칙 쪽 <see cref="BattleReport"/>가 이미 계산했다.</summary>
    public sealed class ResultScreen : GameScreen
    {
        private readonly SortiePlan _plan;
        private readonly BattleResult _result;
        private readonly BattleReport _report;
        private readonly string _onlineOutcome;

        /// <param name="onlineOutcome">서버 확인 결과 (SyncOutcomes, 오프라인 모드면 null).</param>
        public ResultScreen(SortiePlan plan, BattleResult result, BattleReport report, string onlineOutcome = null)
        {
            _plan = plan;
            _result = result;
            _report = report;
            _onlineOutcome = onlineOutcome;
        }

        public override void Build()
        {
            Ui.Column(Root, 16, 32);
            Ui.Size(Ui.Label(Root, T(_plan.Step.NameKey), Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim), 70);
            string head = _report.Won ? Ui.Colored(T("ui.result.win"), Palette.Good) : Ui.Colored(T("ui.result.lose"), Palette.Bad);
            if (_report.FirstClear) head += "  " + T("ui.result.firstClear");
            Ui.Size(Ui.Label(Root, head, Ui.FontTitle, TextAnchor.MiddleCenter), 130);
            string reason = T("ui.result.endReason." + _result.EndReason);
            Ui.Size(Ui.Label(Root, T("ui.result.time", _result.ElapsedSeconds.FloorToLong(), reason), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim), 56);

            if (_onlineOutcome != null)
                Ui.Size(Ui.Label(Root, T("ui.online." + _onlineOutcome), Ui.FontSmall, TextAnchor.MiddleCenter,
                    _onlineOutcome == Game.Flow.SyncOutcomes.Rejected ? Palette.Bad : Palette.TextDim), 48);

            var body = Ui.ScrollList(Root, "Report", 12, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);

            var units = Ui.Card(body, "Units");
            Ui.Label(units, T("ui.result.units"));
            foreach (var u in _report.Units)
            {
                var unit = Ctx.State.FindUnit(u.UnitId);
                string troop = unit != null && Ctx.Defs.TroopTypes.TryGetValue(unit.TroopType, out var tt) ? T(tt.NameKey) : "";
                Ui.Label(units, T("ui.result.unitLine", u.UnitId, troop, u.Losses.ToString("N0"), u.Wounded.ToString("N0"), u.Dead.ToString("N0")), 28);
            }
            Ui.Label(units, T("ui.result.supply", _report.SupplyReturned.ToString("N0"), _report.SupplyCaptured.ToString("N0")), 28);

            var injuries = new List<string>();
            foreach (var kv in _report.Injuries) injuries.Add(Ctx.GeneralName(kv.Key) + " (" + T("ui.generals." + kv.Value) + ")");
            Section(body, "ui.result.injuries", injuries);
            Section(body, "ui.result.capturedOwn", Names(_report.CapturedOwn));
            Section(body, "ui.result.newPrisoners", Names(_report.NewPrisoners));
            Section(body, "ui.result.rescued", Names(_report.Rescued));

            var formations = new List<string>();
            foreach (var f in _report.UnlockedFormations) formations.Add(Ctx.Defs.Formations.TryGetValue(f, out var fd) ? T(fd.NameKey) : f);
            Section(body, "ui.result.formations", formations);
            var skills = new List<string>();
            foreach (var s in _report.UnlockedSkills) skills.Add(Ctx.Defs.Skills.TryGetValue(s, out var sd) ? T(sd.NameKey) : s);
            Section(body, "ui.result.skills", skills);
            var resources = new List<string>();
            foreach (var kv in _report.RewardResources) resources.Add(T("resource." + kv.Key) + " " + kv.Value.ToString("N0"));
            Section(body, "ui.result.resources", resources);
            var levels = new List<string>();
            foreach (var kv in _report.LevelUps) levels.Add(T("ui.result.levelLine", Ctx.GeneralName(kv.Key), kv.Value.Level, kv.Value.LevelsGained));
            Section(body, "ui.result.levelUps", levels);
            var secrets = new List<string>();
            foreach (var s in _report.Secrets) secrets.Add(Ctx.GeneralName(s.GeneralId) + ": " + s.SecretId);
            Section(body, "ui.result.secrets", secrets);

            bool territory = _plan.Step.IsTerritory; // 영토 전투면 영토 탭으로
            // 이긴 스토리 스텝에 뒤 대사가 있으면 그것부터 (story.md 1.3)
            bool after = !territory && _report.Won && AfterDialogueScreen.Has(Ctx, _plan.Step.Id);
            Ui.Button(Root, T(territory ? "ui.result.toTerritory" : after ? "ui.result.toAfter" : "ui.result.toStory"),
                () => { if (after) App.Show(new AfterDialogueScreen(_plan.Step.Id)); else App.ShowMain(territory ? "territory" : "story"); }, 140);
        }

        private List<string> Names(List<string> generalIds)
        {
            var list = new List<string>();
            foreach (var id in generalIds) list.Add(Ctx.GeneralName(id));
            return list;
        }

        /// <summary>내용이 있을 때만 카드 하나.</summary>
        private void Section(RectTransform parent, string headerKey, List<string> lines)
        {
            if (lines.Count == 0) return;
            var card = Ui.Card(parent, headerKey);
            Ui.Label(card, T(headerKey));
            Ui.Label(card, string.Join("\n", lines), 28, TextAnchor.UpperLeft, Palette.TextDim);
        }
    }
}
