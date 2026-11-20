using System;
using System.Collections.Generic;
using Game.Flow;
using Game.Online;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 녹화 (14 시안, replay.md, R-54 · R-81 · R-101): 고정 머리([‹] · "녹화" · 저장 막대 n / 20 · ⓘ) + 탭 내 녹화 · 공략 (서버 꺼지면 공략 탭 없음).
    /// 내 녹화: 분류 칩 · 최근 (자동 5, [저장] · ▶) 가로 줄 · 저장한 녹화 카드 (미니 전장 · 승/패 · 시간 · 손실 % · 날짜 · 큰 ▶ · ⋯ = 공략 공유 · 영상 저장(나중) · 삭제).
    /// [재생]은 지금 규칙으로 한 번 돌려 결과가 같을 때만 연다 (다르면 재생 불가). 공략: 스텝 고르기 · 정렬 칩 · 카드 ▶.
    /// </summary>
    public sealed class ReplayListScreen : GameScreen
    {
        private static string _tab = "mine";
        /// <summary>캡처 도구: 처음 열 탭 (mine / shared).</summary>
        public static string Tab { set => _tab = value; }
        private static string _cat = "all";
        private static string _sharedStep;
        private static string _sort = "won";
        private readonly string _message;
        public ReplayListScreen(string message = null) { _message = message; }

        public override void Build()
        {
            var (header, body, _) = Ui.PageLayout(Root, 0);
            var top = Ui.TitleBar(header, T("ui.replay.head"), () => App.ShowMain());
            int n = Ctx.Replays.Items.Count, max = Ctx.ReplayRules.MaxSaved;
            var bar = Ui.Gauge(top, Palette.Ally, 24);
            Ui.Size(bar.parent, width: 240);
            Ui.SetGauge(bar, max <= 0 ? 0f : Mathf.Clamp01((float)n / max));
            Ui.Size(Ui.Label(top, n + " / " + max, 24, TextAnchor.MiddleRight), width: 110);
            InfoButton.Add(Root, T("ui.replay.note"));
            if (!Ctx.OnlineEnabled) _tab = "mine";
            if (Ctx.OnlineEnabled)
                Ui.SegmentTabs(header, new[] { ("mine", T("ui.replay.tab.mine")), ("shared", T("ui.replay.tab.shared")) }, _tab, id => { _tab = id; App.Show(new ReplayListScreen()); }, 70, 500);
            if (_message != null) Ui.Label(header, _message, 24, TextAnchor.MiddleLeft, Palette.Good);

            if (_tab == "shared") BuildShared(header, body);
            else BuildMine(header, body);
        }

        // ---- 내 녹화 ----
        private void BuildMine(RectTransform header, RectTransform body)
        {
            var cats = new List<(string, string)> { ("all", T("ui.codex.cat.all")), ("story", T("ui.replay.cat.story")), ("territory", T("ui.replay.cat.territory")), ("merc", T("ui.replay.cat.merc")) };
            if (Ctx.OnlineEnabled) cats.Add(("hired", T("ui.replay.cat.hired"))); // 내 용병이 고용된 전투 (R-120, 서버에 있는 녹화)
            else if (_cat == "hired") _cat = "all";
            Ui.SegmentTabs(header, cats, _cat, id => { _cat = id; App.Show(new ReplayListScreen()); }, 60, 170);
            if (_cat == "hired") { BuildHired(body); return; }
            bool Match(ReplayRecord r) => _cat == "all" || ReplayService.CategoryOf(r) == _cat;
            var recent = Ctx.Replays.Recent.FindAll(Match);
            var saved = Ctx.Replays.Items.FindAll(Match);
            if (recent.Count == 0 && saved.Count == 0)
            {
                var empty = Ui.Card(body, "Empty", 40, 12);
                empty.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
                Ui.Label(empty, "卷", 90, TextAnchor.MiddleCenter, Palette.TextDim).font = Ui.TitleFont;
                Ui.Label(empty, T("ui.replay.emptyHead"), Ui.FontBody, TextAnchor.MiddleCenter);
                Ui.Label(empty, T("ui.replay.emptyFlow"), 24, TextAnchor.MiddleCenter, Palette.TextDim);
                Ui.Label(empty, T("ui.replay.emptyRecent", Ctx.ReplayRules.RecentKeep), 22, TextAnchor.MiddleCenter, Palette.Good);
                return;
            }
            if (recent.Count > 0)
            {
                Ui.Label(body, T("ui.replay.recent", Ctx.ReplayRules.RecentKeep), 24, TextAnchor.MiddleLeft, Palette.TextDim);
                var row = Ui.HScrollRow(body, "Recent", 250, 12, 4);
                foreach (var r in recent)
                {
                    var rec = r;
                    var card = Ui.Card(row, rec.Id, 12, 4);
                    Ui.Size(card, 250, 330);
                    Mini(card, rec, 70);
                    Ui.Label(card, StepName(rec.StepId), 22);
                    var line = Ui.Row(card, "Line", 60, 8);
                    WinTag(line, rec);
                    Ui.Button(line, T("ui.replay.keep"), () => { Ctx.KeepReplay(rec.Id); App.Show(new ReplayListScreen(T("ui.replay.saved", Ctx.Replays.Items.Count, Ctx.ReplayRules.MaxSaved))); }, 56, fontSize: 22);
                    var play = Ui.Button(line, "▶", () => Play(rec), 56, Palette.ButtonActive, 26);
                    Ui.Size(play, width: 70);
                    Ui.SetEnabled(play, !rec.Unplayable);
                    if (rec.Unplayable) Dim(card);
                }
            }
            if (saved.Count > 0) Ui.Label(body, T("ui.replay.savedHead"), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            foreach (var r in saved)
            {
                var rec = r;
                var card = Ui.Card(body, rec.Id, 14, 4);
                var line = Ui.Row(card, "Line", 180, 14);
                var mini = Ui.Rect(line, "MiniBox");
                Ui.Size(mini, 170, 220);
                Ui.Column(mini, 0, 0);
                Mini(mini, rec, 170);
                var info = Ui.Rect(line, "Info");
                Ui.Size(info, flexibleWidth: 1);
                Ui.Column(info, 4, 0);
                var title = Ui.Row(info, "Title", 48, 8);
                Ui.Size(Ui.Label(title, StepName(rec.StepId), 26), flexibleWidth: 1);
                WinTag(title, rec);
                long pct = rec.PlayerStartTroops <= 0 ? 0 : rec.PlayerLosses * 100 / rec.PlayerStartTroops;
                Ui.Label(info, T("ui.replay.stat", rec.Seconds / 60, (rec.Seconds % 60).ToString("00"), pct), 24, TextAnchor.MiddleLeft, Palette.TextDim);
                Ui.Label(info, DateTimeOffset.FromUnixTimeSeconds(rec.SavedAtUnixSeconds).ToLocalTime().ToString("MM.dd HH:mm"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                var play = Ui.Button(line, "▶", () => Play(rec), 100, Palette.ButtonActive, 40);
                Ui.Size(play, width: 100);
                if (rec.Unplayable)
                {
                    // 옛 규칙 녹화 (14 시안 5): 흐림 + [다시 볼 수 없음] + ▶ 꺼짐. 삭제는 ⋯에서 그대로
                    Ui.SetEnabled(play, false);
                    Ui.Size(Ui.Tag(info, T("ui.replay.unplayable"), Palette.TextDim, 20, 38), width: 220);
                    Dim(card);
                }
                Button more = null;
                more = Ui.Button(line, "⋯", () => Menu((RectTransform)more.transform, rec), 60, fontSize: 32);
                Ui.Size(more, width: 80);
            }
        }

        /// <summary>카드를 흐리게 (버튼은 그대로 눌림 — ⋯ 삭제).</summary>
        private static void Dim(RectTransform card)
        {
            var g = card.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0.5f;
        }

        /// <summary>
        /// [내 용병 고용됨] (R-120, 14 시안): 남이 내 등록(닉네임 장수 · 부대)을 고용해 싸운 전투. 내 등록들의 최근 전투(HireBook.HiredBattles)를
        /// 늦은 것부터, 줄마다 스텝 · 시각 · 결과 · 잃은 병력 · ▶ (서버 녹화를 받아 지금 규칙으로 확인한 뒤 재생). 녹화가 지워졌으면 안내.
        /// </summary>
        private void BuildHired(RectTransform body)
        {
            var loading = Ui.Label(body, T("ui.hire.loading"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            Ctx.LoadListings(HireKinds.General, gres => Ctx.LoadListings(HireKinds.Unit, ures =>
            {
                if (body == null) return;
                UnityEngine.Object.Destroy(loading.gameObject);
                if (gres == null || ures == null) { Ui.Label(body, T("ui.hire.failed"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.Bad); return; }
                var all = new List<HireListing>(gres.Listings);
                all.AddRange(ures.Listings);
                var battles = HireBook.HiredBattles(all, Ctx.AccountId);
                if (battles.Count == 0) { Ui.Label(body, T("ui.replay.hiredEmpty"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim); return; }
                foreach (var (listing, b) in battles)
                {
                    var card = Ui.Card(body, "Hired", 14, 4);
                    var line = Ui.Row(card, "Line", 110, 12);
                    var info = Ui.Rect(line, "Info");
                    Ui.Size(info, flexibleWidth: 1);
                    Ui.Column(info, 4, 0);
                    var title = Ui.Row(info, "Title", 44, 8);
                    Ui.Size(Ui.Label(title, StepName(b.StepId), 26), flexibleWidth: 1);
                    bool good = b.Result == HireResults.Win;
                    Ui.Size(Ui.Tag(title, T("ui.hire.result." + b.Result), good ? Palette.Good : Palette.Bad, 20, 40), width: 100);
                    Ui.Label(info, HireText.Line(Ctx, listing) + "  ·  " + T("ui.replay.hiredLost", b.TroopsLost.ToString("N0"), b.TroopsStart.ToString("N0")), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    Ui.Label(info, DateTimeOffset.FromUnixTimeSeconds(b.AtUnixSeconds).ToLocalTime().ToString("MM.dd HH:mm"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    string rid = b.ReplayId;
                    var play = Ui.Button(line, "▶", () => PlayHired(rid), 100, Palette.ButtonActive, 40);
                    Ui.Size(play, width: 100);
                    Ui.SetEnabled(play, !string.IsNullOrEmpty(rid));
                }
            }));
        }

        private void PlayHired(string replayId)
        {
            Ctx.LoadHireReplay(replayId, Loading.Wrap<Game.Online.HireReplayResponse>(res =>
            {
                if (Root == null) return;
                if (res?.ReplayJson == null) { App.Show(new ReplayListScreen(T("ui.replay.hiredGone"))); return; }
                ReplayRecord rec;
                try { rec = ReplayService.Read(res.ReplayJson); }
                catch (Exception) { App.Show(new ReplayListScreen(T("ui.replay.broken"))); return; }
                var playback = Ctx.ReplayService.Check(rec);
                if (playback.Ok) { App.Show(new BattleScreen(playback)); return; }
                App.Show(new ReplayListScreen(T(playback.Status == ReplayStatus.VersionChanged ? "ui.replay.versionChanged" : "ui.replay.broken")));
            }));
        }

        private void WinTag(Transform parent, ReplayRecord rec) =>
            Ui.Size(Ui.Tag(parent, T(rec.Won ? "ui.result.win" : "ui.result.lose"), rec.Won ? Palette.Good : Palette.Bad, 20, 40), width: 80);

        /// <summary>전장 미니 그림: 지형 색 바탕 + 양쪽 진영 (파랑 아래 · 빨강 위).</summary>
        private static void Mini(Transform parent, ReplayRecord rec, float h)
        {
            var box = Ui.Rect(parent, "Mini");
            Ui.Size(box, h);
            Ui.Round(Ui.Background(box, new Color(0.55f, 0.62f, 0.4f)));
            void Dot(float x, float y, Color c)
            {
                var d = Ui.Rect(box, "Dot");
                d.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                d.anchorMin = d.anchorMax = new Vector2(x, y);
                d.sizeDelta = new Vector2(h * 0.18f, h * 0.12f);
                Ui.Background(d, c);
            }
            Dot(0.3f, 0.25f, Palette.Ally); Dot(0.55f, 0.2f, Palette.Ally);
            Dot(0.45f, 0.78f, Palette.Enemy); Dot(0.7f, 0.72f, Palette.Enemy);
        }

        private void Menu(RectTransform anchor, ReplayRecord rec)
        {
            Ui.PickMenu(Root, anchor, 420,
                (T("ui.replay.share"), "", Ctx.OnlineEnabled, () => Share(rec)),
                (T("ui.replay.video"), T("ui.replay.later"), false, () => { }),
                (Ui.Colored(T("ui.replay.delete"), Palette.Bad), "", true, () =>
                    Ui.Confirm(Root, T("ui.replay.deleteTitle"), T("ui.replay.deleteBody", StepName(rec.StepId)), T("ui.replay.delete"),
                        () => { Ctx.DeleteReplay(rec.Id); App.Show(new ReplayListScreen()); }, (T("ui.common.cancel"), () => { }))));
        }

        // ---- 공략 ----
        private void BuildShared(RectTransform header, RectTransform body)
        {
            var steps = Ctx.State.ClearedSteps;
            if (_sharedStep == null && steps.Count > 0) _sharedStep = steps[steps.Count - 1];
            var pick = Ui.Row(header, "Pick", 64, 10);
            Button stepBtn = null;
            stepBtn = Ui.Button(pick, (_sharedStep == null ? T("ui.replay.pickStep") : StepName(_sharedStep)) + " ▾", () =>
            {
                var items = new List<(string, string, bool, Action)>();
                foreach (var s in steps) { string id = s; items.Add((StepName(id), "", true, () => { _sharedStep = id; App.Show(new ReplayListScreen()); })); }
                if (items.Count > 0) Ui.PickMenu(Root, (RectTransform)stepBtn.transform, 520, items.ToArray());
            }, 64, fontSize: 24);
            Ui.SegmentTabs(header, new[] { ("won", T("ui.replay.sort.won")), ("losses", T("ui.replay.sort.losses")), ("fast", T("ui.replay.sort.fast")), ("new", T("ui.replay.sort.new")) }, _sort, id => { _sort = id; App.Show(new ReplayListScreen()); }, 56, 200);
            if (_sharedStep == null) { Ui.Label(body, T("ui.replay.noCleared"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim); return; }
            var loading = Ui.Label(body, T("ui.shared.loading"), Ui.FontSmall, TextAnchor.MiddleCenter, Palette.TextDim);
            string step = _sharedStep;
            Ctx.Online.GetSharedReplays(step, res =>
            {
                if (body == null) return;
                loading.text = res == null ? T("ui.shared.failed") : res.Items.Count == 0 ? T("ui.shared.empty") : "";
                if (res == null) return;
                var items = new List<Game.Online.SharedReplaySummary>(res.Items);
                Comparison<Game.Online.SharedReplaySummary> cmp;
                switch (_sort)
                {
                    case "losses": cmp = (a, b) => Ratio(a).CompareTo(Ratio(b)); break;
                    case "fast": cmp = (a, b) => a.Seconds.CompareTo(b.Seconds); break;
                    case "new": cmp = (a, b) => b.SharedAtUnixSeconds.CompareTo(a.SharedAtUnixSeconds); break;
                    default: cmp = (a, b) => a.Won != b.Won ? (a.Won ? -1 : 1) : Ratio(a).CompareTo(Ratio(b)); break;
                }
                items.Sort(cmp);
                foreach (var it in items)
                {
                    var item = it;
                    var card = Ui.Card(body, item.Id, 14, 4);
                    var line = Ui.Row(card, "Line", 120, 12);
                    var info = Ui.Rect(line, "Info");
                    Ui.Size(info, flexibleWidth: 1);
                    Ui.Column(info, 4, 0);
                    var title = Ui.Row(info, "Title", 48, 8);
                    Ui.Size(Ui.Label(title, string.IsNullOrEmpty(item.OwnerName) ? T("ui.shared.anonymous") : item.OwnerName, 26), flexibleWidth: 1);
                    Ui.Size(Ui.Tag(title, T(item.Won ? "ui.result.win" : "ui.result.lose"), item.Won ? Palette.Good : Palette.Bad, 20, 40), width: 80);
                    long pct = item.PlayerStartTroops <= 0 ? 0 : item.PlayerLosses * 100 / item.PlayerStartTroops;
                    Ui.Label(info, T("ui.replay.stat", item.Seconds / 60, (item.Seconds % 60).ToString("00"), pct) + "   " + DateTimeOffset.FromUnixTimeSeconds(item.SharedAtUnixSeconds).ToLocalTime().ToString("MM.dd"), 22, TextAnchor.MiddleLeft, Palette.TextDim);
                    Ui.Size(Ui.Button(line, "▶", () => Watch(item.Id), 100, Palette.ButtonActive, 40), width: 100);
                }
            });
        }

        private static long Ratio(Game.Online.SharedReplaySummary s) => s.PlayerStartTroops <= 0 ? 0 : s.PlayerLosses * 10000 / s.PlayerStartTroops;

        private void Watch(string id)
        {
            Ctx.Online.GetSharedReplay(id, Loading.Wrap<Game.Online.SharedReplayResponse>(res =>
            {
                if (res == null) { App.Show(new ReplayListScreen(T("ui.shared.failed"))); return; }
                ReplayRecord rec;
                try { rec = ReplayService.Read(res.ReplayJson); } catch (Exception) { rec = null; }
                var playback = rec == null ? null : Ctx.ReplayService.Check(rec);
                if (playback != null && playback.Ok) { App.Show(new BattleScreen(playback)); return; }
                App.Show(new ReplayListScreen(T(playback?.Status == ReplayStatus.VersionChanged ? "ui.replay.versionChanged" : "ui.replay.broken")));
            }));
        }

        private void Share(ReplayRecord rec)
        {
            Ctx.Online.ShareReplay(new Game.Online.ShareReplayRequest { ReplayJson = ReplayService.Write(rec) }, Loading.Wrap<Game.Online.ShareReplayResponse>(res =>
                App.Show(new ReplayListScreen(res == null ? T("ui.shared.failed")
                    : res.Ok ? T("ui.replay.shared")
                    : T(res.Reason == ReplayStatus.VersionChanged ? "ui.replay.versionChanged" : "ui.replay.broken")))));
        }

        private void Play(ReplayRecord rec)
        {
            var playback = Ctx.ReplayService.Check(rec);
            if (playback.Ok) { App.Show(new BattleScreen(playback)); return; }
            Ctx.MarkReplayUnplayable(rec.Id); // 다음부터 목록에서 흐리게 (미리 확인은 다시 돌려야 해 무거워, 한 번 눌러 본 것만)
            App.Show(new ReplayListScreen(T(playback.Status == ReplayStatus.VersionChanged ? "ui.replay.versionChanged" : "ui.replay.broken")));
        }

        private string StepName(string stepId)
        {
            try { return T(Ctx.Flow.LoadStep(stepId).NameKey); }
            catch (Exception) { return stepId; }
        }
    }

    /// <summary>
    /// 공략 (R-81): 다른 사람이 공유한 이 스텝의 녹화. 이긴 것·손실 적은 것 먼저. [보기]는 받아서 지금 규칙으로 확인한 뒤 재생.
    /// </summary>
    public sealed class SharedReplayScreen : GameScreen
    {
        private readonly string _stepId;
        private readonly string _message;
        public SharedReplayScreen(string stepId, string message = null) { _stepId = stepId; _message = message; }

        public override void Build()
        {
            string stepName;
            try { stepName = T(Ctx.Flow.LoadStep(_stepId).NameKey); } catch (Exception) { stepName = _stepId; }
            var (header, list, _, _) = Ui.TitledPage(Root, T("ui.shared.title", stepName), () => App.Show(new StepScreen(_stepId)));
            InfoButton.Add(Root, T("ui.shared.note"));
            if (_message != null) Ui.Label(header, _message, 26, TextAnchor.MiddleLeft, Palette.Bad);
            var loading = Ui.Label(list, T("ui.shared.loading"), Ui.FontSmall, TextAnchor.MiddleLeft, Palette.TextDim);

            Ctx.Online.GetSharedReplays(_stepId, res =>
            {
                if (list == null) return; // 화면이 바뀜
                loading.text = res == null ? T("ui.shared.failed") : res.Items.Count == 0 ? T("ui.shared.empty") : "";
                if (res == null) return;
                foreach (var it in res.Items)
                {
                    var item = it;
                    var card = Ui.Card(list, item.Id);
                    string result = item.Won ? Ui.Colored(T("ui.result.win"), Palette.Good) : Ui.Colored(T("ui.result.lose"), Palette.Bad);
                    Ui.Label(card, result + "  " + (string.IsNullOrEmpty(item.OwnerName) ? T("ui.shared.anonymous") : item.OwnerName), Ui.FontSmall);
                    string when = DateTimeOffset.FromUnixTimeSeconds(item.SharedAtUnixSeconds).ToLocalTime().ToString("MM-dd HH:mm");
                    Ui.Label(card, T("ui.replay.line", item.Seconds / 60, (item.Seconds % 60).ToString("00"), item.PlayerLosses.ToString("N0"), item.PlayerStartTroops.ToString("N0"), when),
                        24, TextAnchor.MiddleLeft, Palette.TextDim);
                    Ui.Button(card, T("ui.shared.watch"), () => Watch(item.Id), 80, Palette.ButtonActive, 28);
                }
            });
        }

        private void Watch(string id)
        {
            Ctx.Online.GetSharedReplay(id, Loading.Wrap<Game.Online.SharedReplayResponse>(res =>
            {
                if (res == null) { App.Show(new SharedReplayScreen(_stepId, T("ui.shared.failed"))); return; }
                ReplayRecord rec;
                try { rec = ReplayService.Read(res.ReplayJson); } catch (Exception) { rec = null; }
                var playback = rec == null ? null : Ctx.ReplayService.Check(rec);
                if (playback != null && playback.Ok) { App.Show(new BattleScreen(playback)); return; }
                App.Show(new SharedReplayScreen(_stepId, T(playback?.Status == ReplayStatus.VersionChanged ? "ui.replay.versionChanged" : "ui.replay.broken")));
            }));
        }
    }
}
