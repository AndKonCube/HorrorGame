using System.Collections.Generic;
using UnityEngine;

namespace FearMe.Core
{
    // Keys sit at every candidate spot in the scene; this keeps a random
    // handful and removes the rest at load, so each run hides them somewhere
    // different without needing prefabs or runtime instantiation.
    public class KeySpawner : MonoBehaviour
    {
        [SerializeField] private List<KeyItem> candidates = new List<KeyItem>();
        [SerializeField] private ObjectiveTracker objectives;
        [Tooltip("Used only when no ObjectiveTracker is assigned.")]
        [SerializeField] private int fallbackKeyCount = 3;

        private void Awake()
        {
            int keep = objectives != null ? objectives.KeysRequired : fallbackKeyCount;
            Prune(keep);
        }

        private void Prune(int keep)
        {
            List<KeyItem> pool = new List<KeyItem>();
            foreach (KeyItem candidate in candidates)
            {
                if (candidate != null) pool.Add(candidate);
            }

            keep = Mathf.Clamp(keep, 0, pool.Count);

            if (pool.Count < keep)
            {
                Debug.LogWarning("[FearMe] Fewer key spots than keys required.");
                return;
            }

            Shuffle(pool);

            // Everything past the first `keep` is not part of this run.
            for (int i = keep; i < pool.Count; i++)
                Destroy(pool[i].gameObject);
        }

        private static void Shuffle(List<KeyItem> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);
                (list[i], list[swap]) = (list[swap], list[i]);
            }
        }
    }
}
