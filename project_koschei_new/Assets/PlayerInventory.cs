using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    private NetworkVariable<bool> holdingItem = new NetworkVariable<bool>(false);

    public bool IsHoldingItem()
    {
        return holdingItem.Value;
    }

    public void PickupFoodCan()
    {
        if (IsServer)
        {
            holdingItem.Value = true;
            Debug.Log("Player picked up a can!");
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
}
