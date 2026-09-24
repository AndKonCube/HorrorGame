using System.Collections;
using FearMe.Core;
using FearMe.Settings;
using UnityEngine;

namespace FearMe.Player
{
    // Your real voice, in the game. Talk, laugh or shout at your partner and
    // it makes noise where you are standing - loud enough and the stalker
    // comes to look. The whole point is that you end up whispering.
    //
    // Only runs when the player has switched it on in Settings.
    public class MicrophoneNoise : MonoBehaviour
    {
        [Header("Loudness")]
        [Tooltip("dB at which the loudest voice counts as shouting.")]
        [SerializeField] private float shoutDb = -12f;
        [Tooltip("dB below which it is ignored, at the lowest sensitivity.")]
        [SerializeField] private float quietDbLow = -28f;
        [Tooltip("...and at the highest sensitivity. Room tone sits well below this.")]
        [SerializeField] private float quietDbHigh = -50f;

        [Header("Reach")]
        [SerializeField] private float whisperRadius = 3f;
        [SerializeField] private float shoutRadius = 18f;
        [Tooltip("How often a sustained voice is re-reported.")]
        [SerializeField] private float reportInterval = 0.4f;

        private const int SampleRate = 16000;
        private const int Window = 1024; // ~64ms

        private readonly float[] samples = new float[Window];
        private AudioClip clip;
        private bool recording;
        private bool warnedNoDevice;
        private float nextReport;

        // 0 when silent or switched off, 1 at a shout. The HUD reads it so the
        // player knows they are being heard before the stalker does.
        public static float Level { get; private set; }

        // Set by voice chat while it is connected: how loud you are as it hears
        // you (0 when push-to-talk is up). It replaces this component's own
        // microphone, so the mic is never opened twice - and it counts whether
        // or not the Settings toggle is on, because talking to your partner is
        // exactly the thing the demon should be able to hear.
        public static System.Func<float> ExternalLevel;

        private void OnEnable()
        {
            GameSettingsService.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GameSettingsService.Changed -= Apply;
            StopListening();
        }

        private void Apply()
        {
            bool wanted = GameSettingsService.Current.micAttractsMonster && ExternalLevel == null;

            if (wanted && !recording) StartCoroutine(StartListening());
            else if (!wanted && recording) StopListening();
        }

        private IEnumerator StartListening()
        {
            // macOS and some other platforms ask first; elsewhere this is instant.
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);

            if (!Application.HasUserAuthorization(UserAuthorization.Microphone)) yield break;

            if (Microphone.devices.Length == 0)
            {
                if (!warnedNoDevice) Debug.LogWarning("[FearMe] Voice detection is on, but no microphone was found.");
                warnedNoDevice = true;
                yield break;
            }

            // Settings may have flipped back while permission was asked for.
            if (recording || !GameSettingsService.Current.micAttractsMonster) yield break;

            clip = Microphone.Start(null, true, 1, SampleRate);
            recording = clip != null;
        }

        private void StopListening()
        {
            if (recording) Microphone.End(null);

            recording = false;
            clip = null;
            Level = 0f;
        }

        private void Update()
        {
            if (ExternalLevel != null)
            {
                if (recording) StopListening();
                Level = Mathf.Clamp01(ExternalLevel());
                Report();
                return;
            }

            if (!recording || clip == null)
            {
                Level = 0f;
                return;
            }

            int position = Microphone.GetPosition(null);
            if (position < Window) return; // wrapped this frame; next one will do

            clip.GetData(samples, position - Window);

            float sum = 0f;
            for (int i = 0; i < Window; i++) sum += samples[i] * samples[i];
            float rms = Mathf.Sqrt(sum / Window);
            float db = 20f * Mathf.Log10(Mathf.Max(rms, 1e-7f));

            float quiet = Mathf.Lerp(quietDbLow, quietDbHigh, GameSettingsService.Current.micSensitivity);
            Level = Mathf.InverseLerp(quiet, shoutDb, db);
            Report();
        }

        private void Report()
        {
            if (Level <= 0f || Time.time < nextReport) return;

            nextReport = Time.time + reportInterval;
            NoiseBus.Emit(transform.position, Mathf.Lerp(whisperRadius, shoutRadius, Level));
        }
    }
}
