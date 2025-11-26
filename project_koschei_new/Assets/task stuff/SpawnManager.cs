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
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
    }

    void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = true;

        int currentPlayers = NetworkManager.Singleton.ConnectedClients.Count;
        int spawnIndex = Mathf.Clamp(currentPlayers, 0, spawnPositions.Count - 1);
        response.Position = spawnPositions[spawnIndex];
        response.Rotation = Quaternion.identity;
        response.Pending = false;
    }
}
