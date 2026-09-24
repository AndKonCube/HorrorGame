using System.Collections;
using System.Collections.Generic;
using FearMe.Core;
using UnityEngine;

namespace FearMe.Player
{
    // A closet you climb into rather than a zone you crouch in. Entering
    // pulls you inside, shuts the door and pins your view to the gap, so
    // hiding costs you the ability to move or look around - which is what
    // makes waiting in one uncomfortable.
    [RequireComponent(typeof(Collider))]
    public class HidingSpot : Interactable
    {
        [Header("Anchors")]
        [Tooltip("Where the player stands inside. Defaults to this object.")]
        [SerializeField] private Transform insideAnchor;
        [Tooltip("Where the player is put on leaving. Defaults to just in front.")]
        [SerializeField] private Transform exitAnchor;

        [Header("View")]
        [Tooltip("Degrees either side of the doorway you can turn your head.")]
        [SerializeField] private float yawLimit = 55f;
        [SerializeField] private float pitchLimit = 35f;

        [Header("Door")]
        [SerializeField] private Transform door;
        [SerializeField] private float openAngle = 95f;
        [SerializeField] private float doorSpeed = 260f;

        [Header("Pose")]
        [Tooltip("Eye height change while inside. Under a bed, about -1.2.")]
        [SerializeField] private float eyeHeightOffset;
        [Tooltip("How far the door cracks open while peeking.")]
        [SerializeField] private float peekAngle = 16f;

        [Header("Noise")]
        [Tooltip("Diving in at a run slams the door - and it can hear that.")]
        [SerializeField] private float hastyNoiseRadius = 11f;

        private static readonly List<HidingSpot> all = new List<HidingSpot>();

        private PlayerController occupant;
        private Coroutine doorMove;
        private bool peeking;

        public bool IsOccupied => occupant != null;
        public float EyeHeightOffset => eyeHeightOffset;

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        // Where this player is hiding, if anywhere.
        public static HidingSpot Holding(PlayerController player)
        {
            foreach (HidingSpot spot in all)
                if (spot != null && spot.occupant == player) return spot;
            return null;
        }

        // Pulled out - by the demon finding them, say.
        public static void Release(PlayerController player)
        {
            HidingSpot spot = Holding(player);
            if (spot != null) spot.Leave();
        }

        // The door eases open a crack while they look out, and shuts again.
        public void SetPeek(bool on)
        {
            if (peeking == on || occupant == null) return;
            peeking = on;

            if (door == null) return;
            if (doorMove != null) StopCoroutine(doorMove);
            doorMove = StartCoroutine(MoveDoor(on ? peekAngle : 0f));
        }

        [Tooltip("What the prompt says - \"Hide under the bed\", say.")]
        [SerializeField] private string hidePrompt = "Hide inside";

        public override string Prompt => IsOccupied ? "Step out" : hidePrompt;

        private Transform Inside => insideAnchor != null ? insideAnchor : transform;

        private Vector3 ExitPosition =>
            exitAnchor != null ? exitAnchor.position : Inside.position + Inside.forward * 1.3f;

        public override void Interact()
        {
            // The same prompt both ways, so one key gets you in and out.
            if (IsOccupied) Leave();
            else Enter();
        }

        private void Enter()
        {
            // The local player specifically - in co-op the teammate's body is
            // a PlayerController too.
            PlayerController player = PlayerRegistry.Local;
            if (player == null || player.IsConfined) return;

            // Hiding in a panic is the loud way to hide.
            if (player.IsSprinting) NoiseBus.Emit(Inside.position, hastyNoiseRadius);

            occupant = player;
            player.EnterConfinement(Inside.position, Inside.eulerAngles.y, yawLimit, pitchLimit);
            SwingClosetDoor(closing: true);
        }

        private void Leave()
        {
            PlayerController player = occupant;
            occupant = null;
            peeking = false;

            SwingClosetDoor(closing: false);
            if (player != null) player.ExitConfinement(ExitPosition);
        }

        private void SwingClosetDoor(bool closing)
        {
            if (door == null) return;

            if (doorMove != null) StopCoroutine(doorMove);
            doorMove = StartCoroutine(MoveDoor(closing ? 0f : openAngle));
        }

        private IEnumerator MoveDoor(float targetAngle)
        {
            Vector3 euler = door.localEulerAngles;

            while (Mathf.Abs(Mathf.DeltaAngle(euler.y, targetAngle)) > 0.5f)
            {
                euler.y = Mathf.MoveTowardsAngle(euler.y, targetAngle, doorSpeed * Time.deltaTime);
                door.localEulerAngles = euler;
                yield return null;
            }

            euler.y = targetAngle;
            door.localEulerAngles = euler;
            doorMove = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(Inside.position, 0.3f);
            Gizmos.DrawLine(Inside.position, ExitPosition);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(Inside.position, Inside.forward * 2f);
        }
    }
}
