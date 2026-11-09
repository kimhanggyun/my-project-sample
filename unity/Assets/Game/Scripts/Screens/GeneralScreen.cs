using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Economy;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 장수 키우기 › 장수 한 명 (generals.md 4장): 능력치, 아는 스킬, 스킬북 사기·배우기.
    /// 배울 수 있는지(책·요구 능력치·습득 최소 조건)는 규칙 객체 <see cref="SkillBookService"/>가 판단한다.
    /// </summary>
    public sealed class GeneralScreen : GameScreen
    {
        private static readonly string[] MainStats = { "leadership", "might", "intellect" };

        private readonly string _generalId;
        private Text _notice;

        public GeneralScreen(string generalId) { _generalId = generalId; }

        public override void Build()
        {
            var books = new SkillBookService(Ctx.Defs);
            var g = Ctx.State.FindGeneral(_generalId);
            Ui.Column(Root, 12, 24);
            Ui.Size(Ui.Label(Root, T("ui.generals.line", Ctx.GeneralName(_generalId), g.Level, T("ui.generals." + g.Status)), Ui.FontBody), 80);
            Ui.Size(Ui.Label(Root, T("resource.gold") + " " + Ctx.Resource(ResourceIds.Gold).ToString("N0"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim), 48);
            _notice = Ui.Label(Root, "", 28, TextAnchor.MiddleLeft, Palette.Text);
            Ui.Size(_notice, 44);

            var list = Ui.ScrollList(Root, "Body", 12, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);

            var stats = Ui.Card(list, "Stats");
            var parts = new List<string>();
            foreach (var id in MainStats)
                if (Ctx.Defs.Stats.TryGetValue(id, out var sd)) parts.Add(T(sd.NameKey) + " " + CorpsValidator.EffectiveStat(Ctx.Defs, g, id).FloorToLong());
            Ui.Label(stats, string.Join("   ", parts));

            var known = Ui.Card(list, "Known");
            Ui.Label(known, T("ui.general.knownSkills"));
            var names = new List<string>();
            foreach (var s in Ctx.Defs.Skills.Values) if (books.Knows(g, s.Id)) names.Add(T(s.NameKey));
            Ui.Label(known, names.Count == 0 ? T("ui.common.none") : string.Join(", ", names), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            Ui.Label(list, T("ui.general.skillBooks"), Ui.FontBody);
            foreach (var s in Ctx.Defs.Skills.Values)
            {
                if (books.Knows(g, s.Id)) continue;
                var price = books.Price(s.Id);
                var check = books.Check(Ctx.State, _generalId, s.Id);
                var card = Ui.Card(list, s.Id, 16, 6);
                Ui.Label(card, T(s.NameKey) + "  " + Ui.Colored(T("ui.general.owned", books.Owned(Ctx.State, s.Id)), Palette.TextDim));
                var why = new List<string> { T("ui.learn." + check) };
                foreach (var p in books.Progress(g, s))
                    why.Add(Ui.Colored(T("ui.general.progress", T("counter." + p.Counter), p.Have, p.Need), p.Met ? Palette.Good : Palette.TextDim));
                if (s.LearnConditions.Count > 1) why.Add(T(s.LearnAny ? "ui.general.anyOf" : "ui.general.allOf"));
                Ui.Label(card, string.Join("\n", why), 26, TextAnchor.UpperLeft, check == LearnCheck.Ok ? Palette.Good : Palette.TextDim);
                var row = Ui.Row(card, "Buttons", 84, 8);
                string id = s.Id;
                var buy = Ui.Button(row, price.HasValue ? T("ui.general.buy", price.Value.ToString("N0")) : T("ui.general.notSold"), () => Buy(books, id), 84, fontSize: 28);
                Ui.SetEnabled(buy, price.HasValue && Ctx.Resource(ResourceIds.Gold) >= price.Value);
                var learn = Ui.Button(row, T("ui.general.learn"), () => Learn(books, id), 84, fontSize: 28);
                Ui.SetEnabled(learn, check == LearnCheck.Ok);
            }

            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("general"));
        }

        private void Buy(SkillBookService books, string skillId)
        {
            bool ok = books.Buy(Ctx.State, skillId);
            if (ok) Ctx.Save();
            Reopen(ok ? T("ui.general.bought") : T("ui.general.cannotBuy"));
        }

        private void Learn(SkillBookService books, string skillId)
        {
            var r = books.Learn(Ctx.State, _generalId, skillId);
            if (r == LearnCheck.Ok) Ctx.Save();
            Reopen(r == LearnCheck.Ok ? T("ui.general.learned") : T("ui.learn." + r));
        }

        private void Reopen(string notice)
        {
            var next = new GeneralScreen(_generalId);
            App.Show(next);
            next._notice.text = notice;
        }
    }
}
