using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Battle.Model.Control;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Flow;
using Game.Foundation.Math;
using Game.Unity.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Boot
{
    /// <summary>
    /// 플레이 테스트 (개발용, 에디터 메뉴 Game ▸ Play Test): 원격 인계의 확인 목록(탭·마스터·샘플전투·맵·전투 이동·일기토·탈것·아이콘·큰 전투)을
    /// 버튼을 코드로 눌러 가며 차례로 해 보고, 화면을 PNG로, 결과를 <c>playtest.log</c>로 남긴다.
    /// 마스터 행동은 세이브를 바꾸므로 시작 전에 세이브 폴더를 <c>save_backup/</c>으로 복사해 둔다.
    /// </summary>
    public sealed class PlayTestTour
    {
        private readonly GameApp _app;
        private readonly string _folder;
        private readonly StringBuilder _log = new StringBuilder();
        public bool Done { get; private set; }
        public string Folder => _folder;

        private readonly bool _battleOnly;
        public PlayTestTour(GameApp app, string folder, bool battleOnly = false) { _app = app; _folder = folder; _battleOnly = battleOnly; }

        private GameContext Ctx => _app.Context;
        private string T(string key, params object[] args) => Ctx.T(key, args);

        public IEnumerator Run()
        {
            Directory.CreateDirectory(_folder);
            Log("# 플레이 테스트 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", 화면 " + Screen.width + "x" + Screen.height);
            BackupSave();
            UI.GuideOverlay.Suppressed = true; // 처음 안내가 화면·전투를 막지 않게
            var sections = new (string name, Func<IEnumerator> run)[]
            {
                ("1 하단 탭", Tabs),
                ("2 마스터", Master),
                ("3 샘플전투", SampleBattle),
                ("4 맵", Maps),
                ("5·6·7 전투 이동·일기토(거절)·탈것", () => BattleChecks(false)),
                ("6 일기토 응하기 (새 전투)", () => BattleChecks(true)),
                ("8 아이콘·ⓘ", IconsAndInfo),
                ("9 큰 전투", BigBattle),
            };
            if (_battleOnly) sections = new (string name, Func<IEnumerator> run)[]
            {
                ("R-92 진형 전환", Reform),
                ("R-93 퇴각 선택 [버틴다] + 사기 막대·쓰러짐·일기토 알림", () => RetreatChoice(true)),
                ("R-93 퇴각 선택 [퇴각한다]", () => RetreatChoice(false)),
                ("장수 자세 확대", PoseCloseups),
                ("9 큰 전투", BigBattle),
            };
            foreach (var s in sections)
            {
                Log("\n## " + s.name);
                yield return Guard(s.run());
            }
            UI.GuideOverlay.Suppressed = false;
            Ctx.ClearSuspendedBattle();
            _app.ShowMain("story");
            Log("\n# 끝");
            File.WriteAllText(Path.Combine(_folder, "playtest.log"), _log.ToString(), new UTF8Encoding(false));
            Done = true;
            Debug.Log("플레이 테스트 끝: " + _folder);
        }

        // ---------- 1. 하단 두 줄 탭 ----------
        private IEnumerator Tabs()
        {
            _app.ShowMain("story");
            yield return Frames();
            yield return Shot("pt01_tabs");
            var dev = FindByName("DevTabs");
            if (dev == null) { Log("개발 탭 줄 없음 (DevToolsVisible=" + Ctx.DevToolsVisible + ")"); yield break; }
            var scroll = dev.GetComponentInParent<ScrollRect>();
            var viewport = scroll != null ? (RectTransform)scroll.viewport ?? (RectTransform)scroll.transform : null;
            float contentW = ((RectTransform)dev).rect.width, viewW = viewport != null ? viewport.rect.width : 0f;
            Log($"개발 탭 줄: 내용 폭 {contentW:0} / 보이는 폭 {viewW:0} → {(contentW > viewW + 1 ? "가로 스크롤 필요" : "한 화면에 다 보임")}, 스크롤 {(scroll != null && scroll.horizontal ? "켜짐" : "꺼짐")}, '›' 표시 {(FindByName("MoreHint") != null ? "있음" : "없음")}");
            if (scroll != null)
            {
                scroll.horizontalNormalizedPosition = 1f;
                yield return Frames();
                yield return Shot("pt02_tabs_scrolled_right");
            }
            // 위 줄 탭을 하나씩 눌러 내용이 바뀌는지
            var labels = dev.GetComponentsInChildren<Button>(true).Select(LabelOf).ToList();
            var idOf = new Dictionary<string, string>();
            foreach (var t in Ctx.MainTabs) if (!idOf.ContainsKey(T(t.NameKey))) idOf[T(t.NameKey)] = t.Id;
            foreach (var label in labels)
            {
                var b = FindButton(label, FindByName("DevTabs"));
                if (b == null) { Log($"탭 '{label}': 버튼을 다시 못 찾음"); continue; }
                b.onClick.Invoke();
                yield return Frames();
                string id = idOf.TryGetValue(label, out var tid) ? tid : null;
                bool shown = id != null && FindByName(id) != null;
                bool comingSoon = Texts().Any(x => x.text == T("ui.tab.comingSoon"));
                Log($"탭 '{label}' ({id ?? "?"}) → {(shown ? "그 탭 내용이 열림" : "내용 못 찾음!")}{(comingSoon ? " (준비 중 화면)" : "")}");
            }
        }

        // ---------- 2. 마스터 ----------
        private IEnumerator Master()
        {
            if (!HasTab("master")) { Log("마스터 탭 없음"); yield break; }
            _app.ShowMain("master");
            yield return Frames();
            yield return Shot("pt03_master");
            var before = Wallet();
            foreach (var rid in new[] { "supply", "gold", "gem" })
            {
                string res = T("resource." + rid);
                var b = AllButtons().FirstOrDefault(x => LabelOf(x).Contains(res) && x.GetComponentInParent<ScrollRect>() != null && !LabelOf(x).Equals("+"));
                if (b == null) { Log($"'{res}' 지급 버튼 없음"); continue; }
                Log($"누름: '{LabelOf(b)}'");
                b.onClick.Invoke();
                yield return new WaitForSecondsRealtime(1.5f); // 서버 왕복
            }
            _app.ShowMain("master");
            yield return Frames();
            var after = Wallet();
            Log("자원 전 " + Fmt(before) + " → 후 " + Fmt(after) + " (상단 숫자는 화면을 다시 열어야 바뀌는지도 같이 봄)");
            Log("화면 결과 글: " + string.Join(" / ", Texts().Where(t => t.color == UI.Palette.Good || t.color == UI.Palette.Bad).Select(t => t.text)));
            yield return Shot("pt04_master_after_grant");

            int unlockedBefore = Unlocked(), genBefore = Ctx.State.Generals.Count;
            yield return Press(T("ui.master.unlockAll"), 1.5f);
            yield return Press(T("ui.master.allGenerals"), 1.5f);
            _app.ShowMain("master");
            yield return Frames();
            Log($"열린 스텝 {unlockedBefore} → {Unlocked()} / 전체 {Ctx.Defs.StepIndex.Order.Count}, 장수 {genBefore} → {Ctx.State.Generals.Count}");
            yield return Shot("pt05_master_after_unlock");
        }

        // ---------- 3. 샘플전투 ----------
        private IEnumerator SampleBattle()
        {
            if (!HasTab("sampleBattle")) { Log("샘플전투 탭 없음"); yield break; }
            _app.ShowMain("sampleBattle");
            yield return Frames();
            yield return Shot("pt06_sample_list");
            var catalog = SampleBattles.Catalog(Ctx.Defs, Ctx.Flow);
            Log("샘플 맵 " + catalog.Count + "개: " + string.Join(", ", catalog.Select(m => m.MapId + "(" + m.BattleType + ", 스텝 " + m.Steps.Count + ")")));
            if (catalog.Count == 0) yield break;
            string saveBefore = SaveJson.Write(Ctx.State);
            var fight = AllButtons().FirstOrDefault(b => LabelOf(b) == T("ui.sample.fight"));
            if (fight == null) { Log("[싸우기] 버튼 없음"); yield break; }
            fight.onClick.Invoke();
            yield return new WaitForSecondsRealtime(2f);
            Log("싸우기 → 화면 " + ScreenName());
            yield return Shot("pt07_sample_battle");
            yield return Press(T("ui.battle.skip"), 2f);
            Log("끝까지 → 화면 " + ScreenName());
            yield return Shot("pt08_sample_result");
            yield return Press(T("ui.sample.again"), 2f);
            Log("다시 싸우기 → 화면 " + ScreenName());
            yield return Press(T("ui.battle.skip"), 2f);
            yield return Press(T("ui.sample.backToList"), 1f);
            Log("목록 → 화면 " + ScreenName());
            string saveAfter = SaveJson.Write(Ctx.State);
            Log("세이브 " + (saveBefore == saveAfter ? "그대로 (같음)" : "바뀜! " + FirstDiff(saveBefore, saveAfter)));
        }

        // ---------- 4. 맵 ----------
        private IEnumerator Maps()
        {
            if (!HasTab("map")) { Log("맵 탭 없음"); yield break; }
            _app.ShowMain("map");
            yield return Frames();
            yield return Shot("pt09_map_list");
            var catalog = SampleBattles.Catalog(Ctx.Defs, Ctx.Flow);
            int n = 0;
            foreach (var m in catalog)
            {
                _app.Show(new MapDetailScreen(m.MapId, "map"));
                yield return Frames();
                var def = MapDef.Load(Ctx.Data, m.MapId);
                var pic = FindByName("MapPicture") as RectTransform;
                string ratio = pic != null ? $"그림 {pic.rect.width:0}x{pic.rect.height:0} (비율 {pic.rect.width / Mathf.Max(1f, pic.rect.height):0.00})" : "그림 없음";
                Log($"맵 {m.MapId}: 칸 {def.Width}x{def.Height} (비율 {def.Width / (float)Mathf.Max(1, def.Height):0.00}), {ratio}, 스텝 {m.Steps.Count}개");
                if (n++ < 2) yield return Shot("pt10_map_detail_" + m.MapId);
            }
        }

        // ---------- 5·6·7. 전투 이동·일기토·탈것 ----------
        private IEnumerator BattleChecks(bool acceptRun)
        {
            var catalog = SampleBattles.Catalog(Ctx.Defs, Ctx.Flow);
            if (catalog.Count == 0) { Log("전투할 맵 없음"); yield break; }
            var screen = StartSample(catalog[0]);
            if (screen == null) yield break;
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("pt11_battle_start");
            var ctl = screen.DebugControl; var sim = screen.DebugSim;
            Log("군단: " + string.Join(", ", sim.Context.Corps.Select(c => c.Id + "(" + c.Team + (c.Setup.Role == CorpsRoles.Commander ? ", 총대장" : "") + ")")) + " — 탈것(말·가마)·자세는 캡처 pt11로 확인");

            // 일기토 신청 스킬이 모든 아군 장수에게 있는지
            foreach (var c in ctl.OwnCorps())
            {
                ctl.Select(c.Id);
                var sk = ctl.Skills().FirstOrDefault(s => s.SkillId == "challenge");
                Log($"스킬 메뉴 {c.Id}: 일기토 신청 {(sk == null ? "없음!" : "있음 (남은 횟수 " + sk.UsesLeft + ", 지금 쓸 수 " + sk.Affordable + ")")}");
            }
            screen.DebugSetSpeed(2); // ×4
            if (acceptRun) Log("이번 전투는 적의 일기토 신청에 [응한다]를 누른다");
            bool moved = acceptRun, attacked = acceptRun, skillTried = acceptRun, refused = acceptRun, accepted = false;
            float until = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < until && _app.CurrentScreen == screen)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                // 일기토 창 (신청 기회 / 적의 신청)
                var yes = VisibleButton(T("ui.duel.challenge")) ?? VisibleButton(T("ui.duel.accept"));
                if (yes != null)
                {
                    bool offer = LabelOf(yes) == T("ui.duel.accept");
                    yield return Shot(offer ? "pt14_duel_offer" : "pt13_duel_chance");
                    var mine = ctl.OwnCorps().FirstOrDefault();
                    if (offer && !refused)
                    {
                        var morale = ctl.OwnCorps().Select(c => c.Id + " " + c.Morale.FloorToLong()).ToList();
                        VisibleButton(T("ui.duel.refuse"))?.onClick.Invoke();
                        refused = true;
                        yield return new WaitForSecondsRealtime(1.5f);
                        Log("적의 일기토 신청 → [거절한다]. 사기 전: " + string.Join(", ", morale) + " / 후: " + string.Join(", ", ctl.OwnCorps().Select(c => c.Id + " " + c.Morale.FloorToLong())));
                        yield return Shot("pt15_after_refuse");
                    }
                    else
                    {
                        Log((offer ? "적의 신청 → [응한다]" : "마주침 → [일기토 신청]"));
                        yes.onClick.Invoke();
                        yield return new WaitForSecondsRealtime(2.5f);
                        Log("→ 화면 " + ScreenName());
                        if (_app.CurrentScreen is ContestScreen)
                        {
                            accepted = true;
                            yield return Shot("pt16_duel_screen");
                            yield return Press(T("ui.contest.auto"), 1f);
                            yield return Press(T("ui.duel.backToBattle"), 2f);
                            Log("[전투로 돌아가기] → 화면 " + ScreenName());
                            if (_app.CurrentScreen is BattleScreen b2) { screen = b2; ctl = b2.DebugControl; sim = b2.DebugSim; until = Time.realtimeSinceStartup + 40f; }
                        }
                    }
                    continue;
                }
                if (!(_app.CurrentScreen is BattleScreen)) break;

                // 교전 중인 아군을 뒤로 빼기 / 적 쪽으로 누르기 / 일기토 신청 스킬
                var engaged = ctl.OwnCorps().FirstOrDefault(c => c.Engaged || c.MeleeContact);
                if (engaged == null) continue;
                var enemy = sim.Context.Corps.Where(c => c.Team != ctl.Team && c.InBattle).OrderBy(c => Vec.DistanceSquared(c.Anchor, engaged.Anchor)).FirstOrDefault();
                if (enemy == null) continue;
                if (!moved)
                {
                    moved = true;
                    var dir = (engaged.Anchor - enemy.Anchor).Normalized();
                    var away = ClampToField(sim, engaged.Anchor + dir * Fix.FromInt(5));
                    if ((away - engaged.Anchor).Length < Fix.FromInt(2)) { away = ClampToField(sim, engaged.Anchor + dir.RightOf() * Fix.FromInt(5)); Log("뒤쪽이 맵 끝이라 옆쪽(적과 수직)으로 빼기를 시험"); }
                    var d0 = (engaged.Anchor - enemy.Anchor).Length;
                    yield return Shot("pt12_move_before");
                    ctl.Select(engaged.Id);
                    bool ok = ctl.MoveTo(away, MoveModes.Normal, false);
                    if (!ok) { away = ClampToField(sim, engaged.Anchor + dir.RightOf() * Fix.FromInt(4)); ok = ctl.MoveTo(away, MoveModes.Normal, false); Log("뒤쪽 지점이 거부되어 옆쪽 지점으로 다시 시도"); }
                    yield return new WaitForSecondsRealtime(2f);
                    var d1 = (engaged.Anchor - enemy.Anchor).Length;
                    Log($"교전 중 뒤쪽 이동 ({engaged.Id} {engaged.Anchor} → {away}, 적 {enemy.Id} {enemy.Anchor}, 맵 {sim.Context.Field.Width}x{sim.Context.Field.Height}): 명령 {(ok ? "받음" : "거부")}, 적과 거리 {d0.ToDisplayDouble():0.0} → {d1.ToDisplayDouble():0.0}칸, 교전 {engaged.Engaged}/접촉 {engaged.MeleeContact}");
                    yield return Shot("pt12_move_after");
                    continue;
                }
                if (!attacked)
                {
                    attacked = true;
                    ctl.Select(engaged.Id);
                    var r = ctl.Tap(enemy.Anchor);
                    yield return new WaitForSecondsRealtime(2f);
                    Log($"적 쪽 누름 ({engaged.Id} → {enemy.Id}): {r}, 거리 {(engaged.Anchor - enemy.Anchor).Length.ToDisplayDouble():0.0}칸, 교전 {engaged.Engaged}");
                    continue;
                }
                if (!skillTried)
                {
                    skillTried = true;
                    ctl.Select(engaged.Id);
                    bool pressed = ctl.PressSkill("challenge");
                    var r = pressed ? ctl.Tap(enemy.Anchor) : TapResult.None;
                    yield return new WaitForSecondsRealtime(1f);
                    var sk = ctl.Skills().FirstOrDefault(s => s.SkillId == "challenge");
                    Log($"일기토 신청 스킬 사용 ({engaged.Id} → {enemy.Id}): 누름 {pressed}, 결과 {r}, 다음 사용 가능 {(sk == null ? "?" : sk.Affordable.ToString())} (60초 쿨타임이면 False)");
                }
            }
            if (acceptRun) Log($"일기토 응하기: 대결 화면 {(accepted ? "봄" : "적의 신청이 없어 못 봄")}");
            else
            {
                Log($"일기토 거절 시험 {(refused ? "함" : "적의 신청이 없어 못 함")}");
                if (!moved) Log("교전 중인 아군을 못 찾아 이동 시험 못 함");
            }
            if (_app.CurrentScreen is BattleScreen) { yield return Shot("pt17_battle_late"); VisibleButton(T("ui.battle.skip"))?.onClick.Invoke(); }
            yield return new WaitForSecondsRealtime(1f);
        }

        // ---------- 8. 아이콘·ⓘ ----------
        private IEnumerator IconsAndInfo()
        {
            foreach (var tab in new[] { "general", "prisoner" })
            {
                if (!HasTab(tab)) continue;
                _app.ShowMain(tab);
                yield return Frames();
                yield return Shot("pt18_tab_" + tab);
                var info = FindByName("InfoButton")?.GetComponent<Button>();
                if (info == null) { Log($"탭 {tab}: ⓘ 버튼 없음"); continue; }
                info.onClick.Invoke();
                yield return Frames();
                yield return Shot("pt19_info_" + tab);
                info.onClick.Invoke(); // 다시 누르면 닫히는지
                yield return Frames();
                Log($"탭 {tab}: ⓘ 열기 → 닫기 시도. 닫힌 뒤 설명 카드 {(FindByName("InfoCard") != null ? "남아 있음" : "없음")}");
            }
            if (Ctx.State.Generals.Count > 0)
            {
                _app.Show(new GeneralScreen(Ctx.State.Generals[0].GeneralId));
                yield return Frames();
                yield return Shot("pt20_general_screen");
            }
            Log("아이콘 그림: " + string.Join(", ", new[] { "supply", "gold", "gem", "leadership", "might", "intellect" }.Select(id => id + "=" + (Resources.Load<Sprite>("Ui/icons/" + id) != null ? "그림" : "낙관"))));
        }

        // ---------- 9. 큰 전투 ----------
        private IEnumerator BigBattle()
        {
            var catalog = SampleBattles.Catalog(Ctx.Defs, Ctx.Flow);
            SampleMapEntry biggest = null; int area = 0;
            foreach (var m in catalog)
            {
                var d = MapDef.Load(Ctx.Data, m.MapId);
                if (d.Width * d.Height > area) { area = d.Width * d.Height; biggest = m; }
            }
            if (biggest == null) { Log("맵 없음"); yield break; }
            var screen = StartSample(biggest);
            if (screen == null) yield break;
            var sim = screen.DebugSim;
            long troops = sim.Context.Corps.Sum(c => c.Troops);
            Log($"큰 전투: 맵 {biggest.MapId}, 군단 {sim.Context.Corps.Count}개, 병력 합 {troops:N0}");
            yield return new WaitForSecondsRealtime(3f);
            screen.DebugSetSpeed(0);
            var frames = new List<float>();
            float end = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < end) { yield return null; frames.Add(Time.unscaledDeltaTime); }
            frames.Sort();
            float avg = frames.Average();
            float p95 = frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.95f))];
            Log($"FPS (에디터, ×1, 6초): 평균 {1f / avg:0.0}, 느린 5% 프레임 {p95 * 1000f:0}ms (= {1f / p95:0.0} FPS), 가장 느린 {frames[frames.Count - 1] * 1000f:0}ms, 프레임 {frames.Count}개");
            yield return Shot("pt21_big_battle");
            VisibleButton(T("ui.battle.skip"))?.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1f);
        }

        // ---------- PR #139: 진형 전환 ----------
        private IEnumerator Reform()
        {
            var screen = StartFirstSample(); if (screen == null) yield break;
            var ctl = screen.DebugControl; var sim = screen.DebugSim; var field = FieldOf(screen);
            screen.DebugSetSpeed(0);
            yield return new WaitForSecondsRealtime(1f);
            var c = ctl.OwnCorps().FirstOrDefault(x => x.Setup.KnownFormations != null && x.Setup.KnownFormations.Any(f => f != x.Formation?.Id));
            if (c == null) { Log("진형을 바꿀 수 있는 아군 군단 없음 (아는 진형이 하나뿐)"); yield break; }
            string to = c.Setup.KnownFormations.First(f => f != c.Formation?.Id);
            field.LookAt(c); field.Rig.SetDistance(14f);
            ctl.Select(c.Id);
            bool ok = ctl.ChangeFormation(to);
            yield return null; yield return null;
            Log($"진형 전환 {c.Id}: {c.Formation?.Id} → {to}, 명령 {(ok ? "받음" : "거부")}, 전환 {c.ReformTotalTicks}틱 ({c.ReformTotalTicks * sim.Context.Combat.TickSeconds.ToDisplayDouble():0.0}초)");
            yield return Shot("pt30_reform_start");
            float until = Time.realtimeSinceStartup + 12f;
            while (c.Reforming && c.ReformProgress < Fix.FromInt(1) / Fix.FromInt(2) && Time.realtimeSinceStartup < until) yield return null;
            yield return Shot("pt31_reform_mid");
            while (c.Reforming && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            Log($"전환 끝: 지금 진형 {c.Formation?.Id}, 전환 중 {c.Reforming}");
            yield return Shot("pt32_reform_end");
            VisibleButton(T("ui.battle.skip"))?.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1f);
        }

        // ---------- PR #139: 퇴각 선택 (+ 사기 막대·쓰러짐·일기토 알림) ----------
        private IEnumerator RetreatChoice(bool hold)
        {
            var screen = StartFirstSample(); if (screen == null) yield break;
            var ctl = screen.DebugControl; var field = FieldOf(screen);
            screen.DebugSetSpeed(2);
            string tag = hold ? "hold" : "retreat";
            bool asked = false, closeShot = false, duelNoted = false;
            float until = Time.realtimeSinceStartup + 200f, after = 0f;
            while (Time.realtimeSinceStartup < until && _app.CurrentScreen == screen)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                // 일기토 창: 위 알림 문구만 기록하고 거절해 넘어간다 (이 시험의 주제가 아님)
                var duelNo = VisibleButton(T("ui.duel.refuse")) ?? VisibleButton(T("ui.duel.pass"));
                if (duelNo != null)
                {
                    if (!duelNoted) { duelNoted = true; Log("일기토 창이 뜰 때 위 알림: " + string.Join(" / ", NoticeTexts())); yield return Shot("pt33_duel_notice"); }
                    duelNo.onClick.Invoke(); continue;
                }
                // 접전 확대 한 번 (사기 막대·쓰러지는 병사·시체)
                var engaged = ctl.OwnCorps().FirstOrDefault(x => x.MeleeContact);
                if (!closeShot && engaged != null)
                {
                    closeShot = true;
                    screen.DebugSetSpeed(0);
                    field.LookAt(engaged); field.Rig.SetDistance(12f);
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Shot("pt34_melee_close_" + tag);
                    field.Rig.SetDistance(20f);
                    yield return Shot("pt35_morale_bars_" + tag);
                    screen.DebugSetSpeed(2);
                }
                var holdB = VisibleButton(T("ui.retreatAsk.hold"));
                if (holdB != null && !asked)
                {
                    asked = true;
                    var who = ctl.OwnCorps().OrderBy(x => x.Morale).FirstOrDefault();
                    if (who != null) { field.LookAt(who); field.Rig.SetDistance(16f); }
                    yield return null;
                    Log($"퇴각 선택 창 뜸 ({who?.Id} 사기 {who?.Morale.FloorToLong()} 병력 {who?.Troops:N0})");
                    yield return Shot("pt36_retreat_prompt");
                    if (hold) holdB.onClick.Invoke(); else VisibleButton(T("ui.retreatAsk.retreat"))?.onClick.Invoke();
                    Log("누름: " + (hold ? "[버틴다]" : "[퇴각한다]"));
                    after = Time.realtimeSinceStartup;
                    continue;
                }
                if (asked)
                {
                    var hit = NoticeTexts().FirstOrDefault(t => t.Contains("패주") || t.Contains("퇴각한다"));
                    if (hit != null) { Log("알림: " + hit); yield return Shot("pt37_after_" + tag); break; }
                    if (Time.realtimeSinceStartup - after > 90f) { Log("누른 뒤 90초 안에 퇴각·패주 알림을 못 봄"); break; }
                }
            }
            if (!asked) Log("퇴각 선택 창이 안 뜸 (200초)");
            if (!closeShot) Log("접전 장면을 못 찍음");
            if (_app.CurrentScreen is BattleScreen) VisibleButton(T("ui.battle.skip"))?.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1f);
        }

        // ---------- 장수 자세 확대 ----------
        private IEnumerator PoseCloseups()
        {
            var screen = StartFirstSample(); if (screen == null) yield break;
            var sim = screen.DebugSim; var field = FieldOf(screen);
            yield return new WaitForSecondsRealtime(1f);
            SetPaused(screen, true);
            int n = 0;
            foreach (var c in sim.Context.Corps.Where(x => x.InBattle).ToList())
            {
                field.LookAt(c); field.Rig.SetDistance(Game.Unity.Battle3D.BattleCameraRig.MinDistance);
                yield return new WaitForSecondsRealtime(0.4f);
                string role = c.Setup.Role == CorpsRoles.Commander ? "총대장" : "장수";
                string name = $"pt40_pose_{n++}_{c.Id}";
                yield return Shot(name);
                Log($"자세 확대: {c.Id} ({c.Team}, {role}) → {name}");
            }
            SetPaused(screen, false);
            VisibleButton(T("ui.battle.skip"))?.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1f);
        }

        private BattleScreen StartFirstSample()
        {
            var catalog = SampleBattles.Catalog(Ctx.Defs, Ctx.Flow);
            if (catalog.Count == 0) { Log("전투할 맵 없음"); return null; }
            return StartSample(catalog[0]);
        }
        private static Game.Unity.Battle3D.BattleField3D FieldOf(BattleScreen s) =>
            (Game.Unity.Battle3D.BattleField3D)typeof(BattleScreen).GetField("_field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(s);
        private static void SetPaused(BattleScreen s, bool on) =>
            typeof(BattleScreen).GetField("_paused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(s, on);
        private IEnumerable<string> NoticeTexts() => Texts().Select(t => t.text).Where(t => !string.IsNullOrEmpty(t) && (t.Contains("군단") || t.Contains("일기토")));

        // ---------- 도움 ----------
        private static Vec ClampToField(BattleSimulation sim, Vec p)
        {
            Fix maxX = Fix.FromInt(sim.Context.Field.Width - 2), maxY = Fix.FromInt(sim.Context.Field.Height - 2), one = Fix.FromInt(1);
            Fix x = p.X < one ? one : p.X > maxX ? maxX : p.X;
            Fix y = p.Y < one ? one : p.Y > maxY ? maxY : p.Y;
            return new Vec(x, y);
        }

        private BattleScreen StartSample(SampleMapEntry map)
        {
            string err = SampleUi.Start(_app, map);
            if (err != null) { Log("샘플전투 시작 실패: " + err); return null; }
            return _app.CurrentScreen as BattleScreen;
        }

        private IEnumerator Guard(IEnumerator inner)
        {
            while (true)
            {
                object cur;
                try { if (!inner.MoveNext()) yield break; cur = inner.Current; }
                catch (Exception e) { Log("오류: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); yield break; }
                yield return cur;
            }
        }

        private void BackupSave()
        {
            try
            {
                string to = Path.Combine(_folder, "save_backup");
                Directory.CreateDirectory(to);
                foreach (var f in Directory.GetFiles(Application.persistentDataPath))
                    File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
                Log("세이브 백업: " + to + " (되돌리려면 이 파일들을 " + Application.persistentDataPath + " 에 다시 복사)");
            }
            catch (Exception e) { Log("세이브 백업 실패: " + e.Message); }
        }

        private IEnumerator Press(string label, float wait)
        {
            var b = VisibleButton(label);
            if (b == null) { Log($"버튼 '{label}' 없음 (화면 {ScreenName()})"); yield break; }
            b.onClick.Invoke();
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait); else yield return null;
        }

        private static IEnumerator Frames() { yield return null; yield return null; yield return null; }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, name + ".png"));
            yield return null;
            yield return null;
        }

        private void Log(string line) { _log.AppendLine(line); Debug.Log("[PlayTest] " + line); }

        private bool HasTab(string id) => Ctx.MainTabs.Any(t => t.Id == id && t.Status == MainTabEntry.Active);
        private int Unlocked() => Ctx.Defs.StepIndex.Order.Count(id => Ctx.Flow.IsUnlocked(Ctx.State, id));
        private string ScreenName() => _app.CurrentScreen?.GetType().Name ?? "(없음)";
        private Dictionary<string, long> Wallet() => new[] { "supply", "gold", "gem" }.ToDictionary(id => id, id => Ctx.Resource(id));
        private static string Fmt(Dictionary<string, long> w) => string.Join(" ", w.Select(kv => kv.Key + " " + kv.Value.ToString("N0")));

        private static string FirstDiff(string a, string b)
        {
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            int s = Math.Max(0, i - 60);
            return $"(길이 {a.Length}→{b.Length}, 처음 다른 곳: …{a.Substring(s, Math.Min(120, a.Length - s))}… / …{b.Substring(s, Math.Min(120, b.Length - s))}…)";
        }

        private static string LabelOf(Button b) => b.GetComponentInChildren<Text>(true)?.text ?? "";
        private static IEnumerable<Button> AllButtons() => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled);
        private static IEnumerable<Text> Texts() => UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled);
        private static Button VisibleButton(string label) => AllButtons().FirstOrDefault(b => LabelOf(b) == label && b.interactable);
        private static Button FindButton(string label, Transform under) => under == null ? null : under.GetComponentsInChildren<Button>(true).FirstOrDefault(b => LabelOf(b) == label);

        private static Transform FindByName(string name)
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
                if (t.name == name && t.gameObject.activeInHierarchy) return t;
            return null;
        }
    }
}
