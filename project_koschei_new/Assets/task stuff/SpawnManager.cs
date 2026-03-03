using System.Collections;
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

    private int nextSpawnIndex = 0;

    private void Awake()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[SpawnManager] NetworkManager.Singleton is null!");
            return;
        }

        // Override the temporary callback set by LobbyManager before StartHost()
        // Handles all connecting CLIENTS with correct spawn positions
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;

        Debug.Log("[SpawnManager] Approval callback registered.");
    }

    private void Start()
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsServer) return;

        // Host bypasses ConnectionApprovalCallback for its own player object.
        // We wait here until the host's player object is actually spawned in
        // the scene, then teleport it. This is reliable regardless of load time.
        StartCoroutine(TeleportHostWhenReady());
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
    }

    // ----------------------------------------------------------------
    // Waits until the host's PlayerObject exists, then teleports it.
    // Runs only on the server/host.
    // ----------------------------------------------------------------
    private IEnumerator TeleportHostWhenReady()
    {
        ulong hostClientId = NetworkManager.Singleton.LocalClientId;
        NetworkObject playerObject = null;

        // Poll every frame until the player object is spawned
        while (playerObject == null)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(hostClientId, out var client))
                playerObject = client.PlayerObject;

            if (playerObject == null)
                yield return null;
        }

        Vector3 spawnPos = GetNextSpawnPosition();
        playerObject.transform.position = spawnPos;
        Debug.Log($"[SpawnManager] Host teleported to {spawnPos}");
    }

    // ----------------------------------------------------------------
    // Handles all non-host clients connecting
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
