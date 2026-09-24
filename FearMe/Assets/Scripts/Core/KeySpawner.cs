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
        [Tooltip("Always kept, and counted towards the total - a key a puzzle " +
            "is built around, like the one behind a lever gate.")]
        [SerializeField] private List<KeyItem> guaranteed = new List<KeyItem>();
        [SerializeField] private ObjectiveTracker objectives;
        [Tooltip("Used only when no ObjectiveTracker is assigned.")]
        [SerializeField] private int fallbackKeyCount = 3;

        [Tooltip("Online, how long to wait for the shared seed before giving up " +
            "and hiding the keys locally.")]
        [SerializeField] private float seedTimeout = 6f;

        private bool pruned;
        private float waitingSince;

        private void Awake()
        {
            // Indexed before anything is removed, so an id means the same key
            // on both machines however the pruning falls.
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == null) continue;

                candidates[i].CoopId = i;
            }

            // Their own id range, clear of the candidates'.
            for (int i = 0; i < guaranteed.Count; i++)
            {
                if (guaranteed[i] != null) guaranteed[i].CoopId = 1000 + i;
            }

            // Online the seed arrives with the session's run state, a moment
            // after the scene loads. Hide every candidate until then, so nobody
            // can grab a key that is about to be pruned away.
            if (CoopHooks.Online && !CoopHooks.RunSeed.HasValue)
            {
                SetCandidatesActive(false);
                waitingSince = Time.time;
                return;
            }

            PruneNow();
        }

        private void Update()
        {
            if (pruned) return;
            if (!CoopHooks.Online || CoopHooks.RunSeed.HasValue)
            {
                PruneNow();
                return;
            }

            // No run state in this scene means no seed is ever coming. Better
            // keys in different places on each machine than no keys at all.
            if (Time.time - waitingSince > seedTimeout)
            {
                Debug.LogWarning("[FearMe] No shared seed arrived - is there a NetworkRunState in this scene? " +
                    "Run Tools/FearMe/Co-op/Set Up Co-op. Hiding keys locally for now.");
                PruneNow();
            }
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

            foreach (KeyItem key in guaranteed)
            {
                if (key != null) key.gameObject.SetActive(active);
            }
        }

        private void Prune(int keep)
        {
            // Guaranteed keys fill their share first; the rest are drawn at random.
            foreach (KeyItem key in guaranteed)
            {
                if (key != null) keep--;
            }

            List<KeyItem> pool = new List<KeyItem>();
            foreach (KeyItem candidate in candidates)
            {
                if (candidate != null && !guaranteed.Contains(candidate)) pool.Add(candidate);
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
