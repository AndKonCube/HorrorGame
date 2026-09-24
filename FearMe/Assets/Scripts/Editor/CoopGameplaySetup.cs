using System.Collections.Generic;
using FearMe.Core;
using FearMe.Items;
using FearMe.Player;
using FearMe.Scares;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Drops the co-op gameplay into the open scene without rebuilding it:
    // the player's new components, the mimic, cages, stun items and the heavy
    // battery, each placed on reachable floor and each skipped if it is
    // already there. Everything it makes is plain primitives, meant to be
    // moved, reskinned or deleted by hand.
    public static class CoopGameplaySetup
    {
        private const string MaterialFolder = "Assets/Materials/Generated";
        private const string DoorModelPath = "Assets/Art/Asset pack for horror game/Models/Door/Door.fbx";

        [MenuItem("Tools/FearMe/Gameplay/Add Co-op Gameplay (current scene)")]
        public static void AddToCurrentScene()
        {
            PlayerController player = FindLocalPlayer();
            if (player == null)
            {
                Debug.LogError("[FearMe] No player in this scene. Open the gameplay scene first.");
                return;
            }

            List<Vector3> spots = ReachableSpots(player.transform.position);
            if (spots.Count < 8)
            {
                Debug.LogError("[FearMe] Hardly any NavMesh reachable from the player - bake it first " +
                    "(Tools/FearMe/Rebake NavMesh), then run this again.");
                return;
            }

            List<string> done = new List<string>();

            AddPlayerComponents(player, done);
            AddMimic(done);

            Vector3 start = player.transform.position;
            ExitDoor exit = Object.FindFirstObjectByType<ExitDoor>();

            if (Object.FindFirstObjectByType<CageSpot>() == null)
            {
                foreach (Vector3 at in PickFarthest(spots, start, 2, 12f))
                    BuildCage(at, start);
                done.Add("2 cages, far from the start");
            }

            if (Object.FindFirstObjectByType<StunItem>() == null)
            {
                // One brick near the start, so a rescue is always possible.
                BuildBrick(Nearest(spots, start, 4f));
                foreach (Vector3 at in PickFarthest(spots, start, 2, 10f))
                    BuildBrick(at);
                BuildStunGun(PickFarthest(spots, exit != null ? exit.transform.position : start, 1, 0f)[0]);
                done.Add("3 bricks and a stun gun");
            }

            if (Object.FindFirstObjectByType<HeavyItem>() == null && exit != null)
            {
                Vector3 far = PickFarthest(spots, exit.transform.position, 1, 0f)[0];
                HeavyItem battery = BuildBattery(far);
                RequireAtExit(exit, battery);
                done.Add("a car battery the exit now needs, as far from it as the floor allows");
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log("[FearMe] Co-op gameplay added: " + (done.Count > 0 ? string.Join("; ", done) : "nothing new - it was all there") +
                ". Rebake the NavMesh so the cages count as solid, then save the scene. " +
                "For the two-player gate, use Tools/FearMe/Gameplay/Create Lever Gate At Scene View.");
        }

        // --- Player -----------------------------------------------------------------

        private static PlayerController FindLocalPlayer()
        {
            foreach (PlayerController candidate in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (candidate.IsLocalPlayer) return candidate;
            return null;
        }

        private static void AddPlayerComponents(PlayerController player, List<string> done)
        {
            GameObject go = player.gameObject;
            int before = go.GetComponents<Component>().Length;

            if (go.GetComponent<PlayerHands>() == null) go.AddComponent<PlayerHands>();
            if (go.GetComponent<MicrophoneNoise>() == null) go.AddComponent<MicrophoneNoise>();
            if (go.GetComponent<PlayerSanity>() == null) go.AddComponent<PlayerSanity>();

            HallucinationDirector hallucinations = go.GetComponent<HallucinationDirector>();
            if (hallucinations == null)
            {
                hallucinations = go.AddComponent<HallucinationDirector>();

                // The asylum pack's door looks right in a wall that has none.
                GameObject door = AssetDatabase.LoadAssetAtPath<GameObject>(DoorModelPath);
                if (door != null) SetObject(hallucinations, "fakeDoorPrefab", door);
            }

            if (go.GetComponents<Component>().Length > before)
                done.Add("hands, voice, sanity and hallucinations on the player");
        }

        private static void AddMimic(List<string> done)
        {
            ScareDirector director = Object.FindFirstObjectByType<ScareDirector>();
            if (director == null || Object.FindFirstObjectByType<MimicScare>() != null) return;

            GameObject go = new GameObject("MimicScare");
            go.transform.SetParent(director.transform, false);
            MimicScare mimic = go.AddComponent<MimicScare>();

            // Rare: once it has happened, the doubt does the rest.
            SetFloat(mimic, "weight", 0.6f);
            SetFloat(mimic, "cooldown", 150f);

            SerializedObject so = new SerializedObject(director);
            SerializedProperty list = so.FindProperty("scares");
            if (list != null)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = mimic;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            done.Add("the mimic, added to the scare director");
        }

        // --- Cages ------------------------------------------------------------------

        private static void BuildCage(Vector3 at, Vector3 facing)
        {
            Material iron = GetMaterial("CageIron", new Color(0.16f, 0.15f, 0.14f));

            GameObject root = new GameObject("Cage");
            Vector3 toward = facing - at;
            toward.y = 0f;
            root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toward.sqrMagnitude > 0.01f ? toward : Vector3.forward));
            Undo.RegisterCreatedObjectUndo(root, "Cage");

            const float half = 0.65f;
            const float height = 2.1f;

            // Back and sides: bars every 0.2m.
            for (float x = -half; x <= half + 0.001f; x += 0.2f)
                Bar(root.transform, new Vector3(x, height * 0.5f, -half), new Vector3(0.04f, height, 0.04f), iron);
            for (float z = -half + 0.2f; z <= half + 0.001f; z += 0.2f)
            {
                Bar(root.transform, new Vector3(-half, height * 0.5f, z), new Vector3(0.04f, height, 0.04f), iron);
                Bar(root.transform, new Vector3(half, height * 0.5f, z), new Vector3(0.04f, height, 0.04f), iron);
            }
            Bar(root.transform, new Vector3(0f, height, 0f), new Vector3(half * 2f, 0.05f, half * 2f), iron);

            // The front is a door, hinged on the left post.
            Transform door = new GameObject("Door").transform;
            door.SetParent(root.transform, false);
            door.localPosition = new Vector3(-half, 0f, half);
            for (float x = 0.2f; x <= half * 2f + 0.001f; x += 0.2f)
                Bar(door, new Vector3(x, height * 0.5f, 0f), new Vector3(0.04f, height, 0.04f), iron);

            Transform hold = new GameObject("HoldPoint").transform;
            hold.SetParent(root.transform, false);

            Transform dropOff = new GameObject("DropOff").transform;
            dropOff.SetParent(root.transform, false);
            dropOff.localPosition = new Vector3(0f, 0f, half + 0.9f);

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.maxDistance = 25f;

            CageSpot cage = root.AddComponent<CageSpot>();
            SetObject(cage, "holdPoint", hold);
            SetObject(cage, "dropOffPoint", dropOff);
            SetObject(cage, "door", door);
            SetObject(cage, "audioSource", audio);
        }

        private static void Bar(Transform parent, Vector3 position, Vector3 size, Material material)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = position;
            bar.transform.localScale = size;
            bar.GetComponent<Renderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(bar, StaticEditorFlags.NavigationStatic);
        }

        // --- Items --------------------------------------------------------------

        private static void BuildBrick(Vector3 at)
        {
            GameObject brick = Block("Brick", at, new Vector3(0.22f, 0.07f, 0.1f), GetMaterial("Brick", new Color(0.45f, 0.2f, 0.14f)));
            StunItem item = brick.AddComponent<StunItem>();
            SetString(item, "displayName", "brick");
            SetBool(item, "thrown", true);
            SetFloat(item, "range", 10f);
            SetFloat(item, "stunSeconds", 5f);
            SetInt(item, "uses", 1);
            SetVector(item, "heldOffset", Vector3.zero);
        }

        private static void BuildStunGun(Vector3 at)
        {
            GameObject gun = Block("StunGun", at, new Vector3(0.07f, 0.12f, 0.2f), GetMaterial("StunGun", new Color(0.75f, 0.62f, 0.1f)));
            StunItem item = gun.AddComponent<StunItem>();
            SetString(item, "displayName", "stun gun");
            SetBool(item, "thrown", false);
            SetFloat(item, "range", 3f);
            SetFloat(item, "stunSeconds", 8f);
            SetInt(item, "uses", 2);
            SetFloat(item, "hitNoiseRadius", 8f);
        }

        private static HeavyItem BuildBattery(Vector3 at)
        {
            GameObject battery = Block("CarBattery", at, new Vector3(0.35f, 0.25f, 0.2f), GetMaterial("Battery", new Color(0.1f, 0.1f, 0.11f)));
            HeavyItem item = battery.AddComponent<HeavyItem>();
            SetString(item, "displayName", "car battery");
            SetFloat(item, "dropNoiseRadius", 9f);
            SetVector(item, "heldOffset", new Vector3(-0.28f, -0.15f, 0.1f));
            return item;
        }

        private static void RequireAtExit(ExitDoor exit, HeavyItem item)
        {
            Transform slot = new GameObject("BatterySlot").transform;
            slot.SetParent(exit.transform, false);
            slot.localPosition = new Vector3(0.9f, 0.05f, 0.6f);

            SetObject(exit, "requiredItem", item);
            SetObject(exit, "deliverySlot", slot);
        }

        private static GameObject Block(string name, Vector3 at, Vector3 size, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.position = at + Vector3.up * (size.y * 0.5f + 0.02f);
            block.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            block.transform.localScale = size;
            block.GetComponent<Renderer>().sharedMaterial = material;
            Undo.RegisterCreatedObjectUndo(block, name);
            return block;
        }

        // --- Lever and gate -----------------------------------------------------

        [MenuItem("Tools/FearMe/Gameplay/Create Lever Gate At Scene View")]
        public static void CreateLeverGate()
        {
            SceneView view = SceneView.lastActiveSceneView;
            Vector3 at = view != null ? view.pivot : Vector3.zero;
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 20f)) at = ground.point;

            Material iron = GetMaterial("GateIron", new Color(0.2f, 0.19f, 0.18f));

            GameObject root = new GameObject("LeverGatePuzzle");
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "Lever Gate");

            // The gate: a portcullis that slides up.
            GameObject gate = new GameObject("Gate");
            gate.transform.SetParent(root.transform, false);
            Transform bars = new GameObject("Bars").transform;
            bars.SetParent(gate.transform, false);
            for (float x = -1.2f; x <= 1.2f + 0.001f; x += 0.2f)
                Bar(bars, new Vector3(x, 1.3f, 0f), new Vector3(0.05f, 2.6f, 0.05f), iron);
            for (float y = 0.4f; y <= 2.4f; y += 1f)
                Bar(bars, new Vector3(0f, y, 0f), new Vector3(2.45f, 0.05f, 0.05f), iron);

            BoxCollider block = bars.gameObject.AddComponent<BoxCollider>();
            block.center = new Vector3(0f, 1.3f, 0f);
            block.size = new Vector3(2.5f, 2.6f, 0.12f);

            // So the stalker paths round a shut gate and through an open one.
            NavMeshObstacle obstacle = bars.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = block.center;
            obstacle.size = block.size;
            obstacle.carving = true;

            // Bars that move must not be baked in as a permanent wall - the
            // carving obstacle above is what blocks the way while it is shut.
            IgnoreInBake(bars.gameObject);

            AudioSource gateAudio = gate.AddComponent<AudioSource>();
            gateAudio.playOnAwake = false;
            gateAudio.spatialBlend = 1f;

            // The lever, off to one side - put it in another room.
            GameObject lever = new GameObject("Lever");
            lever.transform.SetParent(root.transform, false);
            lever.transform.localPosition = new Vector3(5f, 0f, 0f);

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Base";
            plate.transform.SetParent(lever.transform, false);
            plate.transform.localPosition = new Vector3(0f, 1f, 0f);
            plate.transform.localScale = new Vector3(0.3f, 0.5f, 0.15f);
            plate.GetComponent<Renderer>().sharedMaterial = iron;

            Transform handle = new GameObject("Handle").transform;
            handle.SetParent(lever.transform, false);
            handle.localPosition = new Vector3(0f, 1.05f, 0.1f);
            GameObject stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stick.name = "Stick";
            stick.transform.SetParent(handle, false);
            stick.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            stick.transform.localScale = new Vector3(0.05f, 0.6f, 0.05f);
            stick.GetComponent<Renderer>().sharedMaterial = GetMaterial("LeverRed", new Color(0.5f, 0.08f, 0.06f));

            AudioSource leverAudio = lever.AddComponent<AudioSource>();
            leverAudio.playOnAwake = false;
            leverAudio.spatialBlend = 1f;
            leverAudio.maxDistance = 25f;

            HeavyLever heavyLever = lever.AddComponent<HeavyLever>();
            SetObject(heavyLever, "handle", handle);
            SetObject(heavyLever, "audioSource", leverAudio);

            LeverGate leverGate = gate.AddComponent<LeverGate>();
            SetObject(leverGate, "lever", heavyLever);
            SetObject(leverGate, "gateBody", bars);
            SetObject(leverGate, "audioSource", gateAudio);

            // And the reason to go through: a key that is always part of the run.
            KeyItem key = BuildGuaranteedKey(root.transform, new Vector3(0f, 0.9f, -3f));

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[FearMe] Lever gate created at the scene view. Move 'Gate' into a doorway, 'Lever' into a " +
                "different room, and the key behind the gate" + (key != null ? "" : " (no KeySpawner found - add one by hand)") +
                ". Then rebake the NavMesh.");
        }

        private static KeyItem BuildGuaranteedKey(Transform parent, Vector3 localPosition)
        {
            KeySpawner spawner = Object.FindFirstObjectByType<KeySpawner>();
            KeyItem existing = Object.FindFirstObjectByType<KeyItem>();

            GameObject go;
            if (existing != null)
            {
                go = Object.Instantiate(existing.gameObject);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = new Vector3(0.12f, 0.25f, 0.04f);
                go.AddComponent<KeyItem>();
            }

            go.name = "GatedKey";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            KeyItem key = go.GetComponent<KeyItem>();
            if (spawner == null) return null;

            SerializedObject so = new SerializedObject(spawner);
            SerializedProperty list = so.FindProperty("guaranteed");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = key;
            so.ApplyModifiedPropertiesWithoutUndo();
            return key;
        }

        // --- Swing doors -------------------------------------------------------

        [MenuItem("Tools/FearMe/Gameplay/Make Selected Into Swing Doors")]
        public static void MakeSwingDoors()
        {
            int made = 0;
            foreach (GameObject door in Selection.gameObjects)
            {
                if (door.GetComponentInParent<SwingDoor>() != null) continue;

                Renderer renderer = door.GetComponentInChildren<Renderer>();
                if (renderer == null) continue;

                if (door.GetComponentInChildren<Collider>() == null) door.AddComponent<BoxCollider>();

                // Doors swing from an edge. If the pivot is in the middle - as
                // most models have it - put a hinge at the edge and hang the
                // door off that.
                Transform hinge = door.transform;
                Bounds bounds = renderer.bounds;
                Vector3 right = door.transform.right;
                float offCentre = Vector3.Dot(door.transform.position - bounds.center, right);
                float halfWidth = Mathf.Abs(Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z))));

                if (Mathf.Abs(offCentre) < halfWidth * 0.5f)
                {
                    GameObject pivot = new GameObject(door.name + "_Hinge");
                    Undo.RegisterCreatedObjectUndo(pivot, "Hinge");
                    pivot.transform.SetParent(door.transform.parent, false);
                    pivot.transform.SetPositionAndRotation(
                        new Vector3(bounds.center.x, door.transform.position.y, bounds.center.z) - right * halfWidth,
                        door.transform.rotation);
                    Undo.SetTransformParent(door.transform, pivot.transform, "Hinge");
                    hinge = pivot.transform;
                }

                AudioSource audio = hinge.gameObject.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1f;
                audio.maxDistance = 25f;

                SwingDoor swing = hinge.gameObject.AddComponent<SwingDoor>();
                SetObject(swing, "hinge", hinge);
                SetObject(swing, "audioSource", audio);

                // A door that moves should not be baked into the NavMesh as a wall.
                IgnoreInBake(hinge.gameObject);

                made++;
            }

            if (made > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[FearMe] {made} swing door(s) made. Tap E slams (loud), hold E eases. " +
                "Assign slam and creak clips on each when you have them.");
        }

        private static void IgnoreInBake(GameObject go)
        {
            NavMeshModifier modifier = go.GetComponent<NavMeshModifier>();
            if (modifier == null) modifier = go.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = true;

            foreach (Transform t in go.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }

        // --- Spots on the floor -------------------------------------------------

        // Centres of NavMesh triangles on the player's storey that the player
        // can actually walk to, thinned to about one every 2m.
        private static List<Vector3> ReachableSpots(Vector3 from)
        {
            NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
            List<Vector3> spots = new List<Vector3>();
            HashSet<Vector3Int> taken = new HashSet<Vector3Int>();
            NavMeshPath path = new NavMeshPath();

            if (!NavMesh.SamplePosition(from, out NavMeshHit origin, 3f, NavMesh.AllAreas)) return spots;

            for (int i = 0; i + 2 < mesh.indices.Length; i += 3)
            {
                Vector3 centre = (mesh.vertices[mesh.indices[i]] + mesh.vertices[mesh.indices[i + 1]] +
                                  mesh.vertices[mesh.indices[i + 2]]) / 3f;

                if (Mathf.Abs(centre.y - origin.position.y) > 2f) continue;

                Vector3Int cell = new Vector3Int(Mathf.FloorToInt(centre.x / 2f), 0, Mathf.FloorToInt(centre.z / 2f));
                if (!taken.Add(cell)) continue;

                if (!NavMesh.CalculatePath(origin.position, centre, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    continue;

                spots.Add(centre);
            }

            return spots;
        }

        // Greedy farthest-point: each pick as far as possible from the start
        // and from every earlier pick.
        private static List<Vector3> PickFarthest(List<Vector3> spots, Vector3 from, int count, float minSeparation)
        {
            List<Vector3> picked = new List<Vector3>();
            for (int n = 0; n < count; n++)
            {
                Vector3 best = spots[0];
                float bestScore = -1f;

                foreach (Vector3 spot in spots)
                {
                    float score = Vector3.Distance(spot, from);
                    bool tooClose = false;
                    foreach (Vector3 p in picked)
                    {
                        float d = Vector3.Distance(spot, p);
                        if (d < minSeparation) tooClose = true;
                        score = Mathf.Min(score, d);
                    }
                    if (tooClose || score <= bestScore) continue;

                    bestScore = score;
                    best = spot;
                }

                picked.Add(best);
            }
            return picked;
        }

        private static Vector3 Nearest(List<Vector3> spots, Vector3 from, float atLeast)
        {
            Vector3 best = spots[0];
            float bestDistance = float.MaxValue;
            foreach (Vector3 spot in spots)
            {
                float d = Vector3.Distance(spot, from);
                if (d < atLeast || d >= bestDistance) continue;
                bestDistance = d;
                best = spot;
            }
            return best;
        }

        // --- Assets and fields --------------------------------------------------

        private static Material GetMaterial(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Generated");

            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetObject(Object target, string field, Object value) =>
            Edit(target, field, p => p.objectReferenceValue = value);

        private static void SetFloat(Object target, string field, float value) =>
            Edit(target, field, p => p.floatValue = value);

        private static void SetInt(Object target, string field, int value) =>
            Edit(target, field, p => p.intValue = value);

        private static void SetBool(Object target, string field, bool value) =>
            Edit(target, field, p => p.boolValue = value);

        private static void SetString(Object target, string field, string value) =>
            Edit(target, field, p => p.stringValue = value);

        private static void SetVector(Object target, string field, Vector3 value) =>
            Edit(target, field, p => p.vector3Value = value);

        private static void Edit(Object target, string field, System.Action<SerializedProperty> set)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[FearMe] Missing field '{field}' on {target.GetType().Name}");
                return;
            }
            set(prop);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
