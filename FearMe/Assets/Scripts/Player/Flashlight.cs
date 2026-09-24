using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Player
{
    public class Flashlight : MonoBehaviour
    {
        [SerializeField] private Light lightSource;
        [SerializeField] private Key toggleKey = Key.F;

        private bool blocked;

        public bool IsOn => lightSource != null && lightSource.enabled;

        // Both hands full: the torch goes off and stays off until they are free.
        public bool Blocked
        {
            get => blocked;
            set
            {
                blocked = value;
                if (blocked && lightSource != null) lightSource.enabled = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || lightSource == null || blocked) return;

            if (keyboard[toggleKey].wasPressedThisFrame)
                lightSource.enabled = !lightSource.enabled;
        }
    }
}
