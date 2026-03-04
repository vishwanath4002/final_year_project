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
        [Tooltip("Assign the NetworkObject player prefab here. Must also be registered in NetworkManager's Prefab list.")]
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

        // -----------------------------------------------------------------------
        // Awake – runs on all instances (host + all clients)
        // -----------------------------------------------------------------------
        private void Awake()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[SpawnManager] NetworkManager.Singleton is null!");
                return;
            }

            if (!NetworkManager.Singleton.IsServer) return;

            // ---------------------------------------------------------------
            // CRITICAL: Set CreatePlayerObject = false.
            //
            // We must NOT let NGO auto-spawn player objects at connection time.
            // If we do, the spawn message can arrive on the client BEFORE it
            // has finished loading the game scene, causing the
            // "Deferred messages … trigger not received within 10s" warning
            // and players not appearing on the wrong side.
            //
            // We will spawn manually in OnSceneLoadEventCompleted, which fires
            // only after every connected client has fully loaded the scene.
            // ---------------------------------------------------------------
            NetworkManager.Singleton.ConnectionApprovalCallback = (request, response) =>
            {
                response.Approved = true;
                response.CreatePlayerObject = false; // manual spawn below
                response.Pending = false;
                Debug.Log($"[SpawnManager] Client {request.ClientNetworkId} approved (no auto-spawn).");
            };

            // Subscribe to the scene-load-complete event.
            // OnLoadEventCompleted fires on the SERVER once every client that
            // was asked to load the scene has confirmed it finished loading.
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoadEventCompleted;

            Debug.Log("[SpawnManager] Awake – approval callback + scene listener registered.");
        }

        // -----------------------------------------------------------------------
        private void OnDestroy()
        {
            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.ConnectionApprovalCallback = null;

            if (NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoadEventCompleted;
        }

        // -----------------------------------------------------------------------
        // Called on the SERVER after all clients have finished loading the scene.
        // This is the ONLY safe place to spawn player objects – every client is
        // guaranteed to be in the correct scene and can immediately process the
        // spawn messages with no deferral needed.
        // -----------------------------------------------------------------------
        private void OnSceneLoadEventCompleted(
            string sceneName,
            LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            // Only act when our game scene finishes loading
            if (sceneName != "Scene_A") return;

            if (clientsTimedOut.Count > 0)
                Debug.LogWarning($"[SpawnManager] {clientsTimedOut.Count} client(s) timed out during scene load.");

            Debug.Log($"[SpawnManager] Scene load complete. Spawning {clientsCompleted.Count} player(s).");

            foreach (ulong clientId in clientsCompleted)
                SpawnPlayerForClient(clientId);
        }

        // -----------------------------------------------------------------------
        // Instantiate and network-spawn one player object for the given clientId.
        // Runs only on the server.
        // -----------------------------------------------------------------------
        private void SpawnPlayerForClient(ulong clientId)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[SpawnManager] playerPrefab is not assigned! " +
                               "Assign it in the Inspector and add it to NetworkManager's prefab list.");
                return;
            }

            Vector3 spawnPos = GetNextSpawnPosition();

            NetworkObject instance = Instantiate(playerPrefab, spawnPos, Quaternion.identity);

            // SpawnAsPlayerObject assigns ownership to clientId AND marks it as
            // that client's "PlayerObject" (accessible via ConnectedClients[id].PlayerObject).
            instance.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            Debug.Log($"[SpawnManager] Spawned player for client {clientId} at {spawnPos}");
        }

        // -----------------------------------------------------------------------
        private Vector3 GetNextSpawnPosition()
        {
            if (spawnPositions == null || spawnPositions.Count == 0)
            {
                Debug.LogWarning("[SpawnManager] No spawn positions defined! Spawning at origin.");
                return Vector3.zero;
            }

            Vector3 pos = spawnPositions[nextSpawnIndex % spawnPositions.Count];
            nextSpawnIndex++;
            return pos;
        }
    }
}