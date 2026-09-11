using System.Collections.Generic;
using System.IO;
using FearMe.AI;
using FearMe.Core;
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
    // Turns any environment scene into a playable demo: copies it, bakes a
    // NavMesh, then places the player, stalker, patrol route, keys and exit
    // at positions sampled from the walkable floor.
    //
    // Nothing here knows the layout, so it works on a hospital, an asylum or
    // anything else - if the geometry bakes, the demo can be built on it.
    public static class EnvironmentDemoBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string LevelLayerName = "Level";

        // Spacing between sampled points, in metres.
        private const float PointSpacing = 7f;
        private const int PatrolPoints = 8;
        private const int KeySpots = 10;
        private const int HidingSpots = 4;

        [MenuItem("Tools/FearMe/Build Demo From Environment Scene")]
        public static void BuildFromEnvironment()
        {
            string source = ResolveSourceScene();
            if (source == null) return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string name = Path.GetFileNameWithoutExtension(source);
            string outPath = SceneFolder + "/Demo_" + name + ".unity";

            if (!CopyScene(source, outPath)) return;

            Scene scene = EditorSceneManager.OpenScene(outPath, OpenSceneMode.Single);

            int levelLayer = DemoSceneBuilder.EnsureLayer(LevelLayerName);
            int levelMask = 1 << levelLayer;

            MoveGeometryToLevelLayer(levelLayer);
            LayerMask bakeMask = Bake(levelMask, name);

            // Everything below is placed from the walkable surface, so the
            // bake has to come first.
            List<Vector3> spots = SampleGroundFloor();
            if (spots.Count < PatrolPoints)
            {
                Debug.LogError($"[FearMe] Only {spots.Count} walkable spot(s) found. " +
                    "The NavMesh is too small or did not bake - check the floor geometry, then retry.");
                return;
            }

            Populate(scene, spots, (int)bakeMask, outPath);
        }

        private static void Populate(Scene scene, List<Vector3> spots, int bakeMask, string outPath)
        {
            Material enemyMat = DemoSceneBuilder.GetOrCreateMaterial("Demo_Enemy", new Color(0.35f, 0.05f, 0.05f));
            Material keyMat = DemoSceneBuilder.GetOrCreateMaterial("Demo_Key", new Color(0.85f, 0.7f, 0.2f), emissive: true);
            Material exitMat = DemoSceneBuilder.GetOrCreateMaterial("Demo_Door", new Color(0.30f, 0.18f, 0.08f));

            DemoSceneBuilder.BuildAtmosphere();

            Vector3 playerSpawn = spots[0];
            Vector3 enemySpawn = Farthest(spots, playerSpawn);

            GameObject player = DemoSceneBuilder.BuildPlayer(playerSpawn + Vector3.up * 0.1f);
            PlayerController controller = player.GetComponent<PlayerController>();

            PatrolRoute route = BuildRoute(Tour(spots, enemySpawn, PatrolPoints));
            EnemyStalkerAI enemy = DemoSceneBuilder.BuildEnemy(
                enemyMat, controller, route, bakeMask, enemySpawn + Vector3.up);

            GameObject managers = DemoSceneBuilder.BuildManagers(player, enemy);
            ObjectiveTracker objectives = managers.GetComponent<ObjectiveTracker>();

            // Keys go as far from the entrance as possible, so the player has
            // to cross the building rather than loot the first room.
            List<Vector3> keySpots = FarthestSet(spots, playerSpawn, KeySpots);
            KeySpawnSetup.PlaceSpots(null, keyMat, objectives, Raised(keySpots, 1f));

            BuildExit(playerSpawn, exitMat, managers.GetComponent<GameOverController>());
            PlaceHidingVolumes(spots, playerSpawn);

            DemoSceneBuilder.BuildFog(managers, player.transform);

            ScareDirector director = DemoSceneBuilder.BuildScares(player, enemy, bakeMask);
            AmbientAudioSetup.Configure(AmbientAudioSetup.CreateController(), director);
            AmbientAudioSetup.AssignScareClips();
            AtmosphereSetup.WireCurrentScene();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, outPath);
            RegisterScene(outPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[FearMe] Demo built at {outPath} from {spots.Count} walkable spot(s). " +
                "Open it and press Play.");
        }

        // A .unity selected in the Project window wins; otherwise fall back to
        // the environment scene that ships with the hospital pack.
        private static string ResolveSourceScene()
        {
            foreach (Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".unity")) return path;
            }

            string[] found = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Art" });
            if (found.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(found[0]);
                Debug.Log("[FearMe] No scene selected; using " + path +
                    ". Select a different .unity in the Project window to build from that instead.");
                return path;
            }

            Debug.LogError("[FearMe] No environment scene found. Select the environment's .unity " +
                "file in the Project window and run this again.");
            return null;
        }

        private static bool CopyScene(string source, string outPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(outPath) != null &&
                !EditorUtility.DisplayDialog("Overwrite demo scene?",
                    outPath + " already exists and will be replaced.\n\n" +
                    "Any hand edits made to it will be lost.", "Overwrite", "Cancel"))
            {
                return false;
            }

            AssetDatabase.DeleteAsset(outPath);
            if (!AssetDatabase.CopyAsset(source, outPath))
            {
                Debug.LogError("[FearMe] Could not copy " + source);
                return false;
            }

            AssetDatabase.Refresh();
            return true;
        }

        private static void MoveGeometryToLevelLayer(int levelLayer)
        {
            int moved = 0;

            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                GameObject go = renderer.gameObject;

                // A ceiling on the bake layer becomes a floor on the roof.
                string name = go.name.ToLowerInvariant();
                if (name.Contains("ceiling") || name.Contains("roof")) continue;
                if (go.layer == levelLayer) continue;

                go.layer = levelLayer;
                moved++;
            }

            Debug.Log($"[FearMe] Put {moved} mesh(es) on the {LevelLayerName} layer.");
        }

        private static LayerMask Bake(int levelMask, string name)
        {
            NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (surface == null)
            {
                GameObject go = new GameObject("Navigation");
                surface = go.AddComponent<NavMeshSurface>();
            }

            surface.collectObjects = CollectObjects.All;
            surface.layerMask = levelMask;
            surface.BuildNavMesh();

            if (surface.navMeshData != null && !AssetDatabase.Contains(surface.navMeshData))
            {
                // Its own asset, so two generated demos cannot clobber each other.
                AssetDatabase.CreateAsset(surface.navMeshData, SceneFolder + "/Demo_" + name + "_NavMesh.asset");
                AssetDatabase.SaveAssets();
            }

            return surface.layerMask;
        }

        // Walkable points on the lowest floor only: an asylum may have upper
        // storeys, and the player should not start on a roof.
        private static List<Vector3> SampleGroundFloor()
        {
            NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
            List<Vector3> centroids = new List<Vector3>();

            for (int i = 0; i + 2 < mesh.indices.Length; i += 3)
            {
                Vector3 a = mesh.vertices[mesh.indices[i]];
                Vector3 b = mesh.vertices[mesh.indices[i + 1]];
                Vector3 c = mesh.vertices[mesh.indices[i + 2]];
                centroids.Add((a + b + c) / 3f);
            }

            if (centroids.Count == 0) return centroids;

            float lowest = float.MaxValue;
            foreach (Vector3 point in centroids) lowest = Mathf.Min(lowest, point.y);

            List<Vector3> groundFloor = new List<Vector3>();
            foreach (Vector3 point in centroids)
            {
                if (point.y <= lowest + 4f) groundFloor.Add(point);
            }

            return Thin(groundFloor, PointSpacing);
        }

        // Greedy spacing pass: keeps the set spread out instead of clustered
        // wherever the mesh happens to be dense.
        private static List<Vector3> Thin(List<Vector3> points, float spacing)
        {
            for (int i = points.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);
                (points[i], points[swap]) = (points[swap], points[i]);
            }

            float minSqr = spacing * spacing;
            List<Vector3> kept = new List<Vector3>();

            foreach (Vector3 point in points)
            {
                bool clear = true;
                foreach (Vector3 other in kept)
                {
                    if ((point - other).sqrMagnitude >= minSqr) continue;
                    clear = false;
                    break;
                }

                if (clear) kept.Add(point);
            }

            return kept;
        }

        private static Vector3 Farthest(List<Vector3> points, Vector3 from)
        {
            Vector3 best = points[0];
            float bestDistance = -1f;

            foreach (Vector3 point in points)
            {
                float distance = (point - from).sqrMagnitude;
                if (distance <= bestDistance) continue;

                bestDistance = distance;
                best = point;
            }

            return best;
        }

        private static List<Vector3> FarthestSet(List<Vector3> points, Vector3 from, int count)
        {
            List<Vector3> sorted = new List<Vector3>(points);
            sorted.Sort((a, b) => (b - from).sqrMagnitude.CompareTo((a - from).sqrMagnitude));

            if (sorted.Count > count) sorted.RemoveRange(count, sorted.Count - count);
            return sorted;
        }

        // Nearest-neighbour ordering, so the patrol walks a sensible circuit
        // instead of criss-crossing the building.
        private static List<Vector3> Tour(List<Vector3> points, Vector3 start, int count)
        {
            List<Vector3> pool = new List<Vector3>(points);
            List<Vector3> tour = new List<Vector3>();
            Vector3 current = start;

            while (tour.Count < count && pool.Count > 0)
            {
                int nearest = 0;
                float best = float.MaxValue;

                for (int i = 0; i < pool.Count; i++)
                {
                    float distance = (pool[i] - current).sqrMagnitude;
                    if (distance >= best) continue;

                    best = distance;
                    nearest = i;
                }

                current = pool[nearest];
                tour.Add(current);
                pool.RemoveAt(nearest);
            }

            return tour;
        }

        private static List<Vector3> Raised(List<Vector3> points, float height)
        {
            List<Vector3> raised = new List<Vector3>();
            foreach (Vector3 point in points) raised.Add(point + Vector3.up * height);
            return raised;
        }

        private static PatrolRoute BuildRoute(List<Vector3> points)
        {
            GameObject routeGO = new GameObject("PatrolRoute");
            PatrolRoute route = routeGO.AddComponent<PatrolRoute>();

            Transform[] waypoints = new Transform[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                GameObject wp = new GameObject("Waypoint_" + (i + 1));
                wp.transform.SetParent(routeGO.transform, false);
                wp.transform.position = points[i];
                waypoints[i] = wp.transform;
            }

            SerializedObject so = new SerializedObject(route);
            SerializedProperty list = so.FindProperty("waypoints");
            list.arraySize = waypoints.Length;
            for (int i = 0; i < waypoints.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = waypoints[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            return route;
        }

        // The way out is where you came in, marked so it can be found again.
        private static void BuildExit(Vector3 position, Material material, GameOverController flow)
        {
            GameObject exit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            exit.name = "ExitPoint";
            exit.transform.position = position + Vector3.up;
            exit.transform.localScale = new Vector3(1.2f, 2f, 0.3f);
            exit.GetComponent<Renderer>().sharedMaterial = material;

            ExitDoor door = exit.AddComponent<ExitDoor>();

            GameObject glow = new GameObject("ExitGlow");
            glow.transform.SetParent(exit.transform, false);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 1.2f;
            light.color = new Color(0.5f, 0.8f, 0.6f);

            SerializedObject so = new SerializedObject(door);
            SerializedProperty prop = so.FindProperty("gameFlow");
            if (prop != null)
            {
                prop.objectReferenceValue = flow;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // Trigger volumes only: the environment's own geometry provides the
        // cover, so no procedural alcove can intersect the level art.
        private static void PlaceHidingVolumes(List<Vector3> spots, Vector3 playerSpawn)
        {
            List<Vector3> chosen = FarthestSet(spots, playerSpawn, HidingSpots + 2);
            GameObject root = new GameObject("HidingSpots");

            int placed = 0;
            foreach (Vector3 spot in chosen)
            {
                if (placed >= HidingSpots) break;

                GameObject go = new GameObject("HideVolume_" + (placed + 1));
                go.transform.SetParent(root.transform, false);
                go.transform.position = spot + Vector3.up;

                BoxCollider box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(1.6f, 2f, 1.6f);
                go.AddComponent<HidingSpot>();
                placed++;
            }

            Debug.Log($"[FearMe] Placed {placed} hiding volume(s). They rely on the level's own " +
                "geometry for cover, so move any that sit in the open.");
        }

        private static void RegisterScene(string path)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
