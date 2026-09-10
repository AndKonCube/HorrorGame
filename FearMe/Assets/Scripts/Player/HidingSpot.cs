using UnityEngine;

namespace FearMe.Player
{
    // Trigger volume (wardrobe, locker, under a bed) that lets the player
    // break enemy line-of-sight entirely as long as they stay crouched inside it.
    [RequireComponent(typeof(Collider))]
    public class HidingSpot : MonoBehaviour
    {
        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out PlayerController player))
                player.SetInHidingZone(true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out PlayerController player))
                player.SetInHidingZone(false);
        }
    }
}
