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
                CurrentTarget = hit.collider.GetComponentInParent<Interactable>();

            if (CurrentTarget != null && interactAction.WasPressedThisFrame())
                CurrentTarget.Interact();
        }
    }
}
