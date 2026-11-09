using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Data.Defs;
using Game.Economy;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 부대 편성 탭 (formation.md 4장): 군단 목록(누르면 군단 편집) + 저장·되돌리기, 보유 부대 목록(병력 관리, 4.1).
    /// 편집은 사본(<see cref="FormationEditor"/>)에 하고, 검사를 통과해야 저장된다 (R-5).
    /// </summary>
    public sealed class FormationTabView : ITabView
    {
        internal const long Step = 500;

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var ed = ctx.Formation;
            var list = Ui.ScrollList(root, "List", 12, 16);
            Ui.Fill((RectTransform)list.parent.parent);

            // 상황 고르기: 기본 + 상황별 (전장 유형, R-35)
            var situations = Ui.HScrollRow(list, "Situations", 84, 8, 0);
            void SituationButton(string id, string label)
            {
                bool saved = id == null || ctx.State.CompositionPresets.ContainsKey(id);
                var b = Ui.Button(situations, label + (saved ? "" : " ·"), () => { ctx.SelectFormationSituation(id); app.ShowMain("formation"); }, 76, fontSize: 28);
                Ui.Size(b, width: 200);
                b.targetGraphic.color = id == ed.Situation ? Palette.ButtonActive : Palette.Button;
            }
            SituationButton(null, ctx.T("ui.formation.default"));
            foreach (var s in ctx.Defs.Campaign.FormationSituations) SituationButton(s, ctx.T("situation." + s));
            if (ed.Situation != null)
            {
                Ui.Label(list, ctx.T("ui.formation.presetOf", ctx.T("situation." + ed.Situation)), Ui.FontBody);
                if (!ed.HasPreset) Ui.Label(list, ctx.T("ui.formation.usingDefault"), 26, TextAnchor.MiddleLeft, Palette.TextDim);
            }

            Ui.Label(list, ctx.T("ui.formation.corpsHeader", ed.Draft.Count, ed.MaxCorps) + (ed.Changed() ? "  " + Ui.Colored(ctx.T("ui.formation.unsaved"), Palette.Bad) : ""), Ui.FontBody);
            for (int i = 0; i < ed.Draft.Count; i++)
            {
                int index = i;
                var c = ed.Draft[i];
                long troops = ed.Troops(i), cap = ed.Cap(i);
                string line = ctx.T("ui.formation.corpsLine", i + 1, ctx.GeneralName(c.GeneralId), FormationName(ctx, c.Formation), ctx.T("role." + c.Role),
                    Ui.Colored(troops.ToString("N0"), troops > cap ? Palette.Bad : Palette.Text), cap.ToString("N0"), c.Slots.Count);
                var b = Ui.Button(list, line, () => app.Show(new CorpsEditScreen(index)), 130, Palette.Card, 30);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }

            var errors = ed.Errors();
            foreach (var e in errors) Ui.Label(list, Ui.Colored(e, Palette.Bad), 26);

            var row = Ui.Row(list, "Buttons", 96, 8);
            var add = Ui.Button(row, ctx.T("ui.formation.addCorps"), () => { if (ed.AddCorps() != null) app.ShowMain("formation"); }, 96, fontSize: 28);
            Ui.SetEnabled(add, ed.Draft.Count < ed.MaxCorps && ed.FreeGenerals().Count > 0);
            var save = Ui.Button(row, ctx.T("ui.formation.save"), () => { if (ed.Apply()) { ctx.Save(); app.ShowMain("formation"); } }, 96, fontSize: 28);
            Ui.SetEnabled(save, errors.Count == 0 && ed.Changed());
            var revert = Ui.Button(row, ctx.T("ui.formation.revert"), () => { ctx.DiscardFormationDraft(); app.ShowMain("formation"); }, 96, fontSize: 28);
            Ui.SetEnabled(revert, ed.Changed() && ed.HasPreset);
            if (ed.Situation != null && ed.HasPreset)
                Ui.Button(list, ctx.T("ui.formation.removePreset"), () => { if (ed.RemovePreset()) { ctx.Save(); ctx.DiscardFormationDraft(); } app.ShowMain("formation"); }, 84, new Color(0.45f, 0.22f, 0.20f), 28);

            // 병력 관리
            var troopsSvc = new TroopService(ctx.Defs);
            Ui.Label(list, ctx.T("ui.formation.unitsHeader", ctx.Resource(ResourceIds.Supply).ToString("N0"), ctx.Resource(ResourceIds.Gold).ToString("N0")), Ui.FontBody);
            foreach (var u in ctx.State.Units)
            {
                string unitId = u.UnitId;
                int at = ed.CorpsOf(unitId);
                string where = at < 0 ? ctx.T("ui.formation.reserveUnit") : ctx.GeneralName(ed.Draft[at].GeneralId);
                var card = Ui.Card(list, unitId, 16, 6);
                Ui.Label(card, ctx.T("ui.formation.unitLine", unitId, TroopName(ctx, u.TroopType), u.Troops.ToString("N0"), ctx.Defs.Balance.UnitMaxTroops.ToString("N0"),
                    u.Wounded.ToString("N0"), u.Cohesion.FloorToLong(), where), 26);
                var r = Ui.Row(card, "Troops", 72, 6);
                long room = troopsSvc.Room(u);
                long n = System.Math.Min(Step, room);
                var c1 = Ui.Button(r, ctx.T("ui.formation.conscript", n, troopsSvc.ConscriptCost(n).ToString("N0")), () => Do(app, troopsSvc.Conscript(ctx.State, unitId, Step)), 72, fontSize: 22);
                Ui.SetEnabled(c1, n > 0 && ctx.Resource(ResourceIds.Supply) >= troopsSvc.ConscriptCost(n));
                var c2 = Ui.Button(r, ctx.T("ui.formation.recruit", n, troopsSvc.RecruitCost(n).ToString("N0")), () => Do(app, troopsSvc.Recruit(ctx.State, unitId, Step)), 72, fontSize: 22);
                Ui.SetEnabled(c2, n > 0 && ctx.Resource(ResourceIds.Gold) >= troopsSvc.RecruitCost(n));
                long w = u.Wounded;
                var h1 = Ui.Button(r, ctx.T("ui.formation.healSupply", HealCostCalculator.HealCostInSupply(ctx.Defs.Balance, w).ToString("N0")), () => Do(app, troopsSvc.Heal(ctx.State, unitId, w, ResourceIds.Supply)), 72, fontSize: 22);
                Ui.SetEnabled(h1, w > 0 && ctx.Resource(ResourceIds.Supply) >= HealCostCalculator.HealCostInSupply(ctx.Defs.Balance, w));
                var h2 = Ui.Button(r, ctx.T("ui.formation.healGold", HealCostCalculator.HealCostInGold(ctx.Defs.Balance, w).ToString("N0")), () => Do(app, troopsSvc.Heal(ctx.State, unitId, w, ResourceIds.Gold)), 72, fontSize: 22);
                Ui.SetEnabled(h2, w > 0 && ctx.Resource(ResourceIds.Gold) >= HealCostCalculator.HealCostInGold(ctx.Defs.Balance, w));
            }
        }

        private static void Do(GameApp app, long changed)
        {
            if (changed > 0) app.Context.Save();
            app.ShowMain("formation");
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

            Ui.Label(body, T("ui.formation.slots"), Ui.FontBody);
            foreach (var slot in SlotIds.All)
            {
                string s = slot;
                string unit = c.Slots.TryGetValue(slot, out var id) ? id : null;
                var u = unit == null ? null : Ctx.State.FindUnit(unit);
                string label = T("slot." + slot) + "  " + (u == null ? Ui.Colored(T("ui.formation.empty"), Palette.TextDim)
                    : u.UnitId + " " + FormationTabView.TroopName(Ctx, u.TroopType) + " " + u.Troops.ToString("N0"));
                var b = Ui.Button(body, label, () => App.Show(new UnitPickerScreen(_index, s)), 96, Palette.Card, 30);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
            foreach (var e in ed.Errors()) Ui.Label(body, Ui.Colored(e, Palette.Bad), 24);

            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(row, T("ui.common.back"), () => App.ShowMain("formation"));
            Ui.Button(row, T("ui.formation.removeCorps"), () => { ed.RemoveCorps(_index); App.ShowMain("formation"); }, color: new Color(0.45f, 0.22f, 0.20f));
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

    /// <summary>칸에 넣을 부대 고르기: 비우기 + 보유 부대 (다른 군단에 있으면 옮겨 온다).</summary>
    public sealed class UnitPickerScreen : GameScreen
    {
        private readonly int _index;
        private readonly string _slot;
        public UnitPickerScreen(int index, string slot) { _index = index; _slot = slot; }

        public override void Build()
        {
            var ed = Ctx.Formation;
            Ui.Column(Root, 10, 24);
            Ui.Size(Ui.Label(Root, T("ui.formation.pickTitle", _index + 1, T("slot." + _slot)), Ui.FontBody), 80);
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
            Ui.Button(Root, T("ui.common.back"), () => App.Show(new CorpsEditScreen(_index)));
        }

        private void Pick(string unitId)
        {
            Ctx.Formation.Assign(_index, _slot, unitId);
            App.Show(new CorpsEditScreen(_index));
        }
    }
}
