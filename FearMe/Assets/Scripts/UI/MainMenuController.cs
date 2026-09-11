using FearMe.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FearMe.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private string gameSceneName = "Demo";
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject settingsPanel;

        private void Start()
        {
            // Gameplay locks the cursor; the menu needs it back.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ShowMain();
        }

        public void OnPlay()
        {
            SceneManager.LoadScene(gameSceneName);
        }

        public void OnOpenSettings()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(true);
        }

        public void OnCloseSettings()
        {
            GameSettingsService.Save();
            ShowMain();
        }

        public void OnQuit()
        {
            GameSettingsService.Save();
            Application.Quit();
        }

        private void ShowMain()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (mainPanel != null) mainPanel.SetActive(true);
        }
    }
}
