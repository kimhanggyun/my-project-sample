using System.Collections.Generic;
using Game.Flow;
using Game.Services;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 장수 모집 (12 시안, generals.md 5장, R-51 · R-100).
    /// 고정 머리([‹] · 제목 · 보석 칩 + [+]) · 배너 탭 · 배너 그림(대표 장수) · 보장 · 천장 막대 · 확률 줄(늘 보임, 데이터 그대로) · 무료 1회(하루 1번, 광고 보고) · 아래 고정 [1회] [10회].
    /// 결과는 카드 10장을 한 장씩 뒤집는다 (+ [모두 보기]). 확률 판은 등급별 · 장수별 표. 결과는 규칙(서버와 같은 시드)이 정하고 화면은 연출만.
    /// </summary>
    public sealed class GachaScreen : GameScreen
    {
        private enum Mode { Main, Rates, Result }

        private readonly string _bannerId;
        private readonly GachaResult _last;
        private readonly Mode _mode;
        private readonly bool[] _open;
        private readonly string _notice;

        public GachaScreen(string bannerId = "standard", GachaResult last = null, bool showRates = false, bool[] open = null, string notice = null)
        {
            _bannerId = bannerId;
            _last = last;
            _mode = showRates ? Mode.Rates : last != null && last.Ok ? Mode.Result : Mode.Main;
            _open = open ?? (last != null ? new bool[last.Pulls.Count] : null);
            _notice = notice ?? (last != null && !last.Ok ? Reason(last.Reason) : null);
        }

        private string Reason(string r) => r == GachaService.NoFree ? T("ui.gacha.noFree") : T("ui.gacha.notEnough");

        public override void Build()
        {
            var g = Ctx.Defs.Gacha;
            if (!g.Banners.TryGetValue(_bannerId, out var b)) { App.ShowMain("general"); return; }
            var (header, body, footer) = Ui.PageLayout(Root, 170);

            var top = Ui.Row(header, "Title", 90, 12);
            Ui.Size(Ui.Button(top, "‹", Back, 84, fontSize: 40), width: 90);
            Ui.Size(Ui.Label(top, T(_mode == Mode.Result ? "ui.gacha.result" : _mode == Mode.Rates ? "ui.gacha.ratesHead" : "ui.gacha.title"), Ui.FontTitle - 6), flexibleWidth: 1);
            Ui.Size(Ui.ResourceChip(Ctx, top, b.Currency, Ctx.Resource(b.Currency), () => App.ShowMain("shop"), 74), width: 280);
            InfoButton.Add(Root, T("ui.gacha.note"));
            if (_notice != null) Ui.Label(header, Ui.Colored(_notice, Palette.Bad), 24);

            if (_mode == Mode.Result) BuildResult(body, footer, b);
            else if (_mode == Mode.Rates) BuildRates(body, footer, b);
            else BuildMain(header, body, footer, b);
        }

        private void Back()
        {
            if (_mode == Mode.Main) App.ShowMain("general");
            else App.Show(new GachaScreen(_bannerId));
        }

        // ---- 모집 ----
        private void BuildMain(RectTransform header, RectTransform body, RectTransform footer, Game.Data.Defs.GachaBannerDef b)
        {
            var g = Ctx.Defs.Gacha;
            var tabs = new List<(string, string)>();
            foreach (var kv in g.Banners) tabs.Add((kv.Key, T(kv.Value.NameKey)));
            Ui.SegmentTabs(header, tabs, _bannerId, id => App.Show(new GachaScreen(id)), 60, 220);

            // 배너 그림: 높은 등급 대표 장수 (가운데 가장 높은 등급, 양옆 다음)
            var pool = new List<string>(b.Pool);
            pool.Sort((x, y) => Rank(y).CompareTo(Rank(x)));
            var art = Ui.Card(body, "Banner", 16, 8);
            Ui.Round(Ui.Background(art, new Color(0.16f, 0.15f, 0.14f)));
            var faces = Ui.Row(art, "Faces", 300, 16);
            Ui.Spacer(faces, 0, 1);
            int[] order = { 1, 0, 2 };
            foreach (int i in order)
            {
                if (i >= pool.Count) continue;
                string id = pool[i];
                float size = i == 0 ? 280 : 190;
                var col = Ui.Rect(faces, "Face");
                Ui.Size(col, 300, size);
                Ui.Column(col, 4, 0).childAlignment = TextAnchor.MiddleCenter;
                var pic = Ui.Rect(col, "Pic");
                Ui.Size(pic, size, size);
                string grade = Ctx.Defs.Generals[id].Grade;
                new Portrait(pic, size).Show(id, Ctx.GeneralName(id), GradeLabel.ColorOf(grade));
                Ui.Tag(col, T("grade." + grade), GradeLabel.ColorOf(grade), 20, 36);
            }
            Ui.Spacer(faces, 0, 1);
            Ui.Button(art, T("ui.gacha.poolCount", b.Pool.Count), () => App.Show(new GachaScreen(_bannerId, null, true)), 60, new Color(0.22f, 0.21f, 0.2f), 24);

            // 보장 · 천장
            var pity = Ui.Row(body, "Pity", 70, 10);
            if (b.TenGuarantee != null) Ui.Size(Ui.Tag(pity, T("ui.gacha.guarantee", T("grade." + b.TenGuarantee)), GradeLabel.ColorOf(b.TenGuarantee), 22, 50), width: 240);
            if (b.PityGrade != null)
            {
                Ctx.State.GachaPity.TryGetValue(b.Id, out int since);
                Ui.Size(Ui.Label(pity, Ui.Colored("★", GradeLabel.ColorOf(b.PityGrade)), 40, TextAnchor.MiddleCenter), width: 50);
                var gauge = Ui.Gauge(pity, GradeLabel.ColorOf(b.PityGrade), 24);
                Ui.Size(gauge, flexibleWidth: 1);
                Ui.SetGauge((RectTransform)gauge.GetChild(0), b.PityCount <= 0 ? 0f : Mathf.Clamp01((float)since / b.PityCount));
                Ui.Size(Ui.Label(pity, since + " / " + b.PityCount, 24, TextAnchor.MiddleRight), width: 140);
            }

            // 확률 줄 (늘 보임)
            var rates = Ui.Card(body, "Rates", 14, 6);
            var bar = Ui.Row(rates, "Bar", 26, 0);
            foreach (var gr in Ctx.Defs.Gacha.Grades)
            {
                if (gr.Rate.Raw <= 0) continue;
                var seg = Ui.Rect(bar, gr.Id);
                Ui.Background(seg, GradeLabel.ColorOf(gr.Id));
                Ui.Size(seg, 26, 0, -1, Mathf.Max(0.01f, gr.Rate.Raw / 10000f));
            }
            var legend = Ui.Row(rates, "Legend", 50, 8);
            var parts = new List<string>();
            foreach (var gr in Ctx.Defs.Gacha.Grades) parts.Add(Ui.Colored("■", GradeLabel.ColorOf(gr.Id)) + T("grade." + gr.Id) + " " + Percent(gr.Rate.Raw) + "%");
            Ui.Size(Ui.Label(legend, string.Join("  ", parts), 20), flexibleWidth: 1);
            Ui.Size(Ui.Button(legend, T("ui.gacha.ratesMore"), () => App.Show(new GachaScreen(_bannerId, null, true)), 50, fontSize: 22), width: 160);

            // 무료 1회
            if (b.FreeDaily > 0)
            {
                long now = Ctx.Clock.UtcNow.ToUnixTimeSeconds();
                int left = GachaService.FreeLeft(Ctx.State, b, now);
                var free = Ui.Card(body, "Free", 14, 4);
                Ui.Round(Ui.Background(free, new Color(0.88f, 0.94f, 0.86f)));
                var row = Ui.Row(free, "Row", 70, 10);
                Ui.Size(Ui.Label(row, "▶", 34, TextAnchor.MiddleCenter, Palette.Good), width: 50);
                string sub = left > 0 ? T(AdFree(now) ? "ui.gacha.freeNoteNoAd" : "ui.gacha.freeNote") : T("ui.gacha.freeNext", Until(now));
                Ui.Size(Ui.Label(row, T("ui.gacha.free") + "  " + Ui.Colored(sub, Palette.TextDim), 24), flexibleWidth: 1);
                var get = Ui.Button(row, T("ui.gacha.freeGet"), () => Free(b.Id, now), 64, Palette.Good, 24);
                Ui.Size(get, width: 215);
                Ui.SetEnabled(get, left > 0);
            }

            DrawButtons(footer, b, false);
        }

        private void DrawButtons(RectTransform footer, Game.Data.Defs.GachaBannerDef b, bool againOnly)
        {
            long have = Ctx.Resource(b.Currency);
            if (!againOnly)
            {
                var one = Ui.Button(footer, T("ui.gacha.one", b.CostOne.ToString("N0")), () => Draw(1), 150, fontSize: 30);
                Ui.Size(one, width: 420);
                Ui.SetEnabled(one, have >= b.CostOne);
            }
            int off = b.CostOne > 0 ? (int)(100 - b.CostTen * 100 / (b.CostOne * 10)) : 0;
            var ten = Ui.Styled(Ui.PrimaryButton(footer, T(againOnly ? "ui.gacha.again" : "ui.gacha.ten", b.CostTen.ToString("N0")) + (off > 0 ? "  " + T("ui.gacha.discount", off) : ""), () => Draw(10)), "button.primary");
            Ui.Size(ten, flexibleWidth: 1);
            Ui.SetEnabled(ten, have >= b.CostTen);
        }

        private void Draw(int count) => App.Show(new GachaScreen(_bannerId, Ctx.DrawGacha(_bannerId, count)));

        private bool AdFree(long now) => Ctx.RewardedAd is OffRewardedAdService || Ctx.State.AdFreeForever || Ctx.State.AdFreeUntilUnixSeconds > now;

        /// <summary>무료 1회: 광고를 끝까지 보면 (광고가 꺼져 있거나 광고 제거면 바로) 모집 (R-100).</summary>
        private void Free(string bannerId, long now)
        {
            void Go() => App.Show(new GachaScreen(bannerId, Ctx.DrawGachaFree(bannerId)));
            if (AdFree(now)) { Go(); return; }
            if (!Ctx.RewardedAd.IsReady) { App.Show(new GachaScreen(bannerId, notice: T("ui.gacha.adNotReady"))); return; }
            Ctx.RewardedAd.Show("gacha.free", o =>
            {
                if (o == AdOutcome.Completed) Go();
                else App.Show(new GachaScreen(bannerId, notice: T("ui.gacha.adSkipped")));
            });
        }

        private static string Until(long now)
        {
            long s = PracticeService.SecondsPerDay - now % PracticeService.SecondsPerDay;
            return (s / 3600).ToString("00") + ":" + (s % 3600 / 60).ToString("00");
        }

        // ---- 결과: 한 장씩 뒤집기 ----
        private void BuildResult(RectTransform body, RectTransform footer, Game.Data.Defs.GachaBannerDef b)
        {
            var pulls = _last.Pulls;
            bool all = true;
            foreach (var o in _open) all &= o;
            for (int row = 0; row * 5 < pulls.Count; row++)
            {
                var line = Ui.Row(body, "Cards" + row, 400, 10);
                for (int i = row * 5; i < System.Math.Min(pulls.Count, row * 5 + 5); i++)
                {
                    var p = pulls[i];
                    int at = i;
                    var color = GradeLabel.ColorOf(p.Grade);
                    if (!_open[at])
                    {
                        var back = Ui.Button(line, "?", () => Flip(at), 400, new Color(0.18f, 0.17f, 0.16f), 72);
                        var outline = back.gameObject.AddComponent<Outline>();
                        outline.effectColor = color;
                        outline.effectDistance = new Vector2(6, -6);
                        continue;
                    }
                    var card = Ui.Card(line, "Card" + i, 8, 4);
                    Ui.Size(card, 400, -1, -1, 1);
                    card.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
                    var outl = card.gameObject.AddComponent<Outline>();
                    outl.effectColor = color;
                    outl.effectDistance = new Vector2(5, -5);
                    var pic = Ui.Rect(card, "Pic");
                    Ui.Size(pic, 170, 170);
                    new Portrait(pic, 170).Show(p.GeneralId, Ctx.GeneralName(p.GeneralId), color);
                    Ui.Label(card, Ctx.GeneralName(p.GeneralId), 24, TextAnchor.MiddleCenter);
                    Ui.Tag(card, T("grade." + p.Grade), color, 20, 36);
                    Ui.Label(card, p.Duplicate ? Ui.Colored(T("ui.gacha.shard"), Palette.TextDim) : Ui.Colored(T("ui.gacha.newTag"), Palette.Seal), 22, TextAnchor.MiddleCenter);
                    string gid = p.GeneralId;
                    var tap = card.gameObject.AddComponent<Button>();
                    tap.onClick.AddListener(() => App.Show(new GeneralScreen(gid)));
                }
            }
            if (!all)
            {
                Ui.Button(body, T("ui.gacha.revealAll"), () =>
                {
                    var open = new bool[_open.Length];
                    for (int i = 0; i < open.Length; i++) open[i] = true;
                    App.Show(new GachaScreen(_bannerId, _last, false, open));
                }, 76, fontSize: 26);
            }
            else
            {
                int shards = 0, fresh = 0;
                var byGrade = new Dictionary<string, int>();
                foreach (var p in pulls)
                {
                    byGrade.TryGetValue(p.Grade, out var n);
                    byGrade[p.Grade] = n + 1;
                    if (p.Duplicate) shards++; else fresh++;
                }
                var parts = new List<string>();
                foreach (var gr in Ctx.Defs.Gacha.Grades)
                    if (byGrade.TryGetValue(gr.Id, out var n)) parts.Add(Ui.Colored(T("grade." + gr.Id), GradeLabel.ColorOf(gr.Id)) + " " + n);
                var sum = Ui.Card(body, "Summary", 16, 4);
                Ui.Label(sum, string.Join("  ", parts), 24);
                Ui.Label(sum, T("ui.gacha.summary", fresh, shards), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            }
            Ui.Size(Ui.Button(footer, T("ui.common.ok"), () => App.Show(new GachaScreen(_bannerId)), 150, fontSize: 30), width: 420);
            DrawButtons(footer, b, true);
        }

        private void Flip(int at)
        {
            var open = (bool[])_open.Clone();
            open[at] = true;
            App.Show(new GachaScreen(_bannerId, _last, false, open));
        }

        // ---- 확률 판 (데이터 그대로) ----
        private void BuildRates(RectTransform body, RectTransform footer, Game.Data.Defs.GachaBannerDef b)
        {
            var svc = new GachaService(Ctx.Defs);
            var all = svc.Rates(b.Id);
            Ui.Label(body, T("ui.gacha.ratesTitle2", T(b.NameKey)), Ui.FontSmall);
            var grades = Ui.Card(body, "Grades", 16, 6);
            foreach (var gr in Ctx.Defs.Gacha.Grades)
            {
                int n = 0;
                foreach (var r in all) if (r.Grade == gr.Id) n++;
                var row = Ui.Row(grades, gr.Id, 44, 10);
                Ui.Size(Ui.Tag(row, T("grade." + gr.Id), GradeLabel.ColorOf(gr.Id), 20, 40), width: 120);
                var gauge = Ui.Gauge(row, GradeLabel.ColorOf(gr.Id), 20);
                Ui.Size(gauge, flexibleWidth: 1);
                Ui.SetGauge((RectTransform)gauge.GetChild(0), gr.Rate.Raw / 10000f);
                Ui.Size(Ui.Label(row, Percent(gr.Rate.Raw) + "%  ×" + n, 22, TextAnchor.MiddleRight), width: 200);
            }
            var table = Ui.Card(body, "Generals", 16, 4);
            foreach (var gr in Ctx.Defs.Gacha.Grades)
                foreach (var r in all)
                {
                    if (r.Grade != gr.Id) continue;
                    var row = Ui.Row(table, r.GeneralId, 64, 10);
                    var pic = Ui.Rect(row, "Pic");
                    Ui.Size(pic, 56, 56);
                    new Portrait(pic, 56).Show(r.GeneralId, Ctx.GeneralName(r.GeneralId), GradeLabel.ColorOf(r.Grade));
                    Ui.Size(Ui.Label(row, Ctx.GeneralName(r.GeneralId), 24), flexibleWidth: 1);
                    Ui.Size(Ui.Tag(row, T("grade." + r.Grade), GradeLabel.ColorOf(r.Grade), 18, 36), width: 100);
                    Ui.Size(Ui.Label(row, Percent(r.Rate.Raw) + "%", 22, TextAnchor.MiddleRight), width: 120);
                }
            var rules = Ui.Card(body, "Rules", 16, 4);
            if (b.TenGuarantee != null && b.PityGrade != null)
                Ui.Label(rules, T("ui.gacha.rules", T("grade." + b.TenGuarantee), T("grade." + b.PityGrade), b.PityCount, Ctx.State.GachaPity.TryGetValue(b.Id, out var s) ? s : 0), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Label(rules, T("ui.gacha.shardRule"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Size(Ui.Button(footer, T("ui.common.close"), () => App.Show(new GachaScreen(_bannerId)), 150, fontSize: 30), flexibleWidth: 1);
        }

        private int Rank(string generalId)
        {
            var gr = Ctx.Defs.Gacha.Grade(Ctx.Defs.Generals[generalId].Grade);
            return gr == null ? -1 : gr.Rank;
        }

        /// <summary>원본 정수(1 = 0.0001) → "0.5" 같은 % 글자.</summary>
        private static string Percent(long raw) => (raw / 100m).ToString("0.##");
    }
}
