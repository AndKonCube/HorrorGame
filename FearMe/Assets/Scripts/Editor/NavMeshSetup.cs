using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Rebakes the NavMesh for whatever scene is open.
    //
    // The usual failure is the surface being restricted to the generated
    // blockout's layer while the real geometry sits on Default, so it bakes
    // nothing at all. This picks the layers that actually hold geometry and
    // reports what came out, rather than silently producing an empty mesh.
    public static class NavMeshSetup
    {
        private const string DataPath = "Assets/Scenes/Demo_NavMesh.asset";
        private const string LevelLayerName = "Level";

        [MenuItem("Tools/FearMe/Rebake NavMesh (current scene)")]
        public static void RebakeCurrentScene()
        {
            NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (surface == null)
            {
                GameObject go = new GameObject("Navigation");
                surface = go.AddComponent<NavMeshSurface>();
                Debug.Log("[FearMe] No NavMeshSurface found; created one.");
            }

            surface.collectObjects = CollectObjects.All;
            surface.layerMask = ChooseLayers();

            surface.BuildNavMesh();
            PersistData(surface);

            Report(surface);

            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // Prefer the dedicated Level layer, but only if the geometry is
        // actually on it; otherwise take everything.
        private static LayerMask ChooseLayers()
        {
            int levelLayer = LayerMask.NameToLayer(LevelLayerName);
            if (levelLayer < 0)
            {
                Debug.Log("[FearMe] No '" + LevelLayerName + "' layer; baking all layers.");
                return ~0;
            }

            int onLevelLayer = 0;
            int total = 0;

            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                total++;
                if (renderer.gameObject.layer == levelLayer) onLevelLayer++;
            }

            if (onLevelLayer >= 4)
            {
                Debug.Log($"[FearMe] Baking the '{LevelLayerName}' layer ({onLevelLayer} of {total} renderers).");
                return 1 << levelLayer;
            }

            Debug.Log($"[FearMe] Only {onLevelLayer} of {total} renderers are on '{LevelLayerName}', " +
                "so baking all layers instead. Agents with a NavMeshAgent are excluded automatically.");
            return ~0;
        }

        private static void PersistData(NavMeshSurface surface)
        {
            if (surface.navMeshData == null) return;

            if (!AssetDatabase.Contains(surface.navMeshData))
            {
                AssetDatabase.CreateAsset(surface.navMeshData, DataPath);
            }
            else
            {
                EditorUtility.SetDirty(surface.navMeshData);
            }

            AssetDatabase.SaveAssets();
        }

        // An empty bake still "succeeds", so check there are triangles.
        private static void Report(NavMeshSurface surface)
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            int triangles = triangulation.indices.Length / 3;

            if (triangles == 0)
            {
                Debug.LogError("[FearMe] NavMesh baked empty. Check that the floor has a MeshRenderer, " +
                    "is roughly level, and is not excluded by the surface's Include Layers.");
                return;
            }

            Debug.Log($"[FearMe] NavMesh baked: {triangles} triangles. Saved to {DataPath}.");

            foreach (NavMeshAgent agent in Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            {
                if (NavMesh.SamplePosition(agent.transform.position, out _, 4f, NavMesh.AllAreas)) continue;

                Debug.LogWarning("[FearMe] '" + agent.name + "' is not standing near the NavMesh; " +
                    "move it over walkable floor or it will not path.");
            }
        }
    }
}
