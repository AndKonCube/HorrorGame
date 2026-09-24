using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    // Something you pick up and carry in your hands - one at a time. Picking
    // it up, dropping it and using it up are all mirrored to the other player,
    // who sees it in their teammate's hands rather than on the floor.
    public abstract class HeldItem : Interactable
    {
        [SerializeField] private string displayName = "item";

        [Header("In the hands")]
        [SerializeField] private Vector3 heldOffset = Vector3.zero;
        [SerializeField] private Vector3 heldRotation = Vector3.zero;

        [Header("Noise")]
        [Tooltip("Heavy things are loud when they hit the floor.")]
        [SerializeField] private float dropNoiseRadius;

        // Subclasses use 10 and up for their own states.
        private const int Taken = 1;
        private const int Dropped = 2;
        private const int Consumed = 3;

        private Collider[] colliders;
        private Rigidbody body;

        protected int PropId { get; private set; }

        public PlayerHands Holder { get; private set; }
        public bool IsHeld => Holder != null;

        // In the other player's hands, on this machine.
        public bool HeldRemotely { get; private set; }

        public string DisplayName => displayName;

        public override string Prompt => IsHeld || HeldRemotely ? string.Empty : "Take " + displayName;

        protected virtual void Awake()
        {
            colliders = GetComponentsInChildren<Collider>();
            body = GetComponent<Rigidbody>();

            // Registered for the object's life: it gets reparented while
            // held, but the id is fixed from where it started in the scene.
            PropId = PropSync.Register(this, ApplyRemote);
        }

        protected virtual void OnDestroy()
        {
            PropSync.Unregister(PropId);
        }

        public override void Interact()
        {
            PlayerController local = PlayerRegistry.Local;
            PlayerHands hands = local != null ? local.GetComponent<PlayerHands>() : null;
            if (hands != null) hands.TryTake(this);
        }

        // What it does when the use key is pressed with it in hand.
        public abstract void Use(PlayerHands hands);

        protected virtual void OnTaken(PlayerHands hands) { }

        // Dropped, delivered or used up - the hands are free again.
        protected virtual void OnReleased(PlayerHands hands) { }

        protected virtual void OnRemote(int state, Vector3 value) { }

        internal void AttachTo(PlayerHands hands)
        {
            Holder = hands;
            SetPhysical(false);

            transform.SetParent(hands.HoldPoint, false);
            transform.localPosition = heldOffset;
            transform.localRotation = Quaternion.Euler(heldRotation);

            OnTaken(hands);
            PropSync.Publish(PropId, Taken, Vector3.zero);
        }

        internal void DetachTo(Vector3 position)
        {
            PlayerHands hands = Holder;
            Holder = null;

            PlaceAt(position);
            OnReleased(hands);

            if (dropNoiseRadius > 0f) NoiseBus.Emit(position, dropNoiseRadius);
            PropSync.Publish(PropId, Dropped, position);
        }

        // Out of the hands without landing anywhere - delivered, say.
        protected void ReleaseFromHands()
        {
            PlayerHands hands = Holder;
            if (hands == null) return;

            Holder = null;
            hands.Forget(this);
            OnReleased(hands);
        }

        // Spent: gone from both machines.
        protected void Consume()
        {
            ReleaseFromHands();
            PropSync.Publish(PropId, Consumed, Vector3.zero);
            Destroy(gameObject);
        }

        protected void PlaceAt(Vector3 position)
        {
            transform.SetParent(null, true);
            transform.position = position;
            SetPhysical(true);
        }

        private void ApplyRemote(int state, Vector3 value)
        {
            switch (state)
            {
                case Taken:
                    HeldRemotely = true;
                    SetPhysical(false);
                    ShowInTeammatesHands();
                    break;

                case Dropped:
                    HeldRemotely = false;
                    PlaceAt(value);
                    break;

                case Consumed:
                    Destroy(gameObject);
                    break;

                default:
                    OnRemote(state, value);
                    break;
            }
        }

        // Two players, so the other one is simply whoever is not local.
        private void ShowInTeammatesHands()
        {
            foreach (PlayerController player in PlayerRegistry.All)
            {
                if (player == null || player.IsLocalPlayer) continue;

                transform.SetParent(player.transform, false);
                transform.localPosition = new Vector3(0.3f, 1.1f, 0.45f);
                transform.localRotation = Quaternion.identity;
                return;
            }
        }

        private void SetPhysical(bool physical)
        {
            if (colliders != null)
            {
                foreach (Collider c in colliders)
                    if (c != null) c.enabled = physical;
            }

            if (body != null) body.isKinematic = !physical;
        }
    }
}
