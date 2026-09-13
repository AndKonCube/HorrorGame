using System.Collections.Generic;
using System.IO;
using FearMe.AI;
using FearMe.Core;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Repairs and rebakes the NavMesh for the open scene.
    //
    // Written against the ways this actually breaks:
    //   - several NavMeshSurfaces stacked up from repeated attempts, all with
    //     the same agent type, baking over one another to nothing
    //   - geometry sitting on a layer the surface does not collect
    //   - ceilings collected too, giving a walkable surface on the roof
    //   - agents left standing off the mesh, which spams "Failed to create
    //     agent because it is not close enough to the NavMesh"
    public static class NavMeshSetup
    {
        private const string LevelLayerName = "Level";

        [MenuItem("Tools/FearMe/Rebake NavMesh (current scene)")]
        public static void RebakeCurrentScene()
        {
            NavMeshSurface surface = ConsolidateSurfaces();
            if (surface == null) return;

            PrepareLayers(out int levelLayer);

            surface.collectObjects = CollectObjects.All;
            // Level only: ceilings stay off this layer, so no walkable roofs.
            surface.layerMask = 1 << levelLayer;
            surface.BuildNavMesh();

            PersistData(surface);
            SnapAgents();
            SnapWaypoints();
            FixStalkerVision(levelLayer);
            Report();

            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // One surface per agent type. Stacked duplicates bake over each other.
        private static NavMeshSurface ConsolidateSurfaces()
        {
            NavMeshSurface[] surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);

            if (surfaces.Length == 0)
            {
                GameObject go = new GameObject("Navigation");
                Debug.Log("[FearMe] No NavMeshSurface found; created one.");
                return go.AddComponent<NavMeshSurface>();
            }

            if (surfaces.Length == 1) return surfaces[0];

            NavMeshSurface keep = surfaces[0];
            for (int i = 1; i < surfaces.Length; i++)
            {
                Undo.DestroyObjectImmediate(surfaces[i]);
            }

            Debug.LogWarning($"[FearMe] Found {surfaces.Length} NavMeshSurfaces in this scene and removed " +
                $"{surfaces.Length - 1}. Several surfaces sharing one agent type bake over each other, " +
                "which is why nothing was produced. Keeping the one on '" + keep.gameObject.name + "'.");

            return keep;
        }

        private static void PrepareLayers(out int levelLayer)
        {
            int onLayer = LevelLayerSetup.CountOnLevelLayer();
            if (onLayer >= 4)
            {
                levelLayer = LayerMask.NameToLayer(LevelLayerName);
                Debug.Log($"[FearMe] Baking the {LevelLayerName} layer ({onLayer} meshes already on it).");
                return;
            }

            int moved = LevelLayerSetup.AssignAllGeometry(out levelLayer);
            Debug.Log($"[FearMe] Only {onLayer} mesh(es) were on {LevelLayerName}; moved {moved} more onto it " +
                "(ceilings, the player, agents and pickups excluded).");
        }

        private static void PersistData(NavMeshSurface surface)
        {
            if (surface.navMeshData == null) return;

            if (!AssetDatabase.Contains(surface.navMeshData))
            {
                AssetDatabase.CreateAsset(surface.navMeshData, DataPathForActiveScene());
            }
            else
            {
                EditorUtility.SetDirty(surface.navMeshData);
            }

            AssetDatabase.SaveAssets();
        }

        // Beside the scene it belongs to, so scenes cannot share one asset.
        private static string DataPathForActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
                return "Assets/Scenes/Untitled_NavMesh.asset";

            string folder = Path.GetDirectoryName(scene.path).Replace('\\', '/');
            return folder + "/" + scene.name + "_NavMesh.asset";
        }

        // The direct cause of "Failed to create agent": the agent's transform
        // is not over the mesh. Put it there.
        private static void SnapAgents()
        {
            foreach (NavMeshAgent agent in Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            {
                Vector3 groundLevel = agent.transform.position - Vector3.up * agent.baseOffset;

                // This storey first. A 30m search would happily drop the
                // stalker onto a different floor of the building.
                if (!NavMeshUtility.TrySampleSameFloor(groundLevel, out Vector3 onMesh, 5f, 2.5f))
                {
                    if (!NavMesh.SamplePosition(groundLevel, out NavMeshHit far, 30f, NavMesh.AllAreas))
                    {
                        Debug.LogWarning("[FearMe] No NavMesh within 30m of '" + agent.name +
                            "'. Move it over walkable floor by hand.");
                        continue;
                    }

                    onMesh = far.position;
                    Debug.LogWarning($"[FearMe] '{agent.name}' had no mesh on its own storey; moved it " +
                        $"{Mathf.Abs(far.position.y - groundLevel.y):0.#}m vertically. Check it is on the floor you meant.");
                }

                Vector3 placed = onMesh + Vector3.up * agent.baseOffset;
                if ((placed - agent.transform.position).sqrMagnitude < 0.0001f) continue;

                Undo.RecordObject(agent.transform, "Snap agent to NavMesh");
                agent.transform.position = placed;
                Debug.Log($"[FearMe] Moved '{agent.name}' onto the NavMesh.");
            }
        }

        // A waypoint even slightly off the floor gives a path the agent can
        // never complete, which stalls the patrol where it stands.
        private static void SnapWaypoints()
        {
            int moved = 0;
            int stranded = 0;

            foreach (PatrolRoute route in Object.FindObjectsByType<PatrolRoute>(FindObjectsSortMode.None))
            {
                SerializedObject so = new SerializedObject(route);
                SerializedProperty list = so.FindProperty("waypoints");
                if (list == null) continue;

                for (int i = 0; i < list.arraySize; i++)
                {
                    Transform waypoint = list.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (waypoint == null) continue;

                    // Bounded vertically: snapping a first-floor waypoint down
                    // to the ground floor silently breaks an upstairs patrol.
                    if (!NavMeshUtility.TrySampleSameFloor(waypoint.position, out Vector3 onMesh, 6f, 2f))
                    {
                        Debug.LogWarning("[FearMe] '" + waypoint.name + "' has no NavMesh on its own storey; " +
                            "the patrol will skip it. Move it over walkable floor.", waypoint);
                        stranded++;
                        continue;
                    }

                    if ((onMesh - waypoint.position).sqrMagnitude < 0.0025f) continue;

                    Undo.RecordObject(waypoint, "Snap waypoint to NavMesh");
                    waypoint.position = onMesh;
                    moved++;
                }
            }

            if (moved > 0 || stranded > 0)
                Debug.Log($"[FearMe] Snapped {moved} patrol waypoint(s) onto the NavMesh; {stranded} stranded.");
        }

        // A vision mask of Nothing makes the line-of-sight ray hit nothing,
        // so the stalker sees straight through walls.
        private static void FixStalkerVision(int levelLayer)
        {
            foreach (EnemyStalkerAI stalker in Object.FindObjectsByType<EnemyStalkerAI>(FindObjectsSortMode.None))
            {
                SerializedObject so = new SerializedObject(stalker);
                SerializedProperty mask = so.FindProperty("obstructionMask");
                if (mask == null || mask.intValue != 0) continue;

                mask.intValue = 1 << levelLayer;
                so.ApplyModifiedPropertiesWithoutUndo();

                Debug.LogWarning("[FearMe] '" + stalker.name + "' had no vision blockers set, so it could " +
                    "see through walls. Pointed it at the " + LevelLayerName + " layer.", stalker);
            }
        }

        // An empty bake still counts as success, so check for triangles.
        // Groups walkable area into height bands. If the upper floor did not
        // bake, this is where it shows.
        private static void ReportStoreys(NavMeshTriangulation triangulation)
        {
            Dictionary<int, int> bands = new Dictionary<int, int>();

            for (int i = 0; i + 2 < triangulation.indices.Length; i += 3)
            {
                float y = triangulation.vertices[triangulation.indices[i]].y;
                int band = Mathf.FloorToInt(y / 3f);
                bands.TryGetValue(band, out int count);
                bands[band] = count + 1;
            }

            List<int> keys = new List<int>(bands.Keys);
            keys.Sort();

            foreach (int band in keys)
                Debug.Log($"[FearMe]   storey around y={band * 3}..{band * 3 + 3}: {bands[band]} triangles");

            if (keys.Count < 2)
                Debug.LogWarning("[FearMe] Walkable floor found at only one height. If this level has an " +
                    "upper storey, its floor is not on the bake layer or the stairs are too steep to connect.");
        }

        private static void Report()
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            int triangles = triangulation.indices.Length / 3;

            if (triangles == 0)
            {
                Debug.LogError("[FearMe] NavMesh baked empty. Check that the floor meshes have MeshRenderers, " +
                    "are roughly level, and that the agent radius in Window > AI > Navigation is not wider " +
                    "than the corridors.");
                return;
            }

            Debug.Log($"[FearMe] NavMesh baked: {triangles} triangles, saved to {DataPathForActiveScene()}.");
            ReportStoreys(triangulation);
        }
    }
}
