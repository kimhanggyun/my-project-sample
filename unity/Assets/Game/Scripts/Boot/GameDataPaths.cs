using System.IO;
using Game.Data.Json;
using UnityEngine;

namespace Game.Unity.Boot
{
    /// <summary>
    /// 데이터(data/)·설정(config/)을 어디서 읽는지 정한다.
    /// 에디터: 저장소의 data/·config/를 그대로 읽는다 (고치면 바로 반영).
    /// 빌드: 빌드 직전에 Assets/GameDataBuild/Resources/GameData/로 복사한 것을 Resources로 읽는다 (Android의 압축 패키지 안에서도 동작).
    /// </summary>
    public static class GameDataPaths
    {
        /// <summary>Resources 안의 복사본 위치 (빌드 전처리가 만든다, git에는 넣지 않는다).</summary>
        public const string ResourcesRoot = "GameData";

        /// <summary>저장소 루트 (unity/의 부모). 에디터에서만 의미가 있다.</summary>
        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        public static IDataSource Data()
        {
#if UNITY_EDITOR
            return new FileDataSource(Path.Combine(RepoRoot, "data"));
#else
            return new ResourcesDataSource(ResourcesRoot + "/data");
#endif
        }

        public static IDataSource Config()
        {
#if UNITY_EDITOR
            return new FileDataSource(Path.Combine(RepoRoot, "config"));
#else
            return new ResourcesDataSource(ResourcesRoot + "/config");
#endif
        }

        /// <summary>외부 서비스 설정 파일: 개발 빌드(에디터 포함)는 development, 출시 빌드는 production.</summary>
        public static string ServiceConfigFile => Debug.isDebugBuild ? "services.development.json" : "services.production.json";
    }

    /// <summary>Resources의 TextAsset으로 읽는 데이터 소스. 경로의 확장자(.json)는 떼고 찾는다.</summary>
    public sealed class ResourcesDataSource : IDataSource
    {
        private readonly string _root;

        public ResourcesDataSource(string root) { _root = root.TrimEnd('/'); }

        public bool Exists(string relativePath) => Load(relativePath) != null;

        public string ReadText(string relativePath)
        {
            var asset = Load(relativePath);
            if (asset == null) throw new FileNotFoundException("Resources에 데이터가 없습니다: " + _root + "/" + relativePath);
            return asset.text;
        }

        private TextAsset Load(string relativePath)
        {
            string p = relativePath.Replace('\\', '/');
            string ext = Path.GetExtension(p);
            if (!string.IsNullOrEmpty(ext)) p = p.Substring(0, p.Length - ext.Length);
            return Resources.Load<TextAsset>(_root + "/" + p);
        }
    }
}
