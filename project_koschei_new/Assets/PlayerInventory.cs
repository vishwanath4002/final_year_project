using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private NetworkVariable<bool> holdingItem = new NetworkVariable<bool>(false);
    [SerializeField] private GameObject foodCanPrefab;

    public bool IsHoldingItem()
    {
        return holdingItem.Value;
    }

    void Update()
    {
        if (IsOwner && Input.GetKeyDown(KeyCode.Q))
        {
            if (holdingItem.Value)
            {
                // Calculate drop position in front of player
                Vector3 dropPos = transform.position + transform.forward * 2f;
                dropPos.y = 1f; // Make sure it's above ground

                DropCanServerRpc(dropPos);
            }
        }
    }

    public void PickupFoodCan()
    {
        if (IsServer)
        {
            holdingItem.Value = true;
            Debug.Log("Player picked up a can! (Press Q to drop, E in zone to deposit)");
        }
    }

    public void DepositFoodCan()
    {
        if (IsServer)
        {
            holdingItem.Value = false;
            Debug.Log("Player deposited a can!");
        }
    }

    [ServerRpc]
    void DropCanServerRpc(Vector3 dropPosition)
    {
        if (!holdingItem.Value) return;

        holdingItem.Value = false;

        if (foodCanPrefab != null)
        {
            // Spawn the can at the drop position
            GameObject droppedCan = Instantiate(foodCanPrefab, dropPosition, Quaternion.identity);

            // Get NetworkObject and spawn it on the network
            NetworkObject netObj = droppedCan.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
                Debug.Log($"Can dropped and spawned at {dropPosition}");
            }
            else
            {
                Debug.LogError("FoodCan prefab is missing NetworkObject component!");
                Destroy(droppedCan);
            }
        }
        else
        {
            Debug.LogError("Food Can Prefab is not assigned!");
        }
    }
}
