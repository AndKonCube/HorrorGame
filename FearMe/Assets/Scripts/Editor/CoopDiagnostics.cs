using System;
using System.Collections.Generic;
using System.Text;
using FearMe.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // One button that checks everything online co-op depends on and prints
    // a checklist, so "the lobby doesn't work" turns into the one line that
    // is actually wrong.
    public static class CoopDiagnostics
    {
        [MenuItem("Tools/FearMe/Co-op/Check Online Setup")]
        public static void Check()
        {
            List<string> ok = new List<string>();
            List<string> problems = new List<string>();

            void Pass(string line) => ok.Add(line);
            void Fail(string line) => problems.Add(line);

            // Packages and the switches that turn their code on.
            bool netcode = TypeExists("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
            bool sessions = TypeExists("Unity.Services.Multiplayer.MultiplayerService, Unity.Services.Multiplayer");
            bool vivox = TypeExists("Unity.Services.Vivox.VivoxService, Unity.Services.Vivox");

            if (netcode && sessions) Pass("Netcode and Multiplayer Services are installed.");
            else Fail("Online packages missing - run Tools/FearMe/Co-op/Install Online Packages.");

            string[] defines = Defines();
            bool online = Array.IndexOf(defines, CoopPackageInstaller.Define) >= 0;
            if (online) Pass("Online co-op code is switched on.");
            else Fail("Online co-op code is switched off - run Tools/FearMe/Co-op/Enable Online Code " +
                      "(the lobby only ever hosts offline without it).");

            // The one everything else depends on.
            if (!string.IsNullOrEmpty(CloudProjectSettings.projectId))
                Pass($"Linked to Unity Cloud project '{CloudProjectSettings.projectName}'.");
            else
                Fail("NOT LINKED TO UNITY CLOUD - Edit > Project Settings > Services, choose your organisation, " +
                     "link or create a project. Hosting and joining cannot work until this is done.");

            // What the lobby and the host spawn when they go online.
            if (Resources.Load<GameObject>("Coop/CoopNetwork") != null) Pass("The network prefab exists.");
            else Fail("Resources/Coop/CoopNetwork.prefab is missing - run Tools/FearMe/Co-op/Set Up Co-op.");

            if (Resources.Load<GameObject>("Coop/CoopRunState") != null)
                Pass("The run-state prefab exists (players' bodies, shared pickups, the stalker's position).");
            else
                Fail("Resources/Coop/CoopRunState.prefab is missing - without it players cannot see each other " +
                     "or share pickups. Run Tools/FearMe/Co-op/Set Up Co-op.");

            // Netcode can only load scenes that are in the build.
            CheckBuildScenes(Pass, Fail);

            // Whatever scene is open.
            CheckOpenScene(Pass, Fail);

            bool voice = Array.IndexOf(defines, CoopPackageInstaller.VoiceDefine) >= 0;
            if (voice && vivox) Pass("Proximity voice is installed - make sure Vivox is switched on in the Unity Cloud dashboard.");
            else if (voice) Fail("Voice is switched on but the Vivox package is missing - run Install Proximity Voice again.");
            else if (vivox) Fail("Vivox is installed but voice code is off - run Tools/FearMe/Co-op/Install Proximity Voice.");
            else Fail("Proximity voice is not installed, so there is no voice chat - run " +
                      "Tools/FearMe/Co-op/Install Proximity Voice, then switch Vivox on in the Unity Cloud dashboard.");

            StringBuilder report = new StringBuilder();
            report.AppendLine(problems.Count == 0
                ? "[FearMe] Online setup: everything checks out."
                : $"[FearMe] Online setup: {problems.Count} thing(s) to fix.");
            foreach (string line in problems) report.AppendLine("  [FIX] " + line);
            foreach (string line in ok) report.AppendLine("  [ok]  " + line);

            if (problems.Count == 0) Debug.Log(report.ToString());
            else Debug.LogWarning(report.ToString());
        }

        private static void CheckBuildScenes(Action<string> pass, Action<string> fail)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int menuIndex = -1, gameIndex = -1;

            for (int i = 0; i < scenes.Length; i++)
            {
                if (!scenes[i].enabled) continue;
                if (scenes[i].path.EndsWith("/MainMenu.unity")) menuIndex = i;
                if (scenes[i].path.EndsWith("/Demo.unity")) gameIndex = i;
            }

            if (menuIndex == 0) pass("MainMenu is the first scene in the build.");
            else if (menuIndex > 0) fail("MainMenu is in the build but not first - drag it to the top in File > Build Profiles.");
            else fail("MainMenu is not in the build - add it in File > Build Profiles.");

            if (gameIndex >= 0) pass("Demo is in the build.");
            else fail("Demo is not in the build - add it in File > Build Profiles, or Netcode cannot load it.");
        }

        private static void CheckOpenScene(Action<string> pass, Action<string> fail)
        {
            Scene scene = SceneManager.GetActiveScene();

            LobbyPanel lobby = UnityEngine.Object.FindFirstObjectByType<LobbyPanel>(FindObjectsInactive.Include);
            MainMenuController menu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (menu != null)
            {
                if (lobby != null) pass($"'{scene.name}' has a lobby panel.");
                else fail($"'{scene.name}' has no lobby panel - run Tools/FearMe/Add Lobby Panel (current scene).");
                return;
            }

            // A gameplay scene needs nothing extra any more: the host spawns
            // the run state itself when the level loads.
        }

        private static bool TypeExists(string assemblyQualifiedName) => Type.GetType(assemblyQualifiedName) != null;

        private static string[] Defines()
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] defines);
            return defines;
        }
    }
}
