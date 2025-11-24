using Unity.Netcode;
using UnityEngine;

public class PickupObject : NetworkBehaviour
{
    private bool playerInRange = false;
    private GameObject nearbyPlayer = null;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            nearbyPlayer = other.gameObject;
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
        // Only local players can press E
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
                GameObject prefabReference = FindPrefabInNetworkList();
                if (prefabReference != null)
                {
                    inventory.PickupItem(prefabReference);
                    // Only the server should call Despawn!
                    if (NetworkObject != null && NetworkObject.IsSpawned)
                        NetworkObject.Despawn();
                    Destroy(gameObject);
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
        string cleanName = gameObject.name.Replace("(Clone)", "").Trim();
        var prefabList = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;

        foreach (var networkPrefab in prefabList)
        {
            if (networkPrefab.Prefab.name == cleanName)
            {
                return networkPrefab.Prefab;
            }
        }
        return null;
    }
}
