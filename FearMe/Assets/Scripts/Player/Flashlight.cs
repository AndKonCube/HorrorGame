using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Player
{
    public class Flashlight : MonoBehaviour
    {
        [SerializeField] private Light lightSource;
        [SerializeField] private Key toggleKey = Key.F;

        public bool IsOn => lightSource != null && lightSource.enabled;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || lightSource == null) return;

            if (keyboard[toggleKey].wasPressedThisFrame)
                lightSource.enabled = !lightSource.enabled;
        }
    }
}
