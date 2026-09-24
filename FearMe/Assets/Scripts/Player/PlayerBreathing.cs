using FearMe.Scares;
using FearMe.Settings;
using UnityEngine;

namespace FearMe.Player
{
    // The player's own breathing, rising with exertion and with how close the
    // stalker is. Hearing yourself panic is a strong cue that something is
    // wrong even when nothing is on screen.
    public class PlayerBreathing : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private ScareDirector director;
        [SerializeField] private AudioClip[] calmBreaths;
        [SerializeField] private AudioClip[] heavyBreaths;

        [Header("Pacing")]
        [SerializeField] private float calmInterval = 5.5f;
        [SerializeField] private float heavyInterval = 1.6f;
        [SerializeField, Range(0f, 1f)] private float calmVolume = 0.25f;
        [SerializeField, Range(0f, 1f)] private float heavyVolume = 0.7f;
        [Tooltip("Exertion above which breathing turns heavy.")]
        [SerializeField, Range(0f, 1f)] private float heavyThreshold = 0.5f;

        private AudioSource source;
        private float nextBreath;
        private float exertion;

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // your own breathing has no direction
        }

        private void Update()
        {
            exertion = Mathf.Clamp01(CurrentExertion());

            // Holding it in: silence, and the next breath comes the moment
            // they let go.
            if (player != null && player.HoldingBreath)
            {
                nextBreath = Time.time + 0.15f;
                return;
            }

            if (Time.time < nextBreath) return;

            PlayBreath();
            float interval = Mathf.Lerp(calmInterval, heavyInterval, exertion);
            nextBreath = Time.time + interval * Random.Range(0.85f, 1.15f);
        }

        private float CurrentExertion()
        {
            float fear = director != null ? director.Tension : 0f;
            float effort = 0f;

            if (player != null)
            {
                // Crouching and holding still is the one time you hold it in.
                effort = player.IsHidden ? 0f : player.CurrentNoiseRadius / 9f;
            }

            return Mathf.Max(fear, effort);
        }

        private void PlayBreath()
        {
            bool heavy = exertion >= heavyThreshold;
            AudioClip[] set = heavy ? heavyBreaths : calmBreaths;

            if (set == null || set.Length == 0) set = heavy ? calmBreaths : heavyBreaths;
            if (set == null || set.Length == 0) return;

            // This writes volume per breath, so it applies the SFX setting
            // itself rather than adding an AudioCategoryVolume to fight with.
            source.clip = set[Random.Range(0, set.Length)];
            source.volume = Mathf.Lerp(calmVolume, heavyVolume, exertion)
                * GameSettingsService.Current.sfxVolume;
            source.Play();
        }
    }
}
