using Unity.Netcode;
using UnityEngine;

public class FoodCan : NetworkBehaviour
{
    private NetworkVariable<bool> isCollected = new NetworkVariable<bool>(false);
    private bool playerInRange = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isCollected.OnValueChanged += OnCollectedChanged;

        if (isCollected.Value)
        {
            gameObject.SetActive(false);
        }
    }

    void OnCollectedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            gameObject.SetActive(false);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
        }
    }

    void Update()
    {
        if (!IsOwner && playerInRange && Input.GetKeyDown(KeyCode.E) && !isCollected.Value)
        {
            TryPickupServerRpc(NetworkManager.Singleton.LocalClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void TryPickupServerRpc(ulong playerId)
    {
        if (isCollected.Value) return;

        // Find player
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            PlayerInventory inventory = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inventory != null && !inventory.IsHoldingItem())
            {
                isCollected.Value = true;
                inventory.PickupFoodCan();
            }
        }
    }
}
