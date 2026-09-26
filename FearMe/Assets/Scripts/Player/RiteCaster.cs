using FearMe.Core;
using FearMe.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Player
{
    // Hold R to read the rite from the pages the team has gathered. One or
    // two pages buy ten desperate seconds; all three buy a full minute - but
    // every page is spent either way and scatters somewhere new.
    //
    // The reading takes a moment of standing and chanting, so doing it with
    // the demon on top of you is a gamble.
    [RequireComponent(typeof(PlayerController))]
    public class RiteCaster : MonoBehaviour
    {
        [SerializeField] private Key riteKey = Key.R;
        [Tooltip("Seconds of reading per page held - long enough to say its verse out loud.")]
        [SerializeField] private float secondsPerPage = 3.2f;
        [Tooltip("Looped while chanting.")]
        [SerializeField] private AudioClip chantLoop;

        private PlayerController player;
        private PlayerVitals vitals;
        private AudioSource voice;
        private float cooldownUntil;

        // 0-1 through the reading, for the HUD.
        public float Progress { get; private set; }

        // R is held and the rite can be read: the HUD shows the words.
        public bool Reading { get; private set; }

        private float ChantSeconds
        {
            get
            {
                int pages = SpawnDirector.Instance != null ? SpawnDirector.Instance.State.pagesHeld : 1;
                return Mathf.Max(1.5f, secondsPerPage * Mathf.Max(1, pages));
            }
        }

        // What the HUD shows under the prompt, and why it cannot be used yet.
        public bool CanChant
        {
            get
            {
                SpawnDirector director = SpawnDirector.Instance;
                if (director == null) return false;
                if (director.State.pagesHeld <= 0 || director.State.banished) return false;
                return vitals == null || !vitals.IsDown;
            }
        }

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            vitals = GetComponent<PlayerVitals>();

            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = true;
            voice.spatialBlend = 0f;
        }

        private void Update()
        {
            if (!player.IsLocalPlayer) return;

            Keyboard keyboard = Keyboard.current;
            bool held = keyboard != null && keyboard[riteKey].isPressed;
            bool chanting = held && CanChant && Time.time >= cooldownUntil;
            Reading = chanting;

            if (chanting)
            {
                Progress += Time.deltaTime / ChantSeconds;
                StartVoice();

                if (Progress >= 1f)
                {
                    Progress = 0f;
                    cooldownUntil = Time.time + 1f;
                    StopVoice();
                    SpawnDirector.Instance.Request(RunRequest.PerformRite, 0);
                }
                return;
            }

            // Stop reading and the words slip away.
            Progress = Mathf.MoveTowards(Progress, 0f, Time.deltaTime * 2f);
            StopVoice();
        }

        private void StartVoice()
        {
            if (chantLoop == null || voice.isPlaying) return;
            voice.clip = chantLoop;
            voice.volume = 0.8f * GameSettingsService.Current.sfxVolume;
            voice.Play();
        }

        private void StopVoice()
        {
            if (voice.isPlaying) voice.Stop();
        }
    }
}
