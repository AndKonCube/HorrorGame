using FearMe.Items;
using FearMe.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Core
{
    // A lever too stiff to lock: it only stays down while someone hangs on to
    // it, and while they do they cannot move a step. It grinds, loudly, the
    // whole time. Meanwhile their partner goes through the gate it holds open
    // and into the dark on the other side.
    //
    // Once pulled, the holder can look around - they just cannot leave.
    // Letting go of the key lets go of the lever.
    //
    // Solo, there is nobody to hold it, so letting go latches the gate open
    // for a short while instead - enough to make a run for it.
    public class HeavyLever : Interactable
    {
        [Header("Handle")]
        [SerializeField] private Transform handle;
        [SerializeField] private Vector3 pulledRotation = new Vector3(-70f, 0f, 0f);
        [Tooltip("Seconds of hauling before it is all the way down.")]
        [SerializeField] private float pullSeconds = 1.2f;
        [Tooltip("How far the holder can drift before losing their grip.")]
        [SerializeField] private float gripRange = 2.5f;

        [Header("Noise")]
        [SerializeField] private float noiseRadius = 12f;
        [SerializeField] private float noiseInterval = 0.8f;
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Looped while it is held down.")]
        [SerializeField] private AudioClip strainLoop;
        [SerializeField] private AudioClip releaseClip;

        [Header("Solo")]
        [SerializeField] private float soloLatchSeconds = 12f;

        private const int Engaged = 1;
        private const int Released = 0;

        private Quaternion restRotation;
        private float pull;
        private PlayerController holder;
        private InputAction holdAction;
        private float heldSpeed;
        private bool engaged;
        private bool heldRemotely;
        private float latchUntil;
        private float nextNoise;
        private int propId;

        // Held all the way down, here or by the other player - or latched solo.
        public bool GateOpen => engaged || heldRemotely || Time.time < latchUntil;

        public override string Prompt
        {
            get
            {
                if (heldRemotely) return string.Empty;
                if (holder == null && ArmsFull(PlayerRegistry.Local)) return "Your arms are full";
                return holder != null ? "Keep holding" : "Hold to pull the lever";
            }
        }

        public override bool HoldToUse => true;

        private void Awake()
        {
            if (handle == null) handle = transform;
            restRotation = handle.localRotation;
            propId = PropSync.Register(this, ApplyRemote);
        }

        private void OnDestroy()
        {
            PropSync.Unregister(propId);
            LetGo();
        }

        // First frame of holding it: take hold. After that the lever watches
        // the key itself, so the holder is free to look away from it.
        public override void Interact()
        {
            if (holder != null || heldRemotely) return;

            PlayerController local = PlayerRegistry.Local;
            if (local == null || ArmsFull(local)) return;

            PlayerInput input = local.GetComponent<PlayerInput>();
            if (input == null) return;

            holder = local;
            holdAction = input.actions["Interact"];

            // Rooted to the spot while hanging on to it.
            heldSpeed = holder.SpeedMultiplier;
            holder.SpeedMultiplier = 0f;
        }

        private void Update()
        {
            if (holder != null) TickHeld();
            else pull = Mathf.MoveTowards(pull, heldRemotely ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, pullSeconds) * 2f);

            handle.localRotation = restRotation * Quaternion.Slerp(Quaternion.identity,
                Quaternion.Euler(pulledRotation), pull);
        }

        private void TickHeld()
        {
            bool stillHolding = holdAction != null && holdAction.IsPressed()
                && Vector3.Distance(holder.transform.position, transform.position) <= gripRange;

            PlayerVitals vitals = holder.GetComponent<PlayerVitals>();
            if (vitals != null && vitals.IsDown) stillHolding = false;

            if (!stillHolding)
            {
                LetGo();
                return;
            }

            pull = Mathf.MoveTowards(pull, 1f, Time.deltaTime / Mathf.Max(0.05f, pullSeconds));
            if (pull >= 1f && !engaged) Engage();

            if (engaged && Time.time >= nextNoise)
            {
                nextNoise = Time.time + noiseInterval;
                NoiseBus.Emit(transform.position, noiseRadius);
            }
        }

        private void Engage()
        {
            engaged = true;
            nextNoise = Time.time;

            if (audioSource != null && strainLoop != null)
            {
                audioSource.clip = strainLoop;
                audioSource.loop = true;
                audioSource.Play();
            }

            PropSync.Publish(propId, Engaged, Vector3.zero);
        }

        private void LetGo()
        {
            if (holder == null) return;

            holder.SpeedMultiplier = heldSpeed;
            holder = null;
            holdAction = null;

            if (!engaged) return;
            engaged = false;

            if (audioSource != null)
            {
                audioSource.Stop();
                if (releaseClip != null) audioSource.PlayOneShot(releaseClip);
            }

            // Nobody else to hold it: give the one player a head start instead.
            if (PlayerRegistry.All.Count < 2) latchUntil = Time.time + soloLatchSeconds;

            PropSync.Publish(propId, Released, Vector3.zero);
        }

        // Both hands round something heavy leaves none for the lever.
        private static bool ArmsFull(PlayerController player)
        {
            PlayerHands hands = player != null ? player.GetComponent<PlayerHands>() : null;
            return hands != null && hands.Held is HeavyItem;
        }

        private void ApplyRemote(int state, Vector3 value)
        {
            heldRemotely = state == Engaged;
        }
    }
}
