#if FEARME_COOP_ONLINE
using FearMe.Core;
using FearMe.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FearMe.Net.Online
{
    // The run's shared truth, placed once in the gameplay scene: the seed that
    // hides the keys, which keys are gone, and whether the run is over. It is
    // also what spawns each player's proxy and plugs the online layer into
    // CoopHooks.
    //
    // With no session running it never spawns and does nothing at all.
    public class NetworkRunState : NetworkBehaviour
    {
        [SerializeField] private NetworkObject playerProxyPrefab;

        private readonly NetworkVariable<int> seed = new NetworkVariable<int>();
        private readonly NetworkList<int> takenKeys = new NetworkList<int>();

        private bool ended;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                seed.Value = Random.Range(int.MinValue, int.MaxValue);

                SpawnProxies();
                NetworkManager.OnClientConnectedCallback += SpawnProxyFor;
            }

            // The key spawner has been waiting for exactly this.
            CoopHooks.RunSeed = seed.Value;

            takenKeys.OnListChanged += OnKeysChanged;
            ApplyAllKeys();

            InstallHooks();
        }

        public override void OnNetworkDespawn()
        {
            takenKeys.OnListChanged -= OnKeysChanged;

            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientConnectedCallback -= SpawnProxyFor;

            // A reloaded level must wait for its own seed, not reuse this one.
            CoopHooks.RunSeed = null;
        }

        private void InstallHooks()
        {
            CoopHooks.Online = true;

            CoopHooks.KeyTaken = key =>
            {
                if (key.CoopId < 0) return false; // not one the spawner knows
                RequestKeyRpc(key.CoopId);
                return true;
            };

            CoopHooks.EscapeRequested = () =>
            {
                RequestEndRpc(true);
                return true;
            };

            CoopHooks.RunEnded = escaped =>
            {
                RequestEndRpc(escaped);
                return true;
            };

            CoopHooks.DownRequested = NetworkPlayer.HandleDownRequest;
            CoopHooks.ReviveRequested = NetworkPlayer.HandleReviveRequest;

            // The stalker only runs here on the host, so a guest's noise is
            // sent over; the host's own noise needs no help.
            CoopHooks.NoiseMade = IsServer ? null : (System.Func<Vector3, float, bool>)((position, radius) =>
            {
                NoiseRpc(position, radius);
                return true;
            });

            CoopHooks.PropChanged = (id, state, value) => PropRpc(id, state, value);

            // Both machines ask; only the host acts. The guest just waits to
            // be carried along by the scene load.
            CoopHooks.RestartRequested = () =>
            {
                if (IsServer) Restart();
                return true;
            };
        }

        // --- Players ------------------------------------------------------------

        private void SpawnProxies()
        {
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                SpawnProxyFor(clientId);
        }

        private void SpawnProxyFor(ulong clientId)
        {
            if (playerProxyPrefab == null)
            {
                Debug.LogError("[FearMe] NetworkRunState has no player proxy prefab. " +
                    "Run Tools/FearMe/Co-op/Set Up Co-op.");
                return;
            }

            NetworkClient client;
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out client)) return;
            if (client.PlayerObject != null) return;

            PlayerController start = PlayerRegistry.Local;
            Vector3 position = start != null ? start.transform.position : transform.position;

            NetworkObject proxy = Instantiate(playerProxyPrefab, position, Quaternion.identity);

            // Destroyed with the level, so a restart spawns fresh proxies.
            proxy.SpawnAsPlayerObject(clientId, true);
        }

        // --- Keys -----------------------------------------------------------------

        // Two players can reach for the same key at once; the first request
        // to arrive wins and the list tells everyone.
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestKeyRpc(int keyId)
        {
            if (takenKeys.Contains(keyId)) return;
            takenKeys.Add(keyId);
        }

        private void OnKeysChanged(NetworkListEvent<int> change)
        {
            if (change.Type == NetworkListEvent<int>.EventType.Add)
                RemoveKey(change.Value);

            SyncKeyCount();
        }

        private void ApplyAllKeys()
        {
            for (int i = 0; i < takenKeys.Count; i++)
                RemoveKey(takenKeys[i]);

            SyncKeyCount();
        }

        private static void RemoveKey(int keyId)
        {
            KeyItem key = KeyItem.Find(keyId);
            if (key != null) key.Vanish();
        }

        private void SyncKeyCount()
        {
            if (ObjectiveTracker.Instance != null)
                ObjectiveTracker.Instance.SetKeysCollected(takenKeys.Count);
        }

        // --- Props ---------------------------------------------------------------

        // Last writer wins: two friends, one door, and whoever touched it
        // most recently is right.
        [Rpc(SendTo.NotMe, RequireOwnership = false)]
        private void PropRpc(int id, int state, float value)
        {
            PropSync.Receive(id, state, value);
        }

        // --- Noise ---------------------------------------------------------------

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void NoiseRpc(Vector3 position, float radius)
        {
            NoiseBus.EmitLocal(position, radius);
        }

        // --- End of the run -----------------------------------------------------

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestEndRpc(bool escaped)
        {
            if (ended) return;
            ended = true;
            EndRunRpc(escaped);
        }

        [Rpc(SendTo.Everyone)]
        private void EndRunRpc(bool escaped)
        {
            GameOverController flow = FindFirstObjectByType<GameOverController>();
            if (flow == null) return;

            if (escaped) flow.ApplyEscaped();
            else flow.ApplyCaught();
        }

        private void Restart()
        {
            NetworkManager.SceneManager.LoadScene(gameObject.scene.name, LoadSceneMode.Single);
        }
    }
}
#endif
