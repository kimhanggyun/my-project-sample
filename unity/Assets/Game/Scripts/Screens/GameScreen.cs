using Game.Unity.Boot;
using UnityEngine;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 화면 하나. 화면은 보여주기와 입력만 맡고, 규칙 계산·상태 변경은 규칙 객체(<see cref="GameContext"/>)를 부른다.
    /// 새 화면은 이 클래스를 상속해 추가한다.
    /// </summary>
    public abstract class GameScreen
    {
        protected GameApp App { get; private set; }
        protected GameContext Ctx => App.Context;
        protected RectTransform Root { get; private set; }

        internal void Attach(GameApp app, RectTransform root)
        {
            App = app;
            Root = root;
        }

        /// <summary>Root 아래에 화면을 만든다.</summary>
        public abstract void Build();

        /// <summary>매 프레임 (필요한 화면만). deltaTime은 화면 진행용이며 규칙 계산에는 쓰지 않는다.</summary>
        public virtual void Tick(float deltaTime) { }

        /// <summary>
        /// 안드로이드 뒤로 키 (열린 판이 없을 때): 처리했으면 true. 기본 = 메인 화면으로.
        /// false면 앱 종료 확인 창 (메인 화면 · 시작 화면).
        /// </summary>
        public virtual bool OnBack()
        {
            App.ShowMain();
            return true;
        }

        /// <summary>앱이 뒤로 가거나(true) 다시 앞으로 올 때(false). 전투 화면은 멈추고 이어하기 기록을 남긴다.</summary>
        public virtual void OnAppPause(bool paused) { }

        /// <summary>화면이 닫힐 때.</summary>
        public virtual void Close() { }

        /// <summary>메인 탭 안에 끼워진 화면이면 그 탭 id (<see cref="ScreenTabView"/>가 정함), 따로 연 화면이면 null.</summary>
        public string HostTab { get; internal set; }

        /// <summary>
        /// 같은 자리에서 다시 그리기 (2026-11-20 사용자: 모집 배너 · 연합 만들기 등이 새 페이지로 넘어가 아래 탭 · 위 막대가 사라지던 것).
        /// 탭 안 화면이면 메인 화면 틀(위 막대 · 아래 탭)을 그대로 두고 그 탭 내용만 next로, 따로 연 화면이면 화면 전환.
        /// 다른 화면으로 가는 것(전투 · 결과 등)은 지금처럼 App.Show.
        /// </summary>
        protected void Replace(GameScreen next)
        {
            if (HostTab == null) { App.Show(next); return; }
            ScreenTabView.Hand(HostTab, next);
            App.ShowMain(HostTab);
        }

        protected string T(string key) => Ctx.T(key);
        protected string T(string key, params object[] args) => Ctx.T(key, args);
    }
}
