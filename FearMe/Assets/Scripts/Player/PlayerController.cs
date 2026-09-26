using FearMe.Core;
using FearMe.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Player
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float sprintSpeed = 5.8f;
        [SerializeField] private float crouchSpeed = 1.6f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float jumpHeight = 1.1f;

        [Header("Look")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float maxLookAngle = 85f;

        [Header("Crouch")]
        [SerializeField] private float standHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.0f;
        [SerializeField] private float crouchTransitionSpeed = 8f;

        [Header("Noise Radii")]
        [Tooltip("How far a stalking enemy can 'hear' the player at each movement state.")]
        [SerializeField] private float crouchNoiseRadius = 0f;
        [SerializeField] private float walkNoiseRadius = 4f;
        [SerializeField] private float sprintNoiseRadius = 9f;

        private CharacterController controller;
        private PlayerInput playerInput;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction sprintAction;
        private InputAction crouchAction;
        private InputAction jumpAction;
        private System.Action<InputAction.CallbackContext> crouchHandler;

        private Vector3 verticalVelocity;
        private float pitch;
        private bool isCrouching;
        private bool isSprinting;
        private float currentHeight;
        private float yaw;

        private bool confined;
        private float confinedYawCentre;
        private float confinedYawLimit;
        private float confinedPitchLimit;
        private bool incapacitated;

        [Header("Network")]
        [Tooltip("Off for a teammate's body: it is driven by the network, not by " +
            "this machine's input. Serialized so a remote body is never mistaken " +
            "for the local player, even for the frame before netcode claims it.")]
        [SerializeField] private bool isLocalPlayer = true;

        public bool IsLocalPlayer
        {
            get => isLocalPlayer;
            set => isLocalPlayer = value;
        }

        // A remote player's controller never moves itself, so its velocity and
        // closet state mean nothing here - the owner sends them instead.
        public float RemoteNoiseRadius { get; set; }
        public bool RemoteHidden { get; set; }
        public bool RemoteHoldingBreath { get; set; }
        public bool RemotePeeking { get; set; }

        // What a teammate's name tag reads. Set by the network for remote
        // players; the local player's comes from Settings.
        public string DisplayName { get; set; } = string.Empty;

        // Where a remote player's hands are, for showing what they carry.
        public Transform HandAnchor { get; set; }

        private bool holdingBreath;
        private bool peeking;

        // Hidden and holding it in: nothing for the demon to hear.
        public bool HoldingBreath => isLocalPlayer ? holdingBreath : RemoteHoldingBreath;

        // Hidden but looking out through the gap - which can be seen.
        public bool IsPeeking => isLocalPlayer ? peeking : RemotePeeking;

        // Extra degrees either side while peeking, beyond the hiding spot's own.
        public float ConfinedLookBonus { get; set; }

        public void SetBreathHeld(bool value) => holdingBreath = value;
        public void SetPeeking(bool value) => peeking = value;

        public bool IsCrouching => isCrouching;
        public bool IsSprinting => isSprinting;

        // Something heavy in your arms: slower, and no running.
        public float SpeedMultiplier { get; set; } = 1f;
        public bool CanSprint { get; set; } = true;
        public bool IsIncapacitated => incapacitated;

        // Shut inside a closet: out of sight until you step back out.
        public bool IsHidden => isLocalPlayer ? confined : RemoteHidden;
        public bool IsConfined => confined;

        public float CurrentNoiseRadius
        {
            get
            {
                if (!isLocalPlayer) return RemoteNoiseRadius;

                Vector3 flatVelocity = controller.velocity;
                flatVelocity.y = 0f;
                bool isStationary = flatVelocity.sqrMagnitude < 0.01f;

                if (IsHidden && isStationary) return 0f;
                if (isCrouching) return crouchNoiseRadius;
                if (isSprinting) return sprintNoiseRadius;
                return walkNoiseRadius;
            }
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            playerInput = GetComponent<PlayerInput>();

            moveAction = playerInput.actions["Move"];
            lookAction = playerInput.actions["Look"];
            sprintAction = playerInput.actions["Sprint"];
            crouchAction = playerInput.actions["Crouch"];
            jumpAction = playerInput.actions["Jump"];

            crouchHandler = _ => ToggleCrouch();
            crouchAction.performed += crouchHandler;

            currentHeight = standHeight;
            yaw = transform.eulerAngles.y;
        }

        // The action asset outlives this component, so a lambda left subscribed
        // would fire into a destroyed object after a scene reload.
        private void OnDestroy()
        {
            if (crouchAction != null && crouchHandler != null)
                crouchAction.performed -= crouchHandler;
        }

        private void OnEnable()
        {
            PlayerRegistry.Register(this);
        }

        private void OnDisable()
        {
            PlayerRegistry.Unregister(this);
        }

        private void Start()
        {
            if (!IsLocalPlayer) return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            // A remote player is driven by the network, not by this machine's
            // input, so it must not read the local devices.
            if (!IsLocalPlayer) return;

            HandleLook();

            // Inside a closet, or on the floor waiting for help: you can look
            // around, but going anywhere is not on offer.
            if (confined || incapacitated) return;

            HandleCrouchTransition();
            HandleMove();
        }

        private void HandleLook()
        {
            Vector2 lookInput = lookAction.ReadValue<Vector2>();

            // Sensitivity is a saved player preference, not a scene value.
            GameSettings settings = GameSettingsService.Current;
            float sensitivity = settings.mouseSensitivity;
            float vertical = settings.invertLook ? -1f : 1f;

            yaw += lookInput.x * sensitivity;
            pitch -= lookInput.y * sensitivity * vertical;
            pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

            if (confined)
            {
                // Penned in: you can only look out through the gap.
                float yawLimit = confinedYawLimit + ConfinedLookBonus;
                yaw = Mathf.Clamp(yaw, confinedYawCentre - yawLimit, confinedYawCentre + yawLimit);
                pitch = Mathf.Clamp(pitch, -confinedPitchLimit, confinedPitchLimit);
            }

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (playerCamera != null)
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void HandleCrouchTransition()
        {
            float targetHeight = isCrouching ? crouchHeight : standHeight;
            currentHeight = Mathf.Lerp(currentHeight, targetHeight, Time.deltaTime * crouchTransitionSpeed);
            controller.height = currentHeight;
            controller.center = new Vector3(0f, currentHeight * 0.5f, 0f);
        }

        private void HandleMove()
        {
            Vector2 moveInput = moveAction.ReadValue<Vector2>();
            isSprinting = CanSprint && sprintAction.IsPressed() && !isCrouching && moveInput.y > 0.1f;

            float speed = isCrouching ? crouchSpeed : (isSprinting ? sprintSpeed : walkSpeed);
            speed *= SpeedMultiplier;
            Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
            controller.Move(move.normalized * speed * Time.deltaTime);

            if (controller.isGrounded)
            {
                verticalVelocity.y = -1f;
                if (jumpAction.WasPressedThisFrame() && !isCrouching)
                    verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            verticalVelocity.y += gravity * Time.deltaTime;
            controller.Move(verticalVelocity * Time.deltaTime);
        }

        private void ToggleCrouch()
        {
            isCrouching = !isCrouching;
        }

        // Called by HidingSpot when climbing into or out of a closet.
        public void EnterConfinement(Vector3 position, float facingYaw, float yawLimit, float pitchLimit)
        {
            confined = true;
            confinedYawCentre = facingYaw;
            confinedYawLimit = yawLimit;
            confinedPitchLimit = pitchLimit;

            isCrouching = false;
            yaw = facingYaw;
            pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

            Teleport(position);
        }

        // Downed: dropped to the floor, no movement, but still able to look
        // around and watch for a teammate.
        public void SetIncapacitated(bool value)
        {
            incapacitated = value;

            float targetHeight = value ? crouchHeight : standHeight;
            currentHeight = targetHeight;
            controller.height = targetHeight;
            controller.center = new Vector3(0f, targetHeight * 0.5f, 0f);

            if (value) isCrouching = false;
        }

        public void ExitConfinement(Vector3 position)
        {
            confined = false;
            holdingBreath = false;
            peeking = false;
            ConfinedLookBonus = 0f;
            Teleport(position);
        }

        // Held in place by something else - a grip, a cage - while still free
        // to look around at it.
        public void Pin(Vector3 position)
        {
            Teleport(position);
        }

        // For spawning a second player beside the first, or putting a player
        // somewhere the network says they are.
        public void Warp(Vector3 position, float facingYaw)
        {
            yaw = facingYaw;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Teleport(position);
        }

        // A CharacterController resists being moved directly, so switch it off
        // for the frame the position changes.
        private void Teleport(Vector3 position)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = wasEnabled;
        }
    }
}
