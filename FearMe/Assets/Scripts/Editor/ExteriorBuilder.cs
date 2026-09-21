using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Builds everything outside the building: the moon, the ground it stands
    // on, a treeline that closes the site in, a perimeter fence and a far
    // ridge on the horizon. Windows cut into the outer shell look out onto
    // this instead of a black void.
    //
    // Two rules keep the exterior from leaking into gameplay:
    //   - every object stays on the Default layer, so the NavMesh bake and the
    //     stalker's vision mask (both scoped to "Level") ignore it entirely;
    //   - colliders are stripped, since the player can never reach any of it.
    //
    // The moon is a directional light plus the sun disk of a procedural night
    // skybox, so the disk itself is drawn at infinity and the interior fog
    // never swallows it. Its culling mask is Default-only for now - see
    // MoonCullingLayers.
    public static class ExteriorBuilder
    {
        private const string RootName = "Exterior";
        private const string MoonName = "Moon";
        private const string SkyboxPath = "Assets/Materials/Night_Sky.mat";

        // Fallback footprint, used when nothing better can be measured.
        private const float DefaultHalfWidth = 40f;
        private const float DefaultHalfDepth = 30f;

        // The ground sits flush with the underside of the interior floor slab,
        // so the building reads as standing on it rather than floating.
        private const float GroundDrop = 0.5f;
        private const float GroundExtent = 500f;

        private const float FenceOffset = 7f;
        private const float GateHalfWidth = 3.5f;
        private const float FencePostSpacing = 6f;
        private const float FenceHeight = 1.6f;

        // The exit is in the middle of the south wall. Leave a gap this wide so
        // the way out reads as a way out and not a wall of trees.
        private const float ExitGapHalfWidth = 11f;

        private const float RidgeRadius = 190f;
        private const int RidgeCount = 20;

        // Fixed so a rebuild produces the same wood, not a different one.
        private const int Seed = 91117;

        // Moonlight is confined to the exterior until windows are cut into the
        // outer shell. Widen this to include the Level layer once they are, and
        // the moon will throw shafts across the interior floor.
        private const string MoonCullingLayers = "Default,TransparentFX,Water,UI";

        private struct Ring
        {
            public float Offset;    // metres beyond the building footprint
            public int Count;
            public float MinHeight;
            public float MaxHeight;

            public Ring(float offset, int count, float minHeight, float maxHeight)
            {
                Offset = offset;
                Count = count;
                MinHeight = minHeight;
                MaxHeight = maxHeight;
            }
        }

        // Close ranks first, then thinning out. Note that BuildAtmosphere's fog
        // (ExponentialSquared, density 0.045) leaves roughly 65% of the first
        // ring visible, 15% of the second and almost nothing beyond: the outer
        // rings and the ridge only appear if that density comes down. They are
        // built anyway so thinning the fog is a one-number change.
        private static readonly Ring[] Rings =
        {
            new Ring(14f, 34, 7f, 11f),
            new Ring(30f, 30, 8f, 13f),
            new Ring(52f, 24, 9f, 15f),
            new Ring(80f, 18, 10f, 16f)
        };

        [MenuItem("Tools/FearMe/Build Exterior (current scene)")]
        public static void BuildForCurrentScene()
        {
            Vector2 footprint = MeasureFootprint();
            Build(footprint.x, footprint.y, -GroundDrop);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[FearMe] Exterior built around a " + (footprint.x * 2f) + " x " + (footprint.y * 2f) +
                " footprint. Cut windows into the outer walls to see it.");
        }

        // Ground sits flush with the underside of the interior floor slab.
        internal static Transform Build(float halfWidth, float halfDepth)
        {
            return Build(halfWidth, halfDepth, -GroundDrop);
        }

        // Rebuilds the exterior from scratch; an existing one is replaced so
        // this can be run repeatedly while tuning the numbers above.
        internal static Transform Build(float halfWidth, float halfDepth, float groundY)
        {
            GameObject stale = GameObject.Find(RootName);
            if (stale != null) Object.DestroyImmediate(stale);

            GameObject root = new GameObject(RootName);
            Transform t = root.transform;

            Light moon = BuildMoon(t);
            BuildNightSky(moon);
            BuildGround(t, groundY);
            BuildTreeline(t, halfWidth, halfDepth, groundY);
            BuildFence(t, halfWidth, halfDepth, groundY);
            BuildAccessTrack(t, halfDepth, groundY);
            BuildRidge(t, groundY);

            return t;
        }

        private static Light BuildMoon(Transform parent)
        {
            GameObject go = new GameObject(MoonName);
            go.transform.SetParent(parent, false);

            // High and behind the north-west corner, so corridors running that
            // way catch it and the disk clears the ridge.
            go.transform.rotation = Quaternion.Euler(26f, 212f, 0f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.62f, 0.72f, 0.95f);
            light.intensity = 0.32f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            light.cullingMask = LayerMask.GetMask(MoonCullingLayers.Split(','));

            return light;
        }

        private static void BuildNightSky(Light moon)
        {
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (sky == null)
            {
                Shader shader = Shader.Find("Skybox/Procedural");
                if (shader == null)
                {
                    Debug.LogWarning("[FearMe] Skybox/Procedural is missing, so the moon has no disk. " +
                        "Add it under Project Settings > Graphics > Always Included Shaders.");
                    return;
                }

                sky = new Material(shader);
                sky.SetFloat("_SunDisk", 2f);            // high quality, so it stays round
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 10f);
                sky.SetFloat("_AtmosphereThickness", 0.35f);
                sky.SetColor("_SkyTint", new Color(0.07f, 0.09f, 0.15f));
                sky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
                sky.SetFloat("_Exposure", 0.45f);

                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");
                AssetDatabase.CreateAsset(sky, SkyboxPath);
            }

            RenderSettings.skybox = sky;

            // The disk is drawn wherever this light points, which is what makes
            // it the moon rather than a second sun.
            RenderSettings.sun = moon;

            // Ambient stays Flat (set in BuildAtmosphere) on purpose: the sky is
            // there to be looked at, not to raise the interior black level.
            RenderSettings.ambientMode = AmbientMode.Flat;
        }

        private static void BuildGround(Transform parent, float groundY)
        {
            Material mat = DemoSceneBuilder.GetOrCreateMaterial("Exterior_Ground", new Color(0.055f, 0.06f, 0.055f));

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Ground";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, groundY, 0f);
            go.transform.localScale = Vector3.one * (GroundExtent / 10f); // a Plane is 10 units across
            Finish(go, mat);
        }

        private static void BuildTreeline(Transform parent, float halfWidth, float halfDepth, float groundY)
        {
            Material trunkMat = DemoSceneBuilder.GetOrCreateMaterial("Exterior_Trunk", new Color(0.07f, 0.06f, 0.05f));
            Material canopyMat = DemoSceneBuilder.GetOrCreateMaterial("Exterior_Canopy", new Color(0.05f, 0.09f, 0.07f));

            GameObject treeline = new GameObject("Treeline");
            treeline.transform.SetParent(parent, false);

            System.Random rng = new System.Random(Seed);
            int planted = 0;

            foreach (Ring ring in Rings)
            {
                float a = halfWidth + ring.Offset;
                float b = halfDepth + ring.Offset;

                for (int i = 0; i < ring.Count; i++)
                {
                    // Jitter the angle and radius so the ring never reads as a ring.
                    float angle = (i + (float)rng.NextDouble() * 0.7f - 0.35f) / ring.Count * Mathf.PI * 2f;
                    float stretch = 1f + ((float)rng.NextDouble() * 0.3f - 0.1f);

                    Vector3 pos = new Vector3(a * stretch * Mathf.Cos(angle), groundY, b * stretch * Mathf.Sin(angle));
                    if (InExitGap(pos, halfDepth)) continue;

                    float height = Mathf.Lerp(ring.MinHeight, ring.MaxHeight, (float)rng.NextDouble());
                    BuildTree(treeline.transform, "Tree_" + planted, pos, height, rng, trunkMat, canopyMat);
                    planted++;
                }
            }
        }

        // Keeps the wedge due south of the exit clear of trees.
        private static bool InExitGap(Vector3 pos, float halfDepth)
        {
            return pos.z < -halfDepth && Mathf.Abs(pos.x) < ExitGapHalfWidth;
        }

        // Placeholder conifer: one trunk and three flattened spheres tapering
        // upward. Swap the whole GameObject for a real model later - the
        // positions are what matter.
        private static void BuildTree(Transform parent, string name, Vector3 basePos, float height,
            System.Random rng, Material trunkMat, Material canopyMat)
        {
            GameObject tree = new GameObject(name);
            tree.transform.SetParent(parent, false);
            tree.transform.position = basePos;
            tree.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            float trunkHeight = height * 0.45f;
            float trunkRadius = Mathf.Lerp(0.14f, 0.24f, (float)rng.NextDouble());

            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(tree.transform, false);
            trunk.transform.localPosition = new Vector3(0f, trunkHeight * 0.5f, 0f);
            // A Cylinder is 2 units tall and 1 across at scale 1.
            trunk.transform.localScale = new Vector3(trunkRadius * 2f, trunkHeight * 0.5f, trunkRadius * 2f);
            Finish(trunk, trunkMat);

            float radius = height * 0.26f;
            float y = trunkHeight * 0.8f;

            for (int tier = 0; tier < 3; tier++)
            {
                GameObject canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                canopy.name = "Canopy_" + tier;
                canopy.transform.SetParent(tree.transform, false);
                canopy.transform.localPosition = new Vector3(0f, y + radius * 0.55f, 0f);
                canopy.transform.localScale = new Vector3(radius * 2f, radius * 1.5f, radius * 2f);
                Finish(canopy, canopyMat);

                y += radius * 0.95f;
                radius *= 0.72f;
            }
        }

        private static void BuildFence(Transform parent, float halfWidth, float halfDepth, float groundY)
        {
            Material mat = DemoSceneBuilder.GetOrCreateMaterial("Exterior_Fence", new Color(0.1f, 0.1f, 0.11f));

            GameObject fence = new GameObject("Fence");
            fence.transform.SetParent(parent, false);
            Transform f = fence.transform;

            float x = halfWidth + FenceOffset;
            float z = halfDepth + FenceOffset;

            BuildFenceRun(f, mat, new Vector3(-x, groundY, z), new Vector3(x, groundY, z), "North");
            // The south run breaks for a gate, so the track out is not fenced off.
            BuildFenceRun(f, mat, new Vector3(-x, groundY, -z), new Vector3(-GateHalfWidth, groundY, -z), "SouthWest");
            BuildFenceRun(f, mat, new Vector3(GateHalfWidth, groundY, -z), new Vector3(x, groundY, -z), "SouthEast");
            BuildFenceRun(f, mat, new Vector3(-x, groundY, -z), new Vector3(-x, groundY, z), "West");
            BuildFenceRun(f, mat, new Vector3(x, groundY, -z), new Vector3(x, groundY, z), "East");
        }

        private static void BuildFenceRun(Transform parent, Material mat, Vector3 from, Vector3 to, string name)
        {
            GameObject run = new GameObject("Fence_" + name);
            run.transform.SetParent(parent, false);

            Vector3 delta = to - from;
            float length = delta.magnitude;
            Vector3 dir = delta / length;
            Vector3 mid = (from + to) * 0.5f;

            // Two rails, one cube each, rotated to lie along the run.
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            foreach (float railY in new[] { FenceHeight * 0.45f, FenceHeight * 0.95f })
            {
                GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = "Rail_" + Mathf.RoundToInt(railY * 100f);
                rail.transform.SetParent(run.transform, false);
                rail.transform.position = mid + Vector3.up * railY;
                rail.transform.rotation = rot;
                rail.transform.localScale = new Vector3(0.06f, 0.06f, length);
                Finish(rail, mat);
            }

            int posts = Mathf.Max(2, Mathf.RoundToInt(length / FencePostSpacing));
            for (int i = 0; i <= posts; i++)
            {
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "Post_" + i;
                post.transform.SetParent(run.transform, false);
                post.transform.position = from + dir * (length * i / posts) + Vector3.up * (FenceHeight * 0.5f);
                post.transform.localScale = new Vector3(0.12f, FenceHeight, 0.12f);
                Finish(post, mat);
            }
        }

        // The track out of the exit runs a little way south and then stops in
        // the trees: somewhere to walk to, nowhere to go.
        private static void BuildAccessTrack(Transform parent, float halfDepth, float groundY)
        {
            Material mat = DemoSceneBuilder.GetOrCreateMaterial("Exterior_Track", new Color(0.1f, 0.1f, 0.095f));

            GameObject track = GameObject.CreatePrimitive(PrimitiveType.Cube);
            track.name = "AccessTrack";
            track.transform.SetParent(parent, false);
            // Sits a hair above the ground so it wins the depth test against it.
            track.transform.position = new Vector3(0f, groundY + 0.02f, -(halfDepth + 20f));
            track.transform.localScale = new Vector3(5f, 0.04f, 40f);
            Finish(track, mat);
        }

        // A ring of low hills at the edge of sight. Most of each one is under
        // the ground plane, so only the crest shows and the horizon closes.
        private static void BuildRidge(Transform parent, float groundY)
        {
            Material mat = DemoSceneBuilder.GetOrCreateMaterial("Exterior_Ridge", new Color(0.035f, 0.04f, 0.045f));

            GameObject ridge = new GameObject("Ridge");
            ridge.transform.SetParent(parent, false);

            System.Random rng = new System.Random(Seed + 1);

            for (int i = 0; i < RidgeCount; i++)
            {
                float angle = i / (float)RidgeCount * Mathf.PI * 2f;
                float radius = RidgeRadius * (1f + (float)rng.NextDouble() * 0.2f);
                float width = Mathf.Lerp(70f, 110f, (float)rng.NextDouble());
                float rise = Mathf.Lerp(14f, 26f, (float)rng.NextDouble());

                GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = "Hill_" + i;
                hill.transform.SetParent(ridge.transform, false);
                hill.transform.position = new Vector3(
                    radius * Mathf.Cos(angle), groundY - rise * 0.55f, radius * Mathf.Sin(angle));
                hill.transform.localScale = new Vector3(width, rise * 2f, width);
                Finish(hill, mat);
            }
        }

        // Decoration only: no collider, off the Level layer, and batched.
        private static void Finish(GameObject go, Material mat)
        {
            go.layer = 0;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // Prefers the built level's own bounds so the treeline hugs whatever is
        // actually in the scene, not the hospital's dimensions.
        private static Vector2 MeasureFootprint()
        {
            GameObject level = GameObject.Find("Level");
            if (level == null) return new Vector2(DefaultHalfWidth, DefaultHalfDepth);

            Renderer[] renderers = level.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Vector2(DefaultHalfWidth, DefaultHalfDepth);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            return new Vector2(
                Mathf.Max(DefaultHalfWidth * 0.25f, bounds.extents.x),
                Mathf.Max(DefaultHalfDepth * 0.25f, bounds.extents.z));
        }
    }
}
