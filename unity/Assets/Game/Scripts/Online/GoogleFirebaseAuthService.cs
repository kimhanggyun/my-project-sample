using System;
using System.Text;
using System.Threading;
using Game.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Unity.Online
{
    /// <summary>
    /// 구글 로그인 (서비스 auth.google = "credentialManager", docs/impl/deploy-cloudrun.md 5장):
    /// ① 안드로이드 Credential Manager로 구글 ID 토큰 (플러그인 Plugins/Android/GoogleSignIn.androidlib)
    /// ② Firebase Auth REST(accounts:signInWithIdp)로 Firebase ID 토큰 → <see cref="IdToken"/>
    /// ③ GameContext가 서버(/v1/auth/firebase)에 보여 계정 토큰을 받는다.
    /// 자동 로그인은 이전에 승인한 계정으로 먼저 해 보고, 없으면 계정 고르기 창. 안드로이드 기기가 아니면(에디터) 가짜 로그인으로 대신한다.
    /// </summary>
    public sealed class GoogleFirebaseAuthService : IAuthService
    {
        public const string ImplName = "credentialManager";
        public const string ApiKeyKey = "firebaseApiKey";
        public const string WebClientIdKey = "googleWebClientId";
        private const string BridgeClass = "net.itgold.unitygame1.auth.GoogleSignInBridge";

        private readonly MonoBehaviour _host;
        private readonly string _apiKey;
        private readonly string _webClientId;

        public GoogleFirebaseAuthService(MonoBehaviour host, string apiKey, string webClientId)
        {
            _host = host;
            _apiKey = apiKey;
            _webClientId = webClientId;
        }

        public bool IsSignedIn { get; private set; }
        public string AccountId { get; private set; }
        public bool IsGuest => false;
        public string IdToken { get; private set; }
        /// <summary>마지막 실패 이유 (화면·로그용).</summary>
        public string LastError { get; private set; }

        public void SignInAutomatically(Action<bool> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_webClientId)) { Fail("설정에 firebaseApiKey·googleWebClientId가 없습니다.", done); return; }
            var main = SynchronizationContext.Current;
            GoogleIdToken(true, main, (token, error) =>
            {
                if (token != null) { Exchange(token, done); return; }
                if (error != null && error.StartsWith("noCredential")) GoogleIdToken(false, main, (t2, e2) => { if (t2 != null) Exchange(t2, done); else Fail(e2, done); });
                else Fail(error, done);
            });
#else
            Debug.Log("구글 로그인: 안드로이드 기기가 아니라 가짜 로그인으로 대신합니다.");
            IsSignedIn = true;
            AccountId = "google-editor-local";
            done?.Invoke(true);
#endif
        }

        private void Fail(string error, Action<bool> done)
        {
            LastError = error;
            Debug.LogWarning("구글 로그인 실패: " + error);
            done?.Invoke(false);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class Callback : AndroidJavaProxy
        {
            private readonly SynchronizationContext _main;
            private readonly Action<string, string> _done;
            public Callback(SynchronizationContext main, Action<string, string> done) : base(BridgeClass + "$Callback") { _main = main; _done = done; }
            // Java 쪽 스레드에서 불린다 → 메인 스레드로
            public void onResult(string idToken, string error) { if (_main != null) _main.Post(_ => _done(idToken, error), null); else _done(idToken, error); }
        }

        private void GoogleIdToken(bool onlyAuthorized, SynchronizationContext main, Action<string, string> done)
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var bridge = new AndroidJavaClass(BridgeClass);
                bridge.CallStatic("signIn", activity, _webClientId, onlyAuthorized, new Callback(main, done));
            }
            catch (Exception e) { done(null, "plugin: " + e.Message); }
        }
#endif

        /// <summary>구글 ID 토큰 → Firebase ID 토큰 (Firebase Auth REST).</summary>
        private void Exchange(string googleIdToken, Action<bool> done)
        {
            var body = new JObject
            {
                ["postBody"] = "id_token=" + Uri.EscapeDataString(googleIdToken) + "&providerId=google.com",
                ["requestUri"] = "http://localhost",
                ["returnSecureToken"] = true,
                ["returnIdpCredential"] = false,
            }.ToString(Newtonsoft.Json.Formatting.None);
            var req = new UnityWebRequest("https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=" + Uri.EscapeDataString(_apiKey), "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SendWebRequest().completed += _ =>
            {
                try
                {
                    if (req.result != UnityWebRequest.Result.Success) { Fail("firebase " + req.responseCode + ": " + req.downloadHandler?.text, done); return; }
                    var r = JObject.Parse(req.downloadHandler.text);
                    IdToken = r.Value<string>("idToken");
                    AccountId = r.Value<string>("localId");
                    IsSignedIn = IdToken != null;
                    if (!IsSignedIn) { Fail("firebase: idToken 없음", done); return; }
                    done?.Invoke(true);
                }
                finally { req.Dispose(); }
            };
        }
    }
}
