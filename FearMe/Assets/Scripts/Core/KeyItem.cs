using System.Collections.Generic;
using FearMe.Net;
using UnityEngine;

namespace FearMe.Core
{
    public class KeyItem : Interactable
    {
        [SerializeField] private float spinSpeed = 60f;
        [SerializeField] private float bobHeight = 0.15f;
        [SerializeField] private float bobSpeed = 2f;

        private static readonly Dictionary<int, KeyItem> live = new Dictionary<int, KeyItem>();

        private Vector3 basePosition;

        private int coopId = -1;

        // Its index in the spawner's candidate list. Every machine loads the
        // same scene, so the same key has the same id everywhere - which is all
        // the server needs to say which one was picked up.
        //
        // Registered on assignment rather than in OnEnable, because OnEnable
        // has already run by the time the spawner hands out ids.
        public int CoopId
        {
            get => coopId;
            set
            {
                if (coopId >= 0) live.Remove(coopId);

                coopId = value;
                if (coopId >= 0) live[coopId] = this;
            }
        }

        public override string Prompt => "Take key";

        public static KeyItem Find(int coopId)
        {
            return live.TryGetValue(coopId, out KeyItem key) ? key : null;
        }

        private void Start()
        {
            basePosition = transform.position;
        }

        private void OnDestroy()
        {
            if (coopId >= 0 && live.TryGetValue(coopId, out KeyItem held) && held == this)
                live.Remove(coopId);
        }

        private void Update()
        {
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
            float offset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = basePosition + Vector3.up * offset;
        }

        public override void Interact()
        {
            // Online, two players can reach for the same key in the same
            // frame; the server picks one and tells everyone.
            if (CoopHooks.KeyTaken != null && CoopHooks.KeyTaken(this)) return;

            Collect();
        }

        // Called locally in a solo run.
        public void Collect()
        {
            if (ObjectiveTracker.Instance != null)
                ObjectiveTracker.Instance.CollectKey();
            Destroy(gameObject);
        }

        // Online: the server has already counted it, this just removes it.
        public void Vanish()
        {
            Destroy(gameObject);
        }
    }
}
