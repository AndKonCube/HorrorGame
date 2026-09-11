using System;
using System.IO;
using UnityEngine;

namespace FearMe.Settings
{
    // Player preferences, held once and written to
    // <persistentDataPath>/settings.json. Loads before the first scene so
    // audio never plays a frame at the wrong volume.
    public static class SettingsService
    {
        public const string FileName = "settings.json";

        private static GameSettings current;

        public static event Action Changed;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static GameSettings Current
        {
            get
            {
                if (current == null) Load();
                return current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialise()
        {
            Load();
        }

        public static void Load()
        {
            current = ReadFromDisk();
            current.Clamp();
            Apply();
        }

        private static GameSettings ReadFromDisk()
        {
            try
            {
                if (!File.Exists(FilePath)) return new GameSettings();

                string json = File.ReadAllText(FilePath);
                GameSettings loaded = JsonUtility.FromJson<GameSettings>(json);
                return loaded ?? new GameSettings();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FearMe] Could not read settings, using defaults: " + exception.Message);
                return new GameSettings();
            }
        }

        public static void Save()
        {
            Current.Clamp();

            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FearMe] Could not write settings: " + exception.Message);
            }

            Apply();
        }

        // Call after changing Current so listeners and the mixer keep up.
        public static void Apply()
        {
            AudioListener.volume = Current.masterVolume;
            Changed?.Invoke();
        }

        public static void ResetToDefaults()
        {
            current = new GameSettings();
            Save();
        }
    }
}
