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

        /// <summary>고른 군단 (판에서 누른 칸, 앱이 켜져 있는 동안). 없으면 총대장 · 첫 군단.</summary>
        private static int _sel = -1;
        /// <summary>다시 그린 뒤 고른 군단 판을 다시 연다 (판 안에서 값을 바꿀 때).</summary>
        private static bool _openSheet;
        /// <summary>캡처 투어: 다음 그리기에 고른 군단 판을 연다.</summary>
        internal static bool OpenSheet { set => _openSheet = value; }
        /// <summary>캡처 투어: 고른 군단 (-1 = 총대장 · 첫 군단).</summary>
        internal static int Selected { set => _sel = value; }

        /// <summary>
        /// 편성 (2026-11-28 R-159 · 편성 새 방식 2단계): 위 = 제목 · 상황 · 계급 · 보유 병력 줄, 가운데 = 3 × 3 판 (앞 · 가운데 · 뒤 × 왼 · 가운데 · 오른),
        /// 칸마다 군단 카드 (장수 · 병종 그림 · 인원 · 진형 아이콘 · ★), 아래 = 고른 군단 (장수 · 병종 · 인원 · 진형 · ★ · 자동 · 빼기).
        /// 빈 칸을 누르면 그 자리에 군단 (계급 군단 수까지), 군단을 끌어 다른 칸으로 옮기고 판 밖으로 끌면 뺀다. 보유 병력 카드를 누르면 고른 군단의 병종으로.
        /// </summary>
        public void Build(GameApp app, RectTransform root)
        {
            UiStyle.Page = "formation";
            var ctx = app.Context;
            var ed = ctx.Formation;
            var rank = Army.Rank(ctx.Defs, ctx.State);
            if (_sel >= ed.Draft.Count) _sel = -1;
            if (_sel < 0 && ed.Draft.Count > 0) { _sel = ed.Draft.FindIndex(c => c.Commander); if (_sel < 0) _sel = 0; }
            // 자리가 없는 군단(예전 편성 · 새 군단)은 기본 자리부터 채워 보인다 (앞 가운데 → 가운데 왼 · 오른 → …)
            var spots = new[] { (0, 1), (1, 0), (1, 2), (0, 0), (0, 2), (2, 1), (1, 1), (2, 0), (2, 2) };
            for (int i = 0; i < ed.Draft.Count; i++)
                if (!ed.Draft[i].BoardRow.HasValue || !ed.Draft[i].BoardCol.HasValue)
                    foreach (var sp in spots)
                        if (!ed.Draft.Exists(c => c.BoardRow == sp.Item1 && c.BoardCol == sp.Item2)) { ed.SetBoard(i, sp.Item1, sp.Item2); break; }

            var (header, body, footer) = Ui.PageLayout(root);
            ScrollMemory.Keep(body, "formation." + (ed.Situation ?? ""));

            // ---- 고정 머리: 제목 줄 · 상황 세그먼트 · 계급 · 보유 병력 ----
            var top = Ui.HeaderRow(header, ctx.T("ui.seg.formation"), ctx.T("ui.formation.countOf", ed.Draft.Count, rank.Corps));
            var tabs = new List<(string, string)> { ("", ctx.T("ui.formation.default")) };
            foreach (var s in ctx.Defs.Campaign.FormationSituations) tabs.Add((s, ctx.T("situation." + s)));
            var copy = Ui.SecondaryButton(top, ctx.T("ui.formation.copy") + " ▾", null, 70, 26);
            Ui.Size(copy, 70, 150).flexibleWidth = 0;
            var copyRt = (RectTransform)copy.transform;
            copy.onClick.AddListener(() =>
            {
                var items = new List<(string, string, bool, System.Action)>();
                foreach (var (id, label) in tabs)
                {
                    string target = id == "" ? null : id;
                    if (target == ed.Situation) continue;
                    items.Add((label, null, true, () => { bool ok = ed.CopyTo(target); Toast.Show(ok ? ctx.T("ui.formation.copied", label) : ctx.T("ui.formation.copyFailed"), !ok); app.ShowMain("formation"); }));
                }
                Ui.PickMenu(root, copyRt, 360, items.ToArray());
            });
            Ui.Size(Ui.SecondaryButton(top, ctx.T("ui.formation.autoAll"), () => { ed.AutoFill(); _sel = -1; app.ShowMain("formation"); }, 70, 26), 70, 170).flexibleWidth = 0;
            Ui.Size(Ui.Rect(top, "InfoGap"), 70, 66).flexibleWidth = 0;
            InfoButton.Add(root, ctx.T("ui.formation.note"));
            Ui.MidTabs(header, tabs, ed.Situation ?? "", id => Leave(app, () => { ctx.SelectFormationSituation(id == "" ? null : id); _sel = -1; app.ShowMain("formation"); }));
            Ui.RankBar(ctx, header, root, ed.Draft.Count, ed.Draft.Count > 0 ? 1 : 0);
            Ui.Size(Ui.Label(header, ctx.T("ui.formation.troopsHeaderBoard"), 22, TextAnchor.MiddleLeft, Palette.TextDim), 30);
            var troopsRow = Ui.HScrollRow(header, "Troops", 170, 10, 6);
            foreach (var u in ctx.State.Units)
            {
                long used = 0;
                foreach (var c in ed.Draft) foreach (var p in c.Picks) if (p.UnitId == u.UnitId) used += p.Troops;
                long left = u.Troops - used;
                if (u.Troops <= 0 && used <= 0) continue;
                string unitId = u.UnitId;
                Ui.MiniCard(ctx, troopsRow, u.TroopType, null, TroopName(ctx, u.TroopType), left.ToString("N0") + " / " + u.Troops.ToString("N0"),
                    left < 0 ? Ui.MiniState.Warn : left == 0 ? Ui.MiniState.Dim : Ui.MiniState.Normal, () => UseTroop(app, unitId));
            }
            Ui.MoreHint((RectTransform)troopsRow.parent.parent, 170, Palette.Background);

            // ---- 3 × 3 판 ----
            var targets = new DropTargets<(int row, int col)>();
            var cells = Ui.DropGrid(body, 3, 3, 336, 236, ctx.T("ui.board.front"));
            for (int r = 0; r < 3; r++)
                for (int k = 0; k < 3; k++)
                {
                    int row = r, col = k;
                    targets.Add(cells[r, k], (r, k));
                    if (ed.Draft.Exists(c => c.BoardRow == r && c.BoardCol == k)) continue;
                    EmptyCell(app, cells[r, k], ctx.T("ui.board.spot." + r + k), ed.Draft.Count < rank.Corps, () => AddAt(app, row, col));
                }
            var errors = ed.Errors();
            for (int i = 0; i < ed.Draft.Count; i++)
            {
                var c = ed.Draft[i];
                var cell = cells[Mathf.Clamp(c.BoardRow ?? 0, 0, 2), Mathf.Clamp(c.BoardCol ?? 0, 0, 2)];
                bool warn = errors.Exists(e => e.Contains("'" + c.GeneralId + "'"));
                CorpsCell(app, root, cell, i, warn, targets);
            }
            Ui.Hint(body, ctx.T("ui.formation.boardHint"), 54);

            // ---- 고른 군단: 군단 칸을 누르면 아래에서 올라오는 판 (판을 가리지 않게, 2026-11-28 캡처) ----
            if (_openSheet && _sel >= 0 && _sel < ed.Draft.Count) SelectedSheet(app, root, _sel);
            _openSheet = false;
            var moreCorps = NextRankWith(ctx, r => r.Corps > rank.Corps);
            if (moreCorps != null)
                Ui.ListRow(body, "@lock", Palette.TextDim, ctx.T("ui.formation.lockedCorps", rank.Corps + 1, ctx.T("rank." + moreCorps.Id), moreCorps.MinLevel), null, null, dim: true, height: 84);
            if (ed.Situation != null && !ed.HasPreset) Ui.Hint(body, ctx.T("ui.formation.usingDefault"), 50);
            if (errors.Count > 0) Ui.ErrorLine(body, errors[0] + (errors.Count > 1 ? ctx.T("ui.formation.moreErrors", errors.Count - 1) : ""), 60);

            // ---- 고정 아래 ----
            var revert = Ui.SecondaryButton(footer, ctx.T("ui.formation.revert"), () => { ctx.DiscardFormationDraft(); _sel = -1; app.ShowMain("formation"); }, 92, 28);
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

        /// <summary>빈 칸: 아군 색 아주 옅게 + 얇은 테두리, 가운데 + (군단을 더 만들 수 없으면 흐리게), 위에 자리 이름.</summary>
        private static void EmptyCell(GameApp app, RectTransform cell, string spot, bool canAdd, System.Action add)
        {
            var team = Palette.Ally;
            var img = cell.GetComponent<Image>();
            if (img != null) img.color = new Color(team.r, team.g, team.b, canAdd ? 0.10f : 0.05f);
            var ring = Ui.Fill(Ui.Rect(cell, "Ring"));
            var ri = ring.gameObject.AddComponent<Image>();
            ri.sprite = UiKit.Ring(12, 3); ri.type = Image.Type.Sliced; ri.fillCenter = false; ri.raycastTarget = false;
            ri.color = new Color(team.r, team.g, team.b, canAdd ? 0.35f : 0.15f);
            var sp = Ui.Label(Ui.Place(Ui.Rect(cell, "Spot"), 10, 6, 300, 28), spot, 18, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Fill(sp.rectTransform);
            var plus = Ui.Label(cell, "+", 64, TextAnchor.MiddleCenter, new Color(team.r, team.g, team.b, canAdd ? 0.8f : 0.3f));
            Ui.Fill(plus.rectTransform);
            plus.raycastTarget = false;
            var b = cell.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); add(); });
        }

        /// <summary>빈 칸 누름: 그 자리에 첫 빈 장수로 군단을 만들고 (남은 병력이 가장 많은 병종 · 통솔 한도까지) 고른다. 계급 군단 수가 차면 알림.</summary>
        private static void AddAt(GameApp app, int row, int col)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var rank = Army.Rank(ctx.Defs, ctx.State);
            if (ed.Draft.Count >= rank.Corps)
            {
                var more = NextRankWith(ctx, r => r.Corps > rank.Corps);
                Toast.Show(more == null ? ctx.T("ui.board.fullMax", ed.Draft.Count, rank.Corps) : ctx.T("ui.board.full", ed.Draft.Count, rank.Corps, ctx.T("rank." + more.Id), more.MinLevel, more.Corps), bad: true);
                return;
            }
            var made = ed.AddCorps();
            if (made == null) { Toast.Show(ctx.T("ui.formation.noFreeGeneral"), bad: true); return; }
            int index = ed.Draft.Count - 1;
            ed.SetBoard(index, row, col);
            ed.AutoFillCorps(index);
            _sel = index;
            app.ShowMain("formation");
        }

        /// <summary>보유 병력 카드 누름: 고른 군단의 병종을 그 병종으로 (남은 인원 · 통솔 한도까지). 고른 군단이 없으면 알림.</summary>
        private static void UseTroop(GameApp app, string unitId)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            if (_sel < 0 || _sel >= ed.Draft.Count) { Toast.Show(ctx.T("ui.formation.pickCellFirst"), bad: true); return; }
            long most = MostFor(ctx, _sel, unitId);
            if (most <= 0) { Toast.Show(ctx.T("ui.formation.quickAddFailed"), bad: true); return; }
            ed.SetTroop(_sel, unitId, most);
            app.ShowMain("formation");
        }

        /// <summary>그 군단에 그 병종을 넣을 수 있는 최대 (다른 군단이 쓰고 남은 병력 · 통솔 한도).</summary>
        private static long MostFor(GameContext ctx, int index, string unitId)
        {
            var ed = ctx.Formation;
            var u = ctx.State.FindUnit(unitId);
            long free = (u?.Troops ?? 0) - ed.UsedElsewhere(index, unitId);
            return System.Math.Max(0, System.Math.Min(free, ed.Cap(index)));
        }

        /// <summary>판의 군단 칸: 장수 초상 · 이름 · Lv / 병종 그림 · 병종 · 인원 / 진형 아이콘 / ★. 누르면 고름, 끌면 옮김 (판 밖 = 빼기).</summary>
        private static void CorpsCell(GameApp app, RectTransform root, RectTransform cell, int index, bool warn, DropTargets<(int row, int col)> targets)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var c = ed.Draft[index];
            bool sel = index == _sel;
            bool commander = c.Commander || (index == 0 && !ed.Draft.Exists(x => x.Commander));
            var card = Ui.Rect(cell, "Corps");
            Ui.Fill(card);
            var bg = Ui.Background(card, sel ? Palette.CellOn : Palette.Card);
            Ui.Round(bg);
            if (sel || warn)
            {
                var ring = Ui.Fill(Ui.Rect(card, "SelRing"));
                var ri = ring.gameObject.AddComponent<Image>();
                ri.sprite = UiKit.Ring(Mathf.Max(4, Mathf.RoundToInt(UiStyle.CornerRadius)), 5); ri.type = Image.Type.Sliced; ri.fillCenter = false; ri.raycastTarget = false;
                ri.color = warn ? Palette.Bad : Palette.Good;
            }
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => { Sfx.Play("click", 0.5f); _sel = index; _openSheet = true; app.ShowMain("formation"); });

            // 위: 초상 · 이름 · Lv (초상은 칸 왼쪽 위 상자에 맞춤 — 이름과 겹치지 않게)
            var prBox = Ui.Place(Ui.Rect(card, "PortraitBox"), 10, 8, 66, 66);
            var pr = new Portrait(prBox, 62);
            pr.Rect.anchorMin = pr.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            pr.Rect.sizeDelta = new Vector2(62f, 62f);
            pr.Show(c.GeneralId, ctx.GeneralName(c.GeneralId), Palette.Ally);
            var g = ctx.State.FindGeneral(c.GeneralId);
            var n = Ui.Label(Ui.Place(Ui.Rect(card, "Name"), 84, 10, 190, 34), (commander ? "★ " : "") + ctx.GeneralName(c.GeneralId), 24);
            n.fontStyle = FontStyle.Bold;
            n.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Fill(n.rectTransform);
            var lv = Ui.Label(Ui.Place(Ui.Rect(card, "Lv"), 84, 44, 190, 26), "Lv." + (g?.Level ?? 1) + " · " + ctx.T("role." + Army.RoleOf(c)), 18, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Fill(lv.rectTransform);
            // 오른쪽 위: 진형 아이콘 (새 전투 진형)
            ctx.Defs.Formations.TryGetValue(c.Formation ?? "", out var fdef);
            var ic = FormationIcon.Add(card, fdef?.Crowd ?? "line", Palette.Enemy, 46f, 4f);
            ic.anchorMin = ic.anchorMax = new Vector2(1f, 1f);
            ic.pivot = new Vector2(1f, 1f);
            ic.anchoredPosition = new Vector2(-10f, -10f);
            // 아래: 병종 그림 · 병종 · 인원
            var pick = c.Picks.Count > 0 ? c.Picks[0] : null;
            var u = pick != null ? ctx.State.FindUnit(pick.UnitId) : null;
            string troopType = u?.TroopType ?? pick?.UnitId;
            if (troopType != null) Ui.Place(Ui.TroopPicture(ctx, card, troopType, 120), 10, 84, 120, 140);
            var tn = Ui.Label(Ui.Place(Ui.Rect(card, "Troop"), 138, 96, 180, 34), troopType == null ? ctx.T("ui.formation.noTroop") : TroopName(ctx, troopType) + (pick != null && pick.Hired ? " " + ctx.T("ui.formation.hired") : ""), 24, TextAnchor.MiddleLeft, troopType == null ? Palette.Bad : Palette.Text);
            Ui.Fill(tn.rectTransform);
            long troops = ed.Troops(index), cap = ed.Cap(index);
            var cnt = Ui.Label(Ui.Place(Ui.Rect(card, "Count"), 138, 132, 190, 40), troops.ToString("N0"), 34, TextAnchor.MiddleLeft, troops > cap ? Palette.Bad : Palette.Text);
            cnt.fontStyle = FontStyle.Bold;
            Ui.Fill(cnt.rectTransform);
            var capT = Ui.Label(Ui.Place(Ui.Rect(card, "Cap"), 138, 172, 190, 28), "/ " + cap.ToString("N0"), 18, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Fill(capT.rectTransform);
            foreach (var gph in card.GetComponentsInChildren<Graphic>()) if (gph != bg) gph.raycastTarget = false;
            var d = card.gameObject.AddComponent<DragItem>();
            d.Payload = "c|" + index;
            d.GhostParent = root;
            d.Dropped += (pl, screen) =>
            {
                if (targets.Find(screen, out var to)) ed.SetBoard(index, to.row, to.col);
                else { ed.RemoveCorps(index); _sel = -1; } // 판 밖 = 빼기
                app.ShowMain("formation");
            };
        }

        /// <summary>고른 군단 판 (아래에서 올라옴): 장수 [바꾸기 ▾] · ★ / 병종 그림 · 병종 [바꾸기 ▾] · 남은 / − 인원 + [최대] / 진형 아이콘 · 이름 [진형 ▾] / [자동] [빼기].</summary>
        private static void SelectedSheet(GameApp app, RectTransform root, int index)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var c = ed.Draft[index];
            bool commander = c.Commander || (index == 0 && !ed.Draft.Exists(x => x.Commander));
            var (card, close) = Ui.BottomSheet(root, ctx.T("ui.formation.selected", ctx.T("ui.board.spot." + c.BoardRow + c.BoardCol)), 610);
            Ui.Column(card, 12, 0);
            // 판 안에서 값을 바꾸면 화면을 다시 그리고 이 판을 다시 연다
            void Reopen() { _openSheet = true; app.ShowMain("formation"); }

            // 장수
            var gRow = Ui.Row(card, "General", 90, 12);
            gRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var pr = new Portrait(gRow, 80);
            pr.Show(c.GeneralId, ctx.GeneralName(c.GeneralId), Palette.Ally);
            var g = ctx.State.FindGeneral(c.GeneralId);
            var gn = Ui.Label(gRow, ctx.GeneralName(c.GeneralId) + "  " + Ui.Colored("Lv." + (g?.Level ?? 1), Palette.TextDim), 32);
            gn.fontStyle = FontStyle.Bold;
            Ui.Size(gn, 90, -1, -1, 1);
            var change = Ui.SecondaryButton(gRow, ctx.T("ui.formation.changeGeneral") + " ▾", null, 70, 24);
            Ui.Size(change, 70, 190).flexibleWidth = 0;
            var changeRt = (RectTransform)change.transform;
            change.onClick.AddListener(() =>
            {
                var items = new List<(string, string, bool, System.Action)>();
                foreach (var gs in ctx.State.Generals)
                {
                    string gid = gs.GeneralId;
                    if (gid == c.GeneralId || !GeneralStatus.CanDeploy(gs.Status)) continue;
                    int there = ed.Draft.FindIndex(x => x.GeneralId == gid);
                    items.Add((ctx.GeneralName(gid), there >= 0 ? ctx.T("ui.formation.swapWith", ctx.T("ui.formation.corpsNo", there + 1)) : null, true, () => { ed.SetGeneral(index, gid); Reopen(); }));
                }
                if (items.Count == 0) { Toast.Show(ctx.T("ui.formation.noFreeGeneral"), bad: true); return; }
                Ui.PickMenu(root, changeRt, 420, items.ToArray());
            });
            var star = commander ? Ui.PrimaryButton(gRow, "★ " + ctx.T("ui.board.commander"), () => { }, 70, 24) : Ui.SecondaryButton(gRow, "☆ " + ctx.T("ui.board.commander"), () => { ed.SetCommander(index); Reopen(); }, 70, 24);
            Ui.Size(star, 70, 170).flexibleWidth = 0;

            // 병종
            var pick = c.Picks.Count > 0 ? c.Picks[0] : null;
            var u = pick != null ? ctx.State.FindUnit(pick.UnitId) : null;
            var tRow = Ui.Row(card, "Troop", 120, 12);
            tRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            if (u != null || pick != null) Ui.TroopPicture(ctx, tRow, u?.TroopType ?? pick.UnitId, 110);
            var tcol = Ui.Rect(tRow, "TroopText");
            Ui.Size(tcol, 110, -1, -1, 1);
            Ui.Column(tcol, 2, 0).childAlignment = TextAnchor.MiddleLeft;
            var tn = Ui.Label(tcol, pick == null ? ctx.T("ui.formation.noTroop") : (u == null ? pick.UnitId : TroopName(ctx, u.TroopType)) + (pick.Hired ? " " + ctx.T("ui.formation.hired") : ""), 30);
            tn.fontStyle = FontStyle.Bold;
            long most = pick != null && !pick.Hired ? MostFor(ctx, index, pick.UnitId) : 0;
            if (pick != null && u != null)
            {
                long free = u.Troops - ed.UsedElsewhere(index, pick.UnitId);
                Ui.Label(tcol, ctx.T("ui.formation.left", (free - pick.Troops).ToString("N0")) + "  ·  " + ctx.T("ui.formation.supplyPerDay", SupplyPerDay(ctx, c).ToString("N0")), 22, TextAnchor.MiddleLeft, free - pick.Troops < 0 ? Palette.Bad : Palette.TextDim);
            }
            var tchange = Ui.SecondaryButton(tRow, ctx.T("ui.formation.changeTroop") + " ▾", null, 70, 24);
            Ui.Size(tchange, 70, 190).flexibleWidth = 0;
            var tchangeRt = (RectTransform)tchange.transform;
            tchange.onClick.AddListener(() =>
            {
                var items = new List<(string, string, bool, System.Action)>();
                foreach (var unit in ctx.State.Units)
                {
                    string uid = unit.UnitId;
                    long m = MostFor(ctx, index, uid);
                    if (unit.Troops <= 0) continue;
                    items.Add((TroopName(ctx, unit.TroopType), m.ToString("N0"), m > 0, () => { ed.SetTroop(index, uid, m); Reopen(); }));
                }
                Ui.PickMenu(root, tchangeRt, 420, items.ToArray());
            });

            // 인원: − 숫자 + [최대]
            if (pick != null && !pick.Hired)
            {
                string unitId = pick.UnitId;
                var row = Ui.Row(card, "Count", 64, 8);
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                void Set(long v) { ed.SetTroop(index, unitId, System.Math.Max(1, System.Math.Min(most, v))); Reopen(); }
                var minus = Ui.Styled(Ui.SecondaryButton(row, "−", () => Set(pick.Troops - Step), 60, 30), "button.small");
                Ui.Size(minus, 60, 70).flexibleWidth = 0;
                RepeatButton.Add(minus, () => Set(pick.Troops - Step * 5));
                var num = Ui.SecondaryButton(row, pick.Troops.ToString("N0"), () => NumberInput(app, root, index, unitId, pick.Troops, most), 60, 30);
                Ui.Size(num, 60, 180).flexibleWidth = 0;
                num.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
                var plus = Ui.Styled(Ui.SecondaryButton(row, "+", () => Set(pick.Troops + Step), 60, 30), "button.small");
                Ui.Size(plus, 60, 70).flexibleWidth = 0;
                RepeatButton.Add(plus, () => Set(pick.Troops + Step * 5));
                Ui.Size(Ui.Styled(Ui.SecondaryButton(row, ctx.T("ui.formation.max"), () => Set(most), 60, 24), "button.small"), 60, 110).flexibleWidth = 0;
                Ui.Size(Ui.Rect(row, "Gap"), 60, -1, -1, 1);
                long troops = ed.Troops(index), cap = ed.Cap(index);
                Ui.Size(Ui.Label(row, ctx.T("ui.formation.capOf", cap.ToString("N0")), 22, TextAnchor.MiddleRight, troops > cap ? Palette.Bad : Palette.TextDim), 60, 260).flexibleWidth = 0;
            }

            // 진형 (새 전투 진형 아이콘 + 이름) · 역할
            var fRow = Ui.Row(card, "Form", 70, 12);
            fRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            ctx.Defs.Formations.TryGetValue(c.Formation ?? "", out var fdef);
            var iconBox = Ui.Rect(fRow, "FormIcon");
            Ui.Size(iconBox, 60, 60).flexibleWidth = 0;
            Ui.Fill(FormationIcon.Add(iconBox, fdef?.Crowd ?? "line", Palette.Enemy, 56f, 5f));
            Ui.Size(Ui.Label(fRow, FormationName(ctx, c.Formation) + "  " + Ui.Colored(ctx.T("ui.formation.roleFromBoard", ctx.T("role." + Army.RoleOf(c))), Palette.TextDim), 26), 70, -1, -1, 1);
            Ui.Size(Ui.SecondaryButton(fRow, ctx.T("ui.formation.changeForm") + " ▾", () => { close(); FormationPicker(app, root, index); }, 64, 24), 64, 190).flexibleWidth = 0;

            // 자동 · 빼기 · 닫기
            var aRow = Ui.Row(card, "Actions", 72, 12);
            Ui.Size(Ui.SecondaryButton(aRow, ctx.T("ui.formation.autoCorps"), () => { ed.AutoFillCorps(index); Reopen(); }, 72, 26), 72, -1, -1, 1);
            Ui.Size(Ui.SecondaryButton(aRow, "✕ " + ctx.T("ui.formation.removeCorps"), () => { close(); ed.RemoveCorps(index); _sel = -1; app.ShowMain("formation"); }, 72, 26), 72, -1, -1, 1);
            Ui.Size(Ui.PrimaryButton(aRow, ctx.T("ui.common.close"), close, 72, 26), 72, -1, -1, 1);
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
                ctx.Formation.SetTroop(index, unitId, System.Math.Max(1, System.Math.Min(most, v)));
                _openSheet = true; app.ShowMain("formation");
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
                b.onClick.AddListener(() => { close(); ed.SetFormation(index, id); _openSheet = true; app.ShowMain("formation"); });
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
            if (ed.Errors().Count == 0 && ctx.Do(PlayerActionRunner.SaveFormation(ed.Situation, ed.Draft))) { ctx.DiscardFormationDraft(); Toast.Show(ctx.T("ui.formation.saved")); }
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
            var (header, body, footer, _) = Ui.TitledPage(Root, T("ui.formation.corpsTitle", _index + 1), () => App.ShowMain("formation"), Ui.ButtonHeight + 28, infoGap: false);
            long troops = ed.Troops(_index), cap = ed.Cap(_index);
            Ui.Label(header, T("ui.formation.troopsLine", Ui.Colored(troops.ToString("N0"), troops > cap ? Palette.Bad : Palette.Good), cap.ToString("N0")), Ui.FontSmall);

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

            Ui.Button(footer, T("ui.formation.removeCorps"), () => { ed.RemoveCorps(_index); App.ShowMain("formation"); }, color: Palette.Danger);
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
            var (_, list, _, _) = Ui.TitledPage(Root, T("ui.formation.pickTitle", _index + 1, _slot == null ? T("ui.formation.addPick") : FormationTabView.TroopName(Ctx, Ctx.State.FindUnit(_slot)?.TroopType)), () => App.ShowMain("formation"), infoGap: false);
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
            var (header, list, _, _) = Ui.TitledPage(Root, T("ui.formation.createTitle"), () => App.ShowMain("formation"), infoGap: false);
            Ui.Label(header, T("ui.formation.createNote"), 28, TextAnchor.MiddleLeft, Palette.TextDim);
            foreach (var id in troops.CreatableTroopTypes())
            {
                string t = id;
                Ui.Button(list, FormationTabView.TroopName(Ctx, t), () =>
                {
                    Ctx.Do(PlayerActionRunner.CreateUnit(t));
                    App.ShowMain("formation");
                }, 110, Palette.Card, 34);
            }
        }
    }

    /// <summary>부대 해산 확인. 병력·부상병이 사라진다.</summary>
    public sealed class DisbandUnitScreen : GameScreen
    {
        private readonly string _unitId;
        public DisbandUnitScreen(string unitId) { _unitId = unitId; }

        public override bool OnBack() { App.ShowMain("formation"); return true; }

        public override void Build()
        {
            var u = Ctx.State.FindUnit(_unitId);
            if (u == null) { App.ShowMain("formation"); return; }
            // 공통 타이틀 (← 부대 해산) · 몸통 확인 문구 · 아래 [취소] [해산]
            var (_, body, footer, _) = Ui.TitledPage(Root, T("ui.formation.disbandTitle"), () => App.ShowMain("formation"), 120, infoGap: false);
            var card = Ui.Card(body, "Confirm");
            // 병종 묶음은 이름 = 병종이라 한 번만 ("보병 보병 부대" 막기)
            string label = Ctx.UnitLabel(u.UnitId), troop = FormationTabView.TroopName(Ctx, u.TroopType);
            Ui.Label(card, T("ui.formation.disbandConfirm", label == troop ? label : label + " " + troop, (u.Troops + u.Wounded).ToString("N0")), Ui.FontBody, TextAnchor.MiddleCenter);
            Ui.Button(footer, T("ui.common.cancel"), () => App.ShowMain("formation"), 92);
            Ui.Button(footer, T("ui.formation.disband"), () =>
            {
                Ctx.Do(PlayerActionRunner.DisbandUnit(_unitId));
                App.ShowMain("formation");
            }, 92, Palette.Danger);
        }
    }
}
