using System.Collections;
using System.Collections.Generic;
using FearMe.Core;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Scares
{
    // Paces the frights. Scares land hardest when the player feels safe, so
    // this tracks how threatened they are and only stages something once
    // they have been calm for a while. During a chase it stays quiet and
    // lets the stalker do the work.
    public class ScareDirector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerController player;
        [SerializeField] private Camera playerCamera;
        [Tooltip("Used only for distance, so the director knows when a chase is on.")]
        [SerializeField] private Transform stalker;
        [SerializeField] private List<ScareEvent> scares = new List<ScareEvent>();

        [Header("Pacing")]
        [SerializeField] private float calmBeforeScare = 8f;
        [SerializeField] private float minSecondsBetweenScares = 18f;
        [SerializeField] private float maxSecondsBetweenScares = 38f;
        [Tooltip("Scares only fire while tension sits below this.")]
        [SerializeField, Range(0f, 1f)] private float tensionCeiling = 0.35f;
        [Tooltip("Seconds of dead air staged immediately before a scare.")]
        [SerializeField] private float silenceBeforeScare = 2.5f;

        [Header("Tension")]
        [SerializeField] private float threatRadius = 22f;
        [SerializeField] private float tensionRise = 0.8f;
        [SerializeField] private float tensionFall = 0.12f;

        public float Tension { get; private set; }

        private bool staging;
        private float calmTimer;
        private float nextScareTime;

        private void Start()
        {
            nextScareTime = Time.time + minSecondsBetweenScares;
        }

        private void Update()
        {
            UpdateTension();

            if (Tension < tensionCeiling) calmTimer += Time.deltaTime;
            else calmTimer = 0f;

            if (!staging && calmTimer >= calmBeforeScare && Time.time >= nextScareTime)
                StartCoroutine(StageAfterSilence());
        }

        // The quiet is what makes the hit land, so cut the ambience first.
        private IEnumerator StageAfterSilence()
        {
            staging = true;

            if (silenceBeforeScare > 0f && AmbientAudioController.Instance != null)
            {
                AmbientAudioController.Instance.DropToSilence(silenceBeforeScare + 2f);
                yield return new WaitForSeconds(silenceBeforeScare);
            }

            StageScare();
            staging = false;
        }

        private void UpdateTension()
        {
            float threat = 0f;

            if (stalker != null && player != null)
            {
                float distance = Vector3.Distance(stalker.position, player.transform.position);
                if (distance < threatRadius)
                    threat = 1f - (distance / threatRadius);
            }

            if (threat > 0f)
                Tension = Mathf.MoveTowards(Tension, threat, tensionRise * Time.deltaTime);
            else
                Tension = Mathf.MoveTowards(Tension, 0f, tensionFall * Time.deltaTime);
        }

        private void StageScare()
        {
            ScareContext context = BuildContext();

            List<ScareEvent> candidates = new List<ScareEvent>();
            foreach (ScareEvent scare in scares)
            {
                if (scare == null || !scare.CanPlay(context)) continue;
                candidates.Add(scare);
            }

            // Keep trying down the list: a scare that cannot place itself
            // hands the slot on rather than swallowing it.
            while (candidates.Count > 0)
            {
                ScareEvent pick = WeightedPick(candidates);
                if (pick == null) break;

                if (pick.Trigger(context))
                {
                    calmTimer = 0f;
                    nextScareTime = Time.time + Random.Range(minSecondsBetweenScares, maxSecondsBetweenScares);
                    return;
                }

                candidates.Remove(pick);
            }

            // Nothing could fire; look again shortly rather than every frame.
            nextScareTime = Time.time + 5f;
        }

        private static ScareEvent WeightedPick(List<ScareEvent> candidates)
        {
            float total = 0f;
            foreach (ScareEvent scare in candidates)
                total += Mathf.Max(0.01f, scare.Weight);

            float roll = Random.Range(0f, total);
            foreach (ScareEvent scare in candidates)
            {
                roll -= Mathf.Max(0.01f, scare.Weight);
                if (roll <= 0f) return scare;
            }

            return candidates.Count > 0 ? candidates[candidates.Count - 1] : null;
        }

        private ScareContext BuildContext()
        {
            return new ScareContext
            {
                Player = player != null ? player.transform : transform,
                Eye = playerCamera != null ? playerCamera.transform : transform,
                PlayerHidden = player != null && player.IsHidden,
                Tension = Tension
            };
        }
    }
}
