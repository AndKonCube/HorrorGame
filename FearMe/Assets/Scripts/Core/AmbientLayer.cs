using UnityEngine;

namespace FearMe.Core
{
    // One strand of the ambient bed. Layers stack as tension climbs, each
    // arriving at its own threshold, so the air thickens by degrees instead
    // of a single track getting louder.
    [System.Serializable]
    public class AmbientLayer
    {
        public string label = "Layer";
        public AudioSource source;
        public AudioClip clip;

        [Tooltip("Tension at which this layer starts to come in.")]
        [Range(0f, 1f)] public float startsAt = 0.3f;
        [Range(0f, 1f)] public float maxVolume = 0.5f;
    }
}
