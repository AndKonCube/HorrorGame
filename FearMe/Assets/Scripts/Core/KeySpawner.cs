using System.Collections.Generic;
using FearMe.Net;
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

        private bool pruned;

        private void Awake()
        {
            // Indexed before anything is removed, so an id means the same key
            // on both machines however the pruning falls.
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == null) continue;

                candidates[i].CoopId = i;
            }

            // Online the seed arrives with the session's run state, a moment
            // after the scene loads. Hide every candidate until then, so nobody
            // can grab a key that is about to be pruned away.
            if (CoopHooks.Online && !CoopHooks.RunSeed.HasValue)
            {
                SetCandidatesActive(false);
                return;
            }

            PruneNow();
        }

        private void Update()
        {
            if (pruned) return;
            if (!CoopHooks.Online || CoopHooks.RunSeed.HasValue) PruneNow();
        }

        private void PruneNow()
        {
            pruned = true;
            SetCandidatesActive(true);

            int keep = objectives != null ? objectives.KeysRequired : fallbackKeyCount;
            Prune(keep);
        }

        private void SetCandidatesActive(bool active)
        {
            foreach (KeyItem candidate in candidates)
            {
                if (candidate != null) candidate.gameObject.SetActive(active);
            }
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

            // Online every machine prunes with the host's seed, so they all
            // end up hiding the keys in the same places.
            int seed = CoopHooks.RunSeed ?? System.Environment.TickCount;
            Shuffle(pool, new System.Random(seed));

            // Everything past the first `keep` is not part of this run.
            for (int i = keep; i < pool.Count; i++)
                Destroy(pool[i].gameObject);
        }

        // System.Random rather than UnityEngine.Random: it takes a seed
        // without disturbing the global sequence the scares draw from.
        private static void Shuffle(List<KeyItem> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swap = random.Next(0, i + 1);
                (list[i], list[swap]) = (list[swap], list[i]);
            }
        }
    }
}
