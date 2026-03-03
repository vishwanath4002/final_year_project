using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    [Header("Spawn Positions")]
    public List<Vector3> spawnPositions = new List<Vector3>()
    {
        new Vector3(550, 20, 475),
        new Vector3(560, 20, 475),
        new Vector3(540, 20, 475),
        new Vector3(530, 20, 475)
    };

    // Tracks which index to give to the next connecting client
    private int nextSpawnIndex = 0;

    private void Awake()
    {
        // Must be set in Awake -- Start() is too late, host has already connected by then
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[SpawnManager] NetworkManager.Singleton is null in Awake!");
            return;
        }

        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;

        // Subscribe to client connected so we can teleport the host immediately
        // (host bypasses ApprovalCheck for its own player object)
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        Debug.Log("[SpawnManager] Approval callback registered.");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

            // Clear the approval callback so it doesn't persist across scenes
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }
    }

    // ----------------------------------------------------------------
    // Called for every client that connects -- INCLUDING the host
    // We use this to teleport the host to their spawn point since
    // the host bypasses ConnectionApprovalCallback for its own player
    // ----------------------------------------------------------------
    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // Only handle the host's own connection here
        // All other clients are already positioned via ApprovalCheck
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                Vector3 spawnPos = GetNextSpawnPosition();
                client.PlayerObject.transform.position = spawnPos;
                Debug.Log($"[SpawnManager] Host (client {clientId}) teleported to {spawnPos}");
            }
        }
    }

    // ----------------------------------------------------------------
    // Called for every non-host client connecting
    // ----------------------------------------------------------------
    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Rotation = Quaternion.identity;
        response.Pending = false;

        Vector3 spawnPos = GetNextSpawnPosition();
        response.Position = spawnPos;

        Debug.Log($"[SpawnManager] Client {request.ClientNetworkId} approved. Spawn: {spawnPos}");
    }

    // ----------------------------------------------------------------
    // Returns the next available spawn position in sequence
    // Wraps around if more players connect than positions available
    // ----------------------------------------------------------------
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
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    [Header("Spawn Positions")]
    public List<Vector3> spawnPositions = new List<Vector3>()
    {
        new Vector3(550, 20, 475),
        new Vector3(560, 20, 475),
        new Vector3(540, 20, 475),
        new Vector3(530, 20, 475)
    };

    // Tracks which index to give to the next connecting client
    private int nextSpawnIndex = 0;

    private void Awake()
    {
        // Must be set in Awake -- Start() is too late, host has already connected by then
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[SpawnManager] NetworkManager.Singleton is null in Awake!");
            return;
        }

        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;

        // Subscribe to client connected so we can teleport the host immediately
        // (host bypasses ApprovalCheck for its own player object)
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        Debug.Log("[SpawnManager] Approval callback registered.");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

            // Clear the approval callback so it doesn't persist across scenes
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }
    }

    // ----------------------------------------------------------------
    // Called for every client that connects -- INCLUDING the host
    // We use this to teleport the host to their spawn point since
    // the host bypasses ConnectionApprovalCallback for its own player
    // ----------------------------------------------------------------
    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // Only handle the host's own connection here
        // All other clients are already positioned via ApprovalCheck
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                Vector3 spawnPos = GetNextSpawnPosition();
                client.PlayerObject.transform.position = spawnPos;
                Debug.Log($"[SpawnManager] Host (client {clientId}) teleported to {spawnPos}");
            }
        }
    }

    // ----------------------------------------------------------------
    // Called for every non-host client connecting
    // ----------------------------------------------------------------
    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Rotation = Quaternion.identity;
        response.Pending = false;

        Vector3 spawnPos = GetNextSpawnPosition();
        response.Position = spawnPos;

        Debug.Log($"[SpawnManager] Client {request.ClientNetworkId} approved. Spawn: {spawnPos}");
    }

    // ----------------------------------------------------------------
    // Returns the next available spawn position in sequence
    // Wraps around if more players connect than positions available
    // ----------------------------------------------------------------
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
