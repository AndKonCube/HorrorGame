using FearMe.Core;
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
        public float BleedOutRemaining => bleedOutRemaining;
        public float BleedOutFraction => Mathf.Clamp01(bleedOutRemaining / Mathf.Max(0.01f, bleedOutSeconds));
        public float ReviveFraction => Mathf.Clamp01(reviveProgress / Mathf.Max(0.01f, reviveSeconds));

        public override string Prompt => IsDown && !IsDead ? "Hold to revive" : string.Empty;

        public override bool HoldToUse => true;

        private void Awake()
        {
            self = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (!IsDown || IsDead) return;

            bleedOutRemaining -= Time.deltaTime;
            if (bleedOutRemaining <= 0f) Die();

            // Reviving has to be continuous; stepping away loses the progress.
            reviveProgress = Mathf.MoveTowards(reviveProgress, 0f, Time.deltaTime);
        }

        public void GoDown()
        {
            if (IsDown || IsDead) return;

            IsDown = true;
            bleedOutRemaining = bleedOutSeconds;
            reviveProgress = 0f;
            self.SetIncapacitated(true);
        }

        // Called each frame by whoever is holding the revive key.
        public override void Interact()
        {
            if (!IsDown || IsDead) return;

            PlayerController rescuer = PlayerRegistry.Local;
            if (rescuer == null || rescuer == self) return;
            if (Vector3.Distance(rescuer.transform.position, transform.position) > reviveRange) return;

            reviveProgress += Time.deltaTime * 2f; // countered by the decay above
            if (reviveProgress >= reviveSeconds) Revive();
        }

        public void Revive()
        {
            if (IsDead) return;

            IsDown = false;
            reviveProgress = 0f;
            self.SetIncapacitated(false);
        }

        private void Die()
        {
            IsDead = true;
            bleedOutRemaining = 0f;

            // Bleeding out is the other way a run can end: if nobody is left
            // upright, finish it here rather than waiting for a catch.
            if (PlayerRegistry.AnyAlive()) return;

            GameOverController flow = FindFirstObjectByType<GameOverController>();
            if (flow != null) flow.OnPlayerCaught();
        }
    }
}
