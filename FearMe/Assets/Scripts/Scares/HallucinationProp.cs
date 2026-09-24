using FearMe.Core;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Scares
{
    // Something that is not there. It looks like a key, or a door, and it
    // answers to the interact key like one - and then it is gone, with a
    // whisper and another knock to the player's sanity.
    //
    // Exists only on this machine. Never touches the objectives, the network
    // or the noise the stalker hears.
    public class HallucinationProp : Interactable
    {
        private string prompt = "Take key";
        private PlayerSanity sanity;
        private AudioClip[] whispers;
        private float sanityCost;
        private float spin;
        private float lifeLeft;
        private Vector3 basePosition;

        public override string Prompt => prompt;

        public void Setup(string label, PlayerSanity owner, AudioClip[] whisperClips,
            float cost, float spinSpeed, float lifetime)
        {
            prompt = label;
            sanity = owner;
            whispers = whisperClips;
            sanityCost = cost;
            spin = spinSpeed;
            lifeLeft = lifetime;
            basePosition = transform.position;
        }

        public override void Interact()
        {
            if (sanity != null) sanity.Shake(sanityCost);
            Vanish();
        }

        private void Update()
        {
            if (spin != 0f)
            {
                transform.Rotate(Vector3.up, spin * Time.deltaTime, Space.World);
                transform.position = basePosition + Vector3.up * Mathf.Sin(Time.time * 2f) * 0.15f;
            }

            lifeLeft -= Time.deltaTime;
            if (lifeLeft <= 0f) Vanish();
        }

        private void Vanish()
        {
            if (whispers != null && whispers.Length > 0)
            {
                AudioClip clip = whispers[Random.Range(0, whispers.Length)];
                if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, 0.8f);
            }

            Destroy(gameObject);
        }
    }
}
