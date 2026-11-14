using System.Collections.Generic;
using Game.Data.Defs;
using Game.Economy;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 스토리 탭 (시안 04): 머리 줄(진행도·도감·녹화) → 훈련 전투 줄 → "다음 도전" 큰 카드 → 구출 카드 → 깬 스텝 줄 → 다음(잠긴) 스텝 줄.
    /// 값은 <see cref="StoryBoard"/>에서 받아 그리기만 한다.
    /// </summary>
    public sealed class StoryTabView : ITabView
    {
        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "Steps", 14, 25);
            Ui.Fill((RectTransform)list.parent.parent);

            var (cleared, total) = StoryBoard.Progress(ctx.Defs, ctx.State);
            var head = Ui.HeaderRow(list, ctx.T("ui.story.header"), ctx.T("ui.story.progress", cleared, total));
            int found = SecretText.FoundCount(ctx, out int secrets);
            if (secrets > 0) Small(head, ctx.T("ui.codex.short", found, secrets), () => app.Show(new CodexScreen()));
            Small(head, ctx.T("ui.replay.short", ctx.Replays.Items.Count), () => app.Show(new ReplayListScreen()));

            // 훈련 전투 (R-85): 그 병종이 있으면 머리 아래 작은 버튼 (반복 가능)
            var drills = StoryBoard.Drills(ctx.Defs, ctx.Flow, ctx.State);
            if (drills.Count > 0)
            {
                var row = Ui.Row(list, "Drills", 72, 10);
                row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandWidth = false;
                foreach (var id in drills)
                {
                    string drillId = id, name;
                    try { name = ctx.T(StepDef.Load(ctx.Data, drillId).NameKey); } catch (System.Exception) { name = drillId; }
                    Ui.Size(Ui.SecondaryButton(row, name, () => app.Show(new StepScreen(drillId)), 72, 26), width: 260);
                }
            }

            var cards = StoryBoard.Build(ctx.Defs, ctx.Flow, ctx.State);
            foreach (var c in cards) if (c.IsNext) NextCard(app, list, c);
            foreach (var c in cards) if (c.IsRescue && c.State == StepCardState.Open) RescueCard(app, list, c);

            bool header = false;
            foreach (var c in cards)
            {
                if (c.IsRescue || c.State != StepCardState.Cleared) continue;
                if (!header) { Ui.SectionHeader(list, ctx.T("ui.story.sectionCleared")); header = true; }
                string id = c.Id;
                Ui.ListRow(list, "✓", Palette.Good, ctx.T(c.NameKey), ctx.T("ui.story.replay"), () => app.Show(new StepScreen(id)));
            }
            header = false;
            foreach (var c in cards)
            {
                if (c.IsRescue || c.IsNext || c.State == StepCardState.Cleared) continue;
                if (!header) { Ui.SectionHeader(list, ctx.T("ui.story.sectionNext")); header = true; }
                string id = c.Id;
                if (c.State == StepCardState.Open)
                    Ui.ListRow(list, "›", Palette.Text, ctx.T(c.NameKey), ctx.T("ui.story.open"), () => app.Show(new StepScreen(id)));
                else
                    Ui.ListRow(list, "@lock", Palette.TextDim, ctx.T(c.NameKey), ctx.T("ui.story.lockedHint." + c.LockReason), null, dim: true, height: 84);
            }
        }

        private static void Small(RectTransform head, string text, System.Action onClick) =>
            Ui.Size(Ui.SecondaryButton(head, text, onClick, 72, 26), 72, 170).flexibleWidth = 0;

        /// <summary>다음 도전: 주홍 테두리 카드 — 이름, 요약 한 줄, 첫 클리어 보상, [출진].</summary>
        private static void NextCard(GameApp app, RectTransform list, StepCard c)
        {
            var ctx = app.Context;
            var card = Ui.FeatureCard(list, ctx.T("ui.story.next"), Palette.ButtonActive);
            Name(card, ctx.T(c.NameKey));
            Ui.Label(card, Summary(ctx, c), 30, TextAnchor.MiddleLeft, Palette.TextDim);
            var rh = Ui.Label(card, ctx.T("ui.story.firstReward"), 30);
            rh.fontStyle = FontStyle.Bold;
            Ui.Label(card, Rewards(ctx, c), 30);
            string id = c.Id;
            Ui.PrimaryButton(card, ctx.T("ui.story.sortie"), () => app.Show(new StepScreen(id)), 130);
        }

        /// <summary>구출: 빨간 테두리 카드 — 이름, 갇힌 장수·남은 기한, [출진].</summary>
        private static void RescueCard(GameApp app, RectTransform list, StepCard c)
        {
            var ctx = app.Context;
            var card = Ui.FeatureCard(list, ctx.T("ui.story.rescue"), Palette.Bad);
            Name(card, ctx.T(c.NameKey));
            string line = c.RescueHeld > 0
                ? ctx.T("ui.rescue.listLine", c.RescueHeld, RescueInfo.Left(ctx, c.RescueDeadlineUnixSeconds))
                : ctx.T("ui.rescue.none");
            Ui.Label(card, line, 30, TextAnchor.MiddleLeft, Palette.Bad);
            string id = c.Id;
            Ui.PrimaryButton(card, ctx.T("ui.story.sortie"), () => app.Show(new StepScreen(id)), 130);
        }

        private static void Name(RectTransform card, string text)
        {
            var t = Ui.Label(card, text, 54, TextAnchor.MiddleLeft);
            t.font = Ui.TitleFont;
            Ui.Size(t, 80);
        }

        private static string Summary(GameContext ctx, StepCard c)
        {
            var parts = new List<string> { ctx.T("battleType." + c.BattleType) };
            if (c.MaxMinutes > Game.Foundation.Math.Fix.Zero) parts.Add(ctx.T("ui.story.minutes", c.MaxMinutes.FloorToLong()));
            parts.Add(ctx.T("ui.story.enemyCorps", c.EnemyCorps));
            return string.Join(" · ", parts);
        }

        private static string Rewards(GameContext ctx, StepCard c)
        {
            var parts = new List<string>();
            foreach (var kv in c.RewardResources) parts.Add(ctx.T("resource." + kv.Key) + " " + kv.Value.ToString("N0"));
            if (c.RewardExp > 0) parts.Add(ctx.T("ui.story.exp", c.RewardExp.ToString("N0")));
            foreach (var g in c.RewardGenerals) parts.Add(ctx.T("ui.story.joins", ctx.GeneralName(g)));
            foreach (var s in c.RewardSkills) parts.Add(ctx.Defs.Skills.TryGetValue(s, out var sd) ? ctx.T(sd.NameKey) : s);
            foreach (var f in c.RewardFormations) parts.Add(ctx.Defs.Formations.TryGetValue(f, out var fd) ? ctx.T(fd.NameKey) : f);
            return parts.Count == 0 ? ctx.T("ui.common.none") : string.Join(" · ", parts);
        }
    }

    /// <summary>구출 (E23): 수용소에 갇힌 우리 장수와 가장 짧은 남은 기한.</summary>
    internal static class RescueInfo
    {
        public static List<Game.Campaign.Roster.GeneralState> Held(Game.Unity.Boot.GameContext ctx, string camp)
        {
            var list = new List<Game.Campaign.Roster.GeneralState>();
            foreach (var g in ctx.State.Generals)
                if (g.Status == Game.Campaign.Roster.GeneralStatus.Captured && g.CampId == camp) list.Add(g);
            return list;
        }

        public static string Left(Game.Unity.Boot.GameContext ctx, long deadline)
        {
            long left = System.Math.Max(0, deadline - ctx.Clock.UtcNow.ToUnixTimeSeconds());
            return ctx.T("ui.rescue.time", left / 3600, left % 3600 / 60);
        }
    }

    /// <summary>스텝 정보 + 출진 (직접 지휘 / 위임). 출진 준비(Prepare)와 확정(Confirm)은 규칙 객체가 한다.</summary>
    /// <summary>
    /// 출진 준비 공통 조각 (10 시안): 전장 타일 · 적(대략, 내 닉네임 장수가 척후 파견을 알면 정확) · 전력 막대 + 판단 · 내 군단 카드 줄.
    /// 스토리 스텝(StepScreen)과 영토 공성전(TerritorySortieScreen)이 같이 쓴다. 글자는 줄이고 설명은 ⓘ.
    /// </summary>
    internal static class SortiePrep
    {
        public static void Tiles(GameContext ctx, Transform parent, StepDef step)
        {
            var row = Ui.Row(parent, "Field", 96, 8);
            foreach (var t in new[] { ctx.T("battleType." + step.BattleType), ctx.T("ui.sortie.minutes", step.MaxMinutes), ctx.T("timeOfDay." + step.TimeOfDay), ctx.T("weather." + step.Weather), (step.WindStrength.ToString() == "0" ? ctx.T("ui.battle.windNone") : ctx.T("ui.sortie.wind", ctx.T("ui.battle.windLevel." + step.WindStrength))) })
            {
                var tile = Ui.Rect(row, "Tile");
                Ui.Round(Ui.Background(tile, Palette.Card));
                Ui.Fill(Ui.Label(tile, t, 24, TextAnchor.MiddleCenter).rectTransform);
            }
        }

        /// <summary>내 닉네임 장수가 척후 파견을 안다 → 적 병력 정확히 (10 시안).</summary>
        public static bool Scout(GameContext ctx)
        {
            var me = ctx.State.FindGeneral(AvatarGeneral.GeneralId);
            return me != null && me.LearnedSkills.Contains("scoutDispatch");
        }

        public static void Enemies(GameContext ctx, Transform card, SortiePlan plan)
        {
            bool exact = Scout(ctx);
            if (exact) Ui.Label(card, Ui.Colored(ctx.T("ui.sortie.scout"), Palette.Ally), 22);
            foreach (var c in plan.Setup.Corps)
            {
                if (c.Team != StoryFlow.EnemyTeam) continue;
                long troops = 0;
                foreach (var u in c.Units) troops += u.Troops;
                string n = exact ? troops.ToString("N0") : "~" + SortieAdvice.Rough(troops).ToString("N0");
                string f = ctx.Defs.Formations.TryGetValue(c.Formation ?? "", out var fd) ? ctx.T(fd.NameKey) : c.Formation;
                Ui.Label(card, Ui.Colored(ctx.GeneralName(c.General?.Id), Palette.Enemy) + "  " + n + "  · " + f + (c.Reserve ? "  (" + ctx.T("ui.corps.reserve") + ")" : ""), Ui.FontSmall);
            }
        }

        public static long PlayerTroops(SortiePlan plan)
        {
            long t = 0;
            foreach (var c in plan.Setup.Corps) if (c.Team == StoryFlow.PlayerTeam) foreach (var u in c.Units) t += u.Troops;
            return t;
        }

        public static long EnemyTroops(SortiePlan plan)
        {
            long t = 0;
            foreach (var c in plan.Setup.Corps) if (c.Team == StoryFlow.EnemyTeam) foreach (var u in c.Units) t += u.Troops;
            return t;
        }

        /// <summary>전력: 파랑 내 병력 ─ 막대 ─ 빨강 상대 병력, 판단 알약 (우세 · 비슷 · 위험).</summary>
        public static void Power(GameContext ctx, Transform parent, long mine, long enemy)
        {
            var card = Ui.Card(parent, "Power", 18, 6);
            string judge = SortieAdvice.Judge(mine, enemy);
            var color = judge == SortieAdvice.Favorable ? Palette.Good : judge == SortieAdvice.Even ? Palette.Text : Palette.Bad;
            Ui.Tag(card, ctx.T("ui.sortie.judge." + judge), color, 24, 44);
            var row = Ui.Row(card, "Bar", 52, 10);
            Ui.Size(Ui.Label(row, Ui.Colored(mine.ToString("N0"), Palette.Ally), 28), width: 170);
            var g = Ui.Gauge(row, Palette.Ally, 26);
            Ui.Size(g, flexibleWidth: 1);
            Ui.SetGauge((RectTransform)g.GetChild(0), mine + enemy <= 0 ? 0.5f : (float)mine / (mine + enemy));
            Ui.Size(Ui.Label(row, Ui.Colored("~" + SortieAdvice.Rough(enemy).ToString("N0"), Palette.Enemy), 28, TextAnchor.MiddleRight), width: 170);
        }

        /// <summary>내 군단 카드 줄: 장수 · 병력 · 진형, 고용 표시, 영토 깃발에 나간 장수는 출진 불가 표시.</summary>
        public static void Army(GameContext ctx, Transform parent, SortiePlan plan)
        {
            if (plan.Compositions == null || plan.Compositions.Count == 0) return;
            var row = Ui.HScrollRow(parent, "Army", 170, 10, 4);
            foreach (var comp in plan.Compositions)
            {
                long troops = 0;
                foreach (var p in comp.Picks) troops += p.Troops;
                bool onFlag = false;
                if (TerritoryTabView.Owners != null) foreach (var o in TerritoryTabView.Owners.Values) if (o.OwnerAccountId == ctx.AccountId && o.GeneralId == comp.GeneralId) onFlag = true;
                bool hired = comp.GeneralId == AvatarGeneral.HiredId || ctx.State.FindGeneral(comp.GeneralId) == null;
                var card = Ui.Card(row, comp.GeneralId, 14, 4);
                Ui.Size(card, width: 300);
                Ui.Label(card, (comp.Commander ? "★ " : "") + ctx.GeneralName(comp.GeneralId) + (hired ? "  " + Ui.Colored(ctx.T("ui.formation.hired"), Palette.Ally) : ""), 24);
                Ui.Label(card, troops.ToString("N0") + "  · " + (ctx.Defs.Formations.TryGetValue(comp.Formation ?? "", out var fd) ? ctx.T(fd.NameKey) : comp.Formation), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                if (onFlag) Ui.Label(card, Ui.Colored(ctx.T("ui.terr.onFlag"), Palette.Bad), 20);
            }
        }
    }

    public sealed class StepScreen : GameScreen
    {
        /// <summary>스텝마다 고른 군량 분 (E22, 앱을 켜 둔 동안 기억). 없으면 자동(3분).</summary>
        private static readonly Dictionary<string, int> SupplyMinutes = new Dictionary<string, int>();
        private readonly string _stepId;
        public StepScreen(string stepId) { _stepId = stepId; }

        public override void Build()
        {
            var step = StepDef.Load(Ctx.Data, _stepId);
            var (header, body, footer) = Ui.PageLayout(Root, 130);
            var top = Ui.Row(header, "Title", 100, 12);
            Ui.Size(Ui.Button(top, "‹", () => App.ShowMain("story"), 90, fontSize: 40), width: 90);
            Ui.Size(Ui.Label(top, T(step.NameKey), Ui.FontTitle), flexibleWidth: 1);
            SortiePrep.Tiles(Ctx, header, step);
            InfoButton.Add(Root, T("ui.sortie.note"));

            // 구출 전투 (E23): 갇힌 장수마다 남은 기한, 승리 조건(수용소 점령)
            if (step.RescueCamp != null)
            {
                var rescue = Ui.Card(body, "Rescue");
                Ui.Label(rescue, T("ui.rescue.header", T("camp." + step.RescueCamp)));
                var held = RescueInfo.Held(Ctx, step.RescueCamp);
                if (held.Count == 0) Ui.Label(rescue, T("ui.rescue.none"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
                foreach (var g in held)
                    Ui.Label(rescue, T("ui.rescue.held", Ui.Colored(Ctx.GeneralName(g.GeneralId), Palette.Ally), RescueInfo.Left(Ctx, g.RescueDeadlineUnixSeconds)), Ui.FontSmall);
                foreach (var o in step.Objects)
                    if (o.Type == "zone" && o.Team == "player")
                        Ui.Label(rescue, T("ui.rescue.goal", o.Seconds.FloorToLong()), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good);
            }

            // 고용 (온라인일 때, R-43): 고른 것을 편성 사본에 넣어 출진 준비에 넘긴다
            var request = Request(step, false, out var hireError);
            // 출진 준비 (상태는 바꾸지 않는다)
            var plan = Ctx.Flow.Prepare(Ctx.State, request);
            int minutes = SupplyMinutes.TryGetValue(_stepId, out var mm) ? mm : SortieAdvice.AutoMinutes;
            if (minutes != SortieAdvice.AutoMinutes && plan.AutoSupply > 0)
            {
                request.Supply = SortieAdvice.SupplyFor(plan.AutoSupply, minutes);
                plan = Ctx.Flow.Prepare(Ctx.State, request);
            }

            // 적 (대략, 척후 파견이면 정확)
            var enemies = Ui.Card(body, "Enemies");
            int reserve = 0;
            foreach (var e in step.Enemy) if (e.Reserve) reserve++;
            Ui.Label(enemies, T("ui.step.enemies", step.Enemy.Count, reserve), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            if (plan.Setup != null) SortiePrep.Enemies(Ctx, enemies, plan);

            var extra = Ui.Row(body, "Extra", 84, 10);
            if (Ctx.OnlineEnabled) Ui.Button(extra, T("ui.shared.button"), () => App.Show(new SharedReplayScreen(_stepId)), 84, Palette.Card, 26); // 공략 (R-81)
            var parts = new List<string>();
            foreach (var kv in step.RewardResources) parts.Add(T("resource." + kv.Key) + " " + kv.Value.ToString("N0"));
            foreach (var f in step.RewardFormations) parts.Add(FormationName(f));
            foreach (var s in step.RewardSkills) parts.Add(SkillName(s));
            var reward = Ui.Card(extra, "Rewards", 12, 2);
            Ui.Label(reward, Ui.Colored(T("ui.sortie.firstClear"), Palette.Seal) + "  " + (parts.Count == 0 ? T("ui.common.none") : string.Join(", ", parts)), 22);

            if (plan.Setup != null) SortiePrep.Power(Ctx, body, SortiePrep.PlayerTroops(plan), SortiePrep.EnemyTroops(plan));
            if (step.RecommendedLevel > 0) Ui.Label(body, T("ui.step.recommendedLevel", step.RecommendedLevel), 22, TextAnchor.MiddleLeft, Palette.TextDim);

            Ui.Label(body, T("ui.step.allies") + "  " + Ui.Colored(T("ui.step.situation", T("battleType." + step.BattleType)), Palette.TextDim), Ui.FontSmall);
            SortiePrep.Army(Ctx, body, plan);
            var armyRow = Ui.Row(body, "ArmyButtons", 72, 10);
            Ui.Button(armyRow, T("ui.sortie.formation"), () => App.ShowMain("formation"), 72, fontSize: 24);
            if (Ctx.HiringAvailable) Ui.Button(armyRow, T("ui.sortie.mercenary"), () => App.ShowMain("hiring"), 72, fontSize: 24);
            if (Ctx.HiringAvailable && !Ctx.HiresFor(_stepId).IsEmpty) HireCard(body, step);

            var sortie = Ui.Card(body, "Sortie");
            if (hireError != null) Ui.Label(sortie, Ui.Colored(hireError, Palette.Bad), Ui.FontSmall);
            if (plan.Hired != null)
                foreach (var kv in plan.Hired.Costs) Ui.Label(sortie, T("ui.hire.cost", kv.Value.ToString("N0") + " " + T("resource." + kv.Key)), Ui.FontSmall);
            Ui.Label(sortie, T("ui.sortie.supply", plan.SupplyTaken.ToString("N0"), Ctx.Resource(ResourceIds.Supply).ToString("N0")), Ui.FontSmall);
            var supplyRow = Ui.Row(sortie, "SupplyRow", 76, 8);
            Ui.Button(supplyRow, "−", () => SetMinutes(minutes - 1), 76, fontSize: 32);
            Ui.Size(Ui.Label(supplyRow, T("ui.step.supplyMinutes", minutes), 28, TextAnchor.MiddleCenter), flexibleWidth: 2);
            Ui.Button(supplyRow, "+", () => SetMinutes(minutes + 1), 76, fontSize: 32);
            long wallet = Ctx.Resource(ResourceIds.Supply);
            if (plan.SupplyTaken > wallet)
            {
                Ui.Label(sortie, Ui.Colored(T("ui.step.supplyShort", (plan.SupplyTaken - wallet).ToString("N0")), Palette.Bad), Ui.FontSmall);
                var fix = Ui.Row(sortie, "SupplyFix", 76, 8);
                int can = SortieAdvice.AffordableMinutes(plan.AutoSupply, wallet);
                var fit = Ui.Button(fix, T("ui.step.supplyFit", can), () => SetMinutes(can), 76, fontSize: 26);
                Ui.SetEnabled(fit, can > 0);
                Ui.Button(fix, T("ui.top.shop"), () => App.ShowMain("shop"), 76, fontSize: 26);
            }
            foreach (var err in plan.Errors) Ui.Label(sortie, Ui.Colored(err, Palette.Bad), Ui.FontSmall);

            Ui.Size(Ui.Button(footer, T("ui.common.back"), () => App.ShowMain("story"), 100), width: 200);
            var delegated = Ui.Button(footer, T("ui.sortie.delegate"), () => Sortie(step, true), 100);
            Ui.Size(delegated, width: 240);
            var direct = Ui.PrimaryButton(footer, T("ui.sortie.go"), () => Sortie(step, false), 100);
            Ui.Size(direct, flexibleWidth: 1);
            Ui.SetEnabled(direct, plan.Ok && hireError == null);
            Ui.SetEnabled(delegated, plan.Ok && hireError == null);
            GuideOverlay.Show(App, Root, "screen.step"); // 처음 안내 (R-62)
        }

        private void SetMinutes(int m)
        {
            SupplyMinutes[_stepId] = System.Math.Max(SortieAdvice.MinMinutes, System.Math.Min(SortieAdvice.MaxMinutes, m));
            App.Show(new StepScreen(_stepId));
        }

        /// <summary>출진 요청: 고른 고용이 있으면 그 편성(사본)과 고용을 싣는다.</summary>
        private SortieRequest Request(StepDef step, bool delegate_, out string hireError)
        {
            hireError = null;
            var request = new SortieRequest { StepId = _stepId, Seed = Ctx.NewSeed(), Delegate = delegate_ };
            var draft = Ctx.HiresFor(_stepId);
            if (draft.IsEmpty) return request;
            var (comps, hired) = draft.Build(Ctx.State, step.BattleType, Ctx.AccountId, out hireError);
            if (hireError != null) return request;
            request.Compositions = comps;
            request.Hired = hired;
            return request;
        }

        private void HireCard(RectTransform body, StepDef step)
        {
            var card = Ui.Card(body, "Hire");
            Ui.Label(card, T("ui.hire.sortieTitle"));
            var draft = Ctx.HiresFor(_stepId);
            var comps = Ctx.State.CompositionsFor(step.BattleType);
            foreach (var p in draft.Picks)
            {
                string id = p.Listing.Id;
                string who = p.CorpsIndex < comps.Count ? Ctx.GeneralName(comps[p.CorpsIndex].GeneralId) : "?";
                string text = p.Listing.Kind == Game.Online.HireKinds.General
                    ? T("ui.hire.pickGeneral", Ctx.GeneralName(p.Listing.GeneralId), p.CorpsIndex + 1, who)
                    : T("ui.hire.pickUnit", FormationTabView.TroopName(Ctx, p.Listing.TroopType), p.Count.ToString("N0"), p.CorpsIndex + 1, who);
                var row = Ui.Row(card, id, 72, 8);
                Ui.Size(Ui.Label(row, text, 26), flexibleWidth: 3);
                Ui.Size(Ui.Button(row, T("ui.hire.remove"), () => { draft.Remove(id); App.Show(new StepScreen(_stepId)); }, 72, fontSize: 26), width: 160);
            }
            var buttons = Ui.Row(card, "HireButtons", 80, 8);
            Ui.Button(buttons, T("ui.hire.hireGeneral"), () => App.Show(new HirePickScreen(_stepId, Game.Online.HireKinds.General)), 80, fontSize: 28);
            Ui.Button(buttons, T("ui.hire.hireUnit"), () => App.Show(new HirePickScreen(_stepId, Game.Online.HireKinds.Unit)), 80, fontSize: 28);
        }

        /// <summary>직접 지휘(위임 꺼짐) 또는 위임 출진. 확정할 때 준비를 다시 한다 (시드·위임 여부). 고용 비용도 확정 때 낸다.</summary>
        private void Sortie(StepDef step, bool delegate_)
        {
            Ctx.Flow.Tick(Ctx.State); // 출진 시각 기준 시간 경과 (서버도 같은 시각으로 맞춘다, R-61)
            var request = Request(step, delegate_, out var hireError);
            var plan = Ctx.Flow.Prepare(Ctx.State, request);
            if (SupplyMinutes.TryGetValue(_stepId, out var m) && m != SortieAdvice.AutoMinutes && plan.AutoSupply > 0)
            {
                request.Supply = SortieAdvice.SupplyFor(plan.AutoSupply, m); // 고른 군량 분 (E22)
                plan = Ctx.Flow.Prepare(Ctx.State, request);
            }
            if (hireError != null || !Ctx.Flow.Confirm(Ctx.State, plan)) { App.Show(new StepScreen(_stepId)); return; }
            Ctx.ClearHires();
            App.Show(new DialogueScreen(plan, "dialogue." + step.Id + ".start", !delegate_));
        }

        private string FormationName(string id) => Ctx.Defs.Formations.TryGetValue(id, out var f) ? T(f.NameKey) : id;
        private string SkillName(string id) => Ctx.Defs.Skills.TryGetValue(id, out var s) ? T(s.NameKey) : id;
    }

    /// <summary>
    /// 전투 전 대사 (story.md 1.3): data/Dialogue/&lt;스텝&gt;.json의 before를 한 줄씩 [다음], [건너뛰기]로 바로 전투.
    /// 대사 파일이 없으면 예전 한 줄 대사(키)를 쓴다. 끝나면 대결(직접 지휘일 때) 또는 전투.
    /// </summary>
    public sealed class DialogueScreen : GameScreen
    {
        private readonly SortiePlan _plan;
        private readonly string _lineKey;
        private readonly bool _playContests;
        private readonly int _index;

        /// <param name="playContests">전투 직전 대결(일기토·논쟁)을 직접 고를지 (직접 지휘). 아니면 AI가 고른다.</param>
        public DialogueScreen(SortiePlan plan, string lineKey, bool playContests = false, int index = 0) { _plan = plan; _lineKey = lineKey; _playContests = playContests; _index = index; }

        public override void Build()
        {
            var lines = DialogueScript.Load(Ctx.Data, _plan.Step.Id).Before;
            string speaker, text;
            int count = lines.Count;
            if (count > 0)
            {
                var l = lines[System.Math.Min(_index, count - 1)];
                speaker = l.Speaker != null ? Ctx.GeneralName(l.Speaker) : "";
                text = T(l.Key);
            }
            else
            {
                speaker = _plan.Setup.Corps.Count > 0 && _plan.Setup.Corps[0].General != null ? Ctx.GeneralName(_plan.Setup.Corps[0].General.Id) : "";
                text = Ctx.Defs.Strings.ContainsKey(_lineKey) ? T(_lineKey) : "…";
            }
            bool last = count == 0 || _index >= count - 1;
            DialogueView.Show(Ctx, Root, T(_plan.Step.NameKey), speaker, text,
                last ? null : (System.Action)(() => App.Show(new DialogueScreen(_plan, _lineKey, _playContests, _index + 1))),
                Begin, last ? (_playContests && _plan.Setup.Contests.Count > 0 ? T("ui.contest.challenge") : T("ui.dialogue.next")) : null);
        }

        private void Begin()
        {
            bool contest = _playContests && _plan.Setup.Contests.Count > 0;
            App.Show(contest ? (GameScreen)new ContestScreen(_plan, 0) : new BattleScreen(_plan));
        }
    }

    /// <summary>전투 뒤 대사 (이긴 스텝의 after). 끝나면(또는 건너뛰면) 스토리 탭.</summary>
    public sealed class AfterDialogueScreen : GameScreen
    {
        private readonly string _stepId;
        private readonly int _index;
        public AfterDialogueScreen(string stepId, int index = 0) { _stepId = stepId; _index = index; }

        public static bool Has(GameContext ctx, string stepId) => DialogueScript.Load(ctx.Data, stepId).After.Count > 0;

        public override void Build()
        {
            var lines = DialogueScript.Load(Ctx.Data, _stepId).After;
            if (lines.Count == 0) { App.ShowMain("story"); return; }
            var l = lines[System.Math.Min(_index, lines.Count - 1)];
            bool last = _index >= lines.Count - 1;
            var step = Ctx.Flow.LoadStep(_stepId);
            DialogueView.Show(Ctx, Root, T(step.NameKey), l.Speaker != null ? Ctx.GeneralName(l.Speaker) : "", T(l.Key),
                last ? null : (System.Action)(() => App.Show(new AfterDialogueScreen(_stepId, _index + 1))),
                () => App.ShowMain("story"), last ? T("ui.result.toStory") : null);
        }
    }

    /// <summary>대사 한 장 그리기: 스텝 이름, 말하는 사람, 문구, [다음] 또는 마지막 버튼, [건너뛰기].</summary>
    internal static class DialogueView
    {
        /// <param name="next">다음 줄 (마지막이면 null).</param>
        /// <param name="finish">끝 / 건너뛰기.</param>
        /// <param name="finishLabel">마지막 줄의 버튼 문구 (마지막이 아니면 null).</param>
        public static void Show(GameContext ctx, RectTransform root, string title, string speaker, string text, System.Action next, System.Action finish, string finishLabel)
        {
            Ui.Column(root, 24, 48).childAlignment = TextAnchor.LowerCenter;
            Ui.Size(Ui.Label(root, title, Ui.FontTitle, TextAnchor.MiddleCenter), 200);
            Ui.Size(Ui.Label(root, "", Ui.FontBody), flexibleHeight: 1);
            var box = Ui.Card(root, "Box", 40, 16);
            Ui.Label(box, Ui.Colored(speaker, Palette.Ally), Ui.FontBody);
            Ui.Size(Ui.Label(box, text, Ui.FontBody, TextAnchor.UpperLeft), 200);
            var row = Ui.Row(root, "Buttons", 140);
            if (next != null)
            {
                Ui.Button(row, ctx.T("ui.dialogue.skip"), finish, 140, Palette.Card);
                Ui.Button(row, ctx.T("ui.dialogue.more"), next, 140);
            }
            else Ui.Button(row, finishLabel, finish, 140);
        }
    }
}
