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

        [Header("Value readouts")]
        [SerializeField] private Text masterValue;
        [SerializeField] private Text ambientValue;
        [SerializeField] private Text sfxValue;
        [SerializeField] private Text sensitivityValue;

        private bool binding;

        private void OnEnable()
        {
            GameSettings settings = SettingsService.Current;

            binding = true;
            if (masterSlider != null) masterSlider.value = settings.masterVolume;
            if (ambientSlider != null) ambientSlider.value = settings.ambientVolume;
            if (sfxSlider != null) sfxSlider.value = settings.sfxVolume;
            if (sensitivitySlider != null) sensitivitySlider.value = settings.mouseSensitivity;
            if (invertToggle != null) invertToggle.isOn = settings.invertLook;
            binding = false;

            Listen(masterSlider, OnMasterChanged);
            Listen(ambientSlider, OnAmbientChanged);
            Listen(sfxSlider, OnSfxChanged);
            Listen(sensitivitySlider, OnSensitivityChanged);

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
            SettingsService.Current.masterVolume = value;
            SettingsService.Apply();
            RefreshLabels();
        }

        private void OnAmbientChanged(float value)
        {
            if (binding) return;
            SettingsService.Current.ambientVolume = value;
            SettingsService.Apply();
            RefreshLabels();
        }

        private void OnSfxChanged(float value)
        {
            if (binding) return;
            SettingsService.Current.sfxVolume = value;
            SettingsService.Apply();
            RefreshLabels();
        }

        private void OnSensitivityChanged(float value)
        {
            if (binding) return;
            SettingsService.Current.mouseSensitivity = value;
            SettingsService.Apply();
            RefreshLabels();
        }

        private void OnInvertChanged(bool value)
        {
            if (binding) return;
            SettingsService.Current.invertLook = value;
            SettingsService.Apply();
        }

        private void RefreshLabels()
        {
            GameSettings settings = SettingsService.Current;
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
