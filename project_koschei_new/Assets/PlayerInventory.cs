using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private NetworkVariable<bool> holdingItem = new NetworkVariable<bool>(false);
    [SerializeField] private Transform handPosition;
    private GameObject heldItemVisual;

    public bool IsHoldingItem()
    {
        return holdingItem.Value;
    }

    public void PickupFoodCan()
    {
        if (IsServer)
        {
            holdingItem.Value = true;
            ShowItemClientRpc();
        }
    }

    public void DepositFoodCan()
    {
        if (IsServer)
        {
            holdingItem.Value = false;
            HideItemClientRpc();
        }
    }

    [ClientRpc]
    void ShowItemClientRpc()
    {
        // Visual feedback: show can in player's hand
        // You can instantiate a small can model here
    }

    [ClientRpc]
    void HideItemClientRpc()
    {
        if (heldItemVisual != null)
        {
            Destroy(heldItemVisual);
        }
    }
}

