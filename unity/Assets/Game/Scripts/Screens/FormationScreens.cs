using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 부대 편성 탭 (시안 07, army-v2.md 4장): 고정 머리(상황 탭 · [복사 ▾] · [군단 편성 ›] · 계급 띠 · 보유 병력 미니 카드 줄) · 접는 군단 카드(장수·진형·진형 미리보기·병종 칸) ·
    /// 고정 아래([자동 편성] [되돌리기] [저장]). 편집은 사본(<see cref="FormationEditor"/>)에 하고, 검사(R-5)를 통과해야 저장된다. [군단 편성]은 같은 탭 안의 포진판 페이지.
    /// </summary>
    public sealed class FormationTabView : ITabView, ILeaveGuard
    {
        internal const long Step = 100;
        /// <summary>접은 군단 번호 (기본은 모두 펼침).</summary>
        private static readonly HashSet<int> Collapsed = new HashSet<int>();
        /// <summary>군단 편성(포진판) 페이지를 보고 있나.</summary>
        internal static bool BoardOpen;
        private static string _notice;

        public void Build(GameApp app, RectTransform root)
        {
            UiStyle.Page = "formation";
            if (BoardOpen) { CorpsBoardView.Build(app, root); return; }
            var ctx = app.Context;
            var ed = ctx.Formation;
            var rank = Army.Rank(ctx.Defs, ctx.State);
            var (header, body, footer) = Ui.PageLayout(root);

            // ---- 고정 머리 ----
            var top = Ui.Row(header, "Top", 58, 8);
            var th = top.GetComponent<HorizontalLayoutGroup>();
            th.childForceExpandWidth = false;
            th.childAlignment = TextAnchor.MiddleLeft;
            var tabs = new List<(string, string)> { ("", ctx.T("ui.formation.default")) };
            foreach (var s in ctx.Defs.Campaign.FormationSituations) tabs.Add((s, ctx.T("situation." + s)));
            Ui.SegmentTabs(top, tabs, ed.Situation ?? "", id => Leave(app, () => { ctx.SelectFormationSituation(id == "" ? null : id); app.ShowMain("formation"); }), 58, 120);
            Ui.Size(Ui.Rect(top, "Gap"), 58, -1, -1, 1);
            var copy = Ui.SecondaryButton(top, ctx.T("ui.formation.copy") + " ▾", null, 58, 24);
            Ui.Size(copy, 58, 130).flexibleWidth = 0;
            var copyRt = (RectTransform)copy.transform;
            copy.onClick.AddListener(() =>
            {
                var items = new List<(string, string, bool, System.Action)>();
                foreach (var (id, label) in tabs)
                {
                    string target = id == "" ? null : id;
                    if (target == ed.Situation) continue;
                    items.Add((label, null, true, () => { _notice = ed.CopyTo(target) ? ctx.T("ui.formation.copied", label) : ctx.T("ui.formation.copyFailed"); app.ShowMain("formation"); }));
                }
                Ui.PickMenu(root, copyRt, 360, items.ToArray());
            });
            var boardBtn = Ui.SecondaryButton(top, ctx.T("ui.formation.board") + " ›", () => { BoardOpen = true; app.ShowMain("formation"); }, 58, 24);
            Ui.Size(boardBtn, 58, 200).flexibleWidth = 0;

            int usedPicks = 0;
            foreach (var c in ed.Draft) usedPicks = Mathf.Max(usedPicks, Army.OwnPicks(c));
            Ui.RankBar(ctx, header, root, ed.Draft.Count, usedPicks);
            Ui.Size(Ui.Label(header, ctx.T("ui.formation.troopsHeader"), 22, TextAnchor.MiddleLeft, Palette.TextDim), 30);
            var troopsRow = Ui.HScrollRow(header, "Troops", 170, 10, 6);
            foreach (var u in ctx.State.Units)
            {
                long used = 0;
                foreach (var c in ed.Draft) foreach (var p in c.Picks) if (p.UnitId == u.UnitId) used += p.Troops;
                long left = u.Troops - used;
                if (u.Troops <= 0 && used <= 0) continue;
                string unitId = u.UnitId;
                Ui.MiniCard(ctx, troopsRow, u.TroopType, null, TroopName(ctx, u.TroopType), left.ToString("N0") + " / " + u.Troops.ToString("N0"),
                    left < 0 ? Ui.MiniState.Warn : left == 0 ? Ui.MiniState.Dim : Ui.MiniState.Normal, () => QuickAdd(app, unitId, left));
            }
            Ui.MoreHint((RectTransform)troopsRow.parent.parent, 170, Palette.Background);
            if (_notice != null) { Ui.Hint(header, _notice, 40); _notice = null; }

            // ---- 군단 카드 ----
            var errors = ed.Errors();
            for (int i = 0; i < ed.Draft.Count; i++) CorpsCard(app, root, body, i, errors);
            if (ed.Draft.Count < rank.Corps && ed.FreeGenerals().Count > 0)
                Ui.SecondaryButton(body, ctx.T("ui.formation.addCorps"), () => { ed.AddCorps(); app.ShowMain("formation"); }, 90, 28);
            var moreCorps = NextRankWith(ctx, r => r.Corps > rank.Corps);
            if (moreCorps != null)
                Ui.ListRow(body, "@lock", Palette.TextDim, ctx.T("ui.formation.lockedCorps", rank.Corps + 1, ctx.T("rank." + moreCorps.Id), moreCorps.MinLevel), null, null, dim: true, height: 84);
            if (ed.Situation != null && !ed.HasPreset) Ui.Hint(body, ctx.T("ui.formation.usingDefault"), 50);
            if (errors.Count > 0) Ui.ErrorLine(body, errors[0] + (errors.Count > 1 ? ctx.T("ui.formation.moreErrors", errors.Count - 1) : ""), 60);

            // ---- 고정 아래 ----
            Ui.SecondaryButton(footer, ctx.T("ui.formation.autoFill"), () => { ed.AutoFill(); app.ShowMain("formation"); }, 92, 28);
            var revert = Ui.SecondaryButton(footer, ctx.T("ui.formation.revert"), () => { ctx.DiscardFormationDraft(); app.ShowMain("formation"); }, 92, 28);
            Ui.SetEnabled(revert, ed.Changed() && ed.HasPreset);
            var save = Ui.PrimaryButton(footer, ctx.T("ui.formation.save"), () => Save(app), 92, 32);
            Ui.SetPrimaryEnabled(save, errors.Count == 0 && ed.Changed());
            Ui.Size(save, 92, -1, -1, 2);
        }

        private static RankDef NextRankWith(GameContext ctx, System.Func<RankDef, bool> pred)
        {
            foreach (var r in ctx.Defs.Campaign.Ranks) if (r.MinLevel > ctx.State.LordLevel && pred(r)) return r;
            return null;
        }

        /// <summary>미니 카드 누름: 펼친 첫 군단에 그 병종 칸을 넣는다 (남은 인원·통솔 한도 안에서).</summary>
        private static void QuickAdd(GameApp app, string unitId, long left)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            for (int i = 0; i < ed.Draft.Count; i++)
            {
                if (Collapsed.Contains(i)) continue;
                long room = ed.Cap(i) - ed.Troops(i);
                var have = ed.Draft[i].Picks.Find(p => p.UnitId == unitId);
                long n = System.Math.Min(System.Math.Max(0, left), room) + (have?.Troops ?? 0);
                if (n > 0 && ed.SetPick(i, unitId, n)) { app.ShowMain("formation"); return; }
            }
            _notice = ctx.T("ui.formation.quickAddFailed");
            app.ShowMain("formation");
        }

        private static void CorpsCard(GameApp app, RectTransform root, RectTransform body, int index, List<string> errors)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var c = ed.Draft[index];
            var rank = Army.Rank(ctx.Defs, ctx.State);
            long troops = ed.Troops(index), cap = ed.Cap(index);
            bool warn = troops > cap || errors.Exists(e => e.Contains("'" + c.GeneralId + "'"));
            bool expanded = !Collapsed.Contains(index);
            var (head, cbody) = Ui.CollapsibleCard(body, "Corps" + index, expanded, () => { if (!Collapsed.Remove(index)) Collapsed.Add(index); app.ShowMain("formation"); }, warn);

            var pr = new Portrait(head, expanded ? 90 : 76);
            pr.Show(c.GeneralId, ctx.GeneralName(c.GeneralId), Palette.Ally);
            var g = ctx.State.FindGeneral(c.GeneralId);
            var texts = Ui.Rect(head, "Texts");
            Ui.Size(texts, 90, -1, -1, 1);
            Ui.Column(texts, 0, 0);
            string spot = c.BoardRow.HasValue ? ctx.T("ui.board.spot." + c.BoardRow + c.BoardCol) : "";
            Ui.Label(texts, ctx.T("ui.formation.corpsNo", index + 1) + (c.Commander || (index == 0 && !ed.Draft.Exists(x => x.Commander)) ? " · ★ " + ctx.T("ui.board.commander") : "") + (spot != "" ? " · " + spot : ""), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            var nameT = Ui.Label(texts, ctx.GeneralName(c.GeneralId) + "  " + Ui.Colored("Lv." + (g?.Level ?? 1), Palette.TextDim), 32);
            nameT.fontStyle = FontStyle.Bold;
            if (!expanded)
            {
                var parts = new List<string>();
                foreach (var p in c.Picks) { var u = ctx.State.FindUnit(p.UnitId); parts.Add((u == null ? p.UnitId : TroopName(ctx, u.TroopType)) + " " + p.Troops.ToString("N0")); }
                Ui.Label(texts, FormationName(ctx, c.Formation) + " · " + ctx.T("role." + Army.RoleOf(c)) + "  " + string.Join(" + ", parts), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            }
            var right = Ui.Rect(head, "Right");
            Ui.Size(right, 90, 260).flexibleWidth = 0;
            Ui.Column(right, 0, 0);
            Ui.Label(right, troops.ToString("N0") + " / " + cap.ToString("N0"), 24, TextAnchor.MiddleRight, troops > cap ? Palette.Bad : Palette.Text);
            Ui.Label(right, ctx.T("ui.formation.supplyPerDay", SupplyPerDay(ctx, c).ToString("N0")), 22, TextAnchor.MiddleRight, Palette.TextDim);
            if (!expanded) return;

            // 진형 · 역할
            var formRow = Ui.Row(cbody, "Form", 62, 10);
            var fr = formRow.GetComponent<HorizontalLayoutGroup>();
            fr.childForceExpandWidth = false;
            fr.childAlignment = TextAnchor.MiddleLeft;
            Ui.Size(Ui.SecondaryButton(formRow, FormationName(ctx, c.Formation) + " ▾", () => FormationPicker(app, root, index), 62, 28), 62, 220).flexibleWidth = 0;
            Ui.Size(Ui.Label(formRow, ctx.T("ui.formation.roleFromBoard", ctx.T("role." + Army.RoleOf(c))), 24, TextAnchor.MiddleLeft, Palette.TextDim), 62, -1, -1, 1);

            // 진형 미리보기 + 병종 칸
            var mid = Ui.Row(cbody, "Mid", 10, 14);
            var mh = mid.GetComponent<HorizontalLayoutGroup>();
            mh.childForceExpandWidth = false;
            mh.childForceExpandHeight = false;
            mh.childAlignment = TextAnchor.UpperLeft;
            mid.GetComponent<LayoutElement>().minHeight = mid.GetComponent<LayoutElement>().preferredHeight = -1;
            var picks = new List<(string, string, long)>();
            foreach (var p in c.Picks) { var u = ctx.State.FindUnit(p.UnitId); if (u != null) picks.Add((p.UnitId, u.TroopType, p.Troops)); }
            Ui.FormationPreview(ctx, mid, c.Formation, Army.Layout(ctx.Defs, picks), 380, 320, () => FormationPicker(app, root, index));
            var pickCol = Ui.Rect(mid, "Picks");
            Ui.Size(pickCol, -1, -1, -1, 1);
            Ui.Column(pickCol, 10, 0);
            foreach (var p in c.Picks.ToArray()) PickRow(app, root, pickCol, index, p);
            if (Army.OwnPicks(c) < rank.Slots)
                Ui.SecondaryButton(pickCol, ctx.T("ui.formation.addPick"), () => app.Show(new UnitPickerScreen(index, null)), 90, 26);
            var moreSlots = NextRankWith(ctx, r => r.Slots > rank.Slots);
            if (moreSlots != null)
                Ui.ListRow(pickCol, "@lock", Palette.TextDim, ctx.T("ui.formation.lockedPick", rank.Slots + 1, ctx.T("rank." + moreSlots.Id), moreSlots.MinLevel), null, null, dim: true, height: 70);

            long max = System.Math.Max(1, cap);
            Ui.GaugeRow(cbody, ctx.T("ui.formation.corpsTroops"), (float)troops / max, troops > cap ? Palette.Bad : new Color(0.247f, 0.435f, 0.69f), troops.ToString("N0") + " / " + cap.ToString("N0"), barWidth: 560);
        }

        private static long SupplyPerDay(GameContext ctx, CorpsComposition c)
        {
            long d = 0;
            foreach (var p in c.Picks)
            {
                var u = ctx.State.FindUnit(p.UnitId);
                if (u != null && ctx.Defs.TroopTypes.TryGetValue(u.TroopType, out var t)) d += p.Troops * t.SupplyPerDay;
            }
            return d;
        }

        /// <summary>병종 칸 한 줄: 병종 그림 · 이름 · 남은 · [−] 인원 [+] [최대] [빼기]. 길게 누르면 빨리, 인원을 누르면 직접 입력.</summary>
        private static void PickRow(GameApp app, RectTransform root, RectTransform parent, int index, TroopPick p)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var u = ctx.State.FindUnit(p.UnitId);
            string unitId = p.UnitId;
            long free = (u?.Troops ?? 0) - ed.UsedElsewhere(index, unitId);
            long room = ed.Cap(index) - ed.Troops(index) + p.Troops;
            long most = System.Math.Max(0, System.Math.Min(free, room));
            var card = Ui.Rect(parent, "Pick");
            Ui.Size(card, 136);
            Ui.Round(Ui.Background(card, new Color(0.98f, 0.965f, 0.925f)));
            Ui.Place(Ui.TroopPicture(ctx, card, u?.TroopType ?? unitId, 104), 12, 16, 104, 104);
            var name = Ui.Label(Ui.Place(Ui.Rect(card, "Name"), 128, 8, 220, 44), (u == null ? unitId : TroopName(ctx, u.TroopType)) + (p.Hired ? " " + ctx.T("ui.formation.hired") : ""), 28);
            name.fontStyle = FontStyle.Bold;
            Ui.Fill(name.rectTransform);
            var leftT = Ui.Label(Ui.Place(Ui.Rect(card, "Left"), 300, 8, 260, 44), ctx.T("ui.formation.left", (free - p.Troops).ToString("N0")), 22, TextAnchor.MiddleRight, free - p.Troops < 0 ? Palette.Bad : Palette.TextDim);
            Ui.Fill(leftT.rectTransform);
            var row = Ui.Place(Ui.Rect(card, "Controls"), 128, 66, 440, 56);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            void Set(long v) { ed.SetPick(index, unitId, System.Math.Max(1, System.Math.Min(most, v))); app.ShowMain("formation"); }
            var minus = Ui.Styled(Ui.SecondaryButton(row, "−", () => Set(p.Troops - Step), 56, 30), "button.small");
            Ui.Size(minus, 56, 56).flexibleWidth = 0;
            RepeatButton.Add(minus, () => Set(p.Troops - Step * 5));
            var num = Ui.SecondaryButton(row, p.Troops.ToString("N0"), () => NumberInput(app, root, index, unitId, p.Troops, most), 56, 28);
            Ui.Size(num, 56, 120).flexibleWidth = 0;
            num.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            var plus = Ui.Styled(Ui.SecondaryButton(row, "+", () => Set(p.Troops + Step), 56, 30), "button.small");
            Ui.Size(plus, 56, 56).flexibleWidth = 0;
            RepeatButton.Add(plus, () => Set(p.Troops + Step * 5));
            Ui.Size(Ui.Styled(Ui.SecondaryButton(row, ctx.T("ui.formation.max"), () => Set(most), 56, 22), "button.small"), 56, 82).flexibleWidth = 0;
            Ui.Size(Ui.Styled(Ui.SecondaryButton(row, ctx.T("ui.formation.remove"), () => { ed.SetPick(index, unitId, 0); app.ShowMain("formation"); }, 56, 22), "button.small"), 56, 82).flexibleWidth = 0;
        }

        /// <summary>인원 직접 입력 (아래 판).</summary>
        private static void NumberInput(GameApp app, RectTransform root, int index, string unitId, long now, long most)
        {
            var ctx = app.Context;
            var (content, close) = Ui.BottomSheet(root, ctx.T("ui.formation.numberTitle", most.ToString("N0")), 360);
            Ui.Column(content, 14, 0);
            var input = Ui.TextInput(content, now.ToString(), 7);
            input.contentType = InputField.ContentType.IntegerNumber;
            input.text = now.ToString();
            Ui.PrimaryButton(content, ctx.T("ui.common.ok"), () =>
            {
                long.TryParse(input.text, out var v);
                close();
                ctx.Formation.SetPick(index, unitId, System.Math.Max(0, System.Math.Min(most, v)));
                app.ShowMain("formation");
            }, 92, 30);
        }

        /// <summary>진형 고르기 (아래 판): 배운 진형마다 지금 병력의 미리보기 · 이름 · 특성 한 줄, 고르면 바로. 못 배운 진형은 이름만.</summary>
        private static void FormationPicker(GameApp app, RectTransform root, int index)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var c = ed.Draft[index];
            var (content, close) = Ui.BottomSheet(root, ctx.T("ui.formation.pickForm", ctx.T("ui.formation.corpsNo", index + 1)), 900);
            Ui.Column(content, 12, 0);
            var picks = new List<(string, string, long)>();
            foreach (var p in c.Picks) { var u = ctx.State.FindUnit(p.UnitId); if (u != null) picks.Add((p.UnitId, u.TroopType, p.Troops)); }
            var layout = Army.Layout(ctx.Defs, picks);
            var grid = Ui.Rect(content, "Grid");
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(330, 470);
            gl.spacing = new Vector2(12, 12);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 3;
            foreach (var fid in ctx.State.LearnedFormations)
            {
                string id = fid;
                bool now = id == c.Formation;
                var card = Ui.Rect(grid, "Form_" + id);
                var bg = Ui.Background(card, Palette.Card);
                Ui.Round(bg);
                if (now) { var o = card.gameObject.AddComponent<Outline>(); o.effectColor = Palette.ButtonActive; o.effectDistance = new Vector2(4, -4); }
                var b = card.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.onClick.AddListener(() => { close(); ed.SetFormation(index, id); app.ShowMain("formation"); });
                Ui.Column(card, 6, 12);
                Ui.FormationPreview(ctx, card, id, layout, 306, 290, null).GetComponent<Image>().raycastTarget = false;
                var n = Ui.Label(card, FormationName(ctx, id), 38);
                n.font = Ui.TitleFont;
                Ui.Label(card, FormationStatLine(ctx, id), 22, TextAnchor.UpperLeft, Palette.TextDim);
                Ui.Label(card, now ? ctx.T("ui.formation.formNow") : ctx.T("ui.formation.formLearned"), 22, TextAnchor.MiddleLeft, now ? Palette.ButtonActive : Palette.Good);
            }
            var locked = new List<string>();
            foreach (var f in ctx.Defs.Formations.Values) if (!ctx.State.LearnedFormations.Contains(f.Id)) locked.Add(ctx.T(f.NameKey));
            if (locked.Count > 0) Ui.Label(content, ctx.T("ui.formation.formLocked", string.Join(", ", locked)), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.SecondaryButton(content, ctx.T("ui.common.close"), close, 80, 28);
        }

        /// <summary>진형 특성 한 줄 (R-72 진형 특성치에서 0이 아닌 것).</summary>
        private static string FormationStatLine(GameContext ctx, string id)
        {
            if (!ctx.Defs.Formations.TryGetValue(id, out var f) || f.Stats == null) return "";
            var parts = new List<string>();
            void Add(string key, Game.Foundation.Math.Fix v) { long n = v.FloorToLong(); if (n != 0) parts.Add(ctx.T("ui.formation.stat." + key, (n > 0 ? "+" : "") + n)); }
            Add("breakthrough", f.Stats.BreakthroughPp);
            Add("envelop", f.Stats.EnvelopPp);
            Add("ranged", f.Stats.RangedPp);
            Add("frontDefense", f.Stats.FrontDefensePp);
            Add("flankDefense", f.Stats.FlankDefensePp);
            Add("moveSpeed", f.Stats.MoveSpeedPct);
            return string.Join(" · ", parts);
        }

        private static void Save(GameApp app)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            if (ed.Errors().Count == 0 && ctx.Do(PlayerActionRunner.SaveFormation(ed.Situation, ed.Draft))) { ctx.DiscardFormationDraft(); _notice = ctx.T("ui.formation.saved"); }
            app.ShowMain("formation");
        }

        // ---- 나갈 때 확인 (ILeaveGuard) ----
        public bool HasUnsaved(GameApp app) => app.Context.Formation.Changed() && app.Context.Formation.HasPreset;
        public void SaveForLeave(GameApp app) { var ed = app.Context.Formation; if (ed.Errors().Count == 0) app.Context.Do(PlayerActionRunner.SaveFormation(ed.Situation, ed.Draft)); app.Context.DiscardFormationDraft(); BoardOpen = false; }
        public void Discard(GameApp app) { app.Context.DiscardFormationDraft(); BoardOpen = false; }

        /// <summary>같은 탭 안에서 상황을 바꾸는 것처럼 고친 것을 버리는 일이 생기면 먼저 묻는다.</summary>
        private void Leave(GameApp app, System.Action go)
        {
            if (!HasUnsaved(app)) { go(); return; }
            MainScreen.AskLeave(app, this, go);
        }

        internal static string FormationName(GameContext ctx, string id) => id != null && ctx.Defs.Formations.TryGetValue(id, out FormationDef f) ? ctx.T(f.NameKey) : id;
        internal static string TroopName(GameContext ctx, string id) => ctx.Defs.TroopTypes.TryGetValue(id ?? "", out var t) ? ctx.T(t.NameKey) : id;
    }

    /// <summary>
    /// 군단 편성 (시안 07 두 번째 페이지, R-95): 위 장수 미니 카드(놓인 장수는 흐림 + "제n군", 상태 띠) · 3×3 포진판 (앞 선봉 · 가운데 측면 · 뒤 후위).
    /// 장수를 칸에 끌어 놓으면 군단이 생기고(계급 군단 수까지), 군단 카드를 다른 칸으로 끌면 자리를 바꾸고, 판 밖으로 끌면 뺀다. 군단 카드를 누르면 ★ 총대장.
    /// </summary>
    internal static class CorpsBoardView
    {
        private static string _notice;

        public static void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var rank = Army.Rank(ctx.Defs, ctx.State);
            // 자리가 없는 군단(예전 편성·새 군단)은 기본 자리부터 채워 보인다 (앞 가운데 → 가운데 왼·오른 → …)
            var spots = new[] { (0, 1), (1, 0), (1, 2), (0, 0), (0, 2), (2, 1), (1, 1), (2, 0), (2, 2) };
            for (int i = 0; i < ed.Draft.Count; i++)
                if (!ed.Draft[i].BoardRow.HasValue || !ed.Draft[i].BoardCol.HasValue)
                    foreach (var sp in spots)
                        if (!ed.Draft.Exists(c => c.BoardRow == sp.Item1 && c.BoardCol == sp.Item2)) { ed.SetBoard(i, sp.Item1, sp.Item2); break; }
            var (header, body, footer) = Ui.PageLayout(root);
            var top = Ui.Row(header, "Top", 76, 12);
            var th = top.GetComponent<HorizontalLayoutGroup>();
            th.childForceExpandWidth = false;
            th.childAlignment = TextAnchor.MiddleLeft;
            Ui.Size(Ui.SecondaryButton(top, "‹", () => { FormationTabView.BoardOpen = false; app.ShowMain("formation"); }, 72, 40), 72, 80).flexibleWidth = 0;
            var title = Ui.Label(top, ctx.T("ui.board.title"), 50);
            title.font = Ui.TitleFont;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(title, 76, -1, -1, 1);
            Ui.Size(Ui.Label(top, ctx.T("ui.board.count", ed.Draft.Count, rank.Corps, ctx.T("rank." + rank.Id)), 24, TextAnchor.MiddleRight, Palette.TextDim), 76, 360).flexibleWidth = 0;
            Ui.Size(Ui.Label(header, ctx.T("ui.board.generals"), 22, TextAnchor.MiddleLeft, Palette.TextDim), 30);

            var targets = new DropTargets<(int row, int col)>();
            // 장수 미니 카드 (여러 줄로 감싼다 — 끌기와 가로 스크롤이 부딪히지 않게)
            var gens = Ui.Rect(header, "Generals");
            var gl = gens.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(150, 178);
            gl.spacing = new Vector2(10, 10);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 6;
            foreach (var g in ctx.State.Generals)
            {
                int at = ed.Draft.FindIndex(c => c.GeneralId == g.GeneralId);
                bool deploy = GeneralStatus.CanDeploy(g.Status);
                string status = g.Status == GeneralStatus.Available ? null : ctx.T("ui.generals." + g.Status) + (deploy ? "" : " · " + ctx.T("ui.board.cannot"));
                Color sc = g.Status == GeneralStatus.LightInjury ? new Color(0.851f, 0.651f, 0.18f) : g.Status == GeneralStatus.SeriousInjury ? new Color(0.851f, 0.463f, 0.18f) : new Color(0.69f, 0.227f, 0.18f);
                var b = Ui.MiniCard(ctx, gens, null, g.GeneralId, ctx.GeneralName(g.GeneralId), "Lv." + g.Level, at >= 0 || !deploy ? Ui.MiniState.Dim : Ui.MiniState.Normal, null,
                    at >= 0 ? ctx.T("ui.formation.corpsNo", at + 1) : null, status, sc, 150, 178);
                if (at < 0 && deploy) Draggable(app, root, (RectTransform)b.transform, "g|" + g.GeneralId, targets);
            }

            var cells = Ui.DropGrid(body, 3, 3, 320, 250, ctx.T("ui.board.front"));
            for (int r = 0; r < 3; r++)
                for (int k = 0; k < 3; k++)
                {
                    targets.Add(cells[r, k], (r, k));
                    if (k == 0) { var rl = Ui.Label(Ui.Place(Ui.Rect(cells[r, k], "RowName"), 6, 2, 300, 26), ctx.T("ui.board.row." + r), 18, TextAnchor.MiddleLeft, Palette.TextDim); Ui.Fill(rl.rectTransform); }
                }
            for (int i = 0; i < ed.Draft.Count; i++)
            {
                var c = ed.Draft[i];
                if (!c.BoardRow.HasValue || !c.BoardCol.HasValue) continue;
                var cell = cells[Mathf.Clamp(c.BoardRow.Value, 0, 2), Mathf.Clamp(c.BoardCol.Value, 0, 2)];
                int index = i;
                var card = Ui.Rect(cell, "Corps");
                Ui.Fill(card);
                var bg = Ui.Background(card, Palette.Card);
                Ui.Round(bg);
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => { ed.SetCommander(index); app.ShowMain("formation"); });
                var pr = new Portrait(card, 100);
                pr.Rect.anchorMin = pr.Rect.anchorMax = new Vector2(0, 0.5f);
                pr.Rect.anchoredPosition = new Vector2(70, -6);
                pr.Show(c.GeneralId, ctx.GeneralName(c.GeneralId), Palette.Ally);
                if (c.Commander || (index == 0 && !ed.Draft.Exists(x => x.Commander))) { var tag = Ui.Place(Ui.Rect(card, "Star"), 10, 8, 130, 32); var ti = Ui.Background(tag, new Color(0.788f, 0.588f, 0.169f)); ti.sprite = UiShapes.Rounded(10); ti.type = Image.Type.Sliced; var tt = Ui.Label(tag, "★ " + ctx.T("ui.board.commander"), 20, TextAnchor.MiddleCenter, Ui.OnAccent); Ui.Fill(tt.rectTransform); }
                var n = Ui.Label(Ui.Place(Ui.Rect(card, "Name"), 130, 50, 170, 40), ctx.GeneralName(c.GeneralId), 28);
                n.fontStyle = FontStyle.Bold;
                Ui.Fill(n.rectTransform);
                var parts = new List<string>();
                foreach (var p in c.Picks) { var u = ctx.State.FindUnit(p.UnitId); parts.Add(u == null ? p.UnitId : FormationTabView.TroopName(ctx, u.TroopType)); }
                var sub = Ui.Label(Ui.Place(Ui.Rect(card, "Sub"), 130, 92, 170, 120), ctx.T("ui.formation.corpsNo", index + 1) + " · " + ctx.T("role." + Army.RoleOf(c)) + "\n" + string.Join("·", parts) + " " + ed.Troops(index).ToString("N0"), 20, TextAnchor.UpperLeft, Palette.TextDim);
                Ui.Fill(sub.rectTransform);
                foreach (var gph in card.GetComponentsInChildren<Graphic>()) if (gph != bg) gph.raycastTarget = false;
                Draggable(app, root, card, "c|" + index, targets);
            }
            Ui.Hint(body, _notice ?? ctx.T("ui.board.hint"), 60);
            _notice = null;
            Ui.PrimaryButton(footer, ctx.T("ui.board.done"), () => { FormationTabView.BoardOpen = false; app.ShowMain("formation"); }, 92, 32);
        }

        private static void Draggable(GameApp app, RectTransform root, RectTransform rt, string payload, DropTargets<(int row, int col)> targets)
        {
            var d = rt.gameObject.AddComponent<DragItem>();
            d.Payload = payload;
            d.GhostParent = root;
            d.Dropped += (pl, screen) => Drop(app, pl, screen, targets);
        }

        private static void Drop(GameApp app, string payload, Vector2 screen, DropTargets<(int row, int col)> targets)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var p = payload.Split('|');
            bool onBoard = targets.Find(screen, out var to);
            if (p[0] == "c")
            {
                int index = int.Parse(p[1]);
                if (onBoard) ed.SetBoard(index, to.row, to.col);
                else ed.RemoveCorps(index); // 판 밖 = 빼기
            }
            else if (onBoard)
            {
                string gid = p[1];
                int there = ed.Draft.FindIndex(c => c.BoardRow == to.row && c.BoardCol == to.col);
                if (there >= 0) ed.SetGeneral(there, gid); // 그 칸의 군단 장수를 바꾼다
                else
                {
                    var made = ed.AddCorps(gid);
                    if (made == null)
                    {
                        var rank = Army.Rank(ctx.Defs, ctx.State);
                        RankDef more = null;
                        foreach (var r in ctx.Defs.Campaign.Ranks) if (r.MinLevel > ctx.State.LordLevel && r.Corps > rank.Corps) { more = r; break; }
                        _notice = more == null ? ctx.T("ui.board.fullMax", ed.Draft.Count, rank.Corps) : ctx.T("ui.board.full", ed.Draft.Count, rank.Corps, ctx.T("rank." + more.Id), more.MinLevel, more.Corps);
                    }
                    else ed.SetBoard(ed.Draft.Count - 1, to.row, to.col);
                }
            }
            app.ShowMain("formation");
        }
    }

    /// <summary>군단 하나 편집: 장수·진형·역할, 소부대 칸 6개.</summary>
    public sealed class CorpsEditScreen : GameScreen
    {
        private readonly int _index;
        public CorpsEditScreen(int index) { _index = index; }

        public override void Build()
        {
            var ed = Ctx.Formation;
            if (_index >= ed.Draft.Count) { App.ShowMain("formation"); return; }
            var c = ed.Draft[_index];
            Ui.Column(Root, 10, 24);
            Ui.Size(Ui.Label(Root, T("ui.formation.corpsTitle", _index + 1), Ui.FontTitle), 100);
            long troops = ed.Troops(_index), cap = ed.Cap(_index);
            Ui.Size(Ui.Label(Root, T("ui.formation.troopsLine", Ui.Colored(troops.ToString("N0"), troops > cap ? Palette.Bad : Palette.Good), cap.ToString("N0")), Ui.FontSmall), 48);

            var body = Ui.ScrollList(Root, "Body", 10, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);

            // 장수: 출전 가능한 장수를 차례로 (다른 군단 장수면 맞바꿈)
            var generals = new List<string>();
            foreach (var g in Ctx.State.Generals) if (GeneralStatus.CanDeploy(g.Status)) generals.Add(g.GeneralId);
            Cycle(body, T("ui.formation.general", Ctx.GeneralName(c.GeneralId)), generals, c.GeneralId, v => ed.SetGeneral(_index, v));
            Cycle(body, T("ui.formation.formation", FormationTabView.FormationName(Ctx, c.Formation)), Ctx.State.LearnedFormations, c.Formation, v => ed.SetFormation(_index, v));
            Cycle(body, T("ui.formation.role", T("role." + c.Role)), new List<string>(FormationEditor.Roles), c.Role, v => ed.SetRole(_index, v));

            // 병종 칸 (army-v2.md 4장, 07 시안 전 임시 화면): 칸마다 병종·인원, [−500][+500], 바꾸기. 계급의 칸 수까지 [+ 병종 칸]
            Ui.Label(body, T("ui.formation.picks", c.Picks.Count, ed.MaxPicks), Ui.FontBody);
            foreach (var pick in c.Picks.ToArray())
            {
                string unitId = pick.UnitId;
                var u = Ctx.State.FindUnit(unitId);
                long free = u == null ? 0 : u.Troops - ed.UsedElsewhere(_index, unitId);
                var pickRow = Ui.Row(body, "Pick", 96, 8);
                var name = Ui.Button(pickRow, (u == null ? unitId : FormationTabView.TroopName(Ctx, u.TroopType)) + "  " + pick.Troops.ToString("N0") + " / " + free.ToString("N0"),
                    () => App.Show(new UnitPickerScreen(_index, unitId)), 96, Palette.Card, 30);
                Ui.Size(name, flexibleWidth: 3);
                long cur = pick.Troops;
                Ui.Size(Ui.Button(pickRow, "−500", () => { ed.SetPick(_index, unitId, System.Math.Max(0, cur - 500)); App.Show(new CorpsEditScreen(_index)); }, 96, fontSize: 26), width: 150);
                Ui.Size(Ui.Button(pickRow, "+500", () => { ed.SetPick(_index, unitId, System.Math.Min(free, cur + 500)); App.Show(new CorpsEditScreen(_index)); }, 96, fontSize: 26), width: 150);
            }
            if (c.Picks.Count < ed.MaxPicks) Ui.Button(body, T("ui.formation.addPick"), () => App.Show(new UnitPickerScreen(_index, null)), 90, Palette.Card, 30);
            foreach (var e in ed.Errors()) Ui.Label(body, Ui.Colored(e, Palette.Bad), 24);

            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(row, T("ui.common.back"), () => App.ShowMain("formation"));
            Ui.Button(row, T("ui.formation.removeCorps"), () => { ed.RemoveCorps(_index); App.ShowMain("formation"); }, color: Palette.Danger);
        }

        /// <summary>누를 때마다 다음 값으로.</summary>
        private void Cycle(Transform parent, string label, IReadOnlyList<string> values, string current, System.Action<string> set)
        {
            var b = Ui.Button(parent, label, () =>
            {
                if (values.Count == 0) return;
                int i = 0;
                for (int k = 0; k < values.Count; k++) if (values[k] == current) i = k;
                set(values[(i + 1) % values.Count]);
                App.Show(new CorpsEditScreen(_index));
            }, 96, fontSize: 30);
            Ui.SetEnabled(b, values.Count > 1);
        }
    }

    /// <summary>병종 칸에 넣을 병종 고르기 (army-v2.md 4장): 비우기 + 병종 묶음. 인원은 그 병종의 남은 인원과 장수 한도 남은 자리 중 작은 쪽.</summary>
    public sealed class UnitPickerScreen : GameScreen
    {
        private readonly int _index;
        /// <summary>바꿀 병종 칸의 병종 묶음 id (null = 새 칸).</summary>
        private readonly string _slot;
        public UnitPickerScreen(int index, string replaceUnitId) { _index = index; _slot = replaceUnitId; }

        public override void Build()
        {
            var ed = Ctx.Formation;
            Ui.Column(Root, 10, 24);
            Ui.Size(Ui.Label(Root, T("ui.formation.pickTitle", _index + 1, _slot == null ? T("ui.formation.addPick") : FormationTabView.TroopName(Ctx, Ctx.State.FindUnit(_slot)?.TroopType)), Ui.FontBody), 80);
            var list = Ui.ScrollList(Root, "Units", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            Ui.Button(list, T("ui.formation.clearSlot"), () => Pick(null), 90, Palette.Card, 30);
            foreach (var u in Ctx.State.Units)
            {
                string id = u.UnitId;
                int at = ed.CorpsOf(id);
                string where = at < 0 ? T("ui.formation.reserveUnit") : T("ui.formation.inCorps", at + 1, Ctx.GeneralName(ed.Draft[at].GeneralId));
                var b = Ui.Button(list, id + "  " + FormationTabView.TroopName(Ctx, u.TroopType) + "  " + u.Troops.ToString("N0") + "   " + Ui.Colored(where, Palette.TextDim),
                    () => Pick(id), 90, Palette.Card, 28);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("formation"));
        }

        private void Pick(string unitId)
        {
            var ed = Ctx.Formation;
            if (_slot != null) ed.SetPick(_index, _slot, 0);
            if (unitId != null)
            {
                var u = Ctx.State.FindUnit(unitId);
                long free = u == null ? 0 : u.Troops - ed.UsedElsewhere(_index, unitId);
                long room = ed.Cap(_index) - ed.Troops(_index);
                ed.SetPick(_index, unitId, System.Math.Max(1, System.Math.Min(free, room)));
            }
            App.ShowMain("formation");
        }
    }

    /// <summary>새 부대 창설: 기본 병종 고르기 (formation.md 4.1). 병력 0으로 생기고 징병·모병으로 채운다.</summary>
    public sealed class CreateUnitScreen : GameScreen
    {
        public override void Build()
        {
            var troops = new TroopService(Ctx.Defs);
            Ui.Column(Root, 12, 32);
            Ui.Size(Ui.Label(Root, T("ui.formation.createTitle"), Ui.FontTitle), 110);
            Ui.Label(Root, T("ui.formation.createNote"), 28, TextAnchor.MiddleLeft, Palette.TextDim);
            var list = Ui.ScrollList(Root, "Types", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            foreach (var id in troops.CreatableTroopTypes())
            {
                string t = id;
                Ui.Button(list, FormationTabView.TroopName(Ctx, t), () =>
                {
                    Ctx.Do(PlayerActionRunner.CreateUnit(t));
                    App.ShowMain("formation");
                }, 110, Palette.Card, 34);
            }
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("formation"));
        }
    }

    /// <summary>부대 해산 확인. 병력·부상병이 사라진다.</summary>
    public sealed class DisbandUnitScreen : GameScreen
    {
        private readonly string _unitId;
        public DisbandUnitScreen(string unitId) { _unitId = unitId; }

        public override void Build()
        {
            var u = Ctx.State.FindUnit(_unitId);
            if (u == null) { App.ShowMain("formation"); return; }
            Ui.Column(Root, 24, 48).childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(Ui.Label(Root, T("ui.formation.disbandTitle"), Ui.FontTitle, TextAnchor.MiddleCenter), 120);
            Ui.Label(Root, T("ui.formation.disbandConfirm", Ctx.UnitLabel(u.UnitId), FormationTabView.TroopName(Ctx, u.TroopType), (u.Troops + u.Wounded).ToString("N0")), Ui.FontBody, TextAnchor.MiddleCenter);
            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(row, T("ui.common.cancel"), () => App.ShowMain("formation"));
            Ui.Button(row, T("ui.formation.disband"), () =>
            {
                Ctx.Do(PlayerActionRunner.DisbandUnit(_unitId));
                App.ShowMain("formation");
            }, color: Palette.Danger);
        }
    }
}
