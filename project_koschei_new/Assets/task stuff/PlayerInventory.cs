using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private NetworkVariable<bool> holdingItem = new NetworkVariable<bool>(false);
    private NetworkVariable<int> heldPrefabIndex = new NetworkVariable<int>(-1); // -1 = nothing held

    public bool IsHoldingItem()
    {
        return holdingItem.Value;
    }

    public GameObject GetHeldPrefab()
    {
        if (heldPrefabIndex.Value < 0) return null;
        var prefabList = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
        if (heldPrefabIndex.Value < prefabList.Count)
        {
            return prefabList[heldPrefabIndex.Value].Prefab;
        }
        return null;
    }

    void Update()
    {
        if (IsOwner && Input.GetKeyDown(KeyCode.Q))
        {
            if (holdingItem.Value)
            {
                // Drop item above and in front of player
                Vector3 dropPos = transform.position + Vector3.up * 1f + transform.forward * 2f;
                DropItemServerRpc(dropPos);
            }
        }
    }

    public void PickupItem(GameObject itemPrefab)
    {
        if (IsServer)
        {
            int prefabIndex = FindPrefabIndex(itemPrefab);
            if (prefabIndex >= 0)
            {
                holdingItem.Value = true;
                heldPrefabIndex.Value = prefabIndex;
            }
            else
            {
                Debug.LogError($"Could not find {itemPrefab.name} in Network Prefabs List!");
            }
        }
    }

    public void DepositItem()
    {
        if (IsServer)
        {
            holdingItem.Value = false;
            heldPrefabIndex.Value = -1;
        }
    }

    [ServerRpc]
    void DropItemServerRpc(Vector3 dropPosition)
    {
        if (!holdingItem.Value || heldPrefabIndex.Value < 0)
        {
            return;
        }

        GameObject prefab = GetHeldPrefab();
        if (prefab == null) return;

        GameObject dropped = Instantiate(prefab, dropPosition, Quaternion.identity);
        var netObj = dropped.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
        else
        {
            Destroy(dropped);
        }

        holdingItem.Value = false;
        heldPrefabIndex.Value = -1;
    }

    int FindPrefabIndex(GameObject prefab)
    {
        var prefabList = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
        for (int i = 0; i < prefabList.Count; i++)
        {
            if (prefabList[i].Prefab == prefab || prefabList[i].Prefab.name == prefab.name)
                return i;
        }
        return -1;
    }
}
