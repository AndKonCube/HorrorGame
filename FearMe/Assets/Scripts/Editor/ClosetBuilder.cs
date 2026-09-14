using FearMe.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Builds closets you can climb into: a cabinet open at the front, a door
    // that swings shut behind you, and an anchor marking where you stand.
    //
    // The interaction collider is deliberately NOT a trigger and covers only
    // the front face: PlayerInteractor's raycast ignores triggers, and a
    // collider filling the interior would fight the player standing in it.
    public static class ClosetBuilder
    {
        private const float Width = 1.4f;
        private const float Height = 2.2f;
        private const float Depth = 1.0f;
        private const float Panel = 0.1f;

        [MenuItem("Tools/FearMe/Upgrade Hiding Spots (current scene)")]
        public static void UpgradeCurrentScene()
        {
            HidingSpot[] spots = Object.FindObjectsByType<HidingSpot>(FindObjectsSortMode.None);
            if (spots.Length == 0)
            {
                Debug.LogWarning("[FearMe] No hiding spots in this scene.");
                return;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Demo_Wall.mat");
            int layer = LayerMask.NameToLayer("Level");
            if (layer < 0) layer = 0;

            int upgraded = 0;
            foreach (HidingSpot spot in spots)
            {
                // Already has a cabinet built around it.
                if (spot.transform.Find("Cabinet") != null) continue;

                Dress(spot, layer, mat);
                upgraded++;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[FearMe] Upgraded {upgraded} hiding spot(s) into closets. " +
                "Walk up and press E to climb in. Save the scene to keep it.");
        }

        // Fresh closet, used by the scene builders.
        internal static HidingSpot Create(Transform parent, Vector3 position, float yaw, int layer, Material mat)
        {
            GameObject root = new GameObject("Closet");
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            HidingSpot spot = root.AddComponent<HidingSpot>();
            Dress(spot, layer, mat);
            return spot;
        }

        // Adds the cabinet, door, anchors and interaction collider to a spot
        // that may be brand new or an old trigger volume.
        private static void Dress(HidingSpot spot, int layer, Material mat)
        {
            Transform root = spot.transform;

            GameObject cabinet = new GameObject("Cabinet");
            cabinet.transform.SetParent(root, false);
            Transform c = cabinet.transform;

            float half = Width * 0.5f;
            float backZ = -Depth * 0.5f;

            Panelling(c, "Back", new Vector3(0f, Height * 0.5f, backZ), new Vector3(Width, Height, Panel), layer, mat);
            Panelling(c, "Side_L", new Vector3(-half, Height * 0.5f, 0f), new Vector3(Panel, Height, Depth), layer, mat);
            Panelling(c, "Side_R", new Vector3(half, Height * 0.5f, 0f), new Vector3(Panel, Height, Depth), layer, mat);
            Panelling(c, "Top", new Vector3(0f, Height - Panel * 0.5f, 0f), new Vector3(Width, Panel, Depth), layer, mat);

            // Hinge on the left edge of the opening so the door swings outward.
            GameObject hinge = new GameObject("DoorHinge");
            hinge.transform.SetParent(root, false);
            hinge.transform.localPosition = new Vector3(-half, 0f, Depth * 0.5f);

            GameObject door = Panelling(hinge.transform, "Door",
                new Vector3(Width * 0.5f, Height * 0.5f, 0f),
                new Vector3(Width, Height, 0.08f), layer, mat);
            door.name = "Door";

            GameObject inside = new GameObject("InsideAnchor");
            inside.transform.SetParent(root, false);
            inside.transform.localPosition = new Vector3(0f, 0f, 0.05f);

            GameObject exit = new GameObject("ExitAnchor");
            exit.transform.SetParent(root, false);
            exit.transform.localPosition = new Vector3(0f, 0f, Depth * 0.5f + 0.9f);

            // Front face only, and solid so the interaction ray can hit it.
            BoxCollider box = spot.GetComponent<BoxCollider>();
            if (box == null) box = spot.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = new Vector3(0f, Height * 0.5f, Depth * 0.5f);
            box.size = new Vector3(Width, Height, 0.2f);

            spot.gameObject.layer = layer;

            SerializedObject so = new SerializedObject(spot);
            SetRef(so, "insideAnchor", inside.transform);
            SetRef(so, "exitAnchor", exit.transform);
            SetRef(so, "door", hinge.transform);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Panelling(Transform parent, string name, Vector3 localPosition,
            Vector3 size, int layer, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.layer = layer;

            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = value;
        }
    }
}
