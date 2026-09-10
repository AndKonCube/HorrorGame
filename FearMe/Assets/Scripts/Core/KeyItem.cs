using UnityEngine;

namespace FearMe.Core
{
    public class KeyItem : Interactable
    {
        [SerializeField] private float spinSpeed = 60f;
        [SerializeField] private float bobHeight = 0.15f;
        [SerializeField] private float bobSpeed = 2f;

        private Vector3 basePosition;

        public override string Prompt => "Take key";

        private void Start()
        {
            basePosition = transform.position;
        }

        private void Update()
        {
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
            float offset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = basePosition + Vector3.up * offset;
        }

        public override void Interact()
        {
            if (ObjectiveTracker.Instance != null)
                ObjectiveTracker.Instance.CollectKey();
            Destroy(gameObject);
        }
    }
}
