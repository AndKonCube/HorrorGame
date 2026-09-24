using FearMe.Net;
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
        [SerializeField] private GameObject lobbyPanel;

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

        public void OnOpenLobby()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (lobbyPanel != null) lobbyPanel.SetActive(true);
        }

        // Backing out of the lobby has to drop the session too, or the next
        // visit reopens a lobby the player thinks they left.
        public void OnCloseLobby()
        {
            CoopSession.Leave();
            ShowMain();
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
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (mainPanel != null) mainPanel.SetActive(true);
        }
    }
}
