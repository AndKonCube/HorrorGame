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
        private bool inHidingZone;
        private float currentHeight;

        public bool IsCrouching => isCrouching;

        // Fully concealed only while crouched inside a hiding zone.
        public bool IsHidden => inHidingZone && isCrouching;

        public float CurrentNoiseRadius
        {
            get
            {
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
        }

        // The action asset outlives this component, so a lambda left subscribed
        // would fire into a destroyed object after a scene reload.
        private void OnDestroy()
        {
            if (crouchAction != null && crouchHandler != null)
                crouchAction.performed -= crouchHandler;
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            HandleLook();
            HandleCrouchTransition();
            HandleMove();
        }

        private void HandleLook()
        {
            Vector2 lookInput = lookAction.ReadValue<Vector2>();

            // Sensitivity is a saved player preference, not a scene value.
            GameSettings settings = SettingsService.Current;
            float sensitivity = settings.mouseSensitivity;
            float vertical = settings.invertLook ? -1f : 1f;

            transform.Rotate(Vector3.up * (lookInput.x * sensitivity));

            pitch -= lookInput.y * sensitivity * vertical;
            pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);
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
            isSprinting = sprintAction.IsPressed() && !isCrouching && moveInput.y > 0.1f;

            float speed = isCrouching ? crouchSpeed : (isSprinting ? sprintSpeed : walkSpeed);
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

        // Called by HidingSpot trigger volumes.
        public void SetInHidingZone(bool value)
        {
            inHidingZone = value;
        }
    }
}
