using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Battle.Model.Simulation;
using Game.Flow;
using Game.Unity.Screens;
using UnityEngine;

namespace Game.Unity.Boot
{
    /// <summary>
    /// 화면 캡처 둘러보기 (개발용, 에디터 메뉴 Game ▸ Capture All Screens): 주요 화면을 차례로 열어 PNG로 저장한다.
    /// 세이브는 바꾸지 않는다 (전투·결과는 세이브 사본으로 만들고, 이어하기 기록은 지운다). 해상도는 Game 뷰 크기 그대로.
    /// </summary>
    public sealed class ScreenTour
    {
        private readonly GameApp _app;
        private readonly string _folder;
        public string Folder => _folder;
        public bool Done { get; private set; }
        public List<string> Saved { get; } = new List<string>();

        public ScreenTour(GameApp app, string folder) { _app = app; _folder = folder; }

        private GameContext Ctx => _app.Context;

        public IEnumerator Run()
        {
            Directory.CreateDirectory(_folder);
            var shots = new List<(string name, Func<GameScreen> make, float wait)>
            {
                ("01_title", () => new TitleScreen(), 0f),
                ("02_signin", () => new SignInScreen(), 0f),
                ("03_terms", () => new TermsScreen(), 0f),
                ("03b_status_update", () => new AppStatusScreen(new AppStatusResult { UpdateRequired = true }, StartupSteps.VersionCheck), 0f), // 버전 · 점검 (R-118, 2026-11-16)
                ("03c_status_maintenance", () => new AppStatusScreen(new AppStatusResult { Maintenance = true, MessageKey = AppStatus.DefaultMaintenanceKey, MaintenanceEndsAtUnixSeconds = Ctx.Clock.UtcNow.ToUnixTimeSeconds() + 7200 }, StartupSteps.VersionCheck), 0f),
                ("04_main_story", () => new MainScreen("story"), 0f),
                ("05_main_general", () => new MainScreen("general"), 0f),
                ("06_main_troop", () => new MainScreen("troop"), 0f),
                ("06c_cosmetic", () => new Game.Unity.Screens.CosmeticScreen("infantry"), 0.3f), // 치장 1단계: 꾸미기 (기본)
                ("06d_cosmetic_try", () => Game.Unity.Screens.CosmeticScreen.Trying("cavalry", "armor_brown", "horse_navy"), 0.3f), // 갈색 갑옷 · 진남색 말 입어 보기
                ("07_main_formation", () => { Game.Unity.Screens.FormationTabView.BoardOpen = false; return new MainScreen("formation"); }, 0f),
                ("07b_formation_board", () => { Game.Unity.Screens.FormationTabView.BoardOpen = true; return new MainScreen("formation"); }, 0f),
                ("07c_parts", () => new MainScreen("parts"), 0f),
                ("08_main_territory", () => { Game.Unity.Screens.TerritoryTabView.Zoom = 1; Game.Unity.Screens.TerritoryTabView.PanM = Vector2.zero; return new MainScreen("territory"); }, 0f),
                ("08b_territory_mid", () => { Game.Unity.Screens.TerritoryTabView.Zoom = 3; return new MainScreen("territory"); }, 0f), // 지도 바탕: 중간 줌 (한강)
                ("08c_territory_far", () => { Game.Unity.Screens.TerritoryTabView.Zoom = 6; return new MainScreen("territory"); }, 0f), // 멀리 줌 (서해 · 반도)
                ("09_main_hiring", () => { Game.Unity.Screens.TerritoryTabView.Zoom = 1; HiringTabView.Tab = "hire"; return new MainScreen("hiring"); }, 2f), // 목록을 받아 온 뒤 찍기
                ("10_step", () => new StepScreen(FirstStep()), 0f),
            };
            if (Ctx.State.Units.Count > 0) shots.Insert(6, ("06b_troop_promote", () => { TroopGrowthTabView.PreparePromote(Ctx.State.Units[0].UnitId); return new MainScreen("troop"); }, 0f)); // 시안 06 승급 화면
            if (Ctx.State.Units.Count > 0) shots.Add(("07d_disband", () => new DisbandUnitScreen(Ctx.State.Units[0].UnitId), 0f)); // 공통 타이틀 (2026-11-16)
            if (Ctx.State.Generals.Count > 0) shots.Add(("11_general", () => new GeneralScreen(Ctx.State.Generals[0].GeneralId), 0f));
            shots.Add(("12_gacha", () => new MainScreen("gacha"), 0f)); // 모집 탭 (따로 열던 모집 화면은 없앰)
            shots.Add(("13_codex", () => new CodexScreen(), 0f));
            if (Ctx.State.FoundSecrets.Count > 0) shots.Add(("13c_codex_detail", () => new CodexScreen(null, Ctx.State.FoundSecrets[0]), 0f)); // [그 전투 보기 ›] (원격 #223)
            shots.Add(("09b_hiring_mine", () => { HiringTabView.Tab = "mine"; return new MainScreen("hiring"); }, 1f)); // 등록 제한 안내 (원격 #222)
            shots.Add(("14_replays", () => new ReplayListScreen(), 0f));
            shots.Add(("15_settings", () => new SettingsScreen(), 0f));
            // 캡처에 없던 화면 (2026-11-15 사용자 요청)
            shots.Add(("12b_gacha_rates", () => new GachaScreen("standard", null, true), 0f));
            shots.Add(("12d_gacha_in_tab", () => { ScreenTabView.Hand("gacha", new GachaScreen("limited_winter")); return new MainScreen("gacha"); }, 0f)); // 탭 안에서 배너 바꾸기 = 아래 탭 유지 (2026-11-20)
            shots.Add(("12c_gacha_limited", () => new GachaScreen("limited_winter"), 0f)); // 기간 배너 꼬리표 (R-121, 예시 배너가 열려 있을 때)
            shots.Add(("13b_codex_generals", () => { CodexScreen.Tab = "generals"; return new CodexScreen(); }, 0f));
            shots.Add(("14b_replays_shared", () => { ReplayListScreen.Tab = "shared"; return new ReplayListScreen(); }, 1f));
            shots.Add(("15b_profile", () => new ProfileScreen(), 0.5f));
            shots.Add(("22_main_practice", () => new MainScreen("practice"), 0f));
            shots.Add(("23_main_sample", () => new MainScreen("sampleBattle"), 0f));
            shots.Add(("24_main_prisoner", () => new MainScreen("prisoner"), 0f));
            shots.Add(("25_mail", () => new MailScreen(null, null), 1f));
            shots.Add(("26_main_alliance", () => new MainScreen("alliance"), 1f));
            shots.Add(("27_main_shop", () => { ShopTabView.Tab = ShopTabView.Gold; return new MainScreen("shop"); }, 0f));
            shots.Add(("27b_shop_gem", () => { ShopTabView.Tab = ShopTabView.Gem; return new MainScreen("shop"); }, 0f)); // 상점 위 탭 (2026-11-16)
            shots.Add(("27c_shop_cash", () => { ShopTabView.Tab = ShopTabView.Cash; return new MainScreen("shop"); }, 0f));
            shots.Add(("28_main_pvp", () => new MainScreen("pvp"), 0f));

            System.Action endLoading = null;
            shots.Add(("29_loading", () => { endLoading = Game.Unity.UI.Loading.Begin(); return new MainScreen("territory"); }, 0.6f)); // 공통 로딩 표시 (H5): 0.25초 뒤 보임
            foreach (var s in shots) yield return Shot(s.name, s.make, s.wait);
            endLoading?.Invoke();
            CodexScreen.Tab = "secrets";
            ReplayListScreen.Tab = "mine";

            // 전투: 세이브는 그대로 두고 출진 준비만 해서 몇 초 돌린 화면
            var plan = Ctx.Flow.Prepare(Ctx.State, new SortieRequest { StepId = FirstStep(), Seed = 5 });
            if (plan.Ok)
            {
                if (plan.Setup.Contests.Count > 0) yield return Shot("16_contest", () => new ContestScreen(plan, 0), 0f);
                yield return Shot("17_dialogue", () => new DialogueScreen(plan, "dialogue." + plan.Step.Id + ".start"), 0f);
                // 안내 팝업을 띄우지 않고 (전투가 멈추지 않게) 시작·진행 중을 찍는다. 세이브의 "본 안내"는 그대로
                Game.Unity.UI.GuideOverlay.Suppressed = true;
                var battle = new BattleScreen(plan);
                yield return Shot("18_battle_start", () => battle, 0.5f); // 승패 조건 카드
                battle.CloseConditionsForTour();
                yield return Shot("18b_battle_plan", null, 0.3f); // 멈춘 채 작전 시간 (명령 줄)
                battle.OpenMenuForTour("attack");
                yield return Shot("18c_battle_attack", null, 0.3f); // 공격 › 하위 명령 (흰 동그라미)
                battle.OpenMenuForTour("root");
                battle.BeginBattleForTour();
                yield return Shot("19_battle_3s", null, 3f); // 같은 전투를 3초 더 (진행 중)
                // 움직임 확인 (2026-11-21): 0.1초 간격 연속 프레임 — 행군 (고른 군단 가까이) · 맞붙음 (근접이 닿은 곳)
                float waited = 0f;
                while (!battle.FocusMovingForTour() && waited < 30f) { battle.RunForTour(2); waited += Time.unscaledDeltaTime; yield return null; }
                battle.RunForTour(0);
                for (int i = 0; i < 10; i++) yield return Frame($"19w_walk_{i:00}", battle); // 카메라는 고정 (따라가면 카메라가 흔들림을 만든다)
                battle.RunForTour(2);
                waited = 0f;
                while (!battle.FocusMeleeForTour() && waited < 60f) { battle.RunForTour(2); waited += Time.unscaledDeltaTime; yield return null; }
                battle.RunForTour(0);
                yield return Shot("19m_fight_wide", null, 0.5f);
                battle.FocusMeleeForTour();
                for (int i = 0; i < 12; i++) yield return Frame($"19m_fight_{i:00}", battle);
                Game.Unity.UI.GuideOverlay.Suppressed = false;
                Ctx.ClearSuspendedBattle();

                // 결과: 세이브 사본에 반영한 보고서로
                var copy = SaveJson.Read(SaveJson.Write(Ctx.State));
                var plan2 = Ctx.Flow.Prepare(copy, new SortieRequest { StepId = FirstStep(), Seed = 5, Delegate = true });
                if (plan2.Ok)
                {
                    var result = BattleSimulation.Run(plan2.Setup);
                    var report = Ctx.Flow.ApplyResult(copy, plan2, result);
                    yield return Shot("20_result", () => new ResultScreen(plan2, result, report), 0f);
                }
                // 이긴 결과도 (시드를 바꿔 몇 번, 세이브 사본)
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    var c = SaveJson.Read(SaveJson.Write(Ctx.State));
                    var p = Ctx.Flow.Prepare(c, new SortieRequest { StepId = FirstStep(), Seed = seed, Delegate = true });
                    if (!p.Ok) break;
                    var res = BattleSimulation.Run(p.Setup);
                    if (res.Winner != StoryFlow.PlayerTeam) continue;
                    var rep = Ctx.Flow.ApplyResult(c, p, res);
                    yield return Shot("20b_result_win", () => new ResultScreen(p, res, rep), 0f);
                    break;
                }
            }

            // 샘플전투2 (R-125, 2026-11-21): 병사 단위 전투 — 시작 화면, 맞붙는 곳 0.1초(1틱) 간격 연속 프레임, 중간 · 끝, V자 vs 두툼
            var s2 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup
            {
                AllyFormation = Game.Battle.Model.Crowd.CrowdFormations.Wedge, EnemyFormation = Game.Battle.Model.Crowd.CrowdFormations.Line, Seed = 11,
            });
            yield return Shot("30_sample2_setup", () => s2, 0.3f);
            // 걷기 (다리): 다가가는 우리 쐐기 앞쪽을 가까이, 1틱 간격
            s2.StepForTour(40);
            s2.FocusArmyForTour(0, 9f);
            for (int i = 0; i < 8; i++) { s2.StepForTour(1); yield return Shot($"30w_sample2_walk_{i:00}", null, 0f); }
            int guard = 0;
            while (guard++ < 400) { s2.StepForTour(5); if (s2.FightingForTour) break; }
            s2.StepForTour(10);
            s2.FocusFightForTour(14f);
            yield return Shot("30b_sample2_contact", null, 0.2f);
            for (int i = 0; i < 12; i++) { s2.StepForTour(1); yield return Shot($"30f_sample2_{i:00}", null, 0f); }
            s2.StepForTour(60);
            s2.FocusFightForTour(22f);
            yield return Shot("30c_sample2_mid", null, 0.2f);
            s2.StepForTour(2000);
            yield return Shot("30d_sample2_end", null, 0.2f);
            var s3 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup
            {
                AllyFormation = Game.Battle.Model.Crowd.CrowdFormations.Vee, EnemyFormation = Game.Battle.Model.Crowd.CrowdFormations.Thick, Seed = 11,
            });
            yield return Shot("31_sample2_vee_setup", () => s3, 0.3f);
            guard = 0;
            while (guard++ < 400) { s3.StepForTour(5); if (s3.FightingForTour) break; }
            s3.StepForTour(60);
            s3.FocusFightForTour(22f);
            yield return Shot("31b_sample2_vee_fight", null, 0.2f);
            // 조작 · 돌격 · 부딪침 (R-126): 셋 대 셋, 중군은 적 중군 공격, 좌 · 우군은 바깥으로 돌아 들어감
            var s4 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup
            {
                AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false, Seed = 11,
                AllyFormation = Game.Battle.Model.Crowd.CrowdFormations.Wedge, EnemyFormation = Game.Battle.Model.Crowd.CrowdFormations.Line,
            });
            yield return Shot("32_sample2_corps_setup", () => s4, 0.3f);
            s4.StepForTour(1);
            s4.AttackForTour("center", "center");
            s4.MoveForTour("left", 4f, 34f);
            s4.MoveForTour("right", 56f, 34f);
            s4.SelectForTour("center");
            s4.StepForTour(60);
            s4.FocusArmyForTour(0, 40f);
            yield return Shot("32b_sample2_corps_move", null, 0.2f);
            s4.AttackForTour("left", "left");
            s4.AttackForTour("right", "right");
            guard = 0;
            while (guard++ < 600 && !s4.ImpactForTour) s4.StepForTour(1);
            s4.FocusFightForTour(12f);
            for (int i = 0; i < 8; i++) { yield return Shot($"32i_sample2_impact_{i:00}", null, 0f); s4.StepForTour(1); }
            s4.StepForTour(80);
            s4.FocusArmyForTour(0, 40f);
            yield return Shot("32c_sample2_corps_fight", null, 0.2f);
            // [전체] 고르기 · 땅 누르기: 우리 부대마다 노란 고리, 땅마다 흰 목표 고리 (2026-11-21 사용자)
            var s5 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false, Seed = 11 });
            yield return Shot("33_sample2_all_setup", () => s5, 0.2f);
            s5.StepForTour(1);
            s5.SelectAllAndTapForTour(30f, 28f);
            s5.StepForTour(10);
            s5.FocusArmyForTour(0, 38f);
            yield return Shot("33b_sample2_all_move", null, 0.2f);
            // 말 탄 총대장이 달리는 모습 (말 다리 넷, 2026-11-21 사용자) — 1틱 간격, 카메라가 따라감
            for (int i = 0; i < 6; i++) { s5.StepForTour(1); s5.FocusGeneralForTour(5f); yield return Shot($"33h_sample2_horse_{i:00}", null, 0f); }
            // 병종 · 상성 (R-127): 기병 돌격 → 창병 (받아치기), 궁병 화살, 기병 말 다리
            var s6 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup
            {
                AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false, EnemyAdvance = true, Seed = 12,
                AllyTroops = SampleBattle2Screen.Preset("mixed"), EnemyTroops = new[] { "spearman", "infantry", "archer" },
                AllyFormation = Game.Battle.Model.Crowd.CrowdFormations.Thick, EnemyFormation = Game.Battle.Model.Crowd.CrowdFormations.Line,
            });
            yield return Shot("34_sample2_troops_setup", () => s6, 0.2f);
            s6.StepForTour(1);
            s6.AttackForTour("left", "left");
            s6.StepForTour(50);
            s6.FocusArmyForTour(0, 30f);
            yield return Shot("34b_sample2_troops_arrows", null, 0.2f);
            for (int i = 0; i < 6; i++) { s6.StepForTour(1); yield return Shot($"34c_sample2_cavalry_{i:00}", null, 0f); }
            s6.StepForTour(60);
            s6.FocusFightForTour(18f);
            yield return Shot("34d_sample2_troops_fight", null, 0.2f);
            // 사기 · 퇴각 (R-128): V자로 두툼을 감싸 무너뜨리고, 무너진 적이 도망치는 모습
            var s7 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup
            {
                AllyFormation = Game.Battle.Model.Crowd.CrowdFormations.Vee, EnemyFormation = Game.Battle.Model.Crowd.CrowdFormations.Thick, Seed = 1,
            });
            yield return Shot("35_sample2_rout_setup", () => s7, 0.2f);
            guard = 0;
            while (guard++ < 600 && !s7.EnemyRoutedForTour) s7.StepForTour(1);
            s7.FocusFightForTour(24f);
            yield return Shot("35b_sample2_rout_start", null, 0.2f);
            s7.StepForTour(25);
            yield return Shot("35c_sample2_rout_flee", null, 0.2f);
            // 조작 화면 (R-129): 이름표 · 미니맵 · 부대 원형 메뉴 · 예약 이동
            var s8 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false, Seed = 11,
                AllyTroops = SampleBattle2Screen.Preset("mixed"), EnemyTroops = SampleBattle2Screen.Preset("mixed") });
            yield return Shot("36_sample2_ui_setup", () => s8, 0.2f);
            s8.StepForTour(1);
            s8.FocusArmyForTour(0, 34f);
            s8.OpenCommandForTour("left", "move"); // 조작 화면 (R-132): 원형 메뉴 대신 아래 명령 줄 · 세부 줄
            yield return Shot("36b_sample2_ui_menu", null, 0.4f);
            s8.QueueForTour("left", new[] { 8f, 26f, 8f, 34f, 18f, 38f });
            s8.StepForTour(20);
            yield return Shot("36c_sample2_ui_queue", null, 0.3f);
            // 스킬 (R-130): 우군 궁병 화살비 (전조 · 떨어짐), 중군 돌격 함성
            var s9 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false, EnemyAdvance = true, Seed = 11,
                AllyTroops = SampleBattle2Screen.Preset("mixed"), EnemyTroops = SampleBattle2Screen.Preset("mixed") });
            yield return Shot("37_sample2_skill_setup", () => s9, 0.2f);
            // 다가오는 적이 사거리(14칸) 안에 들 때까지 기다렸다가 화살비
            bool rained = false;
            Vector2 rainAt = Vector2.zero;
            for (int w = 0; w < 60 && !rained; w++)
            {
                s9.StepForTour(5);
                rainAt = s9.EnemyNearForTour("right");
                rained = s9.SkillForTour("right", Game.Battle.Model.Crowd.CrowdBattle.ArrowRain, rainAt.x, rainAt.y);
            }
            s9.SkillForTour("center", Game.Battle.Model.Crowd.CrowdBattle.WarCry);
            s9.StepForTour(4);
            s9.FocusPointForTour(rainAt.x, rainAt.y - 5f, 22f); // 화살비 떨어질 곳 (붉은 고리 · 날아가는 화살)
            yield return Shot(rained ? "37b_sample2_skill_rain" : "37b_sample2_skill_rain_outOfRange", null, 0.2f);
            s9.StepForTour(12);
            yield return Shot("37c_sample2_skill_landed", null, 0.2f);
            // 테스트 탭 (R-131): 3×3 칸 설정 화면, 그 설정으로 전투
            yield return Shot("38_test_tab", () => new MainScreen("test"), 0.3f);
            var s10 = new SampleBattle2Screen(TestBattleTabView.Setup(_app.Context), startNow: true, returnTab: "test");
            yield return Shot("38b_test_battle", () => s10, 0.3f);
            s10.StepForTour(60);
            s10.FocusArmyForTour(0, 36f);
            yield return Shot("38c_test_battle_moving", null, 0.2f);
            // 조작 화면 (R-132): ① 부대 줄 · ② 명령 줄 · ③ 세부 줄, 경로 그리기, 위임 · 근접 공격 · 후퇴 · 일시정지 · 끝 창
            var s11 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyCorps = 3, EnemyCorps = 3, AllyAdvance = false, EnemyAdvance = false, Seed = 11,
                AllyTroops = SampleBattle2Screen.Preset("mixed"), EnemyTroops = SampleBattle2Screen.Preset("mixed") }, startNow: true);
            yield return Shot("39_ctl_open", () => s11, 0.3f); // 화면을 먼저 띄운 뒤 카메라 (띄우기 전엔 전장이 없음)
            s11.FocusArmyForTour(0, 46f);
            yield return Shot("39_ctl_all", null, 0.2f);
            s11.StepForTour(1);
            s11.FocusArmyForTour(0, 46f);
            s11.OpenCommandForTour("right", "formation");
            yield return Shot("39b_ctl_formation", null, 0.2f);
            s11.OpenCommandForTour("right", "skill");
            yield return Shot("39c_ctl_skill", null, 0.2f);
            s11.OpenCommandForTour("left", "attack");
            yield return Shot("39d_ctl_attack", null, 0.2f);
            var foe = s11.EnemyNearForTour("left");
            s11.FocusPointForTour(14f, 30f, 40f); // 그린 길이 보이게
            s11.DrawPathForTour("left", new[] { 14f, 22f, 6f, 28f, 5f, 36f, foe.x, foe.y }, false);
            yield return Shot("39e_ctl_path_drawing", null, 0.2f);
            s11.ReleasePathForTour();
            s11.StepForTour(40);
            yield return Shot("39f_ctl_path_moving", null, 0.2f);
            s11.PressForTour("center", "delegate");
            s11.PressForTour("right", "nearest");
            s11.StepForTour(80);
            s11.SelectForTour("center");
            s11.FocusArmyForTour(0, 40f);
            yield return Shot("39g_ctl_delegate_nearest", null, 0.2f);
            s11.PressForTour(null, "retreat");
            s11.StepForTour(25);
            s11.FocusArmyForTour(0, 46f);
            yield return Shot("39h_ctl_retreat", null, 0.2f);
            s11.PauseForTour(true);
            yield return Shot("39i_ctl_paused", null, 0.2f);
            s11.PauseForTour(false);
            s11.PressForTour(null, "delegate");
            for (int w = 0; w < 40 && !s11.OverForTour; w++) s11.StepForTour(100);
            yield return Shot("39j_ctl_result", null, 0.4f);
            // 지형 (R-133): 맵마다 전체 · 가까이 · 진군 뒤
            yield return Shot("40_test_tab_maps", () => new MainScreen("test"), 0.3f);
            yield return Shot("40b_map_view_river", () => new MapViewScreen("crowd_river"), 0.6f); // 맵 보기 (부대 없이)
            yield return Shot("40c_map_view_hills", () => new MapViewScreen("crowd_highland"), 0.6f);
            string[] maps = { "crowd_highland", "crowd_gorge", "crowd_river", "crowd_marsh", "crowd_forest", "crowd_pass" };
            for (int mi = 0; mi < maps.Length; mi++)
            {
                var units = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 0, Troop = "cavalry", Formation = "wedge" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 2, Troop = "spearman", Formation = "thick" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 2, Col = 1, Troop = "archer" },
                };
                var foes = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>();
                foreach (var u in units) foes.Add(new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = u.Row, Col = u.Col, Troop = u.Troop, Formation = u.Formation, Commander = u.Commander });
                var sm = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = units, EnemyUnits = foes, AllyAdvance = false, EnemyAdvance = true, Seed = 5,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, maps[mi]) }, startNow: true);
                string tag = $"41{(char)('a' + mi)}_map_{maps[mi]}";
                yield return Shot(tag + "_0open", () => sm, 0.5f);
                sm.StepForTour(1);
                sm.FocusPointForTour(30f, 50f, 70f);
                yield return Shot(tag + "_1whole", null, 0.3f);
                sm.FocusArmyForTour(0, 22f);
                yield return Shot(tag + "_2near", null, 0.3f);
                sm.PressForTour(null, "advance");
                sm.StepForTour(260);
                sm.FocusArmyForTour(0, 34f);
                yield return Shot(tag + "_3advance", null, 0.3f);
                sm.FocusArmyForTour(0, 14f); // 가까이: 나무 · 바위 사이로 비켜 가는지 (R-134)
                yield return Shot(tag + "_4close", null, 0.3f);
                if (maps[mi] == "crowd_gorge" || maps[mi] == "crowd_forest")
                {
                    // 진형 그대로 이동 (막힌 병사는 지름길로, R-137) · 뒤 궁병은 [행군 이동] (R-136)
                    sm.MoveForTour("middle_center", 30f, 86f);
                    sm.MoveForTour("front_left", 30f, 86f);
                    sm.MoveForTour("front_right", 30f, 86f);
                    sm.MoveForTour("back_center", 30f, 86f, march: true);
                    sm.StepForTour(120);
                    sm.FocusArmyForTour(0, 30f);
                    yield return Shot(tag + "_5move", null, 0.3f);
                    sm.StepForTour(200);
                    sm.FocusArmyForTour(0, 30f);
                    yield return Shot(tag + "_6move_later", null, 0.3f);
                }
            }

            // 장수 성향 (R-138): 왼쪽 맹장(선봉) · 가운데 장수 · 오른쪽 책사(멀리 뒤)
            {
                var gu = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 0, Troop = "infantry", General = "warrior" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", General = "standard", Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 2, Troop = "infantry", General = "strategist" },
                };
                var ge = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>();
                foreach (var u in gu) ge.Add(new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = u.Row, Col = u.Col, Troop = u.Troop, Commander = u.Commander });
                var sgn = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = gu, EnemyUnits = ge, AllyAdvance = false, EnemyAdvance = true, Seed = 9,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_plain") }, startNow: true);
                yield return Shot("42_generals_open", () => sgn, 0.4f);
                sgn.StepForTour(1);
                sgn.FocusArmyForTour(0, 52f); // 세 부대 모두: 맹장은 대열 앞, 책사는 멀리 뒤
                yield return Shot("42b_generals_roles", null, 0.3f);
                sgn.FocusPlaceGeneralForTour("front_left", 14f);
                yield return Shot("42b_generals_warrior", null, 0.3f);
                sgn.FocusPlaceGeneralForTour("front_right", 14f);
                yield return Shot("42b_generals_strategist", null, 0.3f);
                // R-144 · R-145: 맹장 체력 15% (물러남 30%) → 대열 안으로 빠졌다가 (맞지 않으면) 회복해 80%에 다시 앞줄 — 싸우기 전이라 끝까지 보임
                sgn.WoundGeneralForTour("front_left", 15);
                sgn.StepForTour(15);
                sgn.FocusPlaceGeneralForTour("front_left", 14f);
                yield return Shot("42e_generals_warrior_pulled_back", null, 0.3f);
                sgn.StepForTour(250);
                sgn.FocusPlaceGeneralForTour("front_left", 14f);
                yield return Shot("42e_generals_warrior_healed_front", null, 0.3f);
                sgn.FocusPlaceGeneralForTour("front_center", 16f);
                yield return Shot("42f_generals_standard_middle", null, 0.3f);
                sgn.PressForTour(null, "advance");
                for (int w = 0; w < 400 && !sgn.FightingForTour; w++) sgn.StepForTour(2);
                sgn.StepForTour(15);
                sgn.FocusPlaceGeneralForTour("front_left", 16f);
                yield return Shot("42c_generals_fight", null, 0.3f);
                // 물러나 지휘: 맹장 체력 55% → 앞줄 바로 뒤로 (적 쪽을 본 채), "지휘"
                sgn.WoundGeneralForTour("front_left", 15);
                for (int k = 0; k < 4; k++) { sgn.StepForTour(5); sgn.FocusPlaceGeneralForTour("front_left", 14f); yield return Shot($"42d_generals_commanding_{k}", null, 0.1f); }
            }

            // 공수 교대 (R-139): 고지전 수비 = 맵을 뒤집어 우리가 꼭대기, 적이 올라온다
            {
                var du = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "spearman", Formation = "thick", Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer", General = "strategist" },
                };
                var de = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 0, Troop = "infantry", General = "warrior" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 2, Troop = "infantry" },
                };
                var sd = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = du, EnemyUnits = de, AllyAdvance = false, EnemyAdvance = true, Seed = 13,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_highland", true) }, startNow: true);
                yield return Shot("43_defend_open", () => sd, 0.4f);
                sd.StepForTour(1);
                sd.FocusArmyForTour(0, 40f);
                yield return Shot("43b_defend_heights", null, 0.3f);
                sd.StepForTour(450);
                sd.FocusArmyForTour(0, 34f);
                yield return Shot("43c_defend_enemy_climbing", null, 0.3f);
                // R-143 높이: 비탈을 오르는 적은 느려 대열이 늘어지고, 꼭대기 궁병은 멀리서 쏜다
                sd.StepForTour(200);
                sd.FocusArmyForTour(1, 34f);
                yield return Shot("43d_height_climb_slow", null, 0.3f);
                sd.StepForTour(150); // 적이 꼭대기 궁병 사거리에 들어올 때
                sd.FocusArmyForTour(0, 40f);
                yield return Shot("43e_height_archers_far", null, 0.3f);
            }
            // 공성 (R-140): 성문 앞에 모여 치고, 부서지면 들어간다
            {
                var su = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 120, Commander = true, General = "warrior" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer" },
                };
                var se = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "spearman", Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 2, Col = 0, Troop = "archer" },
                };
                var ss = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = su, EnemyUnits = se, AllyAdvance = true, Seed = 17,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_siege") }, startNow: true);
                yield return Shot("44_siege_open", () => ss, 0.5f);
                ss.StepForTour(1);
                ss.FocusPointForTour(30f, 62f, 50f);
                yield return Shot("44b_siege_castle", null, 0.3f);
                ss.StepForTour(420);
                ss.FocusPointForTour(30f, 60f, 22f);
                yield return Shot("44c_siege_at_gate", null, 0.3f);
                for (int w = 0; w < 100 && !ss.GateOpenForTour; w++) ss.StepForTour(10);
                ss.StepForTour(80);
                ss.FocusPointForTour(30f, 66f, 26f);
                yield return Shot("44d_siege_inside", null, 0.3f);
                // R-142: 성문으로 들어가도 성벽 위로 올라서지 않음 (성벽 위는 계단으로만)
                ss.StepForTour(150);
                ss.FocusPointForTour(30f, 63f, 18f);
                yield return Shot("44e_siege_wall_after", null, 0.3f);
            }
            // 공성전 맵 3개 (원격 PR #282): 맵 보기 (성 전체) · 성문 앞 · 부서진 뒤
            foreach (var sm in new[] { "crowd_siege_plain", "crowd_siege_river", "crowd_siege_mountain" })
            {
                yield return Shot("45_" + sm + "_view", () => new MapViewScreen(sm), 0.6f);
                var su2 = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 120, Commander = true, General = "warrior" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer" },
                };
                var se2 = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "spearman", Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 2, Col = 0, Troop = "archer" },
                };
                var sb2 = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = su2, EnemyUnits = se2, AllyAdvance = true, Seed = 19,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, sm) }, startNow: true);
                yield return Shot("45_" + sm + "_0open", () => sb2, 0.5f);
                for (int w = 0; w < 120 && !sb2.GateHitForTour; w++) sb2.StepForTour(10);
                sb2.StepForTour(30);
                sb2.FocusGateForTour(22f);
                yield return Shot("45_" + sm + "_1gate", null, 0.3f);
                for (int w = 0; w < 200 && !sb2.GateOpenForTour; w++) sb2.StepForTour(10);
                sb2.StepForTour(80);
                sb2.FocusGateForTour(28f);
                yield return Shot("45_" + sm + "_2inside", null, 0.3f);
            }
            // R-142: 원진 맹장 = 원 가운데, 크게 다쳐도 부대가 버티면 "장수 부상" (맵 끝까지 안 감), 대기 중 진형 바꾸면 바로 바뀜
            {
                var cu = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 150, Formation = "circle", General = "warrior", Commander = true },
                };
                var ce = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 150, Commander = true },
                };
                var sc = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = cu, EnemyUnits = ce, AllyAdvance = false, EnemyAdvance = true, Seed = 23,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_plain") }, startNow: true);
                yield return Shot("46_circle_open", () => sc, 0.4f);
                sc.StepForTour(1);
                sc.FocusPlaceGeneralForTour("front_center", 22f);
                yield return Shot("46a_circle_general_middle", null, 0.3f);
                for (int w = 0; w < 400 && !sc.FightingForTour; w++) sc.StepForTour(2);
                sc.StepForTour(20);
                sc.FocusPlaceGeneralForTour("front_center", 22f);
                yield return Shot("46b_circle_fight", null, 0.3f);
                sc.WoundGeneralForTour("front_center", 15);
                for (int k = 0; k < 3; k++) { sc.StepForTour(30); sc.FocusPlaceGeneralForTour("front_center", 22f); yield return Shot($"46c_general_wounded_{k}", null, 0.1f); }

                var fu = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 120, Formation = "line", Commander = true },
                };
                var fe = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true },
                };
                var sf = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = fu, EnemyUnits = fe, AllyAdvance = false, EnemyAdvance = false, Seed = 29,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_plain") }, startNow: true);
                yield return Shot("46d_formation_hold", () => sf, 0.4f);
                sf.StepForTour(150);
                sf.FocusArmyForTour(0, 30f);
                yield return Shot("46d_formation_hold_line", null, 0.3f);
                sf.FormationForTour("front_center", "circle");
                for (int k = 0; k < 3; k++) { sf.StepForTour(20); sf.FocusArmyForTour(0, 30f); yield return Shot($"46e_formation_now_{k}", null, 0.1f); }
            }
            // 공성 명령 (R-145): 치는 쪽 — 충차 · 사다리 · 성벽 쏘기 · 돌입 대기, 지키는 적은 AI (궁병 · 창병 성벽, 보병 문 막기)
            {
                var au = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 120, Commander = true, General = "warrior" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman", Count = 90 },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 2, Troop = "cavalry", Count = 60 },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer", Count = 90, General = "strategist" },
                };
                var ae = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer" },
                };
                var sa = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = au, EnemyUnits = ae, AllyAdvance = false, EnemyAdvance = true, Seed = 31,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_siege") }, startNow: true);
                yield return Shot("47_siege_attack_open", () => sa, 0.5f);
                sa.SiegeForTour("front_center", "ram");
                sa.SiegeForTour("front_left", "ladder");
                sa.SiegeForTour("front_right", "waitBreach");
                sa.SiegeForTour("middle_center", "shootWall");
                sa.SelectForTour("front_left");
                yield return Shot("47a_siege_attack_commands", null, 0.3f);
                sa.StepForTour(350);
                sa.FocusLadderForTour(16f);
                yield return Shot("47b_siege_ladder", null, 0.3f);
                sa.StepForTour(60);
                sa.FocusLadderForTour(16f);
                yield return Shot("47b_siege_ladder_2", null, 0.3f);
                sa.FocusGateForTour(18f);
                yield return Shot("47c_siege_ram", null, 0.3f);
                sa.FocusGateForTour(34f);
                yield return Shot("47d_siege_walls", null, 0.3f);
                for (int w = 0; w < 300 && !sa.BreachedForTour && !sa.OverForTour; w++) sa.StepForTour(10);
                sa.StepForTour(40);
                sa.FocusGateForTour(26f);
                yield return Shot("47e_siege_breach_field_commands", null, 0.3f);
            }
            // 공성 명령 (R-145): 지키는 쪽 (맵을 뒤집어 우리가 성 안) — 성벽 배치 · 성벽 지키기 · 문 막기, 치는 적은 AI
            {
                var du2 = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer" },
                };
                var de2 = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "infantry", Count = 120, Commander = true },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 0, Troop = "spearman", Count = 90 },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Troop = "archer", Count = 90 },
                };
                var sdf = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = du2, EnemyUnits = de2, AllyAdvance = false, EnemyAdvance = true, Seed = 37,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_siege", true) }, startNow: true);
                yield return Shot("48_siege_defend_open", () => sdf, 0.5f);
                sdf.SiegeForTour("middle_center", "wall");
                sdf.SiegeForTour("front_left", "wall");
                sdf.SiegeForTour("front_center", "brace");
                yield return Shot("48a_siege_defend_commands", null, 0.3f);
                sdf.StepForTour(400);
                sdf.FocusGateForTour(30f);
                yield return Shot("48b_siege_defend_walls", null, 0.3f);
                sdf.StepForTour(200);
                sdf.FocusLadderForTour(16f);
                yield return Shot("48c_siege_defend_ladder", null, 0.3f);
                sdf.FocusGateForTour(18f);
                yield return Shot("48d_siege_defend_gate", null, 0.3f);
            }
            // 병사꾸미기 · 장수꾸미기 탭 (R-149 · R-150, 개발용): 부위 칸마다 번호 붙은 부품 → 샘플전투2 우리 편 · 장수 (투어가 바꾼 개발용 고르기는 끝나면 되돌림)
            {
                string keep = Game.Unity.Battle3D.DevLooks.Snapshot();
                Game.Unity.Battle3D.DevLooks.Restore("");
                foreach (var cat in LooksTabView.Categories)
                {
                    if (cat == "M") continue;
                    LooksTabView.ShowForTour("infantry", null, cat, null);
                    yield return Shot("50_slooks_infantry_" + cat, () => new MainScreen("looks"), 0.6f);
                }
                foreach (var (troop, cat) in new[] { ("cavalry", "M"), ("cavalry", "H"), ("archer", "B"), ("shieldbearer", "D"), ("spearman", "P") })
                {
                    LooksTabView.ShowForTour(troop, null, cat, null);
                    yield return Shot("50_slooks_" + troop + "_" + cat, () => new MainScreen("looks"), 0.6f);
                }
                foreach (var set in new[] { "armor_brown", "armor_iron", "armor_crimson", "armor_black", "armor_white", "armor_gold" })
                {
                    LooksTabView.PickForTour("infantry", set);
                    LooksTabView.ShowForTour("infantry", null, "set", null);
                    yield return Shot("50_slooks_set_" + set, () => new MainScreen("looks"), 0.6f);
                }
                Game.Unity.Battle3D.DevLooks.Restore("");
                foreach (var preset in LooksTabView.Presets)
                    foreach (var cat in new[] { "H", "A", "C", "S", "P", "M" })
                    {
                        if (preset != "warrior" && cat != "H") continue;
                        LooksTabView.ShowForTour(null, preset, null, cat);
                        yield return Shot("51_glooks_" + preset + "_" + cat, () => new MainScreen("generalLooks"), 0.6f);
                    }
                // 샘플전투2: 우리 보병 = 진홍 세트, 기병 = 기본, 창병 = 금테 / 장수 = 맹장 · 지장 · 책사 (말에 앉은 모습 가까이)
                LooksTabView.PickForTour("infantry", "armor_crimson");
                LooksTabView.PickForTour("spearman", "armor_gold");
                var lu = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 0, Troop = "infantry", Count = 60, General = "warrior" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "cavalry", Count = 40, Commander = true, General = "standard" },
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 2, Troop = "spearman", Count = 60, General = "strategist" },
                };
                var le = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec>
                {
                    new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 0, Col = 1, Troop = "heavyCavalry", Count = 60, Commander = true, General = "warrior" },
                };
                var sl = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup { AllyUnits = lu, EnemyUnits = le, AllyAdvance = false, EnemyAdvance = false, Seed = 41,
                    Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_plain") }, startNow: true);
                yield return Shot("52_looks_battle_open", () => sl, 0.5f);
                sl.StepForTour(1);
                sl.FocusArmyForTour(0, 22f);
                yield return Shot("52_looks_battle_close", null, 0.3f);
                sl.FocusPlaceGeneralForTour("front_center", 6f);
                yield return Shot("52_looks_battle_cavalry_general", null, 0.3f);
                sl.FocusPlaceGeneralForTour("front_left", 6f);
                yield return Shot("52_looks_battle_warrior", null, 0.3f);
                sl.FocusPlaceGeneralForTour("front_right", 6f);
                yield return Shot("52_looks_battle_strategist", null, 0.3f);
                sl.StepForTour(400);
                sl.FocusFightForTour(10f);
                yield return Shot("52_looks_battle_fight", null, 0.3f);
                Game.Unity.Battle3D.DevLooks.Restore(keep);
            }
            // 부대 하나면 [전체] 없음 (R-139)
            {
                var one = new SampleBattle2Screen(new Game.Battle.Model.Crowd.CrowdSetup
                {
                    AllyUnits = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec> { new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Commander = true } },
                    EnemyUnits = new System.Collections.Generic.List<Game.Battle.Model.Crowd.CrowdUnitSpec> { new Game.Battle.Model.Crowd.CrowdUnitSpec { Row = 1, Col = 1, Commander = true } },
                    AllyAdvance = false, Seed = 3, Terrain = TestBattleTabView.TerrainOf(_app.Context, "crowd_plain"),
                }, startNow: true);
                yield return Shot("43d_single_corps", () => one, 0.4f);
            }

            _app.ShowMain("story");
            Done = true;
            Debug.Log($"화면 캡처 {Saved.Count}장: {_folder}");
        }

        private string FirstStep()
        {
            string last = Ctx.Defs.StepIndex.Order[0];
            foreach (var id in Ctx.Defs.StepIndex.Order)
                if (Ctx.Flow.IsUnlocked(Ctx.State, id)) last = id;
            return last;
        }

        /// <summary>연속 프레임 한 장: 0.1초 뒤 찍는다 (멈춤이 걸려도 풀고, 카메라는 그대로).</summary>
        private IEnumerator Frame(string name, Game.Unity.Screens.BattleScreen battle)
        {
            float t = 0f;
            while (t < 0.1f) { battle.RunForTour(0); t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_folder, name + ".png");
            UnityEngine.ScreenCapture.CaptureScreenshot(path);
            Saved.Add(path);
        }

        private IEnumerator Shot(string name, Func<GameScreen> make, float waitSeconds)
        {
            if (make != null)
            {
                try { _app.Show(make()); }
                catch (Exception e) { Debug.LogWarning($"캡처 '{name}' 화면을 열지 못했습니다: {e.Message}"); yield break; }
            }
            yield return null; // 레이아웃
            yield return null;
            if (waitSeconds > 0f) yield return new WaitForSecondsRealtime(waitSeconds);
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_folder, name + ".png");
            UnityEngine.ScreenCapture.CaptureScreenshot(path);
            Saved.Add(path);
            yield return null; // 파일 쓰기
            yield return null;
        }
    }
}
