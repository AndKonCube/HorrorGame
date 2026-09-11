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
    // Puts level geometry on the Level layer so the NavMesh bake can be
    // restricted to it. Keeping the bake layer-scoped is what stops ceilings
    // and pickups turning into walkable surfaces or holes.
    //
    // Both entry points are undoable: if a pass grabs something it should
    // not have, Ctrl+Z puts it back.
    public static class LevelLayerSetup
    {
        private const string LevelLayerName = "Level";

        // A ceiling on the bake layer becomes a walkable floor on the roof.
        private static readonly string[] SkipNames = { "ceiling", "roof", "apparition", "keyitem" };

        [MenuItem("Tools/FearMe/Layers/Assign Level Layer to Selection")]
        public static void AssignToSelection()
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
            {
                Debug.LogWarning("[FearMe] Nothing selected. Select the new walls, floors or beds first.");
                return;
            }

            int layer = EnsureLevelLayer();
            int changed = 0;

            foreach (GameObject go in selection)
                changed += ApplyRecursive(go, layer);

            Finish(changed, "selection");
        }

        [MenuItem("Tools/FearMe/Layers/Assign Level Layer to Static Geometry")]
        public static void AssignToStaticGeometry()
        {
            int changed = AssignAllGeometry(out _);
            Finish(changed, "static geometry");
        }

        // Assignment without the rebake, so the NavMesh repair can call it
        // without the two bouncing off each other forever.
        internal static int AssignAllGeometry(out int layer)
        {
            layer = EnsureLevelLayer();
            int changed = 0;

            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                GameObject go = renderer.gameObject;
                if (go.layer == layer || ShouldSkip(go)) continue;

                Undo.RecordObject(go, "Assign Level Layer");
                go.layer = layer;
                changed++;
            }

            return changed;
        }

        internal static int CountOnLevelLayer()
        {
            int layer = LayerMask.NameToLayer(LevelLayerName);
            if (layer < 0) return 0;

            int count = 0;
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.gameObject.layer == layer) count++;
            }

            return count;
        }

        // Gameplay objects must stay off the bake layer: agents get carved
        // around, pickups punch holes, and the player is not scenery.
        private static bool ShouldSkip(GameObject go)
        {
            if (go.GetComponentInParent<PlayerController>() != null) return true;
            if (go.GetComponentInParent<NavMeshAgent>() != null) return true;
            if (go.GetComponentInParent<KeyItem>() != null) return true;

            string name = go.name.ToLowerInvariant();
            foreach (string fragment in SkipNames)
            {
                if (name.Contains(fragment)) return true;
            }

            return false;
        }

        private static int ApplyRecursive(GameObject go, int layer)
        {
            int changed = 0;

            if (!ShouldSkip(go) && go.layer != layer)
            {
                Undo.RecordObject(go, "Assign Level Layer");
                go.layer = layer;
                changed++;
            }

            foreach (Transform child in go.transform)
                changed += ApplyRecursive(child.gameObject, layer);

            return changed;
        }

        private static void Finish(int changed, string what)
        {
            if (changed == 0)
            {
                Debug.Log($"[FearMe] Nothing to change in {what}; already on the {LevelLayerName} layer.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[FearMe] Moved {changed} object(s) in {what} onto the {LevelLayerName} layer. Rebaking.");

            // Layer changes only matter once the NavMesh is rebuilt.
            NavMeshSetup.RebakeCurrentScene();
        }

        private static int EnsureLevelLayer()
        {
            int existing = LayerMask.NameToLayer(LevelLayerName);
            if (existing >= 0) return existing;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return 0;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = LevelLayerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }

            Debug.LogWarning("[FearMe] No free layer slot; using Default.");
            return 0;
        }
    }
}
