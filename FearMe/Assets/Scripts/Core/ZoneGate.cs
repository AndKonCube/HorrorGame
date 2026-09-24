using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FearMe.Core
{
    // The locked door into the next zone - the Wards, then the Morgue. It
    // opens with the key found in the zones before it, and stays open for
    // the rest of the run. While it is shut it is a wall to the demon too,
    // so the hunt stays where the players are.
    public class ZoneGate : Interactable
    {
        private static readonly List<ZoneGate> all = new List<ZoneGate>();

        [Tooltip("The zone this door opens into (1 = the second zone).")]
        [SerializeField] private int zone = 1;

        [Header("Door")]
        [Tooltip("The part that swings. Defaults to this object.")]
        [SerializeField] private Transform door;
        [SerializeField] private Vector3 openRotation = new Vector3(0f, 100f, 0f);
        [SerializeField] private float swingSpeed = 90f;
        [Tooltip("Switched off once open. Defaults to the door's own collider.")]
        [SerializeField] private Collider blocker;
        [Tooltip("Keeps the demon out while locked. Optional.")]
        [SerializeField] private NavMeshObstacle obstacle;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip unlockClip;
        [SerializeField] private AudioClip lockedRattle;

        private Quaternion closedRotation;
        private float openAmount;
        private bool wasOpen;

        public int Zone => zone;

        private SpawnDirector Director => SpawnDirector.Instance;
        private bool IsOpen => Director != null && Director.State.zonesUnlocked >= zone;
        private bool HaveKey => Director != null && Director.State.keysHeld >= zone;
        private string KeyName => Director != null ? Director.KeyName(zone - 1) : "key";

        public override string Prompt
        {
            get
            {
                if (IsOpen || Director == null) return string.Empty;
                string where = HospitalZone.NameOf(zone);
                return HaveKey ? "Unlock the " + where : "Locked - it needs the " + KeyName;
            }
        }

        public static ZoneGate For(int zone)
        {
            foreach (ZoneGate gate in all)
                if (gate != null && gate.zone == zone) return gate;
            return null;
        }

        private void Awake()
        {
            if (door == null) door = transform;
            if (blocker == null) blocker = door.GetComponentInChildren<Collider>();
            closedRotation = door.localRotation;
        }

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        public override void Interact()
        {
            if (IsOpen || Director == null) return;

            if (HaveKey)
            {
                Director.Request(RunRequest.UnlockZone, zone);
                return;
            }

            // Trying a locked door is a small sound, but it is a sound.
            if (audioSource != null && lockedRattle != null) audioSource.PlayOneShot(lockedRattle);
            NoiseBus.Emit(transform.position, 5f);
        }

        private void Update()
        {
            bool open = IsOpen;

            if (open && !wasOpen)
            {
                if (audioSource != null && unlockClip != null) audioSource.PlayOneShot(unlockClip);
                if (blocker != null) blocker.enabled = false;
                if (obstacle != null) obstacle.enabled = false;
            }
            wasOpen = open;

            openAmount = Mathf.MoveTowards(openAmount, open ? 1f : 0f,
                swingSpeed / Mathf.Max(1f, openRotation.magnitude) * Time.deltaTime);
            door.localRotation = closedRotation * Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(openRotation), openAmount);
        }
    }
}
