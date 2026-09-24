using System;
using System.Collections.Generic;
using System.Text;
using FearMe.AI;
using FearMe.Net;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    // Runs the hospital's progress: which key is out, where the three rite
    // pages are hiding, which zones are open, when the demon is banished and
    // how angry it comes back, and how many exit bolts are drawn.
    //
    // The authority (host or solo player) makes every decision and records it
    // in a RunSnapshot; every machine, the authority included, builds what it
    // sees from that snapshot. So the network only ever has to carry one
    // small struct, and a guest can never disagree about where a page is.
    public class SpawnDirector : MonoBehaviour
    {
        public static SpawnDirector Instance { get; private set; }

        [Header("Keys - one per zone; each opens the next, the last opens the exit")]
        [SerializeField] private string[] keyNames = { "Wards key", "Morgue key", "Exit key" };
        [Tooltip("The key model. A heavy iron shape is made if left empty.")]
        [SerializeField] private GameObject keyPrefab;
        [SerializeField] private AudioClip[] chainRattles;

        [Header("Rite pages")]
        [Tooltip("The page model. A glowing sheet is made if left empty.")]
        [SerializeField] private GameObject pagePrefab;
        [SerializeField] private AudioClip whisperLoop;

        [Header("Banishment")]
        [Tooltip("One or two pages: a desperate breather.")]
        [SerializeField] private float shortBanish = 10f;
        [Tooltip("All three pages: a real window - but they scatter wider after.")]
        [SerializeField] private float fullBanish = 60f;
        [SerializeField] private AudioClip riteClip;
        [SerializeField] private AudioClip returnClip;

        [Header("Placement")]
        [Tooltip("Nothing spawns closer than this to anything else already out.")]
        [SerializeField] private float minSeparation = 3f;
        [Tooltip("Nothing pops into being closer than this to a player.")]
        [SerializeField] private float minPlayerDistance = 7f;
        [Tooltip("How often to retry anything that could not be placed yet.")]
        [SerializeField] private float retryInterval = 2f;

        [Header("References (found automatically if empty)")]
        [SerializeField] private EnemyStalkerAI demon;
        [SerializeField] private ObjectiveTracker objectives;

        private readonly List<SpawnSpot> spots = new List<SpawnSpot>();
        private readonly RitePage[] pageVisuals = new RitePage[RunSnapshot.PageSlots];
        private ZoneKeyPickup keyVisual;

        private RunSnapshot state = RunSnapshot.Initial;
        private System.Random random;
        private bool started;
        private float banishEndsAt;
        private float banishShownUntil;
        private float nextRetry;

        // Raised on the authority whenever the run changes; the network layer
        // mirrors the snapshot to the guest from here.
        public event Action Changed;

        public RunSnapshot State => state;
        public bool IsAuthority => !CoopHooks.Online || CoopHooks.IsHost;
        public int KeyCount => keyNames.Length;
        public int ZoneCount => HospitalZone.Count;
        public bool AllKeysHeld => state.keysHeld >= KeyCount;
        public float BanishRemaining => state.banished ? Mathf.Max(0f, banishShownUntil - Time.time) : 0f;

        // When the demon last came back, for the HUD's warning.
        public float LastReturnTime { get; private set; } = -100f;

        public string KeyName(int index) =>
            index >= 0 && index < keyNames.Length ? keyNames[index] : "key";

        public float BanishFor(int pages) => pages >= RunSnapshot.PageSlots ? fullBanish : shortBanish;

        private void Awake()
        {
            Instance = this;
            if (demon == null) demon = FindFirstObjectByType<EnemyStalkerAI>();
            if (objectives == null) objectives = FindFirstObjectByType<ObjectiveTracker>();

            // Same scene on both machines, sorted the same way: a spot's index
            // means the same place everywhere.
            spots.AddRange(FindObjectsByType<SpawnSpot>(FindObjectsSortMode.None));
            spots.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!started)
            {
                // Online, wait for the session to say who decides.
                if (CoopHooks.Online && !CoopHooks.RunSeed.HasValue) return;
                Begin();
            }

            if (IsAuthority) TickAuthority();
        }

        private void Begin()
        {
            started = true;
            random = new System.Random(CoopHooks.RunSeed ?? Environment.TickCount);

            if (objectives != null) objectives.SetKeysRequired(KeyCount);
            if (spots.Count == 0)
                Debug.LogWarning("[FearMe] The run director has no spawn spots. Run Tools/FearMe/Hospital/Set Up Hospital Run.");

            // A guest waits for the host's snapshot instead of rolling its own.
            if (!IsAuthority) return;

            RunSnapshot next = state;
            SpawnNextKey(ref next);
            PlaceMissingPages(ref next, wide: false);
            Commit(next);
        }

        // --- Asking ------------------------------------------------------------

        // Every pickup, rite, gate and bolt comes through here. On the
        // authority it is decided now; anywhere else it is sent to the host.
        public void Request(RunRequest request, int argument)
        {
            if (IsAuthority)
            {
                Handle(request, argument);
                return;
            }

            if (CoopHooks.RunRequested != null) CoopHooks.RunRequested((int)request, argument);
        }

        // The host receiving a guest's request.
        public void HandleRemote(int request, int argument)
        {
            if (IsAuthority) Handle((RunRequest)request, argument);
        }

        private void Handle(RunRequest request, int argument)
        {
            RunSnapshot next = state;

            switch (request)
            {
                case RunRequest.TakeKey:
                    if (argument != next.keySpot || next.keySpot < 0) return;
                    TakeKey(ref next);
                    break;

                case RunRequest.TakePage:
                    if (argument < 0 || argument >= RunSnapshot.PageSlots || next.PageSpot(argument) < 0) return;
                    next.SetPageSpot(argument, -1);
                    next.pagesHeld++;
                    break;

                case RunRequest.PerformRite:
                    if (next.pagesHeld <= 0 || next.banished) return;
                    PerformRite(ref next);
                    break;

                case RunRequest.UnlockZone:
                    if (argument != next.zonesUnlocked + 1 || next.keysHeld < argument) return;
                    next.zonesUnlocked = argument;
                    SpawnNextKey(ref next);
                    break;

                case RunRequest.OpenBolt:
                    if (argument != next.boltsOpen || !AllKeysHeldIn(next) || argument >= Deadbolt.Count) return;
                    next.boltsOpen++;
                    Deadbolt.RaiseAlarm(argument);
                    break;
            }

            Commit(next);
        }

        private bool AllKeysHeldIn(RunSnapshot snapshot) => snapshot.keysHeld >= KeyCount;

        private void TakeKey(ref RunSnapshot next)
        {
            int taken = next.keysHeld;
            next.keysHeld++;
            next.keySpot = -1;

            // The zone this key opens has no gate in the level: open it now,
            // or the next key could never be reached - a soft-lock.
            int opens = taken + 1;
            if (opens < ZoneCount && ZoneGate.For(opens) == null)
                next.zonesUnlocked = Mathf.Max(next.zonesUnlocked, opens);

            SpawnNextKey(ref next);
        }

        private void PerformRite(ref RunSnapshot next)
        {
            int used = next.pagesHeld;
            bool full = used >= RunSnapshot.PageSlots;

            next.pagesHeld = 0;
            next.banished = true;
            next.banishSerial++;
            next.banishSeconds = BanishFor(used);
            banishEndsAt = Time.time + next.banishSeconds;

            // Every page, held or still hidden, is gone - and somewhere new.
            for (int slot = 0; slot < RunSnapshot.PageSlots; slot++) next.SetPageSpot(slot, -1);
            PlaceMissingPages(ref next, wide: full);

            // Held far from everyone while gone, so the players' tension eases
            // as well as the danger.
            if (demon != null) demon.Banish(ReturnPoint());
        }

        // --- Authority upkeep -------------------------------------------------

        private void TickAuthority()
        {
            RunSnapshot next = state;
            bool changed = false;

            if (next.banished && Time.time >= banishEndsAt)
            {
                next.banished = false;
                next.demonLevel++;
                if (demon != null) demon.ReturnFromBanishment(next.demonLevel, ReturnPoint());
                changed = true;
            }

            // Anything that found no room last time (players stood on every
            // free spot, say) gets another go.
            if (Time.time >= nextRetry)
            {
                nextRetry = Time.time + retryInterval;

                int before = next.keySpot;
                int pagesBefore = next.PagesOnMap;
                SpawnNextKey(ref next);
                PlaceMissingPages(ref next, wide: false);
                changed |= next.keySpot != before || next.PagesOnMap != pagesBefore;
            }

            if (changed) Commit(next);
        }

        private void SpawnNextKey(ref RunSnapshot next)
        {
            int key = next.keysHeld;
            if (key >= KeyCount || next.keySpot >= 0) return;

            // Key N waits until zone N is open, and turns up there if it can:
            // the newest zone is where the team needs a reason to go.
            int needs = Mathf.Min(key, ZoneCount - 1);
            if (next.zonesUnlocked < needs) return;

            int spot = PickSpot(next, forKey: true, preferZone: next.zonesUnlocked, wide: false);
            if (spot < 0) spot = PickSpot(next, forKey: true, preferZone: -1, wide: false);
            next.keySpot = spot;
        }

        private void PlaceMissingPages(ref RunSnapshot next, bool wide)
        {
            int unplaced = RunSnapshot.PageSlots - next.pagesHeld - next.PagesOnMap;

            for (int slot = 0; slot < RunSnapshot.PageSlots && unplaced > 0; slot++)
            {
                if (next.PageSpot(slot) >= 0) continue;

                int spot = PickSpot(next, forKey: false, preferZone: -1, wide: wide);
                if (spot < 0) return;

                next.SetPageSpot(slot, spot);
                unplaced--;
            }
        }

        // A spot in an open zone, clear of everything already out and of
        // every player. Narrow picks are weighted-random; wide picks go for
        // the spot furthest from players and from each other.
        private int PickSpot(RunSnapshot snapshot, bool forKey, int preferZone, bool wide)
        {
            List<int> candidates = new List<int>();
            List<Vector3> taken = TakenPositions(snapshot);

            for (int i = 0; i < spots.Count; i++)
            {
                SpawnSpot spot = spots[i];
                if (spot == null) continue;
                if (forKey ? !spot.AllowKeys : !spot.AllowPages) continue;

                int zone = spot.Zone;
                if (zone > snapshot.zonesUnlocked) continue;
                if (preferZone >= 0 && zone != preferZone) continue;

                Vector3 at = spot.transform.position;
                if (NearAny(at, taken, minSeparation)) continue;
                if (NearAnyPlayer(at, minPlayerDistance)) continue;

                candidates.Add(i);
            }

            if (candidates.Count == 0) return -1;
            return wide ? Widest(candidates, taken) : Weighted(candidates);
        }

        private List<Vector3> TakenPositions(RunSnapshot snapshot)
        {
            List<Vector3> taken = new List<Vector3>();
            if (snapshot.keySpot >= 0 && snapshot.keySpot < spots.Count) taken.Add(spots[snapshot.keySpot].transform.position);

            for (int slot = 0; slot < RunSnapshot.PageSlots; slot++)
            {
                int spot = snapshot.PageSpot(slot);
                if (spot >= 0 && spot < spots.Count) taken.Add(spots[spot].transform.position);
            }
            return taken;
        }

        private static bool NearAny(Vector3 at, List<Vector3> points, float distance)
        {
            foreach (Vector3 p in points)
                if (Vector3.Distance(at, p) < distance) return true;
            return false;
        }

        private static bool NearAnyPlayer(Vector3 at, float distance)
        {
            foreach (PlayerController player in PlayerRegistry.All)
                if (player != null && Vector3.Distance(at, player.transform.position) < distance) return true;
            return false;
        }

        private int Weighted(List<int> candidates)
        {
            float total = 0f;
            foreach (int i in candidates) total += spots[i].Weight;

            float roll = (float)random.NextDouble() * total;
            foreach (int i in candidates)
            {
                roll -= spots[i].Weight;
                if (roll <= 0f) return i;
            }
            return candidates[candidates.Count - 1];
        }

        // Scored by distance to the nearest player or already-placed thing;
        // one of the best three, so a wide scatter is not predictable either.
        private int Widest(List<int> candidates, List<Vector3> taken)
        {
            List<Vector3> avoid = new List<Vector3>(taken);
            foreach (PlayerController player in PlayerRegistry.All)
                if (player != null) avoid.Add(player.transform.position);

            candidates.Sort((a, b) => Score(b, avoid).CompareTo(Score(a, avoid)));
            return candidates[random.Next(0, Mathf.Min(3, candidates.Count))];
        }

        private float Score(int index, List<Vector3> avoid)
        {
            Vector3 at = spots[index].transform.position;
            float nearest = float.MaxValue;
            foreach (Vector3 p in avoid) nearest = Mathf.Min(nearest, Vector3.Distance(at, p));
            return nearest == float.MaxValue ? 0f : nearest;
        }

        // Somewhere in an open zone as far from everyone as possible, so the
        // return is a threat on its way rather than a jump into someone's lap.
        private Vector3 ReturnPoint()
        {
            Vector3 best = demon != null ? demon.transform.position : transform.position;
            float bestScore = -1f;

            foreach (SpawnSpot spot in spots)
            {
                if (spot == null || spot.Zone > state.zonesUnlocked) continue;

                float nearest = float.MaxValue;
                foreach (PlayerController player in PlayerRegistry.All)
                    if (player != null) nearest = Mathf.Min(nearest, Vector3.Distance(spot.transform.position, player.transform.position));

                if (nearest > bestScore)
                {
                    bestScore = nearest;
                    best = spot.transform.position;
                }
            }
            return best;
        }

        // --- Applying ---------------------------------------------------------

        private void Commit(RunSnapshot next)
        {
            if (next.Equals(state) && started) return;
            Apply(next);
            Changed?.Invoke();
        }

        // A guest receiving the host's snapshot.
        public void ApplyRemote(RunSnapshot next)
        {
            Apply(next);
        }

        private void Apply(RunSnapshot next)
        {
            RunSnapshot previous = state;
            state = next;

            if (objectives != null) objectives.SetKeysCollected(next.keysHeld);

            ApplyKey(previous, next);
            for (int slot = 0; slot < RunSnapshot.PageSlots; slot++) ApplyPage(slot, next.PageSpot(slot));

            if (next.banished && next.banishSerial != previous.banishSerial)
            {
                banishShownUntil = Time.time + next.banishSeconds;
                if (demon != null) demon.SetBanishedVisual(true);
                PlayAt(riteClip, PlayerRegistry.Local != null ? PlayerRegistry.Local.transform.position : transform.position);
                if (VolumetricFogController.Instance != null) VolumetricFogController.Instance.Pulse(0.05f);
            }

            if (previous.banished && !next.banished)
            {
                LastReturnTime = Time.time;
                if (demon != null)
                {
                    demon.SetBanishedVisual(false);
                    PlayAt(returnClip, demon.transform.position);
                }
            }

            for (int bolt = previous.boltsOpen; bolt < next.boltsOpen; bolt++) Deadbolt.PlayOpened(bolt);
        }

        private void ApplyKey(RunSnapshot previous, RunSnapshot next)
        {
            bool unchanged = next.keySpot == previous.keySpot && next.keysHeld == previous.keysHeld && keyVisual != null;
            if (unchanged && next.keySpot >= 0) return;

            if (keyVisual != null)
            {
                // Picked up rather than just moved: the chain is heard going.
                if (next.keysHeld > previous.keysHeld) keyVisual.PlayTaken();
                Destroy(keyVisual.gameObject);
                keyVisual = null;
            }

            if (next.keySpot < 0 || next.keySpot >= spots.Count) return;

            keyVisual = ZoneKeyPickup.Create(this, spots[next.keySpot].transform, next.keySpot,
                KeyName(next.keysHeld), keyPrefab, chainRattles);
        }

        private void ApplyPage(int slot, int spot)
        {
            RitePage current = pageVisuals[slot];
            if (current != null && current.Spot == spot) return;

            if (current != null)
            {
                current.Dissolve();
                pageVisuals[slot] = null;
            }

            if (spot < 0 || spot >= spots.Count) return;

            pageVisuals[slot] = RitePage.Create(this, spots[spot].transform, slot, spot, pagePrefab, whisperLoop);
        }

        private static void PlayAt(AudioClip clip, Vector3 position)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, position);
        }

        private static string PathOf(Transform t)
        {
            StringBuilder path = new StringBuilder();
            for (Transform node = t; node != null; node = node.parent)
                path.Insert(0, "/" + node.GetSiblingIndex().ToString("D4"));
            return path.ToString();
        }
    }
}
