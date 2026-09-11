using FearMe.Scares;
using FearMe.Settings;
using UnityEngine;
using UnityEngine.AI;

namespace FearMe.Core
{
    // Small sounds with a place in the world: a creak off to the left, steps
    // in a corridor you are not in. Quiet and frequent, unlike the director's
    // scares - the point is that the player explains them to themselves, and
    // explains them wrong.
    public class DiegeticSoundscape : MonoBehaviour
    {
        [SerializeField] private Transform listener;
        [SerializeField] private ScareDirector director;

        [Header("Sounds")]
        [Tooltip("Creaks, settling, pipes - things a building does.")]
        [SerializeField] private AudioClip[] structureSounds;
        [Tooltip("Footsteps and movement somewhere out of sight.")]
        [SerializeField] private AudioClip[] movementSounds;

        [Header("Placement")]
        [SerializeField] private float minDistance = 6f;
        [SerializeField] private float maxDistance = 22f;
        [SerializeField] private int voices = 3;

        [Header("Pacing")]
        [SerializeField] private Vector2 calmInterval = new Vector2(9f, 22f);
        [Tooltip("Interval multiplier at full tension - they crowd in.")]
        [SerializeField] private float tenseIntervalScale = 0.45f;
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;

        private AudioSource[] pool;
        private int nextVoice;
        private float nextPlayTime;

        private void Awake()
        {
            pool = new AudioSource[Mathf.Max(1, voices)];
            for (int i = 0; i < pool.Length; i++)
                pool[i] = CreateVoice(i);
        }

        private AudioSource CreateVoice(int index)
        {
            GameObject go = new GameObject("DiegeticVoice_" + index);
            go.transform.SetParent(transform, false);

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = 35f;
            return source;
        }

        private void Start()
        {
            nextPlayTime = Time.time + Random.Range(calmInterval.x, calmInterval.y);
        }

        private void Update()
        {
            if (Time.time < nextPlayTime) return;

            PlayOne();

            float tension = director != null ? Mathf.Clamp01(director.Tension) : 0f;
            float scale = Mathf.Lerp(1f, tenseIntervalScale, tension);
            nextPlayTime = Time.time + Random.Range(calmInterval.x, calmInterval.y) * scale;
        }

        private void PlayOne()
        {
            if (listener == null) return;

            AudioClip clip = ChooseClip();
            if (clip == null) return;

            if (!TryFindSpot(out Vector3 spot)) return;

            AudioSource source = pool[nextVoice];
            nextVoice = (nextVoice + 1) % pool.Length;

            source.transform.position = spot;
            source.clip = clip;
            source.volume = volume * GameSettingsService.Current.sfxVolume;
            source.Play();
        }

        private AudioClip ChooseClip()
        {
            bool hasStructure = structureSounds != null && structureSounds.Length > 0;
            bool hasMovement = movementSounds != null && movementSounds.Length > 0;

            if (!hasStructure && !hasMovement) return null;
            if (!hasMovement) return Pick(structureSounds);
            if (!hasStructure) return Pick(movementSounds);

            // Footsteps are the rarer, more alarming half.
            return Random.value < 0.65f ? Pick(structureSounds) : Pick(movementSounds);
        }

        private static AudioClip Pick(AudioClip[] clips)
        {
            return clips[Random.Range(0, clips.Length)];
        }

        // Somewhere reachable, so the sound comes from floor the player could
        // walk to rather than out of the middle of a wall.
        private bool TryFindSpot(out Vector3 spot)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(minDistance, maxDistance);
                Vector3 candidate = listener.position + new Vector3(offset.x, 0f, offset.y);

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    spot = hit.position + Vector3.up;
                    return true;
                }
            }

            spot = Vector3.zero;
            return false;
        }
    }
}
