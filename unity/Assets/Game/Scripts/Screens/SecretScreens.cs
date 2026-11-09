using System.Collections.Generic;
using Game.Data.Defs;
using Game.Flow;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>숨은 보상 문구 (secrets.md 3장): 이름 "secret.{id}", 특성 이름 "trait.{id}".</summary>
    public static class SecretText
    {
        public static string Name(GameContext ctx, string secretId) => ctx.T("secret." + secretId);

        public static string Reward(GameContext ctx, SecretDef s)
        {
            var parts = new List<string>();
            if (s.RewardSkill != null) parts.Add(ctx.T("ui.result.secretSkill", ctx.Defs.Skills.TryGetValue(s.RewardSkill, out var sd) ? ctx.T(sd.NameKey) : s.RewardSkill));
            if (s.RewardTrait != null) parts.Add(ctx.T("ui.result.secretTrait", ctx.T("trait." + s.RewardTrait)));
            return string.Join(", ", parts);
        }

        /// <summary>결과 화면 한 줄: 「이름」 장수 → 보상 (보관함이면 표시).</summary>
        public static string Found(GameContext ctx, FoundSecret f)
        {
            SecretDef def = null;
            foreach (var s in ctx.Defs.Secrets) if (s.Id == f.SecretId) { def = s; break; }
            string reward = def != null ? Reward(ctx, def) : f.SecretId;
            if (f.StoredAsBook) reward = ctx.T("ui.result.secretStored", reward);
            return ctx.T("ui.result.secretLine", Name(ctx, f.SecretId), ctx.GeneralName(f.GeneralId), reward);
        }

        public static int FoundCount(GameContext ctx, out int total)
        {
            var codex = SecretEngine.Codex(ctx.Defs, ctx.State);
            total = codex.Count;
            int n = 0;
            foreach (var e in codex) if (e.Found) n++;
            return n;
        }
    }

    /// <summary>숨은 보상 도감 (secrets.md 3장): 찾은 것은 이름·보상, 못 찾은 것은 "???"와 힌트.</summary>
    public sealed class CodexScreen : GameScreen
    {
        public override void Build()
        {
            Ui.Column(Root, 12, 32);
            Ui.Size(Ui.Label(Root, T("ui.codex.title"), Ui.FontTitle), 110);
            int found = SecretText.FoundCount(Ctx, out int total);
            Ui.Label(Root, T("ui.codex.count", found, total), Ui.FontSmall);
            Ui.Label(Root, T("ui.codex.note"), 24, TextAnchor.MiddleLeft, Palette.TextDim);

            var list = Ui.ScrollList(Root, "Codex", 8, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            var defs = Ctx.Defs.Secrets;
            var codex = SecretEngine.Codex(Ctx.Defs, Ctx.State);
            for (int i = 0; i < codex.Count; i++)
            {
                var e = codex[i];
                var card = Ui.Card(list, "Secret" + i);
                if (e.Found)
                {
                    Ui.Label(card, Ui.Colored(SecretText.Name(Ctx, e.SecretId), Palette.Good), Ui.FontSmall);
                    Ui.Label(card, T("ui.codex.reward", SecretText.Reward(Ctx, defs[i])), 24, TextAnchor.MiddleLeft, Palette.TextDim);
                }
                else
                {
                    Ui.Label(card, T("ui.codex.unknown"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
                    if (e.HintKey != null) Ui.Label(card, T(e.HintKey), 24, TextAnchor.MiddleLeft, Palette.TextDim);
                }
            }
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("story"));
        }
    }
}
