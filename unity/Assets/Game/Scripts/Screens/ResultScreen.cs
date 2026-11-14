using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 전투 결과 (story.md 5장, 20 시안 v2). 보여줄 내용은 규칙 쪽 <see cref="BattleReport"/>가 이미 계산했다.
    /// 큰 도장(승리 주홍 · 패배 먹)이 찍힌 뒤 머리로 작아지고, 영토 · 배신 줄 · 패배 팁(R-104, 바로가기) · 구역 접기(잃은 것 · 자란 것 · 얻은 것 · 포로) ·
    /// 성장 게이지(경험치 · 결속: 원래 양 진하게 + 이번 양 밝게, +n / −n, Lv UP) · 아래 고정 [▶ 녹화 ⋯] + 주요 [계속 ›] (패배: [스토리로] + [다시 도전]).
    /// </summary>
    public sealed class ResultScreen : GameScreen
    {
        private readonly SortiePlan _plan;
        private readonly BattleResult _result;
        private readonly BattleReport _report;
        private readonly string _onlineOutcome;
        private static readonly HashSet<string> Open = new HashSet<string>();
        private RectTransform _stamp;
        private float _stampTime;
        private readonly bool _animate;

        /// <param name="onlineOutcome">서버 확인 결과 (SyncOutcomes, 오프라인 모드면 null).</param>
        public ResultScreen(SortiePlan plan, BattleResult result, BattleReport report, string onlineOutcome = null) : this(plan, result, report, onlineOutcome, true) { }

        private ResultScreen(SortiePlan plan, BattleResult result, BattleReport report, string onlineOutcome, bool animate)
        {
            _plan = plan;
            _result = result;
            _report = report;
            _onlineOutcome = onlineOutcome;
            _animate = animate;
        }

        private void Reopen() => App.Show(new ResultScreen(_plan, _result, _report, _onlineOutcome, false));

        public override void Build()
        {
            if (_animate) { Open.Clear(); Sfx.Play(_report.Won ? "cheer" : "gong"); }
            var (header, body, footer) = Ui.PageLayout(Root, 170);
            bool won = _report.Won;

            // ---- 머리: 도장 · 전투명 · 시간 · 끝난 까닭 ----
            var top = Ui.Row(header, "Head", 240, 20);
            _stamp = Ui.Rect(top, "Stamp");
            Ui.Size(_stamp, 230, 230);
            var seal = Ui.Background(_stamp, won ? Palette.Seal : new Color(0.17f, 0.15f, 0.14f));
            seal.sprite = UiShapes.Circle;
            var glyph = Ui.Label(_stamp, won ? "勝" : "敗", 120, TextAnchor.MiddleCenter, Color.white);
            glyph.font = Ui.TitleFont;
            Ui.Fill(glyph.rectTransform);
            _stamp.localRotation = Quaternion.Euler(0, 0, -8f);
            if (_animate) { _stamp.localScale = Vector3.one * 2.2f; _stampTime = 0f; } else _stampTime = 1f;
            var info = Ui.Rect(top, "Info");
            Ui.Size(info, flexibleWidth: 1);
            Ui.Column(info, 6, 0).childAlignment = TextAnchor.MiddleLeft;
            Ui.Label(info, T(won ? "ui.result.win" : "ui.result.lose") + (_report.FirstClear ? "  " + Ui.Colored(T("ui.result.firstClear"), Palette.Seal) : ""), Ui.FontTitle - 10, TextAnchor.MiddleLeft, won ? Palette.Seal : Palette.Text);
            Ui.Label(info, T(_plan.Step.NameKey), 30);
            long sec = _result.ElapsedSeconds.FloorToLong();
            Ui.Label(info, sec / 60 + ":" + (sec % 60).ToString("00") + "  ·  " + WhyEnded(), 24, TextAnchor.MiddleLeft, Palette.TextDim);
            if (_onlineOutcome != null)
                Ui.Label(header, T("ui.online." + _onlineOutcome), 22, TextAnchor.MiddleLeft, _onlineOutcome == SyncOutcomes.Rejected ? Palette.Bad : Palette.TextDim);

            // ---- 영토 · 배신 · 패배 팁 ----
            if (_plan.Step.IsTerritory)
            {
                var land = Ui.Card(body, "Land", 18, 4);
                land.GetComponent<Image>().color = won ? new Color(0.86f, 0.93f, 0.84f) : new Color(0.93f, 0.9f, 0.86f);
                Ui.Label(land, T(won ? "ui.result.flagSwap" : "ui.result.flagHold"), 26);
            }
            if (_report.Betrayed.Count > 0)
            {
                var b = Ui.Card(body, "Betrayed", 18, 4);
                b.GetComponent<Image>().color = new Color(0.9f, 0.86f, 0.95f);
                var refund = new List<string>();
                foreach (var kv in _report.Refunded) refund.Add(kv.Value.ToString("N0") + " " + T("resource." + kv.Key));
                Ui.Label(b, T("ui.result.refund", string.Join(", ", refund)), 24, TextAnchor.MiddleLeft, new Color(0.42f, 0.25f, 0.6f));
            }
            if (!won)
            {
                var tip = DefeatAdvice.Load(Ctx.Data).Pick(_plan, _result, false);
                if (tip != null)
                {
                    var card = Ui.Card(body, "Tip", 18, 6);
                    card.GetComponent<Image>().color = new Color(0.98f, 0.93f, 0.75f);
                    Ui.Label(card, Ui.Colored("! ", Palette.Seal) + T("ui.result.tip." + tip.Id), 28);
                    var row = Ui.Row(card, "Go", 70, 10);
                    foreach (var s in tip.Shortcuts)
                    {
                        string go = s;
                        Ui.Button(row, T("ui.result.go." + go), () => Go(go), 70, fontSize: 24);
                    }
                }
            }

            // ---- 구역: 잃은 것 · 자란 것 · 얻은 것 · 포로 ----
            BuildLost(body);
            BuildGrown(body);
            BuildGained(body);
            BuildPrisoners(body);

            // ---- 아래: 녹화 ⋯ · 계속 / 다시 도전 ----
            var recent = Ctx.RecordRecent(_plan, _result.Commands, _result); // R-101: 최근에 저절로
            Button rec = null;
            rec = Ui.Button(footer, "▶ " + T("ui.result.replay"), () => Ui.PickMenu(Root, (RectTransform)rec.transform, 420,
                (T("ui.replay.save"), "", Ctx.Replays.Find(recent.Id) != null && !Ctx.Replays.Items.Contains(recent), () => { Ctx.KeepReplay(recent.Id); Reopen(); }),
                (T("ui.result.replayList"), "", true, () => App.Show(new ReplayListScreen()))), 150, fontSize: 24);
            Ui.Size(rec, width: 260);
            bool territory = _plan.Step.IsTerritory;
            if (won)
            {
                bool after = !territory && AfterDialogueScreen.Has(Ctx, _plan.Step.Id);
                bool prisoners = _report.NewPrisoners.Count > 0;
                string next = after ? T("ui.result.next.after") : prisoners ? T("ui.result.next.prisoner") : T(territory ? "ui.result.next.territory" : "ui.result.next.story");
                var go = Ui.Styled(Ui.PrimaryButton(footer, T("ui.result.continue") + "\n" + Ui.Colored(next, new Color(1f, 1f, 1f, 0.75f)), () =>
                {
                    if (after) App.Show(new AfterDialogueScreen(_plan.Step.Id));
                    else if (prisoners) App.ShowMain("prisoner");
                    else App.ShowMain(territory ? "territory" : "story");
                }, 150, 30), "button.primary");
                Ui.Size(go, flexibleWidth: 1);
            }
            else
            {
                Ui.Size(Ui.Button(footer, T(territory ? "ui.result.toTerritory" : "ui.result.toStory"), () => App.ShowMain(territory ? "territory" : "story"), 150, fontSize: 26), width: 260);
                var retry = Ui.Styled(Ui.PrimaryButton(footer, T("ui.result.retry"), () =>
                {
                    if (territory) App.ShowMain("territory");
                    else App.Show(new StepScreen(_plan.Step.Id));
                }, 150, 32), "button.primary");
                Ui.Size(retry, flexibleWidth: 1);
            }
        }

        /// <summary>도장 찍기: 크게 → 원래 크기로 (0.35초).</summary>
        public override void Tick(float deltaTime)
        {
            if (_stamp == null || _stampTime >= 1f) return;
            _stampTime = Mathf.Min(1f, _stampTime + deltaTime / 0.35f);
            float s = Mathf.Lerp(2.2f, 1f, 1f - (1f - _stampTime) * (1f - _stampTime));
            _stamp.localScale = Vector3.one * s;
        }

        private void Go(string id)
        {
            switch (id)
            {
                case "shared": if (Ctx.OnlineEnabled) App.Show(new SharedReplayScreen(_plan.Step.Id)); else App.ShowMain("replay"); break;
                case "supply": App.Show(new StepScreen(_plan.Step.Id)); break;
                default: App.ShowMain(id); break;
            }
        }

        // ---- 접는 구역 ----

        /// <summary>구역 머리 (아이콘 · 이름 · 한 줄 요약 · ▸/▾). 열려 있으면 내용 카드를 돌려준다 (닫혀 있으면 null).</summary>
        private RectTransform Fold(RectTransform body, string key, string icon, string summary, bool forceOpen, bool gold = false)
        {
            bool open = forceOpen || Open.Contains(key);
            var card = Ui.Card(body, key, 16, 6);
            if (gold)
            {
                var o = card.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0.85f, 0.65f, 0.15f);
                o.effectDistance = new Vector2(4, -4);
            }
            var head = Ui.Row(card, "Head", 60, 12);
            var dot = Ui.Rect(head, "Icon");
            Ui.Size(dot, 52, 52);
            Ui.Background(dot, new Color(0.9f, 0.86f, 0.78f)).sprite = UiShapes.Circle;
            var g = Ui.Label(dot, icon, 26, TextAnchor.MiddleCenter, Palette.Text);
            g.font = Ui.TitleFont;
            Ui.Fill(g.rectTransform);
            Ui.Size(Ui.Label(head, T("ui.result.sec." + key), 28), width: 160);
            Ui.Size(Ui.Label(head, summary, 22, TextAnchor.MiddleLeft, Palette.TextDim), flexibleWidth: 1);
            if (!forceOpen)
            {
                Ui.Size(Ui.Label(head, T(open ? "ui.result.fold" : "ui.result.more"), 22, TextAnchor.MiddleRight, Palette.TextDim), width: 110);
                card.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (!Open.Remove(key)) Open.Add(key); Reopen(); });
            }
            return open ? card : null;
        }

        private void BuildLost(RectTransform body)
        {
            long dead = 0, wounded = 0;
            foreach (var u in _report.Units) { dead += u.Dead; wounded += u.Wounded; }
            int beaten = 0;
            foreach (var c in _result.Corps) if (c.Team != StoryFlow.PlayerTeam && c.Status != CorpsStatus.Active) beaten++;
            var card = Fold(body, "lost", "損", T("ui.result.lostSummary", dead.ToString("N0"), wounded.ToString("N0"), beaten), false);
            if (card == null) return;
            foreach (var u in _report.Units)
            {
                var unit = Ctx.State.FindUnit(u.UnitId);
                string troop = unit != null && Ctx.Defs.TroopTypes.TryGetValue(unit.TroopType, out var tt) ? T(tt.NameKey) : "";
                long start = 0;
                foreach (var c in _result.Corps) if (c.Units != null) foreach (var ur in c.Units) if (ur.UnitId == u.UnitId) start += ur.StartTroops;
                if (start <= 0) start = u.Losses;
                var row = Ui.Row(card, u.UnitId, 44, 10);
                Ui.Size(Ui.Label(row, Ctx.UnitLabel(u.UnitId) + " " + troop, 22), width: 300);
                LossBar(row, start, u.Wounded, u.Dead);
                Ui.Size(Ui.Label(row, "−" + u.Losses.ToString("N0"), 22, TextAnchor.MiddleRight, u.Losses > 0 ? Palette.Bad : Palette.TextDim), width: 120);
            }
            Ui.Label(card, T("ui.result.hurtNote"), 20, TextAnchor.MiddleLeft, Palette.TextDim);
        }

        /// <summary>손실 막대: 살아남음 초록 · 부상 주황 · 전사 빨강 (전체 = 처음 병력).</summary>
        private static void LossBar(RectTransform row, long start, long wounded, long dead)
        {
            var bar = Ui.Rect(row, "Loss");
            Ui.Size(bar, 20, -1, -1, 1);
            Ui.Background(bar, Palette.Good);
            float w = start <= 0 ? 0f : Mathf.Clamp01((float)wounded / start);
            float d = start <= 0 ? 0f : Mathf.Clamp01((float)dead / start);
            void Seg(float from, float to, Color c)
            {
                if (to <= from) return;
                var s = Ui.Rect(bar, "Seg");
                s.anchorMin = new Vector2(from, 0f);
                s.anchorMax = new Vector2(to, 1f);
                s.offsetMin = s.offsetMax = Vector2.zero;
                Ui.Background(s, c);
            }
            Seg(1f - d - w, 1f - d, new Color(0.95f, 0.6f, 0.2f));
            Seg(1f - d, 1f, Palette.Bad);
        }

        private void BuildGrown(RectTransform body)
        {
            int ups = _report.LevelUps.Count;
            int bondUp = 0, bondDown = 0;
            foreach (var u in _report.Units) { if (u.CohesionAfter > u.CohesionBefore) bondUp++; else if (u.CohesionAfter < u.CohesionBefore) bondDown++; }
            if (_report.ExpGains.Count == 0 && bondUp == 0 && bondDown == 0) return;
            var card = Fold(body, "grown", "長", T("ui.result.grownSummary", ups, bondUp, bondDown), ups > 0, ups > 0);
            if (card == null) return;
            var gains = new List<KeyValuePair<string, (int levelBefore, long expBefore, long gained, int levelAfter, long expAfter)>>(_report.ExpGains);
            gains.Sort((a, b) => (b.Value.levelAfter - b.Value.levelBefore).CompareTo(a.Value.levelAfter - a.Value.levelBefore));
            foreach (var kv in gains)
            {
                var v = kv.Value;
                long need = System.Math.Max(1, Ctx.Defs.Levels.ExpToNext(v.levelAfter));
                float baseRatio = v.levelAfter == v.levelBefore ? Mathf.Clamp01((float)v.expBefore / need) : 0f;
                float newRatio = Mathf.Clamp01((float)v.expAfter / need);
                string tag = v.levelAfter > v.levelBefore ? T("ui.result.lvUp", v.levelBefore, v.levelAfter) : null;
                GrowthRow(card, Ctx.GeneralName(kv.Key), baseRatio, newRatio, "+" + v.gained.ToString("N0"), Palette.Good, tag, new Color(0.85f, 0.65f, 0.15f));
            }
            foreach (var u in _report.Units)
            {
                if (u.CohesionAfter == u.CohesionBefore) continue;
                float before = Mathf.Clamp01((float)u.CohesionBefore.ToDisplayDouble() / 100f);
                float after = Mathf.Clamp01((float)u.CohesionAfter.ToDisplayDouble() / 100f);
                bool up = after > before;
                var diff = u.CohesionAfter - u.CohesionBefore;
                string n = (up ? "+" : "−") + (up ? diff : -diff).ToDisplayDouble().ToString("0.#");
                GrowthRow(card, Ctx.UnitLabel(u.UnitId) + " " + T("ui.result.bondName"), up ? before : after, up ? after : before, n, up ? Palette.Good : Palette.Bad,
                    T(up ? "ui.result.bondUp" : "ui.result.bondDown"), up ? Palette.Ally : Palette.Bad, !up);
            }
        }

        /// <summary>성장 게이지 한 줄: 이름 · 막대 (원래 양 진하게 + 이번 양 밝게 / 줄면 빨강) · +n · 꼬리표.</summary>
        private static void GrowthRow(RectTransform card, string name, float baseRatio, float newRatio, string delta, Color deltaColor, string tag, Color tagColor, bool down = false)
        {
            var row = Ui.Row(card, name, 46, 10);
            Ui.Size(Ui.Label(row, name, 22), width: 260);
            var bar = Ui.Rect(row, "Bar");
            Ui.Size(bar, 20, -1, -1, 1);
            Ui.Background(bar, new Color(0f, 0f, 0f, 0.12f));
            void Seg(float from, float to, Color c)
            {
                if (to <= from) return;
                var s = Ui.Rect(bar, "Seg");
                s.anchorMin = new Vector2(from, 0f);
                s.anchorMax = new Vector2(to, 1f);
                s.offsetMin = s.offsetMax = Vector2.zero;
                Ui.Background(s, c);
            }
            Seg(0f, baseRatio, down ? new Color(0.35f, 0.5f, 0.35f) : new Color(0.25f, 0.5f, 0.3f));
            Seg(baseRatio, newRatio, down ? new Color(0.85f, 0.35f, 0.3f, 0.7f) : new Color(0.55f, 0.85f, 0.5f));
            Ui.Size(Ui.Label(row, delta, 22, TextAnchor.MiddleRight, deltaColor), width: 110);
            if (tag != null) Ui.Size(Ui.Tag(row, tag, tagColor, 18, 36), width: 170);
        }

        private void BuildGained(RectTransform body)
        {
            var lines = new List<string>();
            lines.Add(T("ui.result.supplyBack", _report.SupplyReturned.ToString("N0")) + (_report.SupplyCaptured > 0 ? "  " + T("ui.result.supplyCaught", _report.SupplyCaptured.ToString("N0")) : ""));
            foreach (var kv in _report.RewardResources) lines.Add(T("resource." + kv.Key) + " +" + kv.Value.ToString("N0"));
            foreach (var f in _report.UnlockedFormations) lines.Add(T("ui.result.formations") + ": " + (Ctx.Defs.Formations.TryGetValue(f, out var fd) ? T(fd.NameKey) : f));
            foreach (var s in _report.UnlockedSkills) lines.Add(T("ui.result.skills") + ": " + (Ctx.Defs.Skills.TryGetValue(s, out var sd) ? T(sd.NameKey) : s));
            foreach (var id in _report.JoinedGenerals) lines.Add(T("ui.result.joined") + ": " + Ctx.GeneralName(id));
            foreach (var id in _report.JoinedAsShard) lines.Add(T("ui.result.shardLine", Ctx.GeneralName(id)));
            bool secrets = _report.Secrets.Count > 0;
            var card = Fold(body, "gained", "得", T("ui.result.gainedSummary", lines.Count + _report.Secrets.Count), secrets);
            if (card == null) return;
            foreach (var l in lines) Ui.Label(card, l, 24);
            foreach (var s in _report.Secrets)
            {
                var sc = Ui.Card(card, s.SecretId, 14, 4);
                var o = sc.gameObject.AddComponent<Outline>();
                o.effectColor = Palette.Seal;
                o.effectDistance = new Vector2(4, -4);
                var row = Ui.Row(sc, "Row", 60, 10);
                Ui.Size(Ui.Tag(row, T("ui.gacha.newTag"), Palette.Seal, 18, 36), width: 80);
                Ui.Size(Ui.Label(row, T("ui.result.secretFound") + "  " + SecretText.Found(Ctx, s), 22), flexibleWidth: 1);
                Ui.Size(Ui.Button(row, T("ui.result.toCodex"), () => App.ShowMain("codex"), 56, fontSize: 22), width: 140);
            }
        }

        private void BuildPrisoners(RectTransform body)
        {
            var lines = new List<string>();
            foreach (var id in _report.NewPrisoners) lines.Add(T("ui.result.caught", Ctx.GeneralName(id)));
            foreach (var id in _report.CapturedOwn) lines.Add(Ui.Colored(T("ui.result.capturedOwnLine", Ctx.GeneralName(id)), Palette.Bad));
            foreach (var kv in _report.Injuries) lines.Add(Ctx.GeneralName(kv.Key) + " (" + T("ui.generals." + kv.Value) + ")");
            foreach (var id in _report.Rescued) lines.Add(T("ui.result.rescued") + ": " + Ctx.GeneralName(id));
            if (lines.Count == 0) return;
            var card = Fold(body, "prisoners", "俘", T("ui.result.prisonerSummary", _report.NewPrisoners.Count, _report.CapturedOwn.Count, _report.Injuries.Count), _report.NewPrisoners.Count > 0);
            if (card == null) return;
            foreach (var l in lines) Ui.Label(card, l, 24);
            if (_report.NewPrisoners.Count > 0) Ui.Label(card, T("ui.result.prisonerHint"), 20, TextAnchor.MiddleLeft, Palette.TextDim);
        }

        /// <summary>끝난 까닭을 구체적으로: 승패 조건이면 누가 무엇으로 졌는지 (총대장 퇴각 / 모두 물러남 / 그 밖의 조건).</summary>
        private string WhyEnded()
        {
            if (_result.EndReason != "defeat" || _plan?.Setup == null) return T("ui.result.endReason." + _result.EndReason);
            string loser = _report.Won ? StoryFlow.EnemyTeam : StoryFlow.PlayerTeam;
            bool anyIn = false;
            CorpsSetup commander = null;
            foreach (var c in _plan.Setup.Corps)
            {
                if (c.Team != loser) continue;
                if (c.Role == "commander") commander = c;
                var r = _result.Find(c.Id);
                if (r != null && r.Status == CorpsStatus.Active) anyIn = true;
            }
            if (!anyIn) return T(_report.Won ? "ui.result.why.allEnemy" : "ui.result.why.allOwn");
            var cr = commander == null ? null : _result.Find(commander.Id);
            if (cr != null && cr.Status != CorpsStatus.Active) return T(_report.Won ? "ui.result.why.commanderEnemy" : "ui.result.why.commanderOwn");
            return T("ui.result.why.other");
        }
    }
}
