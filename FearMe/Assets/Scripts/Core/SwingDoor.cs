using UnityEngine;

namespace FearMe.Core
{
    // A door with a choice in it. Tap the key and it swings hard and bangs -
    // fast, and the stalker hears exactly where. Hold it and the door eases
    // round a little at a time, quiet, while you stand in the open waiting.
    //
    // Put it on the door's root and point hinge at the part that turns.
    public class SwingDoor : Interactable
    {
        [SerializeField] private Transform hinge;
        [SerializeField] private float openAngle = 95f;

        [Header("Handling")]
        [Tooltip("Released sooner than this counts as a tap.")]
        [SerializeField] private float tapThreshold = 0.22f;
        [Tooltip("Degrees per second while easing it by hand.")]
        [SerializeField] private float easeSpeed = 40f;
        [Tooltip("Degrees per second when it is thrown.")]
        [SerializeField] private float slamSpeed = 420f;

        [Header("Noise")]
        [SerializeField] private float slamNoiseRadius = 16f;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip slamClip;
        [SerializeField] private AudioClip creakClip;

        private enum Remote { Ease = 0, Slam = 1 }

        private float angle;
        private float target;
        private float easeGoal;
        private float holdTime;
        private int lastInteractFrame = -10;
        private bool wasHeld;
        private bool slamming;
        private bool slamIsRemote;
        private bool creaked;
        private int propId;

        private bool IsOpen => angle > openAngle * 0.5f;

        public override string Prompt => IsOpen ? "Hold to ease shut  ·  tap slams" : "Hold to ease open  ·  tap slams";

        // Held, so how long the key is down can be measured.
        public override bool HoldToUse => true;

        private void Awake()
        {
            if (hinge == null) hinge = transform;
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            propId = PropSync.Register(this, ApplyRemote);
        }

        private void OnDisable()
        {
            PropSync.Unregister(propId);
        }

        // Called every frame the key is held on this door.
        public override void Interact()
        {
            lastInteractFrame = Time.frameCount;
        }

        private void Update()
        {
            // Interact may run before or after this in a frame; one frame of
            // slack keeps a held key from reading as released.
            bool held = Time.frameCount - lastInteractFrame <= 1;

            if (held && !wasHeld)
            {
                holdTime = 0f;
                creaked = false;
                easeGoal = IsOpen ? 0f : openAngle;
            }

            if (held)
            {
                holdTime += Time.deltaTime;
                if (holdTime >= tapThreshold) Ease();
            }
            else if (wasHeld && holdTime < tapThreshold)
            {
                Throw(IsOpen ? 0f : openAngle, publish: true);
            }

            wasHeld = held;

            if (slamming) TickSlam();
            hinge.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        private void Ease()
        {
            slamming = false;

            float previous = angle;
            angle = Mathf.MoveTowards(angle, easeGoal, easeSpeed * Time.deltaTime);
            target = angle;

            if (Mathf.Approximately(previous, angle)) return;

            // A soft creak as it starts to give is the only sound easing makes.
            if (!creaked)
            {
                creaked = true;
                PlayClip(creakClip, 0.5f);
            }

            PropSync.Publish(propId, (int)Remote.Ease, new Vector3(angle, 0f, 0f));
        }

        private void Throw(float to, bool publish)
        {
            target = to;
            slamming = true;
            slamIsRemote = false;

            if (publish) PropSync.Publish(propId, (int)Remote.Slam, new Vector3(to, 0f, 0f));
        }

        private void TickSlam()
        {
            angle = Mathf.MoveTowards(angle, target, slamSpeed * Time.deltaTime);
            if (!Mathf.Approximately(angle, target)) return;

            slamming = false;

            // The bang is at the end of the swing, and that is where it is heard.
            // A slam from the other player was already reported on their side.
            PlayClip(slamClip, 1f);
            if (!slamIsRemote) NoiseBus.Emit(hinge.position, slamNoiseRadius);
        }

        // The other player moved it. Their machine already made the noise,
        // so this only moves the door and plays the sound.
        private void ApplyRemote(int state, Vector3 payload)
        {
            float value = payload.x;

            if (state == (int)Remote.Slam)
            {
                target = value;
                slamming = true;
                slamIsRemote = true;
                return;
            }

            slamming = false;
            angle = value;
            target = value;
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
        }
    }
}
