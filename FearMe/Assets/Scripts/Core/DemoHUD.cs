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

        private void DrawEndCard(bool escaped)
        {
            centerStyle.normal.textColor = escaped
                ? new Color(0.82f, 0.80f, 0.74f)
                : new Color(0.75f, 0.08f, 0.08f);

            float third = Screen.height / 3f;

            GUI.Label(
                new Rect(0f, third - 40f, Screen.width, 80f),
                escaped ? "YOU ESCAPED" : "IT FOUND YOU",
                centerStyle);

            if (!escaped) return;

            GUIStyle thanks = new GUIStyle(centerStyle) { fontSize = 26 };
            thanks.normal.textColor = new Color(0.7f, 0.68f, 0.64f);
            GUI.Label(
                new Rect(0f, third + 50f, Screen.width, 40f),
                "THANK YOU FOR PLAYING THE DEMO",
                thanks);

            GUIStyle note = new GUIStyle(centerStyle) { fontSize = 18 };
            note.normal.textColor = new Color(0.45f, 0.44f, 0.42f);
            GUI.Label(
                new Rect(0f, third + 100f, Screen.width, 30f),
                "returning to the menu",
                note);
        }

        // Your own bleed-out, and a nudge when a teammate is waiting for help.
        private void DrawVitals()
        {
            PlayerController local = PlayerRegistry.Local;
            if (local == null) return;

            PlayerVitals mine = local.GetComponent<PlayerVitals>();
            if (mine != null && mine.IsDown && !mine.IsDead)
            {
                int left = Mathf.CeilToInt(mine.BleedOutRemaining);
                string headline = mine.Captivity switch
                {
                    Captivity.Dragged => $"IT HAS YOU - {left}s",
                    Captivity.Caged => $"CAGED - {left}s",
                    _ => $"DOWN - {left}s"
                };
                string help = mine.Captivity switch
                {
                    Captivity.Dragged => "your partner has to stop it",
                    Captivity.Caged => "your partner has to break the lock",
                    _ => "your partner has to reach you"
                };

                GUIStyle downed = new GUIStyle(centerStyle) { fontSize = 30 };
                downed.normal.textColor = new Color(0.75f, 0.1f, 0.1f);
                GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 40f), headline, downed);

                GUIStyle sub = new GUIStyle(centerStyle) { fontSize = 18 };
                sub.normal.textColor = new Color(0.6f, 0.58f, 0.55f);
                GUI.Label(new Rect(0f, Screen.height * 0.62f + 40f, Screen.width, 30f), help, sub);
                return;
            }

            foreach (PlayerController other in PlayerRegistry.All)
            {
                if (other == null || other == local) continue;

                PlayerVitals vitals = other.GetComponent<PlayerVitals>();
                if (vitals == null || !vitals.IsDown || vitals.IsDead) continue;

                int left = Mathf.CeilToInt(vitals.BleedOutRemaining);
                string line = vitals.Captivity switch
                {
                    Captivity.Dragged => $"Your partner is being dragged away - stop it - {left}s",
                    Captivity.Caged => $"Your partner is caged - break the lock - {left}s",
                    _ => $"Your partner is down - {left}s"
                };

                GUI.Label(new Rect(24f, 76f, 700f, 30f), line, textStyle);
                return;
            }
        }

        private void DrawHeld()
        {
            PlayerController local = PlayerRegistry.Local;
            PlayerHands hands = local != null ? local.GetComponent<PlayerHands>() : null;
            if (hands == null || hands.Held == null) return;

            GUIStyle held = new GUIStyle(textStyle) { alignment = TextAnchor.LowerRight };
            GUI.Label(new Rect(0f, Screen.height - 50f, Screen.width - 24f, 30f),
                hands.Held.DisplayName.ToUpperInvariant() + "  ·  click to use  ·  G to drop", held);
        }

        // Only when the microphone is on and picking you up: a warning that
        // arrives before the stalker does.
        private void DrawVoice()
        {
            float level = MicrophoneNoise.Level;
            if (level <= 0f) return;

            GUIStyle voice = new GUIStyle(textStyle) { alignment = TextAnchor.LowerCenter };
            voice.normal.textColor = Color.Lerp(new Color(0.7f, 0.68f, 0.64f, 0.6f),
                new Color(0.85f, 0.15f, 0.12f, 1f), level);

            GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 30f),
                level > 0.6f ? "IT CAN HEAR YOU" : "your voice carries", voice);
        }

        private void OnGUI()
        {
            BuildStyles();

            if (gameFlow != null && gameFlow.IsFinished)
            {
                DrawEndCard(gameFlow.DidEscape);
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
            DrawHeld();

            DrawVitals();
            DrawVoice();

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
