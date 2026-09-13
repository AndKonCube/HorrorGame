using FearMe.Core;
using UnityEngine;
using UnityEngine.AI;

namespace FearMe.Scares
{
    // Moves a 3D audio source somewhere meaningful - right behind the player,
    // or off down a corridor - and plays a clip there, so the sound has a
    // direction to look towards.
    //
    // Clips are intentionally empty for now; CanPlay reports false until at
    // least one is assigned, so the director quietly picks another scare.
    public class PositionalSoundScare : ScareEvent
    {
        public enum Placement
        {
            BehindPlayer,
            Nearby,
            FarOff
        }

        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip[] clips;
        [SerializeField] private Placement placement = Placement.BehindPlayer;

        [Header("Distances")]
        [SerializeField] private float behindDistance = 4f;
        [SerializeField] private float nearbyDistance = 10f;
        [SerializeField] private float farDistance = 24f;

        public override bool CanPlay(ScareContext context)
        {
            return base.CanPlay(context) && source != null && clips != null && clips.Length > 0;
        }

        protected override bool OnTrigger(ScareContext context)
        {
            if (source == null || clips == null || clips.Length == 0) return false;

            source.transform.position = ChoosePosition(context);
            source.clip = clips[Random.Range(0, clips.Length)];
            source.Play();
            return true;
        }

        private Vector3 ChoosePosition(ScareContext context)
        {
            Vector3 origin = context.Player != null ? context.Player.position : transform.position;

            Vector3 forward = context.Eye != null ? context.Eye.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 direction;
            float distance;

            switch (placement)
            {
                case Placement.BehindPlayer:
                    direction = -forward;
                    distance = behindDistance;
                    break;
                case Placement.FarOff:
                    direction = Random.insideUnitSphere;
                    direction.y = 0f;
                    direction = direction.sqrMagnitude < 0.001f ? forward : direction.normalized;
                    distance = farDistance;
                    break;
                default:
                    direction = Quaternion.AngleAxis(Random.Range(-120f, 120f), Vector3.up) * forward;
                    distance = nearbyDistance;
                    break;
            }

            Vector3 candidate = origin + direction * distance;
            if (NavMeshUtility.TrySampleSameFloor(candidate, out Vector3 onMesh))
                return onMesh + Vector3.up;

            return candidate;
        }
    }
}
