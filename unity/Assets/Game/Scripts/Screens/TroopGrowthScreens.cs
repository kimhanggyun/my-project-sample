using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Economy;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 부대 관리 탭 (시안 06, troop-growth.md, R-52): 부대마다 병종 그림, 병력(+반투명 부상)·훈련도·결속 게이지, [훈련]·[충원] 버튼,
    /// 병종 ⓘ 설명, 아래 줄 "승급 n가지 ›" → 승급 화면. 바꾸는 것은 모두 행동(unit.train·conscript·recruit·heal·promote)이라 온라인이면 서버도 같은 규칙.
    /// </summary>
    public sealed class TroopGrowthTabView : ITabView
    {
        /// <summary>[충원] 한 번에 늘리는 병력 (시안 06: +500).</summary>
        private const long RefillStep = 500;
        private const float CardHeight = 238f;
        private static string _notice;
        private static bool _noticeBad;

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "Units", 14, 25);
            Ui.Fill((RectTransform)list.parent.parent);
            var head = Ui.HeaderRow(list, ctx.T("ui.tab.troop"), ctx.T("ui.troops.count", ctx.State.Units.Count));
            Ui.Size(Ui.Rect(head, "InfoGap"), 70, 66).flexibleWidth = 0;
            var info = InfoButton.Add(root, ctx.T("ui.troops.note"));
            ((RectTransform)info.transform).anchoredPosition = new Vector2(-25f, -(25f + 50f - InfoButton.Size / 2f));
            if (_notice != null)
            {
                Ui.Size(Ui.Label(list, _notice, Ui.FontSmall, TextAnchor.MiddleLeft, _noticeBad ? Palette.Bad : Palette.Good), 50);
                _notice = null;
            }
            foreach (var u in ctx.State.Units) Card(app, root, list, u);
        }

        private static void Card(GameApp app, RectTransform root, RectTransform list, UnitState u)
        {
            var ctx = app.Context;
            var growth = new TroopGrowthService(ctx.Defs);
            var troops = new TroopService(ctx.Defs);
            string id = u.UnitId;
            long max = ctx.Defs.Balance.UnitMaxTroops;

            var card = Ui.Rect(list, id);
            Ui.Size(card, CardHeight);
            Ui.Round(Ui.Background(card, Palette.Card));

            Ui.Place(Ui.TroopPicture(ctx, card, u.TroopType, 196), 15, 21, 196, 196);

            const float tx = 230f;
            var name = Ui.Label(Ui.Place(Ui.Rect(card, "Name"), tx, 10, 500, 56), ctx.UnitLabel(id) + " · " + FormationTabView.TroopName(ctx, u.TroopType), 34);
            name.fontStyle = FontStyle.Bold;
            Ui.Fill(name.rectTransform);
            var infoBox = Ui.Place(Ui.Rect(card, "InfoBox"), 735, 18, 46, 46);
            string troopType = u.TroopType;
            Ui.Fill((RectTransform)Ui.SmallInfo(infoBox, anchor => TroopTip(ctx, root, anchor, troopType)).transform);

            var rows = Ui.Place(Ui.Rect(card, "Gauges"), tx, 58, 570, 134);
            Ui.Column(rows, 2, 0);
            float troopRatio = max > 0 ? (float)u.Troops / max : 0f, woundRatio = max > 0 ? (float)u.Wounded / max : 0f;
            Ui.GaugeRow(rows, ctx.T("ui.troops.row.troops"), troopRatio, new Color(0.247f, 0.435f, 0.69f), u.Troops.ToString("N0"),
                ctx.T("ui.troops.wounded", u.Wounded.ToString("N0"), max.ToString("N0")), woundRatio);
            long trainingPct = u.Training.Raw / 100;
            Ui.GaugeRow(rows, ctx.T("ui.troops.row.training"), trainingPct / 100f, Palette.Good, trainingPct + "%");
            long cohesion = u.Cohesion.FloorToLong(), cohesionMax = System.Math.Max(1, ctx.Defs.Campaign.CohesionMax.FloorToLong());
            Ui.GaugeRow(rows, ctx.T("ui.troops.row.cohesion"), (float)cohesion / cohesionMax, new Color(0.788f, 0.588f, 0.169f), cohesion.ToString());

            // 아래 줄: 승급 수 › (누르면 승급 화면) / 없으면 회색
            var promos = growth.PromotionsFor(u);
            string bottom = promos.Count > 0 ? ctx.T("ui.troops.promoteCount", promos.Count) : ctx.T("ui.troops.noPromotion");
            var line = Ui.Label(Ui.Place(Ui.Rect(card, "Bottom"), tx, 194, 570, 40), bottom, 24, TextAnchor.MiddleLeft, promos.Count > 0 ? Palette.Text : Palette.TextDim);
            Ui.Fill(line.rectTransform);
            if (promos.Count > 0)
            {
                line.raycastTarget = true;
                var b = line.gameObject.AddComponent<UnityEngine.UI.Button>();
                b.targetGraphic = line;
                b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); app.Show(new TroopPromoteScreen(id)); });
            }

            // 오른쪽 2단 버튼: [훈련 +n% / 돈] · [충원 +500 / 군량]
            string trainCheck = growth.CheckTrain(ctx.State, id);
            bool maxed = trainCheck == TroopGrowthService.MaxTraining;
            var train = Ui.TwoLineButton(card, ctx.T("ui.troops.trainShort", ctx.Defs.TroopGrowth.TrainingStep.Raw / 100),
                maxed ? ctx.T("ui.troops.trainMax") : ctx.T("ui.troops.cost.gold", growth.TrainCost(u).ToString("N0")),
                () => Do(app, PlayerActionRunner.TrainUnit(id), growth.CheckTrain(ctx.State, id)),
                trainCheck == TroopGrowthService.Ok, trainCheck == TroopGrowthService.NotEnough);
            Ui.Place((RectTransform)train.transform, 820, 18, 190, 100);

            long room = troops.Room(u);
            long step = System.Math.Min(RefillStep, room);
            var refillTop = ctx.T("ui.troops.refill", (step > 0 ? step : RefillStep).ToString("N0"));
            var refill = Ui.TwoLineButton(card, refillTop,
                room <= 0 ? ctx.T("ui.troops.refill.full") : ctx.T("ui.troops.cost.supply", troops.ConscriptCost(step).ToString("N0")),
                null, room > 0 || u.Wounded > 0);
            var refillRt = (RectTransform)refill.transform;
            Ui.Place(refillRt, 820, 126, 190, 100);
            refill.onClick.AddListener(() => RefillMenu(app, root, refillRt, u, step));
        }

        /// <summary>[충원] 고르는 메뉴: 징병(군량) · 모병(돈) · 치료(부상병, 군량).</summary>
        private static void RefillMenu(GameApp app, RectTransform root, RectTransform anchor, UnitState u, long step)
        {
            var ctx = app.Context;
            var troops = new TroopService(ctx.Defs);
            string id = u.UnitId;
            long supply = ctx.Resource(ResourceIds.Supply), gold = ctx.Resource(ResourceIds.Gold);
            long conscript = troops.ConscriptCost(step), recruit = troops.RecruitCost(step);
            long healCost = HealCostCalculator.HealCostInSupply(ctx.Defs.Balance, u.Wounded);
            long wounded = u.Wounded;
            Ui.PickMenu(root, anchor, 520,
                (ctx.T("ui.troops.refill.conscript", step.ToString("N0")), ctx.T("ui.troops.cost.supply", conscript.ToString("N0")), step > 0 && supply >= conscript,
                    () => Do(app, PlayerActionRunner.Conscript(id, step), null)),
                (ctx.T("ui.troops.refill.recruit", step.ToString("N0")), ctx.T("ui.troops.cost.gold", recruit.ToString("N0")), step > 0 && gold >= recruit,
                    () => Do(app, PlayerActionRunner.Recruit(id, step), null)),
                (ctx.T("ui.troops.refill.heal", wounded.ToString("N0")), ctx.T("ui.troops.cost.supply", healCost.ToString("N0")), wounded > 0 && supply >= healCost,
                    () => Do(app, PlayerActionRunner.Heal(id, wounded, ResourceIds.Supply), null)));
        }

        /// <summary>병종 설명 말풍선 (병종 이름 + troop.desc).</summary>
        private static void TroopTip(GameContext ctx, RectTransform root, RectTransform anchor, string troopType)
        {
            string desc = ctx.T("troop.desc." + troopType);
            var panel = Ui.Popover(root, anchor, 640, 190, new Color(0.23f, 0.2f, 0.16f, 0.96f));
            var v = panel.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            v.padding = new RectOffset(30, 30, 22, 22);
            v.spacing = 6;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandHeight = false;
            var n = Ui.Label(panel, FormationTabView.TroopName(ctx, troopType), 32, TextAnchor.MiddleLeft, Palette.PromptText);
            n.fontStyle = FontStyle.Bold;
            Ui.Label(panel, desc, 26, TextAnchor.UpperLeft, Palette.PromptText);
        }

        /// <summary>행동을 보내고 결과 한 줄을 남긴 채 탭을 다시 그린다. check가 null이면 행동 결과만 본다.</summary>
        internal static void Do(GameApp app, Game.Online.PlayerAction action, string check)
        {
            var ctx = app.Context;
            bool ok = (check == null || check == TroopGrowthService.Ok) && ctx.Do(action);
            _notice = ctx.T("ui.troops.result." + (ok ? TroopGrowthService.Ok : check ?? "failed"));
            _noticeBad = !ok;
            app.ShowMain("troop");
        }
    }

    /// <summary>
    /// 승급 화면 (시안 06 승급 상세): 부대 하나의 갈 수 있는 승급마다 카드 — "→ 병종", 설명, 조건 체크 줄(✓ 초록 / ✗ 빨강), [승급].
    /// 승급할 인원 조절·부분 승급은 승급 규칙을 바꾼 뒤에 붙인다 (06 변경점 "규칙 변경 할 일").
    /// </summary>
    public sealed class TroopPromoteScreen : GameScreen
    {
        private readonly string _unitId;
        public TroopPromoteScreen(string unitId) { _unitId = unitId; }

        public override void Build()
        {
            var u = Ctx.State.FindUnit(_unitId);
            if (u == null) { App.ShowMain("troop"); return; }
            var growth = new TroopGrowthService(Ctx.Defs);
            Ui.Column(Root, 0, 0);
            var list = Ui.ScrollList(Root, "Promotions", 20, 25);
            Ui.Size(list.parent.parent, flexibleHeight: 1);

            var head = Ui.Row(list, "Head", 100, 14);
            var hh = head.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            hh.childForceExpandWidth = false;
            hh.childAlignment = TextAnchor.MiddleLeft;
            Ui.Size(Ui.SecondaryButton(head, "‹", () => App.ShowMain("troop"), 80, 44), 80, 80).flexibleWidth = 0;
            Ui.TroopPicture(Ctx, head, u.TroopType, 80);
            var title = Ui.Label(head, T("ui.troops.promoteTitle", Ctx.UnitLabel(_unitId) + " · " + FormationTabView.TroopName(Ctx, u.TroopType)), 48);
            title.font = Ui.TitleFont;
            Ui.Size(title, 100, -1, -1, 1);

            long gold = Ctx.Resource(ResourceIds.Gold);
            long trainingPct = u.Training.Raw / 100;
            foreach (var p in growth.PromotionsFor(u))
            {
                string to = p.To;
                var card = Ui.Card(list, to, 28, 10);
                var name = Ui.Label(card, "→ " + FormationTabView.TroopName(Ctx, to), 50);
                name.font = Ui.TitleFont;
                Ui.Size(name, 70);
                Ui.Label(card, T("troop.desc." + to), 26, TextAnchor.MiddleLeft, Palette.TextDim);
                long needPct = p.Training.Raw / 100;
                Condition(card, trainingPct >= needPct, T("ui.troops.cond.training", needPct, trainingPct));
                if (p.Step != null) Condition(card, Ctx.State.ClearedSteps.Contains(p.Step), T("ui.troops.stepCleared", T(Ctx.Flow.LoadStep(p.Step).NameKey)));
                Condition(card, gold >= p.Gold, T("ui.troops.cond.gold", p.Gold.ToString("N0"), gold.ToString("N0")));
                string check = growth.CheckPromote(Ctx.State, _unitId, to);
                bool ok = check == TroopGrowthService.Ok;
                string unitId = _unitId;
                var b = Ui.PrimaryButton(card, ok ? T("ui.troops.promoteButton") : T("ui.troops.promoteLocked"), () =>
                {
                    TroopGrowthTabView.Do(App, PlayerActionRunner.PromoteUnit(unitId, to), growth.CheckPromote(Ctx.State, unitId, to));
                }, 90, 32);
                Ui.SetPrimaryEnabled(b, ok);
            }
        }

        private static void Condition(RectTransform card, bool met, string text)
        {
            var row = Ui.Row(card, "Condition", 52, 12);
            var h = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            var mark = Ui.Label(row, met ? "✓" : "✗", 34, TextAnchor.MiddleCenter, met ? Palette.Good : Palette.Bad);
            mark.fontStyle = FontStyle.Bold;
            Ui.Size(mark, 52, 44).flexibleWidth = 0;
            Ui.Size(Ui.Label(row, text, 30), 52, -1, -1, 1);
        }
    }
}
