using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FearMe.Core
{
    // Handles the catch -> jumpscare -> reload sequence. Wire
    // EnemyStalkerAI's "On Player Caught" UnityEvent to OnPlayerCaught().
    public class GameOverController : MonoBehaviour
    {
        [SerializeField] private CanvasGroup fadeCanvasGroup;
        [SerializeField] private AudioSource jumpscareAudio;
        [SerializeField] private PlayerInput playerInputToDisable;
        [SerializeField] private float fadeDuration = 0.6f;
        [SerializeField] private float delayBeforeReload = 1.6f;

        private bool triggered;

        public void OnPlayerCaught()
        {
            if (triggered) return;
            triggered = true;
            StartCoroutine(CaughtSequence());
        }

        private IEnumerator CaughtSequence()
        {
            if (playerInputToDisable != null) playerInputToDisable.enabled = false;
            if (jumpscareAudio != null) jumpscareAudio.Play();

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                if (fadeCanvasGroup != null)
                    fadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
                yield return null;
            }

            yield return new WaitForSeconds(delayBeforeReload);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
