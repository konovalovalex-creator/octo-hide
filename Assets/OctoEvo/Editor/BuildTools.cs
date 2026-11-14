#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OctoEvo.EditorTools
{
    /// <summary>
    /// Настройки проекта и сборки: OctoEvo → Setup Project / Build Android APK / Build Windows (Steam).
    /// </summary>
    public static class BuildTools
    {
        const string ProductName = "Octopus Evolution";
        const string CompanyName = "OctoEvo";
        const string AndroidId = "com.octoevo.octopusevolution";

        [MenuItem("OctoEvo/Setup Project (Android + Steam)", priority = 20)]
        public static void SetupProject()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.bundleVersion = "0.1.0";

            // Android: 64-бит ARM, IL2CPP (требование Google Play), альбомная ориентация
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Windows (Steam): 64-бит, полноэкранное окно
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = false;

            // Линейное цветовое пространство — обязательно для реалистичного PBR
            PlayerSettings.colorSpace = ColorSpace.Linear;

            AssetDatabase.SaveAssets();
            Debug.Log("[OctoEvo] Проект настроен для Android и Windows (Steam).");
        }

        [MenuItem("OctoEvo/Build/Android APK", priority = 40)]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, "Builds/Android/OctopusEvolution.apk");
        }

        [MenuItem("OctoEvo/Build/Windows (Steam)", priority = 41)]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows/OctopusEvolution.exe");
        }

        static void Build(BuildTarget target, string path)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("OctoEvo", "Сначала остановите Play.", "OK");
                return;
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("OctoEvo", "В Build Settings нет сцен. Сначала выполните OctoEvo → Build Reef Scene.", "OK");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                options = BuildOptions.None
            });
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[OctoEvo] Сборка готова: {Path.GetFullPath(path)} ({summary.totalSize / (1024 * 1024)} МБ, {summary.totalTime.TotalSeconds:0} с)");
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                Debug.LogError($"[OctoEvo] Сборка не удалась: {summary.result}, ошибок: {summary.totalErrors}. Подробности — в консоли.");
            }
        }
    }
}
#endif
