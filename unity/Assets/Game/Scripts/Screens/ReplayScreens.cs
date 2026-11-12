using System;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 내 녹화 목록 (replay.md 3장): 기기에 저장한 전투 기록. [재생]은 지금 규칙으로 한 번 돌려 결과가 같을 때만 연다 (4장, 다르면 재생 불가).
    /// [공유]는 서버에 공략으로 올린다 (R-81, 서버가 다시 돌려 확인). 영상 저장은 네이티브 녹화가 필요해 뒤로.
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
                if (Ctx.OnlineEnabled) Ui.Button(row, T("ui.replay.share"), () => Share(rec), 80, fontSize: 28);
                Ui.Button(row, T("ui.replay.delete"), () => { Ctx.DeleteReplay(rec.Id); App.Show(new ReplayListScreen()); }, 80, Palette.Card, 28);
            }
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("story"));
        }

        private void Share(ReplayRecord rec)
        {
            Ctx.Online.ShareReplay(new Game.Online.ShareReplayRequest { ReplayJson = ReplayService.Write(rec) }, res =>
                App.Show(new ReplayListScreen(res == null ? T("ui.shared.failed")
                    : res.Ok ? T("ui.replay.shared")
                    : T(res.Reason == ReplayStatus.VersionChanged ? "ui.replay.versionChanged" : "ui.replay.broken"))));
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

    /// <summary>
    /// 공략 (R-81): 다른 사람이 공유한 이 스텝의 녹화. 이긴 것·손실 적은 것 먼저. [보기]는 받아서 지금 규칙으로 확인한 뒤 재생.
    /// </summary>
    public sealed class SharedReplayScreen : GameScreen
    {
        private readonly string _stepId;
        private readonly string _message;
        public SharedReplayScreen(string stepId, string message = null) { _stepId = stepId; _message = message; }

        public override void Build()
        {
            Ui.Column(Root, 12, 32);
            string stepName;
            try { stepName = T(Ctx.Flow.LoadStep(_stepId).NameKey); } catch (Exception) { stepName = _stepId; }
            Ui.Size(Ui.Label(Root, T("ui.shared.title", stepName), Ui.FontTitle), 110);
            Ui.Label(Root, T("ui.shared.note"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            if (_message != null) Ui.Label(Root, _message, 26, TextAnchor.MiddleLeft, Palette.Bad);
            var list = Ui.ScrollList(Root, "Shared", 8, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            var loading = Ui.Label(list, T("ui.shared.loading"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Button(Root, T("ui.common.back"), () => App.Show(new StepScreen(_stepId)));

            Ctx.Online.GetSharedReplays(_stepId, res =>
            {
                if (list == null) return; // 화면이 바뀜
                loading.text = res == null ? T("ui.shared.failed") : res.Items.Count == 0 ? T("ui.shared.empty") : "";
                if (res == null) return;
                foreach (var it in res.Items)
                {
                    var item = it;
                    var card = Ui.Card(list, item.Id);
                    string result = item.Won ? Ui.Colored(T("ui.result.win"), Palette.Good) : Ui.Colored(T("ui.result.lose"), Palette.Bad);
                    Ui.Label(card, result + "  " + (string.IsNullOrEmpty(item.OwnerName) ? T("ui.shared.anonymous") : item.OwnerName), Ui.FontSmall);
                    string when = DateTimeOffset.FromUnixTimeSeconds(item.SharedAtUnixSeconds).ToLocalTime().ToString("MM-dd HH:mm");
                    Ui.Label(card, T("ui.replay.line", item.Seconds / 60, (item.Seconds % 60).ToString("00"), item.PlayerLosses.ToString("N0"), item.PlayerStartTroops.ToString("N0"), when),
                        24, TextAnchor.MiddleLeft, Palette.TextDim);
                    Ui.Button(card, T("ui.shared.watch"), () => Watch(item.Id), 80, Palette.ButtonActive, 28);
                }
            });
        }

        private void Watch(string id)
        {
            Ctx.Online.GetSharedReplay(id, res =>
            {
                if (res == null) { App.Show(new SharedReplayScreen(_stepId, T("ui.shared.failed"))); return; }
                ReplayRecord rec;
                try { rec = ReplayService.Read(res.ReplayJson); } catch (Exception) { rec = null; }
                var playback = rec == null ? null : Ctx.ReplayService.Check(rec);
                if (playback != null && playback.Ok) { App.Show(new BattleScreen(playback)); return; }
                App.Show(new SharedReplayScreen(_stepId, T(playback?.Status == ReplayStatus.VersionChanged ? "ui.replay.versionChanged" : "ui.replay.broken")));
            });
        }
    }
}
