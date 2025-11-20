using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private NetworkVariable<bool> holdingItem = new NetworkVariable<bool>(false);
    private NetworkVariable<int> heldCanPrefabIndex = new NetworkVariable<int>(-1); // -1 = nothing held

    public bool IsHoldingItem()
    {
        return holdingItem.Value;
    }

    public GameObject GetHeldCanPrefab()
    {
        if (heldCanPrefabIndex.Value < 0) return null;

        // Get the prefab from NetworkManager's list
        var prefabList = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;

        if (heldCanPrefabIndex.Value < prefabList.Count)
        {
            return prefabList[heldCanPrefabIndex.Value].Prefab;
        }

        return null;
    }

    void Update()
    {
        if (IsOwner && Input.GetKeyDown(KeyCode.Q))
        {
            if (holdingItem.Value)
            {
                // Calculate drop position in front of player
                Vector3 dropPos = transform.position + transform.forward * 2f;


                DropCanServerRpc(dropPos);
            }
        }
    }

    public void PickupFoodCan(GameObject canPrefab)
    {
        if (IsServer)
        {
            // Find the index of this prefab in the network list
            int prefabIndex = FindPrefabIndex(canPrefab);

            if (prefabIndex >= 0)
            {
                holdingItem.Value = true;
                heldCanPrefabIndex.Value = prefabIndex;
                Debug.Log($"Player picked up {canPrefab.name} (index {prefabIndex})! Press Q to drop, E in zone to deposit");
            }
            else
            {
                Debug.LogError($"Could not find {canPrefab.name} in Network Prefabs List!");
            }
        }
    }

    public void DepositFoodCan()
    {
        if (IsServer)
        {
            holdingItem.Value = false;
            heldCanPrefabIndex.Value = -1;
            Debug.Log("Player deposited a can!");
        }
    }

    [ServerRpc]
    void DropCanServerRpc(Vector3 dropPosition)
    {
        if (!holdingItem.Value || heldCanPrefabIndex.Value < 0)
        {
            Debug.LogWarning("Cannot drop - not holding anything!");
            return;
        }

        GameObject canPrefab = GetHeldCanPrefab();

        if (canPrefab == null)
        {
            Debug.LogError("Held can prefab is null!");
            return;
        }

        // Spawn the can type that was picked up
        GameObject droppedCan = Instantiate(canPrefab, dropPosition, Quaternion.identity);

        // Get NetworkObject and spawn it on the network
        NetworkObject netObj = droppedCan.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            Debug.Log($"{canPrefab.name} dropped and spawned at {dropPosition}");
        }
        else
        {
            Debug.LogError($"{canPrefab.name} prefab is missing NetworkObject component!");
            Destroy(droppedCan);
        }

        // Clear inventory
        holdingItem.Value = false;
        heldCanPrefabIndex.Value = -1;
    }

    int FindPrefabIndex(GameObject prefab)
    {
        var prefabList = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;

        for (int i = 0; i < prefabList.Count; i++)
        {
            if (prefabList[i].Prefab == prefab || prefabList[i].Prefab.name == prefab.name)
            {
                return i;
            }
        }

        return -1; // Not found
    }
}