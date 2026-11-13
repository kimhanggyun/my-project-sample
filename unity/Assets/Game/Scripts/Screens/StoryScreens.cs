using System.Collections.Generic;
using Game.Data.Defs;
using Game.Economy;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>스토리 탭: 스텝 목록 (차례 해금) + 포로가 있는 수용소의 구출 전투.</summary>
    public sealed class StoryTabView : ITabView
    {
        /// <summary>목록 한 줄: 갇힌 장수 수와 가장 짧은 남은 기한 (E23).</summary>
        private static string RescueLine(Game.Unity.Boot.GameContext ctx, string camp)
        {
            var held = RescueInfo.Held(ctx, camp);
            if (held.Count == 0) return ctx.T("ui.rescue.none");
            long soonest = long.MaxValue;
            foreach (var g in held) soonest = System.Math.Min(soonest, g.RescueDeadlineUnixSeconds);
            return ctx.T("ui.rescue.listLine", held.Count, RescueInfo.Left(ctx, soonest));
        }

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "Steps");
            Ui.Fill((RectTransform)list.parent.parent);
            Ui.Size(Ui.Label(list, ctx.T("ui.story.header"), Ui.FontTitle, TextAnchor.MiddleLeft), 120);
            int found = SecretText.FoundCount(ctx, out int total);
            var extras = Ui.Row(list, "Extras", 100, 8);
            if (total > 0) Ui.Button(extras, ctx.T("ui.codex.button", found, total), () => app.Show(new CodexScreen()), 100, Palette.Card, 28);
            Ui.Button(extras, ctx.T("ui.replay.button", ctx.Replays.Items.Count), () => app.Show(new ReplayListScreen()), 100, Palette.Card, 28);

            var steps = new List<(string id, bool rescue)>();
            foreach (var id in ctx.Defs.StepIndex.Order) steps.Add((id, false));
            foreach (var kv in ctx.Defs.StepIndex.RescueSteps)
                if (ctx.Flow.IsUnlocked(ctx.State, kv.Value)) steps.Add((kv.Value, true));
            // 훈련 전투 (R-85): 그 병종이 있으면 맨 위 줄에 버튼 (반복 가능)
            foreach (var kv in ctx.Defs.StepIndex.Drills)
            {
                if (!ctx.Flow.IsUnlocked(ctx.State, kv.Key)) continue;
                string drillId = kv.Key;
                string name;
                try { name = ctx.T(StepDef.Load(ctx.Data, drillId).NameKey); } catch (System.Exception) { name = drillId; }
                Ui.Button(extras, name, () => app.Show(new StepScreen(drillId)), 100, Palette.Card, 28);
            }

            foreach (var s in steps)
            {
                StepDef step;
                try { step = StepDef.Load(ctx.Data, s.id); }
                catch (System.Exception e) { Debug.LogException(e); Ui.Label(list, s.id + " ?", Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Bad); continue; }
                bool unlocked = ctx.Flow.IsUnlocked(ctx.State, s.id);
                bool cleared = ctx.State.ClearedSteps.Contains(s.id);
                string state = cleared ? Ui.Colored(ctx.T("ui.story.cleared"), Palette.Good)
                             : unlocked ? ctx.T("ui.story.open")
                             : Ui.Colored(ctx.T("ui.story.locked"), Palette.TextDim);
                string label = (s.rescue ? "[" + ctx.T("ui.story.rescue") + "] " : "") + ctx.T(step.NameKey) + "   " + state;
                if (s.rescue && step.RescueCamp != null) label += "\n" + Ui.Colored(RescueLine(ctx, step.RescueCamp), Palette.Bad); // 갇힌 장수·남은 기한 (E23)
                var b = Ui.Button(list, label, () => app.Show(new StepScreen(s.id)), 112);
                Ui.SetEnabled(b, unlocked);
            }
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
    public sealed class StepScreen : GameScreen
    {
        /// <summary>스텝마다 고른 군량 분 (E22, 앱을 켜 둔 동안 기억). 없으면 자동(3분).</summary>
        private static readonly Dictionary<string, int> SupplyMinutes = new Dictionary<string, int>();
        private readonly string _stepId;
        public StepScreen(string stepId) { _stepId = stepId; }

        public override void Build()
        {
            Ui.Column(Root, 16, 32);
            var step = StepDef.Load(Ctx.Data, _stepId);
            Ui.Size(Ui.Label(Root, T(step.NameKey), Ui.FontTitle), 120);

            var body = Ui.ScrollList(Root, "Info", 12, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);

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

            var info = Ui.Card(body, "Conditions");
            Ui.Label(info, T("ui.step.battleType", T("battleType." + step.BattleType)));
            Ui.Label(info, T("ui.step.maxMinutes", step.MaxMinutes));
            Ui.Label(info, T("ui.step.conditions", T("weather." + step.Weather), T("timeOfDay." + step.TimeOfDay)));
            int reserve = 0;
            foreach (var e in step.Enemy) if (e.Reserve) reserve++;
            Ui.Label(info, T("ui.step.enemies", step.Enemy.Count, reserve));

            // 적 정보는 대략 (E22): 장수·병력 어림·진형
            var enemies = Ui.Card(body, "Enemies");
            foreach (var e in step.Enemy)
            {
                long troops = 0;
                foreach (var u in e.Units) troops += u.Troops;
                Ui.Label(enemies, Ui.Colored(Ctx.GeneralName(e.General), Palette.Enemy) + "  " + T("ui.step.roughTroops", SortieAdvice.Rough(troops).ToString("N0"))
                    + "  " + FormationName(e.Formation) + (e.Reserve ? "  (" + T("ui.corps.reserve") + ")" : ""), Ui.FontSmall);
            }

            // 공략 (R-81): 다른 사람이 공유한 이 스텝 녹화
            if (Ctx.OnlineEnabled)
                Ui.Button(body, T("ui.shared.button"), () => App.Show(new SharedReplayScreen(_stepId)), 84, Palette.Card, 28);

            var rewards = Ui.Card(body, "Rewards");
            Ui.Label(rewards, T("ui.step.rewards"));
            var parts = new List<string>();
            foreach (var kv in step.RewardResources) parts.Add(T("resource." + kv.Key) + " " + kv.Value.ToString("N0"));
            foreach (var f in step.RewardFormations) parts.Add(FormationName(f));
            foreach (var s in step.RewardSkills) parts.Add(SkillName(s));
            Ui.Label(rewards, parts.Count == 0 ? T("ui.common.none") : string.Join(", ", parts), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            // 고용 (온라인일 때, R-43): 고른 것을 편성 사본에 넣어 출진 준비에 넘긴다
            var request = Request(step, false, out var hireError);
            if (Ctx.HiringAvailable) HireCard(body, step);

            // 출진 준비 (상태는 바꾸지 않는다)
            var plan = Ctx.Flow.Prepare(Ctx.State, request);
            // 군량 분 (E22): 고른 분이 있으면 그만큼 다시 준비
            int minutes = SupplyMinutes.TryGetValue(_stepId, out var mm) ? mm : SortieAdvice.AutoMinutes;
            if (minutes != SortieAdvice.AutoMinutes && plan.AutoSupply > 0)
            {
                request.Supply = SortieAdvice.SupplyFor(plan.AutoSupply, minutes);
                plan = Ctx.Flow.Prepare(Ctx.State, request);
            }
            // 아군 편성 요약 (출진할 군단: 장수 · 진형 · 병력)
            if (plan.Compositions != null && plan.Compositions.Count > 0)
            {
                var allies = Ui.Card(body, "Allies");
                Ui.Label(allies, T("ui.step.allies") + "  " + Ui.Colored(T("ui.step.situation", T("battleType." + step.BattleType)), Palette.TextDim)); // 고른 상황 편성 (E22)
                long mine = 0;
                foreach (var comp in plan.Compositions)
                {
                    long troops = 0;
                    foreach (var slot in comp.Slots) { var u = Ctx.State.FindUnit(slot.Value); if (u != null) troops += u.Troops; }
                    Ui.Label(allies, T("ui.step.allyLine", Ui.Colored(Ctx.GeneralName(comp.GeneralId), Palette.Ally), FormationName(comp.Formation), troops.ToString("N0")), Ui.FontSmall);
                    mine += troops;
                }
                // 전력 판단 (E22): 내 출진 병력 ÷ 적 병력(예비 포함)
                string judge = SortieAdvice.Judge(mine, SortieAdvice.EnemyTroops(step, true));
                var color = judge == SortieAdvice.Favorable ? Palette.Good : judge == SortieAdvice.Even ? Palette.Text : Palette.Bad;
                Ui.Label(allies, Ui.Colored(T("ui.step.judge." + judge), color), Ui.FontSmall);
                if (step.RecommendedLevel > 0) Ui.Label(allies, T("ui.step.recommendedLevel", step.RecommendedLevel), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            }
            var sortie = Ui.Card(body, "Sortie");
            if (hireError != null) Ui.Label(sortie, Ui.Colored(hireError, Palette.Bad), Ui.FontSmall);
            if (plan.Hired != null)
                foreach (var kv in plan.Hired.Costs) Ui.Label(sortie, T("ui.hire.cost", kv.Value.ToString("N0") + " " + T("resource." + kv.Key)), Ui.FontSmall);
            Ui.Label(sortie, T("ui.step.supply", plan.SupplyTaken.ToString("N0"), Ctx.Resource(ResourceIds.Supply).ToString("N0")), Ui.FontSmall);
            // 군량 ± (E22): 몇 분 분량 가져갈지. 모자라면 안내 + 가능한 만큼·상점
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

            if (!plan.Ok) Ui.Label(Root, Ui.Colored(T("ui.step.cannotSortie"), Palette.Bad), Ui.FontSmall, TextAnchor.MiddleCenter);
            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(row, T("ui.common.back"), () => App.ShowMain("story"));
            var direct = Ui.Button(row, T("ui.step.directSortie"), () => Sortie(step, false));
            var delegated = Ui.Button(row, T("ui.step.delegateSortie"), () => Sortie(step, true));
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
