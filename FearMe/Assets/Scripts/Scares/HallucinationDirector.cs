using System.Collections;
using FearMe.Core;
using FearMe.Player;
using UnityEngine;
using UnityEngine.AI;

namespace FearMe.Scares
{
    // Once sanity is low enough, starts putting things in the world for this
    // player alone: a key that is not a key, a door in a wall with no door
    // behind it, a tall shape at the end of the corridor that is gone when
    // you look straight at it.
    //
    // The co-op point is the one-sided panic: you shout that it is right
    // there, and your partner, looking at the same corridor, sees nothing.
    [RequireComponent(typeof(PlayerSanity))]
    public class HallucinationDirector : MonoBehaviour
    {
        [Tooltip("Sanity below which things start appearing.")]
        [SerializeField, Range(0f, 1f)] private float threshold = 0.35f;
        [Tooltip("Seconds between hallucinations, at the threshold and at rock bottom.")]
        [SerializeField] private Vector2 interval = new Vector2(22f, 7f);

        [Header("What it can show (all optional)")]
        [Tooltip("A door model to put in a wall. A dark slab otherwise.")]
        [SerializeField] private GameObject fakeDoorPrefab;
        [Tooltip("A tall figure. A stretched black shape otherwise.")]
        [SerializeField] private GameObject shadowFigurePrefab;
        [SerializeField] private AudioClip[] whispers;
        [SerializeField] private LayerMask obstructionMask = ~0;

        [Header("Cost")]
        [Tooltip("Sanity lost when one turns out not to be real.")]
        [SerializeField] private float fooledCost = 0.08f;

        private PlayerSanity sanity;
        private PlayerController player;
        private Transform eye;
        private float nextAt;
        private Material voidMaterial;

        private void Awake()
        {
            sanity = GetComponent<PlayerSanity>();
            player = GetComponent<PlayerController>();
            Camera view = GetComponentInChildren<Camera>();
            eye = view != null ? view.transform : transform;
        }

        private void Start()
        {
            if (!player.IsLocalPlayer) enabled = false;
            nextAt = Time.time + interval.x;
        }

        private void OnDestroy()
        {
            if (voidMaterial != null) Destroy(voidMaterial);
        }

        private void Update()
        {
            if (sanity.Sanity > threshold || Time.time < nextAt || player.IsHidden) return;

            float depth = Mathf.InverseLerp(threshold, 0f, sanity.Sanity);
            nextAt = Time.time + Mathf.Lerp(interval.x, interval.y, depth);

            // Try them in a random order; whichever can be placed, is.
            int start = Random.Range(0, 3);
            for (int i = 0; i < 3; i++)
            {
                bool placed = ((start + i) % 3) switch
                {
                    0 => FakeKey(),
                    1 => FakeDoor(),
                    _ => ShadowFigure()
                };
                if (placed) return;
            }
        }

        // --- Fake key -------------------------------------------------------------

        private bool FakeKey()
        {
            if (!TryFloorSpot(5f, 13f, 50f, out Vector3 spot)) return false;

            GameObject look = CloneOfARealKey();
            if (look == null)
            {
                look = GameObject.CreatePrimitive(PrimitiveType.Cube);
                look.transform.localScale = new Vector3(0.12f, 0.25f, 0.04f);
                look.GetComponent<Renderer>().material.color = new Color(0.8f, 0.65f, 0.25f);
            }

            look.name = "Hallucination_Key";
            look.transform.position = spot + Vector3.up * 0.9f;

            // A solid collider, so the interaction ray finds it like a real one.
            // Added fresh, it sizes itself to the mesh.
            foreach (Collider c in look.GetComponentsInChildren<Collider>()) Destroy(c);
            look.AddComponent<BoxCollider>();

            look.AddComponent<HallucinationProp>().Setup("Take key", sanity, whispers, fooledCost, 60f, 40f);
            return true;
        }

        // Looks exactly like the ones that count - which is the whole trick.
        private static GameObject CloneOfARealKey()
        {
            KeyItem real = FindFirstObjectByType<KeyItem>();
            if (real == null) return null;

            GameObject copy = Instantiate(real.gameObject);
            // Gone before it can register, spin or be counted.
            DestroyImmediate(copy.GetComponent<KeyItem>());
            return copy;
        }

        // --- Fake door --------------------------------------------------------------

        private bool FakeDoor()
        {
            // A wall you can see, with room in front of it.
            for (int i = 0; i < 12; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(Random.Range(-50f, 50f), Vector3.up) * Flat(eye.forward);
                if (!Physics.Raycast(eye.position, direction, out RaycastHit hit, 12f, obstructionMask,
                        QueryTriggerInteraction.Ignore))
                    continue;

                if (hit.distance < 4f || Mathf.Abs(hit.normal.y) > 0.2f) continue;

                if (!NavMeshUtility.TrySampleSameFloor(hit.point + hit.normal * 0.6f, out Vector3 floor)) continue;

                Vector3 normal = Flat(hit.normal);
                Vector3 at = new Vector3(hit.point.x, floor.y, hit.point.z) + normal * 0.03f;

                GameObject door = fakeDoorPrefab != null ? Instantiate(fakeDoorPrefab) : Slab(new Vector3(1f, 2.1f, 0.06f));
                door.name = "Hallucination_Door";
                door.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal));

                if (door.GetComponentInChildren<Collider>() == null) door.AddComponent<BoxCollider>();

                door.AddComponent<HallucinationProp>().Setup("Open", sanity, whispers, fooledCost, 0f, 60f);
                return true;
            }

            return false;
        }

        // --- Shadow figure ----------------------------------------------------------

        private bool ShadowFigure()
        {
            if (!TryFloorSpot(10f, 18f, 70f, out Vector3 spot)) return false;

            GameObject figure = shadowFigurePrefab != null
                ? Instantiate(shadowFigurePrefab)
                : Slab(new Vector3(0.55f, 2.4f, 0.35f));

            figure.name = "Hallucination_Shadow";
            foreach (Collider c in figure.GetComponentsInChildren<Collider>()) Destroy(c);

            Vector3 toPlayer = Flat(transform.position - spot);
            figure.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(toPlayer));

            StartCoroutine(Watch(figure));
            return true;
        }

        // There until you look straight at it, or until you look away for good.
        private IEnumerator Watch(GameObject figure)
        {
            float life = 5f;
            while (life > 0f && figure != null)
            {
                life -= Time.deltaTime;

                Vector3 toFigure = figure.transform.position + Vector3.up * 1.2f - eye.position;
                if (Vector3.Angle(eye.forward, toFigure) < 6f) break;

                yield return null;
            }

            if (figure == null) yield break;

            if (whispers != null && whispers.Length > 0)
            {
                AudioClip clip = whispers[Random.Range(0, whispers.Length)];
                if (clip != null) AudioSource.PlayClipAtPoint(clip, figure.transform.position, 0.7f);
            }

            sanity.Shake(fooledCost * 0.5f);
            Destroy(figure);
        }

        // --- Helpers -----------------------------------------------------------------

        // On this floor, in front of you, and in plain view.
        private bool TryFloorSpot(float minDistance, float maxDistance, float maxAngle, out Vector3 spot)
        {
            spot = Vector3.zero;
            Vector3 forward = Flat(eye.forward);

            for (int i = 0; i < 16; i++)
            {
                Vector3 candidate = transform.position +
                    Quaternion.AngleAxis(Random.Range(-maxAngle, maxAngle), Vector3.up) * forward *
                    Random.Range(minDistance, maxDistance);

                if (!NavMeshUtility.TrySampleSameFloor(candidate, out Vector3 onMesh)) continue;
                if (NavMesh.Raycast(transform.position, onMesh, out NavMeshHit _, NavMesh.AllAreas)) continue;

                if (Physics.Linecast(eye.position, onMesh + Vector3.up * 1f, obstructionMask,
                        QueryTriggerInteraction.Ignore))
                    continue;

                spot = onMesh;
                return true;
            }

            return false;
        }

        private GameObject Slab(Vector3 size)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.transform.localScale = Vector3.one;

            // Mesh scaled into a child, so the root's pivot sits on the floor.
            GameObject root = new GameObject();
            slab.transform.SetParent(root.transform, false);
            slab.transform.localScale = size;
            slab.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);

            if (voidMaterial == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                voidMaterial = new Material(unlit != null ? unlit : Shader.Find("Unlit/Color")) { color = new Color(0.01f, 0.01f, 0.012f) };
            }
            slab.GetComponent<Renderer>().sharedMaterial = voidMaterial;

            return root;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }
    }
}
