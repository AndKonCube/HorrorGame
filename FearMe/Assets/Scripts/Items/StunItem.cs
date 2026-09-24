using FearMe.AI;
using FearMe.Core;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Items
{
    // The partner's way to fight back: a brick to throw or a stun gun to jam
    // into it. A hit dazes the stalker and makes it drop whoever it is
    // dragging - still down, but close enough now to pull back up.
    //
    // A thrown brick that misses is not wasted, quite: it lands somewhere
    // loud, and that is a place the stalker will go and look. A decoy.
    public class StunItem : HeldItem
    {
        [Header("Stun")]
        [Tooltip("A thrown brick carries ~10m; a stun gun has to be ~3m or less.")]
        [SerializeField] private float range = 10f;
        [Tooltip("How far off-centre it can be and still count as aimed at.")]
        [SerializeField] private float aimCone = 20f;
        [SerializeField] private float stunSeconds = 6f;
        [SerializeField] private int uses = 1;
        [Tooltip("Thrown things leave your hand on a miss; a stun gun just sparks.")]
        [SerializeField] private bool thrown = true;

        [Header("Noise")]
        [SerializeField] private float hitNoiseRadius = 10f;
        [SerializeField] private float missNoiseRadius = 14f;

        [Header("Audio (optional)")]
        [SerializeField] private AudioClip hitClip;
        [SerializeField] private AudioClip missClip;

        public override void Use(PlayerHands hands)
        {
            Camera view = hands.GetComponentInChildren<Camera>();
            Transform eye = view != null ? view.transform : hands.transform;

            EnemyStalkerAI target = FindTarget(eye);
            if (target != null)
            {
                target.RequestStun(stunSeconds);
                NoiseBus.Emit(target.transform.position, hitNoiseRadius);
                PlayAt(hitClip, target.transform.position);
                Spend();
                return;
            }

            if (!thrown)
            {
                PlayAt(missClip, eye.position);
                return;
            }

            // Missed: it lands wherever it was thrown, and that is loud.
            Vector3 landing = Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, range, ~0,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : eye.position + eye.forward * range;

            NoiseBus.Emit(landing, missNoiseRadius);
            PlayAt(missClip, landing);
            Spend();
        }

        // Nearest stalker in range, roughly where you are looking, with
        // nothing solid in the way.
        private EnemyStalkerAI FindTarget(Transform eye)
        {
            EnemyStalkerAI best = null;
            float bestDistance = float.MaxValue;

            // Includes a switched-off brain on a guest's machine: the body is
            // still there to hit, and the stun is forwarded to the host.
            foreach (EnemyStalkerAI stalker in FindObjectsByType<EnemyStalkerAI>(FindObjectsSortMode.None))
            {
                Vector3 centre = stalker.transform.position + Vector3.up * 1.2f;
                Vector3 toTarget = centre - eye.position;
                float distance = toTarget.magnitude;

                if (distance > range || distance >= bestDistance) continue;
                if (Vector3.Angle(eye.forward, toTarget) > aimCone) continue;

                if (Physics.Linecast(eye.position, centre, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                    && !hit.transform.IsChildOf(stalker.transform))
                    continue;

                best = stalker;
                bestDistance = distance;
            }

            return best;
        }

        private void Spend()
        {
            uses--;
            if (uses <= 0) Consume();
        }

        // At a point rather than on this object, which may be gone a moment later.
        private static void PlayAt(AudioClip clip, Vector3 position)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, position);
        }
    }
}
