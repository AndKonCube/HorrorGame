using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace FearMe.EditorTools
{
    // Installs the optional packages online co-op and proximity voice need,
    // and switches their code on.
    //
    // Packages are added by name with no version, so the Package Manager
    // picks the newest one that supports this editor - pinning a version by
    // hand is how a project ends up refusing to open.
    //
    // The code for each lives behind a define (FEARME_COOP_ONLINE,
    // FEARME_VOICE), so the project compiles with or without the packages,
    // and a define is only added once its install has actually succeeded.
    public static class CoopPackageInstaller
    {
        public const string Define = "FEARME_COOP_ONLINE";
        public const string VoiceDefine = "FEARME_VOICE";

        private static readonly string[] OnlinePackages =
        {
            "com.unity.netcode.gameobjects",
            "com.unity.services.multiplayer"
        };

        private static readonly string[] VoicePackages =
        {
            "com.unity.services.vivox"
        };

        private static AddAndRemoveRequest request;
        private static string[] pendingPackages;
        private static string pendingDefine;
        private static string pendingNext;

        [MenuItem("Tools/FearMe/Co-op/Install Online Packages")]
        public static void Install()
        {
            Begin(OnlinePackages, Define,
                "Next: Edit > Project Settings > Services - link a Unity Cloud project, " +
                "then open the Demo scene and run Tools/FearMe/Co-op/Set Up Co-op.");
        }

        [MenuItem("Tools/FearMe/Co-op/Install Proximity Voice")]
        public static void InstallVoice()
        {
            if (!HasDefine(Define))
            {
                Debug.LogError("[FearMe] Voice rides on online co-op - run Install Online Packages first.");
                return;
            }

            Begin(VoicePackages, VoiceDefine,
                "Next: in the Unity Cloud dashboard, open this project and switch Vivox on. " +
                "Then check Edit > Project Settings > Services > Vivox shows its credentials.");
        }

        // If the packages are removed later, their define has to come off
        // first or the code behind it will not compile. Run this, then remove them.
        [MenuItem("Tools/FearMe/Co-op/Disable Online Code")]
        public static void Disable()
        {
            SetEverywhere(VoiceDefine, false);
            SetEverywhere(Define, false);
            Debug.Log("[FearMe] Online co-op and voice switched off. The lobby falls back to offline.");
        }

        [MenuItem("Tools/FearMe/Co-op/Enable Online Code")]
        public static void Enable()
        {
            SetEverywhere(Define, true);
            Debug.Log("[FearMe] " + Define + " set. Scripts will recompile with online co-op.");
        }

        [MenuItem("Tools/FearMe/Co-op/Disable Proximity Voice Code")]
        public static void DisableVoice()
        {
            SetEverywhere(VoiceDefine, false);
            Debug.Log("[FearMe] " + VoiceDefine + " removed. Co-op still works, without voice.");
        }

        private static void Begin(string[] packages, string define, string next)
        {
            if (request != null && !request.IsCompleted)
            {
                Debug.Log("[FearMe] A package install is already running - wait for it to finish.");
                return;
            }

            pendingPackages = packages;
            pendingDefine = define;
            pendingNext = next;

            Debug.Log("[FearMe] Installing " + string.Join(" and ", packages) + "...");
            request = Client.AddAndRemove(packages, null);
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (request == null || !request.IsCompleted) return;
            EditorApplication.update -= Poll;

            if (request.Status == StatusCode.Success)
            {
                foreach (UnityEditor.PackageManager.PackageInfo info in request.Result)
                {
                    if (System.Array.IndexOf(pendingPackages, info.name) >= 0)
                        Debug.Log($"[FearMe] Installed {info.name} {info.version}.");
                }

                SetEverywhere(pendingDefine, true);
                Debug.Log("[FearMe] " + pendingDefine + " set; scripts will recompile. " + pendingNext);
            }
            else
            {
                Debug.LogError("[FearMe] Package install failed: " +
                    (request.Error != null ? request.Error.message : "unknown error") +
                    ". The code that needs it was left switched off, so the project still compiles.");
            }

            request = null;
        }

        private static bool HasDefine(string define)
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] current);
            return System.Array.IndexOf(current, define) >= 0;
        }

        private static void SetEverywhere(string define, bool on)
        {
            foreach (NamedBuildTarget target in Targets()) SetDefine(target, define, on);
        }

        private static IEnumerable<NamedBuildTarget> Targets()
        {
            yield return NamedBuildTarget.Standalone;

            // Whatever the build profile is pointed at, if that is not desktop.
            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group != BuildTargetGroup.Standalone && group != BuildTargetGroup.Unknown)
                yield return NamedBuildTarget.FromBuildTargetGroup(group);
        }

        private static void SetDefine(NamedBuildTarget target, string define, bool on)
        {
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] current);
            List<string> defines = new List<string>(current);

            bool has = defines.Contains(define);
            if (on == has) return;

            if (on) defines.Add(define);
            else defines.RemoveAll(d => d == define);

            PlayerSettings.SetScriptingDefineSymbols(target, defines.ToArray());
        }
    }
}
