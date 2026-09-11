using System.Collections.Generic;
using FearMe.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // One candidate key spot per room, so keys can be anywhere in the
    // hospital. Shared by the scene builder and the retrofit menu item.
    public static class KeySpawnSetup
    {
        // Picked to sit clear of beds, slabs, desks and doorways.
        private static readonly Vector3[] Spots =
        {
            new Vector3(-31f, 1f, 26f),   // operating theatre
            new Vector3(-20f, 1f, 28f),   // ward A, left bay
            new Vector3(-6f, 1f, 28f),    // ward A, right bay
            new Vector3(6f, 1f, 28f),     // ward B, left bay
            new Vector3(19f, 1f, 28f),    // ward B, right bay
            new Vector3(34f, 1f, 22f),    // supply
            new Vector3(-37f, 1f, 4f),    // radiology
            new Vector3(-18f, 1f, -4f),   // nurses' station
            new Vector3(10f, 1f, 0f),     // pharmacy
            new Vector3(17f, 1f, 0f),     // records
            new Vector3(-34f, 1f, -26f),  // morgue
            new Vector3(-18f, 1f, -16f),  // reception
            new Vector3(18f, 1f, -18f),   // waiting
            new Vector3(34f, 1f, -20f)    // generator
        };

        [MenuItem("Tools/FearMe/Scatter Key Spots (current scene)")]
        public static void ScatterInCurrentScene()
        {
            Material material = FindKeyMaterial();
            ObjectiveTracker tracker = Object.FindFirstObjectByType<ObjectiveTracker>();

            GameObject root = GameObject.Find("KeySpots");
            if (root == null) root = new GameObject("KeySpots");

            List<KeyItem> keys = new List<KeyItem>(Object.FindObjectsByType<KeyItem>(FindObjectsSortMode.None));

            int added = 0;
            foreach (Vector3 spot in Spots)
            {
                if (HasKeyNear(keys, spot)) continue;

                keys.Add(CreateKey(root.transform, spot, material));
                added++;
            }

            KeySpawner spawner = Object.FindFirstObjectByType<KeySpawner>();
            if (spawner == null) spawner = root.AddComponent<KeySpawner>();

            Wire(spawner, keys, tracker);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[FearMe] {keys.Count} key spot(s) available ({added} new). " +
                "A random subset is kept each run. Save the scene to keep it.");
        }

        // Used by the scene builder, which has its own material and parent.
        internal static void PlaceAllSpots(Transform parent, Material material, ObjectiveTracker tracker)
        {
            PlaceSpots(parent, material, tracker, Spots);
        }

        // Positions supplied by the caller, for an environment whose layout
        // is not known ahead of time.
        internal static void PlaceSpots(Transform parent, Material material,
            ObjectiveTracker tracker, IList<Vector3> positions)
        {
            GameObject root = new GameObject("KeySpots");
            if (parent != null) root.transform.SetParent(parent, false);

            List<KeyItem> keys = new List<KeyItem>();
            foreach (Vector3 spot in positions)
                keys.Add(CreateKey(root.transform, spot, material));

            KeySpawner spawner = root.AddComponent<KeySpawner>();
            Wire(spawner, keys, tracker);
        }

        private static void Wire(KeySpawner spawner, List<KeyItem> keys, ObjectiveTracker tracker)
        {
            SerializedObject so = new SerializedObject(spawner);

            SerializedProperty list = so.FindProperty("candidates");
            if (list != null)
            {
                list.arraySize = keys.Count;
                for (int i = 0; i < keys.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = keys[i];
            }

            SerializedProperty objectives = so.FindProperty("objectives");
            if (objectives != null && tracker != null)
                objectives.objectReferenceValue = tracker;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static KeyItem CreateKey(Transform parent, Vector3 position, Material material)
        {
            GameObject key = GameObject.CreatePrimitive(PrimitiveType.Cube);
            key.name = "KeyItem";
            key.transform.SetParent(parent, false);
            key.transform.position = position;
            key.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

            if (material != null) key.GetComponent<Renderer>().sharedMaterial = material;

            GameObject glow = new GameObject("KeyGlow");
            glow.transform.SetParent(key.transform, false);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.85f, 0.4f);

            return key.AddComponent<KeyItem>();
        }

        private static bool HasKeyNear(List<KeyItem> keys, Vector3 spot)
        {
            foreach (KeyItem key in keys)
            {
                if (key != null && Vector3.Distance(key.transform.position, spot) < 3f)
                    return true;
            }
            return false;
        }

        private static Material FindKeyMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Demo_Key.mat");
            if (existing != null) return existing;

            foreach (KeyItem key in Object.FindObjectsByType<KeyItem>(FindObjectsSortMode.None))
            {
                Renderer renderer = key.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null) return renderer.sharedMaterial;
            }

            return null;
        }
    }
}
