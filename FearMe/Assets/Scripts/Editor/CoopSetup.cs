#if FEARME_COOP_ONLINE
using System.Collections.Generic;
using FearMe.AI;
using FearMe.Net.Online;
using FearMe.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FearMe.EditorTools
{
    // Everything online co-op needs that is an asset or a scene edit rather
    // than code:
    //
    //   Resources/Coop/CoopPlayerProxy.prefab  - the teammate's body
    //   Resources/Coop/CoopNetworkPrefabs.asset
    //   Resources/Coop/CoopNetwork.prefab       - NetworkManager + transport
    //
    // and, in the open gameplay scene, a NetworkRunState plus network
    // components on each stalker. Non-destructive: the scene's own player and
    // everything wired to it are left exactly as they are.
    public static class CoopSetup
    {
        private const string Folder = "Assets/Resources/Coop";
        private const string ProxyPath = Folder + "/CoopPlayerProxy.prefab";
        private const string PrefabListPath = Folder + "/CoopNetworkPrefabs.asset";
        private const string NetworkPath = Folder + "/CoopNetwork.prefab";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        [MenuItem("Tools/FearMe/Co-op/Set Up Co-op (open the gameplay scene first)")]
        public static void SetUp()
        {
            EnsureFolder();

            GameObject proxy = BuildProxyPrefab();
            NetworkPrefabsList list = BuildPrefabList(proxy);
            BuildNetworkPrefab(list);

            Scene scene = SceneManager.GetActiveScene();
            int stalkers = PrepareScene(proxy);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            CheckBuildScenes(scene);

            Debug.Log($"[FearMe] Co-op set up in '{scene.name}': run state added, {stalkers} stalker(s) " +
                "made server-driven, network prefabs under " + Folder + ". Save the scene.");
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Resources", "Coop");
        }

        // --- The teammate's body ------------------------------------------------

        // Kept if it already exists, so a real model dropped under Body
        // survives running this again. Delete the prefab to rebuild it.
        private static GameObject BuildProxyPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ProxyPath);
            if (existing != null) return existing;

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

            OwnerNetworkTransform sync = root.AddComponent<OwnerNetworkTransform>();
            sync.SyncRotAngleX = false;
            sync.SyncRotAngleZ = false;
            sync.SyncScaleX = false;
            sync.SyncScaleY = false;
            sync.SyncScaleZ = false;

            GameObject body = BuildBody(root.transform);
            Light torch = BuildTorch(root.transform);

            NetworkPlayer player = root.AddComponent<NetworkPlayer>();
            SetObject(player, "body", body);
            SetObject(player, "torchBeam", torch);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ProxyPath);
            Object.DestroyImmediate(root);
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

        // --- Network assets -----------------------------------------------------

        private static NetworkPrefabsList BuildPrefabList(GameObject proxy)
        {
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }

            bool listed = false;
            foreach (NetworkPrefab entry in list.PrefabList)
            {
                if (entry.Prefab == proxy) listed = true;
            }

            if (!listed) list.Add(new NetworkPrefab { Prefab = proxy });

            EditorUtility.SetDirty(list);
            return list;
        }

        private static void BuildNetworkPrefab(NetworkPrefabsList list)
        {
            GameObject root = new GameObject("CoopNetwork");

            UnityTransport transport = root.AddComponent<UnityTransport>();
            NetworkManager manager = root.AddComponent<NetworkManager>();

            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = true,
                // Proxies are spawned by NetworkRunState once the level has
                // loaded; an automatic player prefab would spawn in the menu.
                PlayerPrefab = null
            };
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { list };

            PrefabUtility.SaveAsPrefabAsset(root, NetworkPath);
            Object.DestroyImmediate(root);
        }

        // --- The open scene -----------------------------------------------------

        private static int PrepareScene(GameObject proxyPrefab)
        {
            NetworkRunState state = Object.FindFirstObjectByType<NetworkRunState>();
            if (state == null)
            {
                GameObject go = new GameObject("CoopRunState");
                go.AddComponent<NetworkObject>();
                state = go.AddComponent<NetworkRunState>();
            }
            SetObject(state, "playerProxyPrefab", proxyPrefab.GetComponent<NetworkObject>());

            // The stalker thinks on the server only; everyone else watches.
            int count = 0;
            foreach (EnemyStalkerAI stalker in Object.FindObjectsByType<EnemyStalkerAI>(FindObjectsSortMode.None))
            {
                GameObject go = stalker.gameObject;

                if (go.GetComponent<NetworkObject>() == null) go.AddComponent<NetworkObject>();
                if (go.GetComponent<NetworkTransform>() == null)
                {
                    NetworkTransform sync = go.AddComponent<NetworkTransform>();
                    sync.SyncRotAngleX = false;
                    sync.SyncRotAngleZ = false;
                    sync.SyncScaleX = false;
                    sync.SyncScaleY = false;
                    sync.SyncScaleZ = false;
                }

                ServerOnly gate = go.GetComponent<ServerOnly>();
                if (gate == null) gate = go.AddComponent<ServerOnly>();

                List<Object> brains = new List<Object> { stalker };
                NavMeshAgent agent = go.GetComponent<NavMeshAgent>();
                if (agent != null) brains.Add(agent);
                SetObjectArray(gate, "serverOnly", brains.ToArray());

                count++;
            }

            return count;
        }

        // Netcode can only load scenes that are in the build list.
        private static void CheckBuildScenes(Scene scene)
        {
            bool menu = false, game = false;
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                if (!entry.enabled) continue;
                if (entry.path.EndsWith("/MainMenu.unity")) menu = true;
                if (entry.path == scene.path) game = true;
            }

            if (!menu) Debug.LogWarning("[FearMe] MainMenu is not in the build list - the lobby lives there.");
            if (!game) Debug.LogWarning($"[FearMe] '{scene.name}' is not in the build list - Netcode cannot load it.");
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

        private static void SetObjectArray(Object target, string field, Object[] values)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null) return;
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
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
