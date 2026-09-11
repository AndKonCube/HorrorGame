using System.Collections;
using FearMe.Core;
using FearMe.Settings;
using UnityEngine;
using UnityEngine.AI;

namespace FearMe.Scares
{
    // Puts a figure at the edge of the player's view, then takes it away the
    // moment they look straight at it - so they are never quite sure it was
    // there. Placement is validated against the NavMesh and line of sight,
    // otherwise it would appear inside walls.
    public class ApparitionScare : ScareEvent
    {
        [SerializeField] private GameObject apparition;
        [SerializeField] private LayerMask obstructionMask;

        [Header("Placement")]
        [SerializeField] private float minDistance = 7f;
        [SerializeField] private float maxDistance = 18f;
        [SerializeField] private float minViewAngle = 12f;
        [SerializeField] private float maxViewAngle = 85f;
        [Tooltip("Chance of standing behind the player instead, to be found on turning round.")]
        [SerializeField, Range(0f, 1f)] private float behindChance = 0.3f;
        [SerializeField] private int placementAttempts = 24;

        [Header("Behaviour")]
        [SerializeField] private float maxVisibleTime = 6f;
        [SerializeField] private float lookedAtAngle = 10f;
        [SerializeField] private float fogPulse = 0.03f;

        [Header("Uncanny motion")]
        [Tooltip("Hold still, then snap somewhere new - wrong timing, not a wrong model.")]
        [SerializeField] private bool uncannyStutter = true;
        [SerializeField] private Vector2 stutterInterval = new Vector2(0.7f, 1.8f);
        [SerializeField] private float stutterDistance = 0.45f;

        [Header("Audio")]
        [Tooltip("Played at the figure's position as it appears. Left empty until you have a cue.")]
        [SerializeField] private AudioClip[] cueClips;
        [Tooltip("Optional; one is created at runtime if left empty.")]
        [SerializeField] private AudioSource cueSource;

        private Coroutine active;

        private void Awake()
        {
            // Self-provisioning so an existing scene needs no rewiring.
            if (cueSource != null) return;

            GameObject go = new GameObject("ApparitionCue");
            go.transform.SetParent(transform, false);

            cueSource = go.AddComponent<AudioSource>();
            cueSource.playOnAwake = false;
            cueSource.spatialBlend = 1f; // 3D: you should hear where it is
            cueSource.rolloffMode = AudioRolloffMode.Linear;
            cueSource.maxDistance = 40f;

            go.AddComponent<AudioCategoryVolume>(); // follows the SFX slider
        }

        private void PlayCue(Vector3 position)
        {
            if (cueSource == null || cueClips == null || cueClips.Length == 0) return;

            cueSource.transform.position = position;
            cueSource.clip = cueClips[Random.Range(0, cueClips.Length)];
            cueSource.Play();
        }

        public override bool CanPlay(ScareContext context)
        {
            // Pointless while they are shut in a wardrobe and cannot see out.
            return base.CanPlay(context) && apparition != null && !context.PlayerHidden;
        }

        protected override bool OnTrigger(ScareContext context)
        {
            if (active != null) return false;
            if (!TryFindSpot(context, out Vector3 spot)) return false;

            active = StartCoroutine(Appear(context, spot));
            return true;
        }

        private bool TryFindSpot(ScareContext context, out Vector3 spot)
        {
            spot = Vector3.zero;
            if (context.Eye == null) return false;

            Vector3 forward = context.Eye.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) return false;
            forward.Normalize();

            for (int i = 0; i < placementAttempts; i++)
            {
                // Mostly at the edge of vision; sometimes squarely behind, so
                // turning round is its own kind of scare.
                float spread = Random.value < behindChance
                    ? Random.Range(120f, 180f)
                    : Random.Range(minViewAngle, maxViewAngle);

                float angle = spread * (Random.value < 0.5f ? -1f : 1f);
                float distance = Random.Range(minDistance, maxDistance);

                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                Vector3 candidate = context.Player.position + direction * distance;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                    continue;

                // It has to actually be visible, or the scare does nothing.
                Vector3 chest = navHit.position + Vector3.up * 1.2f;
                if (Physics.Linecast(context.Eye.position, chest, obstructionMask))
                    continue;

                spot = navHit.position;
                return true;
            }

            return false;
        }

        private IEnumerator Appear(ScareContext context, Vector3 spot)
        {
            apparition.transform.position = spot;
            FacePlayer(context);
            apparition.SetActive(true);
            PlayCue(spot);

            if (fogPulse > 0f && VolumetricFogController.Instance != null)
                VolumetricFogController.Instance.Pulse(fogPulse);

            float elapsed = 0f;
            float nextStutter = 0f;
            Vector3 basePosition = spot;

            while (elapsed < maxVisibleTime)
            {
                elapsed += Time.deltaTime;

                // Unnatural stillness broken by a snap to a new pose reads as
                // wrong in a way smooth motion never does - it is the timing,
                // not the model, that unsettles.
                if (uncannyStutter && elapsed >= nextStutter)
                {
                    nextStutter = elapsed + Random.Range(stutterInterval.x, stutterInterval.y);
                    apparition.transform.position = basePosition + new Vector3(
                        Random.Range(-stutterDistance, stutterDistance), 0f,
                        Random.Range(-stutterDistance, stutterDistance));
                    FacePlayer(context);
                }

                if (context.Eye != null && LookingAt(context.Eye, apparition.transform.position))
                    break;

                yield return null;
            }

            apparition.SetActive(false);
            active = null;
        }

        private void FacePlayer(ScareContext context)
        {
            if (context.Player == null) return;

            Vector3 toPlayer = context.Player.position - apparition.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
                apparition.transform.rotation = Quaternion.LookRotation(toPlayer);
        }

        private bool LookingAt(Transform eye, Vector3 target)
        {
            Vector3 toTarget = target - eye.position;
            return Vector3.Angle(eye.forward, toTarget) <= lookedAtAngle;
        }
    }
}
