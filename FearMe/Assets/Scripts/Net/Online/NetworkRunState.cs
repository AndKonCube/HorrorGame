#if FEARME_COOP_ONLINE
using System;
using System.Collections.Generic;
using System.Text;
using FearMe.AI;
using FearMe.Core;
using FearMe.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FearMe.Net.Online
{
    // Where each stalker is, as the host's brain has it.
    public struct StalkerPose : INetworkSerializeByMemcpy, IEquatable<StalkerPose>
    {
        public Vector3 position;
        public float yaw;

        public bool Equals(StalkerPose other) => position == other.position && yaw.Equals(other.yaw);
        public override bool Equals(object obj) => obj is StalkerPose other && Equals(other);
        public override int GetHashCode() => position.GetHashCode() ^ yaw.GetHashCode();
    }

    // The run's shared truth: the seed that hides the keys, which keys are
    // gone, whether the run is over - and where the stalker is, for a guest
    // whose own copy of it does not think. It is also what spawns each
    // player's proxy and plugs the online layer into CoopHooks.
    //
    // The host spawns it when a level loads, so no scene needs it placed by
    // hand. With no session running it never exists at all.
    public class NetworkRunState : NetworkBehaviour
    {
        [SerializeField] private NetworkObject playerProxyPrefab;

        private readonly NetworkVariable<int> seed = new NetworkVariable<int>();

        // The host's level, as one number. A guest on a different version of
        // it - an unsaved edit in the editor against a build, or two builds
        // made at different times - would see doors, items and keys silently
        // fail to line up; this makes that loud instead.
        private readonly NetworkVariable<int> levelFingerprint = new NetworkVariable<int>();

        // The whole run's progress - keys, pages, zones, banishment, bolts -
        // written by the host's run director, rendered by the guest's.
        private readonly NetworkVariable<RunSnapshotNet> run = new NetworkVariable<RunSnapshotNet>();
        private SpawnDirector director;
        private readonly NetworkList<int> takenKeys = new NetworkList<int>();

        // The stalker thinks on the host only. Guests switch their copy's
        // brain off and follow these instead.
        private readonly NetworkList<StalkerPose> stalkerPoses = new NetworkList<StalkerPose>();
        private readonly List<EnemyStalkerAI> stalkers = new List<EnemyStalkerAI>();
        private float nextPoseSend;

        private bool ended;

        public override void OnNetworkSpawn()
        {
            BindStalkers();

            if (IsServer) levelFingerprint.Value = LevelFingerprint();
            else
            {
                levelFingerprint.OnValueChanged += OnLevelFingerprint;
                CheckLevel(levelFingerprint.Value);
            }

            // Before the seed: the run director reads who decides as soon as
            // the seed appears.
            CoopHooks.IsHost = IsServer;

            if (IsServer)
            {
                seed.Value = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

                SpawnProxies();
                NetworkManager.OnClientConnectedCallback += SpawnProxyFor;
            }

            // The key spawner has been waiting for exactly this.
            CoopHooks.RunSeed = seed.Value;

            takenKeys.OnListChanged += OnKeysChanged;
            ApplyAllKeys();

            InstallHooks();
            BindDirector();
        }

        private void BindDirector()
        {
            director = SpawnDirector.Instance;
            if (director == null) return;

            if (IsServer)
            {
                director.Changed += PushRun;
                PushRun();
                return;
            }

            run.OnValueChanged += OnRunChanged;
            if (run.Value.Valid) director.ApplyRemote(run.Value.Value);

            CoopHooks.RunRequested = (request, argument) =>
            {
                RunRequestRpc(request, argument);
                return true;
            };
        }

        private void PushRun()
        {
            if (director != null) run.Value = new RunSnapshotNet { Valid = true, Value = director.State };
        }

        private void OnRunChanged(RunSnapshotNet previous, RunSnapshotNet current)
        {
            if (director != null && current.Valid) director.ApplyRemote(current.Value);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RunRequestRpc(int request, int argument)
        {
            if (director != null) director.HandleRemote(request, argument);
        }

        public override void OnNetworkDespawn()
        {
            takenKeys.OnListChanged -= OnKeysChanged;
            run.OnValueChanged -= OnRunChanged;
            if (director != null) director.Changed -= PushRun;

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
            CoopHooks.CaptivityRequested = NetworkPlayer.HandleCaptivityRequest;

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

        // --- Same level on both machines? -------------------------------------------

        private static int LevelFingerprint()
        {
            SpawnDirector director = SpawnDirector.Instance;
            return PropSync.Fingerprint() ^ (director != null ? director.SpotFingerprint : 0);
        }

        private void OnLevelFingerprint(int previous, int current) => CheckLevel(current);

        private void CheckLevel(int hosts)
        {
            if (hosts == 0 || hosts == LevelFingerprint()) return;

            CoopSession.ReportLevelMismatch();
            ReportMismatchRpc();
            Debug.LogError("[FearMe] This level does not match the host's. Doors, items, keys and pages will not " +
                "line up between you. Save the scene, make one new build, and both play that same build.");
        }

        // So the host sees the warning too.
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void ReportMismatchRpc()
        {
            CoopSession.ReportLevelMismatch();
            Debug.LogError("[FearMe] Your partner's level does not match yours - make one new build and both play it.");
        }

        // --- The stalker ----------------------------------------------------------

        // Every stalker in the level, in an order both machines agree on. One
        // placed with its own NetworkObject (the old way) syncs itself.
        private void BindStalkers()
        {
            stalkers.Clear();
            foreach (EnemyStalkerAI stalker in FindObjectsByType<EnemyStalkerAI>(FindObjectsSortMode.None))
            {
                if (stalker.GetComponent<NetworkObject>() == null) stalkers.Add(stalker);
            }
            stalkers.Sort((a, b) => string.CompareOrdinal(SpawnDirector.PlaceOf(a.transform), SpawnDirector.PlaceOf(b.transform)));

            if (IsServer)
            {
                stalkerPoses.Clear();
                foreach (EnemyStalkerAI stalker in stalkers) stalkerPoses.Add(PoseOf(stalker));
                return;
            }

            // A guest's copy only shows where the host's is.
            foreach (EnemyStalkerAI stalker in stalkers)
            {
                stalker.enabled = false;
                NavMeshAgent agent = stalker.GetComponent<NavMeshAgent>();
                if (agent != null) agent.enabled = false;
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsServer) SendStalkers();
            else FollowStalkers();
        }

        private void SendStalkers()
        {
            if (Time.unscaledTime < nextPoseSend) return;
            nextPoseSend = Time.unscaledTime + 1f / 20f;

            for (int i = 0; i < stalkers.Count && i < stalkerPoses.Count; i++)
            {
                if (stalkers[i] == null) continue;

                StalkerPose pose = PoseOf(stalkers[i]);
                StalkerPose sent = stalkerPoses[i];
                if ((sent.position - pose.position).sqrMagnitude > 0.0004f || Mathf.Abs(Mathf.DeltaAngle(sent.yaw, pose.yaw)) > 1f)
                    stalkerPoses[i] = pose;
            }
        }

        private void FollowStalkers()
        {
            float ease = 1f - Mathf.Exp(-12f * Time.deltaTime);

            for (int i = 0; i < stalkers.Count && i < stalkerPoses.Count; i++)
            {
                if (stalkers[i] == null) continue;

                Transform t = stalkers[i].transform;
                StalkerPose pose = stalkerPoses[i];

                // A banishment or return is a jump across the map, not a walk.
                if ((t.position - pose.position).sqrMagnitude > 25f)
                    t.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, pose.yaw, 0f));
                else
                    t.SetPositionAndRotation(Vector3.Lerp(t.position, pose.position, ease),
                        Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, pose.yaw, 0f), ease));
            }
        }

        private static StalkerPose PoseOf(EnemyStalkerAI stalker) => new StalkerPose
        {
            position = stalker.transform.position,
            yaw = stalker.transform.eulerAngles.y
        };


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
        private void PropRpc(int id, int state, Vector3 value)
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
