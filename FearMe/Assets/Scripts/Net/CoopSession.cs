using System;
using System.Collections.Generic;
using UnityEngine;

namespace FearMe.Net
{
    public enum SessionState
    {
        Offline,     // not in a session at all
        Connecting,  // waiting on the service to create or join one
        InLobby,     // in a session, waiting for the host to start
        Starting     // handing off to the gameplay scene
    }

    [Serializable]
    public struct SessionMember
    {
        public string name;
        public bool isHost;
        public bool isReady;
        public bool isLocal;
    }

    // The single thing the lobby screen talks to, so the UI never learns
    // whether the session is real. Phase 2 assigns a Relay-backed Backend and
    // not one line of the lobby changes.
    public static class CoopSession
    {
        public const int MaxPlayers = 2;

        private static readonly List<SessionMember> members = new List<SessionMember>();
        private static CoopBackend backend;

        // Fires whenever anything below changes, so the UI redraws on demand
        // instead of polling every frame.
        public static event Action Changed;

        public static SessionState State { get; private set; } = SessionState.Offline;
        public static string Status { get; private set; } = string.Empty;
        public static string JoinCode { get; private set; } = string.Empty;

        // The service's id for the session - the same on both machines, so it
        // doubles as the name of the session's voice channel.
        public static string SessionId { get; private set; } = string.Empty;
        public static IReadOnlyList<SessionMember> Members => members;

        public static CoopBackend Backend
        {
            get
            {
                if (backend == null) backend = new OfflineCoopBackend();
                return backend;
            }
            set
            {
                if (backend != null) backend.Leave();
                backend = value;
                Reset();
            }
        }

        public static bool IsAvailable => Backend.IsAvailable;
        public static string UnavailableReason => Backend.UnavailableReason;
        public static bool InSession => State == SessionState.InLobby || State == SessionState.Starting;
        public static bool IsFull => members.Count >= MaxPlayers;

        public static bool IsHost
        {
            get
            {
                foreach (SessionMember member in members)
                    if (member.isLocal) return member.isHost;
                return false;
            }
        }

        public static bool LocalReady
        {
            get
            {
                foreach (SessionMember member in members)
                    if (member.isLocal) return member.isReady;
                return false;
            }
        }

        // The host starts the run; everyone else has to be ready first. Alone
        // in the lobby that is trivially true, which is how solo play works.
        public static bool CanStart
        {
            get
            {
                if (!IsHost || State != SessionState.InLobby) return false;

                foreach (SessionMember member in members)
                    if (!member.isLocal && !member.isReady) return false;

                return true;
            }
        }

        public static void Host() => Backend.Host(MaxPlayers);
        public static void Join(string joinCode) => Backend.Join(Normalise(joinCode));
        public static void SetReady(bool ready) => Backend.SetReady(ready);
        public static void Leave() => Backend.Leave();
        public static void StartMatch(string sceneName) => Backend.StartMatch(sceneName);

        // Pumped by whoever owns the screen, so a backend that has to poll a
        // web service does not need its own MonoBehaviour.
        public static void Tick() => Backend.Tick();

        // Join codes are read off a friend's screen and typed back in, so be
        // forgiving about case and the spaces people add.
        public static string Normalise(string joinCode)
        {
            if (string.IsNullOrEmpty(joinCode)) return string.Empty;
            return joinCode.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        }

        // Static state outlives a scene load, and in the editor it outlives
        // leaving play mode too. Without this a second run starts in whatever
        // lobby the first one ended in.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Reset()
        {
            members.Clear();
            State = SessionState.Offline;
            Status = string.Empty;
            JoinCode = string.Empty;
            SessionId = string.Empty;
            Raise();
        }

        internal static void Report(SessionState state, string status)
        {
            State = state;
            Status = status ?? string.Empty;
            if (state == SessionState.Offline)
            {
                members.Clear();
                JoinCode = string.Empty;
                SessionId = string.Empty;
            }
            Raise();
        }

        internal static void ReportJoinCode(string joinCode)
        {
            JoinCode = joinCode ?? string.Empty;
            Raise();
        }

        internal static void ReportSessionId(string sessionId)
        {
            SessionId = sessionId ?? string.Empty;
            Raise();
        }

        internal static void ReportMembers(IList<SessionMember> value)
        {
            members.Clear();
            if (value != null)
            {
                for (int i = 0; i < value.Count && i < MaxPlayers; i++)
                    members.Add(value[i]);
            }
            Raise();
        }

        internal static void Raise() => Changed?.Invoke();
    }
}
