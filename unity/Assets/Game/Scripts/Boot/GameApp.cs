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
            try
            {
                Context = GameContext.Create(
                    GameDataPaths.Data(),
                    GameDataPaths.Config(),
                    new FileSaveStore(Application.persistentDataPath),
                    new SystemTimeProvider(),
                    GameDataPaths.ServiceConfigFile,
                    url => new Game.Unity.Online.UnityWebTransport(this, url));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Show(new ErrorScreen("데이터를 불러오지 못했습니다.\n" + e.Message));
                return;
            }
            Context.Auth.SignInAutomatically(ok => Debug.Log("게스트 로그인: " + (ok ? Context.Auth.AccountId : "실패")));
            Show(new TitleScreen());
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
