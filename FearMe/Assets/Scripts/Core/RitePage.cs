using System.Collections;
using FearMe.Settings;
using UnityEngine;

namespace FearMe.Core
{
    // One page of the holy rite. Unmistakable on purpose: a faint gold glow
    // that breathes, and whispering you hear before you see it - so a player
    // hunting for pages in the dark is hunting by ear as much as by eye.
    //
    // Built by the run director wherever the snapshot says a page is; taking
    // it asks the director, which adds it to the team's shared pages.
    public class RitePage : Interactable
    {
        private static readonly Color Gold = new Color(1f, 0.76f, 0.32f);

        private SpawnDirector director;
        private int slot;
        private Light glow;
        private Transform look;
        private Vector3 basePosition;
        private float seed;
        private bool dissolving;

        public int Spot { get; private set; }

        public override string Prompt => dissolving ? string.Empty : "Take the rite page";

        public static RitePage Create(SpawnDirector director, Transform at, int slot, int spot,
            GameObject prefab, AudioClip whisperLoop)
        {
            GameObject root = new GameObject("RitePage");
            root.transform.SetPositionAndRotation(at.position + Vector3.up * 0.35f,
                at.rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            Transform look = prefab != null
                ? Instantiate(prefab, root.transform, false).transform
                : BuildSheet(root.transform);

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.4f, 0.45f, 0.25f);

            Light glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.type = LightType.Point;
            glow.color = Gold;
            glow.range = 3f;
            glow.intensity = 0.9f;
            glow.shadows = LightShadows.None;

            if (whisperLoop != null)
            {
                AudioSource whisper = root.AddComponent<AudioSource>();
                whisper.clip = whisperLoop;
                whisper.loop = true;
                whisper.spatialBlend = 1f;
                whisper.rolloffMode = AudioRolloffMode.Linear;
                whisper.minDistance = 1f;
                whisper.maxDistance = 9f;
                whisper.volume = 0.4f * GameSettingsService.Current.sfxVolume;
                whisper.time = Random.Range(0f, whisperLoop.length);
                whisper.Play();
            }

            RitePage page = root.AddComponent<RitePage>();
            page.director = director;
            page.slot = slot;
            page.Spot = spot;
            page.glow = glow;
            page.look = look;
            page.basePosition = root.transform.position;
            page.seed = Random.value * 100f;
            return page;
        }

        // A sheet propped at an angle, as if dropped or left for someone.
        private static Transform BuildSheet(Transform parent)
        {
            Transform sheet = new GameObject("Sheet").transform;
            sheet.SetParent(parent, false);
            RuntimeMaterials.Part(PrimitiveType.Cube, sheet, Vector3.zero, new Vector3(0.21f, 0.297f, 0.004f),
                new Vector3(-20f, 0f, 0f), RuntimeMaterials.Parchment);
            return sheet;
        }

        public override void Interact()
        {
            if (dissolving || director == null) return;
            director.Request(RunRequest.TakePage, slot);
        }

        private void Update()
        {
            if (dissolving) return;

            // Hovering, turning slowly, the glow breathing unevenly.
            float t = Time.time + seed;
            transform.position = basePosition + Vector3.up * Mathf.Sin(t * 1.3f) * 0.04f;
            if (look != null) look.Rotate(Vector3.up, 12f * Time.deltaTime, Space.World);
            if (glow != null) glow.intensity = Mathf.Lerp(0.55f, 1.15f, Mathf.PerlinNoise(t * 0.8f, seed));
        }

        // Taken, or torn away by a banishment: it goes out rather than blinking off.
        public void Dissolve()
        {
            if (dissolving) return;
            dissolving = true;

            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
            StartCoroutine(FadeAndGo());
        }

        private IEnumerator FadeAndGo()
        {
            Vector3 startScale = transform.localScale;
            float startGlow = glow != null ? glow.intensity : 0f;
            AudioSource whisper = GetComponent<AudioSource>();
            float startVolume = whisper != null ? whisper.volume : 0f;

            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
            {
                transform.localScale = startScale * (1f - t);
                transform.position += Vector3.up * Time.deltaTime * 0.4f;
                if (glow != null) glow.intensity = Mathf.Lerp(startGlow * 3f, 0f, t);
                if (whisper != null) whisper.volume = Mathf.Lerp(startVolume, 0f, t);
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
