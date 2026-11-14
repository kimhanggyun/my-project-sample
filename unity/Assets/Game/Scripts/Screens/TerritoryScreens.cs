using System.Collections.Generic;
using Game.Data.Defs;
using Game.Economy;
using Game.Flow;
using Game.Online;
using Game.Services;
using Game.Territory;
using Game.Unity.Boot;
using Game.Foundation.Geo;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 영토 탭 (08 시안, territory-v2.md, R-96 · R-97): 위치 띠 · 영토 지도(깃발 원 · 내 위치 · 꽂을 수 있는 범위, 줌 가까이 · 중간 칸 대표 · 멀리 세력도)
    /// · 하위 탭 주변 부대(지금 여기에 깃발 꽂기, 가까운 순 깃발, [공격 ›]) / 내 깃발(수입) / 연합.
    /// 지도는 지도 회사 없이 좌표와 칸으로 그린다 (Natural Earth 바탕은 다음 단계, 지금은 한지색 칸 줄).
    /// </summary>
    public sealed class TerritoryTabView : ITabView
    {
        internal static LocationResult Last;
        internal static Dictionary<string, TerritoryOwner> Owners;
        internal static string Notice;
        internal static bool NamesFetched;
        /// <summary>줌 단계: 0 가까이 · 1 중간(칸 대표) · 2 멀리(세력도).</summary>
        internal static int Zoom;
        internal static string Tab = "near";
        private static readonly long[] ViewMeters = { 8000, 60000, 600000 };
        private static readonly long[] GridMeters = { 1000, 5000, 50000 };
        private const float MapHeight = 600f;

        private static readonly Color SealRed = new Color(0.79f, 0.34f, 0.25f);
        private static readonly Color AllyGreen = new Color(0.24f, 0.48f, 0.25f);
        private static readonly Color OtherRed = new Color(0.36f, 0.2f, 0.19f);
        private static readonly Color MeBlue = new Color(0.25f, 0.44f, 0.69f);

        public void Build(GameApp app, RectTransform root)
        {
            var ctx = app.Context;
            var flags = ctx.Territory?.Flags;
            if (flags == null || ctx.Location == null || !ctx.Location.IsAvailable)
            {
                Ui.Column(root, 12, 16);
                Ui.Label(root, ctx.T("ui.territory.off"), Ui.FontBody, TextAnchor.MiddleCenter, Palette.TextDim);
                return;
            }
            var (header, body, _) = Ui.PageLayout(root, 0);

            // ---- 위치 띠 ----
            var band = Ui.Row(header, "Band", 88, 10);
            var where = Ui.Label(band, Last == null ? ctx.T("ui.terr.noLocation") : Last.Ok ? ctx.T("ui.terr.here", Last.Point.ToString()) : Ui.Colored(ctx.T("ui.territory.failed", Last.Reason), Palette.Bad), Ui.FontSmall);
            Ui.Size(where, flexibleWidth: 1);
            Ui.Size(Ui.Button(band, ctx.T("ui.territory.locate"), () =>
            {
                where.text = ctx.T("ui.territory.locating");
                ctx.Location.Request(r => { Last = r; NamesFetched = false; if (root != null) app.ShowMain("territory"); });
            }, 80, fontSize: 24), width: 230);
            InfoButton.Add(root, ctx.T("ui.terr.note"));

            long myRadius = TerritoryFlags.RadiusOf(ctx.Defs, ctx.State);
            GeoPoint center = Last != null && Last.Ok ? Last.Point : FakeLocationService.Default;

            // ---- 지도 ----
            var map = Ui.Rect(header, "Map");
            Ui.Size(map, MapHeight);
            Ui.Round(Ui.Background(map, new Color(0.89f, 0.85f, 0.75f)));
            map.gameObject.AddComponent<RectMask2D>();
            var layer = Ui.Rect(map, "Layer");
            layer.anchorMin = layer.anchorMax = new Vector2(0.5f, 0.5f);
            layer.sizeDelta = Vector2.zero;
            float widthPx = 1030f;
            float ppm = widthPx / ViewMeters[Zoom];
            Vector2 Pos(GeoPoint p) => new Vector2(
                (p.LonE6 - center.LonE6) * flags.MetersPerDegreeLon / (float)GeoPoint.Scale * ppm,
                (p.LatE6 - center.LatE6) * flags.MetersPerDegreeLat / (float)GeoPoint.Scale * ppm);
            // 칸 줄
            float step = GridMeters[Zoom] * ppm;
            var lineC = new Color(0.44f, 0.38f, 0.27f, Zoom == 0 ? 0.16f : 0.28f);
            for (float x = -((widthPx / 2) - ((widthPx / 2) % step)); x <= widthPx / 2; x += step) Box(layer, new Vector2(x, 0), new Vector2(2, MapHeight), lineC);
            for (float y = -((MapHeight / 2) - ((MapHeight / 2) % step)); y <= MapHeight / 2; y += step) Box(layer, new Vector2(0, y), new Vector2(widthPx, 2), lineC);

            string myAlliance = null;
            if (Owners != null) foreach (var o in Owners.Values) if (o.OwnerAccountId == ctx.AccountId && o.OwnerAllianceId != null) myAlliance = o.OwnerAllianceId;
            Color Rel(TerritoryOwner o) => o.OwnerAccountId == ctx.AccountId ? SealRed : myAlliance != null && o.OwnerAllianceId == myAlliance ? AllyGreen : OtherRed;

            if (Owners != null)
            {
                if (Zoom == 0)
                {
                    foreach (var o in Owners.Values)
                    {
                        if (!FlagDefs.TryParseKey(o.RegionId, out var cell)) continue;
                        var c = Rel(o);
                        var p = Pos(cell);
                        Disc(layer, p, o.RadiusM * 2 * ppm, new Color(c.r, c.g, c.b, 0.22f));
                        Marker(ctx, layer, p, c, RankLetter(ctx, o.RadiusM), 44);
                    }
                }
                else if (Zoom == 1)
                {
                    // 칸(5km)마다 대표 깃발 하나 (병력 큰 순, 같으면 반지름 = 계급) + 나머지 수, 내 깃발은 늘
                    var groups = new Dictionary<string, List<TerritoryOwner>>();
                    foreach (var o in Owners.Values)
                    {
                        if (!FlagDefs.TryParseKey(o.RegionId, out var cell)) continue;
                        var p = Pos(cell);
                        string g = Mathf.FloorToInt(p.x / step) + "," + Mathf.FloorToInt(p.y / step);
                        if (!groups.TryGetValue(g, out var l)) groups[g] = l = new List<TerritoryOwner>();
                        l.Add(o);
                    }
                    foreach (var l in groups.Values)
                    {
                        l.Sort((a, b) => Troops(b).CompareTo(Troops(a)) != 0 ? Troops(b).CompareTo(Troops(a)) : b.RadiusM.CompareTo(a.RadiusM));
                        FlagDefs.TryParseKey(l[0].RegionId, out var rc);
                        var rp = Pos(rc);
                        Marker(ctx, layer, rp, Rel(l[0]), RankLetter(ctx, l[0].RadiusM), 48);
                        if (l.Count > 1) Chip(layer, rp + new Vector2(48, 40), "+" + (l.Count - 1), new Color(0.16f, 0.13f, 0.1f));
                        foreach (var o in l)
                            if (o != l[0] && o.OwnerAccountId == ctx.AccountId && FlagDefs.TryParseKey(o.RegionId, out var mc)) Marker(ctx, layer, Pos(mc), SealRed, RankLetter(ctx, o.RadiusM), 30);
                    }
                }
                else
                {
                    // 세력도: 칸(50km)마다 가장 센 연합(없으면 주인) 색
                    var power = new Dictionary<string, Dictionary<string, long>>();
                    var colorOf = new Dictionary<string, Color>();
                    foreach (var o in Owners.Values)
                    {
                        if (!FlagDefs.TryParseKey(o.RegionId, out var cell)) continue;
                        var p = Pos(cell);
                        string g = Mathf.FloorToInt(p.x / step) + "," + Mathf.FloorToInt(p.y / step);
                        string side = o.OwnerAllianceId ?? ("acct:" + o.OwnerAccountId);
                        if (!power.TryGetValue(g, out var d)) power[g] = d = new Dictionary<string, long>();
                        d[side] = (d.TryGetValue(side, out var v) ? v : 0) + Troops(o) + 1;
                        colorOf[side] = Rel(o);
                    }
                    foreach (var kv in power)
                    {
                        string best = null; long bv = -1;
                        foreach (var s in kv.Value) if (s.Value > bv || (s.Value == bv && string.CompareOrdinal(s.Key, best) < 0)) { best = s.Key; bv = s.Value; }
                        var parts = kv.Key.Split(',');
                        var cpos = new Vector2((int.Parse(parts[0]) + 0.5f) * step, (int.Parse(parts[1]) + 0.5f) * step);
                        var col = colorOf[best];
                        Box(layer, cpos, new Vector2(step - 4, step - 4), new Color(col.r, col.g, col.b, 0.45f));
                    }
                }
            }
            // 내 위치 · 꽂을 수 있는 범위
            if (Zoom < 2) Disc(layer, Vector2.zero, myRadius * 2 * ppm, new Color(MeBlue.r, MeBlue.g, MeBlue.b, 0.10f));
            Disc(layer, Vector2.zero, 26, MeBlue);
            // 줌 버튼 · 줌 칩
            var zoomCol = Ui.Rect(map, "Zoom");
            zoomCol.anchorMin = zoomCol.anchorMax = new Vector2(1, 0.5f);
            zoomCol.pivot = new Vector2(1, 0.5f);
            zoomCol.anchoredPosition = new Vector2(-14, 0);
            zoomCol.sizeDelta = new Vector2(70, 160);
            Ui.Column(zoomCol, 12, 0);
            Ui.SetEnabled(Ui.Button(zoomCol, "+", () => { Zoom = Mathf.Max(0, Zoom - 1); app.ShowMain("territory"); }, 70, fontSize: 34), Zoom > 0);
            Ui.SetEnabled(Ui.Button(zoomCol, "−", () => { Zoom = Mathf.Min(2, Zoom + 1); app.ShowMain("territory"); }, 70, fontSize: 34), Zoom < 2);
            var chip = Chip(map, Vector2.zero, ctx.T("ui.terr.zoom." + Zoom, ViewMeters[Zoom] / 1000), new Color(0.98f, 0.96f, 0.93f), Palette.Text);
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0, 0);
            chip.anchoredPosition = new Vector2(14, 14);
            Ui.Label(header, ctx.T("ui.terr.legend", ctx.T("rank." + ctx.Defs.Campaign.RankFor(ctx.State.LordLevel).Id), FormatKm(myRadius)), 22, TextAnchor.MiddleLeft, Palette.TextDim);

            // ---- 하위 탭 ----
            Ui.SegmentTabs(header, new[] { ("near", ctx.T("ui.terr.tab.near")), ("mine", ctx.T("ui.terr.tab.mine")), ("ally", ctx.T("ui.terr.tab.ally")) }, Tab, id => { Tab = id; app.ShowMain("territory"); }, 58, 200);

            if (Notice != null) { Ui.Label(body, Notice, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good); Notice = null; }
            if (!ctx.OnlineEnabled) { Ui.Label(body, ctx.T("ui.territory.needOnline"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim); return; }
            string loc = Last != null && Last.Ok ? Last.Point.ToString() : null;

            if (Tab == "near")
            {
                var plantCard = Ui.Card(body, "Plant", 20, 8);
                Ui.Label(plantCard, ctx.T("ui.terr.plantTitle"), Ui.FontBody);
                Ui.Label(plantCard, ctx.T("ui.terr.plantNote", FormatKm(myRadius)), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                var plantBtn = Ui.Button(plantCard, ctx.T("ui.terr.plant"), null, 80, fontSize: 28);
                Ui.SetEnabled(plantBtn, loc != null);
                plantBtn.onClick.AddListener(() =>
                {
                    var items = new List<(string, string, bool, System.Action)>();
                    foreach (var c in ctx.State.CompositionsFor(null))
                    {
                        string gid = c.GeneralId;
                        bool busy = false;
                        if (Owners != null) foreach (var o in Owners.Values) if (o.OwnerAccountId == ctx.AccountId && o.GeneralId == gid) busy = true;
                        items.Add((ctx.GeneralName(gid), busy ? ctx.T("ui.terr.onFlag") : "", !busy, () => ctx.PlantFlag(new PlantRequest { Location = loc, GeneralId = gid }, r =>
                        {
                            Notice = r == null ? ctx.T("ui.hire.failed") : ctx.T("ui.terr.plant." + r.Reason);
                            NamesFetched = false;
                            if (root != null) app.ShowMain("territory");
                        })));
                    }
                    Ui.PickMenu(root, (RectTransform)plantBtn.transform, 460, items.ToArray());
                });
                if (ctx.Online is LocalOnlineService fake && loc != null)
                    Ui.Button(body, ctx.T("ui.terr.seed"), () => { SeedNearby(ctx, fake, center); app.ShowMain("territory"); }, 72, fontSize: 24);
            }
            var list = body;
            var loading = Ui.Label(list, ctx.T("ui.hire.loading"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            ctx.LoadTerritory(res =>
            {
                if (list == null) return;
                Object.Destroy(loading.gameObject);
                if (res == null) { Ui.Label(list, ctx.T("ui.hire.failed"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad); return; }
                bool first = Owners == null;
                var owners = new Dictionary<string, TerritoryOwner>();
                foreach (var o in res.Owners) owners[o.RegionId] = o;
                Owners = owners;
                if (!NamesFetched)
                {
                    var ids = new List<string>();
                    foreach (var o in res.Owners) ids.Add(o.OwnerAccountId);
                    NamesFetched = true;
                    ctx.LoadNames(ids, () => { if (root != null) app.ShowMain("territory"); });
                    return;
                }
                if (first) { app.ShowMain("territory"); return; }
                var flagsList = new List<(TerritoryOwner o, long dist2)>();
                foreach (var o in owners.Values)
                    if (FlagDefs.TryParseKey(o.RegionId, out var cell)) flagsList.Add((o, flags.DistanceSquaredM(center, cell)));
                flagsList.Sort((a, b) => a.dist2.CompareTo(b.dist2));
                long now = ctx.Clock.UtcNow.ToUnixTimeSeconds();

                if (Tab == "near")
                {
                    Ui.Label(list, ctx.T("ui.terr.nearHeader"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);
                    int shown = 0;
                    foreach (var (o, d2) in flagsList)
                    {
                        if (o.OwnerAccountId == ctx.AccountId) continue;
                        shown++;
                        var card = Ui.Card(list, o.RegionId, 20, 6);
                        var top = Ui.Row(card, "Top", 52, 10);
                        Ui.Size(Ui.Tag(top, RankLetter(ctx, o.RadiusM), Rel(o), 26, 48), width: 64);
                        string owner = ctx.DisplayName(o.OwnerAccountId) + (o.OwnerAllianceName != null ? "  [" + o.OwnerAllianceName + "]" : "");
                        Ui.Size(Ui.Label(top, owner, Ui.FontSmall), flexibleWidth: 1);
                        Ui.Label(card, ctx.T("ui.terr.flagLine", ctx.GeneralName(o.GeneralId), Troops(o).ToString("N0"), FormatKm((long)Mathf.Sqrt(d2))), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                        bool friendly = myAlliance != null && o.OwnerAllianceId == myAlliance;
                        string err = friendly ? ctx.T("ui.terr.ally") : loc == null ? ctx.T("ui.terr.noLocation")
                            : TerritoryFlags.CheckSiege(ctx.Territory, ctx.Defs, ctx.State, ctx.AccountId, owners, o.RegionId, loc, now);
                        if (err == null)
                        {
                            string key = o.RegionId; var def = o.Defense;
                            Ui.Button(card, ctx.T("ui.terr.attack"), () => app.Show(new TerritorySortieScreen(key, loc, def)), 72, fontSize: 26);
                        }
                        else Ui.Label(card, o.ProtectedUntilUnixSeconds > now ? ctx.T("ui.terr.protected", (o.ProtectedUntilUnixSeconds - now + 59) / 60) : err, 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    }
                    if (shown == 0) Ui.Label(list, ctx.T("ui.terr.nobody"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
                }
                else if (Tab == "mine")
                {
                    Ui.Button(list, ctx.T("ui.territory.collect"), () => ctx.CollectTerritory(r =>
                    {
                        if (root == null) return;
                        app.Show(new MailScreen(r == null ? ctx.T("ui.hire.failed") : r.Mail.Count == 0 ? ctx.T("ui.territory.nothing") : ctx.T("ui.territory.collected", r.Mail.Count), "territory"));
                    }), 80, fontSize: 28);
                    int shown = 0;
                    foreach (var (o, d2) in flagsList)
                    {
                        if (o.OwnerAccountId != ctx.AccountId) continue;
                        shown++;
                        var card = Ui.Card(list, o.RegionId, 20, 6);
                        var top = Ui.Row(card, "Top", 52, 10);
                        Ui.Size(Ui.Tag(top, RankLetter(ctx, o.RadiusM), SealRed, 26, 48), width: 64);
                        Ui.Size(Ui.Label(top, ctx.T("ui.terr.myFlag", ctx.GeneralName(o.GeneralId)), Ui.FontSmall), flexibleWidth: 1);
                        Ui.Label(card, ctx.T("ui.terr.myFlagLine", Troops(o).ToString("N0"), FormatKm(o.RadiusM), (now - o.SinceUnixSeconds) / 86400, FormatKm((long)Mathf.Sqrt(d2))), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                        Ui.Label(card, Pending(ctx, o), 22, TextAnchor.MiddleLeft, Palette.Good);
                        if (o.ProtectedUntilUnixSeconds > now) Ui.Label(card, ctx.T("ui.terr.protected", (o.ProtectedUntilUnixSeconds - now + 59) / 60), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    }
                    if (shown == 0) Ui.Label(list, ctx.T("ui.terr.noMine"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
                }
                else
                {
                    int allyFlags = 0;
                    foreach (var (o, _) in flagsList) if (myAlliance != null && o.OwnerAllianceId == myAlliance) allyFlags++;
                    Ui.Label(list, myAlliance == null ? ctx.T("ui.terr.noAlliance") : ctx.T("ui.terr.allyFlags", allyFlags), Ui.FontSmall);
                    Ui.Button(list, ctx.T("ui.alliance.button"), () => app.Show(new AllianceScreen()), 80, fontSize: 28);
                }
            });
        }

        /// <summary>깃발 수비 스냅샷의 병력 합.</summary>
        private static long Troops(TerritoryOwner o)
        {
            long t = 0;
            if (o?.Defense?.Corps == null) return 0;
            foreach (var c in o.Defense.Corps) foreach (var u in c.Units) t += OnlineJson.Read<Game.Campaign.Roster.UnitState>(u.UnitJson)?.Troops ?? 0;
            return t;
        }

        /// <summary>반지름 → 계급 첫 글자 (깃 글자).</summary>
        private static string RankLetter(GameContext ctx, long radius)
        {
            string id = null;
            foreach (var r in ctx.Defs.Campaign.Ranks) if (r.FlagRadiusM <= radius) id = r.Id;
            string name = ctx.T("rank." + (id ?? "squad"));
            return string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
        }

        private static string FormatKm(long m) => m >= 1000 ? (m / 1000f).ToString("0.#") + "km" : m + "m";

        private static string Pending(GameContext ctx, TerritoryOwner o)
        {
            if (o.Pending == null || o.Pending.Count == 0) return ctx.T("ui.territory.pendingNone");
            var parts = new List<string>();
            foreach (var kv in o.Pending) parts.Add(ctx.T("resource." + kv.Key) + " " + kv.Value.ToString("N0"));
            return ctx.T("ui.territory.pending", string.Join(" · ", parts));
        }

        /// <summary>가짜 서버 시험용: 내 위치 둘레에 다른 유저 깃발 몇 개 (주인 없는 계정, 수비 = 내 첫 군단 사본).</summary>
        private static void SeedNearby(GameContext ctx, LocalOnlineService fake, GeoPoint center)
        {
            var flags = ctx.Territory.Flags;
            var first = ctx.State.CompositionsFor(null);
            if (first.Count == 0) return;
            var def = TerritoryDefenses.SnapshotGeneral(ctx.Defs, ctx.State, first[0].GeneralId, out _);
            if (def != null) def.Version = 1;
            var spots = new (long dLat, long dLon, string who, long radius)[] { (6000, 2000, "rival-1", 2000), (-9000, 7000, "rival-2", 3000), (15000, -12000, "rival-3", 1000) };
            foreach (var s in spots)
            {
                var cell = flags.CellOf(new GeoPoint(center.LatE6 + s.dLat, center.LonE6 + s.dLon));
                fake.SeedFlag(new TerritoryOwner { RegionId = FlagDefs.KeyOf(cell), OwnerAccountId = s.who, GeneralId = first[0].GeneralId, RadiusM = s.radius, Defense = def, SinceUnixSeconds = ctx.Clock.UtcNow.ToUnixTimeSeconds(), CollectedUnixSeconds = ctx.Clock.UtcNow.ToUnixTimeSeconds() });
            }
            NamesFetched = false;
        }

        // ---- 지도 그림 조각 ----
        private static RectTransform Box(RectTransform parent, Vector2 pos, Vector2 size, Color c)
        {
            var rt = Ui.Rect(parent, "Box");
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Ui.Background(rt, c).raycastTarget = false;
            return rt;
        }

        private static void Disc(RectTransform parent, Vector2 pos, float diameter, Color c)
        {
            var rt = Box(parent, pos, new Vector2(diameter, diameter), c);
            rt.GetComponent<Image>().sprite = UiShapes.Circle;
        }

        private static void Marker(GameContext ctx, RectTransform parent, Vector2 pos, Color c, string letter, float size)
        {
            Box(parent, pos + new Vector2(0, size * 0.5f), new Vector2(4, size * 1.1f), new Color(0.16f, 0.13f, 0.1f));
            var flag = Box(parent, pos + new Vector2(size * 0.45f, size * 0.85f), new Vector2(size * 0.9f, size * 0.6f), c);
            var t = Ui.Label(flag, letter, (int)(size * 0.42f), TextAnchor.MiddleCenter, Color.white);
            Ui.Fill(t.rectTransform);
        }

        private static RectTransform Chip(RectTransform parent, Vector2 pos, string text, Color back, Color? fore = null)
        {
            var rt = Ui.Rect(parent, "Chip");
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(Mathf.Max(70, text.Length * 18 + 30), 40);
            Ui.Round(Ui.Background(rt, back));
            var t = Ui.Label(rt, text, 20, TextAnchor.MiddleCenter, fore ?? Color.white);
            Ui.Fill(t.rectTransform);
            return rt;
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
            InfoButton.Add(Root, T("ui.alliance.note"));
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
                            Ui.Size(Ui.Button(row, T("ui.alliance.kick"), () => Ctx.ManageAlliance(AllianceActions.Kick, id, r => Reopen(r)), 64, Palette.Danger, 22), width: 200);
                    }
                    Ui.Button(card, T("ui.alliance.leave"), () => Ctx.LeaveAlliance(r => Reopen(r)), 72, Palette.Danger, 26);
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
