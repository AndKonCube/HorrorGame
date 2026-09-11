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
        [SerializeField] private float cooldown = 45f;

        private float lastPlayed;

        public float Weight => weight;

        public virtual bool CanPlay(ScareContext context)
        {
            return Time.time >= lastPlayed + cooldown;
        }

        public void Trigger(ScareContext context)
        {
            lastPlayed = Time.time;
            OnTrigger(context);
        }

        protected abstract void OnTrigger(ScareContext context);
    }
}
