using UnityEngine;

namespace FearMe.UI
{
    // Plays menu tracks back to back. Volume is deliberately left alone -
    // an AudioCategoryVolume on the same object applies the music setting and
    // keeps up as the slider moves, so nothing fights over the field.
    [RequireComponent(typeof(AudioSource))]
    public class MenuMusic : MonoBehaviour
    {
        [SerializeField] private AudioClip[] tracks;
        [SerializeField] private bool shuffle = true;

        private AudioSource source;
        private int lastIndex = -1;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            // A single track has nothing to advance to, so let it loop.
            source.loop = tracks != null && tracks.Length == 1;
        }

        private void Start()
        {
            PlayNext();
        }

        private void Update()
        {
            if (source.loop || source.clip == null) return;
            if (!source.isPlaying) PlayNext();
        }

        private void PlayNext()
        {
            if (tracks == null || tracks.Length == 0) return;

            int index = shuffle ? PickShuffled() : (lastIndex + 1) % tracks.Length;
            lastIndex = index;

            AudioClip clip = tracks[index];
            if (clip == null) return;

            source.clip = clip;
            source.Play();
        }

        private int PickShuffled()
        {
            if (tracks.Length <= 1) return 0;

            int index = Random.Range(0, tracks.Length);
            if (index == lastIndex) index = (index + 1) % tracks.Length;
            return index;
        }
    }
}
