using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FearMe.Player
{
    // Stay in the dark and your grip on things slips. Your own torch only
    // slows it; real light - a lamp, a working fixture, your partner's beam -
    // brings you back, and so does standing next to them.
    //
    // As it goes, the image starts to fail: warping, colour tearing, grain,
    // bands of static. Below that, the HallucinationDirector starts showing
    // you things. All of it is this machine only - your partner sees none of
    // it, and hears only you telling them about it.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerSanity : MonoBehaviour
    {
        [Header("Drain and recovery (per second, sanity is 0-1)")]
        [Tooltip("In full darkness: ~90 seconds from sane to gone.")]
        [SerializeField] private float darkDrain = 0.011f;
        [Tooltip("Your own torch only takes the edge off.")]
        [SerializeField, Range(0f, 1f)] private float torchDrainScale = 0.35f;
        [Tooltip("Company helps: drain is scaled by this near your partner.")]
        [SerializeField, Range(0f, 1f)] private float companyDrainScale = 0.5f;
        [SerializeField] private float companyRange = 4f;
        [SerializeField] private float lightRecovery = 0.03f;

        [Header("What counts as lit")]
        [Tooltip("Summed light reaching you, above which you are 'in the light'.")]
        [SerializeField] private float litThreshold = 0.35f;
        [SerializeField] private float lightRescanInterval = 2f;

        [Header("Screen")]
        [Tooltip("Sanity below this starts to show on screen.")]
        [SerializeField] private float glitchStart = 0.6f;
        [SerializeField] private Vector2 burstInterval = new Vector2(1.5f, 7f);
        [SerializeField] private float burstLength = 0.25f;

        private readonly List<Light> lights = new List<Light>();
        private PlayerController player;
        private Flashlight torch;

        private Volume volume;
        private LensDistortion distortion;
        private ChromaticAberration aberration;
        private FilmGrain grain;
        private ColorAdjustments colour;

        private float nextRescan;
        private float nextBurst;
        private float burstUntil;
        private float bandSeed;
        private Texture2D bandTexture;

        public float Sanity { get; private set; } = 1f;

        // 0 while fine, rising to 1 at the bottom. What the effects scale on.
        public float Unease => Mathf.Clamp01(Mathf.InverseLerp(glitchStart, 0f, Sanity));

        public bool InBurst => Time.time < burstUntil;

        // For hallucinations and anything else that knocks it.
        public void Shake(float amount)
        {
            Sanity = Mathf.Clamp01(Sanity - amount);
            TriggerBurst();
        }

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            torch = GetComponentInChildren<Flashlight>();
        }

        private void Start()
        {
            if (!player.IsLocalPlayer)
            {
                enabled = false;
                return;
            }

            BuildVolume();
            nextBurst = Time.time + Random.Range(burstInterval.x, burstInterval.y);
        }

        private void OnDestroy()
        {
            if (volume != null)
            {
                if (volume.profile != null) Destroy(volume.profile);
                Destroy(volume.gameObject);
            }
            if (bandTexture != null) Destroy(bandTexture);
        }

        // Its own volume, layered over the scene's by weight, so it never
        // fights the tension driver over the same settings.
        private void BuildVolume()
        {
            GameObject go = new GameObject("SanityVolume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.weight = 0f;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            distortion = profile.Add<LensDistortion>(true);
            aberration = profile.Add<ChromaticAberration>(true);
            grain = profile.Add<FilmGrain>(true);
            colour = profile.Add<ColorAdjustments>(true);

            grain.type.Override(FilmGrainLookup.Medium3);
            volume.profile = profile;
        }

        private void Update()
        {
            TickSanity();
            TickScreen();
        }

        private void TickSanity()
        {
            if (Time.time >= nextRescan)
            {
                nextRescan = Time.time + lightRescanInterval;
                lights.Clear();
                lights.AddRange(FindObjectsByType<Light>(FindObjectsSortMode.None));
            }

            if (LightHere() >= litThreshold)
            {
                Sanity = Mathf.MoveTowards(Sanity, 1f, lightRecovery * Time.deltaTime);
                return;
            }

            float drain = darkDrain;
            if (torch != null && torch.IsOn) drain *= torchDrainScale;
            if (PartnerNearby()) drain *= companyDrainScale;

            Sanity = Mathf.MoveTowards(Sanity, 0f, drain * Time.deltaTime);
        }

        // Every light but your own torch, roughly as the renderer would
        // fall it off. Good enough to tell a lit room from a dark one.
        private float LightHere()
        {
            Vector3 here = transform.position + Vector3.up * 1.2f;
            float total = 0f;

            foreach (Light light in lights)
            {
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy) continue;
                if (light.transform.IsChildOf(transform)) continue; // your own torch
                if (light.type == LightType.Directional) continue;  // moonlight is not safety

                Vector3 toHere = here - light.transform.position;
                float distance = toHere.magnitude;
                if (distance >= light.range) continue;

                if (light.type == LightType.Spot &&
                    Vector3.Angle(light.transform.forward, toHere) > light.spotAngle * 0.5f)
                    continue;

                float falloff = 1f - distance / light.range;
                total += light.intensity * falloff * falloff;
            }

            return total;
        }

        private bool PartnerNearby()
        {
            foreach (PlayerController other in FearMe.Core.PlayerRegistry.All)
            {
                if (other == null || other == player) continue;
                if (Vector3.Distance(other.transform.position, transform.position) <= companyRange) return true;
            }
            return false;
        }

        private void TickScreen()
        {
            if (volume == null) return;

            float unease = Unease;

            // Bursts come more often the further gone you are.
            if (unease > 0f && Time.time >= nextBurst)
            {
                TriggerBurst();
                float scale = Mathf.Lerp(1f, 0.3f, unease);
                nextBurst = Time.time + Random.Range(burstInterval.x, burstInterval.y) * scale;
            }

            bool burst = InBurst;
            volume.weight = Mathf.Clamp01(unease + (burst ? 0.5f : 0f));

            float jitter = burst ? Random.Range(-1f, 1f) : Mathf.Sin(Time.time * 0.7f);
            distortion.intensity.Override(jitter * Mathf.Lerp(0.05f, 0.45f, unease));
            aberration.intensity.Override(burst ? 1f : Mathf.Lerp(0.1f, 0.6f, unease));
            grain.intensity.Override(Mathf.Lerp(0.2f, 0.9f, unease));
            colour.saturation.Override(Mathf.Lerp(-10f, -70f, unease));
            colour.hueShift.Override(burst ? Random.Range(-25f, 25f) : 0f);
        }

        private void TriggerBurst()
        {
            burstUntil = Time.time + burstLength;
            bandSeed = Random.value * 1000f;
        }

        // Bands of static across the screen during a burst - the cheapest
        // convincing "the picture is failing" there is.
        private void OnGUI()
        {
            if (!InBurst || Unease <= 0f) return;

            if (bandTexture == null)
            {
                bandTexture = new Texture2D(1, 1);
                bandTexture.SetPixel(0, 0, Color.white);
                bandTexture.Apply();
            }

            Random.State saved = Random.state;
            Random.InitState(Mathf.FloorToInt(bandSeed + Time.time * 30f));

            int bands = Mathf.RoundToInt(Mathf.Lerp(2f, 9f, Unease));
            for (int i = 0; i < bands; i++)
            {
                float y = Random.value * Screen.height;
                float h = Random.Range(2f, 18f);
                float x = Random.Range(-40f, 40f);
                float shade = Random.value;

                GUI.color = new Color(shade, shade, shade, Random.Range(0.08f, 0.35f));
                GUI.DrawTexture(new Rect(x, y, Screen.width, h), bandTexture);
            }

            GUI.color = Color.white;
            Random.state = saved;
        }
    }
}
