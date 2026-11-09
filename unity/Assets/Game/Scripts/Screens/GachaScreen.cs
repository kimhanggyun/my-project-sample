using System.Collections.Generic;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 장수 모집 (generals.md 5장, R-51): 재화, 1회·10회, 10회 보장·천장 안내, [확률 보기](데이터를 그대로 보여준다), 결과 목록.
    /// </summary>
    public sealed class GachaScreen : GameScreen
    {
        private readonly string _bannerId;
        private readonly GachaResult _last;
        private readonly bool _showRates;

        public GachaScreen(string bannerId = "standard", GachaResult last = null, bool showRates = false) { _bannerId = bannerId; _last = last; _showRates = showRates; }

        public override void Build()
        {
            var g = Ctx.Defs.Gacha;
            if (!g.Banners.TryGetValue(_bannerId, out var b)) { App.ShowMain("general"); return; }
            var svc = new GachaService(Ctx.Defs);
            Ui.Column(Root, 12, 32);
            Ui.Size(Ui.Label(Root, T(b.NameKey), Ui.FontTitle), 110);
            Ui.Label(Root, T("ui.gacha.note"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Label(Root, T("ui.gacha.balance", T("resource." + b.Currency), Ctx.Resource(b.Currency).ToString("N0")), Ui.FontSmall);
            Ctx.State.GachaPity.TryGetValue(b.Id, out int since);
            if (b.TenGuarantee != null && b.PityGrade != null)
                Ui.Label(Root, T("ui.gacha.rules", T("grade." + b.TenGuarantee), T("grade." + b.PityGrade), b.PityCount, since), 24, TextAnchor.MiddleLeft, Palette.TextDim);

            var list = Ui.ScrollList(Root, "Body", 8, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            if (_showRates)
            {
                Ui.Label(list, T("ui.gacha.ratesTitle"), Ui.FontBody);
                foreach (var grade in g.Grades)
                {
                    Ui.Label(list, T("ui.gacha.gradeRate", T("grade." + grade.Id), Percent(grade.Rate.Raw)), Ui.FontSmall);
                    foreach (var r in svc.Rates(b.Id))
                        if (r.Grade == grade.Id) Ui.Label(list, T("ui.gacha.generalRate", Ctx.GeneralName(r.GeneralId), T("grade." + r.Grade), Percent(r.Rate.Raw)), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                }
            }
            else if (_last != null)
            {
                Ui.Label(list, T(_last.Ok ? "ui.gacha.result" : "ui.gacha.notEnough"), Ui.FontBody, TextAnchor.MiddleLeft, _last.Ok ? Palette.Text : Palette.Bad);
                foreach (var p in _last.Pulls)
                {
                    var color = p.Grade == "legend" || p.Grade == "hero" ? Palette.Good : Palette.Text;
                    Ui.Label(list, Ui.Colored("[" + T("grade." + p.Grade) + "] ", color) + Ctx.GeneralName(p.GeneralId) + "  " + T(p.Duplicate ? "ui.gacha.shard" : "ui.gacha.new"), Ui.FontSmall);
                }
            }

            var row = Ui.Row(Root, "Draw", Ui.ButtonHeight);
            var one = Ui.Button(row, T("ui.gacha.one", b.CostOne.ToString("N0")), () => App.Show(new GachaScreen(_bannerId, Ctx.DrawGacha(_bannerId, 1))));
            var ten = Ui.Button(row, T("ui.gacha.ten", b.CostTen.ToString("N0")), () => App.Show(new GachaScreen(_bannerId, Ctx.DrawGacha(_bannerId, 10))));
            Ui.SetEnabled(one, Ctx.Resource(b.Currency) >= b.CostOne);
            Ui.SetEnabled(ten, Ctx.Resource(b.Currency) >= b.CostTen);
            var bottom = Ui.Row(Root, "Bottom", Ui.ButtonHeight);
            Ui.Button(bottom, T("ui.common.back"), () => App.ShowMain("general"));
            Ui.Button(bottom, T("ui.gacha.rates"), () => App.Show(new GachaScreen(_bannerId, null, !_showRates)));
        }

        /// <summary>원본 정수(1 = 0.0001) → "0.5" 같은 % 글자.</summary>
        private static string Percent(long raw) => (raw / 100m).ToString("0.##");
    }
}
