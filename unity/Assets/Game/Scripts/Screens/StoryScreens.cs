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
                var b = Ui.Button(list, label, () => app.Show(new StepScreen(s.id)), 140);
                Ui.SetEnabled(b, unlocked);
            }
        }
    }

    /// <summary>스텝 정보 + 출진 (직접 지휘 / 위임). 출진 준비(Prepare)와 확정(Confirm)은 규칙 객체가 한다.</summary>
    public sealed class StepScreen : GameScreen
    {
        private readonly string _stepId;
        public StepScreen(string stepId) { _stepId = stepId; }

        public override void Build()
        {
            Ui.Column(Root, 16, 32);
            var step = StepDef.Load(Ctx.Data, _stepId);
            Ui.Size(Ui.Label(Root, T(step.NameKey), Ui.FontTitle), 120);

            var body = Ui.ScrollList(Root, "Info", 12, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);

            var info = Ui.Card(body, "Conditions");
            Ui.Label(info, T("ui.step.battleType", step.BattleType));
            Ui.Label(info, T("ui.step.maxMinutes", step.MaxMinutes));
            Ui.Label(info, T("ui.step.conditions", step.Weather, step.TimeOfDay));
            int reserve = 0;
            foreach (var e in step.Enemy) if (e.Reserve) reserve++;
            Ui.Label(info, T("ui.step.enemies", step.Enemy.Count, reserve));

            var enemies = Ui.Card(body, "Enemies");
            foreach (var e in step.Enemy)
            {
                long troops = 0;
                foreach (var u in e.Units) troops += u.Troops;
                Ui.Label(enemies, Ui.Colored(Ctx.GeneralName(e.General), Palette.Enemy) + "  " + troops.ToString("N0") + (e.Reserve ? "  (" + T("ui.corps.reserve") + ")" : ""), Ui.FontSmall);
            }

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
            var sortie = Ui.Card(body, "Sortie");
            if (hireError != null) Ui.Label(sortie, Ui.Colored(hireError, Palette.Bad), Ui.FontSmall);
            if (plan.Hired != null)
                foreach (var kv in plan.Hired.Costs) Ui.Label(sortie, T("ui.hire.cost", kv.Value.ToString("N0") + " " + T("resource." + kv.Key)), Ui.FontSmall);
            Ui.Label(sortie, T("ui.step.supply", plan.SupplyTaken.ToString("N0"), Ctx.Resource(ResourceIds.Supply).ToString("N0")), Ui.FontSmall);
            foreach (var err in plan.Errors) Ui.Label(sortie, Ui.Colored(err, Palette.Bad), Ui.FontSmall);

            if (!plan.Ok) Ui.Label(Root, Ui.Colored(T("ui.step.cannotSortie"), Palette.Bad), Ui.FontSmall, TextAnchor.MiddleCenter);
            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(row, T("ui.common.back"), () => App.ShowMain("story"));
            var direct = Ui.Button(row, T("ui.step.directSortie"), () => Sortie(step, false));
            var delegated = Ui.Button(row, T("ui.step.delegateSortie"), () => Sortie(step, true));
            Ui.SetEnabled(direct, plan.Ok && hireError == null);
            Ui.SetEnabled(delegated, plan.Ok && hireError == null);
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
            var plan = Ctx.Flow.Prepare(Ctx.State, Request(step, delegate_, out var hireError));
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
