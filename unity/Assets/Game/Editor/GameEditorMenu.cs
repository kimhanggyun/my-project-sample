using System.Collections.Generic;
using System.IO;
using Game.Unity.Boot;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Unity.EditorTools
{
    /// <summary>에디터 메뉴: Boot 씬 만들기, 빌드용 데이터 복사.</summary>
    public static class GameEditorMenu
    {
        public const string BootScenePath = "Assets/Scenes/Boot.unity";

        /// <summary>Boot 씬(카메라 + GameApp)을 만들고 빌드 목록 맨 앞에 둔다. 세로 화면으로 설정한다.</summary>
        [MenuItem("Game/Setup Boot Scene")]
        public static void SetupBootScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            var camera = cam.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0; // 화면은 오버레이 캔버스가 그린다

            new GameObject("GameApp").AddComponent<GameApp>();

            Directory.CreateDirectory(Path.GetDirectoryName(BootScenePath));
            EditorSceneManager.SaveScene(scene, BootScenePath);

            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(BootScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != BootScenePath) scenes.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = scenes.ToArray();

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            AssetDatabase.SaveAssets();
            Debug.Log("Boot 씬을 만들었습니다: " + BootScenePath + " (빌드 목록 맨 앞, 세로 화면). Game 뷰 해상도를 1080x1920 세로로 두고 Play를 누르세요.");
        }

        [MenuItem("Game/Copy Data For Build")]
        public static void CopyDataMenu()
        {
            BuildDataCopier.Copy();
            Debug.Log("data/·config/를 " + BuildDataCopier.TargetRoot + "로 복사했습니다 (git에는 넣지 않습니다).");
        }

        [MenuItem("Game/Open Save Folder")]
        public static void OpenSaveFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);
    }

    /// <summary>
    /// 빌드 전에 저장소의 data/·config/를 Resources 폴더로 복사한다 (빌드에서는 <see cref="ResourcesDataSource"/>로 읽는다).
    /// 복사본은 생성물이라 git에서 뺀다 (.gitignore).
    /// </summary>
    public sealed class BuildDataCopier : IPreprocessBuildWithReport
    {
        /// <summary>Assets 기준 복사 위치. Resources 아래 GameData/{data,config}.</summary>
        public const string TargetRoot = "Assets/GameDataBuild/Resources/" + GameDataPaths.ResourcesRoot;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Copy();

        public static void Copy()
        {
            string repo = GameDataPaths.RepoRoot;
            string target = Path.GetFullPath(Path.Combine(Application.dataPath, "..", TargetRoot));
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyJson(Path.Combine(repo, "data"), Path.Combine(target, "data"));
            CopyJson(Path.Combine(repo, "config"), Path.Combine(target, "config"));
            AssetDatabase.Refresh();
        }

        private static void CopyJson(string from, string to)
        {
            if (!Directory.Exists(from)) throw new BuildFailedException("데이터 폴더가 없습니다: " + from);
            foreach (var file in Directory.GetFiles(from, "*.json", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(from, file);
                string dest = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, true);
            }
        }
    }
}
