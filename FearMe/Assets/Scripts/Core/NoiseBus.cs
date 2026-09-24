using System;
using FearMe.Net;
using UnityEngine;

namespace FearMe.Core
{
    public struct NoiseEvent
    {
        public Vector3 position;
        public float radius;
    }

    // Everything loud reports here: a slammed door, a heavy lever, a dropped
    // battery, a voice in the room. The stalker hears whatever lands inside
    // the radius and goes to where it happened - not to where you are by the
    // time it arrives, which is what gives you a chance to move.
    public static class NoiseBus
    {
        public static event Action<NoiseEvent> Emitted;

        public static void Emit(Vector3 position, float radius)
        {
            if (radius <= 0f) return;

            // The stalker only thinks on the host, so a guest's noise has to
            // be sent there to be heard at all.
            if (CoopHooks.NoiseMade != null && CoopHooks.NoiseMade(position, radius)) return;

            EmitLocal(position, radius);
        }

        // Raised here without forwarding - for the server re-raising a noise
        // a guest sent, which must not bounce back again.
        public static void EmitLocal(Vector3 position, float radius)
        {
            Emitted?.Invoke(new NoiseEvent { position = position, radius = radius });
        }
    }
}
