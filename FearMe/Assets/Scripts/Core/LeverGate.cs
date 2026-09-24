using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    // A portcullis or shutter that stays up only while its lever is held.
    // It rises slowly and drops fast - and it bangs when it lands, which is
    // one more thing the stalker hears.
    //
    // It will not come down on a player: it waits, jammed, until they are
    // clear, rather than trapping anyone inside the geometry.
    public class LeverGate : MonoBehaviour
    {
        [SerializeField] private HeavyLever lever;
        [Tooltip("The part that moves. Defaults to this object.")]
        [SerializeField] private Transform gateBody;
        [SerializeField] private Vector3 openOffset = new Vector3(0f, 2.6f, 0f);
        [SerializeField] private float riseSpeed = 1.1f;
        [SerializeField] private float dropSpeed = 4f;

        [Header("Safety")]
        [Tooltip("Half-size of the space under the gate that must be clear to close.")]
        [SerializeField] private Vector3 clearance = new Vector3(1.2f, 1.2f, 0.6f);

        [Header("Noise")]
        [SerializeField] private float slamNoiseRadius = 12f;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip slamClip;

        private Vector3 closedPosition;
        private Vector3 closedWorld;
        private float open;

        private void Awake()
        {
            if (gateBody == null) gateBody = transform;
            closedPosition = gateBody.localPosition;
            closedWorld = gateBody.position;
        }

        private void Update()
        {
            bool wantOpen = lever != null && lever.GateOpen;
            float previous = open;

            if (wantOpen) open = Mathf.MoveTowards(open, 1f, riseSpeed / openOffset.magnitude * Time.deltaTime);
            else if (!SomeoneUnderneath()) open = Mathf.MoveTowards(open, 0f, dropSpeed / openOffset.magnitude * Time.deltaTime);

            gateBody.localPosition = closedPosition + openOffset * open;

            if (previous > 0f && open <= 0f) Slam();
        }

        private bool SomeoneUnderneath()
        {
            Vector3 centre = closedWorld + Vector3.up * clearance.y;

            foreach (PlayerController player in PlayerRegistry.All)
            {
                if (player == null) continue;

                Vector3 local = Quaternion.Inverse(transform.rotation) * (player.transform.position + Vector3.up - centre);
                if (Mathf.Abs(local.x) <= clearance.x && Mathf.Abs(local.y) <= clearance.y + 1f
                    && Mathf.Abs(local.z) <= clearance.z)
                    return true;
            }
            return false;
        }

        private void Slam()
        {
            if (audioSource != null && slamClip != null) audioSource.PlayOneShot(slamClip);
            NoiseBus.Emit(gateBody.position, slamNoiseRadius);
        }

        private void OnDrawGizmosSelected()
        {
            Transform body = gateBody != null ? gateBody : transform;
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(body.position + Vector3.up * clearance.y, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, clearance * 2f);
        }
    }
}
