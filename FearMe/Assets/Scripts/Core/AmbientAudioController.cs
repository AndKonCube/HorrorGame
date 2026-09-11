using System.Collections;
using FearMe.Scares;
using FearMe.Settings;
using UnityEngine;

namespace FearMe.Core
{
    // Two layers of atmosphere.
    //
    // The bed plays a track, then goes quiet for a while, then plays another.
    // The silence is the point - a pad looping forever stops registering after
    // a minute, while returning silence keeps the player listening.
    //
    // The tension layer loops underneath and its volume follows the director's
    // tension, so the music closes in as the stalker does without any cue
    // that a chase has "started".
    public class AmbientAudioController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ScareDirector director;
        [SerializeField] private AudioSource bedSource;
        [SerializeField] private AudioSource tensionSource;

        [Header("Ambient bed")]
        [SerializeField] private AudioClip[] calmTracks;
        [SerializeField] private float bedVolume = 0.35f;
        [SerializeField] private Vector2 silenceBetweenTracks = new Vector2(14f, 40f);
        [SerializeField] private float fadeDuration = 3f;

        [Header("Tension layer")]
        [SerializeField] private AudioClip[] tensionTracks;
        [SerializeField] private float tensionVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float tensionOnset = 0.15f;
        [SerializeField] private float tensionFadeSpeed = 0.5f;

        private float bedFade;
        private float bedDuck = 1f;

        private void Awake()
        {
            PrepareSource(bedSource, loop: false);
            PrepareSource(tensionSource, loop: true);
        }

        private static void PrepareSource(AudioSource source, bool loop)
        {
            if (source == null) return;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
        }

        private void Start()
        {
            if (bedSource != null && calmTracks != null && calmTracks.Length > 0)
                StartCoroutine(BedLoop());

            if (tensionSource != null && tensionTracks != null && tensionTracks.Length > 0)
            {
                tensionSource.clip = tensionTracks[Random.Range(0, tensionTracks.Length)];
                tensionSource.Play();
            }
        }

        private void Update()
        {
            float tension = director != null ? Mathf.Clamp01(director.Tension) : 0f;

            // This controller writes volume every frame, so it applies the
            // ambient setting itself rather than using AudioCategoryVolume.
            float ambientScale = GameSettingsService.Current.ambientVolume;

            if (tensionSource != null)
            {
                float target = tension > tensionOnset
                    ? Mathf.InverseLerp(tensionOnset, 1f, tension) * tensionVolume * ambientScale
                    : 0f;
                tensionSource.volume = Mathf.MoveTowards(
                    tensionSource.volume, target, tensionFadeSpeed * Time.deltaTime);
            }

            // Pull the calm bed down while the tension layer takes over.
            bedDuck = Mathf.MoveTowards(bedDuck, 1f - tension, tensionFadeSpeed * Time.deltaTime);

            if (bedSource != null)
                bedSource.volume = bedVolume * bedFade * bedDuck * ambientScale;
        }

        private IEnumerator BedLoop()
        {
            yield return new WaitForSeconds(Random.Range(1f, 4f));

            int lastIndex = -1;
            while (true)
            {
                int index = PickIndex(calmTracks.Length, lastIndex);
                lastIndex = index;

                AudioClip clip = calmTracks[index];
                if (clip == null)
                {
                    yield return null;
                    continue;
                }

                bedSource.clip = clip;
                bedSource.Play();

                yield return FadeBed(0f, 1f, fadeDuration);

                float hold = Mathf.Max(0.1f, clip.length - fadeDuration * 2f);
                yield return new WaitForSeconds(hold);

                yield return FadeBed(1f, 0f, fadeDuration);
                bedSource.Stop();

                yield return new WaitForSeconds(
                    Random.Range(silenceBetweenTracks.x, silenceBetweenTracks.y));
            }
        }

        private IEnumerator FadeBed(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                bedFade = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            bedFade = to;
        }

        private static int PickIndex(int count, int avoid)
        {
            if (count <= 1) return 0;

            int index = Random.Range(0, count);
            if (index == avoid) index = (index + 1) % count;
            return index;
        }
    }
}
