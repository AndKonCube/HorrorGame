using System.Collections.Generic;
using FearMe.Settings;
using FearMe.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FearMe.EditorTools
{
    // Builds the main menu scene: title, Play / Settings / Quit, and a
    // settings panel wired to SettingsService.
    public static class MainMenuBuilder
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameScenePath = "Assets/Scenes/Demo.unity";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        private static readonly Color Ink = new Color(0.86f, 0.85f, 0.82f);
        private static readonly Color Dim = new Color(0.55f, 0.54f, 0.52f);
        private static readonly Color PanelColor = new Color(0.07f, 0.07f, 0.08f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.16f, 0.16f, 0.18f, 1f);
        private static readonly Color Accent = new Color(0.55f, 0.12f, 0.12f, 1f);

        [MenuItem("Tools/FearMe/Build Main Menu Scene")]
        public static void BuildMainMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Font font = ResolveFont();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildCamera();
            BuildEventSystem();

            Canvas canvas = BuildCanvas();
            CreateStretchedImage(canvas.transform, "Background", new Color(0.03f, 0.03f, 0.04f, 1f));

            GameObject mainPanel = BuildMainPanel(canvas.transform, font, out Button play, out Button settings, out Button quit);
            GameObject settingsPanel = BuildSettingsPanel(canvas.transform, font, out SettingsPanel panelScript, out Button back);

            MainMenuController controller = canvas.gameObject.AddComponent<MainMenuController>();
            SetObjectField(controller, "mainPanel", mainPanel);
            SetObjectField(controller, "settingsPanel", settingsPanel);
            SetStringField(controller, "gameSceneName", "Demo");

            UnityEventTools.AddPersistentListener(play.onClick, controller.OnPlay);
            UnityEventTools.AddPersistentListener(settings.onClick, controller.OnOpenSettings);
            UnityEventTools.AddPersistentListener(quit.onClick, controller.OnQuit);
            UnityEventTools.AddPersistentListener(back.onClick, controller.OnCloseSettings);

            settingsPanel.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterScenes();

            AssetDatabase.SaveAssets();
            Debug.Log("[FearMe] Main menu built at " + ScenePath +
                ". It is now scene 0, so the game boots into the menu. Preferences save to " +
                GameSettingsService.FileName + " under the player's persistent data path.");
        }

        private static void BuildCamera()
        {
            GameObject camGO = new GameObject("MenuCamera");
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";
        }

        private static void BuildEventSystem()
        {
            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();

            // This project uses the Input System package, so the legacy
            // StandaloneInputModule would never receive a click.
            InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions != null) module.actionsAsset = actions;
        }

        private static Canvas BuildCanvas()
        {
            GameObject go = new GameObject("MenuCanvas");
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static GameObject BuildMainPanel(Transform parent, Font font,
            out Button play, out Button settings, out Button quit)
        {
            RectTransform panel = CreateRect(parent, "MainPanel", new Vector2(600f, 640f), Vector2.zero);

            CreateText(panel, "Title", "FEAR ME", font, 84, TextAnchor.MiddleCenter, Ink,
                new Vector2(560f, 110f), new Vector2(0f, 210f));
            CreateText(panel, "Subtitle", "ST. ALDEN MEMORIAL  ·  FIRST FLOOR", font, 20,
                TextAnchor.MiddleCenter, Dim, new Vector2(560f, 34f), new Vector2(0f, 150f));

            play = CreateButton(panel, "PlayButton", "PLAY", font, new Vector2(0f, 40f));
            settings = CreateButton(panel, "SettingsButton", "SETTINGS", font, new Vector2(0f, -30f));
            quit = CreateButton(panel, "QuitButton", "QUIT", font, new Vector2(0f, -100f));

            CreateText(panel, "Hint", "WASD move  ·  Shift run  ·  C crouch  ·  F flashlight  ·  E interact",
                font, 16, TextAnchor.MiddleCenter, Dim, new Vector2(600f, 30f), new Vector2(0f, -220f));

            return panel.gameObject;
        }

        private static GameObject BuildSettingsPanel(Transform parent, Font font,
            out SettingsPanel panelScript, out Button back)
        {
            RectTransform panel = CreateRect(parent, "SettingsPanel", new Vector2(760f, 560f), Vector2.zero);
            CreateStretchedImage(panel, "PanelBackground", PanelColor).transform.SetAsFirstSibling();

            CreateText(panel, "Header", "SETTINGS", font, 42, TextAnchor.MiddleCenter, Ink,
                new Vector2(700f, 60f), new Vector2(0f, 220f));

            Slider master = CreateSliderRow(panel, "Master", "MASTER VOLUME", font, 130f, 0f, 1f, out Text masterValue);
            Slider ambient = CreateSliderRow(panel, "Ambient", "MUSIC & AMBIENCE", font, 60f, 0f, 1f, out Text ambientValue);
            Slider sfx = CreateSliderRow(panel, "Sfx", "SOUND EFFECTS", font, -10f, 0f, 1f, out Text sfxValue);
            Slider sensitivity = CreateSliderRow(panel, "Sensitivity", "LOOK SENSITIVITY", font, -80f, 0.02f, 0.6f, out Text sensitivityValue);

            Toggle invert = CreateToggleRow(panel, "Invert", "INVERT VERTICAL LOOK", font, -150f);

            back = CreateButton(panel, "BackButton", "BACK", font, new Vector2(0f, -225f));

            panelScript = panel.gameObject.AddComponent<SettingsPanel>();
            SetObjectField(panelScript, "masterSlider", master);
            SetObjectField(panelScript, "ambientSlider", ambient);
            SetObjectField(panelScript, "sfxSlider", sfx);
            SetObjectField(panelScript, "sensitivitySlider", sensitivity);
            SetObjectField(panelScript, "invertToggle", invert);
            SetObjectField(panelScript, "masterValue", masterValue);
            SetObjectField(panelScript, "ambientValue", ambientValue);
            SetObjectField(panelScript, "sfxValue", sfxValue);
            SetObjectField(panelScript, "sensitivityValue", sensitivityValue);

            return panel.gameObject;
        }

        private static Slider CreateSliderRow(Transform parent, string name, string label, Font font,
            float y, float min, float max, out Text valueLabel)
        {
            CreateText(parent, name + "Label", label, font, 20, TextAnchor.MiddleLeft, Ink,
                new Vector2(260f, 30f), new Vector2(-230f, y));

            Slider slider = CreateSlider(parent, name + "Slider", new Vector2(300f, 18f), new Vector2(70f, y), min, max);

            valueLabel = CreateText(parent, name + "Value", "-", font, 20, TextAnchor.MiddleRight, Dim,
                new Vector2(90f, 30f), new Vector2(295f, y));

            return slider;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 size, Vector2 position,
            float min, float max)
        {
            RectTransform root = CreateRect(parent, name, size, position);
            Slider slider = root.gameObject.AddComponent<Slider>();

            CreateStretchedImage(root, "Background", new Color(0.2f, 0.2f, 0.22f, 1f));

            RectTransform fillArea = CreateStretchedRect(root, "Fill Area", 6f, 6f);
            Image fill = CreateStretchedImage(fillArea, "Fill", Accent);

            RectTransform handleArea = CreateStretchedRect(root, "Handle Slide Area", 6f, 6f);
            RectTransform handle = CreateRect(handleArea, "Handle", new Vector2(16f, 26f), Vector2.zero);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Ink;

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = Mathf.Lerp(min, max, 0.75f);

            return slider;
        }

        private static Toggle CreateToggleRow(Transform parent, string name, string label, Font font, float y)
        {
            CreateText(parent, name + "Label", label, font, 20, TextAnchor.MiddleLeft, Ink,
                new Vector2(300f, 30f), new Vector2(-230f, y));

            RectTransform root = CreateRect(parent, name + "Toggle", new Vector2(26f, 26f), new Vector2(70f, y));
            Toggle toggle = root.gameObject.AddComponent<Toggle>();

            Image background = CreateStretchedImage(root, "Background", new Color(0.2f, 0.2f, 0.22f, 1f));
            RectTransform check = CreateStretchedRect(root, "Checkmark", 5f, 5f);
            Image checkImage = check.gameObject.AddComponent<Image>();
            checkImage.color = Accent;

            toggle.targetGraphic = background;
            toggle.graphic = checkImage;
            toggle.isOn = false;

            return toggle;
        }

        private static Button CreateButton(Transform parent, string name, string label, Font font, Vector2 position)
        {
            RectTransform root = CreateRect(parent, name, new Vector2(320f, 56f), position);

            Image image = root.gameObject.AddComponent<Image>();
            image.color = ButtonColor;

            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.85f, 0.85f, 1f);
            colors.pressedColor = new Color(0.65f, 0.6f, 0.6f, 1f);
            button.colors = colors;

            Text text = CreateText(root, "Label", label, font, 26, TextAnchor.MiddleCenter, Ink,
                new Vector2(300f, 40f), Vector2.zero);
            text.raycastTarget = false;

            return button;
        }

        private static Text CreateText(Transform parent, string name, string content, Font font, int size,
            TextAnchor anchor, Color color, Vector2 rectSize, Vector2 position)
        {
            RectTransform rect = CreateRect(parent, name, rectSize, position);
            Text text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static RectTransform CreateRect(Transform parent, string name, Vector2 size, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static RectTransform CreateStretchedRect(Transform parent, string name, float horizontal, float vertical)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
            return rect;
        }

        private static Image CreateStretchedImage(Transform parent, string name, Color color)
        {
            RectTransform rect = CreateStretchedRect(parent, name, 0f, 0f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        // Unity renamed the built-in font; try both, then anything in the project.
        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) return font;

            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null) return font;

            foreach (string guid in AssetDatabase.FindAssets("t:Font"))
            {
                font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null) return font;
            }

            Debug.LogWarning("[FearMe] No font found; menu labels will be blank until one is assigned.");
            return null;
        }

        // Menu first so the game boots into it, gameplay scene after.
        private static void RegisterScenes()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path == ScenePath) continue;
                scenes.Add(existing);
            }

            bool hasGameScene = scenes.Exists(s => s.path == GameScenePath);
            if (!hasGameScene && System.IO.File.Exists(GameScenePath))
                scenes.Add(new EditorBuildSettingsScene(GameScenePath, true));

            EditorBuildSettings.scenes = scenes.ToArray();
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

        private static void SetStringField(Object target, string fieldName, string value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            prop.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
