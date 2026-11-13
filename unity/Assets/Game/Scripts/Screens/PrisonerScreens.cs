using Game.Campaign.Captivity;
using Game.Economy;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 포로 탭 (captivity.md 1.2, R-66): 잡은 적 장수마다 능력치·성향·등용 확률, [등용 시도] / [해방(보상)].
    /// 등용은 확률(시드 난수, 서버도 같은 결과), 실패하면 재시도 대기 시간 동안 다시 못 한다. 해방하면 보상을 받고 풀어 준다.
    /// 실제 변경은 행동(prisoner.recruit / prisoner.release)으로 한다. 채택 안 하면 이 파일과 등록 2줄만 지운다.
    /// </summary>
    public sealed class PrisonerTabView : ITabView
    {
        private const string TabId = "prisoner";
        /// <summary>바로 전 처리 결과 (탭을 다시 그려도 한 번 보여준다).</summary>
        private static string _lastResult;

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var svc = new CaptivityService(ctx.Defs, ctx.Defs.ResolveFlags(), ctx.Time);
            var list = Ui.ScrollList(root, "List", 12, 16);
            Ui.Fill((RectTransform)list.parent.parent);
            InfoButton.Add(root, ctx.T("ui.prisoner.note"));
            if (_lastResult != null) { Ui.Label(list, _lastResult, Ui.FontBody, TextAnchor.MiddleCenter); _lastResult = null; }
            if (ctx.State.Prisoners.Count == 0) { Ui.Label(list, ctx.T("ui.prisoner.none"), Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim); return; }

            long now = ctx.Time.UtcNow.ToUnixTimeSeconds();
            string reward = "";
            foreach (var kv in ctx.Defs.Campaign.ReleaseReward) reward += ctx.T("resource." + kv.Key) + " " + kv.Value.ToString("N0") + " ";

            foreach (var p in new System.Collections.Generic.List<Game.Campaign.Roster.PrisonerState>(ctx.State.Prisoners))
            {
                string id = p.GeneralId;
                if (!ctx.Defs.Generals.TryGetValue(id, out var def)) continue;
                var card = Ui.Card(list, id, 16, 6);
                Ui.Label(card, Ui.Colored(ctx.GeneralName(id), Palette.Enemy) + "   " + ctx.T("ui.prisoner.temperament", ctx.T("temperament." + def.Temperament)), 32);
                Icons.Stats(ctx, card, def.Stats.Get("leadership").FloorToLong(), def.Stats.Get("might").FloorToLong(), def.Stats.Get("intellect").FloorToLong(), 34, 24);

                bool recruitable = def.Recruitable;
                bool can = svc.CanAttemptRecruit(ctx.State, id);
                string status;
                if (!recruitable) status = Ui.Colored(ctx.T("ui.prisoner.notRecruitable"), Palette.Bad);
                else if (!can && p.LastRecruitAttemptUnixSeconds > 0)
                {
                    long left = System.Math.Max(0, p.LastRecruitAttemptUnixSeconds + ctx.Defs.Campaign.RecruitRetrySeconds - now);
                    status = ctx.T("ui.prisoner.wait", left / 3600, left % 3600 / 60);
                }
                else status = ctx.T("ui.prisoner.chance", (svc.RecruitChance(p) * Game.Foundation.Math.Fix.Hundred).FloorToLong()) + (p.BrainwashBonus ? "  " + Ui.Colored(ctx.T("ui.prisoner.brainwash"), Palette.Good) : "");
                Ui.Label(card, status, 26);

                var row = Ui.Row(card, "Buttons", 90, 8);
                var recruit = Ui.Button(row, ctx.T("ui.prisoner.recruit"), () => Recruit(app, id), 90, Palette.ButtonActive, 28);
                Ui.SetEnabled(recruit, recruitable && can);
                Ui.Button(row, ctx.T("ui.prisoner.release", reward.Trim()), () => Release(app, id), 90, null, 26);
            }
        }

        private static void Recruit(GameApp app, string id)
        {
            var ctx = app.Context;
            string name = ctx.GeneralName(id);
            if (!ctx.Do(PlayerActionRunner.RecruitPrisoner(id, ctx.NewSeed()))) _lastResult = Ui.Colored(ctx.T("ui.cmd.rejected"), Palette.Bad);
            else if (ctx.State.FindGeneral(id) != null) _lastResult = Ui.Colored(ctx.T("ui.prisoner.recruited", name), Palette.Good);
            else if (ctx.State.FindPrisoner(id) != null) _lastResult = Ui.Colored(ctx.T("ui.prisoner.failed", name), Palette.Bad);
            else _lastResult = ctx.T("ui.prisoner.failedReleased", name);
            app.ShowMain(TabId);
        }

        private static void Release(GameApp app, string id)
        {
            var ctx = app.Context;
            _lastResult = ctx.Do(PlayerActionRunner.ReleasePrisoner(id)) ? ctx.T("ui.prisoner.released", ctx.GeneralName(id)) : Ui.Colored(ctx.T("ui.cmd.rejected"), Palette.Bad);
            app.ShowMain(TabId);
        }
    }
}
