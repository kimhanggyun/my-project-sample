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
