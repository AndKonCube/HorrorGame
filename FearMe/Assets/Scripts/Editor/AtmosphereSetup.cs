using FearMe.Core;
using FearMe.Player;
using FearMe.Scares;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Builds the look and the body-cam feel into whatever scene is open:
    // post-processing grade, tension response, head bob and breathing.
    public static class AtmosphereSetup
    {
        private const string ProfilePath = "Assets/Settings/HorrorPostFx.asset";

        [MenuItem("Tools/FearMe/Wire Atmosphere (current scene)")]
        public static void WireCurrentScene()
        {
            VolumeProfile profile = GetOrCreateProfile();
            Volume volume = GetOrCreateVolume(profile);

            EnablePostProcessingOnCameras();

            ScareDirector director = Object.FindFirstObjectByType<ScareDirector>();
            WireTensionDriver(volume, director);
            WirePlayer(director);
            WireSoundscape(director);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[FearMe] Atmosphere wired. Assign creak/step/breath clips on " +
                "DiegeticSoundscape and PlayerBreathing, then save the scene.");
        }

        private static VolumeProfile GetOrCreateProfile()
        {
            VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (existing != null) return existing;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            // Darkness first: pull exposure down and let the flashlight matter.
            ColorAdjustments colour = profile.Add<ColorAdjustments>(true);
            colour.postExposure.Override(-0.9f);
            colour.contrast.Override(15f);
            colour.saturation.Override(-25f);
            AssetDatabase.AddObjectToAsset(colour, profile);

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.5f);
            AssetDatabase.AddObjectToAsset(vignette, profile);

            // Body-cam grain and a little barrel distortion at the edges.
            FilmGrain grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.35f);
            grain.response.Override(0.75f);
            AssetDatabase.AddObjectToAsset(grain, profile);

            LensDistortion lens = profile.Add<LensDistortion>(true);
            lens.intensity.Override(-0.15f);
            lens.scale.Override(1.0f);
            AssetDatabase.AddObjectToAsset(lens, profile);

            ChromaticAberration aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.05f);
            AssetDatabase.AddObjectToAsset(aberration, profile);

            AssetDatabase.SaveAssets();
            Debug.Log("[FearMe] Created post-processing profile at " + ProfilePath);
            return profile;
        }

        private static Volume GetOrCreateVolume(VolumeProfile profile)
        {
            Volume volume = Object.FindFirstObjectByType<Volume>();
            if (volume == null)
            {
                GameObject go = new GameObject("PostProcessing");
                volume = go.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;
            return volume;
        }

        // Without this the whole grade is simply not drawn.
        private static void EnablePostProcessingOnCameras()
        {
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = true;
            }
        }

        private static void WireTensionDriver(Volume volume, ScareDirector director)
        {
            PostFxTensionDriver driver = Object.FindFirstObjectByType<PostFxTensionDriver>();
            if (driver == null) driver = volume.gameObject.AddComponent<PostFxTensionDriver>();

            SetObjectField(driver, "volume", volume);
            if (director != null) SetObjectField(driver, "director", director);
        }

        private static void WirePlayer(ScareDirector director)
        {
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null)
            {
                Debug.LogWarning("[FearMe] No player in this scene; skipped head bob and breathing.");
                return;
            }

            Camera camera = player.GetComponentInChildren<Camera>();
            if (camera != null && camera.GetComponent<HeadBob>() == null)
            {
                HeadBob bob = camera.gameObject.AddComponent<HeadBob>();
                SetObjectField(bob, "controller", player.GetComponent<CharacterController>());
                SetObjectField(bob, "player", player);
            }

            if (player.GetComponent<PlayerBreathing>() == null)
            {
                PlayerBreathing breathing = player.gameObject.AddComponent<PlayerBreathing>();
                SetObjectField(breathing, "player", player);
                if (director != null) SetObjectField(breathing, "director", director);
            }
        }

        private static void WireSoundscape(ScareDirector director)
        {
            DiegeticSoundscape soundscape = Object.FindFirstObjectByType<DiegeticSoundscape>();
            if (soundscape == null)
            {
                GameObject go = new GameObject("DiegeticSoundscape");
                soundscape = go.AddComponent<DiegeticSoundscape>();
            }

            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            if (player != null) SetObjectField(soundscape, "listener", player.transform);
            if (director != null) SetObjectField(soundscape, "director", director);
        }

        private static void SetObjectField(Object target, string fieldName, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning("[FearMe] Missing field '" + fieldName + "' on " + target.GetType().Name);
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
