using UnityEngine;

namespace FearMe.Scares
{
    public struct ScareContext
    {
        public Transform Player;
        public Transform Eye;
        public bool PlayerHidden;
        public float Tension;
    }

    public abstract class ScareEvent : MonoBehaviour
    {
        [SerializeField] private float weight = 1f;
        [Tooltip("Seconds before this particular scare can repeat.")]
        [SerializeField] private float cooldown = 30f;

        private float lastPlayed;

        public float Weight => weight;

        public virtual bool CanPlay(ScareContext context)
        {
            return Time.time >= lastPlayed + cooldown;
        }

        // Returns false when the scare could not actually stage itself, so a
        // failed attempt neither burns the cooldown nor wastes the director's
        // turn - it can hand the slot to another scare instead.
        public bool Trigger(ScareContext context)
        {
            if (!OnTrigger(context)) return false;

            lastPlayed = Time.time;
            return true;
        }

        protected abstract bool OnTrigger(ScareContext context);
    }
}
