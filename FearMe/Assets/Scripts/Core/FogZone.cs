using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    // Room-sized trigger that swaps the fog profile, so the morgue can feel
    // different from a corridor.
    [RequireComponent(typeof(Collider))]
    public class FogZone : MonoBehaviour
    {
        [SerializeField] private FogProfile profile = new FogProfile();

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.TryGetComponent(out PlayerController _)) return;
            if (VolumetricFogController.Instance != null)
                VolumetricFogController.Instance.SetProfile(profile);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.TryGetComponent(out PlayerController _)) return;
            if (VolumetricFogController.Instance != null)
                VolumetricFogController.Instance.ResetProfile();
        }
    }
}
