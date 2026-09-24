using FearMe.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace FearMe.UI
{
    // Binds the sliders to the live settings. Changes are audible immediately
    // and written to disk when the panel closes, so dragging a volume slider
    // does not hit the file system on every frame.
    public class SettingsPanel : MonoBehaviour
    {
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider ambientSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private Toggle invertToggle;

        [Header("Voice")]
        [Tooltip("Optional. Lets the player's real voice make noise in the game.")]
        [SerializeField] private Toggle micToggle;
        [SerializeField] private Slider micSensitivitySlider;

        [Header("Voice chat")]
        [Tooltip("Optional. Proximity voice with your partner.")]
        [SerializeField] private Toggle voiceChatToggle;
        [SerializeField] private Toggle pushToTalkToggle;
        [SerializeField] private Slider voiceVolumeSlider;

        [Header("Value readouts")]
        [SerializeField] private Text masterValue;
        [SerializeField] private Text ambientValue;
        [SerializeField] private Text sfxValue;
        [SerializeField] private Text sensitivityValue;

        private bool binding;

        private void OnEnable()
        {
            GameSettings settings = GameSettingsService.Current;

            binding = true;
            if (masterSlider != null) masterSlider.value = settings.masterVolume;
            if (ambientSlider != null) ambientSlider.value = settings.ambientVolume;
            if (sfxSlider != null) sfxSlider.value = settings.sfxVolume;
            if (sensitivitySlider != null) sensitivitySlider.value = settings.mouseSensitivity;
            if (invertToggle != null) invertToggle.isOn = settings.invertLook;
            if (micToggle != null) micToggle.isOn = settings.micAttractsMonster;
            if (micSensitivitySlider != null) micSensitivitySlider.value = settings.micSensitivity;
            if (voiceChatToggle != null) voiceChatToggle.isOn = settings.voiceChatEnabled;
            if (pushToTalkToggle != null) pushToTalkToggle.isOn = settings.pushToTalk;
            if (voiceVolumeSlider != null) voiceVolumeSlider.value = settings.voiceVolume;
            binding = false;

            Listen(masterSlider, OnMasterChanged);
            Listen(ambientSlider, OnAmbientChanged);
            Listen(sfxSlider, OnSfxChanged);
            Listen(sensitivitySlider, OnSensitivityChanged);
            Listen(micSensitivitySlider, OnMicSensitivityChanged);
            Listen(voiceVolumeSlider, OnVoiceVolumeChanged);
            ListenToggle(voiceChatToggle, OnVoiceChatChanged);
            ListenToggle(pushToTalkToggle, OnPushToTalkChanged);

            if (micToggle != null)
            {
                micToggle.onValueChanged.RemoveListener(OnMicChanged);
                micToggle.onValueChanged.AddListener(OnMicChanged);
            }

            if (invertToggle != null)
            {
                invertToggle.onValueChanged.RemoveListener(OnInvertChanged);
                invertToggle.onValueChanged.AddListener(OnInvertChanged);
            }

            RefreshLabels();
        }

        private void OnDisable()
        {
            Unlisten(masterSlider, OnMasterChanged);
            Unlisten(ambientSlider, OnAmbientChanged);
            Unlisten(sfxSlider, OnSfxChanged);
            Unlisten(sensitivitySlider, OnSensitivityChanged);
            if (invertToggle != null) invertToggle.onValueChanged.RemoveListener(OnInvertChanged);
            Unlisten(micSensitivitySlider, OnMicSensitivityChanged);
            Unlisten(voiceVolumeSlider, OnVoiceVolumeChanged);
            if (voiceChatToggle != null) voiceChatToggle.onValueChanged.RemoveListener(OnVoiceChatChanged);
            if (pushToTalkToggle != null) pushToTalkToggle.onValueChanged.RemoveListener(OnPushToTalkChanged);
            if (micToggle != null) micToggle.onValueChanged.RemoveListener(OnMicChanged);
        }

        private static void Listen(Slider slider, UnityEngine.Events.UnityAction<float> handler)
        {
            if (slider == null) return;
            slider.onValueChanged.RemoveListener(handler);
            slider.onValueChanged.AddListener(handler);
        }

        private static void Unlisten(Slider slider, UnityEngine.Events.UnityAction<float> handler)
        {
            if (slider != null) slider.onValueChanged.RemoveListener(handler);
        }

        private void OnMasterChanged(float value)
        {
            if (binding) return;
            GameSettingsService.Current.masterVolume = value;
            GameSettingsService.Apply();
            RefreshLabels();
        }

        private void OnAmbientChanged(float value)
        {
            if (binding) return;
            GameSettingsService.Current.ambientVolume = value;
            GameSettingsService.Apply();
            RefreshLabels();
        }

        private void OnSfxChanged(float value)
        {
            if (binding) return;
            GameSettingsService.Current.sfxVolume = value;
            GameSettingsService.Apply();
            RefreshLabels();
        }

        private void OnSensitivityChanged(float value)
        {
            if (binding) return;
            GameSettingsService.Current.mouseSensitivity = value;
            GameSettingsService.Apply();
            RefreshLabels();
        }

        private void OnInvertChanged(bool value)
        {
            if (binding) return;
            GameSettingsService.Current.invertLook = value;
            GameSettingsService.Apply();
        }

        private static void ListenToggle(Toggle toggle, UnityEngine.Events.UnityAction<bool> handler)
        {
            if (toggle == null) return;
            toggle.onValueChanged.RemoveListener(handler);
            toggle.onValueChanged.AddListener(handler);
        }

        private void OnVoiceChatChanged(bool value)
        {
            if (binding) return;
            GameSettingsService.Current.voiceChatEnabled = value;
            GameSettingsService.Apply();
        }

        private void OnPushToTalkChanged(bool value)
        {
            if (binding) return;
            GameSettingsService.Current.pushToTalk = value;
            GameSettingsService.Apply();
        }

        private void OnVoiceVolumeChanged(float value)
        {
            if (binding) return;
            GameSettingsService.Current.voiceVolume = value;
            GameSettingsService.Apply();
        }

        private void OnMicChanged(bool value)
        {
            if (binding) return;
            GameSettingsService.Current.micAttractsMonster = value;
            GameSettingsService.Apply();
        }

        private void OnMicSensitivityChanged(float value)
        {
            if (binding) return;
            GameSettingsService.Current.micSensitivity = value;
            GameSettingsService.Apply();
        }

        private void RefreshLabels()
        {
            GameSettings settings = GameSettingsService.Current;
            SetText(masterValue, Percent(settings.masterVolume));
            SetText(ambientValue, Percent(settings.ambientVolume));
            SetText(sfxValue, Percent(settings.sfxVolume));
            SetText(sensitivityValue, settings.mouseSensitivity.ToString("0.00"));
        }

        private static string Percent(float value)
        {
            return Mathf.RoundToInt(value * 100f) + "%";
        }

        private static void SetText(Text label, string value)
        {
            if (label != null) label.text = value;
        }
    }
}
