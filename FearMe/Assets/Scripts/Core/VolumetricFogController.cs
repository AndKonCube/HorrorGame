using UnityEngine;

namespace FearMe.Core
{
    // Drives global fog plus a ground haze particle volume that follows the
    // player. Zones and scare events push profiles at it; everything blends
    // rather than snapping, so changes read as the air thickening.
    public class VolumetricFogController : MonoBehaviour
    {
        public static VolumetricFogController Instance { get; private set; }

        [SerializeField] private FogProfile baseProfile = new FogProfile();
        [SerializeField] private Transform followTarget;
        [SerializeField] private ParticleSystem hazeParticles;
        [SerializeField] private float hazeHeightOffset = 0.2f;

        private FogProfile activeProfile;
        private float densityBoost;
        private float boostDecay = 1f;

        public float CurrentDensity => RenderSettings.fogDensity;

        private void Awake()
        {
            Instance = this;
            activeProfile = baseProfile;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = baseProfile.density;
            RenderSettings.fogColor = baseProfile.color;
        }

        private void Update()
        {
            if (activeProfile == null) activeProfile = baseProfile;

            float blend = Time.deltaTime * activeProfile.blendSpeed;
            float target = activeProfile.density + densityBoost;

            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, target, blend);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, activeProfile.color, blend);

            if (densityBoost > 0f)
                densityBoost = Mathf.MoveTowards(densityBoost, 0f, boostDecay * Time.deltaTime);

            UpdateHaze();
        }

        private void UpdateHaze()
        {
            if (hazeParticles == null) return;

            ParticleSystem.EmissionModule emission = hazeParticles.emission;
            emission.rateOverTime = activeProfile.hazeRate;

            if (followTarget != null)
            {
                Vector3 position = followTarget.position;
                position.y += hazeHeightOffset;
                hazeParticles.transform.position = position;
            }
        }

        public void SetProfile(FogProfile profile)
        {
            if (profile != null) activeProfile = profile;
        }

        public void ResetProfile()
        {
            activeProfile = baseProfile;
        }

        // Used by scares: a sudden thickening that bleeds off again.
        public void Pulse(float extraDensity, float decayPerSecond = 0.02f)
        {
            densityBoost = Mathf.Max(densityBoost, extraDensity);
            boostDecay = Mathf.Max(0.001f, decayPerSecond);
        }
    }
}
