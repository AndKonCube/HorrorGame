using System.Collections;
using FearMe.Core;
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
        [SerializeField] private float minDistance = 9f;
        [SerializeField] private float maxDistance = 18f;
        [SerializeField] private float minViewAngle = 18f;
        [SerializeField] private float maxViewAngle = 50f;
        [SerializeField] private int placementAttempts = 12;

        [Header("Behaviour")]
        [SerializeField] private float maxVisibleTime = 3.5f;
        [SerializeField] private float lookedAtAngle = 10f;
        [SerializeField] private float fogPulse = 0.03f;

        private Coroutine active;

        public override bool CanPlay(ScareContext context)
        {
            // Pointless while they are shut in a wardrobe and cannot see out.
            return base.CanPlay(context) && apparition != null && !context.PlayerHidden;
        }

        protected override void OnTrigger(ScareContext context)
        {
            if (active != null) return;
            if (!TryFindSpot(context, out Vector3 spot)) return;
            active = StartCoroutine(Appear(context, spot));
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
                float angle = Random.Range(minViewAngle, maxViewAngle) * (Random.value < 0.5f ? -1f : 1f);
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

            if (fogPulse > 0f && VolumetricFogController.Instance != null)
                VolumetricFogController.Instance.Pulse(fogPulse);

            float elapsed = 0f;
            while (elapsed < maxVisibleTime)
            {
                elapsed += Time.deltaTime;

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
