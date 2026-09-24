using UnityEngine;

namespace FearMe.Settings
{
    // Serialized straight to JSON by JsonUtility, so keep it plain fields.
    [System.Serializable]
    public class GameSettings
    {
        public float masterVolume = 0.9f;
        public float ambientVolume = 0.7f;
        public float sfxVolume = 1f;
        public float mouseSensitivity = 0.12f;
        public bool invertLook;

        // Shown to the other player in the lobby, so it is a preference like
        // any other and lives in the same file.
        public string playerName = "PLAYER";

        // The file can be hand-edited or written by an older build, so never
        // trust what comes back off disk.
        public void Clamp()
        {
            masterVolume = Mathf.Clamp01(masterVolume);
            ambientVolume = Mathf.Clamp01(ambientVolume);
            sfxVolume = Mathf.Clamp01(sfxVolume);
            mouseSensitivity = Mathf.Clamp(mouseSensitivity, 0.02f, 0.6f);

            playerName = string.IsNullOrWhiteSpace(playerName) ? "PLAYER" : playerName.Trim();
            if (playerName.Length > 16) playerName = playerName.Substring(0, 16);
        }
    }
}
