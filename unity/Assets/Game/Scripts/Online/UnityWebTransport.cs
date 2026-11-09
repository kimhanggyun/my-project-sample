using System;
using System.Collections;
using System.Text;
using Game.Online;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Unity.Online
{
    /// <summary>
    /// UnityWebRequest로 보내는 HTTP 전송 (Game.Online.IOnlineTransport). 코루틴은 GameApp에서 돈다.
    /// 개발용 http:// 주소는 Player Settings ▸ Allow downloads over HTTP가 허용되어야 한다 (docs/impl/server.md 5장).
    /// </summary>
    public sealed class UnityWebTransport : IOnlineTransport
    {
        public const int TimeoutSeconds = 10;

        private readonly MonoBehaviour _host;
        private readonly string _baseUrl;

        public UnityWebTransport(MonoBehaviour host, string baseUrl)
        {
            _host = host;
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
        }

        public void Send(string method, string path, string jsonBody, string bearerToken, Action<int, string> done) =>
            _host.StartCoroutine(Run(method, path, jsonBody, bearerToken, done));

        private IEnumerator Run(string method, string path, string jsonBody, string bearerToken, Action<int, string> done)
        {
            using (var req = new UnityWebRequest(_baseUrl + path, method))
            {
                if (jsonBody != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                if (bearerToken != null) req.SetRequestHeader("Authorization", "Bearer " + bearerToken);
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();
                bool network = req.result == UnityWebRequest.Result.ConnectionError || req.responseCode == 0;
                done(network ? 0 : (int)req.responseCode, req.downloadHandler?.text);
            }
        }
    }
}
