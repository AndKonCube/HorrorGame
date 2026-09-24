using FearMe.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Player
{
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float interactRange = 3f;

        private InputAction interactAction;

        public Interactable CurrentTarget { get; private set; }

        private void Awake()
        {
            interactAction = GetComponent<PlayerInput>().actions["Interact"];
        }

        private void Update()
        {
            CurrentTarget = null;
            if (playerCamera == null) return;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, interactRange, ~0, QueryTriggerInteraction.Ignore))
            {
                Interactable found = hit.collider.GetComponentInParent<Interactable>();

                // A blank prompt means "nothing to do here" - a teammate who
                // is upright, say - so it should not read as a target.
                if (found != null && !string.IsNullOrEmpty(found.Prompt)) CurrentTarget = found;
            }

            if (CurrentTarget == null) return;

            // Held rather than tapped, so reviving can accumulate over time.
            if (CurrentTarget.HoldToUse)
            {
                if (interactAction.IsPressed()) CurrentTarget.Interact();
            }
            else if (interactAction.WasPressedThisFrame())
            {
                CurrentTarget.Interact();
            }
        }
    }
}
