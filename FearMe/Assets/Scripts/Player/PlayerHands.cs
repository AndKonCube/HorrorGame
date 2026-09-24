using FearMe.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Player
{
    // One thing in your hands at a time. Left mouse uses it, G puts it down.
    // Going down drops whatever you were holding - so a partner can pick it up.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerHands : MonoBehaviour
    {
        [Tooltip("Where a held item sits. Made in front of the camera if left empty.")]
        [SerializeField] private Transform holdPoint;
        [SerializeField] private Key dropKey = Key.G;

        private PlayerController player;
        private PlayerVitals vitals;
        private InputAction useAction;

        public HeldItem Held { get; private set; }
        public Transform HoldPoint => holdPoint;
        public PlayerController Player => player;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            vitals = GetComponent<PlayerVitals>();

            PlayerInput input = GetComponent<PlayerInput>();
            if (input != null) useAction = input.actions["Attack"];

            if (holdPoint == null)
            {
                Camera view = GetComponentInChildren<Camera>();
                holdPoint = new GameObject("HoldPoint").transform;
                holdPoint.SetParent(view != null ? view.transform : transform, false);
                holdPoint.localPosition = new Vector3(0.28f, -0.3f, 0.55f);
            }
        }

        public bool TryTake(HeldItem item)
        {
            if (item == null || Held != null || item.IsHeld || item.HeldRemotely) return false;
            if (vitals != null && vitals.IsDown) return false;

            Held = item;
            item.AttachTo(this);
            return true;
        }

        public void Drop()
        {
            if (Held == null) return;

            HeldItem item = Held;
            Held = null;

            // At your feet, a little in front, so it is easy to find again.
            Vector3 at = transform.position + transform.forward * 0.6f + Vector3.up * 0.1f;
            item.DetachTo(at);
        }

        // The item left the hands on its own - used up or delivered.
        internal void Forget(HeldItem item)
        {
            if (Held == item) Held = null;
        }

        private void Update()
        {
            if (!player.IsLocalPlayer || Held == null) return;

            if (vitals != null && vitals.IsDown)
            {
                Drop();
                return;
            }

            if (useAction != null && useAction.WasPressedThisFrame()) Held.Use(this);

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[dropKey].wasPressedThisFrame) Drop();
        }
    }
}
