using FearMe.Scares;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FearMe.Core
{
    // Closes the image in as the stalker nears: the vignette tightens, the
    // exposure drops further and colour starts to come apart at the edges.
    // Static grade lives in the profile; only these three are animated.
    public class PostFxTensionDriver : MonoBehaviour
    {
        [SerializeField] private Volume volume;
        [SerializeField] private ScareDirector director;

        [Header("Vignette")]
        [SerializeField] private float calmVignette = 0.38f;
        [SerializeField] private float tenseVignette = 0.62f;

        [Header("Exposure (EV)")]
        [SerializeField] private float calmExposure = -0.9f;
        [SerializeField] private float tenseExposure = -1.7f;

        [Header("Chromatic aberration")]
        [SerializeField] private float calmAberration = 0.05f;
        [SerializeField] private float tenseAberration = 0.4f;

        [SerializeField] private float responseSpeed = 0.9f;

        private Vignette vignette;
        private ColorAdjustments colour;
        private ChromaticAberration aberration;
        private float smoothed;

        private void Awake()
        {
            // volume.profile hands back a runtime copy, so animating these
            // never writes back into the shared asset on disk.
            if (volume == null || volume.profile == null)
            {
                enabled = false;
                return;
            }

            volume.profile.TryGet(out vignette);
            volume.profile.TryGet(out colour);
            volume.profile.TryGet(out aberration);
        }

        private void Update()
        {
            float tension = director != null ? Mathf.Clamp01(director.Tension) : 0f;
            smoothed = Mathf.MoveTowards(smoothed, tension, responseSpeed * Time.deltaTime);

            if (vignette != null)
                vignette.intensity.Override(Mathf.Lerp(calmVignette, tenseVignette, smoothed));

            if (colour != null)
                colour.postExposure.Override(Mathf.Lerp(calmExposure, tenseExposure, smoothed));

            if (aberration != null)
                aberration.intensity.Override(Mathf.Lerp(calmAberration, tenseAberration, smoothed));
        }
    }
}
