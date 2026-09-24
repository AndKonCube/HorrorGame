#if FEARME_COOP_ONLINE
using Unity.Netcode;
using UnityEngine;

namespace FearMe.Net.Online
{
    // Switches the listed behaviours off everywhere but the server - the
    // stalker's brain and its NavMeshAgent, say. Clients just watch where the
    // server puts it.
    //
    // With no session running this never spawns, so nothing is switched off
    // and a solo run is untouched.
    public class ServerOnly : NetworkBehaviour
    {
        [SerializeField] private Behaviour[] serverOnly;

        public override void OnNetworkSpawn()
        {
            if (IsServer) return;

            foreach (Behaviour behaviour in serverOnly)
            {
                if (behaviour != null) behaviour.enabled = false;
            }
        }
    }
}
#endif
