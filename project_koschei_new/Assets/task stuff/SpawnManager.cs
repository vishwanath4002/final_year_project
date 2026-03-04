using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Koshcei
{
    public class SpawnManager : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Inspector
        // -----------------------------------------------------------------------
        [Header("Player Prefab")]
        [Tooltip("Must also be registered in NetworkManager's NetworkPrefabs list.")]
        [SerializeField] private NetworkObject playerPrefab;

        [Header("Spawn Positions")]
        public List<Vector3> spawnPositions = new List<Vector3>()
        {
            new Vector3(550, 20, 475),
            new Vector3(560, 20, 475),
            new Vector3(540, 20, 475),
            new Vector3(530, 20, 475)
        };

        // -----------------------------------------------------------------------
        // Private
        // -----------------------------------------------------------------------
        private int nextSpawnIndex = 0;

        // Prevents double-spawning if both triggers somehow fire for the same client
        private readonly HashSet<ulong> spawnedClients = new HashSet<ulong>();

        // -----------------------------------------------------------------------
        // Awake – server/host only setup
        // -----------------------------------------------------------------------
        private void Awake()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[SpawnManager] NetworkManager.Singleton is null!");
                return;
            }

            if (!NetworkManager.Singleton.IsServer) return;

            // Never auto-spawn at approval time — we control all spawning below
            NetworkManager.Singleton.ConnectionApprovalCallback = (request, response) =>
            {
                response.Approved = true;
                response.CreatePlayerObject = false;
                response.Pending = false;
                Debug.Log($"[SpawnManager] Client {request.ClientNetworkId} approved (manual spawn).");
            };

            // ---------------------------------------------------------------
            // TWO triggers cover both connection timings:
            //
            // TRIGGER 1 — OnLoadEventCompleted
            //   NGO fires this after all clients that received the original
            //   SceneManager.LoadScene() call have finished loading. This
            //   covers clients who were already connected (via StartClient)
            //   BEFORE or DURING the host's LoadScene call.
            //
            // TRIGGER 2 — OnSceneEvent / SynchronizeComplete
            //   When a client connects AFTER the host already loaded the scene,
            //   NGO sends it a Synchronize packet (not a Load packet), so
            //   OnLoadEventCompleted never fires for that client.
            //   SynchronizeComplete fires on the SERVER once that specific
            //   late-joining client has finished loading the synchronized scene
            //   and is fully ready. This is the only safe moment to spawn
            //   for late joiners — any earlier and the spawn message arrives
            //   while the client is still loading, gets deferred, and after
            //   10 seconds gets purged (the "Deferred messages" warning).
            // ---------------------------------------------------------------
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoadComplete;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;

            Debug.Log("[SpawnManager] Server hooks registered.");
        }

        // -----------------------------------------------------------------------
        private void OnDestroy()
        {
            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.ConnectionApprovalCallback = null;

            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoadComplete;
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
        }

        // -----------------------------------------------------------------------
        // TRIGGER 1 — fires on server when all clients that received the original
        // LoadScene message have confirmed they finished loading.
        // Covers: host + any clients that were already connected during LoadScene.
        // -----------------------------------------------------------------------
        private void OnSceneLoadComplete(
            string sceneName,
            LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            if (sceneName != "Scene_A") return;

            if (clientsTimedOut.Count > 0)
                Debug.LogWarning($"[SpawnManager] {clientsTimedOut.Count} client(s) timed out on scene load.");

            Debug.Log($"[SpawnManager] OnLoadEventCompleted – spawning for {clientsCompleted.Count} client(s).");

            foreach (ulong clientId in clientsCompleted)
                TrySpawnPlayer(clientId);
        }

        // -----------------------------------------------------------------------
        // TRIGGER 2 — fires for every scene event on the server.
        // We filter for SynchronizeComplete, which is what NGO sends when a
        // LATE-JOINING client has finished loading the already-active scene.
        // At this point the client is fully in the scene and can receive spawn
        // messages immediately — no deferral risk.
        // -----------------------------------------------------------------------
        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            // Only care about late-joiner sync finishing, only on the server
            if (sceneEvent.SceneEventType != SceneEventType.SynchronizeComplete) return;
            if (sceneEvent.SceneName != "Scene_A") return;

            ulong clientId = sceneEvent.ClientId;
            Debug.Log($"[SpawnManager] SynchronizeComplete for late-joining client {clientId} – spawning.");
            TrySpawnPlayer(clientId);
        }

        // -----------------------------------------------------------------------
        // Guard + spawn
        // -----------------------------------------------------------------------
        private void TrySpawnPlayer(ulong clientId)
        {
            if (spawnedClients.Contains(clientId))
            {
                Debug.Log($"[SpawnManager] Client {clientId} already spawned – skipping.");
                return;
            }

            spawnedClients.Add(clientId);
            SpawnPlayerForClient(clientId);
        }

        private void SpawnPlayerForClient(ulong clientId)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[SpawnManager] playerPrefab is not assigned in the Inspector!");
                return;
            }

            Vector3 spawnPos = GetNextSpawnPosition();
            NetworkObject instance = Instantiate(playerPrefab, spawnPos, Quaternion.identity);

            // SpawnAsPlayerObject assigns ownership to clientId and registers it
            // as ConnectedClients[clientId].PlayerObject on the server.
            // Every already-connected client receives a spawn message immediately.
            instance.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            Debug.Log($"[SpawnManager] Spawned player for client {clientId} at {spawnPos}");
        }

        // -----------------------------------------------------------------------
        private Vector3 GetNextSpawnPosition()
        {
            if (spawnPositions == null || spawnPositions.Count == 0)
            {
                Debug.LogWarning("[SpawnManager] No spawn positions defined – using origin.");
                return Vector3.zero;
            }

            Vector3 pos = spawnPositions[nextSpawnIndex % spawnPositions.Count];
            nextSpawnIndex++;
            return pos;
        }
    }
}