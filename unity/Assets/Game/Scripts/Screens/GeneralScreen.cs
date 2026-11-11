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

            // 성향·특성 (traits.md, R-67): 특성은 전투에 실제 효과가 있다
            var traits = Ui.Card(list, "Traits");
            if (Ctx.Defs.Generals.TryGetValue(_generalId, out var gd))
                Ui.Label(traits, T("ui.general.temperament", T("temperament." + gd.Temperament)), Ui.FontSmall);
            Ui.Label(traits, T("ui.general.traits"));
            if (g.LearnedTraits.Count == 0) Ui.Label(traits, T("ui.general.noTraits"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            foreach (var t in g.LearnedTraits)
            {
                bool defined = Ctx.Defs.Traits.Traits.TryGetValue(t, out var td);
                Ui.Label(traits, Ui.Colored(T(defined ? td.NameKey : "trait." + t), Palette.Good) + "  " + Ui.Colored(defined ? T(td.DescKey) : "", Palette.TextDim), 26);
            }

            // 한계 돌파 (R-57): 모집에서 겹친 장수의 조각으로 레벨 상한을 올린다
            if (Ctx.Defs.Gacha.MaxLimitBreak > 0)
            {
                var lb = new LimitBreakService(Ctx.Defs);
                var card = Ui.Card(list, "LimitBreak");
                Ui.Label(card, T("ui.limitBreak.line", g.LimitBreak, Ctx.Defs.Gacha.MaxLimitBreak, LimitBreakService.MaxLevelOf(Ctx.Defs, g), LimitBreakService.Shards(Ctx.State, _generalId)));
                var cost = lb.NextCost(g);
                var check = lb.Check(Ctx.State, _generalId);
                var b = Ui.Button(card, cost.HasValue ? T("ui.limitBreak.button", cost.Value) : T("ui.limitBreak.maxStage"), LimitBreak, 84, fontSize: 28);
                Ui.SetEnabled(b, check == LimitBreakService.Ok);
            }

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
            bool ok = Ctx.Do(PlayerActionRunner.BuySkillBook(skillId));
            Reopen(ok ? T("ui.general.bought") : T("ui.general.cannotBuy"));
        }

        private void Learn(SkillBookService books, string skillId)
        {
            // 안 되는 까닭을 먼저 보고, 되면 행동으로 (서버도 같은 규칙으로 적용, R-41)
            var r = books.Check(Ctx.State, _generalId, skillId);
            if (r == LearnCheck.Ok && !Ctx.Do(PlayerActionRunner.LearnSkill(_generalId, skillId))) r = books.Check(Ctx.State, _generalId, skillId);
            Reopen(r == LearnCheck.Ok ? T("ui.general.learned") : T("ui.learn." + r));
        }

        private void LimitBreak()
        {
            bool ok = Ctx.Do(PlayerActionRunner.LimitBreak(_generalId));
            Reopen(ok ? T("ui.limitBreak.done", LimitBreakService.MaxLevelOf(Ctx.Defs, Ctx.State.FindGeneral(_generalId))) : T("ui.limitBreak." + new LimitBreakService(Ctx.Defs).Check(Ctx.State, _generalId)));
        }

        private void Reopen(string notice)
        {
            var next = new GeneralScreen(_generalId);
            App.Show(next);
            next._notice.text = notice;
        }
    }
}
