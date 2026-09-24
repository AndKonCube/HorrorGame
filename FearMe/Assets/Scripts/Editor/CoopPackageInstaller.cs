using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace FearMe.EditorTools
{
    // Installs the two co-op packages and switches the online code on.
    //
    // The packages are added by name with no version, so the Package Manager
    // picks the newest one that supports this editor - pinning a version by
    // hand is how a project ends up refusing to open.
    //
    // Everything under Scripts/Net/Online is wrapped in FEARME_COOP_ONLINE, so
    // the project compiles with or without the packages. The define is only
    // added once the install has actually succeeded.
    public static class CoopPackageInstaller
    {
        public const string Define = "FEARME_COOP_ONLINE";

        private static readonly string[] Packages =
        {
            "com.unity.netcode.gameobjects",
            "com.unity.services.multiplayer"
        };

        private static AddAndRemoveRequest request;

        [MenuItem("Tools/FearMe/Co-op/Install Online Packages")]
        public static void Install()
        {
            if (request != null && !request.IsCompleted)
            {
                Debug.Log("[FearMe] Co-op packages are already installing.");
                return;
            }

            Debug.Log("[FearMe] Installing " + string.Join(" and ", Packages) + "...");
            request = Client.AddAndRemove(Packages, null);
            EditorApplication.update += Poll;
        }

        // If the packages are removed later, this has to come off first or
        // the online code will not compile. Run this, then remove them.
        [MenuItem("Tools/FearMe/Co-op/Disable Online Code")]
        public static void Disable()
        {
            foreach (NamedBuildTarget target in Targets())
                SetDefine(target, false);

            Debug.Log("[FearMe] " + Define + " removed. The lobby falls back to offline.");
        }

        [MenuItem("Tools/FearMe/Co-op/Enable Online Code")]
        public static void Enable()
        {
            foreach (NamedBuildTarget target in Targets())
                SetDefine(target, true);

            Debug.Log("[FearMe] " + Define + " set. Scripts will recompile with online co-op.");
        }

        private static void Poll()
        {
            if (request == null || !request.IsCompleted) return;
            EditorApplication.update -= Poll;

            if (request.Status == StatusCode.Success)
            {
                foreach (UnityEditor.PackageManager.PackageInfo info in request.Result)
                {
                    if (System.Array.IndexOf(Packages, info.name) >= 0)
                        Debug.Log($"[FearMe] Installed {info.name} {info.version}.");
                }

                Enable();
                Debug.Log("[FearMe] Next: Edit > Project Settings > Services - link a Unity Cloud project, " +
                    "then run Tools/FearMe/Co-op/Set Up Current Scene For Co-op in the Demo scene.");
            }
            else
            {
                Debug.LogError("[FearMe] Co-op package install failed: " +
                    (request.Error != null ? request.Error.message : "unknown error") +
                    ". Online code was left switched off, so the project still compiles.");
            }

            request = null;
        }

        private static IEnumerable<NamedBuildTarget> Targets()
        {
            yield return NamedBuildTarget.Standalone;

            // Whatever the build profile is pointed at, if that is not desktop.
            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group != BuildTargetGroup.Standalone && group != BuildTargetGroup.Unknown)
                yield return NamedBuildTarget.FromBuildTargetGroup(group);
        }

        private static void SetDefine(NamedBuildTarget target, bool on)
        {
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] current);
            List<string> defines = new List<string>(current);

            bool has = defines.Contains(Define);
            if (on == has) return;

            if (on) defines.Add(Define);
            else defines.RemoveAll(d => d == Define);

            PlayerSettings.SetScriptingDefineSymbols(target, defines.ToArray());
        }
    }
}
