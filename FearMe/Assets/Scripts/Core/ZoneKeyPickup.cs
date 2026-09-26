using FearMe.Settings;
using UnityEngine;

namespace FearMe.Core
{
    // A zone key: big, dark iron on a length of chain. A cold steel-blue
    // glint - never the pages' gold - and every few seconds the chain stirs
    // and rattles, so a key can be found by listening in the dark too, and
    // never mistaken for a page.
    //
    // Keys are the team's for good once taken: banishment scatters pages,
    // never keys.
    public class ZoneKeyPickup : Interactable
    {
        private SpawnDirector director;
        private int spot;
        private string keyName;
        private AudioClip[] rattles;
        private AudioSource chain;
        private Transform look;
        private Light glint;
        private float nextRattle;
        private float shake;
        private float phase;

        private const float GlintLow = 0.35f, GlintHigh = 0.9f, GlintFlash = 2.2f;
        private static AudioClip[] synthRattles;

        public override string Prompt => "Take the " + keyName;

        public static ZoneKeyPickup Create(SpawnDirector director, Transform at, int spot, string keyName,
            GameObject prefab, AudioClip[] rattles)
        {
            // Built unrotated, so the model's bounds line up with the
            // collider's axes; turned to its random angle at the end.
            GameObject root = new GameObject("ZoneKey_" + keyName);
            root.transform.position = at.position + Vector3.up * 0.02f;

            // The rattle shakes this holder, never the model itself: a model
            // keeps whatever rotation it was imported with.
            Transform look = new GameObject("Look").transform;
            look.SetParent(root.transform, false);

            if (prefab != null) FitModel(Instantiate(prefab, look, false).transform, root.transform.position);
            else BuildKey(look);

            // The pickup area is exactly the key, wherever the model's pivot was.
            BoxCollider box = root.AddComponent<BoxCollider>();
            Bounds bounds = RendererBounds(look, root.transform.position);
            box.center = bounds.center - root.transform.position;
            box.size = Vector3.Max(bounds.size + Vector3.one * 0.08f, new Vector3(0.3f, 0.2f, 0.3f));

            root.transform.rotation = at.rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // A cold glint just above it, so it catches the eye in the dark.
            Light glint = new GameObject("Glint").AddComponent<Light>();
            glint.transform.SetParent(root.transform, false);
            glint.transform.localPosition = box.center + Vector3.up * 0.3f;
            glint.type = LightType.Point;
            glint.color = new Color(0.62f, 0.78f, 1f);
            glint.range = 2.2f;
            glint.intensity = GlintLow;
            glint.shadows = LightShadows.None;

            AudioSource chain = root.AddComponent<AudioSource>();
            chain.playOnAwake = false;
            chain.spatialBlend = 1f;
            chain.rolloffMode = AudioRolloffMode.Linear;
            chain.minDistance = 1f;
            chain.maxDistance = 15f;

            // No chain sounds assigned: a metal rattle made on the spot.
            if (rattles == null || rattles.Length == 0) rattles = SynthRattles();

            ZoneKeyPickup key = root.AddComponent<ZoneKeyPickup>();
            key.director = director;
            key.spot = spot;
            key.keyName = keyName;
            key.rattles = rattles;
            key.chain = chain;
            key.look = look;
            key.glint = glint;
            key.phase = Random.Range(0f, 10f);
            key.nextRattle = Time.time + Random.Range(1f, 3f);
            return key;
        }

        // Any key model, of any size and pivot: scaled to a big iron key and
        // stood on the floor, centred over where the key belongs.
        private static void FitModel(Transform model, Vector3 floor)
        {
            Bounds bounds = RendererBounds(model, model.position);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > 0.0001f && (longest < 0.12f || longest > 0.6f))
            {
                model.localScale *= 0.32f / longest;
                bounds = RendererBounds(model, model.position);
            }

            model.position += floor - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        private static Bounds RendererBounds(Transform of, Vector3 fallback)
        {
            Renderer[] renderers = of.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(fallback, Vector3.one * 0.1f);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        // A few iron links knocking together: bright, inharmonic clinks that
        // die away fast, a little looser each time.
        private static AudioClip[] SynthRattles()
        {
            if (synthRattles != null) return synthRattles;

            const int rate = 44100;
            synthRattles = new AudioClip[3];
            System.Random random = new System.Random(1234);

            for (int v = 0; v < synthRattles.Length; v++)
            {
                float[] data = new float[(int)(rate * 0.7f)];
                float at = 0f;
                int clinks = 5 + v * 2;

                for (int c = 0; c < clinks && at < 0.6f; c++)
                {
                    float pitch = 0.8f + (float)random.NextDouble() * 0.5f;
                    float loud = (1f - c / (float)(clinks + 1)) * (0.5f + (float)random.NextDouble() * 0.5f);
                    int start = (int)(at * rate);

                    for (int i = 0; i < rate * 0.09f && start + i < data.Length; i++)
                    {
                        float s = i / (float)rate;
                        float ring = Mathf.Sin(2f * Mathf.PI * 2150f * pitch * s)
                                   + 0.6f * Mathf.Sin(2f * Mathf.PI * 3470f * pitch * s)
                                   + 0.35f * Mathf.Sin(2f * Mathf.PI * 5230f * pitch * s);
                        float hit = i < 40 ? ((float)random.NextDouble() * 2f - 1f) * 0.6f : 0f;
                        data[start + i] += (ring * 0.4f + hit) * loud * Mathf.Exp(-s * 55f);
                    }

                    at += 0.03f + (float)random.NextDouble() * 0.09f;
                }

                AudioClip clip = AudioClip.Create("KeyRattle" + v, data.Length, 1, rate, false);
                clip.SetData(data, 0);
                synthRattles[v] = clip;
            }

            return synthRattles;
        }

        // A heavy ward key - long shaft, wide bow, blunt teeth - on a few
        // links of chain.
        private static Transform BuildKey(Transform parent)
        {
            Transform key = new GameObject("IronKey").transform;
            key.SetParent(parent, false);
            Material iron = RuntimeMaterials.Iron;

            RuntimeMaterials.Part(PrimitiveType.Cube, key, new Vector3(0f, 0.02f, 0f), new Vector3(0.035f, 0.035f, 0.26f), Vector3.zero, iron);
            RuntimeMaterials.Part(PrimitiveType.Cylinder, key, new Vector3(0f, 0.02f, -0.17f), new Vector3(0.11f, 0.012f, 0.11f), new Vector3(90f, 0f, 0f), iron);
            RuntimeMaterials.Part(PrimitiveType.Cube, key, new Vector3(0.03f, 0.02f, 0.1f), new Vector3(0.05f, 0.03f, 0.03f), Vector3.zero, iron);
            RuntimeMaterials.Part(PrimitiveType.Cube, key, new Vector3(0.03f, 0.02f, 0.06f), new Vector3(0.04f, 0.03f, 0.025f), Vector3.zero, iron);

            for (int i = 0; i < 4; i++)
            {
                RuntimeMaterials.Part(PrimitiveType.Cylinder, key,
                    new Vector3(0f, 0.01f, -0.26f - i * 0.045f), new Vector3(0.035f, 0.006f, 0.035f),
                    new Vector3(i % 2 == 0 ? 90f : 0f, 0f, 90f), iron);
            }

            return key;
        }

        public override void Interact()
        {
            if (director != null) director.Request(RunRequest.TakeKey, spot);
        }

        private void Update()
        {
            if (Time.time >= nextRattle)
            {
                nextRattle = Time.time + Random.Range(3.5f, 7f);
                shake = 1f;
                Rattle(chain, 0.8f);
            }

            // A short shiver when the chain moves, then still.
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);
            if (look != null)
                look.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 40f) * 6f * shake, 0f);

            // A slow breathing glint, flaring as the chain moves.
            if (glint != null)
            {
                float breathe = 0.5f + 0.5f * Mathf.Sin((Time.time + phase) * 2.4f);
                glint.intensity = Mathf.Lerp(GlintLow, GlintHigh, breathe) + GlintFlash * shake;
            }
        }

        public void PlayTaken()
        {
            if (rattles == null || rattles.Length == 0) return;
            AudioClip clip = rattles[Random.Range(0, rattles.Length)];
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, GameSettingsService.Current.sfxVolume);
        }

        private void Rattle(AudioSource source, float volume)
        {
            if (source == null || rattles == null || rattles.Length == 0) return;
            AudioClip clip = rattles[Random.Range(0, rattles.Length)];
            if (clip != null) source.PlayOneShot(clip, volume * GameSettingsService.Current.sfxVolume);
        }
    }
}
