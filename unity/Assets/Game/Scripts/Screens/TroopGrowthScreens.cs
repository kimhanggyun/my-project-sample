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
    /// 병종 ⓘ 설명, 아래 줄 "승급 n가지 ›" → 승급 화면(같은 탭 안). 승급으로 갈라져 나온 부대는 기본 부대 아래에 들여쓴 카드 (나무).
    /// 바꾸는 것은 모두 행동(unit.train·conscript·recruit·heal·promote)이라 온라인이면 서버도 같은 규칙.
    /// </summary>
    public sealed class TroopGrowthTabView : ITabView
    {
        /// <summary>[충원] 한 번에 늘리는 병력 (시안 06: +500).</summary>
        private const long RefillStep = 500;
        /// <summary>승급할 인원 조절 단위 (사용자 결정 2026-11-14: 100명).</summary>
        internal const long PromoteStep = 100;
        private const float CardHeight = 238f, Indent = 70f, Spacing = 14f;
        private static readonly Color LineColor = new Color(0.47f, 0.43f, 0.37f);

        private static string _notice;
        private static bool _noticeBad;
        /// <summary>다음에 그릴 때 목록 대신 열 승급 화면 (그 부대 id). 한 번 그리면 지운다.</summary>
        private static string _promoteUnit;
        /// <summary>승급 화면에서 고른 인원 ("부대|병종" → 인원). 목록에서 새로 열면 지운다.</summary>
        private static readonly Dictionary<string, long> Amounts = new Dictionary<string, long>();

        /// <summary>메인 화면 부대 관리 탭에서 그 부대의 승급 화면을 연다 (fresh면 고른 인원을 처음으로).</summary>
        public static void OpenPromote(GameApp app, string unitId, bool fresh = true)
        {
            PreparePromote(unitId, fresh);
            app.ShowMain("troop");
        }

        /// <summary>다음 탭 그리기에서 승급 화면이 열리게만 한다 (캡처 도구용).</summary>
        public static void PreparePromote(string unitId, bool fresh = true)
        {
            if (fresh) Amounts.Clear();
            _promoteUnit = unitId;
        }

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            string promote = _promoteUnit;
            _promoteUnit = null;
            if (promote != null && ctx.State.FindUnit(promote) != null) { TroopPromoteView.Build(app, root, promote, Amounts); return; }

            var list = Ui.ScrollList(root, "Units", (int)Spacing, 25);
            Ui.Fill((RectTransform)list.parent.parent);
            int roots = 0;
            foreach (var u in ctx.State.Units) if (Depth(ctx, u) == 0) roots++;
            var head = Ui.HeaderRow(list, ctx.T("ui.tab.troop"), ctx.T("ui.troops.count", roots));
            Ui.Size(Ui.Rect(head, "InfoGap"), 70, 66).flexibleWidth = 0;
            var info = InfoButton.Add(root, ctx.T("ui.troops.note"));
            ((RectTransform)info.transform).anchoredPosition = new Vector2(-25f, -(25f + 50f - InfoButton.Size / 2f));
            if (_notice != null)
            {
                Ui.Size(Ui.Label(list, _notice, Ui.FontSmall, TextAnchor.MiddleLeft, _noticeBad ? Palette.Bad : Palette.Good), 50);
                _notice = null;
            }
            var units = ctx.State.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                bool laterSibling = false;
                for (int j = i + 1; j < units.Count && Depth(ctx, units[j]) >= Depth(ctx, u); j++)
                    if (units[j].ParentUnitId == u.ParentUnitId && Depth(ctx, units[j]) == Depth(ctx, u)) { laterSibling = true; break; }
                Card(app, root, list, u, Depth(ctx, u), laterSibling);
            }
        }

        /// <summary>나무 깊이: 기본 부대 0, 그 아래 승급 부대 1, …</summary>
        internal static int Depth(GameContext ctx, UnitState u)
        {
            int d = 0;
            for (var p = u.ParentUnitId; p != null && d < 8; d++)
            {
                var parent = ctx.State.FindUnit(p);
                if (parent == null) break;
                p = parent.ParentUnitId;
            }
            return d;
        }

        private static void Card(GameApp app, RectTransform root, RectTransform list, UnitState u, int depth, bool laterSibling)
        {
            var ctx = app.Context;
            var growth = new TroopGrowthService(ctx.Defs);
            var troops = new TroopService(ctx.Defs);
            string id = u.UnitId;
            long max = Game.Campaign.Roster.Army.TroopMax(ctx.Defs, ctx.State); // 계급의 병종 최대 병력 (army-v2.md 2장)
            float dx = Indent * depth;

            var holder = Ui.Rect(list, id);
            Ui.Size(holder, CardHeight);
            if (depth > 0) TreeLine(holder, dx, laterSibling);
            var card = Ui.Rect(holder, "Card");
            Ui.Fill(card);
            card.offsetMin = new Vector2(dx, 0);
            Ui.Round(Ui.Background(card, Palette.Card));

            float pic = depth > 0 ? 166f : 196f;
            Ui.Place(Ui.TroopPicture(ctx, card, u.TroopType, pic), 15, (CardHeight - pic) / 2f, pic, pic);
            float tx = 15f + pic + 19f, width = 1030f - dx;

            string troopName = FormationTabView.TroopName(ctx, u.TroopType);
            var parent = u.ParentUnitId != null ? ctx.State.FindUnit(u.ParentUnitId) : null;
            string title = troopName; // 병종마다 묶음 하나 (army-v2.md 3장)
            var name = Ui.Label(Ui.Place(Ui.Rect(card, "Name"), tx, 10, width - tx - 300, 56), title, 34);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Fill(name.rectTransform);
            var infoBox = PlaceRight(Ui.Rect(card, "InfoBox"), 249, 18, 46, 46);
            string troopType = u.TroopType;
            Ui.Fill((RectTransform)Ui.SmallInfo(infoBox, anchor => TroopTip(ctx, root, anchor, troopType)).transform);

            var rows = Ui.Place(Ui.Rect(card, "Gauges"), tx, 58, width - tx - 230, 134);
            Ui.Column(rows, 2, 0);
            float bar = 270f - (196f - pic) - dx * 0.5f;
            float troopRatio = max > 0 ? (float)u.Troops / max : 0f, woundRatio = max > 0 ? (float)u.Wounded / max : 0f;
            Ui.GaugeRow(rows, ctx.T("ui.troops.row.troops"), troopRatio, new Color(0.247f, 0.435f, 0.69f), u.Troops.ToString("N0"),
                ctx.T("ui.troops.wounded", u.Wounded.ToString("N0"), max.ToString("N0")), woundRatio, bar);
            long trainingPct = u.Training.Raw / 100;
            Ui.GaugeRow(rows, ctx.T("ui.troops.row.training"), trainingPct / 100f, Palette.Good, trainingPct + "%", barWidth: bar);
            long cohesion = u.Cohesion.FloorToLong(), cohesionMax = System.Math.Max(1, ctx.Defs.Campaign.CohesionMax.FloorToLong());
            Ui.GaugeRow(rows, ctx.T("ui.troops.row.cohesion"), (float)cohesion / cohesionMax, new Color(0.788f, 0.588f, 0.169f), cohesion.ToString(), barWidth: bar);

            // 아래 줄: (승급 부대면 어디서 왔나 ·) 승급 수 › / 다음 승급 없음 / 모두 승급함
            var promos = growth.PromotionsFor(u);
            bool hasChild = ctx.State.Units.Exists(x => x.ParentUnitId == id);
            string bottom;
            if (parent != null)
                bottom = ctx.T("ui.troops.promotedFrom", FormationTabView.TroopName(ctx, parent.TroopType)) + " · ";
            else bottom = "";
            if (u.Troops == 0 && u.Wounded == 0 && hasChild) bottom += ctx.T("ui.troops.allPromoted", troopName);
            else bottom += promos.Count > 0 ? ctx.T("ui.troops.promoteCount", promos.Count) : ctx.T("ui.troops.noPromotion");
            bool canOpen = promos.Count > 0 && u.Troops > 0;
            var line = Ui.Label(Ui.Place(Ui.Rect(card, "Bottom"), tx, 194, width - tx - 220, 40), bottom, 24, TextAnchor.MiddleLeft, canOpen ? Palette.Text : Palette.TextDim);
            line.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Fill(line.rectTransform);
            if (canOpen)
            {
                line.raycastTarget = true;
                var b = line.gameObject.AddComponent<UnityEngine.UI.Button>();
                b.targetGraphic = line;
                b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); OpenPromote(app, id); });
            }

            // 오른쪽 2단 버튼: [훈련 +n% / 돈] · [충원 +500 / 군량]
            string trainCheck = growth.CheckTrain(ctx.State, id);
            bool maxed = trainCheck == TroopGrowthService.MaxTraining;
            var train = Ui.TwoLineButton(card, ctx.T("ui.troops.trainShort", ctx.Defs.TroopGrowth.TrainingStep.Raw / 100),
                maxed ? ctx.T("ui.troops.trainMax") : ctx.T("ui.troops.cost.gold", growth.TrainCost(u).ToString("N0")),
                () => Do(app, PlayerActionRunner.TrainUnit(id), growth.CheckTrain(ctx.State, id)),
                trainCheck == TroopGrowthService.Ok, trainCheck == TroopGrowthService.NotEnough);
            PlaceRight((RectTransform)train.transform, 20, 18, 190, 100);

            long room = troops.Room(ctx.State, u);
            long step = System.Math.Min(RefillStep, room);
            var refillTop = ctx.T("ui.troops.refill", (step > 0 ? step : RefillStep).ToString("N0"));
            var refill = Ui.TwoLineButton(card, refillTop,
                room <= 0 ? ctx.T("ui.troops.refill.full") : ctx.T("ui.troops.cost.supply", troops.ConscriptCost(step).ToString("N0")),
                null, room > 0 || u.Wounded > 0);
            var refillRt = (RectTransform)refill.transform;
            PlaceRight(refillRt, 20, 126, 190, 100);
            refill.onClick.AddListener(() => RefillMenu(app, root, refillRt, u, step));
        }

        /// <summary>오른쪽 위 기준으로 자리·크기 (들여쓴 카드에서도 오른쪽 버튼이 같은 자리에).</summary>
        private static RectTransform PlaceRight(RectTransform rt, float fromRight, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-fromRight, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>나무 선: 위 부대 그림 칸 아래에서 내려와 이 카드 가운데로 꺾인다. 아래에 같은 줄의 형제가 더 있으면 끝까지 내려간다.</summary>
        private static void TreeLine(RectTransform holder, float dx, bool continueDown)
        {
            float x = dx - Indent + 35f;
            float top = Spacing + 12f; // 위 카드 안쪽까지 조금 올라가 이어 보이게
            float bottom = continueDown ? CardHeight : CardHeight / 2f;
            var v = Ui.Rect(holder, "TreeV");
            Ui.Place(v, x, -top, 5, top + bottom);
            Ui.Background(v, LineColor).raycastTarget = false;
            var h = Ui.Rect(holder, "TreeH");
            Ui.Place(h, x, CardHeight / 2f - 2.5f, dx - x, 5);
            Ui.Background(h, LineColor).raycastTarget = false;
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

        /// <summary>행동을 보내고 결과 한 줄을 남긴 채 목록을 다시 그린다. check가 null이면 행동 결과만 본다.</summary>
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
    /// 승급 화면 (시안 06 승급 상세, 부대 관리 탭 안): 부대 하나의 갈 수 있는 승급마다 카드 — "→ 병종", 설명, 조건 체크 줄(✓ 초록 / ✗ 빨강),
    /// 승급할 인원 [−] n [+] / 병력 · [모두] (100명 단위), [승급]. 고른 인원만 아래 승급 부대로 갈라 나간다 (R-52).
    /// </summary>
    internal static class TroopPromoteView
    {
        public static void Build(GameApp app, RectTransform root, string unitId, Dictionary<string, long> amounts)
        {
            var ctx = app.Context;
            var u = ctx.State.FindUnit(unitId);
            var growth = new TroopGrowthService(ctx.Defs);
            var list = Ui.ScrollList(root, "Promotions", 20, 25);
            Ui.Fill((RectTransform)list.parent.parent);

            var head = Ui.Row(list, "Head", 100, 14);
            var hh = head.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            hh.childForceExpandWidth = false;
            hh.childAlignment = TextAnchor.MiddleLeft;
            Ui.Size(Ui.SecondaryButton(head, "‹", () => app.ShowMain("troop"), 80, 44), 80, 80).flexibleWidth = 0;
            Ui.TroopPicture(ctx, head, u.TroopType, 80);
            var title = Ui.Label(head, ctx.T("ui.troops.promoteTitle", FormationTabView.TroopName(ctx, u.TroopType)), 48);
            title.font = Ui.TitleFont;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Size(title, 100, -1, -1, 1);

            long gold = ctx.Resource(ResourceIds.Gold);
            long trainingPct = u.Training.Raw / 100;
            foreach (var p in growth.PromotionsFor(u))
            {
                string to = p.To, key = unitId + "|" + to;
                long n = amounts.TryGetValue(key, out var a) ? a : u.Troops;
                n = System.Math.Max(System.Math.Min(n, u.Troops), System.Math.Min(TroopGrowthTabView.PromoteStep, u.Troops));
                long cost = growth.PromoteCost(p, n);

                var card = Ui.Card(list, to, 28, 10);
                var name = Ui.Label(card, "→ " + FormationTabView.TroopName(ctx, to), 50);
                name.font = Ui.TitleFont;
                Ui.Size(name, 70);
                Ui.Label(card, ctx.T("troop.desc." + to), 26, TextAnchor.MiddleLeft, Palette.TextDim);
                long needPct = p.Training.Raw / 100;
                Condition(card, trainingPct >= needPct, ctx.T("ui.troops.cond.training", needPct, trainingPct));
                if (p.Step != null) Condition(card, ctx.State.ClearedSteps.Contains(p.Step), ctx.T("ui.troops.stepCleared", ctx.T(ctx.Flow.LoadStep(p.Step).NameKey)));
                Condition(card, gold >= cost, ctx.T("ui.troops.cond.gold", cost.ToString("N0"), gold.ToString("N0")));

                long step = TroopGrowthTabView.PromoteStep;
                Ui.Stepper(card, ctx.T("ui.troops.promoteAmount"), n, u.Troops, ctx.T("ui.troops.promoteAll"),
                    v => { amounts[key] = v; TroopGrowthTabView.OpenPromote(app, unitId, false); },
                    n - step < step ? System.Math.Min(step, u.Troops) : n - step,
                    System.Math.Min(u.Troops, (n / step + 1) * step));

                string check = growth.CheckPromote(ctx.State, unitId, to, n);
                bool ok = check == TroopGrowthService.Ok;
                long count = n;
                var b = Ui.PrimaryButton(card, ok ? ctx.T("ui.troops.promoteButton") + "  " + ctx.T("ui.troops.promoteCountLine", count.ToString("N0")) : ctx.T("ui.troops.promoteLocked." + check), () =>
                    TroopGrowthTabView.Do(app, PlayerActionRunner.PromoteUnit(unitId, to, count), growth.CheckPromote(ctx.State, unitId, to, count)), 90, 32);
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
