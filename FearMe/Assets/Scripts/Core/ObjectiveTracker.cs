using UnityEngine;

namespace FearMe.Core
{
    public class ObjectiveTracker : MonoBehaviour
    {
        public static ObjectiveTracker Instance { get; private set; }

        [SerializeField] private int keysRequired = 3;

        public event System.Action Changed;

        public int KeysRequired => keysRequired;
        public int KeysCollected { get; private set; }
        public bool AllKeysCollected => KeysCollected >= keysRequired;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void CollectKey()
        {
            KeysCollected++;
            Changed?.Invoke();
        }

        // The run director decides how many keys there are - one per zone.
        public void SetKeysRequired(int value)
        {
            keysRequired = Mathf.Max(0, value);
            Changed?.Invoke();
        }

        // Online the server owns the count, so a client takes it whole rather
        // than incrementing and drifting.
        public void SetKeysCollected(int value)
        {
            int clamped = Mathf.Clamp(value, 0, keysRequired);
            if (clamped == KeysCollected) return;

            KeysCollected = clamped;
            Changed?.Invoke();
        }
    }
}
