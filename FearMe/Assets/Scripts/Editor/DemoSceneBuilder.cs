using System.Collections.Generic;
using FearMe.AI;
using FearMe.Core;
using FearMe.Player;
using FearMe.Scares;
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
    // Generates the playable demo: the first floor of a hospital, plus the
    // player rig, stalker enemy, baked NavMesh, patrol route and objectives.
    //
    // Floor plan (80 x 60), a grid of corridors so chases can loop:
    //   corridors run E-W at z = +-10 and N-S at x = -25, 0, +25
    //   north row   : operating theatre | ward A | ward B | supply
    //   middle row  : radiology | nurses' station | pharmacy + records | staff
    //   south row   : morgue | reception | waiting | generator
    public static class DemoSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string MaterialFolder = "Assets/Materials";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string LevelLayerName = "Level";

        private const float WallHeight = 4f;
        private const float WallY = 2f;
        private const float WallThickness = 0.4f;
        private const float DoorHalfWidth = 1.5f;

        private const float HalfWidth = 40f;
        private const float HalfDepth = 30f;

        // Openings where a corridor crosses a wall line.
        private static readonly Vector2[] NorthSouthCorridors =
        {
            new Vector2(-28f, -22f),
            new Vector2(-3f, 3f),
            new Vector2(22f, 28f)
        };

        private static readonly Vector2[] EastWestCorridors =
        {
            new Vector2(-13f, -7f),
            new Vector2(7f, 13f)
        };

        [MenuItem("Tools/FearMe/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            int levelLayer = EnsureLayer(LevelLayerName);
            int levelMask = 1 << levelLayer;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material floorMat = GetOrCreateMaterial("Demo_Floor", new Color(0.17f, 0.17f, 0.16f));
            Material wallMat = GetOrCreateMaterial("Demo_Wall", new Color(0.26f, 0.27f, 0.25f));
            Material propMat = GetOrCreateMaterial("Demo_Prop", new Color(0.34f, 0.36f, 0.36f));
            Material enemyMat = GetOrCreateMaterial("Demo_Enemy", new Color(0.35f, 0.05f, 0.05f));
            Material keyMat = GetOrCreateMaterial("Demo_Key", new Color(0.85f, 0.7f, 0.2f), emissive: true);
            Material doorMat = GetOrCreateMaterial("Demo_Door", new Color(0.30f, 0.18f, 0.08f));

            BuildAtmosphere();
            Transform level = BuildLevel(levelLayer, floorMat, wallMat, doorMat, propMat);
            BuildCeilingLights(level);

            GameObject player = BuildPlayer();
            PatrolRoute route = BuildPatrolRoute();
            EnemyStalkerAI enemy = BuildEnemy(enemyMat, player.GetComponent<PlayerController>(), route, levelMask);

            // Alcoves to break line of sight, spread across the wings.
            BuildHidingSpot(level, new Vector3(-18f, 0f, 27f), 180f, levelLayer, wallMat);
            BuildHidingSpot(level, new Vector3(18f, 0f, -26f), 0f, levelLayer, wallMat);
            BuildHidingSpot(level, new Vector3(35f, 0f, 3f), 270f, levelLayer, wallMat);
            BuildHidingSpot(level, new Vector3(-19f, 0f, -26f), 0f, levelLayer, wallMat);

            BuildKey(new Vector3(-34f, 1f, -20f), keyMat);  // morgue
            BuildKey(new Vector3(-34f, 1f, 22f), keyMat);   // operating theatre
            BuildKey(new Vector3(17f, 1f, 0f), keyMat);     // records

            GameObject managers = BuildManagers(player, enemy);
            WireExitDoor(managers.GetComponent<GameOverController>());

            BuildFog(managers, player.transform);
            BuildFogZones(level);

            BakeNavMesh(levelMask);

            // After the bake: apparition placement samples the NavMesh.
            ScareDirector director = BuildScares(player, enemy, levelMask);
            AmbientAudioSetup.Configure(AmbientAudioSetup.CreateController(), director);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterSceneInBuildSettings();

            AssetDatabase.SaveAssets();
            Debug.Log("[FearMe] Hospital demo scene built at " + ScenePath + ". Press Play to run it.");
        }

        private static void BuildAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.045f;
            RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.04f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.055f, 0.07f);
            RenderSettings.skybox = null;
        }

        private static Transform BuildLevel(int levelLayer, Material floorMat, Material wallMat, Material doorMat, Material propMat)
        {
            GameObject root = new GameObject("Level");
            Transform t = root.transform;

            CreateBox(t, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(80f, 0.5f, 60f), levelLayer, floorMat);
            // Default layer keeps the ceiling out of the NavMesh bake and the vision mask.
            CreateBox(t, "Ceiling", new Vector3(0f, WallHeight, 0f), new Vector3(80f, 0.5f, 60f), 0, floorMat);

            BuildOuterShell(t, levelLayer, wallMat, doorMat);
            BuildInteriorWalls(t, levelLayer, wallMat);
            BuildProps(t, levelLayer, propMat);

            return t;
        }

        private static void BuildOuterShell(Transform t, int layer, Material wallMat, Material doorMat)
        {
            BuildWallRun(t, "Wall_North", true, HalfDepth, -HalfWidth, HalfWidth, null, layer, wallMat);
            BuildWallRun(t, "Wall_West", false, -HalfWidth, -HalfDepth, HalfDepth, null, layer, wallMat);
            BuildWallRun(t, "Wall_East", false, HalfWidth, -HalfDepth, HalfDepth, null, layer, wallMat);

            // South wall carries the entrance the player escapes through.
            List<Vector2> southGaps = new List<Vector2> { new Vector2(-2f, 2f) };
            BuildWallRun(t, "Wall_South", true, -HalfDepth, -HalfWidth, HalfWidth, southGaps, layer, wallMat);

            GameObject door = CreateBox(t, "ExitDoor", new Vector3(0f, WallY, -HalfDepth),
                new Vector3(4f, WallHeight, WallThickness), layer, doorMat);
            door.AddComponent<ExitDoor>();
        }

        private static void BuildInteriorWalls(Transform t, int layer, Material mat)
        {
            // North side of the north corridor: theatre, two wards, supply.
            List<Vector2> z13 = CorridorGaps(NorthSouthCorridors);
            AddDoor(z13, -34f);
            AddDoor(z13, -16f);
            AddDoor(z13, -8f);
            AddDoor(z13, 10f);
            AddDoor(z13, 17f);
            AddDoor(z13, 34f);
            BuildWallRun(t, "Wall_Z13", true, 13f, -HalfWidth, HalfWidth, z13, layer, mat);

            // South side of the north corridor: radiology, nurses' station, pharmacy, staff.
            List<Vector2> z7 = CorridorGaps(NorthSouthCorridors);
            AddDoor(z7, -34f);
            AddDoor(z7, 12f);
            AddDoor(z7, 34f);
            z7.Add(new Vector2(-16f, -8f)); // nurses' station opens onto the corridor
            BuildWallRun(t, "Wall_Z7", true, 7f, -HalfWidth, HalfWidth, z7, layer, mat);

            // North side of the south corridor.
            List<Vector2> zMinus7 = CorridorGaps(NorthSouthCorridors);
            AddDoor(zMinus7, -34f);
            AddDoor(zMinus7, -12f);
            AddDoor(zMinus7, 12f);
            AddDoor(zMinus7, 34f);
            BuildWallRun(t, "Wall_ZMinus7", true, -7f, -HalfWidth, HalfWidth, zMinus7, layer, mat);

            // South side of the south corridor: morgue, reception, waiting, generator.
            List<Vector2> zMinus13 = CorridorGaps(NorthSouthCorridors);
            AddDoor(zMinus13, -34f);
            AddDoor(zMinus13, -12f);
            AddDoor(zMinus13, 12f);
            AddDoor(zMinus13, 34f);
            BuildWallRun(t, "Wall_ZMinus13", true, -13f, -HalfWidth, HalfWidth, zMinus13, layer, mat);

            // Vertical corridor walls; doors let each wing be entered from the side too.
            float[] verticalLines = { -28f, -22f, -3f, 3f, 22f, 28f };
            foreach (float x in verticalLines)
            {
                List<Vector2> gaps = CorridorGaps(EastWestCorridors);
                AddDoor(gaps, -20f);
                AddDoor(gaps, 0f);
                AddDoor(gaps, 20f);
                BuildWallRun(t, "Wall_X" + x, false, x, -HalfDepth, HalfDepth, gaps, layer, mat);
            }

            // Room splits inside the larger blocks.
            BuildWallRun(t, "Split_WardA", false, -12.5f, 13f, HalfDepth, null, layer, mat);
            BuildWallRun(t, "Split_WardB", false, 12.5f, 13f, HalfDepth, null, layer, mat);
            BuildWallRun(t, "Split_Records", false, 12.5f, -7f, 7f, null, layer, mat);
        }

        private static void BuildProps(Transform t, int layer, Material mat)
        {
            GameObject props = new GameObject("Props");
            props.transform.SetParent(t, false);
            Transform p = props.transform;

            // Ward beds.
            float[] bedRows = { 18f, 24f };
            float[] bedColumns = { -20f, -15f, -10f, -5.5f, 5.5f, 10f, 15f, 20f };
            foreach (float x in bedColumns)
            {
                foreach (float z in bedRows)
                    CreateBox(p, "Bed", new Vector3(x, 0.5f, z), new Vector3(2f, 0.5f, 1f), layer, mat);
            }

            // Reception desk and waiting benches.
            CreateBox(p, "ReceptionDesk", new Vector3(-10f, 0.55f, -20f), new Vector3(12f, 1.1f, 0.9f), layer, mat);
            CreateBox(p, "Bench_1", new Vector3(10f, 0.3f, -18f), new Vector3(6f, 0.6f, 0.8f), layer, mat);
            CreateBox(p, "Bench_2", new Vector3(10f, 0.3f, -22f), new Vector3(6f, 0.6f, 0.8f), layer, mat);

            // Morgue slabs.
            CreateBox(p, "Slab_1", new Vector3(-36f, 0.5f, -18f), new Vector3(1.2f, 0.5f, 2.4f), layer, mat);
            CreateBox(p, "Slab_2", new Vector3(-36f, 0.5f, -22f), new Vector3(1.2f, 0.5f, 2.4f), layer, mat);
            CreateBox(p, "Slab_3", new Vector3(-32f, 0.5f, -20f), new Vector3(1.2f, 0.5f, 2.4f), layer, mat);

            // Operating table and radiology gear.
            CreateBox(p, "OperatingTable", new Vector3(-34f, 0.6f, 20f), new Vector3(1.4f, 0.6f, 2.6f), layer, mat);
            CreateBox(p, "Scanner", new Vector3(-34f, 1f, 0f), new Vector3(3f, 2f, 2f), layer, mat);

            // Pharmacy shelving.
            CreateBox(p, "Shelf_1", new Vector3(7f, 1f, 4f), new Vector3(6f, 2f, 0.6f), layer, mat);
            CreateBox(p, "Shelf_2", new Vector3(7f, 1f, -4f), new Vector3(6f, 2f, 0.6f), layer, mat);
        }

        private static void BuildCeilingLights(Transform level)
        {
            GameObject lights = new GameObject("CeilingLights");
            lights.transform.SetParent(level, false);

            Vector3[] positions =
            {
                new Vector3(0f, 3.6f, -25f), new Vector3(0f, 3.6f, -18f), new Vector3(0f, 3.6f, 0f),
                new Vector3(0f, 3.6f, 18f), new Vector3(0f, 3.6f, 25f),
                new Vector3(-25f, 3.6f, 10f), new Vector3(0f, 3.6f, 10f), new Vector3(25f, 3.6f, 10f),
                new Vector3(-25f, 3.6f, -10f), new Vector3(0f, 3.6f, -10f), new Vector3(25f, 3.6f, -10f),
                new Vector3(-34f, 3.6f, 20f), new Vector3(-34f, 3.6f, -20f), new Vector3(17f, 3.6f, 0f)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject go = new GameObject("CeilingLight_" + i);
                go.transform.SetParent(lights.transform, false);
                go.transform.position = positions[i];

                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 14f;
                light.intensity = 1.1f;
                light.color = new Color(0.75f, 0.82f, 0.85f);

                // Only some of them are faulty, so the flicker still reads as wrong.
                if (i % 3 == 0) go.AddComponent<FlickeringLight>();
            }
        }

        private static GameObject BuildPlayer()
        {
            GameObject player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.1f, -27f);

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
            cam.farClipPlane = 140f;
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";

            GameObject lightGO = new GameObject("FlashlightBeam");
            lightGO.transform.SetParent(camGO.transform, false);
            Light beam = lightGO.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 24f;
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

            // A tour of the corridor grid, so the stalker sweeps the whole floor.
            Vector3[] points =
            {
                new Vector3(0f, 0f, -25f),
                new Vector3(-25f, 0f, -10f),
                new Vector3(-25f, 0f, 25f),
                new Vector3(0f, 0f, 25f),
                new Vector3(25f, 0f, 25f),
                new Vector3(25f, 0f, 10f),
                new Vector3(25f, 0f, -10f),
                new Vector3(0f, 0f, -10f)
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
            enemy.transform.position = new Vector3(25f, 1f, 20f);
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
            // Corridors are long, so it can spot you from further away here.
            SetFloatField(ai, "viewDistance", 18f);
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

        private static void BuildFog(GameObject managers, Transform followTarget)
        {
            Material hazeMat = GetOrCreateHazeMaterial(GetOrCreateSoftParticleTexture());
            ParticleSystem haze = BuildGroundHaze(hazeMat);

            VolumetricFogController fog = managers.AddComponent<VolumetricFogController>();
            SetFogProfile(fog, "baseProfile", "Corridors", 0.045f, new Color(0.03f, 0.035f, 0.04f), 14f, 1.5f);
            SetObjectField(fog, "followTarget", followTarget);
            SetObjectField(fog, "hazeParticles", haze);
        }

        private static ParticleSystem BuildGroundHaze(Material hazeMat)
        {
            GameObject go = new GameObject("GroundHaze");
            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.startLifetime = 14f;
            main.startSpeed = 0.15f;
            main.startSize = 16f;
            main.startColor = new Color(0.55f, 0.6f, 0.68f, 0.05f);
            main.maxParticles = 140;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 14f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(45f, 0.4f, 45f);

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = hazeMat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingFudge = 40f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ps;
        }

        private static void BuildFogZones(Transform level)
        {
            BuildFogZone(level, "FogZone_Morgue", new Vector3(-34f, 2f, -21.5f), new Vector3(11f, 4f, 16f),
                "Morgue", 0.085f, new Color(0.04f, 0.05f, 0.045f), 26f);
            BuildFogZone(level, "FogZone_Theatre", new Vector3(-34f, 2f, 21.5f), new Vector3(11f, 4f, 16f),
                "Theatre", 0.07f, new Color(0.05f, 0.05f, 0.062f), 20f);
        }

        private static void BuildFogZone(Transform level, string name, Vector3 position, Vector3 size,
            string label, float density, Color color, float hazeRate)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(level, false);
            go.transform.position = position;

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;

            FogZone zone = go.AddComponent<FogZone>();
            SetFogProfile(zone, "profile", label, density, color, hazeRate, 1.2f);
        }

        private static ScareDirector BuildScares(GameObject player, EnemyStalkerAI enemy, int levelMask)
        {
            GameObject apparition = BuildApparition();

            GameObject scaresGO = new GameObject("ScareDirector");

            ApparitionScare apparitionScare = scaresGO.AddComponent<ApparitionScare>();
            SetObjectField(apparitionScare, "apparition", apparition);
            SetIntField(apparitionScare, "obstructionMask", levelMask);

            LightFailureScare lightScare = scaresGO.AddComponent<LightFailureScare>();

            GameObject audioGO = new GameObject("ScareAudio");
            AudioSource scareSource = audioGO.AddComponent<AudioSource>();
            scareSource.playOnAwake = false;
            scareSource.spatialBlend = 1f;
            scareSource.rolloffMode = AudioRolloffMode.Linear;
            scareSource.maxDistance = 45f;

            // Two flavours: something breathing behind you, something far off.
            PositionalSoundScare whisper = scaresGO.AddComponent<PositionalSoundScare>();
            SetObjectField(whisper, "source", scareSource);
            SetEnumField(whisper, "placement", 0);

            PositionalSoundScare distant = scaresGO.AddComponent<PositionalSoundScare>();
            SetObjectField(distant, "source", scareSource);
            SetEnumField(distant, "placement", 2);

            ScareDirector director = scaresGO.AddComponent<ScareDirector>();
            SetObjectField(director, "player", player.GetComponent<PlayerController>());
            SetObjectField(director, "playerCamera", player.GetComponentInChildren<Camera>());
            SetObjectField(director, "stalker", enemy.transform);
            SetObjectArrayField(director, "scares",
                new Object[] { apparitionScare, lightScare, whisper, distant });

            return director;
        }

        private static GameObject BuildApparition()
        {
            Material mat = GetOrCreateMaterial("Demo_Apparition", new Color(0.02f, 0.02f, 0.025f));

            GameObject apparition = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            apparition.name = "Apparition";
            apparition.transform.localScale = new Vector3(0.8f, 0.95f, 0.8f);
            apparition.GetComponent<Renderer>().sharedMaterial = mat;

            // No collider: it must never block movement, sight lines or the bake.
            Collider collider = apparition.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            apparition.SetActive(false);
            return apparition;
        }

        private static Material GetOrCreateHazeMaterial(Texture2D softParticle)
        {
            string path = MaterialFolder + "/Demo_Haze.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            Material mat = new Material(shader);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", softParticle);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", softParticle);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.55f, 0.6f, 0.68f, 1f));

            // Alpha-blended transparent, no depth write.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // A round soft-edged dot; without it the haze renders as hard squares.
        private static Texture2D GetOrCreateSoftParticleTexture()
        {
            const string folder = "Assets/Textures";
            const string path = folder + "/SoftParticle.png";

            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "Textures");

            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, falloff * falloff));
                }
            }

            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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

        private static List<Vector2> CorridorGaps(Vector2[] crossings)
        {
            return new List<Vector2>(crossings);
        }

        private static void AddDoor(List<Vector2> gaps, float center)
        {
            gaps.Add(new Vector2(center - DoorHalfWidth, center + DoorHalfWidth));
        }

        // Builds a straight wall along one axis, skipping any gap intervals.
        private static void BuildWallRun(Transform parent, string name, bool alongX, float fixedCoord,
            float start, float end, List<Vector2> gaps, int layer, Material mat)
        {
            List<Vector2> sorted = gaps != null ? new List<Vector2>(gaps) : new List<Vector2>();
            sorted.Sort((a, b) => a.x.CompareTo(b.x));

            float cursor = start;
            int index = 0;

            foreach (Vector2 gap in sorted)
            {
                float gapStart = Mathf.Max(gap.x, start);
                float gapEnd = Mathf.Min(gap.y, end);
                if (gapEnd <= cursor) continue;

                if (gapStart > cursor)
                    CreateWallSegment(parent, name + "_" + index++, alongX, fixedCoord, cursor, gapStart, layer, mat);

                cursor = gapEnd;
            }

            if (cursor < end)
                CreateWallSegment(parent, name + "_" + index, alongX, fixedCoord, cursor, end, layer, mat);
        }

        private static void CreateWallSegment(Transform parent, string name, bool alongX, float fixedCoord,
            float from, float to, int layer, Material mat)
        {
            float length = to - from;
            if (length < 0.05f) return;

            float mid = (from + to) * 0.5f;
            Vector3 center = alongX
                ? new Vector3(mid, WallY, fixedCoord)
                : new Vector3(fixedCoord, WallY, mid);
            Vector3 size = alongX
                ? new Vector3(length, WallHeight, WallThickness)
                : new Vector3(WallThickness, WallHeight, length);

            CreateBox(parent, name, center, size, layer, mat);
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

        private static void SetEnumField(Object target, string fieldName, int enumIndex)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            prop.enumValueIndex = enumIndex;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFogProfile(Object target, string fieldName, string label,
            float density, Color color, float hazeRate, float blendSpeed)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning("[FearMe] Missing fog profile '" + fieldName + "' on " + target.GetType().Name);
                return;
            }

            prop.FindPropertyRelative("label").stringValue = label;
            prop.FindPropertyRelative("density").floatValue = density;
            prop.FindPropertyRelative("color").colorValue = color;
            prop.FindPropertyRelative("hazeRate").floatValue = hazeRate;
            prop.FindPropertyRelative("blendSpeed").floatValue = blendSpeed;
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
