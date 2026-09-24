#if FEARME_COOP_ONLINE
using Unity.Netcode.Components;

namespace FearMe.Net.Online
{
    // Each player moves themselves and tells the other, instead of asking the
    // server for permission every step. Two friends, not strangers: feeling
    // responsive matters far more than being cheat-proof.
    public class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
#endif
