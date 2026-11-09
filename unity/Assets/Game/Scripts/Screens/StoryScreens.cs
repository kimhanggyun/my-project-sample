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

    /// <summary>(임시) 전투 전 대사. 연출 시스템 전까지 문장 하나를 보여주고 전투로 넘어간다.</summary>
    public sealed class DialogueScreen : GameScreen
    {
        private readonly SortiePlan _plan;
        private readonly string _lineKey;
        private readonly bool _playContests;

        /// <param name="playContests">전투 직전 대결(일기토·논쟁)을 직접 고를지 (직접 지휘). 아니면 AI가 고른다.</param>
        public DialogueScreen(SortiePlan plan, string lineKey, bool playContests = false) { _plan = plan; _lineKey = lineKey; _playContests = playContests; }

        public override void Build()
        {
            Ui.Column(Root, 24, 48).childAlignment = TextAnchor.LowerCenter;
            Ui.Size(Ui.Label(Root, T(_plan.Step.NameKey), Ui.FontTitle, TextAnchor.MiddleCenter), 200);
            Ui.Size(Ui.Label(Root, "", Ui.FontBody), flexibleHeight: 1);
            var box = Ui.Card(Root, "Box", 40, 16);
            string speaker = _plan.Setup.Corps.Count > 0 && _plan.Setup.Corps[0].General != null ? Ctx.GeneralName(_plan.Setup.Corps[0].General.Id) : "";
            Ui.Label(box, Ui.Colored(speaker, Palette.Ally), Ui.FontBody);
            bool has = Ctx.Defs.Strings.ContainsKey(_lineKey);
            Ui.Size(Ui.Label(box, has ? T(_lineKey) : "…", Ui.FontBody, TextAnchor.UpperLeft), 200);
            bool contest = _playContests && _plan.Setup.Contests.Count > 0;
            Ui.Button(Root, contest ? T("ui.contest.challenge") : T("ui.dialogue.next"),
                () => App.Show(contest ? (GameScreen)new ContestScreen(_plan, 0) : new BattleScreen(_plan)), 140);
        }
    }
}
