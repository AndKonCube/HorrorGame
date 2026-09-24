using FearMe.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Player
{
    // What you can still do while hidden.
    //
    // Hold your breath (Space): when the demon is right outside, it hears a
    // hidden player breathing and tears the door open. Held breath stops
    // that - until your lungs give out and you gasp, which it hears for sure.
    //
    // Peek (Q or right mouse): the door cracks open and you lean to the gap
    // to see what is out there. You can look wider - and anything looking
    // back at the gap can see you.
    [RequireComponent(typeof(PlayerController))]
    public class HidingBreath : MonoBehaviour
    {
        [Header("Breath")]
        [Tooltip("How long a full breath can be held.")]
        [SerializeField] private float breathSeconds = 8f;
        [Tooltip("Lungs refilled per second while breathing normally (0-1).")]
        [SerializeField] private float recoveryRate = 0.3f;
        [Tooltip("After a gasp, breath has to recover this far before holding again.")]
        [SerializeField, Range(0f, 1f)] private float recoverBeforeHolding = 0.4f;
        [SerializeField] private float gaspNoiseRadius = 8f;
        [SerializeField] private AudioClip gaspClip;
        [SerializeField] private AudioClip exhaleClip;

        [Header("Peek")]
        [SerializeField] private Key peekKey = Key.Q;
        [Tooltip("How far the head leans to the gap.")]
        [SerializeField] private float peekLean = 0.22f;
        [Tooltip("Extra degrees either side you can look while peeking.")]
        [SerializeField] private float peekLookBonus = 25f;
        [SerializeField] private float poseSpeed = 5f;

        private PlayerController player;
        private InputAction holdAction;
        private HeadBob headBob;
        private Transform view;
        private Vector3 viewRest;
        private Vector3 pose;
        private bool wasHolding;
        private bool wasPeeking;
        private bool winded;

        // 1 with full lungs, 0 at the gasp.
        public float Breath { get; private set; } = 1f;
        public bool Winded => winded;

        private void Awake()
        {
            player = GetComponent<PlayerController>();

            PlayerInput input = GetComponent<PlayerInput>();
            if (input != null) holdAction = input.actions["Jump"];

            headBob = GetComponentInChildren<HeadBob>();
            Camera camera = GetComponentInChildren<Camera>();
            if (camera != null)
            {
                view = camera.transform;
                viewRest = view.localPosition;
            }
        }

        private void Update()
        {
            if (!player.IsLocalPlayer) return;

            bool hidden = player.IsConfined;
            HidingSpot spot = hidden ? HidingSpot.Holding(player) : null;

            TickBreath(hidden);
            TickPeek(hidden, spot);
            TickPose(hidden, spot);
        }

        private void TickBreath(bool hidden)
        {
            bool wantsToHold = hidden && !winded && holdAction != null && holdAction.IsPressed();

            if (wantsToHold)
            {
                Breath -= Time.deltaTime / Mathf.Max(0.5f, breathSeconds);
                if (Breath <= 0f)
                {
                    Breath = 0f;
                    Gasp();
                    wantsToHold = false;
                }
            }
            else
            {
                Breath = Mathf.MoveTowards(Breath, 1f, recoveryRate * Time.deltaTime);
                if (winded && Breath >= recoverBeforeHolding) winded = false;
            }

            // A long hold let out gently is a sound too, just a small one.
            if (wasHolding && !wantsToHold && !winded && Breath < 0.6f) Play(exhaleClip, 0.5f);

            player.SetBreathHeld(wantsToHold);
            wasHolding = wantsToHold;
        }

        private void Gasp()
        {
            winded = true;
            Play(gaspClip, 1f);
            NoiseBus.Emit(transform.position, gaspNoiseRadius);
        }

        private void TickPeek(bool hidden, HidingSpot spot)
        {
            bool wantsToPeek = false;
            if (hidden)
            {
                Keyboard keyboard = Keyboard.current;
                Mouse mouse = Mouse.current;
                wantsToPeek = (keyboard != null && keyboard[peekKey].isPressed) ||
                              (mouse != null && mouse.rightButton.isPressed);
            }

            player.SetPeeking(wantsToPeek);
            player.ConfinedLookBonus = wantsToPeek ? peekLookBonus : 0f;

            if (wantsToPeek != wasPeeking && spot != null) spot.SetPeek(wantsToPeek);
            wasPeeking = wantsToPeek;
        }

        // Lower under a bed, leaning to the gap when peeking, and back to
        // normal once out.
        private void TickPose(bool hidden, HidingSpot spot)
        {
            Vector3 target = Vector3.zero;
            if (hidden && spot != null)
            {
                target.y = spot.EyeHeightOffset;
                if (player.IsPeeking) target.z = peekLean;
            }

            pose = Vector3.MoveTowards(pose, target, poseSpeed * Time.deltaTime);

            if (headBob != null) headBob.PoseOffset = pose;
            else if (view != null) view.localPosition = viewRest + pose;
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position + Vector3.up * 1.6f, volume);
        }
    }
}
