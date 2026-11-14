using System.Collections.Generic;
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
    /// 전투 전 대사 (story.md 1.3, 17 시안): data/Dialogue/&lt;스텝&gt;.json의 before. 대사 파일이 없으면 예전 한 줄 대사(키).
    /// 끝나면(또는 건너뛰면) 대결(직접 지휘일 때) 또는 전투. 첫 장면에 장 표시.
    /// </summary>
    public sealed class DialogueScreen : DialogueScene
    {
        private readonly SortiePlan _plan;
        private readonly string _lineKey;
        private readonly bool _playContests;

        /// <param name="playContests">전투 직전 대결(일기토·논쟁)을 직접 고를지 (직접 지휘). 아니면 AI가 고른다.</param>
        public DialogueScreen(SortiePlan plan, string lineKey, bool playContests = false, int index = 0) { _plan = plan; _lineKey = lineKey; _playContests = playContests; }

        protected override StepDef Step => _plan.Step;
        protected override bool ShowChapter => true;

        protected override List<DialogueLine> Lines()
        {
            var lines = new List<DialogueLine>(DialogueScript.Load(Ctx.Data, _plan.Step.Id).Before);
            if (lines.Count > 0) return lines;
            string speaker = _plan.Setup.Corps.Count > 0 && _plan.Setup.Corps[0].General != null ? _plan.Setup.Corps[0].General.Id : null;
            return new List<DialogueLine> { DialogueScript.Line(speaker, Ctx.Defs.Strings.ContainsKey(_lineKey ?? "") ? _lineKey : null) };
        }

        protected override void Finish()
        {
            bool contest = _playContests && _plan.Setup.Contests.Count > 0;
            App.Show(contest ? (GameScreen)new ContestScreen(_plan, 0) : new BattleScreen(_plan));
        }
    }

    /// <summary>전투 뒤 대사 (이긴 스텝의 after, 17 시안). 끝나면(또는 건너뛰면) 스토리 탭.</summary>
    public sealed class AfterDialogueScreen : DialogueScene
    {
        private readonly string _stepId;
        private StepDef _step;
        public AfterDialogueScreen(string stepId, int index = 0) { _stepId = stepId; }

        public static bool Has(GameContext ctx, string stepId) => DialogueScript.Load(ctx.Data, stepId).After.Count > 0;

        protected override StepDef Step => _step ?? (_step = Ctx.Flow.LoadStep(_stepId));
        protected override List<DialogueLine> Lines() => new List<DialogueLine>(DialogueScript.Load(Ctx.Data, _stepId).After);
        protected override void Finish() => App.ShowMain("story");
    }

    /// <summary>
    /// 대화 장면 (17 시안): 배경(스텝 지형 수묵 자리 · 없으면 한지) · 반신 초상 좌우(아군 왼쪽 · 적 오른쪽, 말하는 쪽 밝게, 지금은 큰 원 초상) ·
    /// 아래 대사 상자(이름표 편 색 · 글자 한 자씩 · n / 전체 · ▼) · 오른쪽 위 [자동] [기록] [건너뛰기] · 아무 데나 누르면 다음 · 장 표시 · 대화 기록 판.
    /// 시간 값은 data/Ui/dialogue.json (화면 연출만).
    /// </summary>
    public abstract class DialogueScene : GameScreen
    {
        private static bool _auto;

        protected abstract StepDef Step { get; }
        protected abstract List<DialogueLine> Lines();
        protected abstract void Finish();
        protected virtual bool ShowChapter => false;

        private List<DialogueLine> _lines;
        private int _index;
        private float _chars, _wait, _chapterLeft;
        private string _full = "";
        private bool _done;
        private Text _text, _name, _progress, _more;
        private Image _nameTag, _autoBg;
        private CanvasGroup _left, _right;
        private Portrait _leftPic, _rightPic;
        private RectTransform _chapter, _log;
        private float _cps = 40f, _autoBase = 1.2f, _autoPerChar = 0.05f, _chapterSeconds = 2.5f;

        public override void Build()
        {
            _lines = Lines();
            if (_lines.Count == 0) { Finish(); return; }
            ReadTiming();
            var step = Step;

            // 배경
            var bg = Ui.Rect(Root, "Backdrop");
            Ui.Fill(bg);
            Ui.Background(bg, BackdropColor(step));
            Hill(bg, new Vector2(0.25f, 0.62f), 900, new Color(0f, 0f, 0f, 0.10f));
            Hill(bg, new Vector2(0.8f, 0.58f), 1100, new Color(0f, 0f, 0f, 0.07f));
            Hill(bg, new Vector2(0.78f, 0.86f), 160, new Color(0.75f, 0.22f, 0.15f, 0.55f)); // 해

            // 초상 좌우
            _left = PortraitSlot("Left", 0.27f, out _leftPic);
            _right = PortraitSlot("Right", 0.74f, out _rightPic);

            // 화면 아무 데나 누르면 다음
            var tap = Ui.Rect(Root, "Tap");
            Ui.Fill(tap);
            Ui.Background(tap, new Color(0, 0, 0, 0));
            tap.gameObject.AddComponent<Button>().onClick.AddListener(Advance);

            // 대사 상자
            var box = Ui.Rect(Root, "Box");
            box.anchorMin = new Vector2(0.03f, 0.08f);
            box.anchorMax = new Vector2(0.97f, 0.29f);
            box.offsetMin = box.offsetMax = Vector2.zero;
            Ui.Round(Ui.Background(box, new Color(0.98f, 0.95f, 0.88f, 0.96f)));
            box.gameObject.AddComponent<Button>().onClick.AddListener(Advance);
            var tag = Ui.Rect(box, "NameTag");
            tag.anchorMin = tag.anchorMax = new Vector2(0f, 1f);
            tag.pivot = new Vector2(0f, 0.5f);
            tag.anchoredPosition = new Vector2(30, 0);
            tag.sizeDelta = new Vector2(260, 64);
            _nameTag = Ui.Round(Ui.Background(tag, Palette.Ally));
            _name = Ui.Label(tag, "", 30, TextAnchor.MiddleCenter, Color.white);
            Ui.Fill(_name.rectTransform);
            _text = Ui.Label(box, "", 36, TextAnchor.UpperLeft, Palette.Text);
            Ui.Fill(_text.rectTransform);
            _text.rectTransform.offsetMin = new Vector2(44, 60);
            _text.rectTransform.offsetMax = new Vector2(-44, -54);
            _progress = Ui.Label(box, "", 22, TextAnchor.LowerLeft, Palette.TextDim);
            Ui.Fill(_progress.rectTransform);
            _progress.rectTransform.offsetMin = new Vector2(44, 16);
            _more = Ui.Label(box, "▼", 30, TextAnchor.LowerRight, Palette.Seal);
            Ui.Fill(_more.rectTransform);
            _more.rectTransform.offsetMax = new Vector2(-30, -16);
            _more.rectTransform.offsetMin = new Vector2(0, 16);

            // 오른쪽 위 원 버튼 3개
            _autoBg = RoundButton(0, T("ui.dialogue.auto"), "自", () => { _auto = !_auto; _autoBg.color = _auto ? Palette.Seal : Palette.Card; _wait = 0; });
            _autoBg.color = _auto ? Palette.Seal : Palette.Card;
            RoundButton(1, T("ui.dialogue.log"), "記", OpenLog);
            RoundButton(2, T("ui.dialogue.skip"), "越", Finish);

            Show(0);
            if (ShowChapter && step != null && step.Chapter > 0) OpenChapter(step);
        }

        private void ReadTiming()
        {
            try
            {
                if (!Ctx.Data.Exists("Ui/dialogue.json")) return;
                var o = Game.Data.Json.JsonUtil.ParseObject(Ctx.Data.ReadText("Ui/dialogue.json"));
                _cps = o.Value<float?>("typeCharsPerSecond") ?? _cps;
                _autoBase = o.Value<float?>("autoBaseSeconds") ?? _autoBase;
                _autoPerChar = o.Value<float?>("autoPerCharSeconds") ?? _autoPerChar;
                _chapterSeconds = o.Value<float?>("chapterSeconds") ?? _chapterSeconds;
            }
            catch (System.Exception e) { Debug.LogWarning("Ui/dialogue.json: " + e.Message); }
        }

        private static Color BackdropColor(StepDef step)
        {
            string key = step?.Backdrop;
            Color c = key == "siege" || key == "castle" ? new Color(0.84f, 0.81f, 0.76f)
                : key == "naval" ? new Color(0.80f, 0.85f, 0.86f)
                : key == "field" ? new Color(0.86f, 0.86f, 0.78f)
                : new Color(0.93f, 0.89f, 0.81f); // 한지
            if (step?.TimeOfDay == "dusk") c = Color.Lerp(c, new Color(0.93f, 0.7f, 0.55f), 0.35f);
            if (step?.TimeOfDay == "night") c = Color.Lerp(c, new Color(0.25f, 0.27f, 0.35f), 0.6f);
            return c;
        }

        private void Hill(RectTransform parent, Vector2 at, float size, Color color)
        {
            var h = Ui.Rect(parent, "Ink");
            h.anchorMin = h.anchorMax = at;
            h.sizeDelta = new Vector2(size, size);
            Ui.Background(h, color).sprite = UiShapes.Circle;
        }

        private CanvasGroup PortraitSlot(string name, float x, out Portrait pic)
        {
            var slot = Ui.Rect(Root, name);
            slot.anchorMin = slot.anchorMax = new Vector2(x, 0.43f);
            slot.sizeDelta = new Vector2(440, 440);
            var group = slot.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            pic = new Portrait(slot, 440);
            return group;
        }

        private Image RoundButton(int i, string label, string icon, System.Action act)
        {
            var b = Ui.Rect(Root, "Round" + i);
            b.anchorMin = b.anchorMax = new Vector2(1f, 1f);
            b.pivot = new Vector2(0.5f, 1f);
            b.anchoredPosition = new Vector2(-440 + i * 140, -40);
            b.sizeDelta = new Vector2(96, 96);
            var img = Ui.Background(b, Palette.Card);
            img.sprite = UiShapes.Circle;
            var glyph = Ui.Label(b, icon, 36, TextAnchor.MiddleCenter, Palette.Text);
            glyph.font = Ui.TitleFont; // 한자 아이콘
            Ui.Fill(glyph.rectTransform);
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => act());
            var cap = Ui.Label(b, label, 20, TextAnchor.UpperCenter, Palette.Text);
            cap.rectTransform.anchorMin = new Vector2(0f, 0f);
            cap.rectTransform.anchorMax = new Vector2(1f, 0f);
            cap.rectTransform.pivot = new Vector2(0.5f, 1f);
            cap.rectTransform.sizeDelta = new Vector2(60, 30);
            cap.rectTransform.anchoredPosition = new Vector2(0, -4);
            return img;
        }

        private string NameOf(DialogueLine l) => l.Speaker != null ? Ctx.GeneralName(l.Speaker) : T("ui.dialogue.narration");

        private static Color SideColor(string side) => side == DialogueScript.Enemy ? Palette.Enemy : side == DialogueScript.Ally ? Palette.Ally : Palette.TextDim;

        private void Show(int index)
        {
            _index = index;
            var l = _lines[index];
            string side = DialogueScript.SideOf(l, Step);
            _full = l.Key != null && Ctx.Defs.Strings.ContainsKey(l.Key) ? T(l.Key) : "…";
            _chars = 0; _wait = 0; _done = false;
            _text.text = "";
            _name.text = NameOf(l);
            _nameTag.color = SideColor(side);
            _progress.text = (index + 1) + " / " + _lines.Count;
            _more.enabled = false;
            // 초상: 이 줄의 편 자리에 말하는 장수, 맞은편은 가장 최근 그 편 장수 (흐리게)
            string ally = null, enemy = null;
            for (int i = index; i >= 0; i--)
            {
                var x = _lines[i];
                string s = DialogueScript.SideOf(x, Step);
                if (s == DialogueScript.Ally && ally == null) ally = x.Speaker;
                if (s == DialogueScript.Enemy && enemy == null) enemy = x.Speaker;
            }
            if (ally != null) _leftPic.Show(ally, Ctx.GeneralName(ally), Palette.Ally);
            if (enemy != null) _rightPic.Show(enemy, Ctx.GeneralName(enemy), Palette.Enemy);
            _left.alpha = ally == null ? 0f : side == DialogueScript.Ally ? 1f : 0.4f;
            _right.alpha = enemy == null ? 0f : side == DialogueScript.Enemy ? 1f : 0.4f;
        }

        /// <summary>누르면: 글자가 다 안 나왔으면 그 줄 완성, 다 나왔으면 다음 줄 (마지막이면 끝).</summary>
        private void Advance()
        {
            if (_chapter != null) { CloseChapter(); return; }
            if (_log != null) return;
            if (!_done) { _chars = _full.Length; Tick(0f); return; }
            if (_index + 1 >= _lines.Count) { Finish(); return; }
            Show(_index + 1);
        }

        public override void Tick(float deltaTime)
        {
            if (_text == null) return;
            if (_chapter != null)
            {
                _chapterLeft -= deltaTime;
                if (_chapterLeft <= 0f) CloseChapter();
                return;
            }
            if (_log != null) return;
            if (!_done)
            {
                _chars += deltaTime * _cps;
                int n = Mathf.Min(_full.Length, Mathf.FloorToInt(_chars));
                _text.text = _full.Substring(0, n);
                if (n >= _full.Length) { _done = true; _more.enabled = true; _wait = 0f; }
                return;
            }
            if (!_auto) return;
            _wait += deltaTime;
            if (_wait >= _autoBase + _autoPerChar * _full.Length) Advance();
        }

        // ---- 장 표시 ----
        private void OpenChapter(StepDef step)
        {
            _chapterLeft = _chapterSeconds;
            _chapter = Ui.Rect(Root, "Chapter");
            Ui.Fill(_chapter);
            Ui.Background(_chapter, new Color(0f, 0f, 0f, 0.55f));
            _chapter.gameObject.AddComponent<Button>().onClick.AddListener(CloseChapter);
            var band = Ui.Rect(_chapter, "Band");
            band.anchorMin = new Vector2(0f, 0.43f);
            band.anchorMax = new Vector2(1f, 0.6f);
            band.offsetMin = band.offsetMax = Vector2.zero;
            Ui.Background(band, new Color(0.12f, 0.11f, 0.1f, 0.92f));
            Ui.Column(band, 6, 24).childAlignment = TextAnchor.MiddleCenter;
            string chapterName = Ctx.Defs.Strings.ContainsKey("chapter." + step.Chapter + ".name") ? T("chapter." + step.Chapter + ".name") : "";
            Ui.Label(band, T("ui.dialogue.chapter", step.Chapter, chapterName), 34, TextAnchor.MiddleCenter, new Color(0.85f, 0.8f, 0.7f));
            var title = Ui.Label(band, T(step.NameKey), 96, TextAnchor.MiddleCenter, Color.white);
            title.font = Ui.TitleFont;
            var line = Ui.Rect(band, "Underline");
            Ui.Size(line, 6, 320);
            Ui.Background(line, Palette.Seal);
            Ui.Label(band, T("ui.dialogue.tapToStart"), 24, TextAnchor.MiddleCenter, new Color(0.75f, 0.72f, 0.66f));
        }

        private void CloseChapter()
        {
            if (_chapter == null) return;
            Object.Destroy(_chapter.gameObject);
            _chapter = null;
        }

        // ---- 대화 기록 ----
        private void OpenLog()
        {
            if (_log != null) return;
            _log = Ui.Rect(Root, "Log");
            Ui.Fill(_log);
            Ui.Background(_log, new Color(0f, 0f, 0f, 0.5f));
            var panel = Ui.Rect(_log, "Panel");
            panel.anchorMin = new Vector2(0.03f, 0.05f);
            panel.anchorMax = new Vector2(0.97f, 0.8f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            Ui.Round(Ui.Background(panel, Palette.Background));
            Ui.Column(panel, 10, 24);
            Ui.Size(Ui.Label(panel, T("ui.dialogue.logTitle"), 40, TextAnchor.MiddleCenter), 70);
            var list = Ui.ScrollList(panel, "Lines", 8, 4);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            for (int i = 0; i <= _index && i < _lines.Count; i++)
            {
                var l = _lines[i];
                string side = DialogueScript.SideOf(l, Step);
                var row = Ui.Row(list, "Line" + i, 130, 14);
                var pic = Ui.Rect(row, "Pic");
                Ui.Size(pic, 96, 96);
                if (l.Speaker != null) new Portrait(pic, 96).Show(l.Speaker, NameOf(l), SideColor(side));
                var col = Ui.Rect(row, "Text");
                Ui.Size(col, flexibleWidth: 1);
                Ui.Column(col, 2, 0);
                Ui.Label(col, Ui.Colored(NameOf(l), SideColor(side)), 24);
                Ui.Label(col, l.Key != null && Ctx.Defs.Strings.ContainsKey(l.Key) ? T(l.Key) : "…", 26, TextAnchor.UpperLeft);
            }
            Ui.Label(list, Ui.Colored("── " + T("ui.dialogue.here") + " ──", Palette.Bad), 22, TextAnchor.MiddleCenter);
            Ui.Button(panel, T("ui.common.close"), () => { Object.Destroy(_log.gameObject); _log = null; }, 96, fontSize: 30);
        }
    }
}
