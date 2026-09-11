using FearMe.Scares;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Scare pacing is serialized into the scene, so changing script defaults
    // does nothing to a scene that already exists. This pushes the current
    // tuning onto the open scene without rebuilding it.
    public static class ScareTuning
    {
        [MenuItem("Tools/FearMe/Retune Scares (current scene)")]
        public static void RetuneCurrentScene()
        {
            ScareDirector director = Object.FindFirstObjectByType<ScareDirector>();
            if (director == null)
            {
                Debug.LogWarning("[FearMe] No ScareDirector in this scene.");
                return;
            }

            SetFloat(director, "calmBeforeScare", 8f);
            SetFloat(director, "minSecondsBetweenScares", 18f);
            SetFloat(director, "maxSecondsBetweenScares", 38f);

            int apparitions = 0;
            foreach (ApparitionScare scare in Object.FindObjectsByType<ApparitionScare>(FindObjectsSortMode.None))
            {
                // Weighted well above the others so it wins most slots.
                SetFloat(scare, "weight", 3f);
                SetFloat(scare, "cooldown", 22f);

                SetFloat(scare, "minDistance", 7f);
                SetFloat(scare, "minViewAngle", 12f);
                SetFloat(scare, "maxViewAngle", 85f);
                SetFloat(scare, "behindChance", 0.3f);
                SetInt(scare, "placementAttempts", 24);
                SetFloat(scare, "maxVisibleTime", 6f);
                apparitions++;
            }

            foreach (LightFailureScare scare in Object.FindObjectsByType<LightFailureScare>(FindObjectsSortMode.None))
            {
                SetFloat(scare, "weight", 1f);
                SetFloat(scare, "cooldown", 35f);
            }

            foreach (PositionalSoundScare scare in Object.FindObjectsByType<PositionalSoundScare>(FindObjectsSortMode.None))
            {
                SetFloat(scare, "weight", 1f);
                SetFloat(scare, "cooldown", 30f);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[FearMe] Retuned {apparitions} apparition scare(s); scares now every 18-38s. Save the scene to keep it.");
        }

        private static void SetFloat(Object target, string fieldName, float value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning("[FearMe] Missing field '" + fieldName + "' on " + target.GetType().Name);
                return;
            }
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string fieldName, int value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
