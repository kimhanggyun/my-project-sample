using System.Collections.Generic;
using Game.Data.Defs;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Game.Services;
using Game.Territory;
using Game.Unity.Boot;
using Game.Unity.UI;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 영토 탭 (territory.md 3·5·7장): [내 위치 확인] → 지금 지역(시·도 → … → 가장 작은 지역)과 공격할 수 있는 지역(단계마다 지금 지역 + 이웃),
    /// 지역마다 주인(나 / 다른 유저 / 없음). 혼자 차지할 수 있는 지역은 [공격] → 영토 출진 (AI 수비군, R-46).
    /// 위치는 버튼을 누를 때만 쓴다 (계속 추적하지 않음). 연합·지도·수비 편성은 다음 단계.
    /// </summary>
    public sealed class TerritoryTabView : ITabView
    {
        /// <summary>마지막 확인 결과 (탭을 다시 열어도 보이게, 위치를 다시 묻지 않는다).</summary>
        internal static LocationResult Last;
        /// <summary>마지막으로 받은 주인 목록 (공격할 때 그 지역의 수비 편성을 넘긴다).</summary>
        internal static Dictionary<string, TerritoryOwner> Owners;
        /// <summary>한 번 보여줄 알림 (수비 등록 결과 등).</summary>
        internal static string Notice;
        /// <summary>이번에 주인 닉네임을 받았는지 (다시 그릴 때 또 묻지 않게, 위치를 다시 확인하면 새로).</summary>
        internal static bool NamesFetched;

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            Ui.Column(root, 12, 16);
            if (ctx.Territory == null || ctx.Location == null || !ctx.Location.IsAvailable)
            {
                Ui.Label(root, ctx.T("ui.territory.off"), Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim);
                return;
            }
            Ui.Label(root, ctx.T("ui.territory.note"), 26, TextAnchor.MiddleLeft, Palette.TextDim);
            var status = Ui.Label(root, "", Ui.FontSmall);
            if (Notice != null) { Ui.Label(root, Notice, 26, TextAnchor.MiddleLeft, Palette.Good); Notice = null; }
            if (ctx.OnlineEnabled)
            {
                var tools = Ui.Row(root, "Income", 88, 8);
                Ui.Button(tools, ctx.T("ui.territory.collect"), () => ctx.CollectTerritory(r =>
                {
                    if (root == null) return;
                    app.Show(new MailScreen(r == null ? ctx.T("ui.hire.failed") : r.Mail.Count == 0 ? ctx.T("ui.territory.nothing") : ctx.T("ui.territory.collected", r.Mail.Count), "territory"));
                }), 88, fontSize: 28);
                Ui.Button(tools, ctx.T("ui.hire.mail"), () => app.Show(new MailScreen(null, "territory")), 88, fontSize: 28);
                Ui.Button(tools, ctx.T("ui.alliance.button"), () => app.Show(new AllianceScreen()), 88, fontSize: 28);
            }
            Ui.Button(root, ctx.T("ui.territory.locate"), () =>
            {
                status.text = ctx.T("ui.territory.locating");
                ctx.Location.Request(r => { Last = r; NamesFetched = false; if (root != null) app.ShowMain("territory"); });
            }, 100);

            var list = Ui.ScrollList(root, "Regions", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            if (Last == null) return;
            if (!Last.Ok) { status.text = Ui.Colored(ctx.T("ui.territory.failed", Last.Reason), Palette.Bad); return; }

            var locator = new RegionLocator(ctx.Territory);
            var path = locator.Locate(Last.Point);
            if (path.Count == 0) { status.text = ctx.T("ui.territory.outside", Last.Point.ToString()); return; }
            var names = new List<string>();
            foreach (var r in path) names.Add(ctx.T(r.NameKey));
            status.text = ctx.T("ui.territory.here", string.Join(" › ", names));
            if (!ctx.OnlineEnabled) Ui.Label(list, ctx.T("ui.territory.needOnline"), 26, TextAnchor.MiddleLeft, Palette.TextDim);

            Ui.Label(list, ctx.T("ui.territory.attackable"), Ui.FontBody);
            var here = new HashSet<string>();
            foreach (var r in path) here.Add(r.Id);
            var ownerLabels = new Dictionary<string, UnityEngine.UI.Text>();
            var cards = new Dictionary<string, RectTransform>();
            foreach (var r in locator.Attackable(Last.Point))
            {
                var card = Ui.Card(list, r.Id, 20, 6);
                var level = ctx.Territory.Levels[r.Level];
                string tags = (here.Contains(r.Id) ? ctx.T("ui.territory.current") : "") + (level.SeasonOnly ? ctx.T("ui.territory.season") : "");
                Ui.Label(card, ctx.T("ui.territory.regionLine", ctx.T(r.NameKey), ctx.T("ui.territory.level." + r.Level), ctx.Territory.RequiredLegions(r), tags), Ui.FontSmall);
                ownerLabels[r.Id] = Ui.Label(card, ctx.OnlineEnabled ? ctx.T("ui.hire.loading") : ctx.T("ui.territory.ownerUnknown"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
                cards[r.Id] = card;
                string err = TerritoryBattles.CheckSortie(ctx.Territory, r.Id, Last.Point.ToString());
                if (err == null)
                {
                    string id = r.Id;
                    var b = Ui.Button(card, ctx.T("ui.territory.attack"), () => app.Show(new TerritorySortieScreen(id, Last.Point.ToString(), Owners != null && Owners.TryGetValue(id, out var ow) ? ow.Defense : null)), 72, fontSize: 26);
                    Ui.SetEnabled(b, ctx.OnlineEnabled);
                }
                else Ui.Label(card, err, 22, TextAnchor.MiddleLeft, Palette.TextDim);
            }
            if (!ctx.OnlineEnabled) return;
            ctx.LoadTerritory(res =>
            {
                if (list == null) return;
                var owners = new Dictionary<string, TerritoryOwner>();
                if (res != null) foreach (var o in res.Owners) owners[o.RegionId] = o;
                Owners = owners;
                if (res != null && !NamesFetched)
                {
                    // 주인 닉네임을 받은 뒤 다시 그린다 (한 번만)
                    var ids = new List<string>();
                    foreach (var o in res.Owners) ids.Add(o.OwnerAccountId);
                    NamesFetched = true;
                    ctx.LoadNames(ids, () => { if (root != null) app.ShowMain("territory"); });
                }
                foreach (var kv in ownerLabels)
                {
                    if (kv.Value == null) continue;
                    bool has = owners.TryGetValue(kv.Key, out var o);
                    bool mineRegion = has && o.OwnerAccountId == ctx.AccountId;
                    string defense = has ? (o.Defense != null ? ctx.T("ui.territory.defenseOwner", o.Defense.Corps.Count) : ctx.T("ui.territory.defenseAi")) : "";
                    if (has && o.OwnerAllianceName != null) defense = ctx.T("ui.territory.ownerAlliance", o.OwnerAllianceName) + "  " + defense;
                    kv.Value.text = res == null ? ctx.T("ui.hire.failed")
                        : !has ? ctx.T("ui.territory.owner")
                        : mineRegion ? Ui.Colored(ctx.T("ui.territory.mine"), Palette.Good) + "  " + Pending(ctx, o) + "\n" + defense
                        : Ui.Colored(ctx.T("ui.territory.othersNamed", ctx.DisplayName(o.OwnerAccountId)), Palette.Enemy) + "  " + defense;
                    if (mineRegion && cards.TryGetValue(kv.Key, out var card) && card != null)
                    {
                        string id = kv.Key;
                        Ui.Button(card, ctx.T("ui.territory.registerDefense"), () => ctx.RegisterDefense(id, r =>
                        {
                            Notice = r == null ? ctx.T("ui.hire.failed") : r.Ok ? ctx.T("ui.territory.defenseRegistered", r.Defense.Version) : ctx.T("ui.territory.defense." + r.Reason);
                            if (root != null) app.ShowMain("territory");
                        }), 72, fontSize: 26);
                    }
                }
            });
        }

        /// <summary>쌓인 수입 한 줄 (R-47).</summary>
        private static string Pending(GameContext ctx, TerritoryOwner o)
        {
            if (o.Pending == null || o.Pending.Count == 0) return ctx.T("ui.territory.pendingNone");
            var parts = new List<string>();
            foreach (var kv in o.Pending) parts.Add(ctx.T("resource." + kv.Key) + " " + kv.Value.ToString("N0"));
            return ctx.T("ui.territory.pending", string.Join(" · ", parts));
        }
    }

    /// <summary>
    /// 연합 (territory.md 6장, R-49): 내 연합(이름·인원·[탈퇴]) 또는 만들기(이름 입력), 다른 연합 목록과 [가입] (지금은 신청 없이 바로).
    /// </summary>
    public sealed class AllianceScreen : GameScreen
    {
        private readonly string _notice;
        private bool _namesLoaded;
        public AllianceScreen(string notice = null) { _notice = notice; }

        public override void Build()
        {
            Ui.Column(Root, 12, 32);
            Ui.Size(Ui.Label(Root, T("ui.alliance.title"), Ui.FontTitle), 110);
            Ui.Label(Root, T("ui.alliance.note"), 26, TextAnchor.MiddleLeft, Palette.TextDim);
            if (_notice != null) Ui.Label(Root, _notice, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good);
            var list = Ui.ScrollList(Root, "Alliances", 10, 8);
            Ui.Size(list.parent.parent, flexibleHeight: 1);
            var loading = Ui.Label(list, T("ui.hire.loading"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            Ctx.LoadAlliances(res =>
            {
                if (list == null) return;
                Object.Destroy(loading.gameObject);
                if (res == null) { Ui.Label(list, T("ui.hire.failed"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad); return; }
                Alliance mine = res.MineId == null ? null : res.Alliances.Find(a => a.Id == res.MineId);
                if (mine != null && !_namesLoaded)
                {
                    // 연합원·신청자 닉네임을 받은 뒤 다시 그린다
                    var ids = new List<string>(mine.Members);
                    ids.AddRange(mine.Requests);
                    Ctx.LoadNames(ids, () => { if (Root != null) App.Show(new AllianceScreen(_notice) { _namesLoaded = true }); });
                }
                if (mine != null)
                {
                    var card = Ui.Card(list, "Mine", 20, 6);
                    bool leader = mine.LeaderAccountId == Ctx.AccountId;
                    Ui.Label(card, T("ui.alliance.mine", mine.Name, mine.Members.Count, res.MemberMax) + (leader ? "  " + T("ui.alliance.leader") : ""), Ui.FontSmall);
                    Ui.Label(card, T(mine.Open ? "ui.alliance.openOn" : "ui.alliance.openOff"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
                    if (leader)
                    {
                        Ui.Button(card, T(mine.Open ? "ui.alliance.close" : "ui.alliance.open"), () => Ctx.ManageAlliance(mine.Open ? AllianceActions.Close : AllianceActions.Open, null, r => Reopen(r)), 72, fontSize: 26);
                        foreach (var who in mine.Requests)
                        {
                            string id = who;
                            var row = Ui.Row(card, "Req" + id, 72, 8);
                            Ui.Size(Ui.Label(row, T("ui.alliance.request", Short(id)), 24), flexibleWidth: 2);
                            Ui.Button(row, T("ui.alliance.accept"), () => Ctx.ManageAlliance(AllianceActions.Accept, id, r => Reopen(r)), 72, fontSize: 24);
                            Ui.Button(row, T("ui.alliance.reject"), () => Ctx.ManageAlliance(AllianceActions.Reject, id, r => Reopen(r)), 72, fontSize: 24);
                        }
                    }
                    foreach (var who in mine.Members)
                    {
                        string id = who;
                        var row = Ui.Row(card, "Mem" + id, 64, 8);
                        Ui.Size(Ui.Label(row, Short(id) + (id == mine.LeaderAccountId ? "  " + T("ui.alliance.leader") : "") + (id == Ctx.AccountId ? "  " + T("ui.alliance.me") : ""), 24, TextAnchor.MiddleLeft, Palette.TextDim), flexibleWidth: 3);
                        if (leader && id != Ctx.AccountId)
                            Ui.Size(Ui.Button(row, T("ui.alliance.kick"), () => Ctx.ManageAlliance(AllianceActions.Kick, id, r => Reopen(r)), 64, new Color(0.45f, 0.22f, 0.20f), 22), width: 200);
                    }
                    Ui.Button(card, T("ui.alliance.leave"), () => Ctx.LeaveAlliance(r => Reopen(r)), 72, new Color(0.45f, 0.22f, 0.20f), 26);
                }
                else
                {
                    var card = Ui.Card(list, "Create", 20, 6);
                    Ui.Label(card, T("ui.alliance.createTitle"), Ui.FontSmall);
                    var name = Ui.TextInput(card, T("ui.alliance.namePlaceholder"), 12, 88, 30);
                    Ui.Button(card, T("ui.alliance.create"), () => Ctx.CreateAlliance(name.text, r => Reopen(r)), 72, fontSize: 26);
                }
                Ui.Label(list, T("ui.alliance.others"), Ui.FontBody);
                int shown = 0;
                foreach (var a in res.Alliances)
                {
                    if (a.Id == res.MineId) continue;
                    shown++;
                    var card = Ui.Card(list, a.Id, 20, 6);
                    Ui.Label(card, T("ui.alliance.line", a.Name, a.Members.Count, res.MemberMax) + "  " + T(a.Open ? "ui.alliance.openOn" : "ui.alliance.openOff"), Ui.FontSmall);
                    string id = a.Id;
                    bool requested = a.Requests != null && a.Requests.Contains(Ctx.AccountId);
                    var join = Ui.Button(card, T(requested ? "ui.alliance.requested" : a.Open ? "ui.alliance.join" : "ui.alliance.apply"), () => Ctx.JoinAlliance(id, r => Reopen(r)), 72, fontSize: 26);
                    Ui.SetEnabled(join, mine == null && !requested && a.Members.Count < res.MemberMax);
                }
                if (shown == 0) Ui.Label(list, T("ui.alliance.none"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            });
            Ui.Button(Root, T("ui.common.back"), () => App.ShowMain("territory"));
        }

        private string Short(string accountId) => Ctx.DisplayName(accountId);

        private void Reopen(AllianceResponse r)
        {
            if (Root == null) return;
            App.Show(new AllianceScreen(r == null ? T("ui.hire.failed") : T("ui.alliance.result." + r.Reason)));
        }
    }

    /// <summary>
    /// 영토 출진 (R-46): 지역의 수비군을 보여주고 [직접 지휘] / [위임]. 스토리 출진과 같은 준비·확정·전투·결과 (결과는 서버가 재현해 맞으면 차지).
    /// </summary>
    public sealed class TerritorySortieScreen : GameScreen
    {
        private readonly string _regionId;
        private readonly string _location;
        private readonly TerritoryDefense _defense;

        /// <param name="defense">주인의 수비 편성 (없으면 AI 수비군).</param>
        public TerritorySortieScreen(string regionId, string location, TerritoryDefense defense = null) { _regionId = regionId; _location = location; _defense = defense; }

        private string StepId => TerritoryBattles.StepIdOf(_regionId);

        public override void Build()
        {
            var step = Ctx.Flow.LoadStep(StepId);
            Ui.Column(Root, 16, 32);
            Ui.Size(Ui.Label(Root, T(step.NameKey), Ui.FontTitle), 120);
            var body = Ui.ScrollList(Root, "Info", 12, 8);
            Ui.Size(body.parent.parent, flexibleHeight: 1);
            var info = Ui.Card(body, "Defense");
            Ui.Label(info, T(_defense != null ? "ui.territory.defenseOwnerTitle" : "ui.territory.defense"));
            var plan0 = Ctx.Flow.Prepare(Ctx.State, Request(false));
            foreach (var c in plan0.Setup.Corps)
            {
                if (c.Team != StoryFlow.EnemyTeam) continue;
                long troops = 0;
                foreach (var u in c.Units) troops += u.Troops;
                Ui.Label(info, Ui.Colored(Ctx.GeneralName(c.General.Id), Palette.Enemy) + "  " + troops.ToString("N0"), Ui.FontSmall);
            }
            Ui.Label(info, T("ui.step.maxMinutes", step.MaxMinutes), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
            Ui.Label(info, T("ui.territory.captureNote"), 24, TextAnchor.MiddleLeft, Palette.TextDim);

            var plan = Ctx.Flow.Prepare(Ctx.State, Request(false));
            var sortie = Ui.Card(body, "Sortie");
            Ui.Label(sortie, T("ui.step.supply", plan.SupplyTaken.ToString("N0"), Ctx.Resource(ResourceIds.Supply).ToString("N0")), Ui.FontSmall);
            foreach (var err in plan.Errors) Ui.Label(sortie, Ui.Colored(err, Palette.Bad), Ui.FontSmall);

            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            Ui.Button(row, T("ui.common.back"), () => App.ShowMain("territory"));
            var direct = Ui.Button(row, T("ui.step.directSortie"), () => Sortie(step, false));
            var delegated = Ui.Button(row, T("ui.step.delegateSortie"), () => Sortie(step, true));
            Ui.SetEnabled(direct, plan.Ok && Ctx.OnlineEnabled);
            Ui.SetEnabled(delegated, plan.Ok && Ctx.OnlineEnabled);
        }

        private SortieRequest Request(bool delegate_) => new SortieRequest { StepId = StepId, Seed = Ctx.NewSeed(), Delegate = delegate_, Location = _location, Defense = _defense };

        private void Sortie(StepDef step, bool delegate_)
        {
            Ctx.Flow.Tick(Ctx.State); // 출진 시각 기준 시간 경과 (서버도 같은 시각으로 맞춘다, R-61)
            var plan = Ctx.Flow.Prepare(Ctx.State, Request(delegate_));
            if (!Ctx.Flow.Confirm(Ctx.State, plan)) { App.Show(new TerritorySortieScreen(_regionId, _location, _defense)); return; }
            App.Show(new DialogueScreen(plan, "dialogue." + step.Id + ".start", !delegate_));
        }
    }
}
