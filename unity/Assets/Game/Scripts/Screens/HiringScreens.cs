using System.Linq;
using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 용병 탭 (09 시안, hiring-v2.md, R-98 · R-99): 위 탭 용병 고용 · 내 용병 등록.
    /// 용병 고용 = 등록자(유저)마다 카드 하나 (닉네임 장수 + 그 부대), 빌리는 단위 장수+부대 · 장수만 · 부대만 → [고용 ›]은 다음 출진에 넣을 고용으로 담는다 (출진 준비에서 군단 · 인원을 고칠 수 있다).
    /// 내 용병 등록 = 닉네임 장수 카드 · 내 등록 해제 · 등록(장수 · 내가 이끄는 부대). 영토 깃발에 나가 있으면 등록 불가.
    /// 배신 · 반간은 전투 규칙(R-99)과 출진 준비가 다룬다 — 이 화면은 배신을 알려 주지 않는다.
    /// </summary>
    public sealed class HiringTabView : ITabView
    {
        private static string _tab = "hire";
        private static string _unit = "both";

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            if (!ctx.HiringAvailable)
            {
                Ui.Column(root, 12, 16);
                Ui.Label(root, ctx.T("ui.hire.offline"), Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim);
                return;
            }
            var (header, body, _) = Ui.PageLayout(root, 0);
            Ui.SegmentTabs(header, new[] { ("hire", ctx.T("ui.merc.tab.hire")), ("mine", ctx.T("ui.merc.tab.mine")) }, _tab, id => { _tab = id; app.ShowMain("hiring"); }, 70, 500);
            InfoButton.Add(root, ctx.T("ui.merc.note"));
            if (_tab == "hire") BuildHire(app, root, header, body);
            else BuildMine(app, root, body);
        }

        private static void BuildHire(GameApp app, RectTransform root, RectTransform header, RectTransform body)
        {
            var ctx = app.Context;
            Ui.SegmentTabs(header, new[] { ("both", ctx.T("ui.merc.unit.both")), ("general", ctx.T("ui.merc.unit.general")), ("troops", ctx.T("ui.merc.unit.troops")) }, _unit, id => { _unit = id; app.ShowMain("hiring"); }, 58, 220);
            var next = ctx.NextHires;
            if (!next.IsEmpty) Ui.Label(header, ctx.T("ui.merc.queued", next.Picks.Count), 22, TextAnchor.MiddleLeft, Palette.Good);
            var loading = Ui.Label(body, ctx.T("ui.hire.loading"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            ctx.LoadListings(HireKinds.General, gres => ctx.LoadListings(HireKinds.Unit, ures =>
            {
                if (body == null) return;
                Object.Destroy(loading.gameObject);
                if (gres == null || ures == null) { Ui.Label(body, ctx.T("ui.hire.failed"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad); return; }
                Ui.Label(body, ctx.T("ui.hire.daily", gres.DailyUsed, gres.DailyMax), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                // 등록자마다 묶기 (내 등록은 빼고)
                var owners = new List<string>();
                var gen = new Dictionary<string, HireListing>();
                var units = new Dictionary<string, List<HireListing>>();
                foreach (var l in gres.Listings) { if (l.OwnerAccountId == ctx.AccountId) continue; if (!owners.Contains(l.OwnerAccountId)) owners.Add(l.OwnerAccountId); gen[l.OwnerAccountId] = l; }
                foreach (var l in ures.Listings)
                {
                    if (l.OwnerAccountId == ctx.AccountId) continue;
                    if (!owners.Contains(l.OwnerAccountId)) owners.Add(l.OwnerAccountId);
                    if (!units.TryGetValue(l.OwnerAccountId, out var ul)) units[l.OwnerAccountId] = ul = new List<HireListing>();
                    ul.Add(l);
                }
                ctx.LoadNames(owners, () => { });
                int shown = 0;
                foreach (var owner in owners)
                {
                    gen.TryGetValue(owner, out var g);
                    units.TryGetValue(owner, out var ul);
                    if (_unit == "both" && (g == null || ul == null)) continue;
                    if (_unit == "general" && g == null) continue;
                    if (_unit == "troops" && ul == null) continue;
                    shown++;
                    var card = Ui.Card(body, owner, 20, 6);
                    var top = Ui.Row(card, "Top", 60, 10);
                    Ui.Size(Ui.Tag(top, ctx.DisplayName(owner).Substring(0, 1), new Color(0.59f, 0.43f, 0.24f), 30, 56), width: 70);
                    Ui.Size(Ui.Label(top, ctx.DisplayName(owner) + (g != null ? "  " + ctx.T("ui.hire.generalLine", ctx.T("general.avatar_hired"), g.Level) : ""), Ui.FontSmall), flexibleWidth: 1);
                    if (g != null && _unit != "troops")
                    {
                        Ui.Label(card, HireText.Stats(ctx, g), 22, TextAnchor.MiddleLeft, Palette.Good);
                        Ui.Label(card, HireText.Price(ctx, g), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    }
                    if (ul != null && _unit != "general")
                        foreach (var u in ul) Ui.Label(card, HireText.Line(ctx, u) + "  ·  " + HireText.Price(ctx, u), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    var gl = g; var uls = ul;
                    bool queued = (gl != null && next.Has(gl.Id)) || (uls != null && uls.Exists(x => next.Has(x.Id)));
                    var b = Ui.Button(card, ctx.T(queued ? "ui.merc.queuedOne" : "ui.merc.hire"), () =>
                    {
                        int corps = Mathf.Max(0, ctx.State.CompositionsFor(null).Count - 1);
                        if (gl != null && _unit != "troops" && !next.Has(gl.Id)) next.Picks.Add(new HirePick { Listing = gl, CorpsIndex = corps });
                        if (uls != null && _unit != "general")
                        {
                            var u0 = uls[0];
                            if (!next.Has(u0.Id)) next.Picks.Add(new HirePick { Listing = u0, CorpsIndex = corps, Count = System.Math.Min(u0.Troops, 1000) });
                        }
                        app.ShowMain("hiring");
                    }, 76, fontSize: 26);
                    Ui.SetEnabled(b, !queued);
                }
                if (shown == 0) Ui.Label(body, ctx.T("ui.hire.empty"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
                if (!next.IsEmpty) Ui.Button(body, ctx.T("ui.merc.clearQueued"), () => { ctx.ClearNextHires(); app.ShowMain("hiring"); }, 72, fontSize: 24);
            }));
        }

        private static void BuildMine(GameApp app, RectTransform root, RectTransform body)
        {
            var ctx = app.Context;
            var me = ctx.State.FindGeneral(AvatarGeneral.GeneralId);
            var card = Ui.Card(body, "Me", 20, 8);
            if (me == null) { Ui.Label(card, ctx.T("ui.merc.noAvatar"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim); return; }
            Ui.Label(card, ctx.T("ui.hire.generalLine", ctx.GeneralName(me.GeneralId), me.Level), Ui.FontBody);
            var corps = TerritoryFlags.CorpsOf(ctx.State, me.GeneralId);
            var myTypes = new List<string>();
            if (corps != null) foreach (var p in corps.Picks) { var u = ctx.State.FindUnit(p.UnitId); if (u != null && !myTypes.Contains(u.UnitId)) myTypes.Add(u.UnitId); }
            Ui.Label(card, corps == null ? ctx.T("ui.merc.noCorps") : ctx.T("ui.merc.myCorps", string.Join(" · ", myTypes.ConvertAll(id => FormationTabView.TroopName(ctx, ctx.State.FindUnit(id).TroopType)))), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            bool onFlag = false;
            if (TerritoryTabView.Owners != null) foreach (var o in TerritoryTabView.Owners.Values) if (o.OwnerAccountId == ctx.AccountId && o.GeneralId == me.GeneralId) onFlag = true;
            if (onFlag)
            {
                Ui.Label(card, Ui.Colored(ctx.T("ui.merc.blocked"), Palette.Bad), Ui.FontSmall);
                Ui.Button(card, ctx.T("ui.merc.toTerritory"), () => app.ShowMain("territory"), 72, fontSize: 24);
            }
            var row = Ui.Row(card, "Register", 80, 10);
            var rg = Ui.Button(row, ctx.T("ui.merc.registerGeneral"), () => app.Show(new HirePriceScreen(HireKinds.General, me.GeneralId)), 80, fontSize: 24);
            Ui.SetEnabled(rg, !onFlag && GeneralStatus.CanDeploy(me.Status));
            foreach (var id in myTypes)
            {
                string uid = id;
                var ru = Ui.Button(row, ctx.T("ui.merc.registerUnit", FormationTabView.TroopName(ctx, ctx.State.FindUnit(uid).TroopType)), () => app.Show(new HirePriceScreen(HireKinds.Unit, uid)), 80, fontSize: 22);
                Ui.SetEnabled(ru, !onFlag);
            }
            Ui.Button(body, ctx.T("ui.hire.mail"), () => app.Show(new MailScreen(null, "hiring")), 72, fontSize: 24);
            Ui.Label(body, ctx.T("ui.merc.myListings"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            var loading = Ui.Label(body, ctx.T("ui.hire.loading"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            ctx.LoadListings(HireKinds.General, gres => ctx.LoadListings(HireKinds.Unit, ures =>
            {
                if (body == null) return;
                Object.Destroy(loading.gameObject);
                if (gres == null || ures == null) { Ui.Label(body, ctx.T("ui.hire.failed"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad); return; }
                int n = 0;
                foreach (var l in gres.Listings.Concat(ures.Listings))
                {
                    if (l.OwnerAccountId != ctx.AccountId) continue;
                    n++;
                    var c = Ui.Card(body, l.Id, 20, 6);
                    Ui.Label(c, HireText.Line(ctx, l), Ui.FontSmall);
                    Ui.Label(c, HireText.Price(ctx, l) + "  ·  " + HireText.Stats(ctx, l), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    string id = l.Id;
                    Ui.Button(c, ctx.T("ui.hire.unregister"), () => ctx.Unregister(id, _ => app.ShowMain("hiring")), 64, Palette.Danger, 24);
                }
                if (n == 0) Ui.Label(body, ctx.T("ui.merc.noListings"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            }));
        }
    }

    /// <summary>고용 등록·목록 문구.</summary>
    public static class HireText
    {
        public static string Line(GameContext ctx, HireListing l) =>
            l.Kind == HireKinds.General
                ? ctx.T("ui.hire.generalLine", ctx.GeneralName(l.GeneralId), l.Level)
                : ctx.T("ui.hire.unitLine", FormationTabView.TroopName(ctx, l.TroopType), l.Troops.ToString("N0"), Game.Foundation.Math.Fix.FromRaw(l.TrainingRaw).ToDisplayDouble().ToString("0.##"));

        /// <summary>이력 한 줄: 고용 수, 승률, 퇴각·포로·궤멸, 평균 생존율 (hiring.md 4장).</summary>
        public static string Stats(GameContext ctx, HireListing l)
        {
            var s = l.Stats ?? new HireStats();
            if (s.Hires == 0) return ctx.T("ui.hire.noHistory");
            long winRate = s.Wins * 100 / s.Hires;
            long survive = s.TroopsHired <= 0 ? 0 : (s.TroopsHired - s.TroopsLost) * 100 / s.TroopsHired;
            return ctx.T("ui.hire.stats", s.Hires, winRate, s.Retreats, s.Captured, s.Destroyed, survive);
        }

        /// <summary>최근 전투 (최대 3개): 결과 / 잃은 병력.</summary>
        public static string Recent(GameContext ctx, HireListing l)
        {
            var parts = new List<string>();
            for (int i = 0; i < l.Recent.Count && i < 3; i++)
                parts.Add(ctx.T("ui.hire.result." + l.Recent[i].Result) + " -" + l.Recent[i].TroopsLost.ToString("N0"));
            return ctx.T("ui.hire.recent", string.Join(" · ", parts));
        }

        public static string Price(GameContext ctx, HireListing l) =>
            ctx.T(l.Kind == HireKinds.General ? "ui.hire.price" : "ui.hire.pricePerSoldier", l.Price.ToString("N0"), ctx.T("resource." + l.PayWith));
    }

    /// <summary>등록: 내 장수(출전 가능) 또는 부대(병력 있음) 고르기 → 가격 정하기.</summary>
    public sealed class HireRegisterScreen : GameScreen
    {
        private readonly string _kind;
        public HireRegisterScreen(string kind) { _kind = kind; }

        public override void Build()
        {
            Ui.Column(Root, 12, 32);
            Ui.Size(Ui.Label(Root, T("ui.hire.registerTitle"), Ui.FontTitle), 110);
            InfoButton.Add(Root, T("ui.hire.registerNote"));
            var list = Ui.ScrollList(Root, "Choices", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            if (_kind == HireKinds.General)
                foreach (var g in Ctx.State.Generals)
                {
                    if (!GeneralStatus.CanDeploy(g.Status) || g.GeneralId != AvatarGeneral.GeneralId) continue; // 닉네임 장수만 (R-98)
                    string id = g.GeneralId;
                    Ui.Button(list, T("ui.hire.generalLine", Ctx.GeneralName(id), g.Level), () => App.Show(new HirePriceScreen(_kind, id)), 100, Palette.Card, 30);
                }
            else
                foreach (var u in Ctx.State.Units)
                {
                    if (u.Troops <= 0) continue;
                    string id = u.UnitId;
                    Ui.Button(list, u.UnitId + "  " + T("ui.hire.unitLine", FormationTabView.TroopName(Ctx, u.TroopType), u.Troops.ToString("N0"), u.Training.ToDisplayDouble().ToString("0.##")),
                        () => App.Show(new HirePriceScreen(_kind, id)), 100, Palette.Card, 28);
                }
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("hiring"));
        }
    }

    /// <summary>등록 가격: 추천 가격에서 시작, 10%씩 올리고 내림 (0 ~ 추천 × 상한 배수, R-27). 받을 자원 돈 / 군량.</summary>
    public sealed class HirePriceScreen : GameScreen
    {
        private readonly string _kind;
        private readonly string _id;
        private string _payWith = ResourceIds.Gold;
        private long? _price;
        private string _notice;

        public HirePriceScreen(string kind, string id) { _kind = kind; _id = id; }

        public override void Build()
        {
            var rules = new HireRules(Ctx.Defs);
            long rec = _kind == HireKinds.General
                ? rules.RecommendedPrice(Ctx.State.FindGeneral(_id), _payWith)
                : rules.RecommendedPrice(Ctx.State.FindUnit(_id), _payWith);
            long max = (Ctx.Defs.Balance.HirePriceCapMultiplier * rec).FloorToLong();
            long price = System.Math.Min(_price ?? rec, max);
            long step = System.Math.Max(1, rec / 10);

            Ui.Column(Root, 16, 32);
            Ui.Size(Ui.Label(Root, T("ui.hire.priceTitle"), Ui.FontTitle), 110);
            Ui.Label(Root, _kind == HireKinds.General ? Ctx.GeneralName(_id) : _id, Ui.FontBody);
            Ui.Label(Root, T("ui.hire.recommended", rec.ToString("N0"), max.ToString("N0")), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Size(Ui.Label(Root, T(_kind == HireKinds.General ? "ui.hire.price" : "ui.hire.pricePerSoldier", price.ToString("N0"), T("resource." + _payWith)), Ui.FontTitle, TextAnchor.MiddleCenter), 120);
            var row = Ui.Row(Root, "Price", Ui.ButtonHeight);
            Ui.Button(row, "-" + step.ToString("N0"), () => { _price = System.Math.Max(0, price - step); Rebuild(); });
            Ui.Button(row, "+" + step.ToString("N0"), () => { _price = System.Math.Min(max, price + step); Rebuild(); });
            Ui.Button(Root, T("ui.hire.payWith", T("resource." + _payWith)), () => { _payWith = _payWith == ResourceIds.Gold ? ResourceIds.Supply : ResourceIds.Gold; _price = null; Rebuild(); });
            if (_notice != null) Ui.Label(Root, _notice, Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Text);
            Ui.Size(Ui.Label(Root, "", Ui.FontSmall), flexibleHeight: 1);
            var buttons = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(buttons, T("ui.common.back"), () => App.Show(new HireRegisterScreen(_kind)));
            Ui.Button(buttons, T("ui.hire.submit"), () =>
            {
                var req = new HireListingRequest { Kind = _kind, Price = price, PayWith = _payWith };
                if (_kind == HireKinds.General) req.GeneralId = _id; else req.UnitId = _id;
                _notice = T("ui.hire.loading");
                Rebuild();
                Ctx.RegisterHire(req, r =>
                {
                    if (r != null && r.Ok) { App.ShowMain("hiring"); return; }
                    _notice = r == null ? T("ui.hire.failed") : T("ui.hire.result." + r.Reason);
                    Rebuild();
                });
            });
        }

        private void Rebuild()
        {
            var next = new HirePriceScreen(_kind, _id) { _payWith = _payWith, _price = _price, _notice = _notice };
            App.Show(next);
        }
    }

    /// <summary>우편함: 고용 보상 받기 (행동 mail.claim — 서버가 우편을 확인하고 같은 규칙으로 더한다).</summary>
    public sealed class MailScreen : GameScreen
    {
        private readonly string _notice;
        private readonly string _backTab;
        /// <param name="backTab">[뒤로]로 돌아갈 메인 탭 (고용 / 영토).</param>
        public MailScreen(string notice = null, string backTab = "hiring") { _notice = notice; _backTab = backTab; }

        public override void Build()
        {
            Ui.Column(Root, 12, 32);
            Ui.Size(Ui.Label(Root, T("ui.hire.mailTitle"), Ui.FontTitle), 110);
            if (_notice != null) Ui.Label(Root, _notice, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good);
            var list = Ui.ScrollList(Root, "Mail", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            var loading = Ui.Label(list, T("ui.hire.loading"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            Ctx.LoadMail(mail =>
            {
                if (list == null) return;
                Object.Destroy(loading.gameObject);
                if (mail == null) { Ui.Label(list, T("ui.hire.failed"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad); return; }
                mail.RemoveAll(m => Ctx.ClaimedMail.Contains(m.Id));
                if (mail.Count == 0) Ui.Label(list, T("ui.hire.mailEmpty"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
                foreach (var m in mail)
                {
                    var item = m;
                    var card = Ui.Card(list, m.Id, 20, 6);
                    Ui.Label(card, T(m.Kind == MailKinds.TerritoryIncome ? "ui.mail.territoryIncome" : "ui.hire.mailLine", m.Amount.ToString("N0"), T("resource." + m.Resource),
                        m.RegionId != null ? T("region." + m.RegionId) : ""), Ui.FontSmall);
                    Ui.Button(card, T("ui.hire.claim"), () => App.Show(new MailScreen(Ctx.ClaimMail(item) ? T("ui.hire.claimed") : T("ui.hire.failed"), _backTab)), 72, fontSize: 26);
                }
            });
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain(_backTab));
        }
    }

    /// <summary>
    /// 출진 화면의 고용 고르기: 등록 목록(내 것·이미 고른 것 빼고)에서 하나 → 넣을 군단. 부대는 기본 인원(등록 인원·장수 한도 남은 자리)으로.
    /// </summary>
    public sealed class HirePickScreen : GameScreen
    {
        private static readonly int[] Fractions = { 25, 50, 75, 100 };
        private readonly string _stepId;
        private readonly string _kind;
        /// <summary>부대 고용 인원 = 기본 인원(최대)의 몇 % (화면을 다시 열어도 유지).</summary>
        private static int _percent = 100;

        public HirePickScreen(string stepId, string kind) { _stepId = stepId; _kind = kind; }

        public override void Build()
        {
            var step = Game.Data.Defs.StepDef.Load(Ctx.Data, _stepId);
            var draft = Ctx.HiresFor(_stepId);
            var comps = Ctx.State.CompositionsFor(step.BattleType);
            Ui.Column(Root, 12, 32);
            Ui.Size(Ui.Label(Root, T("ui.hire.pickTitle"), Ui.FontTitle), 110);
            if (_kind == HireKinds.Unit)
            {
                var pct = Ui.Row(Root, "Percent", 80, 8);
                foreach (var f in Fractions)
                {
                    int p = f;
                    Ui.Button(pct, T("ui.hire.percent", p), () => { _percent = p; App.Show(new HirePickScreen(_stepId, _kind)); }, 80, p == _percent ? Palette.ButtonActive : Palette.Button, 28);
                }
            }
            var list = Ui.ScrollList(Root, "Listings", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            var loading = Ui.Label(list, T("ui.hire.loading"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            Ctx.LoadListings(_kind, res =>
            {
                if (list == null) return;
                Object.Destroy(loading.gameObject);
                if (res == null) { Ui.Label(list, T("ui.hire.failed"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad); return; }
                int picked = draft.Picks.Count;
                bool full = res.DailyUsed + picked >= res.DailyMax;
                Ui.Label(list, T("ui.hire.daily", res.DailyUsed + picked, res.DailyMax), 26, TextAnchor.MiddleLeft, full ? Palette.Bad : Palette.TextDim);
                long now = Ctx.Clock.UtcNow.ToUnixTimeSeconds();
                int shown = 0;
                foreach (var l in res.Listings)
                {
                    if (l.OwnerAccountId == Ctx.AccountId || draft.Has(l.Id)) continue;
                    if (l.Kind == HireKinds.General && Ctx.State.FindGeneral(l.GeneralId) != null) continue; // 이미 가진 장수
                    shown++;
                    var listing = l;
                    var card = Ui.Card(list, l.Id, 20, 6);
                    Ui.Label(card, HireText.Line(Ctx, l), Ui.FontSmall);
                    Ui.Label(card, HireText.Price(Ctx, l), 26, TextAnchor.MiddleLeft, Palette.TextDim);
                    Ui.Label(card, HireText.Stats(Ctx, l), 26, TextAnchor.MiddleLeft, Palette.TextDim);
                    long until = 0;
                    bool waiting = res.CooldownUntil != null && res.CooldownUntil.TryGetValue(l.Id, out until) && until > now;
                    if (waiting) Ui.Label(card, T("ui.hire.cooldown", System.Math.Max(1, (until - now + 59) / 60)), 26, TextAnchor.MiddleLeft, Palette.Bad);
                    for (int i = 0; i < comps.Count; i++)
                    {
                        int corps = i;
                        long max = l.Kind == HireKinds.Unit ? draft.DefaultCount(Ctx.State, step.BattleType, l, corps) : 0;
                        long count = max <= 0 ? 0 : System.Math.Max(1, max * _percent / 100);
                        string label = T("ui.hire.toCorps", corps + 1, Ctx.GeneralName(comps[corps].GeneralId))
                                     + (l.Kind == HireKinds.Unit ? "  " + (count > 0 ? T("ui.hire.countShort", count.ToString("N0")) : T("ui.hire.noRoom")) : "");
                        var b = Ui.Button(card, label, () =>
                        {
                            draft.Picks.Add(new HirePick { Listing = listing, CorpsIndex = corps, Count = count });
                            App.Show(new StepScreen(_stepId));
                        }, 72, fontSize: 26);
                        Ui.SetEnabled(b, !waiting && !full && (l.Kind == HireKinds.General || count > 0));
                    }
                }
                if (shown == 0) Ui.Label(list, T("ui.hire.empty"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            });
            Ui.Button(Root, T("ui.common.back"), () => App.Show(new StepScreen(_stepId)));
        }
    }
}
