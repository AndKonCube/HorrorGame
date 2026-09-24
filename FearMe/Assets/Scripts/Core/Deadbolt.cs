using System.Collections.Generic;
using FearMe.Player;
using FearMe.Settings;
using UnityEngine;

namespace FearMe.Core
{
    // The way out is barred by heavy bolts that have to be drawn one after
    // another. Each takes seconds of grinding effort, standing still at the
    // door - and each one that gives goes off like a gunshot through the
    // building. The demon comes. Someone has to stand guard.
    public class Deadbolt : Interactable
    {
        private static readonly List<Deadbolt> all = new List<Deadbolt>();

        [Tooltip("Drawn in this order: 0 first.")]
        [SerializeField] private int order;
        [Tooltip("The bar that slides. Defaults to this object.")]
        [SerializeField] private Transform bolt;
        [SerializeField] private Vector3 slideOffset = new Vector3(0.18f, 0f, 0f);
        [SerializeField] private float unlockSeconds = 3.5f;
        [Tooltip("How far the clank carries. It is meant to reach the demon.")]
        [SerializeField] private float noiseRadius = 45f;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Looped while someone is working it.")]
        [SerializeField] private AudioClip grindLoop;
        [SerializeField] private AudioClip clankClip;

        private Vector3 closedPosition;
        private float progress;
        private int lastInteractFrame = -10;
        private PlayerController holder;
        private float holderSpeed;
        private float slide;

        public static int Count => all.Count;

        public int Index => all.IndexOf(this);
        public float Progress => Mathf.Clamp01(progress / Mathf.Max(0.1f, unlockSeconds));

        private static SpawnDirector Director => SpawnDirector.Instance;
        private bool IsOpen => Director != null && Director.State.boltsOpen > Index;
        private bool IsNext => Director != null && Director.AllKeysHeld && Director.State.boltsOpen == Index;

        public override string Prompt
        {
            get
            {
                if (Director == null || IsOpen) return string.Empty;
                if (!Director.AllKeysHeld) return "Bolted - find the " + Director.KeyName(Director.KeyCount - 1) + " first";
                return IsNext ? "Hold to draw the bolt" : "Draw the bolt above it first";
            }
        }

        public override bool HoldToUse => true;

        private void Awake()
        {
            if (bolt == null) bolt = transform;
            closedPosition = bolt.localPosition;
        }

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);

            // By order, then by place in the scene - the same on every machine.
            all.Sort((a, b) => a.order != b.order
                ? a.order.CompareTo(b.order)
                : a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        }

        private void OnDisable()
        {
            all.Remove(this);
            Release();
        }

        public override void Interact()
        {
            if (!IsNext) return;

            PlayerController local = PlayerRegistry.Local;
            if (local == null) return;

            // Both hands on the bolt: no walking off mid-pull.
            if (holder == null)
            {
                holder = local;
                holderSpeed = local.SpeedMultiplier;
                local.SpeedMultiplier = 0f;

                if (audioSource != null && grindLoop != null)
                {
                    audioSource.clip = grindLoop;
                    audioSource.loop = true;
                    audioSource.volume = GameSettingsService.Current.sfxVolume;
                    audioSource.Play();
                }
            }

            lastInteractFrame = Time.frameCount;
            progress += Time.deltaTime;

            if (progress >= unlockSeconds)
            {
                progress = 0f;
                Release();
                Director.Request(RunRequest.OpenBolt, Index);
            }
        }

        private void Update()
        {
            // Let go, and the bolt slips back a little.
            if (Time.frameCount - lastInteractFrame > 1)
            {
                Release();
                progress = Mathf.MoveTowards(progress, 0f, Time.deltaTime * 0.6f);
            }

            float target = IsOpen ? 1f : Progress * 0.25f;
            slide = Mathf.MoveTowards(slide, target, Time.deltaTime * 4f);
            bolt.localPosition = closedPosition + slideOffset * slide;
        }

        private void Release()
        {
            if (holder != null)
            {
                holder.SpeedMultiplier = holderSpeed;
                holder = null;
            }

            if (audioSource != null && audioSource.clip == grindLoop && audioSource.isPlaying) audioSource.Stop();
        }

        // On the authority, when a bolt gives: the noise the demon hears.
        public static void RaiseAlarm(int index)
        {
            if (index < 0 || index >= all.Count) return;
            NoiseBus.EmitLocal(all[index].transform.position, all[index].noiseRadius);
        }

        // On every machine: the clank everyone hears.
        public static void PlayOpened(int index)
        {
            if (index < 0 || index >= all.Count) return;

            Deadbolt bolt = all[index];
            if (bolt.audioSource != null && bolt.clankClip != null)
                bolt.audioSource.PlayOneShot(bolt.clankClip, GameSettingsService.Current.sfxVolume);

            if (VolumetricFogController.Instance != null) VolumetricFogController.Instance.Pulse(0.02f);
        }
    }
}
