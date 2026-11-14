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
    /// 장수 상세 (11 시안): 고정 머리(‹ · ◀ ▶ · 초상 · 이름 · 등급 · Lv + 경험치 · 상태 · 능력치 타일 · 성향/특성 꼬리표) + 탭 성장 · 스킬 · 기록.
    /// 배울 수 있는지(책 · 요구 능력치 · 습득 최소 조건)는 규칙 객체 <see cref="SkillBookService"/>가 판단한다. 스킬북은 여기서도 산다.
    /// </summary>
    public sealed class GeneralScreen : GameScreen
    {
        private static readonly string[] MainStats = { "leadership", "might", "intellect" };
        private static string _tab = "grow";

        private readonly string _generalId;
        private Text _notice;

        public GeneralScreen(string generalId) { _generalId = generalId; }

        public override void Build()
        {
            var books = new SkillBookService(Ctx.Defs);
            var g = Ctx.State.FindGeneral(_generalId);
            Ctx.Defs.Generals.TryGetValue(_generalId, out var gdef);
            var (header, body, _) = Ui.PageLayout(Root, 0);

            // ---- 머리 ----
            var nav = Ui.Row(header, "Nav", 84, 10);
            Ui.Size(Ui.Button(nav, "‹", () => App.ShowMain("general"), 84, fontSize: 40), width: 90);
            var gs = Ctx.State.Generals;
            int at = gs.FindIndex(x => x.GeneralId == _generalId);
            Ui.Size(Ui.Label(nav, (at + 1) + " / " + gs.Count, 22, TextAnchor.MiddleRight, Palette.TextDim), flexibleWidth: 1);
            Ui.Size(Ui.Button(nav, "◀", () => App.Show(new GeneralScreen(gs[(at - 1 + gs.Count) % gs.Count].GeneralId)), 84, fontSize: 30), width: 110);
            Ui.Size(Ui.Button(nav, "▶", () => App.Show(new GeneralScreen(gs[(at + 1) % gs.Count].GeneralId)), 84, fontSize: 30), width: 110);

            var who = Ui.Row(header, "Who", 200, 18);
            var pic = Ui.Rect(who, "Portrait");
            Ui.Size(pic, 190, 190);
            new Portrait(pic, 190).Show(_generalId, Ctx.GeneralName(_generalId), GradeColor(gdef?.Grade));
            var id = Ui.Rect(who, "Id");
            Ui.Size(id, flexibleWidth: 1);
            Ui.Column(id, 6, 0);
            Ui.Label(id, Ctx.GeneralName(_generalId), Ui.FontTitle - 10);
            var tags = Ui.Row(id, "Tags", 44, 8);
            if (_generalId == AvatarGeneral.GeneralId) Ui.Size(Ui.Tag(tags, T("ui.general.me"), Palette.Seal, 22, 40), width: 80);
            if (gdef != null) Ui.Size(Ui.Tag(tags, T("grade." + gdef.Grade), GradeColor(gdef.Grade), 22, 40), width: 120);
            Ui.Size(Ui.Tag(tags, "Lv." + g.Level, new Color(0.16f, 0.13f, 0.1f), 22, 40), width: 120);
            long need = Ctx.Defs.Levels.ExpToNext(g.Level);
            var exp = Ui.Gauge(id, Palette.Ally, 22);
            Ui.SetGauge((RectTransform)exp.GetChild(0), need <= 0 ? 1f : Mathf.Clamp01((float)g.Exp / need));
            Ui.Label(id, g.Exp.ToString("N0") + " / " + need.ToString("N0") + "   " + T("ui.generals." + g.Status), 22, TextAnchor.MiddleLeft, Palette.TextDim);

            // 능력치 타일 (아이콘 + 큰 숫자 + 레벨마다 성장)
            var statRow = Ui.Row(header, "Stats", 100, 10);
            foreach (var s in MainStats)
            {
                var tile = Ui.Card(statRow, s, 10, 2);
                long v = CorpsValidator.EffectiveStat(Ctx.Defs, g, s).FloorToLong();
                string grow = gdef != null && gdef.GrowthRates.TryGetValue(s, out var gr) ? "+" + gr.ToDisplayDouble().ToString("0.#") + "/Lv" : "";
                Icons.Value(Ctx, tile, s, v.ToString() + "  " + Ui.Colored(grow, Palette.Good), 44, 34);
            }
            // 성향 · 특성 꼬리표
            var traitRow = Ui.Row(header, "Traits", 48, 8);
            if (gdef != null) Ui.Size(Ui.Tag(traitRow, T("temperament." + gdef.Temperament), Palette.TextDim, 20, 44), width: 140);
            foreach (var t in g.LearnedTraits)
                Ui.Size(Ui.Tag(traitRow, Ctx.Defs.Traits.Traits.TryGetValue(t, out var td) ? T(td.NameKey) : t, Palette.Good, 20, 44), width: 140);
            InfoButton.Add(Root, T("ui.general.note"));
            Ui.SegmentTabs(header, new[] { ("grow", T("ui.general.tab.grow")), ("skill", T("ui.general.tab.skill")), ("record", T("ui.general.tab.record")) }, _tab, t => { _tab = t; App.Show(new GeneralScreen(_generalId)); }, 58, 300);
            _notice = Ui.Label(body, "", 26, TextAnchor.MiddleLeft, Palette.Good);

            if (_tab == "grow")
            {
                var lvl = Ui.Card(body, "Level", 18, 6);
                Ui.Label(lvl, T("ui.general.levelNext", g.Level, g.Level + 1), Ui.FontSmall);
                Ui.Button(lvl, T("ui.general.practice"), () => App.ShowMain("practice"), 72, fontSize: 24);
                if (Ctx.Defs.Gacha.MaxLimitBreak > 0)
                {
                    var lb = new LimitBreakService(Ctx.Defs);
                    var card = Ui.Card(body, "LimitBreak", 18, 6);
                    string stars = new string('★', g.LimitBreak) + new string('☆', System.Math.Max(0, Ctx.Defs.Gacha.MaxLimitBreak - g.LimitBreak));
                    Ui.Label(card, stars + "   " + T("ui.general.cap", LimitBreakService.MaxLevelOf(Ctx.Defs, g)) + "   " + T("ui.general.shards", LimitBreakService.Shards(Ctx.State, _generalId)), Ui.FontSmall);
                    var cost = lb.NextCost(g);
                    var b = Ui.Button(card, cost.HasValue ? T("ui.limitBreak.button", cost.Value) : T("ui.limitBreak.maxStage"), LimitBreak, 72, fontSize: 24);
                    Ui.SetEnabled(b, lb.Check(Ctx.State, _generalId) == LimitBreakService.Ok);
                }
                var rec = Ui.Card(body, "Contests", 18, 6);
                Ui.Label(rec, T("ui.general.contestGrowth"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
                foreach (var c in new[] { "duel", "debate" })
                {
                    var (w, l, r, d, streak) = PracticeService.RecordOf(g, c);
                    Ui.Label(rec, T("ui.practice.recordLine", T("ui.contest." + c), w, l, r, d, streak), 24);
                }
            }
            else if (_tab == "skill")
            {
                var known = Ui.Card(body, "Known", 18, 6);
                Ui.Label(known, T("ui.general.knownSkills"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
                var kr = Ui.Row(known, "Tags", 52, 8);
                int kn = 0;
                foreach (var s in Ctx.Defs.Skills.Values)
                    if (books.Knows(g, s.Id)) { kn++; Ui.Size(Ui.Tag(kr, T(s.NameKey), (gdef?.InnateSkills != null && System.Linq.Enumerable.Contains(gdef.InnateSkills, s.Id)) ? Palette.TextDim : Palette.Ally, 22, 48), width: 170); }
                if (kn == 0) Ui.Label(known, T("ui.common.none"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                Ui.Label(body, T("ui.general.skillBooks") + "  " + Ui.Colored(T("resource.gold") + " " + Ctx.Resource(ResourceIds.Gold).ToString("N0"), Palette.TextDim), Ui.FontSmall);
                foreach (var s in Ctx.Defs.Skills.Values)
                {
                    if (books.Knows(g, s.Id)) continue;
                    var price = books.Price(s.Id);
                    var check = books.Check(Ctx.State, _generalId, s.Id);
                    var card = Ui.Card(body, s.Id, 16, 4);
                    var top = Ui.Row(card, "Top", 52, 8);
                    Ui.Size(Ui.Label(top, T(s.NameKey) + "  ×" + books.Owned(Ctx.State, s.Id), 26), flexibleWidth: 1);
                    if (check == LearnCheck.Ok) Ui.Size(Ui.Tag(top, T("ui.general.canLearn"), Palette.Good, 20, 44), width: 180);
                    var why = new List<string>();
                    if (check != LearnCheck.Ok) why.Add(T("ui.learn." + check));
                    foreach (var p in books.Progress(g, s))
                        why.Add(Ui.Colored(T("ui.general.progress", T("counter." + p.Counter), p.Have, p.Need), p.Met ? Palette.Good : Palette.TextDim));
                    if (why.Count > 0) Ui.Label(card, string.Join("  ", why), 20, TextAnchor.MiddleLeft, Palette.TextDim);
                    var row = Ui.Row(card, "Buttons", 64, 8);
                    string sid = s.Id;
                    var buy = Ui.Button(row, price.HasValue ? T("ui.general.buy", price.Value.ToString("N0")) : T("ui.general.notSold"), () => Buy(books, sid), 64, fontSize: 22);
                    Ui.SetEnabled(buy, price.HasValue && Ctx.Resource(ResourceIds.Gold) >= price.Value);
                    var learn = Ui.Button(row, T("ui.general.learn"), () => Learn(books, sid), 64, check == LearnCheck.Ok ? Palette.Seal : (Color?)null, 22);
                    Ui.SetEnabled(learn, check == LearnCheck.Ok);
                }
            }
            else
            {
                var duels = Ui.Card(body, "Duels", 18, 6);
                foreach (var c in new[] { "duel", "debate" })
                {
                    var (w, l, r, d, streak) = PracticeService.RecordOf(g, c);
                    Ui.Label(duels, T("ui.practice.recordLine", T("ui.contest." + c), w, l, r, d, streak), 24);
                }
                if (_generalId == AvatarGeneral.GeneralId)
                {
                    var merc = Ui.Card(body, "Merc", 18, 6);
                    Ui.Label(merc, T("ui.general.mercNote"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    Ui.Button(merc, T("ui.general.toMerc"), () => App.ShowMain("hiring"), 72, fontSize: 24);
                }
            }
        }

        private static Color GradeColor(string grade) => grade switch
        {
            "legend" => new Color(0.78f, 0.59f, 0.18f),
            "hero" => new Color(0.47f, 0.31f, 0.63f),
            "rare" => new Color(0.25f, 0.44f, 0.69f),
            "advanced" => new Color(0.24f, 0.48f, 0.25f),
            _ => new Color(0.55f, 0.52f, 0.47f),
        };

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
