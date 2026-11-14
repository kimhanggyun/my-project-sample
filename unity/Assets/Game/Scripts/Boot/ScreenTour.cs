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
                ("04_main_story", () => new MainScreen("story"), 0f),
                ("05_main_general", () => new MainScreen("general"), 0f),
                ("06_main_troop", () => new MainScreen("troop"), 0f),
                ("07_main_formation", () => new MainScreen("formation"), 0f),
                ("08_main_territory", () => new MainScreen("territory"), 0f),
                ("09_main_hiring", () => new MainScreen("hiring"), 0f),
                ("10_step", () => new StepScreen(FirstStep()), 0f),
            };
            if (Ctx.State.Units.Count > 0) shots.Insert(6, ("06b_troop_promote", () => { TroopGrowthTabView.PreparePromote(Ctx.State.Units[0].UnitId); return new MainScreen("troop"); }, 0f)); // 시안 06 승급 화면
            if (Ctx.State.Generals.Count > 0) shots.Add(("11_general", () => new GeneralScreen(Ctx.State.Generals[0].GeneralId), 0f));
            shots.Add(("12_gacha", () => new GachaScreen(), 0f));
            shots.Add(("13_codex", () => new CodexScreen(), 0f));
            shots.Add(("14_replays", () => new ReplayListScreen(), 0f));
            shots.Add(("15_settings", () => new SettingsScreen(), 0f));

            foreach (var s in shots) yield return Shot(s.name, s.make, s.wait);

            // 전투: 세이브는 그대로 두고 출진 준비만 해서 몇 초 돌린 화면
            var plan = Ctx.Flow.Prepare(Ctx.State, new SortieRequest { StepId = FirstStep(), Seed = 5 });
            if (plan.Ok)
            {
                if (plan.Setup.Contests.Count > 0) yield return Shot("16_contest", () => new ContestScreen(plan, 0), 0f);
                yield return Shot("17_dialogue", () => new DialogueScreen(plan, "dialogue." + plan.Step.Id + ".start"), 0f);
                // 안내 팝업을 띄우지 않고 (전투가 멈추지 않게) 시작·진행 중을 찍는다. 세이브의 "본 안내"는 그대로
                Game.Unity.UI.GuideOverlay.Suppressed = true;
                yield return Shot("18_battle_start", () => new BattleScreen(plan), 0.5f);
                yield return Shot("19_battle_3s", null, 3f); // 같은 전투를 3초 더 (진행 중)
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
