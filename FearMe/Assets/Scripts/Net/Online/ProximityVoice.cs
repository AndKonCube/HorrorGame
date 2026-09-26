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
using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.Net.Online
{
    // Proximity voice over Vivox. Joins a positional voice channel named
    // after the co-op session the moment you are in one, and leaves it when
    // you are not.
    //
    // Distance is Vivox's own positional audio: full volume up close, fading
    // to nothing at AudibleDistance. This machine reports where your head is
    // several times a second. In the lobby everyone reports the same spot,
    // so it is plain chat until the level loads.
    //
    // Walls: a line from your ears to their mouth that hits something solid
    // drops their volume, so a voice through a door sounds further than it is.
    //
    // And the demon hears you too: while connected, how loud you are talking
    // is what MicrophoneNoise reports, push-to-talk and all.
    public class ProximityVoice : MonoBehaviour
    {
        private const int AudibleDistance = 30;       // metres; silent beyond this
        private const int ConversationalDistance = 1; // about half a person's height
        private const float PositionInterval = 0.15f;
        private const int OccludedVolume = -18;        // Vivox local volume, -50..50
        private const Key TalkKey = Key.V;

        private class Speaker
        {
            public VivoxParticipant participant;
            public bool occluded;
            public int appliedVolume = int.MinValue;
            public float nextOcclusionCheck;
        }

        private readonly Dictionary<VivoxParticipant, Speaker> speakers = new Dictionary<VivoxParticipant, Speaker>();
        private VivoxParticipant self;
        private string joinedChannel;
        private bool initialised;
        private bool busy;
        private bool transmitting;
        private float retryAfter;
        private float nextPosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameObject go = new GameObject("ProximityVoice");
            DontDestroyOnLoad(go);
            go.AddComponent<ProximityVoice>();
        }

        private void OnDisable()
        {
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
            TickPosition();
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
                    speakers.Clear();
                    self = null;
                    MicrophoneNoise.ExternalLevel = null;
                    await VivoxService.Instance.LeaveChannelAsync(leaving);
                }

                if (wanted != null)
                {
                    await EnsureLoggedIn();

                    Channel3DProperties space = new Channel3DProperties(AudibleDistance, ConversationalDistance,
                        1f, AudioFadeModel.LinearByDistance);
                    await VivoxService.Instance.JoinPositionalChannelAsync(wanted, ChatCapability.AudioOnly, space);

                    joinedChannel = wanted;
                    nextPosition = 0f;
                    Debug.Log("[FearMe] Voice chat connected (positional, silent beyond " + AudibleDistance + "m).");

                    transmitting = !GameSettingsService.Current.pushToTalk;
                    ApplyTransmit();
                    MicrophoneNoise.ExternalLevel = LocalLoudness;
                    Problem(string.Empty);
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

        private void OnParticipantAdded(VivoxParticipant participant)
        {
            if (participant.IsSelf)
            {
                self = participant;
                return;
            }

            if (speakers.ContainsKey(participant)) return;

            Debug.Log("[FearMe] Voice: hearing " + participant.DisplayName + ".");
            speakers[participant] = new Speaker { participant = participant };
        }

        private void OnParticipantRemoved(VivoxParticipant participant)
        {
            if (participant == self) self = null;
            speakers.Remove(participant);
        }

        // --- Where you are ------------------------------------------------------

        // Your head, several times a second. With no player yet (the lobby)
        // everyone reports the origin, which is full volume for all.
        private void TickPosition()
        {
            if (Time.unscaledTime < nextPosition) return;
            nextPosition = Time.unscaledTime + PositionInterval;

            PlayerController local = PlayerRegistry.Local;
            Camera view = local != null ? local.GetComponentInChildren<Camera>() : null;

            Vector3 head = Vector3.zero;
            Vector3 forward = Vector3.forward;
            Vector3 up = Vector3.up;

            if (view != null)
            {
                head = view.transform.position;
                forward = view.transform.forward;
                up = view.transform.up;
            }
            else if (local != null)
            {
                head = local.transform.position + Vector3.up * 1.6f;
                forward = local.transform.forward;
            }

            try
            {
                VivoxService.Instance.Set3DPosition(head, head, forward, up, joinedChannel);
            }
            catch (Exception e)
            {
                // Not ready yet straight after joining; the next tick tries again.
                Debug.LogWarning("[FearMe] Voice position: " + e.Message);
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
            PlayerController local = PlayerRegistry.Local;
            Camera view = local != null ? local.GetComponentInChildren<Camera>() : null;
            float setting = GameSettingsService.Current.voiceVolume;
            bool partnerSpeaking = false;

            foreach (Speaker speaker in speakers.Values)
            {
                if (speaker.participant.SpeechDetected) partnerSpeaking = true;

                // Behind a wall or not, checked a few times a second.
                if (Time.unscaledTime >= speaker.nextOcclusionCheck)
                {
                    speaker.nextOcclusionCheck = Time.unscaledTime + 0.2f;
                    Transform body = BodyOf(speaker.participant);
                    speaker.occluded = view != null && body != null &&
                        Blocked(view.transform.position, body.position + Vector3.up * 1.6f, body, local);
                }

                // The settings slider scales -50 (silent) to 0 (as sent).
                int volume = Mathf.RoundToInt(Mathf.Lerp(-50f, 0f, setting)) + (speaker.occluded ? OccludedVolume : 0);
                volume = Mathf.Clamp(volume, -50, 50);

                if (volume == speaker.appliedVolume) continue;
                speaker.appliedVolume = volume;
                speaker.participant.SetLocalVolume(volume);
            }

            SetStatus(true, transmitting, self != null && transmitting && self.SpeechDetected, partnerSpeaking);
        }

        // Their body on this machine; with two players, the one that is not us
        // is the only candidate if the ids have not synced yet.
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
