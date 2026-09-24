#if FEARME_COOP_ONLINE
using System.Collections.Generic;
using FearMe.Core;
using FearMe.Player;
using Unity.Collections;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;

namespace FearMe.Net.Online
{
    // One per player, spawned by the server. It is a proxy, not the player:
    //
    //  - On its owner's machine it is invisible and follows the scene's own
    //    first-person player around, reporting where they are. The scene
    //    player keeps its camera, HUD, closets and every hand-wired reference.
    //  - On the other machine it is the teammate: a visible body with a torch,
    //    registered as a remote player, so the stalker can hunt it and you
    //    can revive it.
    //
    // Down, dead and the bleed-out clock belong to the server, and flow back
    // into both copies of the player's vitals.
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerController))]
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private GameObject body;
        [SerializeField] private Light torchBeam;
        [Tooltip("Where the second player starts, relative to the first.")]
        [SerializeField] private Vector3 secondPlayerOffset = new Vector3(1.5f, 0f, 0f);
        [Tooltip("Positions arrive a little late, so reviving gets some leeway.")]
        [SerializeField] private float reviveRangeSlack = 1.5f;

        private static readonly List<NetworkPlayer> all = new List<NetworkPlayer>();

        private readonly NetworkVariable<bool> down = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> dead = new NetworkVariable<bool>();
        private readonly NetworkVariable<float> bleedOut = new NetworkVariable<float>();

        // Dragged or caged, and by what (a PropSync id both machines share).
        private readonly NetworkVariable<int> captivity = new NetworkVariable<int>();
        private readonly NetworkVariable<int> anchorId = new NetworkVariable<int>();

        private readonly NetworkVariable<bool> torchOn = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<float> noise = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> hidden = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // In a hiding spot: holding breath, or peeking out. The demon on the
        // host needs both to know whether it can hear or see them.
        private readonly NetworkVariable<bool> breathHeld = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> peeking = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // The owner's sign-in id - what voice chat knows a speaker by - so a
        // voice can be put in the right mouth.
        private readonly NetworkVariable<FixedString64Bytes> authId = new NetworkVariable<FixedString64Bytes>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerController avatar;
        private PlayerVitals avatarVitals;
        private CharacterController avatarCollider;

        // Owner only: the scene's first-person player this proxy mirrors.
        private PlayerController local;
        private PlayerVitals localVitals;
        private Flashlight localTorch;

        private float serverBleedOut;

        public static IReadOnlyList<NetworkPlayer> All => all;

        private void Awake()
        {
            avatar = GetComponent<PlayerController>();
            avatarVitals = GetComponent<PlayerVitals>();
            avatarCollider = GetComponent<CharacterController>();

            // Before anything registers it: this is never the local player.
            avatar.IsLocalPlayer = false;
        }

        public override void OnNetworkSpawn()
        {
            all.Add(this);

            if (avatarVitals != null) avatarVitals.OwnsTimer = false;

            if (IsOwner && AuthenticationService.Instance.IsSignedIn)
                authId.Value = new FixedString64Bytes(AuthenticationService.Instance.PlayerId);

            if (IsOwner) BindToLocalPlayer();
            else ShowAsTeammate();

            down.OnValueChanged += OnVitalsChanged;
            dead.OnValueChanged += OnVitalsChanged;
            bleedOut.OnValueChanged += OnBleedOutChanged;
            torchOn.OnValueChanged += OnTorchChanged;
            captivity.OnValueChanged += OnCaptivityChanged;
            anchorId.OnValueChanged += OnCaptivityChanged;

            ApplyVitals();
            ApplyCaptivity();
            ApplyTorch();
        }

        public override void OnNetworkDespawn()
        {
            all.Remove(this);

            down.OnValueChanged -= OnVitalsChanged;
            dead.OnValueChanged -= OnVitalsChanged;
            bleedOut.OnValueChanged -= OnBleedOutChanged;
            torchOn.OnValueChanged -= OnTorchChanged;
            captivity.OnValueChanged -= OnCaptivityChanged;
            anchorId.OnValueChanged -= OnCaptivityChanged;

            // The session is over; the scene player can keep its own clock.
            if (localVitals != null) localVitals.OwnsTimer = true;
        }

        private void BindToLocalPlayer()
        {
            // The proxy is registered as remote, so this is the scene player.
            local = PlayerRegistry.Local;
            if (local == null)
            {
                Debug.LogWarning("[FearMe] No local player in the scene for the co-op proxy to follow.");
                return;
            }

            localVitals = local.GetComponent<PlayerVitals>();
            localTorch = local.GetComponentInChildren<Flashlight>();
            if (localVitals != null) localVitals.OwnsTimer = false;

            // Invisible and out of every system here: this machine already has
            // the real player, and a second one in the registry would double
            // the stalker's targets and the revive prompts.
            if (body != null) body.SetActive(false);
            if (torchBeam != null) torchBeam.enabled = false;
            avatar.enabled = false;
            if (avatarVitals != null) avatarVitals.enabled = false;
            if (avatarCollider != null) avatarCollider.enabled = false;

            // Both machines load the same player start; the guest steps aside.
            if (!IsServer)
            {
                Transform t = local.transform;
                local.Warp(t.position + t.rotation * secondPlayerOffset, t.eulerAngles.y);
            }

            FollowLocal();
        }

        private void ShowAsTeammate()
        {
            if (body != null) body.SetActive(true);
        }

        private void LateUpdate()
        {
            if (IsOwner) FollowLocal();
            else MirrorRemoteState();

            if (IsServer) TickBleedOut();
        }

        // The owner reports; NetworkTransform carries the position out.
        private void FollowLocal()
        {
            if (local == null) return;

            transform.SetPositionAndRotation(local.transform.position,
                Quaternion.Euler(0f, local.transform.eulerAngles.y, 0f));

            bool torch = localTorch != null && localTorch.IsOn;
            if (torchOn.Value != torch) torchOn.Value = torch;

            // Rounded so a jittering radius does not send an update every tick.
            float radius = Mathf.Round(local.CurrentNoiseRadius * 2f) * 0.5f;
            if (!Mathf.Approximately(noise.Value, radius)) noise.Value = radius;

            if (hidden.Value != local.IsHidden) hidden.Value = local.IsHidden;
            if (breathHeld.Value != local.HoldingBreath) breathHeld.Value = local.HoldingBreath;
            if (peeking.Value != local.IsPeeking) peeking.Value = local.IsPeeking;
        }

        // What the stalker on the server needs to know about someone it cannot
        // measure itself: how loud they are, and whether they are in a closet.
        private void MirrorRemoteState()
        {
            avatar.RemoteNoiseRadius = noise.Value;
            avatar.RemoteHidden = hidden.Value;
            avatar.RemoteHoldingBreath = breathHeld.Value;
            avatar.RemotePeeking = peeking.Value;
        }

        private void TickBleedOut()
        {
            if (!down.Value || dead.Value) return;

            serverBleedOut -= Time.deltaTime;

            // Whole seconds are all the HUD shows, so that is all that is sent.
            float shown = Mathf.Max(0f, Mathf.Ceil(serverBleedOut));
            if (!Mathf.Approximately(bleedOut.Value, shown)) bleedOut.Value = shown;

            if (serverBleedOut <= 0f)
            {
                captivity.Value = (int)Captivity.None;
                dead.Value = true;
            }
        }

        // --- Server authority ------------------------------------------------

        public void ServerGoDown()
        {
            if (!IsServer || down.Value || dead.Value) return;

            serverBleedOut = avatarVitals != null ? avatarVitals.BleedOutSeconds : 45f;
            bleedOut.Value = serverBleedOut;
            down.Value = true;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestDownRpc()
        {
            ServerGoDown();
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestReviveRpc(RpcParams rpcParams = default)
        {
            if (!down.Value || dead.Value) return;

            NetworkPlayer rescuer = ForClient(rpcParams.Receive.SenderClientId);
            if (rescuer == null || rescuer == this) return;

            // Checked here too, so a revive cannot come from across the map.
            float range = (avatarVitals != null ? avatarVitals.ReviveRange : 2.5f) + reviveRangeSlack;
            if (Vector3.Distance(rescuer.transform.position, transform.position) > range) return;

            captivity.Value = (int)Captivity.None;
            down.Value = false;
        }

        public void ServerSetCaptivity(int hold, int anchor)
        {
            if (!IsServer || dead.Value) return;

            // Anchor first, so the change that follows already has somewhere
            // to put them.
            anchorId.Value = hold == (int)Captivity.None ? 0 : anchor;
            captivity.Value = hold;
        }

        // A guest can only ever let someone go - breaking a cage lock. Being
        // grabbed or caged comes from the stalker, which lives on the host.
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestReleaseRpc()
        {
            ServerSetCaptivity((int)Captivity.None, 0);
        }

        // --- Hooks, installed by NetworkRunState --------------------------------

        internal static bool HandleDownRequest(PlayerVitals vitals)
        {
            NetworkPlayer player = For(vitals);
            if (player == null) return false;

            if (player.IsServer) player.ServerGoDown();
            else player.RequestDownRpc();
            return true;
        }

        internal static bool HandleCaptivityRequest(PlayerVitals vitals, int hold, int anchor)
        {
            NetworkPlayer player = For(vitals);
            if (player == null) return false;

            if (player.IsServer) player.ServerSetCaptivity(hold, anchor);
            else if (hold == (int)Captivity.None) player.RequestReleaseRpc();
            return true;
        }

        internal static bool HandleReviveRequest(PlayerVitals vitals)
        {
            NetworkPlayer player = For(vitals);
            if (player == null) return false;

            player.RequestReviveRpc();
            return true;
        }

        // Either copy of a player's vitals leads back to the same proxy: the
        // visible body's, or the scene player's on its owner's machine.
        private static NetworkPlayer For(PlayerVitals vitals)
        {
            if (vitals == null) return null;

            foreach (NetworkPlayer player in all)
            {
                if (player.avatarVitals == vitals || player.localVitals == vitals) return player;
            }
            return null;
        }

        // The visible body of whoever signed in as this id, on this machine.
        public static Transform BodyOf(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;

            foreach (NetworkPlayer player in all)
            {
                if (player != null && !player.IsOwner && player.authId.Value.ToString() == playerId)
                    return player.transform;
            }
            return null;
        }

        private static NetworkPlayer ForClient(ulong clientId)
        {
            foreach (NetworkPlayer player in all)
            {
                if (player.OwnerClientId == clientId) return player;
            }
            return null;
        }

        // --- Applying server state ---------------------------------------------

        private void OnVitalsChanged(bool previous, bool current) => ApplyVitals();

        private void OnBleedOutChanged(float previous, float current) => ApplyVitals();

        private void ApplyVitals()
        {
            float remaining = bleedOut.Value;

            if (avatarVitals != null && avatarVitals.enabled)
                avatarVitals.ApplyNetworkVitals(down.Value, dead.Value, remaining);

            if (localVitals != null)
                localVitals.ApplyNetworkVitals(down.Value, dead.Value, remaining);
        }

        private void OnCaptivityChanged(int previous, int current) => ApplyCaptivity();

        private void ApplyCaptivity()
        {
            Captivity hold = (Captivity)captivity.Value;
            ICaptiveAnchor anchor = hold == Captivity.None ? null : PropSync.Find<ICaptiveAnchor>(anchorId.Value);

            if (avatarVitals != null && avatarVitals.enabled) avatarVitals.ApplyCaptivity(hold, anchor);
            if (localVitals != null) localVitals.ApplyCaptivity(hold, anchor);
        }

        private void OnTorchChanged(bool previous, bool current) => ApplyTorch();

        private void ApplyTorch()
        {
            if (torchBeam != null) torchBeam.enabled = !IsOwner && torchOn.Value;
        }
    }
}
#endif
