using System.Collections;
using FearMe.Core;
using FearMe.Player;
using UnityEngine;
using UnityEngine.AI;

namespace FearMe.Scares
{
    // Down the corridor, back turned, torch pointing away: your partner. You
    // walk up to them. It is not your partner.
    //
    // Get close, or stare too long, and it turns round - too fast, all at
    // once - screams, and is gone. The scream is real noise, at your feet,
    // loud enough to bring the stalker straight to you.
    //
    // It only ever exists on the machine of the player who sees it. The real
    // partner has no idea, which is the point: next time, you will not trust
    // the figure in the corridor even when it is them.
    public class MimicScare : ScareEvent
    {
        [Tooltip("What it looks like. Left empty, it copies the teammate's own body " +
            "at runtime, or falls back to a plain dark figure.")]
        [SerializeField] private GameObject mimicBody;
        [SerializeField] private LayerMask obstructionMask = ~0;
        [Tooltip("Only when there is a real partner to be mistaken for.")]
        [SerializeField] private bool coopOnly;

        [Header("Placement")]
        [SerializeField] private float minDistance = 12f;
        [SerializeField] private float maxDistance = 22f;
        [SerializeField] private float maxViewAngle = 40f;
        [Tooltip("Never near the real partner - that would give it away.")]
        [SerializeField] private float partnerClearance = 9f;
        [SerializeField] private int placementAttempts = 30;

        [Header("Reveal")]
        [SerializeField] private float revealDistance = 5f;
        [Tooltip("Staring at it this long from within stareRange also turns it.")]
        [SerializeField] private float stareSeconds = 3.5f;
        [SerializeField] private float stareRange = 10f;
        [SerializeField] private float lookedAtAngle = 8f;
        [Tooltip("Left alone this long, it slips away when not being looked at.")]
        [SerializeField] private float lingerSeconds = 25f;

        [Header("Scream")]
        [SerializeField] private AudioClip screamClip;
        [Tooltip("The scream gives you away: this much noise, at your position.")]
        [SerializeField] private float screamNoiseRadius = 28f;

        private Coroutine active;
        private Light torch;

        public override bool CanPlay(ScareContext context)
        {
            if (!base.CanPlay(context) || context.PlayerHidden || active != null) return false;
            return !coopOnly || Partner() != null;
        }

        protected override bool OnTrigger(ScareContext context)
        {
            if (!TryFindSpot(context, out Vector3 spot)) return false;

            GameObject body = EnsureBody();
            if (body == null) return false;

            active = StartCoroutine(Stand(context, body, spot));
            return true;
        }

        private IEnumerator Stand(ScareContext context, GameObject body, Vector3 spot)
        {
            // Back to you, facing the same way you are - as if walking ahead.
            Vector3 away = spot - context.Player.position;
            away.y = 0f;
            body.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(away.normalized));
            body.SetActive(true);
            if (torch != null) torch.enabled = true;

            float elapsed = 0f;
            float stared = 0f;

            while (true)
            {
                elapsed += Time.deltaTime;

                Vector3 toBody = body.transform.position - context.Player.position;
                float distance = toBody.magnitude;
                bool looking = context.Eye != null &&
                    Vector3.Angle(context.Eye.forward, body.transform.position + Vector3.up * 1.2f - context.Eye.position) <= lookedAtAngle;

                stared = looking && distance <= stareRange ? stared + Time.deltaTime : 0f;

                if (distance <= revealDistance || stared >= stareSeconds)
                {
                    yield return Reveal(context, body);
                    break;
                }

                // Nobody came. It goes quietly, while not being watched.
                if (elapsed >= lingerSeconds && !looking) break;

                yield return null;
            }

            body.SetActive(false);
            active = null;
        }

        private IEnumerator Reveal(ScareContext context, GameObject body)
        {
            // All at once, not a turn: the wrongness is in the speed.
            Vector3 toPlayer = context.Player.position - body.transform.position;
            toPlayer.y = 0f;
            body.transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
            if (torch != null) torch.enabled = false;

            if (screamClip != null) AudioSource.PlayClipAtPoint(screamClip, body.transform.position + Vector3.up * 1.6f);

            // Heard where you are standing, so it comes for you, not for it.
            NoiseBus.Emit(context.Player.position, screamNoiseRadius);

            if (VolumetricFogController.Instance != null) VolumetricFogController.Instance.Pulse(0.05f);

            yield return new WaitForSeconds(0.6f);
        }

        private bool TryFindSpot(ScareContext context, out Vector3 spot)
        {
            spot = Vector3.zero;
            if (context.Eye == null || context.Player == null) return false;

            Vector3 forward = context.Eye.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) return false;
            forward.Normalize();

            PlayerController partner = Partner();

            for (int i = 0; i < placementAttempts; i++)
            {
                float angle = Random.Range(-maxViewAngle, maxViewAngle);
                float distance = Random.Range(minDistance, maxDistance);
                Vector3 candidate = context.Player.position +
                    Quaternion.AngleAxis(angle, Vector3.up) * forward * distance;

                if (!NavMeshUtility.TrySampleSameFloor(candidate, out Vector3 onMesh)) continue;

                // Down a corridor you could walk, not through a wall: a NavMesh
                // raycast returns true when it runs into an edge on the way.
                if (NavMesh.Raycast(context.Player.position, onMesh, out NavMeshHit _, NavMesh.AllAreas))
                    continue;

                Vector3 chest = onMesh + Vector3.up * 1.2f;
                if (Physics.Linecast(context.Eye.position, chest, obstructionMask, QueryTriggerInteraction.Ignore))
                    continue;

                if (partner != null && Vector3.Distance(partner.transform.position, onMesh) < partnerClearance)
                    continue;

                spot = onMesh;
                return true;
            }

            return false;
        }

        private static PlayerController Partner()
        {
            foreach (PlayerController player in PlayerRegistry.All)
                if (player != null && !player.IsLocalPlayer) return player;
            return null;
        }

        // Built once and kept, switched off between appearances.
        private GameObject EnsureBody()
        {
            if (mimicBody != null && mimicBody.scene.IsValid()) return mimicBody;

            GameObject source = mimicBody;
            PlayerController partner = Partner();

            // The teammate's real character if they have one, else their capsule.
            Transform partnerLook = null;
            if (source == null && partner != null)
            {
                partnerLook = partner.transform.Find("Character");
                if (partnerLook == null) partnerLook = partner.transform.Find("Body");
                if (partnerLook != null) source = partnerLook.gameObject;
            }

            GameObject body;
            if (source != null)
            {
                body = Instantiate(source);
                body.transform.localScale = source.transform.lossyScale;
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                Renderer renderer = body.GetComponent<Renderer>();
                if (renderer != null) renderer.material.color = new Color(0.18f, 0.18f, 0.2f);
            }

            body.name = "Mimic";

            // A figure, not an obstacle: nothing to bump into or aim at.
            foreach (Collider c in body.GetComponentsInChildren<Collider>()) Destroy(c);

            // Stood where it stands on the real teammate: a capsule's pivot is
            // its middle, a fitted character's offset was worked out already.
            GameObject root = new GameObject("MimicRoot");
            body.transform.SetParent(root.transform, true);
            body.transform.localPosition = partnerLook != null ? partnerLook.localPosition : new Vector3(0f, 0.9f, 0f);
            body.transform.localRotation = Quaternion.identity;

            torch = BuildTorch(root.transform);

            // The real teammate carries a faint lamp; so does the copy.
            Transform lamp = partner != null ? partner.transform.Find("PresenceLamp") : null;
            if (lamp != null)
            {
                Transform copy = Instantiate(lamp.gameObject, root.transform).transform;
                copy.localPosition = lamp.localPosition;
                copy.localRotation = lamp.localRotation;
            }

            root.SetActive(false);
            mimicBody = root;
            return root;
        }

        // The same beam a teammate carries, so from behind it reads as one.
        private static Light BuildTorch(Transform parent)
        {
            GameObject go = new GameObject("MimicTorch");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0.2f, 1.5f, 0.3f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 14f;
            light.spotAngle = 48f;
            light.intensity = 2.2f;
            light.color = new Color(1f, 0.93f, 0.8f);
            light.shadows = LightShadows.Soft;
            return light;
        }
    }
}
