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

        [Header("How a teammate looks")]
        [Tooltip("Drag a character model here to show it instead of the stand-in capsule. " +
            "It is scaled to Character Height and stood on the floor automatically.")]
        [SerializeField] private GameObject characterModel;
        [SerializeField] private float characterHeight = 1.8f;
        [Tooltip("Light enough to read in a dark corridor.")]
        [SerializeField] private Color bodyTint = new Color(0.6f, 0.56f, 0.5f);
        [Tooltip("A faint warm glow on them, so you can find each other in the dark.")]
        [SerializeField] private float presenceLight = 0.7f;

        private static readonly List<NetworkPlayer> all = new List<NetworkPlayer>();

        private readonly NetworkVariable<bool> down = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> dead = new NetworkVariable<bool>();
        private readonly NetworkVariable<float> bleedOut = new NetworkVariable<float>();

        // Dragged or caged, and by what (a PropSync id both machines share).
        private readonly NetworkVariable<int> captivity = new NetworkVariable<int>();
        private readonly NetworkVariable<int> anchorId = new NetworkVariable<int>();

        // Where the owner is and which way they face. Plain variables the
        // owner writes, rather than a NetworkTransform, whose ownership rules
        // have changed between Netcode versions.
        private readonly NetworkVariable<Vector3> netPosition = new NetworkVariable<Vector3>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<float> netYaw = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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

        private readonly NetworkVariable<FixedString32Bytes> displayName = new NetworkVariable<FixedString32Bytes>(default,
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
            if (IsOwner)
                displayName.Value = new FixedString32Bytes(FearMe.Settings.GameSettingsService.Current.playerName);

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
            DressTeammate();
            spawnedAt = Time.unscaledTime;
            Debug.Log("[FearMe] Teammate joined the level.");

            // Straight to wherever they already are, rather than gliding
            // there from the spawn point.
            if (netPosition.Value != Vector3.zero)
                transform.SetPositionAndRotation(netPosition.Value, Quaternion.Euler(0f, netYaw.Value, 0f));
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

            Vector3 position = local.transform.position;
            float yaw = local.transform.eulerAngles.y;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            // Only real movement is sent; standing still costs nothing.
            if ((netPosition.Value - position).sqrMagnitude > 0.0004f) netPosition.Value = position;
            if (Mathf.Abs(Mathf.DeltaAngle(netYaw.Value, yaw)) > 0.5f) netYaw.Value = yaw;

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
        private float spawnedAt;
        private bool warnedNoPosition;

        // The body in the prefab is a plain dark capsule, which in a dark
        // hospital is as good as invisible. Lighter, with a head, a faint
        // lamp on them and a place for their hands.
        private void DressTeammate()
        {
            // A real character, if one has been given: in place of the capsule.
            if (characterModel != null && transform.Find("Character") == null)
            {
                GameObject character = Instantiate(characterModel, transform);
                character.name = "Character";
                foreach (Collider c in character.GetComponentsInChildren<Collider>()) Destroy(c);
                FitToHeight(character.transform, characterHeight);
                if (body != null) body.SetActive(false);
            }

            Renderer bodyRenderer = body != null && body.activeSelf ? body.GetComponent<Renderer>() : null;
            if (bodyRenderer != null)
            {
                // An instanced material rather than a property block, so the
                // mimic's copy of this body comes out the same colour.
                Material skin = bodyRenderer.material;
                skin.color = bodyTint;
                if (skin.HasProperty("_BaseColor")) skin.SetColor("_BaseColor", bodyTint);

                MeshFilter mesh = body.GetComponent<MeshFilter>();
                if (body.transform.Find("Head") == null && mesh != null && mesh.sharedMesh != null &&
                    mesh.sharedMesh.name.StartsWith("Capsule"))
                {
                    GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    head.name = "Head";
                    Destroy(head.GetComponent<Collider>());
                    head.transform.SetParent(body.transform, false);
                    // Body is scaled (0.7, 0.9, 0.7); this lands a ~0.3m head on top.
                    head.transform.localPosition = new Vector3(0f, 1.05f, 0f);
                    head.transform.localScale = new Vector3(0.42f, 0.33f, 0.42f);
                    head.GetComponent<Renderer>().sharedMaterial = skin;
                }
            }

            if (transform.Find("PresenceLamp") == null && presenceLight > 0f)
            {
                Light lamp = new GameObject("PresenceLamp").AddComponent<Light>();
                lamp.transform.SetParent(transform, false);
                lamp.transform.localPosition = new Vector3(0f, 1.3f, 0.25f);
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.84f, 0.62f);
                lamp.intensity = presenceLight;
                lamp.range = 3f;
                lamp.shadows = LightShadows.None;
            }

            Transform hand = transform.Find("Hand");
            if (hand == null)
            {
                hand = new GameObject("Hand").transform;
                hand.SetParent(transform, false);
                hand.localPosition = new Vector3(0.32f, 1.05f, 0.42f);
            }
            avatar.HandAnchor = hand;
        }

        // Whatever size the model was made at, it ends up this tall with its
        // feet on the floor under the player.
        private void FitToHeight(Transform model, float height)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            if (bounds.size.y < 0.01f) return;

            model.localScale *= height / bounds.size.y;

            bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            model.position += Vector3.up * (transform.position.y - bounds.min.y);
        }

        private void MirrorRemoteState()
        {
            avatar.DisplayName = displayName.Value.ToString();

            // Once, if the other machine never says where its player is: the
            // one thing that makes a teammate invisible no matter what.
            if (!warnedNoPosition && netPosition.Value == Vector3.zero && Time.unscaledTime - spawnedAt > 5f)
            {
                warnedNoPosition = true;
                Debug.LogWarning("[FearMe] The teammate has not sent a position yet - their game may not have " +
                    "found its own player. Check their Console for errors.");
            }

            // Updates arrive a few times a tick; ease between them so the
            // body glides instead of stepping.
            float ease = 1f - Mathf.Exp(-15f * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, netPosition.Value, ease),
                Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, netYaw.Value, 0f), ease));

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
