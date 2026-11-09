using System;
using System.Collections;
using Game.Foundation.Geo;
using Game.Services;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Game.Unity.Online
{
    /// <summary>
    /// 기기 위치 (서비스 설정 location = device, territory.md 3장). 한 번만 묻고 바로 끈다 (계속 추적하지 않음).
    /// Android: 처음에 위치 권한(정밀)을 묻는다. iOS: 시작할 때 시스템이 묻는다 (Player Settings의 Location Usage Description 문구).
    /// 위경도는 여기(엔진 경계)에서 한 번만 백만분의 1도 정수로 바꾼다. 규칙 계산에는 소수가 들어가지 않는다.
    /// </summary>
    public sealed class UnityLocationService : ILocationService
    {
        public const string ImplName = "device";
        private const float TimeoutSeconds = 20f;
        private const float AccuracyMeters = 100f;

        private readonly MonoBehaviour _host;
        private bool _busy;

        public UnityLocationService(MonoBehaviour host) { _host = host; }

        public bool IsAvailable => true;

        public void Request(Action<LocationResult> done)
        {
            if (_busy) { done?.Invoke(new LocationResult { Ok = false, Reason = LocationReasons.Unavailable }); return; }
            _busy = true;
            _host.StartCoroutine(Run(r => { _busy = false; done?.Invoke(r); }));
        }

        private static IEnumerator Run(Action<LocationResult> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                bool? granted = null;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => granted = true;
                callbacks.PermissionDenied += _ => granted = false;
                callbacks.PermissionDeniedAndDontAskAgain += _ => granted = false;
                Permission.RequestUserPermission(Permission.FineLocation, callbacks);
                float waited = 0f;
                while (granted == null && waited < 60f) { waited += Time.unscaledDeltaTime; yield return null; }
                if (granted != true) { done(Fail(LocationReasons.Denied)); yield break; }
            }
            if (!Input.location.isEnabledByUser) { done(Fail(LocationReasons.Denied)); yield break; } // 기기의 위치 설정이 꺼짐
#endif
            Input.location.Start(AccuracyMeters, AccuracyMeters);
            float t = 0f;
            while (Input.location.status == LocationServiceStatus.Initializing && t < TimeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            var status = Input.location.status;
            if (status != LocationServiceStatus.Running)
            {
                Input.location.Stop();
                done(Fail(status == LocationServiceStatus.Failed ? LocationReasons.Denied : LocationReasons.Unavailable));
                yield break;
            }
            var d = Input.location.lastData;
            Input.location.Stop();
            var point = new GeoPoint((long)Math.Round((double)d.latitude * GeoPoint.Scale), (long)Math.Round((double)d.longitude * GeoPoint.Scale));
            done(point.IsValid ? new LocationResult { Ok = true, Point = point } : Fail(LocationReasons.Unavailable));
        }

        private static LocationResult Fail(string reason) => new LocationResult { Ok = false, Reason = reason };
    }
}
