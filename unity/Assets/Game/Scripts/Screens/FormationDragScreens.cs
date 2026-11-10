using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 편성(드래그) 탭: "부대 편성" 탭의 새 버전 (main-screen.md 2.1, 비교용). 군단마다 칸을 진형 모양(앞 1 · 가운데 3 · 뒤 2)으로 보여주고,
    /// 아래 대기 부대를 칸으로 끌어 넣는다. 칸 → 칸은 옮기기(차 있으면 맞바꿈), 칸 → 대기 줄은 빼기.
    /// 편집·검사·저장은 기존과 같은 규칙 객체(<see cref="FormationEditor"/>, R-5)와 행동(formation.save). 채택 안 하면 이 파일과 등록 2줄만 지운다.
    /// </summary>
    public sealed class FormationDragTabView : ITabView
    {
        private const string TabId = "formationDrag";
        private const string Reserve = "reserve";

        /// <summary>진형 모양 배치: 3열 × 3행, null = 빈 자리.</summary>
        private static readonly string[] Layout =
        {
            null, SlotIds.Vanguard, null,
            SlotIds.LeftWing, SlotIds.Center, SlotIds.RightWing,
            SlotIds.LeftGuard, null, SlotIds.RightGuard,
        };

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var targets = new DropTargets<(int corps, string slot)>();
            var list = Ui.ScrollList(root, "List", 12, 16);
            Ui.Fill((RectTransform)list.parent.parent);

            Ui.Label(list, ctx.T("ui.formation.corpsHeader", ed.Draft.Count, ed.MaxCorps) + (ed.Changed() ? "  " + Ui.Colored(ctx.T("ui.formation.unsaved"), Palette.Bad) : ""), Ui.FontBody);
            Ui.Label(list, ctx.T("ui.formationDrag.help"), 24, TextAnchor.MiddleLeft, Palette.TextDim);

            void OnDrop(string payload, Vector2 screen)
            {
                // payload: 부대 id | 원래 군단 번호(-1 = 대기) | 원래 칸
                var p = payload.Split('|');
                string unitId = p[0];
                int fromCorps = int.Parse(p[1]);
                string fromSlot = p[2].Length == 0 ? null : p[2];
                if (!targets.Find(screen, out var to)) return;
                if (to.slot == Reserve)
                {
                    if (fromCorps >= 0) ed.Assign(fromCorps, fromSlot, null);
                }
                else
                {
                    if (fromCorps == to.corps && fromSlot == to.slot) return;
                    string existing = ed.Draft[to.corps].Slots.TryGetValue(to.slot, out var e) ? e : null;
                    ed.Assign(to.corps, to.slot, unitId);
                    if (existing != null && existing != unitId && fromCorps >= 0) ed.Assign(fromCorps, fromSlot, existing); // 맞바꿈
                }
                app.ShowMain(TabId);
            }

            DragItem Draggable(RectTransform rt, string unitId, int corps, string slot)
            {
                var d = rt.gameObject.AddComponent<DragItem>();
                d.Payload = unitId + "|" + corps + "|" + (slot ?? "");
                d.GhostParent = root.root;
                d.Dropped += OnDrop;
                return d;
            }

            for (int i = 0; i < ed.Draft.Count; i++)
            {
                int index = i;
                var c = ed.Draft[i];
                long troops = ed.Troops(i), cap = ed.Cap(i);
                var card = Ui.Card(list, "Corps" + i, 16, 8);
                var head = Ui.Row(card, "Head", 72, 8);
                Ui.Size(Ui.Label(head, ctx.T("ui.formationDrag.corpsLine", i + 1, ctx.GeneralName(c.GeneralId), FormationTabView.FormationName(ctx, c.Formation),
                    Ui.Colored(troops.ToString("N0"), troops > cap ? Palette.Bad : Palette.Good), cap.ToString("N0")), 26), flexibleWidth: 3);
                Ui.Size(Ui.Button(head, ctx.T("ui.formationDrag.edit"), () => app.Show(new CorpsEditScreen(index)), 72, fontSize: 24), width: 200);

                var gridRt = Ui.Rect(card, "Slots");
                var grid = gridRt.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(320, 104);
                grid.spacing = new Vector2(8, 8);
                grid.childAlignment = TextAnchor.MiddleCenter;
                Ui.Size(gridRt, 3 * 104 + 2 * 8);
                foreach (var slot in Layout)
                {
                    var cell = Ui.Rect(gridRt, slot ?? "Gap");
                    if (slot == null) continue;
                    string unitId = c.Slots.TryGetValue(slot, out var id) ? id : null;
                    var u = unitId == null ? null : ctx.State.FindUnit(unitId);
                    Ui.Background(cell, u == null ? new Color(0.14f, 0.15f, 0.18f) : Palette.Button);
                    string text = ctx.T("slot." + slot) + "\n" + (u == null ? Ui.Colored(ctx.T("ui.formation.empty"), Palette.TextDim)
                        : ctx.UnitLabel(u.UnitId) + " " + FormationTabView.TroopName(ctx, u.TroopType) + " " + u.Troops.ToString("N0"));
                    var label = Ui.Label(cell, text, 22, TextAnchor.MiddleCenter);
                    Ui.Fill(label.rectTransform);
                    label.raycastTarget = false;
                    targets.Add(cell, (index, slot));
                    if (u != null) Draggable(cell, u.UnitId, index, slot);
                }
            }

            // 대기 부대: 어느 군단에도 없는 부대. 여기로 끌어 오면 칸에서 뺀다.
            var reserveCard = Ui.Card(list, "Reserve", 16, 8);
            Ui.Label(reserveCard, ctx.T("ui.formationDrag.reserve"), Ui.FontBody);
            var chips = Ui.HScrollRow(reserveCard, "Chips", 104, 8, 4);
            targets.Add(reserveCard, (-1, Reserve));
            int free = 0;
            foreach (var u in ctx.State.Units)
            {
                if (ed.CorpsOf(u.UnitId) >= 0) continue;
                free++;
                var chip = Ui.Rect(chips, u.UnitId);
                Ui.Background(chip, Palette.Card);
                Ui.Size(chip, 96, 300);
                var label = Ui.Label(chip, ctx.UnitLabel(u.UnitId) + "\n" + FormationTabView.TroopName(ctx, u.TroopType) + " " + u.Troops.ToString("N0"), 22, TextAnchor.MiddleCenter);
                Ui.Fill(label.rectTransform);
                label.raycastTarget = false;
                Draggable(chip, u.UnitId, -1, null);
            }
            if (free == 0) Ui.Label(chips, ctx.T("ui.formationDrag.noReserve"), 24, TextAnchor.MiddleLeft, Palette.TextDim);

            var errors = ed.Errors();
            foreach (var e in errors) Ui.Label(list, Ui.Colored(e, Palette.Bad), 26);

            var row = Ui.Row(list, "Buttons", 96, 8);
            var add = Ui.Button(row, ctx.T("ui.formation.addCorps"), () => { if (ed.AddCorps() != null) app.ShowMain(TabId); }, 96, fontSize: 28);
            Ui.SetEnabled(add, ed.Draft.Count < ed.MaxCorps && ed.FreeGenerals().Count > 0);
            var save = Ui.Button(row, ctx.T("ui.formation.save"), () => { if (ed.Errors().Count == 0 && ctx.Do(PlayerActionRunner.SaveFormation(ed.Situation, ed.Draft))) app.ShowMain(TabId); }, 96, fontSize: 28);
            Ui.SetEnabled(save, errors.Count == 0 && ed.Changed());
            var revert = Ui.Button(row, ctx.T("ui.formation.revert"), () => { ctx.DiscardFormationDraft(); app.ShowMain(TabId); }, 96, fontSize: 28);
            Ui.SetEnabled(revert, ed.Changed() && ed.HasPreset);
        }
    }
}
