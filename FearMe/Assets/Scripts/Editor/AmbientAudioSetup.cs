using System.Collections.Generic;
using FearMe.Core;
using FearMe.Scares;
using FearMe.Settings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Wires ambient audio into whatever scene is already open, so hand-placed
    // props and lighting survive. Clip assignment is a guess from file names -
    // swap them in the Inspector once you have heard them.
    public static class AmbientAudioSetup
    {
        private const string AudioFolder = "Assets/Audio";

        // Names that suggest a driving or stinger-like track rather than a pad.
        private static readonly string[] TenseKeywords = { "action", "getout", "re8", "conjuring", "horrormane" };
        private static readonly string[] SkipKeywords = { "typewriter" };

        [MenuItem("Tools/FearMe/Wire Ambient Audio (current scene)")]
        public static void WireCurrentScene()
        {
            AmbientAudioController controller = Object.FindFirstObjectByType<AmbientAudioController>();
            if (controller == null) controller = CreateController();

            Configure(controller, Object.FindFirstObjectByType<ScareDirector>());
            SilenceStrayStartupAudio(controller);
            TagEffectSources(controller);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[FearMe] Ambient audio wired. Save the scene to keep it.");
        }

        internal static AmbientAudioController CreateController()
        {
            GameObject root = new GameObject("AmbientAudio");

            AudioSource bed = CreateSource(root.transform, "AmbientBed", loop: false);
            AudioSource tension = CreateSource(root.transform, "TensionLayer", loop: true);

            AmbientAudioController controller = root.AddComponent<AmbientAudioController>();
            SetObjectField(controller, "bedSource", bed);
            SetObjectField(controller, "tensionSource", tension);
            return controller;
        }

        private static AudioSource CreateSource(Transform parent, string name, bool loop)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f; // 2D: atmosphere should not have a direction
            source.volume = 0f;
            return source;
        }

        internal static void Configure(AmbientAudioController controller, ScareDirector director)
        {
            if (director != null) SetObjectField(controller, "director", director);
            AssignClips(controller);
        }

        private static void AssignClips(AmbientAudioController controller)
        {
            if (!AssetDatabase.IsValidFolder(AudioFolder))
            {
                Debug.LogWarning("[FearMe] No " + AudioFolder + " folder; assign ambient clips by hand.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder });
            AudioClip stinger = FindStinger();

            List<Object> calm = new List<Object>();
            List<Object> tense = new List<Object>();

            foreach (string guid in guids)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;

                // The jumpscare clip must not double as atmosphere.
                if (clip == stinger) continue;

                string name = clip.name.ToLowerInvariant();
                if (MatchesAny(name, SkipKeywords)) continue;

                if (MatchesAny(name, TenseKeywords)) tense.Add(clip);
                else calm.Add(clip);
            }

            SetObjectArrayField(controller, "calmTracks", calm.ToArray());
            SetObjectArrayField(controller, "tensionTracks", tense.ToArray());

            Debug.Log($"[FearMe] Ambient bed: {calm.Count} track(s), tension layer: {tense.Count} track(s).");
        }

        private static AudioClip FindStinger()
        {
            GameOverController flow = Object.FindFirstObjectByType<GameOverController>();
            if (flow == null) return null;

            AudioSource source = flow.GetComponent<AudioSource>();
            return source != null ? source.clip : null;
        }

        // A jumpscare clip set to play on awake fires the scare at startup,
        // which is what makes the level sound like it has no atmosphere.
        private static void SilenceStrayStartupAudio(AmbientAudioController controller)
        {
            AudioClip stinger = FindStinger();
            AudioSource[] sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);

            foreach (AudioSource source in sources)
            {
                if (source == null || !source.playOnAwake) continue;
                if (source.GetComponentInParent<AmbientAudioController>() == controller) continue;

                if (stinger != null && source.clip == stinger)
                {
                    source.playOnAwake = false;
                    Debug.LogWarning("[FearMe] Turned off Play On Awake on '" + source.name +
                        "': it was firing the jumpscare clip at startup. The ambient controller handles atmosphere now.");
                }
                else
                {
                    Debug.LogWarning("[FearMe] '" + source.name +
                        "' also plays on awake; delete it if it is a leftover ambience attempt.");
                }
            }
        }

        // Everything that is not the ambient controller's own pair counts as an
        // effect, so the SFX slider reaches it.
        private static void TagEffectSources(AmbientAudioController controller)
        {
            int tagged = 0;

            foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (source == null) continue;
                if (source.GetComponentInParent<AmbientAudioController>() == controller) continue;
                if (source.GetComponent<AudioCategoryVolume>() != null) continue;

                source.gameObject.AddComponent<AudioCategoryVolume>();
                tagged++;
            }

            if (tagged > 0)
                Debug.Log($"[FearMe] Put {tagged} effect source(s) under the SFX volume setting.");
        }

        private static bool MatchesAny(string name, string[] keywords)
        {
            foreach (string keyword in keywords)
            {
                if (name.Contains(keyword)) return true;
            }
            return false;
        }

        private static void SetObjectField(Object target, string fieldName, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArrayField(Object target, string fieldName, Object[] values)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null) return;

            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
