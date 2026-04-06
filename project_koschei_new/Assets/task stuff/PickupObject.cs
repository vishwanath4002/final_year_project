using Unity.Netcode;
using UnityEngine;

public class PickupObject : NetworkBehaviour
{
    [Header("Pickup Prompt")]
    [SerializeField] private GameObject pickupPromptSprite;

    [Header("Stacking")]
    [Tooltip("Enable for food cans — allows up to 3 to stack in inventory.")]
    [SerializeField] private bool isStackableCan = false;

    private void Awake()
    {
        if (pickupPromptSprite != null)
            pickupPromptSprite.SetActive(false);
    }

    // Called locally by ThirdPersonShooterController when crosshair enters/leaves
    public void ShowPickupPrompt(bool show)
    {
        if (pickupPromptSprite != null)
            pickupPromptSprite.SetActive(show);
    }

    // Returns true if the given inventory can currently receive this item
    public bool CanBePickedUpBy(PlayerInventory inventory)
    {
        if (inventory == null) return false;
        return isStackableCan ? inventory.CanPickupCan() : !inventory.IsHoldingItem();
    }

    public void TryPickup(GameObject playerObject)
    {
        if (playerObject == null) { Debug.LogError("playerObject is null in TryPickup!"); return; }

        NetworkObject playerNetObj = playerObject.GetComponentInParent<NetworkObject>()
                                  ?? playerObject.GetComponent<NetworkObject>();

        if (playerNetObj == null)    { Debug.LogError($"Player '{playerObject.name}' has no NetworkObject!"); return; }
        if (!playerNetObj.IsSpawned) { Debug.LogError("Player NetworkObject is not spawned yet!"); return; }

        PlayerInventory inventory = playerObject.GetComponent<PlayerInventory>()
                                 ?? playerObject.GetComponentInParent<PlayerInventory>();

        if (inventory == null)
        {
            Debug.LogError("PlayerInventory not found on player!");
            return;
        }

        if (CanBePickedUpBy(inventory))
        {
            Debug.Log($"Calling TryPickupServerRpc for player {playerNetObj.OwnerClientId}");
            TryPickupServerRpc(playerNetObj.OwnerClientId);
        }
        else
        {
            Debug.Log(isStackableCan
                ? "[Pickup] Can stack is full (3/3)."
                : "[Pickup] Already holding an item.");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void TryPickupServerRpc(ulong playerId)
    {
        Debug.Log($"[SERVER] TryPickupServerRpc called for player {playerId}");

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        { Debug.LogError($"Client {playerId} not found."); return; }

        if (client.PlayerObject == null)
        { Debug.LogError($"Client {playerId} has null PlayerObject!"); return; }

        PlayerInventory inventory = client.PlayerObject.GetComponent<PlayerInventory>();
        if (inventory == null)
        { Debug.LogError($"PlayerInventory not found on client {playerId}!"); return; }

        if (!CanBePickedUpBy(inventory))
        {
            Debug.Log($"[SERVER] Player {playerId} can't receive this item right now.");
            return;
        }

        GameObject prefabReference = FindPrefabInNetworkList();
        if (prefabReference == null)
        { Debug.LogError($"Prefab not found in NetworkPrefabs for: {gameObject.name}"); return; }

        if (isStackableCan)
            inventory.PickupCan(prefabReference);
        else
            inventory.PickupItem(prefabReference);

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
        Destroy(gameObject);
    }

    GameObject FindPrefabInNetworkList()
    {
        string cleanName = gameObject.name.Replace("(Clone)", "").Trim();
        if (NetworkManager.Singleton == null) return null;

        foreach (var np in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            if (np.Prefab.name == cleanName)
                return np.Prefab;

        Debug.LogError($"No matching prefab for: {cleanName}");
        return null;
    }
}