#if FEARME_COOP_ONLINE && FEARME_VOICE
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FearMe.Core;
using FearMe.Player;
using FearMe.Settings;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using Unity.Services.Vivox.AudioTaps;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FearMe.Net.Online
{
    // Proximity voice over Vivox. Joins a voice channel named after the co-op
    // session the moment you are in one, and leaves it when you are not.
    //
    // Your partner's voice does not come out of Vivox's own mixer: it is
    // tapped into a Unity AudioSource that follows their body around, so it
    // is really 3D - it fades out entirely past a few rooms, and a wall
    // between you muffles it. In the lobby, with no body yet, it is plain chat.
    //
    // And the demon hears you too: while connected, how loud you are talking
    // is what MicrophoneNoise reports, push-to-talk and all.
    public class ProximityVoice : MonoBehaviour
    {
        private const float Audible = 30f;       // metres; silent beyond this
        private const float FullVolume = 1.5f;   // metres; as loud as it gets inside this
        private const float OccludedCutoff = 900f;
        private const float ClearCutoff = 22000f;
        private const float OccludedVolume = 0.55f;
        private const Key TalkKey = Key.V;

        private class Speaker
        {
            public VivoxParticipant participant;
            public GameObject tap;
            public AudioSource source;
            public AudioLowPassFilter muffle;
            public float occlusion;          // 0 clear, 1 behind a wall
            public float nextOcclusionCheck;
        }

        private readonly Dictionary<VivoxParticipant, Speaker> speakers = new Dictionary<VivoxParticipant, Speaker>();
        private VivoxParticipant self;
        private string joinedChannel;
        private bool initialised;
        private bool busy;
        private bool transmitting;
        private float retryAfter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameObject go = new GameObject("ProximityVoice");
            DontDestroyOnLoad(go);
            go.AddComponent<ProximityVoice>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            MicrophoneNoise.ExternalLevel = null;
        }

        private void Update()
        {
            string wanted = WantedChannel();
            if (!busy && wanted != joinedChannel && Time.unscaledTime >= retryAfter) Switch(wanted);

            if (joinedChannel == null)
            {
                SetStatus(false, false, false, false);
                return;
            }

            TickTransmit();
            TickSpeakers();
        }

        // The session's channel, once there is a session, voice is switched on
        // and the player has signed in to Unity's services.
        private static string WantedChannel()
        {
            if (!GameSettingsService.Current.voiceChatEnabled) return null;
            if (!CoopSession.InSession || string.IsNullOrEmpty(CoopSession.SessionId)) return null;
            if (UnityServices.State != ServicesInitializationState.Initialized) return null;
            if (!AuthenticationService.Instance.IsSignedIn) return null;

            // Letters and numbers only, to stay well inside Vivox's rules.
            System.Text.StringBuilder name = new System.Text.StringBuilder("fearme");
            foreach (char c in CoopSession.SessionId)
                if (char.IsLetterOrDigit(c)) name.Append(c);
            return name.ToString();
        }

        // --- Joining and leaving ------------------------------------------------

        private async void Switch(string wanted)
        {
            busy = true;
            try
            {
                if (joinedChannel != null)
                {
                    string leaving = joinedChannel;
                    joinedChannel = null;
                    ClearSpeakers();
                    await VivoxService.Instance.LeaveChannelAsync(leaving);
                }

                if (wanted != null)
                {
                    await EnsureLoggedIn();
                    await VivoxService.Instance.JoinGroupChannelAsync(wanted, ChatCapability.AudioOnly);
                    joinedChannel = wanted;
                    transmitting = !GameSettingsService.Current.pushToTalk;
                    ApplyTransmit();
                    MicrophoneNoise.ExternalLevel = LocalLoudness;
                    Problem(string.Empty);
                }
                else
                {
                    MicrophoneNoise.ExternalLevel = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FearMe] Voice chat could not connect: " + e.Message);
                Problem("Voice chat unavailable - is Vivox enabled for this project in the Unity Cloud dashboard?");
                MicrophoneNoise.ExternalLevel = null;
                joinedChannel = null;
                retryAfter = Time.unscaledTime + 15f;
            }
            finally
            {
                busy = false;
            }
        }

        private async Task EnsureLoggedIn()
        {
            if (!initialised)
            {
                await VivoxService.Instance.InitializeAsync();
                VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;
                initialised = true;
            }

            if (VivoxService.Instance.IsLoggedIn) return;

            await VivoxService.Instance.LoginAsync(new LoginOptions
            {
                DisplayName = GameSettingsService.Current.playerName
            });
        }

        // --- Participants ---------------------------------------------------------

        private void OnParticipantAdded(VivoxParticipant participant)
        {
            if (participant.IsSelf)
            {
                self = participant;
                return;
            }

            if (speakers.ContainsKey(participant)) return;

            // silenceInChannelAudioMix: heard only through this tap, never
            // twice - once placed in the world, once flat in the ears.
            GameObject tap = participant.CreateVivoxParticipantTap("Voice_" + participant.DisplayName, true);
            DontDestroyOnLoad(tap);

            AudioSource source = participant.ParticipantTapAudioSource;
            if (source == null) source = tap.GetComponent<AudioSource>();

            source.spatialBlend = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = FullVolume;
            source.maxDistance = Audible;
            source.dopplerLevel = 0f;

            AudioLowPassFilter muffle = tap.AddComponent<AudioLowPassFilter>();
            muffle.cutoffFrequency = ClearCutoff;

            speakers[participant] = new Speaker
            {
                participant = participant,
                tap = tap,
                source = source,
                muffle = muffle
            };
        }

        private void OnParticipantRemoved(VivoxParticipant participant)
        {
            if (participant == self)
            {
                self = null;
                return;
            }

            if (!speakers.ContainsKey(participant)) return;

            participant.DestroyVivoxParticipantTap();
            speakers.Remove(participant);
        }

        private void ClearSpeakers()
        {
            foreach (Speaker speaker in speakers.Values)
            {
                if (speaker.participant != null) speaker.participant.DestroyVivoxParticipantTap();
                else if (speaker.tap != null) Destroy(speaker.tap);
            }

            speakers.Clear();
            self = null;
            MicrophoneNoise.ExternalLevel = null;
        }

        // A scene load can break the link between a tap and Unity's audio;
        // switching the tap off and on again restores it.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (Speaker speaker in speakers.Values)
            {
                if (speaker.tap == null) continue;

                VivoxParticipantTap tap = speaker.tap.GetComponent<VivoxParticipantTap>();
                if (tap == null) continue;
                tap.enabled = false;
                tap.enabled = true;
            }
        }

        // --- Your voice -----------------------------------------------------------

        private void TickTransmit()
        {
            bool pushToTalk = GameSettingsService.Current.pushToTalk;
            bool wanted = !pushToTalk || (Keyboard.current != null && Keyboard.current[TalkKey].isPressed);

            if (wanted != transmitting)
            {
                transmitting = wanted;
                ApplyTransmit();
            }
        }

        private void ApplyTransmit()
        {
            if (transmitting) VivoxService.Instance.UnmuteInputDevice();
            else VivoxService.Instance.MuteInputDevice();
        }

        // How loud you are as the demon hears it: nothing while muted, and only
        // actual speech, not the hum of the room.
        private float LocalLoudness()
        {
            if (!transmitting || self == null || !self.SpeechDetected) return 0f;
            return Mathf.Clamp(Mathf.InverseLerp(0.3f, 0.85f, (float)self.AudioEnergy), 0.15f, 1f);
        }

        // --- Their voices ---------------------------------------------------------

        private void TickSpeakers()
        {
            AudioListener listener = FindFirstObjectByType<AudioListener>();
            PlayerController local = PlayerRegistry.Local;
            float volume = GameSettingsService.Current.voiceVolume;
            bool partnerSpeaking = false;

            foreach (Speaker speaker in speakers.Values)
            {
                if (speaker.tap == null || speaker.source == null) continue;
                if (speaker.participant.SpeechDetected) partnerSpeaking = true;

                Transform body = BodyOf(speaker.participant);

                // No body yet - the lobby - so just talk.
                if (body == null || listener == null)
                {
                    speaker.source.spatialBlend = 0f;
                    speaker.muffle.cutoffFrequency = ClearCutoff;
                    speaker.source.volume = volume;
                    continue;
                }

                Vector3 mouth = body.position + Vector3.up * 1.6f;
                speaker.tap.transform.position = mouth;
                speaker.source.spatialBlend = 1f;

                if (Time.unscaledTime >= speaker.nextOcclusionCheck)
                {
                    speaker.nextOcclusionCheck = Time.unscaledTime + 0.15f;
                    float target = Blocked(listener.transform.position, mouth, body, local) ? 1f : 0f;
                    speaker.occlusion = Mathf.MoveTowards(speaker.occlusion, target, 0.5f);
                }

                speaker.muffle.cutoffFrequency = Mathf.Lerp(ClearCutoff, OccludedCutoff, speaker.occlusion);
                speaker.source.volume = volume * Mathf.Lerp(1f, OccludedVolume, speaker.occlusion);
            }

            SetStatus(true, transmitting, self != null && transmitting && self.SpeechDetected, partnerSpeaking);
        }

        // Their proxy body on this machine; with two players, the one that is
        // not us is the only candidate if the ids have not synced yet.
        private static Transform BodyOf(VivoxParticipant participant)
        {
            Transform body = NetworkPlayer.BodyOf(participant.PlayerId);
            if (body != null) return body;

            foreach (PlayerController player in PlayerRegistry.All)
                if (player != null && !player.IsLocalPlayer) return player.transform;
            return null;
        }

        // Anything solid between your ears and their mouth, other than the
        // two of you.
        private static bool Blocked(Vector3 from, Vector3 to, Transform them, PlayerController you)
        {
            if (!Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return false;

            Transform hitTransform = hit.collider.transform;
            if (hitTransform.IsChildOf(them)) return false;
            if (you != null && hitTransform.IsChildOf(you.transform)) return false;
            return true;
        }

        // --- Status -----------------------------------------------------------------

        private static void SetStatus(bool active, bool transmittingNow, bool localSpeaking, bool partnerSpeaking)
        {
            bool pushToTalk = GameSettingsService.Current.pushToTalk;

            if (VoiceStatus.Active == active && VoiceStatus.Transmitting == transmittingNow &&
                VoiceStatus.LocalSpeaking == localSpeaking && VoiceStatus.PartnerSpeaking == partnerSpeaking &&
                VoiceStatus.PushToTalk == pushToTalk)
                return;

            VoiceStatus.Active = active;
            VoiceStatus.Transmitting = transmittingNow;
            VoiceStatus.LocalSpeaking = localSpeaking;
            VoiceStatus.PartnerSpeaking = partnerSpeaking;
            VoiceStatus.PushToTalk = pushToTalk;
            VoiceStatus.Version++;
        }

        private static void Problem(string message)
        {
            if (VoiceStatus.Problem == message) return;
            VoiceStatus.Problem = message;
            VoiceStatus.Version++;
        }
    }
}
#endif
