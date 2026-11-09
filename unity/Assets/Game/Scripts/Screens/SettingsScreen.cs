using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>설정 (임시): 버전, 외부 서비스 설정, 세이브 초기화.</summary>
    public sealed class SettingsScreen : GameScreen
    {
        private bool _confirmReset;
        private Text _resetNote;

        public override void Build()
        {
            Ui.Column(Root, 16, 32);
            Ui.Size(Ui.Label(Root, T("ui.settings.header"), Ui.FontTitle), 120);

            var body = Ui.ScrollList(Root, "Body", 12, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);

            var info = Ui.Card(body, "Info");
            Ui.Label(info, T("ui.settings.version", Application.version, Application.unityVersion), Ui.FontSmall);
            Ui.Label(info, T("ui.settings.save", Application.persistentDataPath), 26, TextAnchor.MiddleLeft, Palette.TextDim);

            var services = Ui.Card(body, "Services");
            Ui.Label(services, T("ui.settings.services"));
            foreach (var kv in Ctx.ServiceChoices) Ui.Label(services, kv.Key + ": " + kv.Value, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            var reset = Ui.Card(body, "Reset");
            Ui.Button(reset, T("ui.settings.reset"), OnReset, color: new Color(0.45f, 0.22f, 0.20f));
            _resetNote = Ui.Label(reset, "", Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain());
        }

        private void OnReset()
        {
            if (!_confirmReset)
            {
                _confirmReset = true;
                _resetNote.text = T("ui.settings.resetConfirm");
                return;
            }
            Ctx.ResetSave();
            _confirmReset = false;
            _resetNote.text = T("ui.settings.resetDone");
        }
    }
}
