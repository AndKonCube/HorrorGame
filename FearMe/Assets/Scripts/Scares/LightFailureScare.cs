using System.Collections;
using System.Collections.Generic;
using FearMe.Core;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Scares
{
    // Kills the lights around the player for a moment. The flashlight is left
    // alone on purpose - taking away their only tool reads as a bug, not a scare.
    public class LightFailureScare : ScareEvent
    {
        [SerializeField] private float radius = 20f;
        [SerializeField] private float blackoutDuration = 1.4f;
        [SerializeField] private int stutterCount = 3;
        [SerializeField] private float fogPulse = 0.02f;

        private Coroutine active;

        protected override void OnTrigger(ScareContext context)
        {
            if (active != null) return;
            active = StartCoroutine(Fail(context));
        }

        private IEnumerator Fail(ScareContext context)
        {
            List<Light> affected = CollectLights(context);
            if (affected.Count == 0)
            {
                active = null;
                yield break;
            }

            // Idle flicker would fight us for control of enabled, so pause it.
            List<FlickeringLight> paused = PauseFlicker(affected);

            if (fogPulse > 0f && VolumetricFogController.Instance != null)
                VolumetricFogController.Instance.Pulse(fogPulse);

            // A few stutters first, then the real blackout.
            for (int i = 0; i < stutterCount; i++)
            {
                SetLights(affected, false);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
                SetLights(affected, true);
                yield return new WaitForSeconds(Random.Range(0.06f, 0.2f));
            }

            SetLights(affected, false);
            yield return new WaitForSeconds(blackoutDuration);
            SetLights(affected, true);

            foreach (FlickeringLight flicker in paused)
            {
                if (flicker != null) flicker.enabled = true;
            }

            active = null;
        }

        private static List<FlickeringLight> PauseFlicker(List<Light> lights)
        {
            List<FlickeringLight> paused = new List<FlickeringLight>();
            foreach (Light light in lights)
            {
                FlickeringLight flicker = light.GetComponent<FlickeringLight>();
                if (flicker == null || !flicker.enabled) continue;

                flicker.enabled = false;
                paused.Add(flicker);
            }
            return paused;
        }

        private List<Light> CollectLights(ScareContext context)
        {
            List<Light> result = new List<Light>();
            Light[] all = FindObjectsByType<Light>(FindObjectsSortMode.None);

            foreach (Light light in all)
            {
                if (light == null || !light.enabled) continue;
                if (light.type == LightType.Directional) continue;
                if (light.GetComponentInParent<Flashlight>() != null) continue;
                if (Vector3.Distance(light.transform.position, context.Player.position) > radius) continue;

                result.Add(light);
            }

            return result;
        }

        private static void SetLights(List<Light> lights, bool enabled)
        {
            foreach (Light light in lights)
            {
                if (light != null) light.enabled = enabled;
            }
        }
    }
}
