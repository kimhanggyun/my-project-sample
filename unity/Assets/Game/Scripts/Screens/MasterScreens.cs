using System.Collections.Generic;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

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
    /// <summary>
    /// 부품 미리보기 탭 (개발 줄, 공통부품.md 0장 4): 공통 부품을 상태별로 늘어놓는다. 스타일 데이터(styles.json parts·pages)를 바꾸면 여기서 한 번에 확인.
    /// 화면 문구 없이 부품 이름만 (개발용).
    /// </summary>
    public sealed class PartsTabView : ITabView
    {
        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            UiStyle.Page = null;
            var list = Ui.ScrollList(root, "Parts", 14, 25);
            Ui.Fill((RectTransform)list.parent.parent);
            void H(string t) => Ui.SectionHeader(list, t);

            H("1. 버튼 — button.primary / secondary / small / twoLine / icon");
            var r1 = Ui.Row(list, "Buttons1", 100, 10);
            Ui.PrimaryButton(r1, "주요", null, 100, 30);
            var off = Ui.PrimaryButton(r1, "주요 꺼짐", null, 100, 30);
            Ui.SetPrimaryEnabled(off, false);
            Ui.SecondaryButton(r1, "보조", null, 100, 30);
            var r2 = Ui.Row(list, "Buttons2", 100, 10);
            r2.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Ui.Size(Ui.Styled(Ui.SecondaryButton(r2, "−", null, 56, 30), "button.small"), 56, 56);
            Ui.Size(Ui.Styled(Ui.SecondaryButton(r2, "최대", null, 56, 22), "button.small"), 56, 90);
            Ui.TwoLineButton(r2, "훈련 +10%", "돈 60", null, true);
            Ui.TwoLineButton(r2, "훈련 +10%", "돈 60", null, false, true);
            Ui.IconButton(r2, "shop", "상점", null);
            Ui.IconButton(r2, "settings", "설정", null);

            H("tabs.segment · text.link · check.row");
            Ui.SegmentTabs(list, new List<(string, string)> { ("a", "기본"), ("b", "야전"), ("c", "성새전") }, "a", null, 58, 140);
            var lr = Ui.Row(list, "Link", 50, 10);
            Ui.Link(lr, "보기", null);
            Ui.CheckRow(list, "체크 꺼짐", false, null);
            Ui.CheckRow(list, "체크 켜짐", true, null, link: "보기");

            H("2. 글자 — text.header / section / hint / error");
            Ui.HeaderRow(list, "머리 줄", "부제");
            Ui.Hint(list, "안내 문구", 50);
            Ui.ErrorLine(list, "실패 문구", 50);

            H("3. 카드 — card.mini (보통·흐림·경고·꼬리표·상태 띠) · card.feature · card.collapsible");
            var minis = Ui.HScrollRow(list, "Minis", 200, 10, 6);
            Ui.MiniCard(ctx, minis, "infantry", null, "보병", "2,000 / 3,000", Ui.MiniState.Normal, null);
            Ui.MiniCard(ctx, minis, "cavalry", null, "기병", "0 / 1,000", Ui.MiniState.Dim, null);
            Ui.MiniCard(ctx, minis, "archer", null, "궁병", "-500 / 2,000", Ui.MiniState.Warn, null);
            Ui.MiniCard(ctx, minis, null, "general_a", "장수 A", "Lv.2", Ui.MiniState.Dim, null, "제1군", null, default, 150, 178);
            Ui.MiniCard(ctx, minis, null, "general_b", "장수 B", "Lv.2", Ui.MiniState.Normal, null, null, "경상", new Color(0.851f, 0.651f, 0.18f), 150, 178);
            var fc = Ui.FeatureCard(list, "꼬리표", Palette.ButtonActive);
            Ui.Label(fc, "강조 카드 내용", 30);
            var (ch, cb) = Ui.CollapsibleCard(list, "Collapsible", true, null);
            Ui.Label(ch, "접는 카드 머리 (펼침)", 28);
            Ui.Label(cb, "몸통", 24);
            var (ch2, _) = Ui.CollapsibleCard(list, "Collapsed", false, null, warn: true);
            Ui.Label(ch2, "접힌 카드 + 경고 테두리", 28);

            H("4. 줄·막대 — row.list / gauge / stepper · band.rank");
            Ui.ListRow(list, "✓", Palette.Good, "목록 줄", "오른쪽 ›", null);
            Ui.ListRow(list, "@lock", Palette.TextDim, "잠긴 줄", "조건", null, dim: true, height: 84);
            Ui.GaugeRow(list, "병력", 0.6f, new Color(0.247f, 0.435f, 0.69f), "3,000", "+부상 200 / 5,000", 0.04f);
            Ui.Stepper(list, "인원", 800, 2096, "모두", null, 700, 900);
            Ui.RankBar(ctx, list, root, 1, 1);

            H("5. 겹침 — info.small · pop.tip · pop.menu · pop.sheet · pop.confirm · diagram.formation · board.drop");
            var r5 = Ui.Row(list, "Pops", 90, 10);
            r5.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Ui.SmallInfo(r5, a => Ui.StatTip(ctx, root, a, ("leadership", "통솔", "60"), ("might", "무력", "45")));
            Ui.Size(Ui.SecondaryButton(r5, "메뉴", null, 80, 26), 80, 160);
            var menuBtn = r5.GetChild(r5.childCount - 1).GetComponent<Button>();
            menuBtn.onClick.AddListener(() => Ui.PickMenu(root, (RectTransform)menuBtn.transform, 300, ("하나", "1", true, null), ("둘(흐림)", "2", false, null)));
            Ui.Size(Ui.SecondaryButton(r5, "아래 판", () => { var (c, close) = Ui.BottomSheet(root, "아래에서 올라오는 판", 420); Ui.Column(c, 10, 0); Ui.SecondaryButton(c, "닫기", close, 80, 26); }, 80, 26), 80, 180);
            Ui.Size(Ui.SecondaryButton(r5, "확인 창", () => Ui.Confirm(root, "확인 창", "설명 한 줄", "주요", null, ("보조", null), ("취소", null)), 80, 26), 80, 180);
            var slots = Game.Campaign.Roster.Army.Layout(ctx.Defs, new List<(string, string, long)> { ("infantry", "infantry", 3000), ("archer", "archer", 1500) });
            Ui.FormationPreview(ctx, list, ctx.State.LearnedFormations.Count > 0 ? ctx.State.LearnedFormations[0] : "square", slots, 400, 300, null);
            Ui.DropGrid(list, 3, 3, 200, 120, "▲ 앞");
            Ui.Hint(list, "styles.json parts / pages 를 바꾸고 이 탭을 다시 열면 바뀐 모양을 본다.", 60);
        }
    }
}
