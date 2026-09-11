using UnityEngine;

namespace FearMe.Settings
{
    // Scales one AudioSource by its category setting. Master is handled
    // globally by AudioListener.volume, so this only deals with the split.
    //
    // Do not put this on a source whose volume another script animates -
    // they would fight each frame. AmbientAudioController scales itself.
    [RequireComponent(typeof(AudioSource))]
    public class AudioCategoryVolume : MonoBehaviour
    {
        public enum Category
        {
            Sfx,
            Ambient
        }

        [SerializeField] private Category category = Category.Sfx;
        [SerializeField, Range(0f, 1f)] private float baseVolume = 1f;

        private AudioSource source;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            GameSettingsService.Changed += ApplyVolume;
            ApplyVolume();
        }

        private void OnDisable()
        {
            GameSettingsService.Changed -= ApplyVolume;
        }

        private void ApplyVolume()
        {
            if (source == null) return;

            GameSettings settings = GameSettingsService.Current;
            float categoryVolume = category == Category.Ambient
                ? settings.ambientVolume
                : settings.sfxVolume;

            source.volume = baseVolume * categoryVolume;
        }
    }
}
