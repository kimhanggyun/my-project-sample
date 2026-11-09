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

        /// <summary>앱이 뒤로 가거나(true) 다시 앞으로 올 때(false). 전투 화면은 멈추고 이어하기 기록을 남긴다.</summary>
        public virtual void OnAppPause(bool paused) { }

        /// <summary>화면이 닫힐 때.</summary>
        public virtual void Close() { }

        protected string T(string key) => Ctx.T(key);
        protected string T(string key, params object[] args) => Ctx.T(key, args);
    }
}
