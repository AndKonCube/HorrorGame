using UnityEngine;

namespace FearMe.Core
{
    [System.Serializable]
    public class FogProfile
    {
        public string label = "Profile";
        [Range(0f, 0.3f)] public float density = 0.045f;
        public Color color = new Color(0.03f, 0.035f, 0.04f);
        [Tooltip("Haze particles emitted per second while this profile is active.")]
        public float hazeRate = 14f;
        public float blendSpeed = 1.5f;
    }
}
