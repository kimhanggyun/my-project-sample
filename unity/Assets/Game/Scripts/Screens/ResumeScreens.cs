using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 전투 이어하기 물음 (R-55): 지난번 전투가 중간에 끝났다. [이어하기]는 멈춘 곳부터(멈춘 채로 시작),
    /// [자동으로 끝내기]는 모든 아군 군단을 위임하고 끝까지 돌려 결과를 반영한다.
    /// </summary>
    public sealed class ResumeBattleScreen : GameScreen
    {
        public override void Build()
        {
            Ui.Column(Root, 24, 48).childAlignment = TextAnchor.MiddleCenter;
            var s = Ctx.SuspendedBattle;
            Ui.Size(Ui.Label(Root, T("ui.resume.title"), Ui.FontTitle, TextAnchor.MiddleCenter), 140);
            string step = s?.StepId ?? "";
            try { if (s != null) step = T(Ctx.Flow.LoadStep(s.StepId).NameKey); } catch (System.Exception) { }
            Ui.Label(Root, T("ui.resume.body", step), Ui.FontSmall, TextAnchor.MiddleCenter);
            Ui.Label(Root, T("ui.resume.note"), 24, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Button(Root, T("ui.resume.continue"), () => Open(false), 140, Palette.ButtonActive);
            Ui.Button(Root, T("ui.resume.autoFinish"), () => Open(true), 120, Palette.Card);
        }

        private void Open(bool autoFinish)
        {
            var r = Ctx.ResumeBattle();
            if (r == null) { App.Show(new ResumeFailedScreen()); return; }
            App.Show(new BattleScreen(r, autoFinish));
        }
    }

    /// <summary>이어 할 수 없을 때 (데이터가 바뀌었거나 기록이 깨짐). 가져간 군량은 돌려받지 못한다.</summary>
    public sealed class ResumeFailedScreen : GameScreen
    {
        public override void Build()
        {
            Ui.Column(Root, 24, 48).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Label(Root, T("ui.resume.failed"), Ui.FontSmall, TextAnchor.MiddleCenter), 300);
            Ui.Button(Root, T("ui.common.ok"), () => App.ShowMain(), 140);
        }
    }
}
