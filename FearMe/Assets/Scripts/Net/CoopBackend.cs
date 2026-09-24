using System.Collections.Generic;

namespace FearMe.Net
{
    // What a lobby needs from a transport, and no more. Relay, a LAN socket or
    // the offline stand-in all fit behind this, which is the point: the screen
    // is written once.
    public abstract class CoopBackend
    {
        // False when the thing this backend needs is not installed or not
        // signed in. The lobby shows UnavailableReason instead of failing
        // silently at the first button press.
        public abstract bool IsAvailable { get; }
        public abstract string UnavailableReason { get; }

        public abstract void Host(int maxPlayers);
        public abstract void Join(string joinCode);
        public abstract void SetReady(bool ready);
        public abstract void Leave();
        public abstract void StartMatch(string sceneName);

        // Called every frame while the lobby is open, for backends that poll.
        public virtual void Tick() { }

        protected static void Report(SessionState state, string status) =>
            CoopSession.Report(state, status);

        protected static void ReportJoinCode(string joinCode) =>
            CoopSession.ReportJoinCode(joinCode);

        protected static void ReportMembers(IList<SessionMember> members) =>
            CoopSession.ReportMembers(members);
    }
}
