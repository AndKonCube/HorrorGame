using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    // IMGUI on purpose: no font assets or TextMeshPro import needed, so the
    // demo scene works the moment it is generated.
    public class DemoHUD : MonoBehaviour
    {
        [SerializeField] private ObjectiveTracker objectives;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private GameOverController gameFlow;

        private GUIStyle textStyle;
        private GUIStyle centerStyle;

        private void BuildStyles()
        {
            if (textStyle != null) return;

            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperLeft
            };
            textStyle.normal.textColor = new Color(0.85f, 0.83f, 0.8f);

            centerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 42,
                alignment = TextAnchor.MiddleCenter
            };
            centerStyle.normal.textColor = new Color(0.8f, 0.1f, 0.1f);
        }

        private void OnGUI()
        {
            BuildStyles();

            if (gameFlow != null && gameFlow.IsFinished)
            {
                centerStyle.normal.textColor = gameFlow.DidEscape
                    ? new Color(0.8f, 0.78f, 0.7f)
                    : new Color(0.75f, 0.08f, 0.08f);

                GUI.Label(
                    new Rect(0f, 0f, Screen.width, Screen.height),
                    gameFlow.DidEscape ? "YOU ESCAPED" : "IT FOUND YOU",
                    centerStyle);
                return;
            }

            if (objectives != null)
            {
                string status = objectives.AllKeysCollected
                    ? "All keys found - get to the door"
                    : $"Keys {objectives.KeysCollected} / {objectives.KeysRequired}";
                GUI.Label(new Rect(24f, 20f, 460f, 30f), status, textStyle);
            }

            GUI.Label(new Rect(24f, 48f, 460f, 30f), "F flashlight - C crouch - Shift run", textStyle);

            // Crosshair
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (interactor != null && interactor.CurrentTarget != null)
            {
                GUI.Label(
                    new Rect(cx - 200f, cy + 28f, 400f, 30f),
                    interactor.CurrentTarget.Prompt,
                    new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter });
            }
        }
    }
}
