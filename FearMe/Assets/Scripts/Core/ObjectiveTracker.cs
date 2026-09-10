using UnityEngine;

namespace FearMe.Core
{
    public class ObjectiveTracker : MonoBehaviour
    {
        public static ObjectiveTracker Instance { get; private set; }

        [SerializeField] private int keysRequired = 3;

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
        }
    }
}
