using UnityEngine;

namespace FearMe.Core
{
    public class FlickeringLight : MonoBehaviour
    {
        [SerializeField] private Light target;
        [SerializeField, Range(0f, 1f)] private float flickerChance = 0.05f;
        [SerializeField] private float minBlackout = 0.04f;
        [SerializeField] private float maxBlackout = 0.25f;

        private float blackoutUntil;

        private void Awake()
        {
            if (target == null) target = GetComponent<Light>();
        }

        private void Update()
        {
            if (target == null) return;

            if (Time.time < blackoutUntil)
            {
                target.enabled = false;
                return;
            }

            target.enabled = true;
            if (Random.value < flickerChance * Time.deltaTime * 60f)
                blackoutUntil = Time.time + Random.Range(minBlackout, maxBlackout);
        }
    }
}
