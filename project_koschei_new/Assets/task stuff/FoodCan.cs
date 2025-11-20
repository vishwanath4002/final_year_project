using Unity.Netcode;
using UnityEngine;

public class FoodCan : NetworkBehaviour
{
    private bool playerInRange = false;
    private GameObject nearbyPlayer = null;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            nearbyPlayer = other.gameObject;
            Debug.Log($"Player near {gameObject.name} - Press E to pick up");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            nearbyPlayer = null;
        }
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            if (nearbyPlayer != null)
            {
                PlayerInventory inventory = nearbyPlayer.GetComponent<PlayerInventory>();

                if (inventory != null && !inventory.IsHoldingItem())
                {
                    TryPickupServerRpc(NetworkManager.Singleton.LocalClientId);
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void TryPickupServerRpc(ulong playerId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            PlayerInventory inventory = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inventory != null && !inventory.IsHoldingItem())
            {
                // Find the original prefab from Network Manager's list
                GameObject prefabReference = FindPrefabInNetworkList();

                if (prefabReference != null)
                {
                    // Give it to player
                    inventory.PickupFoodCan(prefabReference);

                    // Destroy this can
                    DestroyCanClientRpc();
                }
                else
                {
                    Debug.LogError($"Could not find prefab for {gameObject.name}!");
                }
            }
        }
    }

    GameObject FindPrefabInNetworkList()
    {
        // Get clean name (remove "(Clone)" suffix)
        string cleanName = gameObject.name.Replace("(Clone)", "").Trim();

        // Search in NetworkManager's prefab list
        var prefabList = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;

        foreach (var networkPrefab in prefabList)
        {
            if (networkPrefab.Prefab.name == cleanName)
            {
                Debug.Log($"Found prefab match: {cleanName}");
                return networkPrefab.Prefab;
            }
        }

        Debug.LogError($"Prefab not found in Network Prefabs List: {cleanName}");
        return null;
    }

    [ClientRpc]
    void DestroyCanClientRpc()
    {
        // Despawn and destroy on all clients
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
        Destroy(gameObject);
    }
}