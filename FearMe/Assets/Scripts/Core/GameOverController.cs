using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FearMe.Core
{
    // Ends the run, either by being caught or by escaping, then reloads.
    // EnemyStalkerAI's onPlayerCaught event calls OnPlayerCaught().
    public class GameOverController : MonoBehaviour
    {
        [SerializeField] private CanvasGroup fadeCanvasGroup;
        [SerializeField] private AudioSource jumpscareAudio;
        [SerializeField] private PlayerInput playerInputToDisable;
        [SerializeField] private float fadeDuration = 0.6f;
        [SerializeField] private float delayBeforeReload = 2.5f;

        public bool IsFinished { get; private set; }
        public bool DidEscape { get; private set; }

        public void OnPlayerCaught()
        {
            if (IsFinished) return;
            IsFinished = true;
            DidEscape = false;
            if (jumpscareAudio != null) jumpscareAudio.Play();
            StartCoroutine(EndSequence(fadeDuration));
        }

        public void OnPlayerEscaped()
        {
            if (IsFinished) return;
            IsFinished = true;
            DidEscape = true;
            StartCoroutine(EndSequence(1.5f));
        }

        private IEnumerator EndSequence(float duration)
        {
            if (playerInputToDisable != null) playerInputToDisable.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (fadeCanvasGroup != null)
                    fadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
                yield return null;
            }

            yield return new WaitForSeconds(delayBeforeReload);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
