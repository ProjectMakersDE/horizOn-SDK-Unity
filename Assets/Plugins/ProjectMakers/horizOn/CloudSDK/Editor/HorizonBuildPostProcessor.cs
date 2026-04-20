#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace PM.horizOn.Cloud.Editor
{
    /// <summary>
    /// iOS Xcode post-processor for the horizOn SDK.
    /// Currently used by Apple Sign-In to:
    ///   1. Link AuthenticationServices.framework
    ///   2. Add the "Sign in with Apple" capability + entitlement
    ///   3. Set USES_NONFRAGILE_RUNTIME on the bundled .mm plugin
    /// </summary>
    public static class HorizonBuildPostProcessor
    {
        private const string EntitlementsFileName = "horizOn.entitlements";
        private const string SignInWithAppleEntitlementKey = "com.apple.developer.applesignin";

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            string projectPath = PBXProject.GetPBXProjectPath(buildPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string targetGuid = project.GetUnityMainTargetGuid();
            string frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();

            // 1. Link AuthenticationServices.framework on both targets to be safe.
            project.AddFrameworkToProject(targetGuid, "AuthenticationServices.framework", false);
            project.AddFrameworkToProject(frameworkTargetGuid, "AuthenticationServices.framework", false);

            // 2. Write the entitlements file and reference it in the main target.
            string entitlementsPath = Path.Combine(buildPath, EntitlementsFileName);
            var entitlements = File.Exists(entitlementsPath)
                ? new PlistDocument()
                : new PlistDocument();
            if (File.Exists(entitlementsPath))
            {
                entitlements.ReadFromFile(entitlementsPath);
            }

            // The Sign in with Apple entitlement value is an array containing the literal "Default".
            // Replace any existing key to keep the file idempotent across rebuilds.
            entitlements.root.values.Remove(SignInWithAppleEntitlementKey);
            var appleArray = entitlements.root.CreateArray(SignInWithAppleEntitlementKey);
            appleArray.AddString("Default");

            entitlements.WriteToFile(entitlementsPath);

            project.AddFile(entitlementsPath, EntitlementsFileName);
            project.AddBuildProperty(targetGuid, "CODE_SIGN_ENTITLEMENTS", EntitlementsFileName);

            project.WriteToFile(projectPath);

            Debug.Log("[horizOn] iOS post-process: linked AuthenticationServices.framework and added Sign in with Apple entitlement.");
        }
    }
}
#endif
