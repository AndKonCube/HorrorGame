#if FEARME_COOP_ONLINE
using System.Collections.Generic;
using FearMe.Net.Online;
using FearMe.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FearMe.EditorTools
{
    // Everything online co-op needs as assets, kept in Resources/Coop:
    //
    //   CoopPlayerProxy.prefab    - the teammate's body
    //   CoopRunState.prefab       - the run's shared state, spawned by the host
    //   CoopNetworkPrefabs.asset  - both of the above, registered with Netcode
    //   CoopNetwork.prefab        - NetworkManager + transport
    //
    // Built automatically whenever scripts compile with online co-op on, so
    // there is nothing to add to any scene and nothing to forget to save: the
    // host spawns the run state itself when a level loads.
    [InitializeOnLoad]
    public static class CoopSetup
    {
        private const string Folder = "Assets/Resources/Coop";
        private const string ProxyPath = Folder + "/CoopPlayerProxy.prefab";
        private const string RunStatePath = Folder + "/CoopRunState.prefab";
        private const string PrefabListPath = Folder + "/CoopNetworkPrefabs.asset";
        private const string NetworkPath = Folder + "/CoopNetwork.prefab";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        static CoopSetup()
        {
            // After the editor has finished loading, never mid-import.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                EnsureAssets(quiet: true);
            };
        }

        [MenuItem("Tools/FearMe/Co-op/Set Up Co-op")]
        public static void SetUp()
        {
            EnsureAssets(quiet: false);
            Debug.Log("[FearMe] Co-op assets are in place under " + Folder + ". Nothing needs adding to " +
                "any scene: the host spawns the run state when a level loads. Make a fresh build for both players.");
        }

        public static void EnsureAssets(bool quiet)
        {
            EnsureFolder();

            bool changed = false;
            GameObject proxy = EnsureProxyPrefab(ref changed);
            GameObject runState = EnsureRunStatePrefab(proxy, ref changed);
            NetworkPrefabsList list = EnsurePrefabList(ref changed, proxy, runState);
            EnsureNetworkPrefab(list, ref changed);

            if (!changed) return;

            AssetDatabase.SaveAssets();
            if (quiet) Debug.Log("[FearMe] Co-op assets updated under " + Folder + ". Make a fresh build for both players.");
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Resources", "Coop");
        }

        // --- The teammate's body --------------------------------------------------

        private static GameObject EnsureProxyPrefab(ref bool changed)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ProxyPath);
            if (existing != null)
            {
                // Earlier builds moved it with a NetworkTransform; NetworkPlayer
                // now syncs its own position, and the two would fight.
                if (existing.GetComponent<NetworkTransform>() != null)
                {
                    GameObject contents = PrefabUtility.LoadPrefabContents(ProxyPath);
                    foreach (NetworkTransform old in contents.GetComponents<NetworkTransform>())
                        Object.DestroyImmediate(old);
                    PrefabUtility.SaveAsPrefabAsset(contents, ProxyPath);
                    PrefabUtility.UnloadPrefabContents(contents);
                    changed = true;
                }
                return AssetDatabase.LoadAssetAtPath<GameObject>(ProxyPath);
            }

            GameObject root = new GameObject("CoopPlayerProxy");

            CharacterController collider = root.AddComponent<CharacterController>();
            collider.height = 1.8f;
            collider.radius = 0.35f;
            collider.center = new Vector3(0f, 0.9f, 0f);

            // PlayerController requires one, but a remote body must never pair
            // with this machine's keyboard - so it is saved switched off.
            PlayerInput input = root.AddComponent<PlayerInput>();
            input.actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            input.enabled = false;

            PlayerController controller = root.AddComponent<PlayerController>();
            SetBool(controller, "isLocalPlayer", false);

            root.AddComponent<PlayerVitals>();
            root.AddComponent<NetworkObject>();

            GameObject body = BuildBody(root.transform);
            Light torch = BuildTorch(root.transform);

            NetworkPlayer player = root.AddComponent<NetworkPlayer>();
            SetObject(player, "body", body);
            SetObject(player, "torchBeam", torch);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ProxyPath);
            Object.DestroyImmediate(root);
            changed = true;
            return prefab;
        }

        // A plain dark figure - swap in a real model under Body whenever one
        // exists. Its own collider goes: the CharacterController already is one.
        private static GameObject BuildBody(Transform parent)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(parent, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            Renderer renderer = body.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            string materialPath = Folder + "/CoopPlayerBody.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null && shader != null)
            {
                material = new Material(shader) { color = new Color(0.18f, 0.18f, 0.2f) };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            if (material != null) renderer.sharedMaterial = material;

            return body;
        }

        // Seeing your partner's torch sweep a corridor is half of co-op horror.
        private static Light BuildTorch(Transform parent)
        {
            GameObject go = new GameObject("TorchBeam");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0.2f, 1.5f, 0.3f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 14f;
            light.spotAngle = 48f;
            light.intensity = 2.2f;
            light.color = new Color(1f, 0.93f, 0.8f);
            light.shadows = LightShadows.Soft;
            light.enabled = false;
            return light;
        }

        // --- The run's shared state ---------------------------------------------

        private static GameObject EnsureRunStatePrefab(GameObject proxy, ref bool changed)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(RunStatePath);
            if (existing != null)
            {
                NetworkRunState state = existing.GetComponent<NetworkRunState>();
                SerializedObject so = new SerializedObject(state);
                if (so.FindProperty("playerProxyPrefab").objectReferenceValue == null)
                {
                    SetObject(state, "playerProxyPrefab", proxy.GetComponent<NetworkObject>());
                    changed = true;
                }
                return existing;
            }

            GameObject root = new GameObject("CoopRunState");
            root.AddComponent<NetworkObject>();
            NetworkRunState runState = root.AddComponent<NetworkRunState>();
            SetObject(runState, "playerProxyPrefab", proxy.GetComponent<NetworkObject>());

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RunStatePath);
            Object.DestroyImmediate(root);
            changed = true;
            return prefab;
        }

        // --- Network assets -----------------------------------------------------

        private static NetworkPrefabsList EnsurePrefabList(ref bool changed, params GameObject[] prefabs)
        {
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
                changed = true;
            }

            foreach (GameObject prefab in prefabs)
            {
                bool listed = false;
                foreach (NetworkPrefab entry in list.PrefabList)
                    if (entry.Prefab == prefab) listed = true;

                if (listed) continue;
                list.Add(new NetworkPrefab { Prefab = prefab });
                changed = true;
            }

            if (changed) EditorUtility.SetDirty(list);
            return list;
        }

        private static void EnsureNetworkPrefab(NetworkPrefabsList list, ref bool changed)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPath);
            if (existing != null)
            {
                NetworkManager manager = existing.GetComponent<NetworkManager>();
                if (manager != null && manager.NetworkConfig != null &&
                    manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(list))
                    return;
            }

            GameObject root = new GameObject("CoopNetwork");

            UnityTransport transport = root.AddComponent<UnityTransport>();
            NetworkManager network = root.AddComponent<NetworkManager>();

            network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = true,
                // Proxies are spawned by the run state once the level has
                // loaded; an automatic player prefab would spawn in the menu.
                PlayerPrefab = null
            };
            network.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { list };

            PrefabUtility.SaveAsPrefabAsset(root, NetworkPath);
            Object.DestroyImmediate(root);
            changed = true;
        }

        // --- Serialized-field helpers ------------------------------------------

        private static void SetObject(Object target, string field, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[FearMe] Missing field '{field}' on {target.GetType().Name}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null) return;
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
