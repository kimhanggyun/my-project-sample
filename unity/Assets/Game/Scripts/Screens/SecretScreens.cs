using System.Collections.Generic;
using Game.Data.Defs;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>숨은 보상 문구 (secrets.md 3장): 이름 "secret.{id}", 특성 이름 "trait.{id}".</summary>
    public static class SecretText
    {
        public static string Name(GameContext ctx, string secretId) => ctx.T("secret." + secretId);

        public static string Reward(GameContext ctx, SecretDef s)
        {
            var parts = new List<string>();
            if (s.RewardSkill != null) parts.Add(ctx.T("ui.result.secretSkill", ctx.Defs.Skills.TryGetValue(s.RewardSkill, out var sd) ? ctx.T(sd.NameKey) : s.RewardSkill));
            if (s.RewardTrait != null) parts.Add(ctx.T("ui.result.secretTrait", ctx.T("trait." + s.RewardTrait)));
            return string.Join(", ", parts);
        }

        /// <summary>결과 화면 한 줄: 「이름」 장수 → 보상 (보관함이면 표시).</summary>
        public static string Found(GameContext ctx, FoundSecret f)
        {
            SecretDef def = null;
            foreach (var s in ctx.Defs.Secrets) if (s.Id == f.SecretId) { def = s; break; }
            string reward = def != null ? Reward(ctx, def) : f.SecretId;
            if (f.StoredAsBook) reward = ctx.T("ui.result.secretStored", reward);
            return ctx.T("ui.result.secretLine", Name(ctx, f.SecretId), ctx.GeneralName(f.GeneralId), reward);
        }

        public static int FoundCount(GameContext ctx, out int total)
        {
            var codex = SecretEngine.Codex(ctx.Defs, ctx.State);
            total = codex.Count;
            int n = 0;
            foreach (var e in codex) if (e.Found) n++;
            return n;
        }
    }

    /// <summary>
    /// 도감 (13 시안): 고정 머리([‹] · "도감" · 진행 고리 찾은 수 / 전체 · ⓘ) + 탭 숨은 보상 · 장수.
    /// 숨은 보상: 분류 칩(아이콘 + 찾은 수) · 3열 도장 격자 (찾은 것 = 주홍 도장 + 이름 + 스킬/특성, 못 찾은 것 = "?" · 눌러야 힌트) · 찾은 것 누르면 자세히 (스텝 · 날짜 · 받은 것 · 받은 장수 · 조건 공개).
    /// 장수: 등급 칩 · 4열 카드 (가진 장수 초상 · 못 가진 장수 실루엣 "???", 등급 테두리는 보임) · 얻는 곳 · [모집 ›].
    /// </summary>
    public sealed class CodexScreen : GameScreen
    {
        private static string _tab = "secrets";
        private static string _cat = "all";
        private static string _grade = "all";
        private readonly string _hint;
        private readonly string _detail;

        private static readonly Color Seal = new Color(0.75f, 0.22f, 0.15f);
        private static readonly Color Dark = new Color(0.2f, 0.19f, 0.18f);

        public CodexScreen(string hint = null, string detail = null) { _hint = hint; _detail = detail; }

        public override void Build()
        {
            var (header, body, _) = Ui.PageLayout(Root, 0);
            var codex = SecretEngine.Codex(Ctx.Defs, Ctx.State);
            var gens = SecretEngine.GeneralCodex(Ctx.Defs, Ctx.State);
            int found = 0, owned = 0;
            foreach (var e in codex) if (e.Found) found++;
            foreach (var g in gens) if (g.Owned) owned++;

            var top = Ui.Row(header, "Title", 100, 12);
            Ui.Size(Ui.Button(top, "‹", () => { if (_detail != null) App.Show(new CodexScreen()); else App.ShowMain("story"); }, 84, fontSize: 40), width: 90);
            Ui.Size(Ui.Label(top, T("ui.codex.head"), Ui.FontTitle - 6), flexibleWidth: 1);
            bool sec = _tab == "secrets";
            Ring(top, sec ? found : owned, sec ? codex.Count : gens.Count);
            InfoButton.Add(Root, T(sec ? "ui.codex.note" : "ui.codex.noteGenerals"));
            Ui.SegmentTabs(header, new[] { ("secrets", T("ui.codex.tab.secrets")), ("generals", T("ui.codex.tab.generals")) }, _tab, id => { _tab = id; App.Show(new CodexScreen()); }, 70, 500);

            if (!sec) BuildGenerals(header, body, gens);
            else if (_detail != null) BuildDetail(body, codex);
            else BuildSecrets(header, body, codex);
        }

        /// <summary>진행 고리: 원 안에 찾은 수 / 전체.</summary>
        private void Ring(Transform parent, int n, int total)
        {
            var ring = Ui.Rect(parent, "Ring");
            Ui.Size(ring, 100, 100);
            Ui.Background(ring, Palette.Card).sprite = UiShapes.Circle;
            var fill = Ui.Rect(ring, "Fill");
            Ui.Fill(fill);
            var img = Ui.Background(fill, Palette.Good);
            img.sprite = UiShapes.Circle;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = 2;
            img.fillClockwise = true;
            img.fillAmount = total <= 0 ? 0f : (float)n / total;
            var hole = Ui.Rect(ring, "Hole");
            Ui.Fill(hole);
            hole.offsetMin = new Vector2(10, 10);
            hole.offsetMax = new Vector2(-10, -10);
            Ui.Background(hole, Palette.Card).sprite = UiShapes.Circle;
            Ui.Fill(Ui.Label(ring, n + "/" + total, 24, TextAnchor.MiddleCenter).rectTransform);
        }

        private static string CatIcon(string cat)
        {
            switch (cat)
            {
                case "story": return "書";
                case "battle": return "戰";
                case "duel": return "劍";
                case "land": return "旗";
                case "merc": return "傭";
                default: return "全";
            }
        }

        // ---- 숨은 보상: 칩 + 도장 격자 ----
        private void BuildSecrets(RectTransform header, RectTransform body, List<CodexEntry> codex)
        {
            var chips = new List<(string, string)> { ("all", T("ui.codex.cat.all")) };
            foreach (var c in SecretDef.Categories)
            {
                int n = 0, f = 0;
                foreach (var e in codex) if (e.Category == c) { n++; if (e.Found) f++; }
                chips.Add((c, CatIcon(c) + " " + f + "/" + n));
            }
            Ui.SegmentTabs(header, chips, _cat, id => { _cat = id; App.Show(new CodexScreen()); }, 62, 150);

            var defs = Ctx.Defs.Secrets;
            RectTransform row = null;
            int col = 0;
            for (int i = 0; i < codex.Count; i++)
            {
                var e = codex[i];
                if (_cat != "all" && e.Category != _cat) continue;
                if (col % 3 == 0) row = Ui.Row(body, "Row" + i, 340, 14);
                col++;
                var def = defs[i];
                var tile = Ui.Card(row, def.Id, 12, 4);
                Ui.Size(tile, 340, -1, -1, 1);
                tile.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
                Ui.Label(tile, Ui.Colored(CatIcon(e.Category), Palette.TextDim), 22, TextAnchor.MiddleLeft);
                var stamp = Ui.Rect(tile, "Stamp");
                Ui.Size(stamp, 130, 130);
                string id = def.Id;
                if (e.Found)
                {
                    Ui.Background(stamp, Seal).sprite = UiShapes.Circle;
                    Ui.Fill(Ui.Label(stamp, def.RewardSkill != null ? "技" : "性", 56, TextAnchor.MiddleCenter, Color.white).rectTransform);
                    Ui.Label(tile, SecretText.Name(Ctx, id), 24, TextAnchor.MiddleCenter);
                    Ui.Tag(tile, T(def.RewardSkill != null ? "ui.codex.skill" : "ui.codex.trait"), Palette.Ally, 18, 34);
                    tile.gameObject.AddComponent<Button>().onClick.AddListener(() => App.Show(new CodexScreen(null, id)));
                }
                else
                {
                    var bg = tile.GetComponent<Image>();
                    if (bg != null) bg.color = Dark;
                    Ui.Background(stamp, new Color(0.45f, 0.42f, 0.4f)).sprite = UiShapes.Circle;
                    Ui.Fill(Ui.Label(stamp, "?", 64, TextAnchor.MiddleCenter, new Color(0.85f, 0.82f, 0.78f)).rectTransform);
                    bool open = _hint == id;
                    Ui.Label(tile, open && e.HintKey != null ? T(e.HintKey) : T("ui.codex.tapHint"), 20, TextAnchor.MiddleCenter, open ? new Color(0.95f, 0.9f, 0.7f) : new Color(0.7f, 0.67f, 0.63f));
                    tile.gameObject.AddComponent<Button>().onClick.AddListener(() => App.Show(new CodexScreen(open ? null : id)));
                }
            }
            if (row != null) for (; col % 3 != 0; col++) Ui.Size(Ui.Rect(row, "Pad"), 340, -1, -1, 1);
            if (_cat == "all" || _cat == "land" || _cat == "merc")
            {
                var later = Ui.Card(body, "Later", 16, 4);
                Ui.Label(later, T("ui.codex.later"), 22, TextAnchor.MiddleCenter, Palette.TextDim);
            }
        }

        // ---- 찾은 것 자세히 ----
        private void BuildDetail(RectTransform body, List<CodexEntry> codex)
        {
            SecretDef def = null;
            CodexEntry entry = null;
            for (int i = 0; i < codex.Count; i++) if (codex[i].SecretId == _detail) { entry = codex[i]; def = Ctx.Defs.Secrets[i]; }
            if (def == null) { Ui.Label(body, T("ui.codex.unknown"), Ui.FontSmall); return; }
            var card = Ui.Card(body, "Detail", 24, 10);
            card.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            var stamp = Ui.Rect(card, "Stamp");
            Ui.Size(stamp, 230, 230);
            Ui.Background(stamp, Seal).sprite = UiShapes.Circle;
            Ui.Fill(Ui.Label(stamp, def.RewardSkill != null ? "技" : "性", 100, TextAnchor.MiddleCenter, Color.white).rectTransform);
            Ui.Label(card, SecretText.Name(Ctx, def.Id), Ui.FontTitle - 14, TextAnchor.MiddleCenter);
            var tags = Ui.Row(card, "Tags", 48, 8);
            Ui.Spacer(tags, 0, 1);
            Ui.Size(Ui.Tag(tags, CatIcon(def.Category) + " " + T("ui.codex.cat." + def.Category), Palette.TextDim, 20, 44), width: 180);
            var find = entry.Find;
            if (find?.StepId != null) Ui.Size(Ui.Tag(tags, T("step." + find.StepId + ".name"), Palette.Ally, 20, 44), width: 260);
            if (find != null && find.AtUnixSeconds > 0) Ui.Size(Ui.Tag(tags, System.DateTimeOffset.FromUnixTimeSeconds(find.AtUnixSeconds).ToLocalTime().ToString("yyyy.MM.dd"), Palette.TextDim, 20, 44), width: 200);
            Ui.Spacer(tags, 0, 1);

            var got = Ui.Card(body, "Got", 18, 6);
            Ui.Label(got, T("ui.codex.got"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Label(got, SecretText.Reward(Ctx, def), 26);
            if (find?.GeneralId != null) Ui.Label(got, T("ui.codex.receiver") + "  " + Ui.Colored(Ctx.GeneralName(find.GeneralId), Palette.Ally), 24);
            var cond = Ui.Card(body, "Condition", 18, 6);
            Ui.Label(cond, T("ui.codex.condition"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Label(cond, T("secret.condition." + def.Id), 24);
            Ui.Button(body, T("ui.common.close"), () => App.Show(new CodexScreen()), 96, fontSize: 28);
        }

        // ---- 장수 도감 ----
        private void BuildGenerals(RectTransform header, RectTransform body, List<GeneralCodexEntry> gens)
        {
            var chips = new List<(string, string)> { ("all", T("ui.codex.cat.all")) };
            foreach (var gr in Ctx.Defs.Gacha.Grades) chips.Add((gr.Id, T("grade." + gr.Id)));
            Ui.SegmentTabs(header, chips, _grade, id => { _grade = id; App.Show(new CodexScreen()); }, 58, 150);
            gens.Sort((a, b) =>
            {
                int ra = Ctx.Defs.Gacha.Grade(a.Grade)?.Rank ?? -1, rb = Ctx.Defs.Gacha.Grade(b.Grade)?.Rank ?? -1;
                if (ra != rb) return rb.CompareTo(ra);
                if (a.Owned != b.Owned) return a.Owned ? -1 : 1;
                return string.CompareOrdinal(a.GeneralId, b.GeneralId);
            });
            RectTransform row = null;
            int col = 0;
            foreach (var g in gens)
            {
                if (_grade != "all" && g.Grade != _grade) continue;
                if (col % 4 == 0) row = Ui.Row(body, "Row" + col, 330, 10);
                col++;
                var color = GradeLabel.ColorOf(g.Grade);
                var card = Ui.Card(row, g.GeneralId, 8, 4);
                Ui.Size(card, 330, -1, -1, 1);
                card.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
                var outline = card.gameObject.AddComponent<Outline>();
                outline.effectColor = color;
                outline.effectDistance = new Vector2(4, -4);
                var pic = Ui.Rect(card, "Pic");
                Ui.Size(pic, 160, 160);
                if (g.Owned) new Portrait(pic, 160).Show(g.GeneralId, Ctx.GeneralName(g.GeneralId), color);
                else
                {
                    Ui.Background(pic, Dark).sprite = UiShapes.Circle;
                    Ui.Fill(Ui.Label(pic, "?", 60, TextAnchor.MiddleCenter, new Color(0.5f, 0.47f, 0.44f)).rectTransform);
                }
                Ui.Label(card, g.Owned ? Ctx.GeneralName(g.GeneralId) : "???", 22, TextAnchor.MiddleCenter, g.Owned ? Palette.Text : Palette.TextDim);
                var src = new List<string>();
                foreach (var s in g.Sources) src.Add(T("ui.codex.src." + s));
                Ui.Label(card, string.Join(" · ", src), 18, TextAnchor.MiddleCenter, Palette.TextDim);
                if (g.Owned)
                {
                    string gid = g.GeneralId;
                    card.gameObject.AddComponent<Button>().onClick.AddListener(() => App.Show(new GeneralScreen(gid)));
                }
            }
            if (row != null) for (; col % 4 != 0; col++) Ui.Size(Ui.Rect(row, "Pad"), 330, -1, -1, 1);
            var more = Ui.Card(body, "More", 16, 6);
            Ui.Label(more, T("ui.codex.missing"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Button(more, T("ui.codex.toGacha"), () => App.Show(new GachaScreen()), 76, fontSize: 26);
        }
    }
}
