using System.Collections.Generic;
using FearMe.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FearMe.Net
{
    // The backend in use until the netcode packages are installed. Hosting
    // works and drops you into a one-person lobby, so the screen and the solo
    // path are both real today; joining is the only thing it cannot do, and it
    // says so rather than hanging on a connection that will never open.
    public class OfflineCoopBackend : CoopBackend
    {
        // Editor and development builds only: fills the second slot so the
        // lobby's slots, ready state and Start gating can be checked without a
        // second machine. Never true in a release build.
        public static bool SimulateGuest;

        private readonly List<SessionMember> members = new List<SessionMember>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const string GuestName = "PLAYER 2 (SIMULATED)";
        private float guestArrivesAt;
        private float guestReadiesAt;
#endif

        public override bool IsAvailable => false;

        public override string UnavailableReason =>
            "Online co-op needs two packages: com.unity.netcode.gameobjects and " +
            "com.unity.services.multiplayer. Add them in Window > Package Manager. " +
            "Hosting still works offline for solo play.";

        public override void Host(int maxPlayers)
        {
            members.Clear();
            members.Add(new SessionMember
            {
                name = LocalName(),
                isHost = true,
                isReady = true,
                isLocal = true
            });

            ReportMembers(members);
            ReportJoinCode(string.Empty);
            Report(SessionState.InLobby, "Offline - no join code to share. Start for a solo run.");

            ScheduleSimulatedGuest();
        }

        public override void Join(string joinCode)
        {
            Report(SessionState.Offline, UnavailableReason);
        }

        public override void SetReady(bool ready)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].isLocal) continue;

                SessionMember member = members[i];
                member.isReady = ready;
                members[i] = member;
            }

            ReportMembers(members);
        }

        public override void Leave()
        {
            members.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            guestArrivesAt = 0f;
            guestReadiesAt = 0f;
#endif
            Report(SessionState.Offline, string.Empty);
        }

        public override void StartMatch(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Report(SessionState.InLobby, "No gameplay scene set on the lobby panel.");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Report(SessionState.InLobby,
                    $"Scene '{sceneName}' is not in the build settings - add it under File > Build Profiles.");
                return;
            }

            Report(SessionState.Starting, "Starting...");
            SceneManager.LoadScene(sceneName);
        }

        public override void Tick()
        {
            AdvanceSimulatedGuest();
        }

        private static string LocalName()
        {
            string name = GameSettingsService.Current.playerName;
            return string.IsNullOrEmpty(name) ? "PLAYER 1" : name;
        }

        private void ScheduleSimulatedGuest()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!SimulateGuest) return;

            guestArrivesAt = Time.unscaledTime + 1.5f;
            guestReadiesAt = guestArrivesAt + 2f;
#endif
        }

        private void AdvanceSimulatedGuest()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (guestArrivesAt > 0f && Time.unscaledTime >= guestArrivesAt)
            {
                guestArrivesAt = 0f;
                members.Add(new SessionMember { name = GuestName, isHost = false, isReady = false, isLocal = false });
                ReportMembers(members);
                Report(SessionState.InLobby, "A player joined.");
            }

            if (guestReadiesAt > 0f && Time.unscaledTime >= guestReadiesAt)
            {
                guestReadiesAt = 0f;
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i].isLocal) continue;

                    SessionMember member = members[i];
                    member.isReady = true;
                    members[i] = member;
                }
                ReportMembers(members);
            }
#endif
        }
    }
}
