using UnityEngine;

namespace FearMe.Player
{
    // Body-cam feel: the camera rides on a body that walks and breathes.
    // Kept small on purpose - readable bob is immersive, obvious bob is
    // nausea. Applied as a local offset so look control stays untouched.
    public class HeadBob : MonoBehaviour
    {
        // A held pose on top of the bob - leaning out to peek, or lying low
        // under a bed. Set by whatever puts the player in that pose.
        public Vector3 PoseOffset { get; set; }

        [SerializeField] private CharacterController controller;
        [SerializeField] private PlayerController player;

        [Header("Walking")]
        [SerializeField] private float stepsPerSecond = 1.9f;
        [SerializeField] private float verticalAmount = 0.045f;
        [SerializeField] private float horizontalAmount = 0.03f;
        [SerializeField] private float sprintMultiplier = 1.5f;

        [Header("Idle sway")]
        [Tooltip("Breathing drift when standing still.")]
        [SerializeField] private float idleAmount = 0.012f;
        [SerializeField] private float idleSpeed = 0.9f;

        [Header("Feel")]
        [SerializeField] private float smoothing = 10f;
        [SerializeField] private float rollDegrees = 0.6f;

        private Vector3 restPosition;
        private float cycle;
        private Vector3 currentOffset;
        private float currentRoll;

        private void Awake()
        {
            restPosition = transform.localPosition;
        }

        private void LateUpdate()
        {
            float speed = HorizontalSpeed();
            bool moving = speed > 0.15f && controller != null && controller.isGrounded;

            Vector3 targetOffset;
            float targetRoll = 0f;

            if (moving)
            {
                float rate = stepsPerSecond * (player != null && player.IsCrouching ? 0.6f : 1f);
                if (speed > 4.5f) rate *= sprintMultiplier;

                cycle += Time.deltaTime * rate * Mathf.PI * 2f;

                // Vertical runs at double rate: two footfalls per full sway.
                float vertical = Mathf.Sin(cycle * 2f) * verticalAmount;
                float horizontal = Mathf.Cos(cycle) * horizontalAmount;

                targetOffset = new Vector3(horizontal, vertical, 0f);
                targetRoll = -Mathf.Cos(cycle) * rollDegrees;
            }
            else
            {
                cycle = 0f;
                float drift = Mathf.Sin(Time.time * idleSpeed) * idleAmount;
                targetOffset = new Vector3(0f, drift, 0f);
            }

            currentOffset = Vector3.Lerp(currentOffset, targetOffset, Time.deltaTime * smoothing);
            currentRoll = Mathf.Lerp(currentRoll, targetRoll, Time.deltaTime * smoothing);

            transform.localPosition = restPosition + PoseOffset + currentOffset;

            // Roll only; pitch and yaw belong to the look controller.
            Vector3 euler = transform.localEulerAngles;
            transform.localEulerAngles = new Vector3(euler.x, euler.y, currentRoll);
        }

        private float HorizontalSpeed()
        {
            if (controller == null) return 0f;

            Vector3 velocity = controller.velocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }
    }
}
