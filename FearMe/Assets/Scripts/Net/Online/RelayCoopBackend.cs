#if FEARME_COOP_ONLINE
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FearMe.Settings;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FearMe.Net.Online
{
    // Real sessions over Unity Relay: host gets a join code, a friend types it
    // in, and Netcode for GameObjects connects the two of them through Unity's
    // relay servers - no port forwarding, no IP addresses.
    //
    // Everything the lobby needs is pushed up through CoopSession, so the
    // lobby screen neither knows nor cares that this is the backend in use.
    public class RelayCoopBackend : CoopBackend
    {
        private const string NameKey = "name";
        private const string ReadyKey = "ready";
        private const string HostKey = "host";

        // Built by Tools/FearMe/Co-op/Set Up Co-op.
        private const string NetworkPrefabPath = "Coop/CoopNetwork";
        private const string MenuScene = "MainMenu";

        private ISession session;
        private bool busy;
        private bool leaving;
        private bool networkHooked;

        public override bool IsAvailable => true;
        public override string UnavailableReason => string.Empty;

        public override void Host(int maxPlayers)
        {
            if (busy || session != null) return;
            Run(HostAsync(maxPlayers), "Couldn't host");
        }

        public override void Join(string joinCode)
        {
            if (busy || session != null) return;

            if (string.IsNullOrEmpty(joinCode))
            {
                Report(SessionState.Offline, "Type the host's join code first.");
                return;
            }

            Run(JoinAsync(joinCode), "Couldn't join that code");
        }

        public override void SetReady(bool ready)
        {
            if (session == null || busy) return;
            Run(SetReadyAsync(ready), "Couldn't update ready state");
        }

        public override void Leave()
        {
            // Fire and forget: the menu should not wait on a web request to
            // let you go.
            Run(LeaveAsync(string.Empty), "Couldn't leave cleanly");
        }

        public override void StartMatch(string sceneName)
        {
            if (session == null || !session.IsHost) return;

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Report(SessionState.InLobby,
                    $"Scene '{sceneName}' is not in the build settings - add it under File > Build Profiles.");
                return;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer)
            {
                Report(SessionState.InLobby, "The connection isn't up yet - give it a second.");
                return;
            }

            Report(SessionState.Starting, "Starting...");

            // The server loads it; Netcode carries the guest along.
            network.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        // --- The async work -----------------------------------------------------

        private async Task HostAsync(int maxPlayers)
        {
            await PrepareAsync();

            Report(SessionState.Connecting, "Creating a session...");
            SessionOptions options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
            session = await MultiplayerService.Instance.CreateSessionAsync(options);

            await Enter(ready: true);
        }

        private async Task JoinAsync(string joinCode)
        {
            await PrepareAsync();

            Report(SessionState.Connecting, "Joining " + joinCode + "...");
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);

            await Enter(ready: false);
        }

        private async Task PrepareAsync()
        {
            Report(SessionState.Connecting, "Signing in...");
            await SignInAsync();
            EnsureNetworkManager();
        }

        private async Task Enter(bool ready)
        {
            Attach();

            // Online from here on, so the gameplay scene knows to wait for the
            // shared seed rather than hiding the keys on its own.
            CoopHooks.Online = true;

            ReportJoinCode(session.Code);
            ReportSessionId(session.Id);
            Report(SessionState.InLobby, string.Empty);

            await PublishSelf(ready);
            PushMembers();
        }

        private async Task SetReadyAsync(bool ready)
        {
            await PublishSelf(ready);
            PushMembers();
        }

        // Your name and ready flag ride on the session as player properties,
        // so the other lobby sees them without any netcode involved.
        private async Task PublishSelf(bool ready)
        {
            if (session == null) return;

            session.CurrentPlayer.SetProperties(new Dictionary<string, PlayerProperty>
            {
                { NameKey, new PlayerProperty(GameSettingsService.Current.playerName, VisibilityPropertyOptions.Member) },
                { ReadyKey, new PlayerProperty(ready ? "1" : "0", VisibilityPropertyOptions.Member) },
                // Each player says whether they host, so the lobby never has
                // to match ids against the session's own host field.
                { HostKey, new PlayerProperty(session.IsHost ? "1" : "0", VisibilityPropertyOptions.Member) }
            });

            await session.SaveCurrentPlayerDataAsync();
        }

        private async Task LeaveAsync(string reason)
        {
            leaving = true;
            try
            {
                CoopHooks.Clear();

                ISession closing = session;
                Detach();
                session = null;

                // Networking first and synchronously, so a scene load straight
                // after this is not fighting a live Netcode scene manager.
                NetworkManager network = NetworkManager.Singleton;
                if (network != null && network.IsListening) network.Shutdown();

                if (closing != null) await closing.LeaveAsync();
            }
            finally
            {
                leaving = false;
                Report(SessionState.Offline, reason);
            }
        }

        // --- Setup ------------------------------------------------------------------

        private static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                InitializationOptions options = new InitializationOptions();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Anonymous sign-in is cached per profile. Two copies on one PC
                // would otherwise be the same player and could never join each
                // other, so test builds get a profile per process.
                options.SetProfile("dev" + System.Diagnostics.Process.GetCurrentProcess().Id);
#endif

                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private void EnsureNetworkManager()
        {
            if (NetworkManager.Singleton == null)
            {
                GameObject prefab = Resources.Load<GameObject>(NetworkPrefabPath);
                if (prefab == null)
                    throw new InvalidOperationException(
                        "The co-op network prefab is missing. Run Tools/FearMe/Co-op/Set Up Co-op.");

                Object.Instantiate(prefab).name = "CoopNetwork";
            }

            if (networkHooked || NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnClientStopped += OnNetworkStopped;
            networkHooked = true;
        }

        // --- Session events -----------------------------------------------------

        private void Attach()
        {
            if (session == null) return;
            session.Changed += PushMembers;
            session.RemovedFromSession += OnRemoved;
        }

        private void Detach()
        {
            if (session == null) return;
            session.Changed -= PushMembers;
            session.RemovedFromSession -= OnRemoved;
        }

        private void PushMembers()
        {
            if (session == null) return;

            string me = AuthenticationService.Instance.PlayerId;
            List<SessionMember> members = new List<SessionMember>();

            foreach (IReadOnlyPlayer player in session.Players)
            {
                bool isHost = Property(player, HostKey, "0") == "1";

                members.Add(new SessionMember
                {
                    name = Property(player, NameKey, "PLAYER"),
                    isHost = isHost,
                    // The host readies by pressing Start.
                    isReady = isHost || Property(player, ReadyKey, "0") == "1",
                    isLocal = player.Id == me
                });
            }

            ReportMembers(members);
        }

        private static string Property(IReadOnlyPlayer player, string key, string fallback)
        {
            if (player.Properties != null && player.Properties.TryGetValue(key, out PlayerProperty property)
                && property != null && !string.IsNullOrEmpty(property.Value))
                return property.Value;

            return fallback;
        }

        private void OnRemoved()
        {
            // Already gone on the service's side; nothing to leave.
            Detach();
            session = null;

            Run(LeaveAsync("The session was closed."), "Couldn't leave cleanly");
            ReturnToMenu();
        }

        // The other end vanished mid-run, or the relay dropped us.
        private void OnNetworkStopped(bool wasHost)
        {
            if (leaving || session == null) return;

            Run(LeaveAsync("Lost the connection."), "Couldn't leave cleanly");
            ReturnToMenu();
        }

        private static void ReturnToMenu()
        {
            if (SceneManager.GetActiveScene().name == MenuScene) return;
            if (!Application.CanStreamedLevelBeLoaded(MenuScene)) return;

            SceneManager.LoadScene(MenuScene);
        }

        // --- Plumbing ---------------------------------------------------------------

        // Async work started from a button: errors land in the lobby's status
        // line instead of vanishing into an unobserved task.
        private async void Run(Task work, string failure)
        {
            busy = true;
            try
            {
                await work;
            }
            catch (Exception e)
            {
                Debug.LogException(e);

                string hint = e is ServicesInitializationException
                    ? " Link a Unity Cloud project under Edit > Project Settings > Services."
                    : string.Empty;

                // Never leave a half-open connection behind a failure.
                CoopHooks.Clear();
                Detach();
                session = null;

                NetworkManager network = NetworkManager.Singleton;
                if (network != null && network.IsListening) network.Shutdown();

                Report(SessionState.Offline, $"{failure}: {e.Message}{hint}");
            }
            finally
            {
                busy = false;
            }
        }
    }
}
#endif
