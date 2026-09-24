#if FEARME_COOP_ONLINE
using UnityEngine;

namespace FearMe.Net.Online
{
    // Swaps the offline stand-in for the real thing, before any scene loads.
    // This is the whole of the switch - the lobby screen is untouched.
    public static class CoopOnlineBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            CoopSession.Backend = new RelayCoopBackend();
        }
    }
}
#endif
