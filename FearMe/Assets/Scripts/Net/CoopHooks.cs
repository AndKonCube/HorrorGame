using System;
using FearMe.Core;
using FearMe.Player;

namespace FearMe.Net
{
    // Where the online layer plugs into gameplay. Every hook is null in a
    // single-player run, so the game behaves exactly as it did before and the
    // project still compiles with no netcode installed - nothing in Core or
    // Player ever names a netcode type.
    //
    // A hook returns true when it has taken the request: the caller then does
    // nothing and waits for the authoritative answer to come back.
    public static class CoopHooks
    {
        // True once a session owns the run.
        public static bool Online;

        // This machine is the host: it runs the stalker and decides the run.
        public static bool IsHost;

        // A player asking the host to change the run (RunRequest, argument).
        public static Func<int, int, bool> RunRequested;

        // Shared by every machine in the session, so the random pruning that
        // decides where the keys are lands the same way for everyone. Null in
        // a solo run, which then picks its own seed.
        public static int? RunSeed;

        // Picking something up, opening the way out and ending the run all
        // have to agree between machines, so the server decides.
        public static Func<KeyItem, bool> KeyTaken;
        public static Func<bool> EscapeRequested;
        public static Func<bool, bool> RunEnded;

        // Going down and being pulled back up are the co-op stakes, so they
        // are server-owned too.
        public static Func<PlayerVitals, bool> DownRequested;
        public static Func<PlayerVitals, bool> ReviveRequested;

        // A scene reload has to happen on every machine at once.
        public static Func<bool> RestartRequested;

        // The stalker lives on the host; a guest's noise is sent there.
        public static Func<UnityEngine.Vector3, float, bool> NoiseMade;

        // Dragged off or caged (vitals, Captivity, anchor PropSync id). Server-owned.
        public static Func<PlayerVitals, int, int, bool> CaptivityRequested;

        // A door, lever or carried thing changed here; tell the other player.
        public static Action<int, int, UnityEngine.Vector3> PropChanged;

        public static void Clear()
        {
            Online = false;
            IsHost = false;
            RunRequested = null;
            RunSeed = null;
            KeyTaken = null;
            EscapeRequested = null;
            RunEnded = null;
            DownRequested = null;
            ReviveRequested = null;
            RestartRequested = null;
            NoiseMade = null;
            PropChanged = null;
            CaptivityRequested = null;
        }
    }
}
