using System.Collections;
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

        private PlayerController occupant;
        private Coroutine doorMove;

        public bool IsOccupied => occupant != null;

        public override string Prompt => IsOccupied ? "Step out" : "Hide inside";

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
            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player == null || player.IsConfined) return;

            occupant = player;
            player.EnterConfinement(Inside.position, Inside.eulerAngles.y, yawLimit, pitchLimit);
            SwingDoor(closing: true);
        }

        private void Leave()
        {
            PlayerController player = occupant;
            occupant = null;

            SwingDoor(closing: false);
            if (player != null) player.ExitConfinement(ExitPosition);
        }

        private void SwingDoor(bool closing)
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
