using FearMe.Net;
using FearMe.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace FearMe.UI
{
    // Host a run or join one with a code, see who is in, ready up, start.
    // Everything it knows about the network it learns from CoopSession, so it
    // works unchanged once a real transport is installed behind that.
    public class LobbyPanel : MonoBehaviour
    {
        [SerializeField] private string gameSceneName = "Demo";

        [Header("Identity")]
        [SerializeField] private InputField nameField;

        [Header("Join")]
        [SerializeField] private InputField joinCodeField;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;

        [Header("In lobby")]
        [SerializeField] private Text joinCodeLabel;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private Text readyButtonLabel;
        [SerializeField] private Button startButton;
        [SerializeField] private Text startButtonLabel;
        [SerializeField] private Button leaveButton;

        [Header("Slots")]
        [Tooltip("One label per player slot - two of them for a two-player run.")]
        [SerializeField] private Text[] slotLabels;

        [Header("Status")]
        [SerializeField] private Text statusLabel;

        [Header("Testing")]
        [Tooltip("Editor and development builds only: fills the second slot a " +
            "moment after hosting so the slots and Start button can be checked solo.")]
        [SerializeField] private bool simulateSecondPlayer;

        private void OnEnable()
        {
            OfflineCoopBackend.SimulateGuest = simulateSecondPlayer;

            if (nameField != null)
            {
                nameField.characterLimit = 16;
                nameField.text = GameSettingsService.Current.playerName;
                nameField.onEndEdit.RemoveListener(OnNameCommitted);
                nameField.onEndEdit.AddListener(OnNameCommitted);
            }

            if (joinCodeField != null)
            {
                joinCodeField.characterLimit = 8;
                joinCodeField.onValueChanged.RemoveListener(OnJoinCodeChanged);
                joinCodeField.onValueChanged.AddListener(OnJoinCodeChanged);
            }

            CoopSession.Changed -= Refresh;
            CoopSession.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            CoopSession.Changed -= Refresh;

            if (nameField != null) nameField.onEndEdit.RemoveListener(OnNameCommitted);
            if (joinCodeField != null) joinCodeField.onValueChanged.RemoveListener(OnJoinCodeChanged);
        }

        private void Update()
        {
            // Relay and Lobby work is asynchronous, so the session gets a pump
            // for as long as this screen is the thing on top.
            CoopSession.Tick();
        }

        public void OnHost()
        {
            CommitName();
            CoopSession.Host();
        }

        public void OnJoin()
        {
            CommitName();
            CoopSession.Join(joinCodeField != null ? joinCodeField.text : string.Empty);
        }

        public void OnToggleReady()
        {
            CoopSession.SetReady(!CoopSession.LocalReady);
        }

        public void OnStart()
        {
            CoopSession.StartMatch(gameSceneName);
        }

        public void OnLeave()
        {
            CoopSession.Leave();
        }

        // The host reads this out or pastes it to a friend, so put it where a
        // paste can reach.
        public void OnCopyJoinCode()
        {
            if (string.IsNullOrEmpty(CoopSession.JoinCode)) return;
            GUIUtility.systemCopyBuffer = CoopSession.JoinCode;
            SetText(statusLabel, "Join code copied.");
        }

        private void OnNameCommitted(string value)
        {
            CommitName();
        }

        private void CommitName()
        {
            if (nameField == null) return;

            GameSettingsService.Current.playerName = nameField.text;
            GameSettingsService.Current.Clamp();
            GameSettingsService.Save();

            // Clamp may have trimmed it, so show what was actually kept.
            nameField.text = GameSettingsService.Current.playerName;
        }

        // Codes are read off someone else's screen; accepting lower case and
        // showing it back upper case saves a failed join.
        private void OnJoinCodeChanged(string value)
        {
            if (joinCodeField == null) return;

            string tidied = CoopSession.Normalise(value);
            if (tidied != value) joinCodeField.text = tidied;

            Refresh();
        }

        private void Refresh()
        {
            bool inLobby = CoopSession.InSession;
            bool connecting = CoopSession.State == SessionState.Connecting;
            bool host = CoopSession.IsHost;

            SetActive(hostButton, !inLobby);
            SetActive(joinButton, !inLobby);
            SetActive(joinCodeField, !inLobby);
            SetActive(nameField, !inLobby);

            SetInteractable(hostButton, !connecting);
            SetInteractable(joinButton, !connecting && HasUsableCode());

            SetActive(readyButton, inLobby && !host);
            SetActive(startButton, inLobby && host);
            SetActive(leaveButton, inLobby);
            SetActive(copyCodeButton, inLobby && host && !string.IsNullOrEmpty(CoopSession.JoinCode));

            SetInteractable(startButton, CoopSession.CanStart);
            SetText(readyButtonLabel, CoopSession.LocalReady ? "NOT READY" : "READY");
            SetText(startButtonLabel, CoopSession.Members.Count > 1 ? "START RUN" : "START SOLO");

            RefreshJoinCode(inLobby, host);
            RefreshSlots(inLobby);
            RefreshStatus(inLobby, host);
        }

        private void RefreshJoinCode(bool inLobby, bool host)
        {
            if (joinCodeLabel == null) return;

            SetActive(joinCodeLabel, inLobby);
            if (!inLobby) return;

            if (!string.IsNullOrEmpty(CoopSession.JoinCode))
                joinCodeLabel.text = "CODE  " + CoopSession.JoinCode;
            else
                joinCodeLabel.text = host ? "NO CODE - OFFLINE" : string.Empty;
        }

        private void RefreshSlots(bool inLobby)
        {
            if (slotLabels == null) return;

            for (int i = 0; i < slotLabels.Length; i++)
            {
                Text label = slotLabels[i];
                if (label == null) continue;

                if (!inLobby)
                {
                    label.text = string.Empty;
                    continue;
                }

                if (i >= CoopSession.Members.Count)
                {
                    label.text = "EMPTY  ·  waiting for a player";
                    continue;
                }

                SessionMember member = CoopSession.Members[i];
                string who = string.IsNullOrEmpty(member.name) ? "PLAYER" : member.name;
                if (member.isHost) who += "  (HOST)";
                if (member.isLocal) who += "  ·  you";

                label.text = member.isReady ? who + "  ·  READY" : who;
            }
        }

        private void RefreshStatus(bool inLobby, bool host)
        {
            if (statusLabel == null) return;

            // A real error from the session beats anything generic.
            if (!string.IsNullOrEmpty(CoopSession.Status))
            {
                statusLabel.text = CoopSession.Status;
                return;
            }

            if (!inLobby)
            {
                statusLabel.text = CoopSession.IsAvailable
                    ? "Host a run and share the code, or type a friend's code to join."
                    : CoopSession.UnavailableReason;
                return;
            }

            if (host)
            {
                statusLabel.text = CoopSession.CanStart
                    ? "Ready when you are."
                    : "Waiting for the other player to ready up.";
                return;
            }

            statusLabel.text = "Waiting for the host to start.";
        }

        private bool HasUsableCode()
        {
            if (joinCodeField == null) return false;
            return CoopSession.Normalise(joinCodeField.text).Length >= 4;
        }

        private static void SetActive(Component target, bool active)
        {
            if (target != null && target.gameObject.activeSelf != active)
                target.gameObject.SetActive(active);
        }

        private static void SetInteractable(Selectable target, bool interactable)
        {
            if (target != null) target.interactable = interactable;
        }

        private static void SetText(Text label, string value)
        {
            if (label != null) label.text = value;
        }
    }
}
