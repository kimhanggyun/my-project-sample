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
        internal static int Zoom = 1;
        /// <summary>지도를 끌어 옮긴 양 (미터, 동 · 북). 0이면 내 위치가 가운데. [내 위치]로 되돌린다.</summary>
        internal static Vector2 PanM;
        private static float _pinch = 1f;
        /// <summary>지도 표시 (좌상단 체크, 2026-11-15 사용자: 아래 주변 부대 · 내 깃발 · 연합 칸 대신): 다른 유저 · 내 깃발 · 연합 깃발.</summary>
        internal static bool ShowOthers = true, ShowMine = true, ShowAlly = true;
        internal static string Tab = "near";
        // 줌 단계 (두 손가락 · 휠 · +/−): 보이는 너비와 칸 줄. 표시 방식 = Mode (0 가까이: 깃발 원 · 1 중간: 칸 대표 · 2 멀리: 세력도)
        private static readonly long[] ViewMeters = { 4000, 8000, 20000, 60000, 200000, 600000, 1500000 };
        private static readonly long[] GridMeters = { 500, 1000, 2500, 5000, 20000, 50000, 100000 };
        private static int Mode => Zoom <= 1 ? 0 : Zoom <= 3 ? 1 : 2;
        /// <summary>지도 높이: 탭 화면을 거의 다 쓴다 (2026-11-15 사용자: 지도를 늘리고 아래 칸 없앰). Build마다 화면 비율로 정한다.</summary>
        private static float MapHeight = 600f;
        /// <summary>내 연합 (없으면 null). 처음 한 번 서버에서 받아 둔다 — 없으면 좌상단에 [연합 가입].</summary>
        internal static string MyAllianceId;
        internal static bool AllianceChecked;

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
            // 캔버스 높이 (너비 1080 맞춤) − 위 막대 · 아래 탭 두 줄 · 여백
            float canvasH = Screen.width > 0 ? 1080f * Screen.height / Screen.width : 1920f;
            if (canvasH < 1920f) canvasH = 1920f;
            MapHeight = Mathf.Max(600f, canvasH - 110f - 104f - 72f - 40f);
            if (!AllianceChecked && ctx.OnlineEnabled)
            {
                AllianceChecked = true;
                ctx.LoadAlliances(r => { MyAllianceId = r?.MineId; if (root != null) app.ShowMain("territory"); });
            }

            // 위치 확인 띠 없음 (2026-11-15 사용자): 위치는 깃발을 꽂거나 공격할 때만, 그때 동의를 받고 확인한다 (WithLocation)
            InfoButton.Add(root, ctx.T("ui.terr.note"));

            long myRadius = TerritoryFlags.RadiusOf(ctx.Defs, ctx.State);
            GeoPoint here = Last != null && Last.Ok ? Last.Point : FakeLocationService.Default;
            // 보는 가운데 = 내 위치 + 끌어 옮긴 양
            GeoPoint center = new GeoPoint(
                here.LatE6 + (long)(PanM.y * GeoPoint.Scale / (double)flags.MetersPerDegreeLat),
                here.LonE6 + (long)(PanM.x * GeoPoint.Scale / (double)flags.MetersPerDegreeLon));

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
            // 바탕 지도 (Natural Earth 1:10m: 바다 · 육지 · 호수 · 강, 수묵 색) — 칸 줄 아래
            var geo = GeoBackdrop.Get(ctx, center, ViewMeters[Zoom], MapHeight / widthPx, flags.MetersPerDegreeLat, flags.MetersPerDegreeLon);
            if (geo != null)
            {
                var raw = new GameObject("GeoBackdrop", typeof(RectTransform)).AddComponent<RawImage>();
                raw.transform.SetParent(layer, false);
                raw.rectTransform.anchorMin = raw.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                raw.rectTransform.sizeDelta = new Vector2(widthPx, MapHeight);
                raw.texture = geo;
                raw.raycastTarget = false;
                // 도시 이름: 멀리 볼수록 큰 도시만, 겹치면 큰 도시 먼저 (Natural Earth 로마자 이름)
                int maxRank = ViewMeters[Zoom] <= 20000 ? 10 : ViewMeters[Zoom] <= 60000 ? 8 : ViewMeters[Zoom] <= 200000 ? 6 : ViewMeters[Zoom] <= 600000 ? 4 : 2;
                var shownPlaces = new List<Vector2>();
                var places = new List<(float x, float y, int rank, string name)>(GeoBackdrop.Places);
                places.Sort((p, q) => p.rank.CompareTo(q.rank));
                foreach (var pl in places)
                {
                    if (pl.rank > maxRank || string.IsNullOrEmpty(pl.name)) continue;
                    var at = new Vector2((pl.x - 0.5f) * widthPx, (pl.y - 0.5f) * MapHeight);
                    bool crowded = false;
                    foreach (var o in shownPlaces) if (Mathf.Abs(o.x - at.x) < 150f && Mathf.Abs(o.y - at.y) < 34f) { crowded = true; break; }
                    if (crowded) continue;
                    shownPlaces.Add(at);
                    Disc(layer, at, 10, new Color(0.35f, 0.25f, 0.18f));
                    var name = Ui.Label(layer, pl.name, pl.rank <= 2 ? 22 : 18, TextAnchor.MiddleLeft, new Color(0.3f, 0.22f, 0.16f));
                    name.raycastTarget = false;
                    name.horizontalOverflow = HorizontalWrapMode.Overflow;
                    name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    name.rectTransform.pivot = new Vector2(0f, 0.5f);
                    name.rectTransform.sizeDelta = new Vector2(200, 30);
                    name.rectTransform.anchoredPosition = at + new Vector2(10, 0);
                }
            }
            // 칸 줄
            float step = GridMeters[Zoom] * ppm;
            var lineC = new Color(0.44f, 0.38f, 0.27f, Mode == 0 ? 0.16f : 0.28f);
            for (float x = -((widthPx / 2) - ((widthPx / 2) % step)); x <= widthPx / 2; x += step) Box(layer, new Vector2(x, 0), new Vector2(2, MapHeight), lineC);
            for (float y = -((MapHeight / 2) - ((MapHeight / 2) % step)); y <= MapHeight / 2; y += step) Box(layer, new Vector2(0, y), new Vector2(widthPx, 2), lineC);

            string myAlliance = MyAllianceId;
            if (myAlliance == null && Owners != null) foreach (var o in Owners.Values) if (o.OwnerAccountId == ctx.AccountId && o.OwnerAllianceId != null) myAlliance = o.OwnerAllianceId;
            Color Rel(TerritoryOwner o) => o.OwnerAccountId == ctx.AccountId ? SealRed : myAlliance != null && o.OwnerAllianceId == myAlliance ? AllyGreen : OtherRed;
            bool Shown(TerritoryOwner o) => o.OwnerAccountId == ctx.AccountId ? ShowMine : myAlliance != null && o.OwnerAllianceId == myAlliance ? ShowAlly : ShowOthers;

            if (Owners != null)
            {
                if (Mode == 0)
                {
                    foreach (var o in Owners.Values)
                    {
                        if (!Shown(o) || !FlagDefs.TryParseKey(o.RegionId, out var cell)) continue;
                        var c = Rel(o);
                        var p = Pos(cell);
                        Disc(layer, p, o.RadiusM * 2 * ppm, new Color(c.r, c.g, c.b, 0.22f));
                        Marker(ctx, layer, p, c, RankLetter(ctx, o.RadiusM), 44);
                        if (o.SiegeAttackerId != null) Chip(layer, p + new Vector2(0, -52), ctx.T("ui.siege.underAttack"), new Color(0.6f, 0.12f, 0.1f)); // 공성전 준비 중 (R-113)
                    }
                }
                else if (Mode == 1)
                {
                    // 칸(5km)마다 대표 깃발 하나 (병력 큰 순, 같으면 반지름 = 계급) + 나머지 수, 내 깃발은 늘
                    var groups = new Dictionary<string, List<TerritoryOwner>>();
                    foreach (var o in Owners.Values)
                    {
                        if (!Shown(o) || !FlagDefs.TryParseKey(o.RegionId, out var cell)) continue;
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
                        if (!Shown(o) || !FlagDefs.TryParseKey(o.RegionId, out var cell)) continue;
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
            // 내 깃발 위 수입 (곡식 · 엽전, 누르면 그 깃발만 받기)
            if (Owners != null && ShowMine && Mode < 2)
                foreach (var o in Owners.Values)
                {
                    if (o.OwnerAccountId != ctx.AccountId || o.Pending == null || !FlagDefs.TryParseKey(o.RegionId, out var ic)) continue;
                    var parts = new List<string>();
                    foreach (var kv in o.Pending) if (kv.Value > 0) parts.Add(ctx.T("resource." + kv.Key) + " " + Ui.Compact(kv.Value));
                    if (parts.Count == 0) continue;
                    string key = o.RegionId;
                    var income = Ui.Button(layer, string.Join(" · ", parts), () => CollectOne(app, root, key), 44, new Color(0.98f, 0.93f, 0.7f), 20);
                    var irt = (RectTransform)income.transform;
                    irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                    irt.sizeDelta = new Vector2(30 + 22 * string.Join(" · ", parts).Length * 0.6f, 44);
                    irt.anchoredPosition = Pos(ic) + new Vector2(0, 56);
                }
            // 내 위치 · 꽂을 수 있는 범위
            var mePos = Pos(here);
            // 위치를 모르면 내 점 · 꽂을 범위는 그리지 않는다 (기본 위치는 지도를 보여 주려는 것뿐)
            if (Last != null && Last.Ok)
            {
                if (Mode < 2) Disc(layer, mePos, myRadius * 2 * ppm, new Color(MeBlue.r, MeBlue.g, MeBlue.b, 0.10f));
                Disc(layer, mePos, 26, MeBlue);
            }
            // 손가락으로: 한 손가락 끌기 = 지도 옮기기, 두 손가락 (휠) = 확대 · 축소 (2026-11-15 사용자)
            var gestures = map.gameObject.AddComponent<GestureArea>();
            gestures.Tapped += screen =>
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, null, out var lp)) return;
                // 내 깃발을 눌렀나 (가까이 볼 때 깃발 표식 둘레)
                var tapPoint = new GeoPoint(
                    center.LatE6 + (long)(lp.y / ppm * GeoPoint.Scale / (double)flags.MetersPerDegreeLat),
                    center.LonE6 + (long)(lp.x / ppm * GeoPoint.Scale / (double)flags.MetersPerDegreeLon));
                if (Owners != null && Mode < 2)
                {
                    // 깃발 표식을 눌렀나 (남의 깃발 포함)
                    foreach (var o in Owners.Values)
                        if (Shown(o) && FlagDefs.TryParseKey(o.RegionId, out var fc) && (Pos(fc) - lp).sqrMagnitude <= 44f * 44f)
                        {
                            if (o.OwnerAccountId == ctx.AccountId) OpenFlagSheet(app, root, o, Last != null && Last.Ok ? (GeoPoint?)tapPoint : null);
                            else OpenOtherFlagSheet(app, root, o, here, myAlliance != null && o.OwnerAllianceId == myAlliance);
                            return;
                        }
                    // 내 깃발 원(땅) 안을 눌렀나 → 가장 가까운 내 깃발 판 (그 자리에 깃발 더 꽂기도, 2026-11-15 사용자)
                    TerritoryOwner mine = null; float best = float.MaxValue;
                    foreach (var o in Owners.Values)
                    {
                        if (o.OwnerAccountId != ctx.AccountId || !ShowMine || !FlagDefs.TryParseKey(o.RegionId, out var mc)) continue;
                        float d2 = (Pos(mc) - lp).sqrMagnitude, r = o.RadiusM * ppm;
                        if (d2 <= r * r && d2 < best) { best = d2; mine = o; }
                    }
                    if (mine != null) { OpenFlagSheet(app, root, mine, Last != null && Last.Ok ? (GeoPoint?)tapPoint : null); return; }
                }
                if (Last == null || !Last.Ok)
                {
                    // 아직 위치를 모름: 동의 · 확인 → 내 위치로 지도를 옮기고 다시 누르게
                    WithLocation(app, root, ok => { Notice = ok ? ctx.T("ui.terr.located") : ctx.T("ui.territory.failed", Last?.Reason ?? "?"); if (root != null) app.ShowMain("territory"); });
                    return;
                }
                var target = tapPoint;
                if (!flags.Within(here, flags.CellOf(target), myRadius)) { Toast(map, ctx.T("ui.terr.plant.outOfRange")); return; }
                OpenPlantSheet(app, root, here, target, myRadius);
            };
            gestures.Dragged += (a, b) =>
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(map, a, null, out var la) && RectTransformUtility.ScreenPointToLocalPointInRectangle(map, b, null, out var lb))
                    layer.anchoredPosition += lb - la;
            };
            gestures.DragEnded += () =>
            {
                if (layer == null || layer.anchoredPosition.sqrMagnitude < 4f) return;
                PanM -= layer.anchoredPosition / ppm / layer.localScale.x;
                app.ShowMain("territory");
            };
            gestures.Zoomed += f =>
            {
                if (layer == null) return;
                _pinch *= f;
                layer.localScale = Vector3.one * Mathf.Clamp(_pinch, 0.5f, 2f);
                int to = _pinch >= 1.6f ? Zoom - 1 : _pinch <= 0.62f ? Zoom + 1 : Zoom;
                to = Mathf.Clamp(to, 0, ViewMeters.Length - 1);
                if (to == Zoom) { if (_pinch >= 1.6f || _pinch <= 0.62f) { _pinch = 1f; layer.localScale = Vector3.one; } return; }
                Zoom = to;
                _pinch = 1f;
                app.ShowMain("territory");
            };
            _pinch = 1f;
            if (PanM != Vector2.zero)
            {
                // 옮긴 지도를 내 위치로 되돌리기
                var back = Ui.Button(map, ctx.T("ui.terr.recenter"), () => { PanM = Vector2.zero; app.ShowMain("territory"); }, 56, new Color(0.98f, 0.96f, 0.93f), 22);
                var bt = (RectTransform)back.transform;
                bt.anchorMin = bt.anchorMax = bt.pivot = new Vector2(1f, 0f);
                bt.sizeDelta = new Vector2(170, 56);
                bt.anchoredPosition = new Vector2(-14, 14);
            }
            // 좌상단: 보일 깃발 체크 (주변 부대 · 내 깃발 · 연합)
            var filters = Ui.Rect(map, "Filters");
            filters.anchorMin = filters.anchorMax = filters.pivot = new Vector2(0f, 1f);
            filters.anchoredPosition = new Vector2(12, -12);
            filters.sizeDelta = new Vector2(196, 3 * 50 + 2 * 6);
            Ui.Column(filters, 6, 0);
            void Filter(string label, bool on, Color col, System.Action toggle)
            {
                var b = Ui.Button(filters, (on ? "☑ " : "☐ ") + label, () => { toggle(); app.ShowMain("territory"); }, 50, on ? new Color(0.98f, 0.96f, 0.93f) : new Color(0.98f, 0.96f, 0.93f, 0.7f), 20);
                var t = b.GetComponentInChildren<Text>();
                t.color = on ? col : Palette.TextDim;
                t.alignment = TextAnchor.MiddleLeft;
            }
            Filter(ctx.T("ui.terr.tab.near"), ShowOthers, OtherRed, () => ShowOthers = !ShowOthers);
            Filter(ctx.T("ui.terr.tab.mine"), ShowMine, SealRed, () => ShowMine = !ShowMine);
            if (myAlliance != null) Filter(ctx.T("ui.terr.tab.ally"), ShowAlly, AllyGreen, () => ShowAlly = !ShowAlly);
            else
            {
                // 연합이 없으면 연합 체크 대신 [연합 가입] (연합 화면)
                var join = Ui.Button(filters, ctx.T("ui.terr.joinAlliance"), () => { AllianceChecked = false; app.Show(new AllianceScreen()); }, 50, new Color(0.24f, 0.48f, 0.25f), 20);
                join.GetComponentInChildren<Text>().color = Color.white;
            }
            if (ctx.Online is LocalOnlineService fakeSeed && Last != null && Last.Ok)
            {
                filters.sizeDelta = new Vector2(196, 4 * 50 + 3 * 6);
                Ui.Button(filters, ctx.T("ui.terr.seed"), () => { SeedNearby(ctx, fakeSeed, center); app.ShowMain("territory"); }, 50, fontSize: 18);
            }
            // 줌 버튼 · 줌 칩
            var zoomCol = Ui.Rect(map, "Zoom");
            zoomCol.anchorMin = zoomCol.anchorMax = new Vector2(1, 0.5f);
            zoomCol.pivot = new Vector2(1, 0.5f);
            zoomCol.anchoredPosition = new Vector2(-14, 0);
            zoomCol.sizeDelta = new Vector2(70, 160);
            Ui.Column(zoomCol, 12, 0);
            Ui.SetEnabled(Ui.Button(zoomCol, "+", () => { Zoom = Mathf.Max(0, Zoom - 1); app.ShowMain("territory"); }, 70, fontSize: 34), Zoom > 0);
            Ui.SetEnabled(Ui.Button(zoomCol, "−", () => { Zoom = Mathf.Min(ViewMeters.Length - 1, Zoom + 1); app.ShowMain("territory"); }, 70, fontSize: 34), Zoom < ViewMeters.Length - 1);
            var chip = Chip(map, Vector2.zero, ctx.T("ui.terr.zoom." + Mode, ViewMeters[Zoom] / 1000) + " · " + ctx.T("ui.terr.rankRadius", ctx.T("rank." + ctx.Defs.Campaign.RankFor(ctx.State.LordLevel).Id), FormatKm(myRadius)), new Color(0.98f, 0.96f, 0.93f), Palette.Text);
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0, 0);
            chip.anchoredPosition = new Vector2(14, 14);



            if (Notice != null) { Toast(map, Notice); Notice = null; }
            if (!ctx.OnlineEnabled) { Toast(map, ctx.T("ui.territory.needOnline")); return; }
            string loc = Last != null && Last.Ok ? Last.Point.ToString() : null;

            // 지도가 전부 (2026-11-15 사용자): 파란 원 안 = 깃발 꽂기, 내 깃발 · 그 땅 = 장수 회수 · 수입 · 더 꽂기, 남의 깃발 = 정보 · 공격. 아래 칸 없음 (알림은 토스트)
            if (Owners == null) Toast(map, ctx.T(loc != null ? "ui.terr.tapHint" : "ui.terr.tapHintNoLoc"));
            ctx.LoadTerritory(res =>
            {
                if (map == null) return;
                if (res == null) { Toast(map, ctx.T("ui.hire.failed")); return; }
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
            });
        }

        /// <summary>
        /// 깃발을 꽂고 · 보내고 · 회수하고 · 수입을 받은 뒤: 장부를 다시 받아 놓고 그린다 (2026-11-16: 받은 뒤에도 옛 수입 표가 남던 것).
        /// 화면은 이미 받아 둔 Owners로 먼저 그리고 새 목록은 처음에만 다시 그리므로, 바뀐 뒤에는 먼저 받아 와야 한다.
        /// </summary>
        private static void Reload(GameApp app, RectTransform root)
        {
            app.Context.LoadTerritory(res =>
            {
                if (res != null)
                {
                    var owners = new Dictionary<string, TerritoryOwner>();
                    foreach (var o in res.Owners) owners[o.RegionId] = o;
                    Owners = owners;
                }
                if (root != null) app.ShowMain("territory");
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

        /// <summary>깃발에 나간 장수 이름들 (R-109, 여럿이면 " · "로).</summary>
        private static string GeneralNames(GameContext ctx, TerritoryOwner o) => string.Join(" · ", TerritoryFlags.GeneralsOf(o).ConvertAll(id => ctx.GeneralName(id)));

        /// <summary>꽂기 판에서 고른 장수들 (판을 다시 그려도 유지).</summary>
        private static readonly List<string> _plantPick = new List<string>();

        /// <summary>
        /// 지도에서 누른 자리에 깃발 꽂기 (R-96 · R-109): 기본 편성의 장수마다 체크 줄 (다른 깃발에 나간 · 출전 못 하는 장수는 못 고름), 1명 이상 → [깃발 꽂기].
        /// </summary>
        /// <param name="reinforceKey">있으면 새 깃발이 아니라 이 내 깃발에 장수 추가 파견 (R-115, 위치 필요 없음).</param>
        private static void OpenPlantSheet(GameApp app, RectTransform root, GeoPoint here, GeoPoint target, long radius, string reinforceKey = null)
        {
            var ctx = app.Context;
            var comps = ctx.State.CompositionsFor(null);
            var busyIds = new List<string>();
            var hereIds = new List<string>(); // 추가 파견: 이 깃발에 이미 나간 장수 (체크된 채 꺼짐, 회수는 깃발 판에서 — H6)
            if (Owners != null)
                foreach (var kv in Owners)
                    if (kv.Value.OwnerAccountId == ctx.AccountId)
                        (reinforceKey != null && (kv.Key == reinforceKey || kv.Value.RegionId == reinforceKey) ? hereIds : busyIds).AddRange(TerritoryFlags.GeneralsOf(kv.Value));
            _plantPick.RemoveAll(id => busyIds.Contains(id) || hereIds.Contains(id) || !comps.Exists(c => c.GeneralId == id));
            bool reinforce = reinforceKey != null;
            var (content, close) = Ui.BottomSheet(root, ctx.T(reinforce ? "ui.terr.reinforceTitle" : "ui.terr.plantHere"), 220 + 100 * Mathf.Max(1, comps.Count) + 120);
            Ui.Column(content, 6, 0);
            Ui.Label(content, reinforce ? ctx.T("ui.terr.reinforceNote") : ctx.T("ui.terr.plantHereNote", FormatKm(radius)), 22, TextAnchor.MiddleLeft, Palette.TextDim);
            foreach (var c in comps)
            {
                string gid = c.GeneralId;
                var g = ctx.State.FindGeneral(gid);
                bool here = hereIds.Contains(gid);
                bool elsewhere = !here && busyIds.Contains(gid);
                bool cant = !here && !elsewhere && (g == null || !Game.Campaign.Roster.GeneralStatus.CanDeploy(g.Status));
                bool busy = here || elsewhere || cant;
                long troops = 0;
                foreach (var p in c.Picks) troops += p.Troops;
                string note = here ? ctx.T("ui.terr.onThisFlag") : elsewhere ? ctx.T(reinforce ? "ui.terr.onOtherFlag" : "ui.terr.onFlag") : cant ? ctx.T("ui.terr.cantDeploy") : ctx.T("ui.terr.troops", troops.ToString("N0"));
                string label = ctx.GeneralName(gid) + "  " + Ui.Colored(note, Palette.TextDim);
                var row = Ui.CheckRow(content, label, here || _plantPick.Contains(gid), busy ? (System.Action)null : () =>
                {
                    if (_plantPick.Contains(gid)) _plantPick.Remove(gid); else _plantPick.Add(gid);
                    close();
                    OpenPlantSheet(app, root, here, target, radius, reinforceKey);
                });
                if (busy) row.interactable = false;
            }
            var go = Ui.Button(content, ctx.T(reinforce ? "ui.terr.reinforceGo" : "ui.terr.plantGo", _plantPick.Count), () =>
            {
                var req = reinforce ? new PlantRequest { FlagKey = reinforceKey, GeneralIds = new List<string>(_plantPick) }
                    : new PlantRequest { Location = here.ToString(), Target = target.ToString(), GeneralIds = new List<string>(_plantPick) };
                close();
                ctx.PlantFlag(req, r =>
                {
                    if (r != null && r.Ok) _plantPick.Clear();
                    Notice = r == null ? ctx.T("ui.hire.failed") : reinforce && r.Ok ? ctx.T("ui.terr.reinforced") : ctx.T("ui.terr.plant." + r.Reason);
                    NamesFetched = false;
                    Reload(app, root); // 바뀐 깃발 · 수입을 다시 받아 그린다
                });
            }, 84, Palette.ButtonActive, 28);
            Ui.SetEnabled(go, _plantPick.Count > 0);
        }

        /// <summary>내 깃발 판 (R-109): 나간 장수마다 [회수], [모두 회수] = 깃발 거두기 (확인, 쌓인 수입은 우편).</summary>
        private static void OpenFlagSheet(GameApp app, RectTransform root, TerritoryOwner flag, GeoPoint? plantAt = null)
        {
            var ctx = app.Context;
            var ids = TerritoryFlags.GeneralsOf(flag);
            var fd = ctx.Territory?.Flags;
            long radius = TerritoryFlags.RadiusOf(ctx.Defs, ctx.State);
            bool canPlant = plantAt != null && Last != null && Last.Ok && fd != null && fd.Within(Last.Point, fd.CellOf(plantAt.Value), radius) && FlagDefs.KeyOf(fd.CellOf(plantAt.Value)) != flag.RegionId;
            var (content, close) = Ui.BottomSheet(root, ctx.T("ui.terr.flagSheet", ids.Count), 420 + 96 * ids.Count + 120);
            Ui.Column(content, 8, 0);
            // 쌓인 수입 · [수입 받기]
            var inc = Ui.Row(content, "Income", 76, 12);
            Ui.Size(Ui.Label(inc, Pending(ctx, flag), 24, TextAnchor.MiddleLeft, Palette.Good), flexibleWidth: 1);
            string flagKey = flag.RegionId;
            Ui.Size(Ui.Button(inc, ctx.T("ui.terr.collectOne"), () => { close(); CollectOne(app, root, flagKey); }, 72, Palette.ButtonActive, 24), 72, 190).flexibleWidth = 0;
            void Recall(List<string> which)
            {
                close();
                ctx.RecallFlag(new RecallRequest { FlagKey = flag.RegionId, GeneralIds = which }, r =>
                {
                    NamesFetched = false;
                    if (r == null || !r.Ok)
                    {
                        Notice = r == null ? ctx.T("ui.hire.failed") : ctx.T("ui.terr.recall." + r.Reason);
                        Reload(app, root); // 바뀐 깃발 · 수입을 다시 받아 그린다
                        return;
                    }
                    // 거두며 나온 수입 우편은 바로 받는다 (2026-11-16 사용자: 수입은 우편함을 거치지 않고)
                    string flagKey = flag.RegionId;
                    ctx.ClaimMailWhere(m => m.Kind == MailKinds.TerritoryIncome && m.RegionId == flagKey, sum =>
                    {
                        string got = sum == null ? null : MailScreen.Amounts(ctx, sum);
                        Notice = r.Flag == null ? (got != null ? ctx.T("ui.terr.recall.removedGot", got) : ctx.T("ui.terr.recall.removed")) : ctx.T("ui.terr.recall.ok");
                        Reload(app, root); // 바뀐 깃발 · 수입을 다시 받아 그린다
                    });
                });
            }
            foreach (var gid in ids)
            {
                string id = gid;
                var row = Ui.Row(content, "General", 84, 12);
                Ui.Size(Ui.Label(row, ctx.GeneralName(id), 28, TextAnchor.MiddleLeft), flexibleWidth: 1);
                Ui.Size(Ui.SecondaryButton(row, ctx.T("ui.terr.recall"), () => Recall(new List<string> { id }), 72, 24), 72, 170).flexibleWidth = 0;
            }
            // 장수 추가 파견 (R-115): 추가 파견 뒤 회수하면 장수 바꾸기
            {
                string key = flag.RegionId;
                var here0 = Last != null && Last.Ok ? Last.Point : default;
                var add = Ui.Button(content, ctx.T("ui.terr.reinforce"), () => { close(); OpenPlantSheet(app, root, here0, here0, radius, key); }, 72, fontSize: 24);
                Ui.SetEnabled(add, flag.SiegeAttackerId == null);
            }
            if (canPlant)
            {
                // 내 땅 안의 다른 자리에 깃발 더 꽂기 (내 원과는 겹쳐도 된다, R-96)
                var at = plantAt.Value; var me = Last.Point;
                Ui.Button(content, ctx.T("ui.terr.plantMore"), () => { close(); OpenPlantSheet(app, root, me, at, radius); }, 72, fontSize: 24);
            }
            Ui.Button(content, ctx.T("ui.terr.recallAll"), () =>
            {
                close();
                Ui.Confirm(root, ctx.T("ui.terr.recallAll"), ctx.T("ui.terr.recallConfirm"), ctx.T("ui.terr.recallAll"), () => Recall(null), (ctx.T("ui.common.cancel"), () => { }));
            }, 80, Palette.Bad, 26);
        }

        /// <summary>
        /// 위치가 필요할 때만 (깃발 꽂기 · 공격): 이미 알면 바로, 아니면 처음 한 번 동의를 묻고 위치를 확인한다. 확인하면 지도를 내 위치로.
        /// then(ok) — ok면 Last에 위치가 있다.
        /// </summary>
        private static void WithLocation(GameApp app, RectTransform root, System.Action<bool> then)
        {
            var ctx = app.Context;
            if (Last != null && Last.Ok) { then(true); return; }
            void Ask()
            {
                ctx.Location.Request(r =>
                {
                    Last = r;
                    NamesFetched = false;
                    PanM = Vector2.zero;
                    then(r != null && r.Ok);
                });
            }
            if (ctx.LocationConsented) { Ask(); return; }
            Ui.Confirm(root, ctx.T("ui.terr.consent.title"), ctx.T("ui.terr.consent.body"), ctx.T("ui.terr.consent.agree"), () => { ctx.SetLocationConsent(true); Ask(); },
                (ctx.T("ui.common.cancel"), () => { }));
        }

        /// <summary>지도 아래쪽에 잠깐 뜨는 알림 (3초 뒤 사라짐). 아래 칸 대신.</summary>
        private static void Toast(RectTransform map, string text)
        {
            if (map == null || string.IsNullOrEmpty(text)) return;
            var t = Ui.Rect(map, "Toast");
            t.anchorMin = new Vector2(0.08f, 0f);
            t.anchorMax = new Vector2(0.92f, 0f);
            t.pivot = new Vector2(0.5f, 0f);
            t.anchoredPosition = new Vector2(0, 80);
            t.sizeDelta = new Vector2(0, 76);
            var bg = Ui.Background(t, new Color(0.16f, 0.13f, 0.1f, 0.86f));
            bg.sprite = UiShapes.Rounded(14);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            var l = Ui.Label(t, text, 22, TextAnchor.MiddleCenter, Color.white);
            l.raycastTarget = false;
            Ui.Fill(l.rectTransform);
            l.rectTransform.offsetMin = new Vector2(16, 4);
            l.rectTransform.offsetMax = new Vector2(-16, -4);
            Object.Destroy(t.gameObject, 3f);
        }

        /// <summary>
        /// 깃발 하나 수입 받기 → 바로 자원에 (2026-11-16 사용자: 깃발 위 수입 · [수입 받기]를 누르면 받아지게).
        /// 서버가 수입을 우편으로 만들고(R-47), 기기가 그 우편을 곧바로 받는다 (mail.claim, 서버가 같은 규칙으로 확인). 영토 화면에 남는다.
        /// </summary>
        private static void CollectOne(GameApp app, RectTransform root, string regionId)
        {
            var ctx = app.Context;
            ctx.CollectFlag(regionId, r =>
            {
                if (r == null) { Notice = ctx.T("ui.hire.failed"); if (root != null) app.ShowMain("territory"); return; }
                // 이 깃발의 수입 우편 (방금 것 + 전에 남은 것)을 우편함에서 찾아 바로 받는다
                ctx.ClaimMailWhere(m => m.Kind == MailKinds.TerritoryIncome && m.RegionId == regionId, sum =>
                {
                    string got = sum == null ? null : MailScreen.Amounts(ctx, sum);
                    Notice = sum == null ? ctx.T("ui.hire.failed") : got == null ? ctx.T("ui.territory.nothing") : ctx.T("ui.territory.got", got);
                    Reload(app, root); // 바뀐 깃발 · 수입을 다시 받아 그린다
                });
            });
        }

        /// <summary>남의 깃발 판: 주인 · 연합 · 장수 · 병력 · 거리 · 보호, [공격] (연합이 아니고 공성전 검사를 통과하면).</summary>
        private static void OpenOtherFlagSheet(GameApp app, RectTransform root, TerritoryOwner o, GeoPoint here, bool ally)
        {
            var ctx = app.Context;
            long now = ctx.Clock.UtcNow.ToUnixTimeSeconds();
            string owner = ctx.DisplayName(o.OwnerAccountId) + (o.OwnerAllianceName != null ? "  [" + o.OwnerAllianceName + "]" : "");
            var (content, close) = Ui.BottomSheet(root, owner, 420);
            Ui.Column(content, 8, 0);
            long d = FlagDefs.TryParseKey(o.RegionId, out var cell) ? (long)Mathf.Sqrt(ctx.Territory.Flags.DistanceSquaredM(here, cell)) : 0;
            Ui.Label(content, ctx.T("ui.terr.flagLine", GeneralNames(ctx, o), Troops(o).ToString("N0"), FormatKm(d)), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            string loc = Last != null && Last.Ok ? Last.Point.ToString() : null;
            // 위치를 모르면 [공격]을 누를 때 동의 · 확인한 뒤 검사 (위치는 공격 · 꽂기 때만)
            string err = ally ? ctx.T("ui.terr.ally") : loc == null ? null
                : TerritoryFlags.CheckSiege(ctx.Territory, ctx.Defs, ctx.State, ctx.AccountId, Owners, o.RegionId, loc, now);
            if (err == null)
            {
                string key = o.RegionId; var def = o.Defense;
                Ui.Button(content, ctx.T("ui.terr.attack"), () =>
                {
                    close();
                    WithLocation(app, root, ok =>
                    {
                        string at = ok ? Last.Point.ToString() : null;
                        string why = !ok ? ctx.T("ui.territory.failed", Last?.Reason ?? "?")
                            : TerritoryFlags.CheckSiege(ctx.Territory, ctx.Defs, ctx.State, ctx.AccountId, Owners, key, at, ctx.Clock.UtcNow.ToUnixTimeSeconds());
                        if (why != null) { Notice = why; if (root != null) app.ShowMain("territory"); return; }
                        // 공성전 준비 (R-113): 서버에 준비 시작 → 준비 시간이 있으면 카운트다운 화면, 없으면 바로 출진
                        ctx.StartSiege(new SiegeRequest { FlagKey = key, Location = at }, sr =>
                        {
                            if (sr == null) { Notice = ctx.T("ui.hire.failed"); if (root != null) app.ShowMain("territory"); return; }
                            if (!sr.Ok) { Notice = sr.Reason == SiegeReasons.Busy ? ctx.T("ui.siege.busy") : sr.Message ?? ctx.T("ui.siege.cannot"); if (root != null) app.ShowMain("territory"); return; }
                            if (sr.StartsAtUnixSeconds > ctx.Clock.UtcNow.ToUnixTimeSeconds()) app.Show(new SiegePrepScreen(key, at, def, sr.StartsAtUnixSeconds));
                            else app.Show(new TerritorySortieScreen(key, at, def));
                        });
                    });
                }, 80, Palette.ButtonActive, 28);
            }
            else Ui.Label(content, o.ProtectedUntilUnixSeconds > now ? ctx.T("ui.terr.protected", (o.ProtectedUntilUnixSeconds - now + 59) / 60) : err, 24, TextAnchor.MiddleLeft, Palette.TextDim);
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
            // [연합] 탭 (공통 타이틀, 탭이라 뒤로 없음)
            var (header, list, _, _) = Ui.TitledPage(Root, T("ui.alliance.title"), null);
            InfoButton.Add(Root, T("ui.alliance.note"));
            if (_notice != null) Ui.Label(header, _notice, Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good);
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
    /// <summary>
    /// 공성전 준비 카운트다운 (R-113, territory-v2.md 2.3): "전투 준비 중 — n초 뒤 시작", [공격 취소] (CancelSiege).
    /// 0이 되면 출진 화면으로. 그동안 수비자에게는 공성전 알림 우편이 간다.
    /// </summary>
    public sealed class SiegePrepScreen : GameScreen
    {
        private readonly string _flagKey, _location;
        private readonly TerritoryDefense _defense;
        private readonly long _startsAt;
        private Text _count;
        private bool _done;

        public SiegePrepScreen(string flagKey, string location, TerritoryDefense defense, long startsAtUnixSeconds)
        {
            _flagKey = flagKey; _location = location; _defense = defense; _startsAt = startsAtUnixSeconds;
        }

        public override void Build()
        {
            var (_, body, _, _) = Ui.TitledPage(Root, T("ui.siege.title"), null, infoGap: false);
            Ui.Column(body, 24, 40);
            Ui.Label(body, T("ui.siege.prep"), 34, TextAnchor.MiddleCenter);
            _count = Ui.Label(body, "", 120, TextAnchor.MiddleCenter, Palette.Seal);
            Ui.Size(_count, 200);
            Ui.Label(body, T("ui.siege.prepNote"), 24, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Button(body, T("ui.siege.cancel"), Cancel, 96, fontSize: 30);
            Tick(0f);
        }

        public override bool OnBack() { Cancel(); return true; }

        private void Cancel()
        {
            if (_done) return;
            _done = true;
            Ctx.CancelSiege(_flagKey, _ =>
            {
                TerritoryTabView.Notice = T("ui.siege.cancelled");
                App.ShowMain("territory");
            });
        }

        public override void Tick(float deltaTime)
        {
            if (_done || _count == null) return;
            long left = _startsAt - Ctx.Clock.UtcNow.ToUnixTimeSeconds();
            if (left <= 0)
            {
                _done = true;
                App.Show(new TerritorySortieScreen(_flagKey, _location, _defense));
                return;
            }
            _count.text = left.ToString();
        }
    }

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
            var (header, body, footer) = Ui.PageLayout(Root, 130);
            string owner = null;
            if (TerritoryTabView.Owners != null && TerritoryTabView.Owners.TryGetValue(_regionId, out var flag)) owner = flag.OwnerAccountId;
            Ui.TitleBar(header, owner != null ? T("ui.sortie.siegeOf", Ctx.DisplayName(owner)) : T(step.NameKey), () => App.ShowMain("territory"));
            SortiePrep.Tiles(Ctx, header, step);
            InfoButton.Add(Root, T("ui.sortie.siegeNote"));

            var plan = Ctx.Flow.Prepare(Ctx.State, Request(false));
            var defense = Ui.Card(body, "Defense");
            SortiePrep.Info(Ctx, defense, "ui.sortie.info.defense");
            Ui.Label(defense, T(_defense != null ? "ui.territory.defenseOwnerTitle" : "ui.territory.defense"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            if (plan.Setup != null) SortiePrep.Enemies(Ctx, defense, plan);
            // 수비 지원 (겹친 상대 깃발 = 전장 하루 뒤 · 연합원 = 늦게, territory-v2.md 2.5 — 지원 입장은 다음 단계)
            int overlap = 0;
            if (owner != null && TerritoryTabView.Owners != null)
                foreach (var o in TerritoryTabView.Owners.Values) if (o.OwnerAccountId == owner && o.RegionId != _regionId) overlap++;
            Ui.Label(defense, Ui.Colored(T("ui.sortie.support", overlap), Palette.Bad), 22);
            if (plan.Setup != null) SortiePrep.Power(Ctx, body, SortiePrep.PlayerTroops(plan), SortiePrep.EnemyTroops(plan));

            Ui.Label(body, T("ui.step.allies"), Ui.FontSmall);
            SortiePrep.Army(Ctx, body, plan);
            var armyRow = Ui.Row(body, "ArmyButtons", 72, 10);
            Ui.Button(armyRow, T("ui.sortie.formation"), () => App.ShowMain("formation"), 72, fontSize: 24);
            if (Ctx.HiringAvailable) Ui.Button(armyRow, T("ui.sortie.mercenary"), () => App.ShowMain("hiring"), 72, fontSize: 24);

            var sortie = Ui.Card(body, "Sortie");
            SortiePrep.Info(Ctx, sortie, "ui.sortie.info.supply");
            if (_hired != null) Ui.Label(sortie, T("ui.merc.inSortie", _hired.Generals.Count + _hired.Units.Count), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.Good);
            // 반간 (R-99, hiring-v2.md 4장): 내 닉네임 장수가 반간을 알면, 이 깃발 주인의 용병이 섞여 있을 때만 낌새를 알린다
            var me = Ctx.State.FindGeneral(AvatarGeneral.GeneralId);
            if (_hired != null && _defense?.OwnerAccountId != null && me != null && me.LearnedSkills.Contains("counterIntrigue") && _hired.GeneralOwners.ContainsValue(_defense.OwnerAccountId))
                Ui.Label(sortie, Ui.Colored(T("ui.merc.hint"), Palette.Bad), Ui.FontSmall);
            Ui.Label(sortie, T("ui.sortie.supply", plan.SupplyTaken.ToString("N0"), Ctx.Resource(ResourceIds.Supply).ToString("N0")), Ui.FontSmall);
            foreach (var err in plan.Errors) Ui.Label(sortie, Ui.Colored(err, Palette.Bad), Ui.FontSmall);
            var result = Ui.Row(body, "Result", 64, 10);
            Ui.Tag(result, T("ui.sortie.win", Ctx.Territory.Flags.IncomePerHour.TryGetValue(ResourceIds.Supply, out var inc) ? inc * 24 : 0), Palette.Good, 22, 56);
            Ui.Tag(result, T("ui.sortie.lose", Ctx.Territory.Flags.ProtectSeconds / 60), Palette.Bad, 22, 56);

            var delegated = Ui.Button(footer, T("ui.sortie.delegate"), () => Sortie(step, true), 100);
            Ui.Size(delegated, width: 240);
            var direct = Ui.PrimaryButton(footer, T("ui.sortie.attack"), () => Sortie(step, false), 100);
            Ui.Size(direct, flexibleWidth: 1);
            Ui.SetEnabled(direct, plan.Ok && Ctx.OnlineEnabled);
            Ui.SetEnabled(delegated, plan.Ok && Ctx.OnlineEnabled);
        }

        private HiredForces _hired;
        private List<Game.Campaign.Roster.CorpsComposition> _comps;

        private SortieRequest Request(bool delegate_)
        {
            // 용병 탭에서 담은 고용도 이 공성전에 (09)
            var step = Ctx.Flow.LoadStep(StepId);
            var (comps, hired) = Ctx.HiresFor(StepId).Build(Ctx.State, step.BattleType, Ctx.AccountId, out _);
            _hired = hired; _comps = hired == null ? null : comps;
            return new SortieRequest { StepId = StepId, Seed = Ctx.NewSeed(), Delegate = delegate_, Location = _location, Defense = _defense, Hired = _hired, Compositions = _comps };
        }

        private void Sortie(StepDef step, bool delegate_)
        {
            Ctx.Flow.Tick(Ctx.State); // 출진 시각 기준 시간 경과 (서버도 같은 시각으로 맞춘다, R-61)
            var plan = Ctx.Flow.Prepare(Ctx.State, Request(delegate_));
            if (!Ctx.Flow.Confirm(Ctx.State, plan)) { App.Show(new TerritorySortieScreen(_regionId, _location, _defense)); return; }
            App.Show(new DialogueScreen(plan, "dialogue." + step.Id + ".start", !delegate_));
        }
    }

    /// <summary>
    /// 영토 지도 바탕 그림 (Natural Earth 타일, Game.Territory.GeoTiles): 보이는 범위를 텍스처로 그린다 — 바다 · 육지(짝홀 채우기) · 도시 지역 · 호수 · 강 · 도로 · 시도 경계 선 (세부 층은 한국 근처만).
    /// 가운데 · 축척이 같으면 다시 그리지 않는다. 그림만 (규칙과 무관), 지도 좌표는 영토 탭과 같은 평면 근사(위도 · 경도마다 미터).
    /// </summary>
    internal static class GeoBackdrop
    {
        private const int TexWidth = 512;
        // 육지는 예전 빈 지도 바탕(0.89, 0.85, 0.75)과 구분되게 조금 짙게, 바다 · 강은 수묵 청회색
        private static readonly Color32 Sea = new Color32(178, 199, 203, 255);
        private static readonly Color32 Land = new Color32(222, 210, 176, 255);
        private static readonly Color32 River = new Color32(120, 156, 170, 255);
        // 세부 층 (한국 근처, tools/naturalearth_detail.js): 도시 지역 · 시도 경계 · 도로
        private static readonly Color32 Urban = new Color32(207, 190, 154, 255);
        private static readonly Color32 Admin1 = new Color32(158, 120, 98, 255);
        private static readonly Color32 Road = new Color32(181, 102, 74, 255);
        private static readonly Color32 Road2 = new Color32(176, 150, 118, 255);
        private static string _key;

        /// <summary>마지막으로 그린 범위의 도시 이름: 그림 안 위치(0~1, 왼쪽 아래 기준) · 등급 · 이름.</summary>
        public static readonly List<(float x, float y, int rank, string name)> Places = new List<(float, float, int, string)>();
        private static Texture2D _tex;

        public static Texture2D Get(GameContext ctx, GeoPoint center, long viewMeters, float aspect, long mPerDegLat, long mPerDegLon)
        {
            string key = center.LatE6 + "," + center.LonE6 + "," + viewMeters;
            if (_key == key && _tex != null) return _tex;
            try
            {
                int w = TexWidth, h = Mathf.Max(8, Mathf.RoundToInt(TexWidth * aspect));
                double halfLonDeg = viewMeters / 2.0 / mPerDegLon, halfLatDeg = viewMeters * aspect / 2.0 / mPerDegLat;
                double cLon = center.LonE6 / 1e6, cLat = center.LatE6 / 1e6;
                double minLon = cLon - halfLonDeg, maxLon = cLon + halfLonDeg, minLat = cLat - halfLatDeg, maxLat = cLat + halfLatDeg;
                var tiles = GeoTiles.ForView(ctx.Data, (long)(minLat * 1e6), (long)(minLon * 1e6), (long)(maxLat * 1e6), (long)(maxLon * 1e6));
                var px = new Color32[w * h];
                for (int k = 0; k < px.Length; k++) px[k] = Sea;
                // 1e-4도 → 그림 칸 (x 오른쪽, y 위)
                double sx = w / (maxLon - minLon) / GeoTiles.Scale, sy = h / (maxLat - minLat) / GeoTiles.Scale;
                double ox = minLon * GeoTiles.Scale, oy = minLat * GeoTiles.Scale;
                var land = new List<int[]>(); var lakes = new List<int[]>(); var rivers = new List<int[]>();
                var urban = new List<int[]>(); var admin1 = new List<int[]>(); var roads = new List<int[]>(); var roads2 = new List<int[]>();
                Places.Clear();
                foreach (var t in tiles)
                {
                    land.AddRange(t.Land); lakes.AddRange(t.Lake); rivers.AddRange(t.River);
                    urban.AddRange(t.Urban); admin1.AddRange(t.Admin1); roads.AddRange(t.Road); roads2.AddRange(t.Road2);
                    foreach (var p in t.Places)
                    {
                        float fx = (float)((p.Lon - ox) * sx / w), fy = (float)((p.Lat - oy) * sy / h);
                        if (fx >= 0f && fx <= 1f && fy >= 0f && fy <= 1f) Places.Add((fx, fy, p.Rank, p.Name));
                    }
                }
                Fill(px, w, h, land, Land, sx, sy, ox, oy);
                Fill(px, w, h, urban, Urban, sx, sy, ox, oy);
                Fill(px, w, h, lakes, Sea, sx, sy, ox, oy);
                foreach (var r in rivers) Line(px, w, h, r, River, sx, sy, ox, oy, 1);
                // 멀리 볼수록 덜 그린다: 작은 도로는 200km 이하, 큰 도로는 600km 이하
                if (viewMeters <= 200000) foreach (var r in roads2) Line(px, w, h, r, Road2, sx, sy, ox, oy, 0);
                foreach (var r in admin1) Line(px, w, h, r, Admin1, sx, sy, ox, oy, 0);
                if (viewMeters <= 600000) foreach (var r in roads) Line(px, w, h, r, Road, sx, sy, ox, oy, viewMeters <= 60000 ? 1 : 0);
                if (_tex == null) _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                else if (_tex.width != w || _tex.height != h) _tex.Reinitialize(w, h);
                _tex.SetPixels32(px);
                _tex.Apply(false);
                _key = key;
                return _tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("지도 바탕: " + e.Message);
                return null;
            }
        }

        /// <summary>고리들을 짝홀 규칙으로 칠한다 (줄마다 교차점).</summary>
        private static void Fill(Color32[] px, int w, int h, List<int[]> rings, Color32 c, double sx, double sy, double ox, double oy)
        {
            var edges = new List<(double x0, double y0, double x1, double y1)>();
            foreach (var r in rings)
            {
                int n = r.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double y0 = (r[2 * j + 1] - oy) * sy, y1 = (r[2 * i + 1] - oy) * sy;
                    if ((y0 < 0 && y1 < 0) || (y0 >= h && y1 >= h) || y0 == y1) continue;
                    edges.Add(((r[2 * j] - ox) * sx, y0, (r[2 * i] - ox) * sx, y1));
                }
            }
            if (edges.Count == 0) return;
            var xs = new List<double>();
            for (int row = 0; row < h; row++)
            {
                double y = row + 0.5;
                xs.Clear();
                foreach (var e in edges)
                    if ((e.y0 > y) != (e.y1 > y)) xs.Add(e.x0 + (y - e.y0) * (e.x1 - e.x0) / (e.y1 - e.y0));
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int a = Mathf.Max(0, (int)System.Math.Ceiling(xs[k] - 0.5)), b = Mathf.Min(w - 1, (int)System.Math.Floor(xs[k + 1] - 0.5));
                    for (int x = a; x <= b; x++) px[row * w + x] = c;
                }
            }
        }

        /// <param name="radius">선 굵기 반지름 (0 = 한 칸, 1 = 3칸).</param>
        private static void Line(Color32[] px, int w, int h, int[] pts, Color32 c, double sx, double sy, double ox, double oy, int radius)
        {
            for (int i = 2; i + 1 < pts.Length; i += 2)
            {
                double x0 = (pts[i - 2] - ox) * sx, y0 = (pts[i - 1] - oy) * sy, x1 = (pts[i] - ox) * sx, y1 = (pts[i + 1] - oy) * sy;
                int steps = Mathf.Max(1, (int)System.Math.Ceiling(System.Math.Max(System.Math.Abs(x1 - x0), System.Math.Abs(y1 - y0))));
                if (steps > 4 * (w + h)) continue; // 화면 밖 먼 선분
                for (int k = 0; k <= steps; k++)
                {
                    int x = (int)(x0 + (x1 - x0) * k / steps), y = (int)(y0 + (y1 - y0) * k / steps);
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && yy >= 0 && xx < w && yy < h) px[yy * w + xx] = c;
                        }
                }
            }
        }
    }
}
