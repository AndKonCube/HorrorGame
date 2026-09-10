using System.Collections.Generic;
using FearMe.AI;
using FearMe.Core;
using FearMe.Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FearMe.EditorTools
{
    // Generates the whole playable demo scene: level blockout, player rig,
    // stalker enemy, baked NavMesh, patrol route, hiding spots and objectives.
    public static class DemoSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string MaterialFolder = "Assets/Materials";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string LevelLayerName = "Level";

        private const float WallHeight = 4f;
        private const float WallY = 2f;

        [MenuItem("Tools/FearMe/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            int levelLayer = EnsureLayer(LevelLayerName);
            int levelMask = 1 << levelLayer;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material floorMat = GetOrCreateMaterial("Demo_Floor", new Color(0.16f, 0.15f, 0.14f));
            Material wallMat = GetOrCreateMaterial("Demo_Wall", new Color(0.22f, 0.20f, 0.19f));
            Material enemyMat = GetOrCreateMaterial("Demo_Enemy", new Color(0.35f, 0.05f, 0.05f));
            Material keyMat = GetOrCreateMaterial("Demo_Key", new Color(0.85f, 0.7f, 0.2f), emissive: true);
            Material doorMat = GetOrCreateMaterial("Demo_Door", new Color(0.30f, 0.18f, 0.08f));

            BuildAtmosphere();
            Transform level = BuildLevel(levelLayer, floorMat, wallMat, doorMat);

            GameObject player = BuildPlayer();
            PatrolRoute route = BuildPatrolRoute();
            EnemyStalkerAI enemy = BuildEnemy(enemyMat, player.GetComponent<PlayerController>(), route, levelMask);

            BuildHidingSpot(level, new Vector3(-16f, 0f, 10f), 0f, levelLayer, wallMat);
            BuildHidingSpot(level, new Vector3(12f, 0f, -12f), 90f, levelLayer, wallMat);

            BuildKey(new Vector3(-16f, 1f, 16f), keyMat);
            BuildKey(new Vector3(16f, 1f, 16f), keyMat);
            BuildKey(new Vector3(16f, 1f, -16f), keyMat);

            GameObject managers = BuildManagers(player, enemy);
            WireExitDoor(managers.GetComponent<GameOverController>());

            BakeNavMesh(levelMask);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterSceneInBuildSettings();

            AssetDatabase.SaveAssets();
            Debug.Log("[FearMe] Demo scene built at " + ScenePath + ". Press Play to run it.");
        }

        private static void BuildAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.055f;
            RenderSettings.fogColor = new Color(0.03f, 0.03f, 0.04f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.skybox = null;

            GameObject moonGO = new GameObject("Moonlight");
            Light moon = moonGO.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.intensity = 0.12f;
            moon.color = new Color(0.6f, 0.7f, 1f);
            moon.shadows = LightShadows.Soft;
            moonGO.transform.rotation = Quaternion.Euler(140f, 30f, 0f);
        }

        private static Transform BuildLevel(int levelLayer, Material floorMat, Material wallMat, Material doorMat)
        {
            GameObject root = new GameObject("Level");
            Transform t = root.transform;

            CreateBox(t, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), levelLayer, floorMat);
            // Default layer: keeps the ceiling out of the NavMesh bake and the vision mask.
            CreateBox(t, "Ceiling", new Vector3(0f, WallHeight, 0f), new Vector3(40f, 0.5f, 40f), 0, floorMat);

            // Outer shell, with a gap in the south wall for the exit door.
            CreateBox(t, "Wall_N", new Vector3(0f, WallY, 20f), new Vector3(40.5f, WallHeight, 0.5f), levelLayer, wallMat);
            CreateBox(t, "Wall_E", new Vector3(20f, WallY, 0f), new Vector3(0.5f, WallHeight, 40.5f), levelLayer, wallMat);
            CreateBox(t, "Wall_W", new Vector3(-20f, WallY, 0f), new Vector3(0.5f, WallHeight, 40.5f), levelLayer, wallMat);
            CreateBox(t, "Wall_S_Left", new Vector3(-11f, WallY, -20f), new Vector3(18f, WallHeight, 0.5f), levelLayer, wallMat);
            CreateBox(t, "Wall_S_Right", new Vector3(11f, WallY, -20f), new Vector3(18f, WallHeight, 0.5f), levelLayer, wallMat);

            // Interior partitions creating a serpentine route with sight breaks.
            CreateBox(t, "Wall_1", new Vector3(-8f, WallY, 6f), new Vector3(24f, WallHeight, 0.5f), levelLayer, wallMat);
            CreateBox(t, "Wall_2", new Vector3(8f, WallY, -6f), new Vector3(24f, WallHeight, 0.5f), levelLayer, wallMat);
            CreateBox(t, "Wall_3", new Vector3(6f, WallY, 12f), new Vector3(0.5f, WallHeight, 16f), levelLayer, wallMat);
            CreateBox(t, "Wall_4", new Vector3(-8f, WallY, -13f), new Vector3(0.5f, WallHeight, 14f), levelLayer, wallMat);

            GameObject door = CreateBox(t, "ExitDoor", new Vector3(0f, WallY, -20f), new Vector3(4f, WallHeight, 0.4f), levelLayer, doorMat);
            door.AddComponent<ExitDoor>();

            return t;
        }

        private static GameObject BuildPlayer()
        {
            GameObject player = new GameObject("Player");
            player.transform.position = new Vector3(-16f, 0.1f, -16f);
            player.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            PlayerInput input = player.AddComponent<PlayerInput>();
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions != null)
            {
                input.actions = actions;
                input.defaultActionMap = "Player";
            }
            else
            {
                Debug.LogWarning("[FearMe] Could not find " + InputActionsPath + " - assign PlayerInput actions manually.");
            }

            GameObject camGO = new GameObject("PlayerCamera");
            camGO.transform.SetParent(player.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";

            GameObject lightGO = new GameObject("FlashlightBeam");
            lightGO.transform.SetParent(camGO.transform, false);
            Light beam = lightGO.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 22f;
            beam.spotAngle = 55f;
            beam.intensity = 4.5f;
            beam.color = new Color(1f, 0.96f, 0.85f);
            beam.shadows = LightShadows.Soft;

            PlayerController controller = player.AddComponent<PlayerController>();
            SetObjectField(controller, "playerCamera", cam);

            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            SetObjectField(interactor, "playerCamera", cam);

            Flashlight flashlight = player.AddComponent<Flashlight>();
            SetObjectField(flashlight, "lightSource", beam);

            return player;
        }

        private static PatrolRoute BuildPatrolRoute()
        {
            GameObject routeGO = new GameObject("PatrolRoute");
            PatrolRoute route = routeGO.AddComponent<PatrolRoute>();

            Vector3[] points =
            {
                new Vector3(-15f, 0f, 15f),
                new Vector3(0f, 0f, 15f),
                new Vector3(15f, 0f, 15f),
                new Vector3(15f, 0f, 0f),
                new Vector3(15f, 0f, -15f),
                new Vector3(0f, 0f, -15f),
                new Vector3(-15f, 0f, -15f),
                new Vector3(-15f, 0f, 0f)
            };

            Transform[] waypoints = new Transform[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                GameObject wp = new GameObject("Waypoint_" + (i + 1));
                wp.transform.SetParent(routeGO.transform, false);
                wp.transform.position = points[i];
                waypoints[i] = wp.transform;
            }

            SetObjectArrayField(route, "waypoints", waypoints);
            return route;
        }

        private static EnemyStalkerAI BuildEnemy(Material mat, PlayerController player, PatrolRoute route, int levelMask)
        {
            GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemy.name = "Stalker";
            enemy.transform.position = new Vector3(14f, 1f, 14f);
            enemy.GetComponent<Renderer>().sharedMaterial = mat;

            NavMeshAgent agent = enemy.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 2f;
            agent.stoppingDistance = 0.5f;
            agent.angularSpeed = 240f;
            agent.acceleration = 12f;
            // Capsule pivot sits at its centre, so lift it clear of the NavMesh.
            agent.baseOffset = 1f;

            GameObject eyes = new GameObject("Eyes");
            eyes.transform.SetParent(enemy.transform, false);
            eyes.transform.localPosition = new Vector3(0f, 0.7f, 0f);

            EnemyStalkerAI ai = enemy.AddComponent<EnemyStalkerAI>();
            SetObjectField(ai, "player", player);
            SetObjectField(ai, "patrolRoute", route);
            SetObjectField(ai, "eyes", eyes.transform);
            SetIntField(ai, "obstructionMask", levelMask);
            // Agent pivot rides 1m above the player's, so allow for that vertical gap.
            SetFloatField(ai, "catchDistance", 2f);

            return ai;
        }

        private static void BuildHidingSpot(Transform levelRoot, Vector3 position, float yaw, int levelLayer, Material mat)
        {
            GameObject spot = new GameObject("HidingSpot");
            spot.transform.SetParent(levelRoot, false);
            spot.transform.position = position;
            spot.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Three solid sides so it genuinely blocks the stalker's line of sight.
            CreateBox(spot.transform, "Back", new Vector3(0f, 1.1f, -0.9f), new Vector3(2f, 2.2f, 0.2f), levelLayer, mat);
            CreateBox(spot.transform, "Left", new Vector3(-0.9f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 2f), levelLayer, mat);
            CreateBox(spot.transform, "Right", new Vector3(0.9f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 2f), levelLayer, mat);

            GameObject trigger = new GameObject("HideVolume");
            trigger.transform.SetParent(spot.transform, false);
            trigger.transform.localPosition = new Vector3(0f, 1f, 0f);
            BoxCollider box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.4f, 2f, 1.4f);
            trigger.AddComponent<HidingSpot>();
        }

        private static void BuildKey(Vector3 position, Material mat)
        {
            GameObject key = GameObject.CreatePrimitive(PrimitiveType.Cube);
            key.name = "KeyItem";
            key.transform.position = position;
            key.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
            key.GetComponent<Renderer>().sharedMaterial = mat;
            key.AddComponent<KeyItem>();

            GameObject glow = new GameObject("KeyGlow");
            glow.transform.SetParent(key.transform, false);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.85f, 0.4f);
        }

        private static GameObject BuildManagers(GameObject player, EnemyStalkerAI enemy)
        {
            GameObject managers = new GameObject("GameManager");

            ObjectiveTracker objectives = managers.AddComponent<ObjectiveTracker>();
            SetIntField(objectives, "keysRequired", 3);

            GameObject canvasGO = new GameObject("FadeCanvas");
            canvasGO.transform.SetParent(managers.transform, false);
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>();
            CanvasGroup group = canvasGO.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            GameObject fadeGO = new GameObject("FadeImage");
            fadeGO.transform.SetParent(canvasGO.transform, false);
            Image image = fadeGO.AddComponent<Image>();
            image.color = Color.black;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            AudioSource stinger = managers.AddComponent<AudioSource>();
            stinger.playOnAwake = false;

            GameOverController flow = managers.AddComponent<GameOverController>();
            SetObjectField(flow, "fadeCanvasGroup", group);
            SetObjectField(flow, "jumpscareAudio", stinger);
            SetObjectField(flow, "playerInputToDisable", player.GetComponent<PlayerInput>());

            DemoHUD hud = managers.AddComponent<DemoHUD>();
            SetObjectField(hud, "objectives", objectives);
            SetObjectField(hud, "interactor", player.GetComponent<PlayerInteractor>());
            SetObjectField(hud, "gameFlow", flow);

            if (enemy.onPlayerCaught == null)
                enemy.onPlayerCaught = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddPersistentListener(enemy.onPlayerCaught, flow.OnPlayerCaught);

            return managers;
        }

        private static void WireExitDoor(GameOverController flow)
        {
            ExitDoor door = Object.FindFirstObjectByType<ExitDoor>();
            if (door != null) SetObjectField(door, "gameFlow", flow);
        }

        private static void BakeNavMesh(int levelMask)
        {
            GameObject navGO = new GameObject("Navigation");
            NavMeshSurface surface = navGO.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = levelMask;
            surface.BuildNavMesh();

            if (surface.navMeshData != null && !AssetDatabase.Contains(surface.navMeshData))
            {
                AssetDatabase.CreateAsset(surface.navMeshData, "Assets/Scenes/Demo_NavMesh.asset");
                AssetDatabase.SaveAssets();
            }
        }

        private static void RegisterSceneInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath)) return;

            // Index 0 so GameOverController's reload-by-build-index works.
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 position, Vector3 size, int layer, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.layer = layer;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static Material GetOrCreateMaterial(string name, Color color, bool emissive = false)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets", "Materials");

            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            Material mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 2.5f);
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static int EnsureLayer(string layerName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return 0;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }

            Debug.LogWarning("[FearMe] No free layer slot for '" + layerName + "'; using Default.");
            return 0;
        }

        private static void SetObjectField(Object target, string fieldName, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning("[FearMe] Missing field '" + fieldName + "' on " + target.GetType().Name);
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArrayField(Object target, string fieldName, Object[] values)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;

            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloatField(Object target, string fieldName, float value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetIntField(Object target, string fieldName, int value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
