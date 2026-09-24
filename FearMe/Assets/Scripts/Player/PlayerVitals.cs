using FearMe.Core;
using FearMe.Net;
using UnityEngine;

namespace FearMe.Player
{
    // Being caught puts you down rather than ending the run. A teammate has
    // until the bleed-out timer runs out to reach you - which is the whole
    // co-op question: do they come back for you with that thing still there?
    //
    // It is an Interactable so a teammate can simply look at you: the player's
    // CharacterController is a solid collider, so the interaction ray hits it.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerVitals : Interactable
    {
        [SerializeField] private float bleedOutSeconds = 45f;
        [SerializeField] private float reviveSeconds = 3f;
        [Tooltip("How far a downed player can be dragged back from the brink.")]
        [SerializeField] private float reviveRange = 2.5f;

        private PlayerController self;
        private float bleedOutRemaining;
        private float reviveProgress;

        public bool IsDown { get; private set; }
        public bool IsDead { get; private set; }

        // The server runs the bleed-out clock once a session owns the run, so
        // a client must not also tick it and race the answer.
        public bool OwnsTimer { get; set; } = true;
        public float BleedOutRemaining => bleedOutRemaining;
        public float BleedOutSeconds => bleedOutSeconds;
        public float ReviveRange => reviveRange;
        public float BleedOutFraction => Mathf.Clamp01(bleedOutRemaining / Mathf.Max(0.01f, bleedOutSeconds));
        public float ReviveFraction => Mathf.Clamp01(reviveProgress / Mathf.Max(0.01f, reviveSeconds));

        // Dragged or caged, you are out of reach of a simple revive: someone
        // has to stop the stalker or break the cage.
        public Captivity Captivity { get; private set; }
        public ICaptiveAnchor Anchor { get; private set; }

        public override string Prompt
        {
            get
            {
                if (!IsDown || IsDead) return string.Empty;
                if (Captivity == Captivity.None) return "Hold to revive";

                // Looking at a caged friend means looking at the lock: their
                // body is in front of it and would otherwise block the ray.
                if (Captivity == Captivity.Caged && Anchor is Interactable cage) return cage.Prompt;

                return string.Empty;
            }
        }

        public override bool HoldToUse => true;

        private void Awake()
        {
            self = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (!IsDown || IsDead) return;

            // Whatever has you decides where you are. Only the owner moves
            // their player; the other machine sees it through the network.
            if (Captivity != Captivity.None && Anchor != null && self.IsLocalPlayer)
            {
                Transform hold = Anchor.HoldPoint;
                if (hold != null) self.Pin(hold.position);
            }

            if (OwnsTimer)
            {
                bleedOutRemaining -= Time.deltaTime;
                if (bleedOutRemaining <= 0f) Die();
            }

            // Reviving has to be continuous; stepping away loses the progress.
            reviveProgress = Mathf.MoveTowards(reviveProgress, 0f, Time.deltaTime);
        }

        public void GoDown()
        {
            if (IsDown || IsDead) return;

            // Online it is the server's call, and it comes back as state.
            if (CoopHooks.DownRequested != null && CoopHooks.DownRequested(this)) return;

            ApplyDown();
        }

        public void ApplyDown()
        {
            if (IsDown || IsDead) return;

            // Found hiding: dragged out of the closet or from under the bed.
            if (self.IsConfined) HidingSpot.Release(self);

            IsDown = true;
            bleedOutRemaining = bleedOutSeconds;
            reviveProgress = 0f;
            self.SetIncapacitated(true);
        }

        // Called each frame by whoever is holding the revive key.
        public override void Interact()
        {
            if (!IsDown || IsDead) return;

            if (Captivity == Captivity.Caged && Anchor is Interactable cage)
            {
                cage.Interact();
                return;
            }
            if (Captivity != Captivity.None) return;

            PlayerController rescuer = PlayerRegistry.Local;
            if (rescuer == null || rescuer == self) return;
            if (Vector3.Distance(rescuer.transform.position, transform.position) > reviveRange) return;

            reviveProgress += Time.deltaTime * 2f; // countered by the decay above
            if (reviveProgress >= reviveSeconds) Revive();
        }

        public void Revive()
        {
            if (IsDead) return;

            if (CoopHooks.ReviveRequested != null && CoopHooks.ReviveRequested(this)) return;

            ApplyRevive();
        }

        // The stalker grabbing you, the cage taking you, or being let go.
        public void SetCaptivity(Captivity value, ICaptiveAnchor anchor)
        {
            if (IsDead) return;

            int anchorId = anchor != null ? anchor.AnchorId : 0;
            if (CoopHooks.CaptivityRequested != null && CoopHooks.CaptivityRequested(this, (int)value, anchorId)) return;

            ApplyCaptivity(value, anchor);
        }

        public void ApplyCaptivity(Captivity value, ICaptiveAnchor anchor)
        {
            Captivity = IsDead ? Captivity.None : value;
            Anchor = Captivity == Captivity.None ? null : anchor;
        }

        public void ApplyRevive()
        {
            if (IsDead) return;

            ApplyCaptivity(Captivity.None, null);
            IsDown = false;
            reviveProgress = 0f;
            self.SetIncapacitated(false);
        }

        // Whole state in one go, from the server's copy.
        public void ApplyNetworkVitals(bool down, bool dead, float remaining)
        {
            if (dead && !IsDead)
            {
                Die();
                return;
            }

            if (down && !IsDown) ApplyDown();
            else if (!down && IsDown) ApplyRevive();

            // After the transitions, which set a full timer of their own.
            if (IsDown && !IsDead) bleedOutRemaining = remaining;
        }

        private void Die()
        {
            IsDead = true;
            bleedOutRemaining = 0f;
            Captivity = Captivity.None;
            Anchor = null;

            // Bleeding out is the other way a run can end: if nobody is left
            // upright, finish it here rather than waiting for a catch.
            if (PlayerRegistry.AnyAlive()) return;

            GameOverController flow = FindFirstObjectByType<GameOverController>();
            if (flow != null) flow.OnPlayerCaught();
        }
    }
}
