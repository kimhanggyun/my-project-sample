using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

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
            Sfx.Play(_report.Won ? "cheer" : "gong");
            string head = _report.Won ? Ui.Colored(T("ui.result.win"), Palette.Good) : Ui.Colored(T("ui.result.lose"), Palette.Bad);
            if (_report.FirstClear) head += "  " + T("ui.result.firstClear");
            Ui.Size(Ui.Label(Root, head, Ui.FontTitle, TextAnchor.MiddleCenter), 130);
            string reason = WhyEnded();
            Ui.Size(Ui.Label(Root, T("ui.result.time", _result.ElapsedSeconds.FloorToLong(), reason), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim), 56);

            if (_onlineOutcome != null)
                Ui.Size(Ui.Label(Root, T("ui.online." + _onlineOutcome), Ui.FontSmall, TextAnchor.MiddleCenter,
                    _onlineOutcome == Game.Flow.SyncOutcomes.Rejected ? Palette.Bad : Palette.TextDim), 48);

            var body = Ui.ScrollList(Root, "Report", 12, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);

            var units = Ui.Card(body, "Units");
            Ui.Label(units, T("ui.result.units"));
            int quiet = 0;
            foreach (var u in _report.Units)
            {
                if (u.Losses == 0) { quiet++; continue; } // 손실 없는 부대는 줄을 줄여 한 줄로
                var unit = Ctx.State.FindUnit(u.UnitId);
                string troop = unit != null && Ctx.Defs.TroopTypes.TryGetValue(unit.TroopType, out var tt) ? T(tt.NameKey) : "";
                Ui.Label(units, T("ui.result.unitLine", Ctx.UnitLabel(u.UnitId), troop, u.Losses.ToString("N0"), u.Wounded.ToString("N0"), u.Dead.ToString("N0")), 28);
            }
            if (quiet > 0) Ui.Label(units, T("ui.result.noLossUnits", quiet), 26, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Label(units, T("ui.result.supply", _report.SupplyReturned.ToString("N0"), _report.SupplyCaptured.ToString("N0")), 28);

            // 부대 결속 (battle.md 2.19.1, E24): 이기면 쌓이고 지면 조금 준다
            var cohesion = new List<string>();
            foreach (var u in _report.Units)
            {
                if (u.CohesionAfter == u.CohesionBefore) continue;
                var diff = u.CohesionAfter - u.CohesionBefore;
                string sign = diff > Game.Foundation.Math.Fix.Zero ? Ui.Colored("+" + Num(diff), Palette.Good) : Ui.Colored("−" + Num(-diff), Palette.Bad);
                cohesion.Add(T("ui.result.cohesionLine", Ctx.UnitLabel(u.UnitId), Num(u.CohesionBefore), Num(u.CohesionAfter), sign));
            }
            Section(body, "ui.result.cohesion", cohesion);

            var injuries = new List<string>();
            foreach (var kv in _report.Injuries) injuries.Add(Ctx.GeneralName(kv.Key) + " (" + T("ui.generals." + kv.Value) + ")");
            Section(body, "ui.result.injuries", injuries);
            Section(body, "ui.result.capturedOwn", Names(_report.CapturedOwn));
            Section(body, "ui.result.newPrisoners", Names(_report.NewPrisoners));
            if (_report.NewPrisoners.Count > 0) Ui.Label(body, T("ui.result.prisonerHint"), 24, TextAnchor.MiddleLeft, Palette.TextDim); // 포로 탭에서 등용·해방 (R-66)
            Section(body, "ui.result.joined", Names(_report.JoinedGenerals));
            var shards = new List<string>();
            foreach (var id in _report.JoinedAsShard) shards.Add(T("ui.result.shardLine", Ctx.GeneralName(id)));
            Section(body, "ui.result.joinedShard", shards);
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
            foreach (var s in _report.Secrets) secrets.Add(SecretText.Found(Ctx, s));
            Section(body, "ui.result.secrets", secrets);

            // 녹화 저장 (replay.md 3장): 기기에만, 한도를 넘으면 가장 오래된 것부터 지운다
            Button save = null;
            save = Ui.Button(Root, T("ui.replay.save"), () =>
            {
                Ctx.SaveReplay(_plan, _result.Commands, _result);
                SetLabel(save, T("ui.replay.saved", Ctx.Replays.Items.Count, Ctx.ReplayRules.MaxSaved));
                Ui.SetEnabled(save, false);
            }, 100, Palette.Card);

            bool territory = _plan.Step.IsTerritory; // 영토 전투면 영토 탭으로
            // 이긴 스토리 스텝에 뒤 대사가 있으면 그것부터 (story.md 1.3)
            bool after = !territory && _report.Won && AfterDialogueScreen.Has(Ctx, _plan.Step.Id);
            Ui.Button(Root, T(territory ? "ui.result.toTerritory" : after ? "ui.result.toAfter" : "ui.result.toStory"),
                () => { if (after) App.Show(new AfterDialogueScreen(_plan.Step.Id)); else App.ShowMain(territory ? "territory" : "story"); }, 140);
        }

        private static void SetLabel(Button b, string text) => b.GetComponentInChildren<Text>().text = text;

        /// <summary>결속 값 표시 (소수 한 자리까지).</summary>
        private static string Num(Game.Foundation.Math.Fix v) => v.ToDisplayDouble().ToString("0.#");

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

        /// <summary>끝난 까닭을 구체적으로: 승패 조건이면 누가 무엇으로 졌는지 (총대장 퇴각 / 모두 물러남 / 그 밖의 조건).</summary>
        private string WhyEnded()
        {
            if (_result.EndReason != "defeat" || _plan?.Setup == null) return T("ui.result.endReason." + _result.EndReason);
            string loser = _report.Won ? StoryFlow.EnemyTeam : StoryFlow.PlayerTeam;
            string side = _report.Won ? "Enemy" : "Own";
            bool anyIn = false;
            Game.Battle.Model.Simulation.CorpsSetup commander = null;
            foreach (var c in _plan.Setup.Corps)
            {
                if (c.Team != loser) continue;
                if (c.Role == "commander") commander = c;
                var r = _result.Find(c.Id);
                if (r != null && r.Status == Game.Battle.Model.Simulation.CorpsStatus.Active) anyIn = true;
            }
            if (!anyIn) return T("ui.result.why.all" + side);
            var cr = commander == null ? null : _result.Find(commander.Id);
            if (cr != null && cr.Status != Game.Battle.Model.Simulation.CorpsStatus.Active) return T("ui.result.why.commander" + side);
            return T("ui.result.why.other");
        }
    }
}
