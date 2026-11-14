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
                Ui.Recolor(b, id == ed.Situation ? Palette.ButtonActive : Palette.Button);
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
            var save = Ui.Button(row, ctx.T("ui.formation.save"), () => { if (ed.Errors().Count == 0 && ctx.Do(PlayerActionRunner.SaveFormation(ed.Situation, ed.Draft))) app.ShowMain("formation"); }, 96, fontSize: 28);
            Ui.SetEnabled(save, errors.Count == 0 && ed.Changed());
            var revert = Ui.Button(row, ctx.T("ui.formation.revert"), () => { ctx.DiscardFormationDraft(); app.ShowMain("formation"); }, 96, fontSize: 28);
            Ui.SetEnabled(revert, ed.Changed() && ed.HasPreset);
            if (ed.Situation != null && ed.HasPreset)
                Ui.Button(list, ctx.T("ui.formation.removePreset"), () => { if (ctx.Do(PlayerActionRunner.RemovePreset(ed.Situation))) ctx.DiscardFormationDraft(); app.ShowMain("formation"); }, 84, Palette.Danger, 28);

            // 병력 관리
            var troopsSvc = new TroopService(ctx.Defs);
            Ui.Label(list, ctx.T("ui.formation.unitsHeader", ctx.Resource(ResourceIds.Supply).ToString("N0"), ctx.Resource(ResourceIds.Gold).ToString("N0")), Ui.FontBody);
            var create = Ui.Button(list, ctx.T("ui.formation.createUnit", ctx.State.Units.Count, ctx.Defs.Campaign.UnitCountMax), () => app.Show(new CreateUnitScreen()), 84, fontSize: 28);
            Ui.SetEnabled(create, troopsSvc.CanCreate(ctx.State));
            foreach (var u in ctx.State.Units)
            {
                string unitId = u.UnitId;
                int at = ed.CorpsOf(unitId);
                string where = at < 0 ? ctx.T("ui.formation.reserveUnit") : ctx.GeneralName(ed.Draft[at].GeneralId);
                var card = Ui.Card(list, unitId, 16, 6);
                Ui.Label(card, ctx.T("ui.formation.unitLine", ctx.UnitLabel(unitId), TroopName(ctx, u.TroopType), u.Troops.ToString("N0"), Game.Campaign.Roster.Army.TroopMax(ctx.Defs, ctx.State).ToString("N0"),
                    u.Wounded.ToString("N0"), u.Cohesion.FloorToLong(), where), 26);
                var r = Ui.Row(card, "Troops", 72, 6);
                long room = troopsSvc.Room(ctx.State, u);
                long n = System.Math.Min(Step, room);
                var c1 = Ui.Button(r, ctx.T("ui.formation.conscript", n, troopsSvc.ConscriptCost(n).ToString("N0")), () => Do(app, PlayerActionRunner.Conscript(unitId, Step)), 72, fontSize: 22);
                Ui.SetEnabled(c1, n > 0 && ctx.Resource(ResourceIds.Supply) >= troopsSvc.ConscriptCost(n));
                var c2 = Ui.Button(r, ctx.T("ui.formation.recruit", n, troopsSvc.RecruitCost(n).ToString("N0")), () => Do(app, PlayerActionRunner.Recruit(unitId, Step)), 72, fontSize: 22);
                Ui.SetEnabled(c2, n > 0 && ctx.Resource(ResourceIds.Gold) >= troopsSvc.RecruitCost(n));
                long w = u.Wounded;
                var h1 = Ui.Button(r, ctx.T("ui.formation.healSupply", HealCostCalculator.HealCostInSupply(ctx.Defs.Balance, w).ToString("N0")), () => Do(app, PlayerActionRunner.Heal(unitId, w, ResourceIds.Supply)), 72, fontSize: 22);
                Ui.SetEnabled(h1, w > 0 && ctx.Resource(ResourceIds.Supply) >= HealCostCalculator.HealCostInSupply(ctx.Defs.Balance, w));
                var h2 = Ui.Button(r, ctx.T("ui.formation.healGold", HealCostCalculator.HealCostInGold(ctx.Defs.Balance, w).ToString("N0")), () => Do(app, PlayerActionRunner.Heal(unitId, w, ResourceIds.Gold)), 72, fontSize: 22);
                Ui.SetEnabled(h2, w > 0 && ctx.Resource(ResourceIds.Gold) >= HealCostCalculator.HealCostInGold(ctx.Defs.Balance, w));
                // 해산: 저장된 편성(기본·상황별)과 고치는 중인 편성 어디에도 없을 때만 (R-39)
                var d = Ui.Button(r, ctx.T("ui.formation.disband"), () => app.Show(new DisbandUnitScreen(unitId)), 72, Palette.Danger, 22);
                Ui.SetEnabled(d, at < 0 && troopsSvc.CanDisband(ctx.State, unitId));
            }
        }

        /// <summary>행동으로 적용·저장 (온라인이면 서버도 같은 규칙으로 적용, R-41).</summary>
        private static void Do(GameApp app, PlayerAction action)
        {
            app.Context.Do(action);
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
            Ui.Button(Root, T("ui.common.back"), () => App.Show(new CorpsEditScreen(_index)));
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
            App.Show(new CorpsEditScreen(_index));
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
