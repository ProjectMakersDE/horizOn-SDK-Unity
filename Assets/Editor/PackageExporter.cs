using UnityEditor;
using System;

namespace PM.horizOn.Cloud.Editor
{
    public static class PackageExporter
    {
        private const string SDK_PATH = "Assets/Plugins/ProjectMakers/horizOn";

        public static void Export()
        {
            string version = Environment.GetEnvironmentVariable("PACKAGE_VERSION") ?? "dev";
            string packageName = $"horizOn-SDK-{version}.unitypackage";

            AssetDatabase.ExportPackage(
                SDK_PATH,
                packageName,
                ExportPackageOptions.Recurse
            );

            UnityEngine.Debug.Log($"Exported: {packageName}");
        }
    }
}
