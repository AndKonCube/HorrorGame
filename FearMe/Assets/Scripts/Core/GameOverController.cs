using System.Collections;
using FearMe.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FearMe.Core
{
    // Ends the run. Being caught drops you back into the level to try again;
    // escaping ends the demo, thanks the player and returns to the menu.
    public class GameOverController : MonoBehaviour
    {
        [SerializeField] private CanvasGroup fadeCanvasGroup;
        [SerializeField] private AudioSource jumpscareAudio;
        [SerializeField] private PlayerInput playerInputToDisable;
        [SerializeField] private float fadeDuration = 0.6f;
        [SerializeField] private float delayBeforeReload = 2.5f;

        [Header("Demo end")]
        [SerializeField] private string menuSceneName = "MainMenu";
        [SerializeField] private float thankYouDuration = 5f;

        public bool IsFinished { get; private set; }
        public bool DidEscape { get; private set; }

        public void OnPlayerCaught()
        {
            if (IsFinished) return;

            // Online the run has to end on both machines at once, so the
            // server says so and this comes back as ApplyCaught.
            if (CoopHooks.RunEnded != null && CoopHooks.RunEnded(false)) return;

            ApplyCaught();
        }

        public void ApplyCaught()
        {
            if (IsFinished) return;
            IsFinished = true;
            DidEscape = false;

            if (jumpscareAudio != null) jumpscareAudio.Play();
            StartCoroutine(CaughtSequence());
        }

        public void OnPlayerEscaped()
        {
            if (IsFinished) return;

            if (CoopHooks.RunEnded != null && CoopHooks.RunEnded(true)) return;

            ApplyEscaped();
        }

        public void ApplyEscaped()
        {
            if (IsFinished) return;
            IsFinished = true;
            DidEscape = true;

            StartCoroutine(EscapeSequence());
        }

        private IEnumerator CaughtSequence()
        {
            yield return FadeOut(fadeDuration);
            yield return new WaitForSeconds(delayBeforeReload);

            // Straight back into the level for another attempt - online, the
            // host reloads it for everyone.
            if (CoopHooks.RestartRequested != null && CoopHooks.RestartRequested()) yield break;

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private IEnumerator EscapeSequence()
        {
            yield return FadeOut(1.5f);

            // Held on black while the HUD shows the thank-you card.
            yield return new WaitForSeconds(thankYouDuration);
            yield return LeaveSession();
            LoadMenu();
        }

        private IEnumerator FadeOut(float duration)
        {
            HandOverControl();

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (fadeCanvasGroup != null)
                    fadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
                yield return null;
            }
        }

        private void HandOverControl()
        {
            if (playerInputToDisable != null) playerInputToDisable.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // The run is over for both players: leave the session and let its
        // network shut down before the menu loads, so the two never overlap.
        // Capped, so a slow service cannot strand anyone on a black screen.
        private static IEnumerator LeaveSession()
        {
            if (CoopSession.State == SessionState.Offline) yield break;

            CoopSession.Leave();

            float giveUpAt = Time.unscaledTime + 6f;
            while (CoopSession.State != SessionState.Offline && Time.unscaledTime < giveUpAt)
                yield return null;
        }

        private void LoadMenu()
        {
            // A scene missing from the build list would hard-fail, so check first.
            if (!string.IsNullOrEmpty(menuSceneName) && Application.CanStreamedLevelBeLoaded(menuSceneName))
            {
                SceneManager.LoadScene(menuSceneName);
                return;
            }

            Debug.LogWarning("[FearMe] Scene '" + menuSceneName +
                "' is not in the build list; build the main menu scene to return to it.");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
