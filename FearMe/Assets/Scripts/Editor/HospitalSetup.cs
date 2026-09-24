using System.Collections.Generic;
using FearMe.Core;
using FearMe.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Turns the open scene into the full hospital run: zones, spawn spots,
    // the run director, deadbolts on the exit and the player's rite and
    // hiding controls. Non-destructive and repeatable - anything already
    // there is left alone - and everything it guesses (zone boxes especially)
    // is meant to be adjusted by hand afterwards.
    public static class HospitalSetup
    {
        private const string KeyModelPath = "Assets/Art/Asset pack for horror game/Models/key/key.fbx";
        private static readonly string[] ZoneNames = { "Lobby", "Wards", "Morgue" };

        [MenuItem("Tools/FearMe/Hospital/Set Up Hospital Run (current scene)")]
        public static void SetUp()
        {
            PlayerController player = CoopGameplaySetup.FindLocalPlayer();
            if (player == null)
            {
                Debug.LogError("[FearMe] No player in this scene. Open the gameplay scene first.");
                return;
            }

            List<Vector3> floor = CoopGameplaySetup.ReachableSpots(player.transform.position);
            if (floor.Count < 12)
            {
                Debug.LogError("[FearMe] Hardly any NavMesh reachable from the player. Rebake it, then run this again.");
                return;
            }

            List<string> done = new List<string>();
            Vector3 start = player.transform.position;

            AddDirector(done);
            AddZones(floor, start, done);
            AddSpawnSpots(floor, done);
            AddDeadbolts(start, done);
            AddPlayerControls(player, done);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log("[FearMe] Hospital run: " + (done.Count > 0 ? string.Join("; ", done) : "everything was already in place") +
                ". Next: check the zone boxes cover the right rooms, put a zone gate in each doorway between them " +
                "(Tools/FearMe/Hospital/Create Zone Gate At Scene View), turn beds into hiding spots, rebake the NavMesh, save.");
        }

        // --- Director ----------------------------------------------------------

        private static void AddDirector(List<string> done)
        {
            if (Object.FindFirstObjectByType<SpawnDirector>() != null) return;

            GameObject go = new GameObject("RunDirector");
            Undo.RegisterCreatedObjectUndo(go, "Run Director");
            SpawnDirector director = go.AddComponent<SpawnDirector>();

            GameObject keyModel = AssetDatabase.LoadAssetAtPath<GameObject>(KeyModelPath);
            if (keyModel != null) CoopGameplaySetup.SetObject(director, "keyPrefab", keyModel);

            done.Add("the run director" + (keyModel != null ? ", using the asylum pack's key model" : ""));
        }

        // --- Zones -------------------------------------------------------------

        // A first guess: the reachable floor split into thirds by distance
        // from the start, one box around each. Real rooms rarely line up with
        // that, so the boxes are meant to be dragged into shape.
        private static void AddZones(List<Vector3> floor, Vector3 start, List<string> done)
        {
            if (Object.FindFirstObjectByType<HospitalZone>() != null) return;

            List<Vector3> sorted = new List<Vector3>(floor);
            sorted.Sort((a, b) => Vector3.Distance(a, start).CompareTo(Vector3.Distance(b, start)));

            GameObject root = new GameObject("HospitalZones");
            Undo.RegisterCreatedObjectUndo(root, "Zones");

            int per = Mathf.CeilToInt(sorted.Count / (float)ZoneNames.Length);
            for (int z = 0; z < ZoneNames.Length; z++)
            {
                int from = z * per;
                int to = Mathf.Min(sorted.Count, from + per);
                if (from >= to) break;

                Bounds bounds = new Bounds(sorted[from], Vector3.zero);
                for (int i = from; i < to; i++) bounds.Encapsulate(sorted[i]);
                bounds.Expand(new Vector3(2f, 0f, 2f));

                GameObject go = new GameObject("Zone_" + ZoneNames[z]);
                go.transform.SetParent(root.transform, false);
                go.transform.position = bounds.center + Vector3.up * 2f;

                HospitalZone zone = go.AddComponent<HospitalZone>();
                CoopGameplaySetup.SetInt(zone, "index", z);
                CoopGameplaySetup.SetString(zone, "displayName", ZoneNames[z]);
                CoopGameplaySetup.SetVector(zone, "size", new Vector3(bounds.size.x, bounds.size.y + 6f, bounds.size.z));
            }

            done.Add("3 zone boxes (Lobby, Wards, Morgue) by distance from the start - adjust them");
        }

        // --- Spawn spots ---------------------------------------------------------

        // About one every five metres of floor, plus every spot a key used to
        // sit on - those were chosen by hand, so they are good hiding places.
        private static void AddSpawnSpots(List<Vector3> floor, List<string> done)
        {
            if (Object.FindFirstObjectByType<SpawnSpot>() != null) return;

            GameObject root = new GameObject("SpawnSpots");
            Undo.RegisterCreatedObjectUndo(root, "Spawn Spots");

            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            int count = 0;

            foreach (KeyItem key in Object.FindObjectsByType<KeyItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Vector3 at = key.transform.position;
                if (NavMesh.SamplePosition(at, out NavMeshHit hit, 2f, NavMesh.AllAreas)) at = hit.position;
                MakeSpot(root.transform, at, "KeySpot");
                cells.Add(Cell(at));
                count++;
            }

            foreach (Vector3 at in floor)
            {
                if (!cells.Add(Cell(at))) continue;
                MakeSpot(root.transform, at, "Spot");
                count++;
            }

            done.Add($"{count} spawn spots");
        }

        private static Vector2Int Cell(Vector3 at) =>
            new Vector2Int(Mathf.FloorToInt(at.x / 5f), Mathf.FloorToInt(at.z / 5f) * 1000 + Mathf.FloorToInt(at.y / 3f));

        private static void MakeSpot(Transform parent, Vector3 at, string name)
        {
            GameObject spot = new GameObject(name);
            spot.transform.SetParent(parent, false);
            spot.transform.position = at;
            spot.AddComponent<SpawnSpot>();
        }

        // --- Exit bolts ------------------------------------------------------------

        private static void AddDeadbolts(Vector3 start, List<string> done)
        {
            if (Object.FindFirstObjectByType<Deadbolt>() != null) return;

            ExitDoor exit = Object.FindFirstObjectByType<ExitDoor>();
            Renderer look = exit != null ? exit.GetComponentInChildren<Renderer>() : null;
            if (look == null) return;

            Transform door = exit.transform;
            Bounds bounds = look.bounds;

            // On the face the players walk up to, near the opening edge.
            float side = Vector3.Dot(start - door.position, door.forward) >= 0f ? 1f : -1f;
            float depth = Extent(bounds, door.forward) + 0.05f;
            float across = Extent(bounds, door.right) * 0.6f;
            float bottom = bounds.min.y;
            float height = bounds.size.y;

            // Their own root, unscaled, so a stretched door cube does not
            // stretch the bolts with it.
            GameObject root = new GameObject("ExitDeadbolts");
            Undo.RegisterCreatedObjectUndo(root, "Deadbolts");
            root.transform.SetPositionAndRotation(bounds.center, door.rotation);

            Material iron = CoopGameplaySetup.GetMaterial("BoltIron", new Color(0.24f, 0.23f, 0.21f));
            float[] heights = { 0.72f, 0.5f, 0.28f };

            for (int i = 0; i < heights.Length; i++)
            {
                Vector3 at = new Vector3(bounds.center.x, bottom + height * heights[i], bounds.center.z)
                             + door.right * across + door.forward * (depth * side);

                GameObject bolt = new GameObject("Deadbolt_" + (i + 1));
                bolt.transform.SetParent(root.transform, true);
                bolt.transform.SetPositionAndRotation(at, door.rotation);

                Block(bolt.transform, "Housing", Vector3.zero, new Vector3(0.1f, 0.12f, 0.07f), iron);
                Transform bar = Block(bolt.transform, "Bar", new Vector3(-0.12f, 0f, 0f), new Vector3(0.3f, 0.045f, 0.045f), iron).transform;

                BoxCollider box = bolt.AddComponent<BoxCollider>();
                box.size = new Vector3(0.45f, 0.18f, 0.15f);

                AudioSource audio = bolt.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1f;
                audio.maxDistance = 30f;

                Deadbolt deadbolt = bolt.AddComponent<Deadbolt>();
                CoopGameplaySetup.SetInt(deadbolt, "order", i);
                CoopGameplaySetup.SetObject(deadbolt, "bolt", bar);
                CoopGameplaySetup.SetObject(deadbolt, "audioSource", audio);
                CoopGameplaySetup.SetVector(deadbolt, "slideOffset", new Vector3(0.2f, 0f, 0f));
            }

            done.Add("3 deadbolts on the exit");
        }

        private static float Extent(Bounds bounds, Vector3 axis) =>
            Mathf.Abs(bounds.extents.x * axis.x) + Mathf.Abs(bounds.extents.y * axis.y) + Mathf.Abs(bounds.extents.z * axis.z);

        private static GameObject Block(Transform parent, string name, Vector3 local, Vector3 size, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(block.GetComponent<Collider>());
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = local;
            block.transform.localScale = size;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block;
        }

        // --- Player ------------------------------------------------------------

        private static void AddPlayerControls(PlayerController player, List<string> done)
        {
            GameObject go = player.gameObject;
            bool added = false;

            if (go.GetComponent<RiteCaster>() == null) { go.AddComponent<RiteCaster>(); added = true; }
            if (go.GetComponent<HidingBreath>() == null) { go.AddComponent<HidingBreath>(); added = true; }

            if (added) done.Add("the rite (R) and hold-breath / peek controls on the player");
        }

        // --- Zone gates --------------------------------------------------------------

        [MenuItem("Tools/FearMe/Hospital/Create Zone Gate At Scene View")]
        public static void CreateZoneGate()
        {
            // The next zone without a gate of its own.
            int zone = 1;
            while (ZoneGate.For(zone) != null || GateInScene(zone)) zone++;

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 at = view != null ? view.pivot : Vector3.zero;
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 20f)) at = ground.point;

            string where = zone < ZoneNames.Length ? ZoneNames[zone] : "zone " + (zone + 1);

            GameObject root = new GameObject("ZoneGate_" + where);
            Undo.RegisterCreatedObjectUndo(root, "Zone Gate");
            root.transform.position = at;

            // Hinged at the left edge; the slab is the part that swings.
            Transform hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(-0.6f, 0f, 0f);

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Door";
            slab.transform.SetParent(hinge, false);
            slab.transform.localPosition = new Vector3(0.6f, 1.1f, 0f);
            slab.transform.localScale = new Vector3(1.2f, 2.2f, 0.1f);
            slab.GetComponent<Renderer>().sharedMaterial = CoopGameplaySetup.GetMaterial("ZoneGate", new Color(0.18f, 0.16f, 0.14f));

            // A wall to the demon while locked; not baked in, since it opens.
            NavMeshObstacle obstacle = slab.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            CoopGameplaySetup.IgnoreInBake(hinge.gameObject);

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;

            ZoneGate gate = root.AddComponent<ZoneGate>();
            CoopGameplaySetup.SetInt(gate, "zone", zone);
            CoopGameplaySetup.SetObject(gate, "door", hinge);
            CoopGameplaySetup.SetObject(gate, "blocker", slab.GetComponent<Collider>());
            CoopGameplaySetup.SetObject(gate, "obstacle", obstacle);
            CoopGameplaySetup.SetObject(gate, "audioSource", audio);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[FearMe] Zone gate into the {where} created. Fit it into the doorway between zones " +
                $"{zone - 1} and {zone}, then rebake the NavMesh.");
        }

        // ZoneGate.For only knows gates that have been enabled at runtime;
        // in the editor, look through the scene instead.
        private static bool GateInScene(int zone)
        {
            foreach (ZoneGate gate in Object.FindObjectsByType<ZoneGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (gate.Zone == zone) return true;
            return false;
        }

        // --- Beds ------------------------------------------------------------------

        [MenuItem("Tools/FearMe/Hospital/Make Selected Beds Into Hiding Spots")]
        public static void MakeBedHidingSpots()
        {
            int made = 0;
            foreach (GameObject bed in Selection.gameObjects)
            {
                if (bed.GetComponent<HidingSpot>() != null) continue;

                Renderer look = bed.GetComponentInChildren<Renderer>();
                if (look == null) continue;

                if (bed.GetComponent<Collider>() == null) bed.AddComponent<BoxCollider>();
                Bounds bounds = look.bounds;

                // Look out from under the long side, lying on the floor.
                Vector3 across = Extent(bounds, bed.transform.right) < Extent(bounds, bed.transform.forward)
                    ? bed.transform.right
                    : bed.transform.forward;
                across.y = 0f;
                across.Normalize();

                Transform inside = new GameObject("UnderBed").transform;
                inside.SetParent(bed.transform, true);
                inside.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
                    Quaternion.LookRotation(across));

                Transform exit = new GameObject("CrawlOut").transform;
                exit.SetParent(bed.transform, true);
                exit.position = inside.position + across * (Extent(bounds, across) + 0.7f);

                HidingSpot spot = Undo.AddComponent<HidingSpot>(bed);
                CoopGameplaySetup.SetObject(spot, "insideAnchor", inside);
                CoopGameplaySetup.SetObject(spot, "exitAnchor", exit);
                CoopGameplaySetup.SetFloat(spot, "yawLimit", 60f);
                CoopGameplaySetup.SetFloat(spot, "pitchLimit", 10f);
                CoopGameplaySetup.SetFloat(spot, "eyeHeightOffset", -1.35f);
                CoopGameplaySetup.SetString(spot, "hidePrompt", "Hide under the bed");
                made++;
            }

            if (made > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[FearMe] {made} bed(s) made into hiding spots. Space holds breath, Q peeks, E crawls out.");
        }
    }
}
