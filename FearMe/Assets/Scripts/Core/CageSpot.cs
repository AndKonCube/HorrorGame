using System.Collections.Generic;
using FearMe.AI;
using FearMe.Player;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

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
        private Quaternion doorRest;

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
            if (door != null) doorRest = door.localRotation;
        }

        private void OnDestroy()
        {
            PropSync.Unregister(anchorId);
        }

        // A cage built at runtime is known by where it stands, which both
        // machines work out the same way, not by its place in the hierarchy.
        private void UseFixedId(int id)
        {
            PropSync.Unregister(anchorId);
            anchorId = PropSync.RegisterWithId(id, this, (state, value) => { });
        }

        // --- Cages for a level that has none ------------------------------------
        //
        // Without a cage, a caught player is just left lying where they fell,
        // and the demon shoves them about. So any level with a demon and no
        // cages gets two, at the patrol points furthest from where the run
        // starts - scene data, so both machines build them in the same places.

        private const int FallbackCages = 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void WatchScenes()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureCages();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureCages();

        private static void EnsureCages()
        {
            if (FindObjectsByType<CageSpot>(FindObjectsSortMode.None).Length > 0) return;
            if (FindFirstObjectByType<EnemyStalkerAI>() == null) return;

            List<Vector3> candidates = new List<Vector3>();
            PatrolRoute[] routes = FindObjectsByType<PatrolRoute>(FindObjectsSortMode.None);
            System.Array.Sort(routes, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (PatrolRoute route in routes)
            {
                for (int i = 0; i < route.Count; i++)
                {
                    Transform waypoint = route.GetWaypoint(i);
                    if (waypoint != null && NavMesh.SamplePosition(waypoint.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                        candidates.Add(hit.position);
                }
            }

            if (candidates.Count == 0)
            {
                Debug.LogWarning("[FearMe] No cages and no patrol waypoints to put any at - a caught player " +
                    "will be left where they fall. Add cages with Tools/FearMe/Gameplay/Add Co-op Gameplay.");
                return;
            }

            Vector3 start = StartPosition();
            List<Vector3> chosen = new List<Vector3>();
            for (int n = 0; n < FallbackCages && n < candidates.Count; n++)
            {
                Vector3 best = candidates[0];
                float bestScore = -1f;
                foreach (Vector3 c in candidates)
                {
                    float score = Vector3.Distance(c, start);
                    foreach (Vector3 p in chosen) score = Mathf.Min(score, Vector3.Distance(c, p));
                    if (score > bestScore) { bestScore = score; best = c; }
                }
                chosen.Add(best);
            }

            for (int i = 0; i < chosen.Count; i++) BuildCage(chosen[i], start, i);
            Debug.Log($"[FearMe] No cages in this level; built {chosen.Count} at the far patrol points.");
        }

        // Where the scene's own player stands before anyone moves - the same
        // on both machines.
        private static Vector3 StartPosition()
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (player.IsLocalPlayer) return player.transform.position;
            return Vector3.zero;
        }

        private static void BuildCage(Vector3 at, Vector3 facing, int index)
        {
            GameObject root = new GameObject("Cage (runtime)");
            Vector3 toward = facing - at;
            toward.y = 0f;
            root.transform.SetPositionAndRotation(at,
                Quaternion.LookRotation(toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward));

            Material iron = RuntimeMaterials.Iron;
            const float half = 0.65f;
            const float height = 2.1f;

            for (float x = -half; x <= half + 0.001f; x += 0.2f)
                Bar(root.transform, new Vector3(x, height * 0.5f, -half), new Vector3(0.04f, height, 0.04f), iron);
            for (float z = -half + 0.2f; z <= half + 0.001f; z += 0.2f)
            {
                Bar(root.transform, new Vector3(-half, height * 0.5f, z), new Vector3(0.04f, height, 0.04f), iron);
                Bar(root.transform, new Vector3(half, height * 0.5f, z), new Vector3(0.04f, height, 0.04f), iron);
            }
            Bar(root.transform, new Vector3(0f, height, 0f), new Vector3(half * 2f, 0.05f, half * 2f), iron);

            Transform door = new GameObject("Door").transform;
            door.SetParent(root.transform, false);
            door.localPosition = new Vector3(-half, 0f, half);
            for (float x = 0.2f; x <= half * 2f + 0.001f; x += 0.2f)
                Bar(door, new Vector3(x, height * 0.5f, 0f), new Vector3(0.04f, height, 0.04f), iron);

            // One solid panel across the door: what the rescuer aims at to
            // work the lock, and what keeps anyone from walking in or out.
            BoxCollider panel = door.gameObject.AddComponent<BoxCollider>();
            panel.center = new Vector3(half, height * 0.5f, 0f);
            panel.size = new Vector3(half * 2f, height, 0.08f);

            Transform hold = new GameObject("HoldPoint").transform;
            hold.SetParent(root.transform, false);

            Transform dropOff = new GameObject("DropOff").transform;
            dropOff.SetParent(root.transform, false);
            dropOff.localPosition = new Vector3(0f, 0f, half + 0.9f);

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.maxDistance = 25f;

            // Fields set before the component wakes, via a disabled parent.
            root.SetActive(false);
            CageSpot cage = root.AddComponent<CageSpot>();
            cage.holdPoint = hold;
            cage.dropOffPoint = dropOff;
            cage.door = door;
            cage.audioSource = audio;
            root.SetActive(true);

            // Rounded to the decimetre: identical on both machines.
            Vector3Int cell = Vector3Int.RoundToInt(at * 10f);
            cage.UseFixedId(unchecked((int)0x6A0E0000 ^ (index * 7919) ^ cell.GetHashCode()));
        }

        private static void Bar(Transform parent, Vector3 position, Vector3 size, Material material)
        {
            RuntimeMaterials.Part(PrimitiveType.Cube, parent, position, size, Vector3.zero, material);
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
            door.localRotation = doorRest * Quaternion.Euler(0f, doorAngle, 0f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.8f, 0.15f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(HoldPoint.position + Vector3.up * 0.9f, new Vector3(1f, 1.8f, 1f));
            Gizmos.DrawLine(HoldPoint.position, DropOff);
        }
    }
}
