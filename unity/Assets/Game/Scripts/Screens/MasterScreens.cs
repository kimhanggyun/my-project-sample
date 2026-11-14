using Game.Economy;
using Game.Flow;
using Game.Online;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 마스터 탭 (R-86, 개발 줄): 자원 지급·모든 스텝 해금·모든 장수 받기.
    /// 행동은 보통 행동처럼 기기에 적용하고 서버로 보낸다. 서버는 마스터 계정(Game:Masters)의 것만 받는다 (아니면 서버 세이브로 되돌아감).
    /// </summary>
    public sealed class MasterTabView : ITabView
    {
        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var list = Ui.ScrollList(root, "List");
            Ui.Fill((RectTransform)list.parent.parent);
            Ui.Size(Ui.Label(list, ctx.T("ui.master.title"), Ui.FontTitle, TextAnchor.MiddleLeft), 90);
            Ui.Label(list, ctx.T("ui.master.account", ctx.AccountId ?? "-"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            var who = Ui.Label(list, ctx.OnlineEnabled ? ctx.T("ui.master.checking") : ctx.T("ui.master.offline"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            if (ctx.OnlineEnabled)
                ctx.LoadProfile(p =>
                {
                    if (who == null) return; // 탭이 이미 닫혔으면
                    bool master = p != null && p.IsMaster;
                    who.text = ctx.T(master ? "ui.master.isMaster" : "ui.master.notMaster");
                    who.color = master ? Palette.Good : Palette.Bad;
                });
            var result = Ui.Label(list, "", Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            void Run(PlayerAction action)
            {
                bool ok = ctx.Do(action);
                if (ok) app.ShowMain("master"); // 위 자원 막대도 새로
                else result.text = ctx.T("ui.master.nothing");
            }

            foreach (var (id, amount) in new[] { (ResourceIds.Supply, 100_000L), (ResourceIds.Gold, 100_000L), (ResourceIds.Gem, 10_000L) })
            {
                string rid = id; long n = amount;
                Ui.Button(list, ctx.T("ui.master.grant", ctx.T("resource." + rid), n), () => Run(PlayerActionRunner.DevGrant(rid, n)), 100, Palette.ButtonActive);
            }
            // 계급 (army-v2.md 2장): 경험치 전까지 여기서 올리고 내려 편성을 시험한다
            var rank = ctx.Defs.Campaign.RankFor(ctx.State.LordLevel);
            Ui.Label(list, ctx.T("ui.master.lordLevel", ctx.T("rank." + rank.Id), ctx.State.LordLevel, rank.Corps, rank.Slots, rank.TroopMax), Ui.FontSmall);
            var lv = Ui.Row(list, "LordLevel", 100, 8);
            Ui.Button(lv, ctx.T("ui.master.lordDown"), () => Run(PlayerActionRunner.DevLordLevel(-1)), 100);
            Ui.Button(lv, ctx.T("ui.master.lordUp"), () => Run(PlayerActionRunner.DevLordLevel(1)), 100, Palette.ButtonActive);
            Ui.Button(lv, ctx.T("ui.master.lordNext"), () =>
            {
                var next = ctx.Defs.Campaign.NextRank(ctx.State.LordLevel);
                Run(PlayerActionRunner.DevLordLevel(next == null ? 0 : next.MinLevel - ctx.State.LordLevel));
            }, 100);
            Ui.Button(list, ctx.T("ui.master.unlockAll"), () => Run(PlayerActionRunner.DevUnlockAll()), 100);
            Ui.Button(list, ctx.T("ui.master.allGenerals"), () => Run(PlayerActionRunner.DevGiveGeneral("*")), 100);
        }
    }
}
