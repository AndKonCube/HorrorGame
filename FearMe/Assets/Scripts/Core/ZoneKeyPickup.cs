using FearMe.Settings;
using UnityEngine;

namespace FearMe.Core
{
    // A zone key: big, dark iron on a length of chain. No glow - that is the
    // pages' - but every few seconds the chain stirs and rattles, so a key
    // can be found by listening in the dark too, and never mistaken for a page.
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
        private float nextRattle;
        private float shake;

        public override string Prompt => "Take the " + keyName;

        public static ZoneKeyPickup Create(SpawnDirector director, Transform at, int spot, string keyName,
            GameObject prefab, AudioClip[] rattles)
        {
            GameObject root = new GameObject("ZoneKey_" + keyName);
            root.transform.SetPositionAndRotation(at.position + Vector3.up * 0.06f,
                at.rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            Transform look = prefab != null
                ? Instantiate(prefab, root.transform, false).transform
                : BuildKey(root.transform);

            // Sized to whatever the model turned out to be.
            BoxCollider box = root.AddComponent<BoxCollider>();
            Bounds bounds = new Bounds(root.transform.position, Vector3.one * 0.1f);
            foreach (Renderer r in look.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = Vector3.Max(bounds.size, new Vector3(0.3f, 0.2f, 0.3f));

            AudioSource chain = root.AddComponent<AudioSource>();
            chain.playOnAwake = false;
            chain.spatialBlend = 1f;
            chain.rolloffMode = AudioRolloffMode.Linear;
            chain.minDistance = 1f;
            chain.maxDistance = 12f;

            ZoneKeyPickup key = root.AddComponent<ZoneKeyPickup>();
            key.director = director;
            key.spot = spot;
            key.keyName = keyName;
            key.rattles = rattles;
            key.chain = chain;
            key.look = look;
            key.nextRattle = Time.time + Random.Range(1f, 4f);
            return key;
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
                nextRattle = Time.time + Random.Range(4.5f, 9f);
                shake = 1f;
                Rattle(chain, 0.7f);
            }

            // A short shiver when the chain moves, then still.
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);
            if (look != null)
                look.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 40f) * 6f * shake, 0f);
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
