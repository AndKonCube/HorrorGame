using FearMe.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FearMe.EditorTools
{
    // Builds the co-op lobby: name, host or join by code, who is in, ready up,
    // start. Kept apart from the menu builder so it can be dropped into a
    // MainMenu scene that has already been tweaked by hand.
    public static class LobbyPanelBuilder
    {
        private const float PanelWidth = 860f;
        private const float PanelHeight = 640f;

        // Non-destructive: it adds to whatever menu scene is open rather than
        // rebuilding it, because the menu gets hand-adjusted.
        [MenuItem("Tools/FearMe/Add Lobby Panel (current scene)")]
        public static void AddLobbyPanelToCurrentScene()
        {
            MainMenuController controller = Object.FindFirstObjectByType<MainMenuController>();
            if (controller == null)
            {
                Debug.LogError("[FearMe] No MainMenuController in this scene. " +
                    "Open Assets/Scenes/MainMenu.unity, or build it with Tools/FearMe/Build Main Menu Scene.");
                return;
            }

            if (Object.FindFirstObjectByType<LobbyPanel>() != null)
            {
                Debug.LogWarning("[FearMe] This scene already has a LobbyPanel. " +
                    "Delete it first if you want a fresh one.");
                return;
            }

            Canvas canvas = controller.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[FearMe] No Canvas in this scene to parent the lobby to.");
                return;
            }

            Font font = MainMenuBuilder.ResolveFont();
            GameObject panel = Build(canvas.transform, font, out Button back);

            MainMenuBuilder.SetObjectField(controller, "lobbyPanel", panel);
            UnityEventTools.AddPersistentListener(back.onClick, controller.OnCloseLobby);

            Button coop = AddCoopButtonIfMissing(controller, font);
            if (coop != null) UnityEventTools.AddPersistentListener(coop.onClick, controller.OnOpenLobby);

            panel.SetActive(false);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[FearMe] Lobby panel added. " + (coop != null
                ? "A CO-OP button was placed under QUIT - drag it where you want it, then save the scene."
                : "Wire your own button to MainMenuController.OnOpenLobby, then save the scene."));
        }

        // Only if the menu has no way in yet, and placed clear of the existing
        // buttons rather than shuffling a layout the player may have arranged.
        private static Button AddCoopButtonIfMissing(MainMenuController controller, Font font)
        {
            Transform mainPanel = FindMainPanel(controller);
            if (mainPanel == null) return null;

            if (mainPanel.Find("CoopButton") != null) return null;

            return MainMenuBuilder.CreateButton(mainPanel, "CoopButton", "CO-OP", font, new Vector2(0f, -170f));
        }

        private static Transform FindMainPanel(MainMenuController controller)
        {
            SerializedObject so = new SerializedObject(controller);
            SerializedProperty prop = so.FindProperty("mainPanel");
            GameObject panel = prop != null ? prop.objectReferenceValue as GameObject : null;
            return panel != null ? panel.transform : null;
        }

        internal static GameObject Build(Transform parent, Font font, out Button back)
        {
            RectTransform panel = MainMenuBuilder.CreateRect(parent, "LobbyPanel",
                new Vector2(PanelWidth, PanelHeight), Vector2.zero);
            MainMenuBuilder.CreateStretchedImage(panel, "PanelBackground", MainMenuBuilder.PanelColor)
                .transform.SetAsFirstSibling();

            MainMenuBuilder.CreateText(panel, "Header", "CO-OP", font, 42, TextAnchor.MiddleCenter,
                MainMenuBuilder.Ink, new Vector2(760f, 60f), new Vector2(0f, 262f));

            // Before joining: who you are, and the code to join with.
            InputField nameField = CreateLabelledField(panel, "Name", "YOUR NAME", font, 190f, "PLAYER");
            InputField codeField = CreateLabelledField(panel, "JoinCode", "JOIN CODE", font, 125f, "ABC123");

            Button host = MainMenuBuilder.CreateButton(panel, "HostButton", "HOST A RUN", font, new Vector2(-170f, 50f));
            Button join = MainMenuBuilder.CreateButton(panel, "JoinButton", "JOIN", font, new Vector2(170f, 50f));

            // Once in: the code to share, the slots, and the way out.
            Text codeLabel = MainMenuBuilder.CreateText(panel, "JoinCodeLabel", "CODE  ------", font, 34,
                TextAnchor.MiddleCenter, MainMenuBuilder.Ink, new Vector2(520f, 46f), new Vector2(-60f, 200f));

            Button copy = MainMenuBuilder.CreateButton(panel, "CopyCodeButton", "COPY", font, new Vector2(290f, 200f));
            Resize(copy, new Vector2(150f, 46f));

            Text slotOne = CreateSlot(panel, "SlotOne", font, 110f);
            Text slotTwo = CreateSlot(panel, "SlotTwo", font, 55f);

            Button ready = MainMenuBuilder.CreateButton(panel, "ReadyButton", "READY", font, new Vector2(0f, -30f));
            Button start = MainMenuBuilder.CreateButton(panel, "StartButton", "START RUN", font, new Vector2(0f, -30f));
            Button leave = MainMenuBuilder.CreateButton(panel, "LeaveButton", "LEAVE LOBBY", font, new Vector2(0f, -105f));

            back = MainMenuBuilder.CreateButton(panel, "BackButton", "BACK", font, new Vector2(0f, -180f));

            // Errors and hints land here, so it has to wrap.
            Text status = MainMenuBuilder.CreateText(panel, "StatusLabel", string.Empty, font, 18,
                TextAnchor.UpperCenter, MainMenuBuilder.Dim, new Vector2(780f, 70f), new Vector2(0f, -255f));
            status.horizontalOverflow = HorizontalWrapMode.Wrap;

            LobbyPanel lobby = panel.gameObject.AddComponent<LobbyPanel>();
            MainMenuBuilder.SetStringField(lobby, "gameSceneName", "Demo");
            MainMenuBuilder.SetObjectField(lobby, "nameField", nameField);
            MainMenuBuilder.SetObjectField(lobby, "joinCodeField", codeField);
            MainMenuBuilder.SetObjectField(lobby, "hostButton", host);
            MainMenuBuilder.SetObjectField(lobby, "joinButton", join);
            MainMenuBuilder.SetObjectField(lobby, "joinCodeLabel", codeLabel);
            MainMenuBuilder.SetObjectField(lobby, "copyCodeButton", copy);
            MainMenuBuilder.SetObjectField(lobby, "readyButton", ready);
            MainMenuBuilder.SetObjectField(lobby, "readyButtonLabel", LabelOf(ready));
            MainMenuBuilder.SetObjectField(lobby, "startButton", start);
            MainMenuBuilder.SetObjectField(lobby, "startButtonLabel", LabelOf(start));
            MainMenuBuilder.SetObjectField(lobby, "leaveButton", leave);
            MainMenuBuilder.SetObjectField(lobby, "statusLabel", status);
            MainMenuBuilder.SetObjectArrayField(lobby, "slotLabels", new Object[] { slotOne, slotTwo });

            UnityEventTools.AddPersistentListener(host.onClick, lobby.OnHost);
            UnityEventTools.AddPersistentListener(join.onClick, lobby.OnJoin);
            UnityEventTools.AddPersistentListener(copy.onClick, lobby.OnCopyJoinCode);
            UnityEventTools.AddPersistentListener(ready.onClick, lobby.OnToggleReady);
            UnityEventTools.AddPersistentListener(start.onClick, lobby.OnStart);
            UnityEventTools.AddPersistentListener(leave.onClick, lobby.OnLeave);

            return panel.gameObject;
        }

        private static Text CreateSlot(Transform parent, string name, Font font, float y)
        {
            RectTransform row = MainMenuBuilder.CreateRect(parent, name, new Vector2(700f, 46f), new Vector2(0f, y));
            MainMenuBuilder.CreateStretchedImage(row, "RowBackground", new Color(0.12f, 0.12f, 0.14f, 1f));

            return MainMenuBuilder.CreateText(row, "Label", "EMPTY", font, 22, TextAnchor.MiddleLeft,
                MainMenuBuilder.Ink, new Vector2(660f, 40f), new Vector2(10f, 0f));
        }

        private static InputField CreateLabelledField(Transform parent, string name, string label, Font font,
            float y, string placeholder)
        {
            MainMenuBuilder.CreateText(parent, name + "Label", label, font, 20, TextAnchor.MiddleLeft,
                MainMenuBuilder.Ink, new Vector2(260f, 30f), new Vector2(-270f, y));

            return CreateInputField(parent, name + "Field", font, new Vector2(380f, 46f),
                new Vector2(140f, y), placeholder);
        }

        // Legacy InputField, to match the sliders the settings panel already uses.
        private static InputField CreateInputField(Transform parent, string name, Font font, Vector2 size,
            Vector2 position, string placeholder)
        {
            RectTransform root = MainMenuBuilder.CreateRect(parent, name, size, position);

            Image background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.14f, 0.14f, 0.16f, 1f);

            // The text sits inside a clipped child so a long name does not
            // spill past the box.
            RectTransform area = MainMenuBuilder.CreateStretchedRect(root, "Text Area", 10f, 6f);
            area.gameObject.AddComponent<RectMask2D>();

            Text hint = MainMenuBuilder.CreateText(area, "Placeholder", placeholder, font, 20,
                TextAnchor.MiddleLeft, new Color(0.42f, 0.41f, 0.4f), size, Vector2.zero);
            Stretch(hint.rectTransform);

            Text text = MainMenuBuilder.CreateText(area, "Text", string.Empty, font, 20,
                TextAnchor.MiddleLeft, MainMenuBuilder.Ink, size, Vector2.zero);
            Stretch(text.rectTransform);
            text.supportRichText = false;

            InputField field = root.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = InputField.LineType.SingleLine;

            return field;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Resize(Button button, Vector2 size)
        {
            ((RectTransform)button.transform).sizeDelta = size;

            Transform label = button.transform.Find("Label");
            if (label != null) ((RectTransform)label).sizeDelta = size - new Vector2(20f, 16f);
        }

        private static Text LabelOf(Button button)
        {
            Transform label = button.transform.Find("Label");
            return label != null ? label.GetComponent<Text>() : null;
        }
    }
}
