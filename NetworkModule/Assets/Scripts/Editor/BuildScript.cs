using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Editor
{
    public class BuildScript : MonoBehaviour
    {
        [MenuItem("Build/Build Server App")]
        public static void BuildServerApp()
        {
            string buildPath = "Builds/ServerApp";
            var options = new BuildPlayerOptions()
            {
                scenes = new[] { "Assets/Scenes/MatchServerScene.unity" },
                locationPathName = buildPath + "/MatchServer.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.AutoRunPlayer
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            LogBuildResult(report);
        }
        [MenuItem("Build/Build Client App")]
        public static void BuildClientApp()
        {
            string buildPath = "Builds/ClientApp";
            var options = new BuildPlayerOptions()
            {
                scenes = new[] { "Assets/Scenes/ClientScene.unity" },
                locationPathName = buildPath + "/ClientScene.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.AutoRunPlayer
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            LogBuildResult(report);
        }
        
        private static void LogBuildResult(BuildReport report)
        {
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Build succeeded: {report.summary.totalSize} bytes");
            }
            else if (report.summary.result == BuildResult.Failed)
            {
                Debug.LogError($"Build failed");
            }
        }
    }
}
