using System.Collections.Generic;
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
        [SerializeField] private float calmBeforeScare = 12f;
        [SerializeField] private float minSecondsBetweenScares = 25f;
        [SerializeField] private float maxSecondsBetweenScares = 55f;
        [Tooltip("Scares only fire while tension sits below this.")]
        [SerializeField, Range(0f, 1f)] private float tensionCeiling = 0.35f;

        [Header("Tension")]
        [SerializeField] private float threatRadius = 22f;
        [SerializeField] private float tensionRise = 0.8f;
        [SerializeField] private float tensionFall = 0.12f;

        public float Tension { get; private set; }

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

            if (calmTimer >= calmBeforeScare && Time.time >= nextScareTime)
                StageScare();
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

            float totalWeight = 0f;
            List<ScareEvent> candidates = new List<ScareEvent>();
            foreach (ScareEvent scare in scares)
            {
                if (scare == null || !scare.CanPlay(context)) continue;
                candidates.Add(scare);
                totalWeight += Mathf.Max(0.01f, scare.Weight);
            }

            if (candidates.Count == 0)
            {
                // Nothing ready; look again shortly rather than every frame.
                nextScareTime = Time.time + 5f;
                return;
            }

            float roll = Random.Range(0f, totalWeight);
            foreach (ScareEvent scare in candidates)
            {
                roll -= Mathf.Max(0.01f, scare.Weight);
                if (roll > 0f) continue;

                scare.Trigger(context);
                break;
            }

            calmTimer = 0f;
            nextScareTime = Time.time + Random.Range(minSecondsBetweenScares, maxSecondsBetweenScares);
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
