using System.Collections.Generic;
using FearMe.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FearMe.EditorTools
{
    // The voice rows for the settings panel - the microphone that the demon
    // hears, and proximity chat with your partner - for a fresh menu build
    // and for a menu scene that already exists and has been arranged by hand.
    public static class SettingsVoiceRows
    {
        private const float RowSpacing = 55f;

        internal static void AddMicRows(RectTransform panel, Font font, out Toggle mic, out Slider sensitivity)
        {
            List<float> rows = MakeRoom(panel, 2);
            mic = MainMenuBuilder.CreateToggleRow(panel, "Mic", "VOICE ATTRACTS IT (MICROPHONE)", font, rows[0]);
            sensitivity = MainMenuBuilder.CreateSliderRow(panel, "MicSensitivity", "VOICE SENSITIVITY", font,
                rows[1], 0f, 1f, out Text _);
        }

        internal static void AddChatRows(RectTransform panel, Font font, out Toggle chat, out Toggle pushToTalk,
            out Slider volume)
        {
            List<float> rows = MakeRoom(panel, 3);
            chat = MainMenuBuilder.CreateToggleRow(panel, "VoiceChat", "PROXIMITY VOICE CHAT", font, rows[0]);
            pushToTalk = MainMenuBuilder.CreateToggleRow(panel, "PushToTalk", "PUSH TO TALK (HOLD V)", font, rows[1]);
            volume = MainMenuBuilder.CreateSliderRow(panel, "VoiceVolume", "PARTNER'S VOICE", font,
                rows[2], 0f, 1f, out Text _);

            chat.isOn = true;
            volume.value = 1f;
        }

        // Rows below whatever is lowest now (BACK aside), BACK moved to sit
        // under them, and the panel grown downwards just enough to fit - with
        // every other row staying exactly where it was on screen.
        private static List<float> MakeRoom(RectTransform panel, int count)
        {
            RectTransform back = panel.Find("BackButton") as RectTransform;

            float lowest = float.MaxValue;
            foreach (RectTransform child in panel)
            {
                if (child.anchorMin != child.anchorMax) continue; // the stretched background
                if (child == back) continue;
                lowest = Mathf.Min(lowest, child.anchoredPosition.y);
            }
            if (lowest == float.MaxValue) lowest = 0f;

            List<float> rows = new List<float>();
            for (int i = 1; i <= count; i++) rows.Add(lowest - RowSpacing * i);

            float bottomRow = rows[rows.Count - 1];
            float backY = bottomRow - RowSpacing - 20f;
            if (back != null) back.anchoredPosition = new Vector2(back.anchoredPosition.x, backY);

            // How far below the current bottom edge the new content reaches.
            float halfHeight = panel.sizeDelta.y * 0.5f;
            float needed = (back != null ? backY - 45f : bottomRow - 30f);
            float extra = Mathf.Max(0f, -needed - halfHeight);

            if (extra > 0f)
            {
                panel.sizeDelta += new Vector2(0f, extra);
                panel.anchoredPosition += new Vector2(0f, -extra * 0.5f);

                // Children are placed from the centre, which just moved down
                // by half; move them back up by the same, new rows included.
                for (int i = 0; i < rows.Count; i++) rows[i] += extra * 0.5f;
                foreach (RectTransform child in panel)
                {
                    if (child.anchorMin == child.anchorMax) child.anchoredPosition += new Vector2(0f, extra * 0.5f);
                }
            }

            return rows;
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
            RectTransform panel = (RectTransform)settings.transform;
            Font font = MainMenuBuilder.ResolveFont();
            List<string> added = new List<string>();

            // Only what is missing, so running this again after an update
            // adds the new rows without doubling the old ones.
            if (so.FindProperty("micToggle").objectReferenceValue == null)
            {
                AddMicRows(panel, font, out Toggle mic, out Slider sensitivity);
                MainMenuBuilder.SetObjectField(settings, "micToggle", mic);
                MainMenuBuilder.SetObjectField(settings, "micSensitivitySlider", sensitivity);
                added.Add("microphone");
            }

            if (so.FindProperty("voiceChatToggle").objectReferenceValue == null)
            {
                AddChatRows(panel, font, out Toggle chat, out Toggle pushToTalk, out Slider volume);
                MainMenuBuilder.SetObjectField(settings, "voiceChatToggle", chat);
                MainMenuBuilder.SetObjectField(settings, "pushToTalkToggle", pushToTalk);
                MainMenuBuilder.SetObjectField(settings, "voiceVolumeSlider", volume);
                added.Add("voice chat");
            }

            if (added.Count == 0)
            {
                Debug.LogWarning("[FearMe] The settings panel already has every voice setting.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[FearMe] Added " + string.Join(" and ", added) + " settings. Save the scene.");
        }
    }
}
