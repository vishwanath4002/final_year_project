using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    [Header("Set spawn point coordinates (Vector3)")]
    public List<Vector3> spawnPositions = new List<Vector3>()
    {
        new Vector3(550,20,475),
        new Vector3(560,20,475),
        new Vector3(540,20,475),
        new Vector3(530,20,475)
    };

    void Start()
    {
        // Make sure NetworkManager exists before assigning callback
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
            Debug.Log("[SpawnManager] Connection approval callback set");
        }
        else
        {
            Debug.LogError("[SpawnManager] NetworkManager.Singleton is null!");
        }
    }

    void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = true;

        // FIXED: Use ConnectedClientsIds count BEFORE this client connects
        // The count includes the new client, so subtract 1 for the index
        int spawnIndex = NetworkManager.Singleton.ConnectedClientsIds.Count;
        
        // Make sure we don't go out of bounds
        spawnIndex = Mathf.Clamp(spawnIndex, 0, spawnPositions.Count - 1);
        
        response.Position = spawnPositions[spawnIndex];
        response.Rotation = Quaternion.identity;
        response.Pending = false;
        
        Debug.Log($"[SpawnManager] Client connecting. Spawn index: {spawnIndex}, Position: {spawnPositions[spawnIndex]}");
    }
}