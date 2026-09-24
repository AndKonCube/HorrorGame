using UnityEngine;

namespace FearMe.Core
{
    // Materials for things built at runtime when no model has been assigned.
    // Made once and shared, so spawning a hundred pages does not leak a
    // hundred materials.
    public static class RuntimeMaterials
    {
        private static Material parchment;
        private static Material iron;

        // Aged paper, lit faintly gold from within.
        public static Material Parchment
        {
            get
            {
                if (parchment != null) return parchment;

                parchment = Make(new Color(0.86f, 0.78f, 0.6f), 0f, 0.2f);
                parchment.EnableKeyword("_EMISSION");
                parchment.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.28f) * 0.9f);
                return parchment;
            }
        }

        // Dark, heavy, and catches a torch beam.
        public static Material Iron => iron != null ? iron : iron = Make(new Color(0.32f, 0.3f, 0.27f), 0.9f, 0.55f);

        private static Material Make(Color colour, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            Material material = new Material(shader) { color = colour };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        // Swap the primitive's collider out; the pickup has its own.
        public static GameObject Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale,
            Vector3 rotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(rotation);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }
    }
}
