using Unity.Netcode;
using UnityEngine;
using TMPro;

public class DeliveryZone : NetworkBehaviour
{
    [SerializeField] private int requiredCans = 2;
    private NetworkVariable<int> collectedCans = new NetworkVariable<int>(0);
    [SerializeField] private TMP_Text progressUI;

    public override void OnNetworkSpawn()
    {
        collectedCans.OnValueChanged += UpdateUI;
        UpdateUI(0, collectedCans.Value);
    }

    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            NetworkObject playerNetObj = other.GetComponent<NetworkObject>();
            if (playerNetObj != null && playerNetObj.IsOwner && Input.GetKeyDown(KeyCode.E))
            {
                PlayerInventory inventory = other.GetComponent<PlayerInventory>();
                if (inventory != null && inventory.IsHoldingItem())
                {
                    DepositCanServerRpc();
                    inventory.DepositFoodCan();
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositCanServerRpc()
    {
        collectedCans.Value++;

        if (collectedCans.Value >= requiredCans)
        {
            CompleteTaskClientRpc();
        }
    }

    [ClientRpc]
    void CompleteTaskClientRpc()
    {
        Debug.Log("TASK COMPLETE! All 12 cans delivered!");
        // Unlock door, spawn NPC, etc.
    }

    void UpdateUI(int oldValue, int newValue)
    {
        if (progressUI != null)
        {
            progressUI.text = $"Supply Cache: {newValue}/{requiredCans}";
        }
    }
}
