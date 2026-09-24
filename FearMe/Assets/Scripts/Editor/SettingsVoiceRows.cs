using FearMe.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FearMe.EditorTools
{
    // The two voice rows for the settings panel, for a fresh menu build and
    // for a menu scene that already exists and has been arranged by hand.
    public static class SettingsVoiceRows
    {
        // Below BACK, so nothing already laid out moves; the panel grows to fit.
        private const float ToggleY = -290f;
        private const float SliderY = -345f;
        private const float ExtraHeight = 140f;

        internal static void Add(RectTransform panel, Font font, out Toggle mic, out Slider sensitivity)
        {
            mic = MainMenuBuilder.CreateToggleRow(panel, "Mic", "VOICE ATTRACTS IT (MICROPHONE)", font, ToggleY);
            sensitivity = MainMenuBuilder.CreateSliderRow(panel, "MicSensitivity", "VOICE SENSITIVITY", font,
                SliderY, 0f, 1f, out Text _);

            // Grow downwards only, keeping every existing row where it was.
            Vector2 size = panel.sizeDelta;
            panel.sizeDelta = new Vector2(size.x, size.y + ExtraHeight);
            panel.anchoredPosition += new Vector2(0f, -ExtraHeight * 0.5f);
            foreach (RectTransform child in panel)
            {
                if (child.anchorMin == child.anchorMax) child.anchoredPosition += new Vector2(0f, ExtraHeight * 0.5f);
            }
        }

        [MenuItem("Tools/FearMe/Add Voice Settings (current scene)")]
        public static void AddToCurrentScene()
        {
            SettingsPanel settings = Object.FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);
            if (settings == null)
            {
                Debug.LogError("[FearMe] No SettingsPanel in this scene. Open the main menu scene first.");
                return;
            }

            SerializedObject so = new SerializedObject(settings);
            if (so.FindProperty("micToggle").objectReferenceValue != null)
            {
                Debug.LogWarning("[FearMe] The settings panel already has voice settings.");
                return;
            }

            RectTransform panel = (RectTransform)settings.transform;
            Add(panel, MainMenuBuilder.ResolveFont(), out Toggle mic, out Slider sensitivity);

            MainMenuBuilder.SetObjectField(settings, "micToggle", mic);
            MainMenuBuilder.SetObjectField(settings, "micSensitivitySlider", sensitivity);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[FearMe] Voice settings added to the settings panel. Save the scene.");
        }
    }
}
