using System;
using Game.Foundation.Save;
using Game.Foundation.Time;
using Game.Unity.Screens;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.Unity.Boot
{
    /// <summary>
    /// 게임 시작점이자 조립 지점 (Boot 씬에 하나). 데이터·세이브·서비스를 준비하고(<see cref="GameContext"/>), 화면을 바꿔 끼운다.
    /// 화면은 지금 코드로 만든 uGUI다 (뼈대 단계). Boot 씬은 메뉴 Game ▸ Setup Boot Scene으로 만든다.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public const float ReferenceWidth = 1080f;
        public const float ReferenceHeight = 1920f;

        public GameContext Context { get; private set; }

        private RectTransform _screenRoot;
        private Image _backdrop;
        private GameScreen _current;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            BuildCanvas();
            Boot();
        }

        /// <summary>데이터·세이브·서비스를 다시 준비하고 시작 순서부터 (설정 화면에서 서버 연결을 바꾼 뒤).</summary>
        public void Reboot() => Boot();

        private void Boot()
        {
            try
            {
                var store = new FileSaveStore(Application.persistentDataPath);
                // 화면 스타일 (UI 1단계 임시): 개발 빌드는 설정에서 고른 것, 아니면 데이터의 기본
                UiStyle.Apply(GameDataPaths.Data(), Debug.isDebugBuild && store.Exists(UiStyle.OverrideKey) ? store.Read(UiStyle.OverrideKey) : null);
                if (_backdrop != null) _backdrop.color = Palette.Background;
                Context = GameContext.Create(
                    GameDataPaths.Data(),
                    GameDataPaths.Config(),
                    store,
                    new SystemTimeProvider(),
                    GameDataPaths.ServiceConfigFile,
                    url => new Game.Unity.Online.UnityWebTransport(this, url),
                    () => new Game.Unity.Online.UnityLocationService(this));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Show(new ErrorScreen("데이터를 불러오지 못했습니다.\n" + e.Message));
                return;
            }
            NextStartup(null);
        }

        /// <summary>
        /// 앱 시작 순서의 다음 화면 (startup.md, data Ui/startup.json): 스플래시 → (버전 확인) → 로그인 → 약관 → 타이틀.
        /// 지금 단계를 마친 화면이 부른다. 로그인 단계를 건너뛰면(전에 쓴 수단) 자동으로 로그인한다.
        /// </summary>
        public void NextStartup(string finished)
        {
            var ctx = Context;
            string step = ctx.Startup.Next(finished, ctx.StartupRecord, false); // 버전 확인 서버는 아직 없다
            if (step != Game.Flow.StartupSteps.SignIn && ctx.StartupRecord.SignInMethod != null && !ctx.Auth.IsSignedIn)
                ctx.SignIn(ctx.StartupRecord.SignInMethod, ok => Debug.Log("자동 로그인: " + (ok ? ctx.Auth.AccountId : "실패")));
            switch (step)
            {
                case Game.Flow.StartupSteps.Splash: Show(new SplashScreen()); break;
                case Game.Flow.StartupSteps.VersionCheck: NextStartup(step); break;
                case Game.Flow.StartupSteps.SignIn: Show(new SignInScreen()); break;
                case Game.Flow.StartupSteps.Terms: Show(new TermsScreen()); break;
                default: Show(new TitleScreen()); break;
            }
        }

        private void OnApplicationPause(bool paused) => ForwardPause(paused);
        private void OnApplicationQuit() => ForwardPause(true);

        private void ForwardPause(bool paused)
        {
            if (_current == null) return;
            try { _current.OnAppPause(paused); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>타이틀 다음: 하다 만 전투가 있으면 이어하기 물음, 없으면 메인 (R-55).</summary>
        public void EnterGame()
        {
            if (Context.SuspendedBattle != null) Show(new ResumeBattleScreen());
            else ShowMain();
        }

        private void Update()
        {
            if (_current == null) return;
            try { _current.Tick(Time.unscaledDeltaTime); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Show(new ErrorScreen("화면 처리 중 오류가 났습니다.\n" + e.Message));
            }
        }

        /// <summary>지금 화면을 닫고 새 화면을 연다.</summary>
        public void Show(GameScreen screen)
        {
            if (_current != null)
            {
                try { _current.Close(); } catch (Exception e) { Debug.LogException(e); }
            }
            SetBackdrop(true);
            Ui.Clear(_screenRoot);
            var root = Ui.Panel(_screenRoot, screen.GetType().Name);
            _current = screen;
            screen.Attach(this, root);
            try { screen.Build(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (!(screen is ErrorScreen)) Show(new ErrorScreen("화면을 만들지 못했습니다.\n" + e.Message));
            }
        }

        /// <summary>화면 전체 배경. 3D 전장처럼 카메라 화면을 보여줄 때 끈다 (화면을 닫을 때 다시 켠다).</summary>
        public void SetBackdrop(bool on) { if (_backdrop != null) _backdrop.enabled = on; }

        /// <summary>개발용: 주요 화면을 차례로 열어 PNG로 저장한다 (<see cref="ScreenTour"/>).</summary>
        public ScreenTour StartScreenTour(string folder)
        {
            var tour = new ScreenTour(this, folder);
            StartCoroutine(tour.Run());
            return tour;
        }

        public void ShowTitle() => Show(new TitleScreen());
        public void ShowMain(string tabId = null) => Show(new MainScreen(tabId));

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var bg = Ui.Panel(canvasGo.transform, "Background", Palette.Background);
            _backdrop = bg.GetComponent<Image>();
            _backdrop.raycastTarget = false;
            var safe = Ui.Panel(canvasGo.transform, "SafeArea");
            safe.gameObject.AddComponent<SafeAreaFitter>();
            _screenRoot = safe;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                var input = es.AddComponent<InputSystemUIInputModule>();
                input.AssignDefaultActions();
            }
        }
    }
}
