using FearMe.Core;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Items
{
    // The last thing the exit needs - a car battery, a generator part, a
    // great iron key - and it is too heavy to carry properly. Whoever has it
    // walks slowly, cannot run, and has no hands for a torch. Their partner
    // has to light the way and keep watch.
    //
    // Dropping it is loud. Going down drops it too, so it can be picked back up.
    public class HeavyItem : HeldItem
    {
        [Header("Weight")]
        [Tooltip("Fraction of normal walking speed while carrying it.")]
        [SerializeField, Range(0.2f, 1f)] private float carrySpeed = 0.45f;

        private const int Delivered = 10;

        public bool IsDelivered { get; private set; }

        public override bool TwoHanded => true;

        public override string Prompt => IsDelivered ? string.Empty : base.Prompt;

        protected override void OnTaken(PlayerHands hands)
        {
            hands.Player.SpeedMultiplier = carrySpeed;
            hands.Player.CanSprint = false;
            SetTorchBlocked(hands, true);
        }

        protected override void OnReleased(PlayerHands hands)
        {
            if (hands == null) return;

            hands.Player.SpeedMultiplier = 1f;
            hands.Player.CanSprint = true;
            SetTorchBlocked(hands, false);
        }

        // Both hands are on it; there is nothing to use.
        public override void Use(PlayerHands hands) { }

        // Set down where it is needed. It stays there for good.
        public void Deliver(Transform slot)
        {
            if (IsDelivered) return;

            ReleaseFromHands();
            Settle(slot != null ? slot.position : transform.position, slot);

            PropSync.Publish(PropId, Delivered, transform.position);
        }

        protected override void OnRemote(int state, Vector3 value)
        {
            if (state != Delivered) return;

            // Out of the teammate's arms and into its slot.
            foreach (PlayerController player in PlayerRegistry.All)
                if (player != null && player.RemoteHeldItem == this) player.RemoteHeldItem = null;

            Settle(value, null);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        }

        private void Settle(Vector3 position, Transform slot)
        {
            IsDelivered = true;
            PlaceAt(position);
            if (slot != null) transform.rotation = slot.rotation;

            // Delivered things are part of the level now, not something to grab.
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
        }

        private static void SetTorchBlocked(PlayerHands hands, bool blocked)
        {
            Flashlight torch = hands.GetComponentInChildren<Flashlight>();
            if (torch != null) torch.Blocked = blocked;
        }
    }
}
