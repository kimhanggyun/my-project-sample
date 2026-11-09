using System;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 내 녹화 목록 (replay.md 3장): 기기에 저장한 전투 기록. [재생]은 지금 규칙으로 한 번 돌려 결과가 같을 때만 연다 (4장, 다르면 재생 불가).
    /// 공략 공유·영상 저장은 이어서 (공유는 서버 필요).
    /// </summary>
    public sealed class ReplayListScreen : GameScreen
    {
        private readonly string _message;
        public ReplayListScreen(string message = null) { _message = message; }

        public override void Build()
        {
            Ui.Column(Root, 12, 32);
            Ui.Size(Ui.Label(Root, T("ui.replay.title"), Ui.FontTitle), 110);
            Ui.Label(Root, T("ui.replay.count", Ctx.Replays.Items.Count, Ctx.ReplayRules.MaxSaved), Ui.FontSmall);
            Ui.Label(Root, T("ui.replay.note"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            if (_message != null) Ui.Label(Root, _message, 26, TextAnchor.MiddleLeft, Palette.Good);

            var list = Ui.ScrollList(Root, "Replays", 8, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            if (Ctx.Replays.Items.Count == 0) Ui.Label(list, T("ui.replay.empty"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            foreach (var r in Ctx.Replays.Items)
            {
                var rec = r;
                var card = Ui.Card(list, rec.Id);
                string result = rec.Won ? Ui.Colored(T("ui.result.win"), Palette.Good) : Ui.Colored(T("ui.result.lose"), Palette.Bad);
                Ui.Label(card, StepName(rec.StepId) + "  " + result, Ui.FontSmall);
                string when = DateTimeOffset.FromUnixTimeSeconds(rec.SavedAtUnixSeconds).ToLocalTime().ToString("MM-dd HH:mm");
                Ui.Label(card, T("ui.replay.line", rec.Seconds / 60, (rec.Seconds % 60).ToString("00"), rec.PlayerLosses.ToString("N0"), rec.PlayerStartTroops.ToString("N0"), when),
                    24, TextAnchor.MiddleLeft, Palette.TextDim);
                var row = Ui.Row(card, "Buttons", 80, 8);
                Ui.Button(row, T("ui.replay.play"), () => Play(rec), 80, Palette.ButtonActive, 28);
                Ui.Button(row, T("ui.replay.delete"), () => { Ctx.DeleteReplay(rec.Id); App.Show(new ReplayListScreen()); }, 80, Palette.Card, 28);
            }
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("story"));
        }

        private void Play(ReplayRecord rec)
        {
            var playback = Ctx.ReplayService.Check(rec);
            if (playback.Ok) { App.Show(new BattleScreen(playback)); return; }
            App.Show(new ReplayListScreen(T(playback.Status == ReplayStatus.VersionChanged ? "ui.replay.versionChanged" : "ui.replay.broken")));
        }

        private string StepName(string stepId)
        {
            try { return T(Ctx.Flow.LoadStep(stepId).NameKey); }
            catch (Exception) { return stepId; }
        }
    }
}
