using System.Collections.Generic;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    // Where the stalker takes a player it has caught while their partner is
    // still up. The partner has until the bleed-out runs dry to get here and
    // hold the key on the lock - which is loud, and the stalker patrols back
    // past the cage it just filled.
    //
    // Occupancy is never stored: it is read off whoever's vitals say they are
    // caged here, which both machines already agree on.
    public class CageSpot : Interactable, ICaptiveAnchor
    {
        private static readonly List<CageSpot> all = new List<CageSpot>();

        [Tooltip("Where the captive is held. Defaults to this object.")]
        [SerializeField] private Transform holdPoint;
        [Tooltip("Where the stalker stands to put them in. Defaults to just in front.")]
        [SerializeField] private Transform dropOffPoint;

        [Header("Breaking out")]
        [SerializeField] private float breakSeconds = 4f;
        [Tooltip("Rattling the lock is loud - this often, this far.")]
        [SerializeField] private float noiseInterval = 0.6f;
        [SerializeField] private float noiseRadius = 13f;

        [Header("Door (optional)")]
        [SerializeField] private Transform door;
        [SerializeField] private float doorOpenAngle = 100f;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip rattleClip;
        [SerializeField] private AudioClip breakClip;

        private float progress;
        private int lastInteractFrame = -10;
        private float nextNoise;
        private int anchorId;
        private float doorAngle;

        public static IReadOnlyList<CageSpot> All => all;

        public int AnchorId => anchorId;
        public Transform HoldPoint => holdPoint != null ? holdPoint : transform;
        public Vector3 DropOff => dropOffPoint != null ? dropOffPoint.position : transform.position + transform.forward * 1.2f;

        public PlayerVitals Occupant
        {
            get
            {
                foreach (PlayerController player in PlayerRegistry.All)
                {
                    if (player == null) continue;

                    PlayerVitals vitals = player.GetComponent<PlayerVitals>();
                    if (vitals != null && vitals.Captivity == Captivity.Caged && ReferenceEquals(vitals.Anchor, this))
                        return vitals;
                }
                return null;
            }
        }

        public bool IsOccupied => Occupant != null;

        // Nothing to do at an empty cage, so it should not even be a target.
        public override string Prompt => IsOccupied ? "Hold to break the lock" : string.Empty;
        public override bool HoldToUse => true;

        public float Progress => Mathf.Clamp01(progress / Mathf.Max(0.01f, breakSeconds));

        private void Awake()
        {
            // Registered for the object's whole life, not just while enabled,
            // so a caught player can always be pointed back at it.
            anchorId = PropSync.Register(this, (state, value) => { });
        }

        private void OnDestroy()
        {
            PropSync.Unregister(anchorId);
        }

        private void OnEnable()
        {
            all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        // Nearest free cage, preferring this storey: a cage on the floor above
        // is a long drag the rescuer would never guess.
        public static CageSpot NearestFree(Vector3 position, float storeyGap = 2.5f)
        {
            CageSpot best = null;
            float bestScore = float.MaxValue;

            foreach (CageSpot cage in all)
            {
                if (cage == null || cage.IsOccupied) continue;

                Vector3 delta = cage.transform.position - position;
                float score = delta.sqrMagnitude;
                if (Mathf.Abs(delta.y) > storeyGap) score += 10000f;

                if (score >= bestScore) continue;
                bestScore = score;
                best = cage;
            }

            return best;
        }

        public override void Interact()
        {
            PlayerVitals occupant = Occupant;
            if (occupant == null) return;

            // Not from inside: the captive holding E would free themselves.
            if (PlayerRegistry.Local != null && PlayerRegistry.Local.GetComponent<PlayerVitals>() == occupant) return;

            lastInteractFrame = Time.frameCount;
            progress += Time.deltaTime;

            if (Time.time >= nextNoise)
            {
                nextNoise = Time.time + noiseInterval;
                NoiseBus.Emit(transform.position, noiseRadius);
                if (audioSource != null && rattleClip != null) audioSource.PlayOneShot(rattleClip, 0.8f);
            }

            if (progress >= breakSeconds) Free(occupant);
        }

        private void Free(PlayerVitals occupant)
        {
            progress = 0f;
            if (audioSource != null && breakClip != null) audioSource.PlayOneShot(breakClip);

            occupant.SetCaptivity(Captivity.None, null);
            occupant.Revive();
        }

        private void Update()
        {
            // Let go of the lock and the progress slips back.
            if (Time.frameCount - lastInteractFrame > 1)
                progress = Mathf.MoveTowards(progress, 0f, Time.deltaTime * 0.5f);

            if (door == null) return;

            float target = IsOccupied ? 0f : doorOpenAngle;
            doorAngle = Mathf.MoveTowards(doorAngle, target, 240f * Time.deltaTime);
            door.localRotation = Quaternion.Euler(0f, doorAngle, 0f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.8f, 0.15f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(HoldPoint.position + Vector3.up * 0.9f, new Vector3(1f, 1.8f, 1f));
            Gizmos.DrawLine(HoldPoint.position, DropOff);
        }
    }
}
