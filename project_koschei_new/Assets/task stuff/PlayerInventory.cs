using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    // ── Single-item slot (firewood, mushrooms, etc.) ──────────────
    private NetworkVariable<bool> holdingItem     = new NetworkVariable<bool>(false);
    private NetworkVariable<int>  heldPrefabIndex = new NetworkVariable<int>(-1);

    // ── Can stack (up to 3 food cans) ─────────────────────────────
    private NetworkList<int> canStack;
    private const int maxCanStack = 3;

    // ================================================================
    // Init
    // ================================================================

    private void Awake()
    {
        canStack = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        canStack.OnListChanged       += OnCanStackChanged;
        heldPrefabIndex.OnValueChanged += OnSingleItemChanged;
        holdingItem.OnValueChanged     += OnHoldingItemChanged;

        // Sync HUD state for late-joining owner
        if (IsOwner) RefreshHeldItemHUD();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        canStack.OnListChanged         -= OnCanStackChanged;
        heldPrefabIndex.OnValueChanged -= OnSingleItemChanged;
        holdingItem.OnValueChanged     -= OnHoldingItemChanged;
    }

    // ================================================================
    // HUD refresh callbacks (owner only)
    // ================================================================

    private void OnCanStackChanged(NetworkListEvent<int> changeEvent)
    {
        if (IsOwner) RefreshHeldItemHUD();
    }

    private void OnSingleItemChanged(int oldVal, int newVal)
    {
        if (IsOwner) RefreshHeldItemHUD();
    }

    private void OnHoldingItemChanged(bool oldVal, bool newVal)
    {
        if (IsOwner) RefreshHeldItemHUD();
    }

    private void RefreshHeldItemHUD()
    {
        if (PlayerHUD.Local == null) return;

        // Can stack takes priority over single item display
        if (canStack.Count > 0)
        {
            PlayerHUD.Local.UpdateCanInventory(BuildCanNameArray());
            return;
        }

        if (holdingItem.Value && heldPrefabIndex.Value >= 0)
        {
            var list = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
            if (heldPrefabIndex.Value < list.Count)
            {
                string itemName = list[heldPrefabIndex.Value].Prefab.name;
                PlayerHUD.Local.ShowHeldItem(itemName);
                return;
            }
        }

        // Nothing held
        PlayerHUD.Local.ClearHeldItem();
    }

    private string[] BuildCanNameArray()
    {
        var names      = new string[canStack.Count];
        var prefabList = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;

        for (int i = 0; i < canStack.Count; i++)
        {
            int idx  = canStack[i];
            names[i] = (idx >= 0 && idx < prefabList.Count)
                ? prefabList[idx].Prefab.name
                : "Can";
        }
        return names;
    }

    // ================================================================
    // Public queries
    // ================================================================

    public bool IsHoldingItem() => holdingItem.Value || canStack.Count > 0;

    public GameObject GetHeldPrefab()
    {
        if (holdingItem.Value && heldPrefabIndex.Value >= 0)
        {
            var list = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
            if (heldPrefabIndex.Value < list.Count)
                return list[heldPrefabIndex.Value].Prefab;
        }
        return GetTopCanPrefab();
    }

    // ── Can stack helpers ─────────────────────────────────────────

    public int  GetCanCount()  => canStack.Count;
    public bool CanPickupCan() => !holdingItem.Value && canStack.Count < maxCanStack;

    public GameObject GetCanPrefab(int stackIndex)
    {
        if (stackIndex < 0 || stackIndex >= canStack.Count) return null;
        var list = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
        int idx  = canStack[stackIndex];
        return (idx >= 0 && idx < list.Count) ? list[idx].Prefab : null;
    }

    public GameObject GetTopCanPrefab() =>
        canStack.Count > 0 ? GetCanPrefab(canStack.Count - 1) : null;

    // ================================================================
    // Drop lock (set by delivery zones)
    // ================================================================

    private bool dropLocked = false;
    public void SetDropLocked(bool locked) => dropLocked = locked;

    public void TryDropItem(Vector3 dropPosition)
    {
        if (dropLocked)
        {
            Debug.Log("[PlayerInventory] Drop blocked — inside a delivery zone.");
            return;
        }

        if (canStack.Count > 0)
            DropTopCanServerRpc(dropPosition);
        else if (holdingItem.Value)
            DropItemServerRpc(dropPosition);
    }

    // ================================================================
    // Single-item RPCs / server methods
    // ================================================================

    [ServerRpc(RequireOwnership = false)]
    public void DropItemServerRpc(Vector3 dropPosition)
    {
        if (!holdingItem.Value || heldPrefabIndex.Value < 0) return;

        GameObject prefab = GetHeldPrefab();
        if (prefab == null) return;

        SpawnDroppedObject(prefab, dropPosition);

        holdingItem.Value     = false;
        heldPrefabIndex.Value = -1;
        Debug.Log("[SERVER] Single item dropped.");
    }

    public void PickupItem(GameObject itemPrefab)
    {
        if (!IsServer) { Debug.LogError("PickupItem must run on server."); return; }

        int idx = FindPrefabIndex(itemPrefab);
        if (idx >= 0)
        {
            holdingItem.Value     = true;
            heldPrefabIndex.Value = idx;
            Debug.Log($"[SERVER] Picked up single item: {itemPrefab.name}");
        }
        else Debug.LogError($"Prefab not found in NetworkPrefabs: {itemPrefab.name}");
    }

    public void DepositItem()
    {
        if (!IsServer) { Debug.LogError("DepositItem must run on server."); return; }
        holdingItem.Value     = false;
        heldPrefabIndex.Value = -1;
        Debug.Log("[SERVER] Single item deposited.");
    }

    // ================================================================
    // Can stack RPCs / server methods
    // ================================================================

    [ServerRpc(RequireOwnership = false)]
    public void DropTopCanServerRpc(Vector3 dropPosition)
    {
        if (canStack.Count == 0) return;

        int lastIndex = canStack.Count - 1;
        int prefabIdx = canStack[lastIndex];
        canStack.RemoveAt(lastIndex);

        var list = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
        if (prefabIdx >= 0 && prefabIdx < list.Count)
            SpawnDroppedObject(list[prefabIdx].Prefab, dropPosition);

        Debug.Log($"[SERVER] Can dropped. Stack remaining: {canStack.Count}");
    }

    public void PickupCan(GameObject canPrefab)
    {
        if (!IsServer) { Debug.LogError("PickupCan must run on server."); return; }
        if (canStack.Count >= maxCanStack) { Debug.LogWarning("Can stack full."); return; }

        int idx = FindPrefabIndex(canPrefab);
        if (idx >= 0)
        {
            canStack.Add(idx);
            Debug.Log($"[SERVER] Can picked up: {canPrefab.name}, stack: {canStack.Count}/{maxCanStack}");
        }
        else Debug.LogError($"Can prefab not found: {canPrefab.name}");
    }

    public void DepositAllCans()
    {
        if (!IsServer) { Debug.LogError("DepositAllCans must run on server."); return; }
        canStack.Clear();
        Debug.Log("[SERVER] All cans deposited, stack cleared.");
    }

    // ================================================================
    // Shared helpers
    // ================================================================

    private void SpawnDroppedObject(GameObject prefab, Vector3 position)
    {
        GameObject dropped = Instantiate(prefab, position, Quaternion.identity);
        var netObj = dropped.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn();
        else { Debug.LogError($"Dropped prefab {prefab.name} has no NetworkObject!"); Destroy(dropped); }
    }

    private int FindPrefabIndex(GameObject prefab)
    {
        var list = NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Prefab == prefab || list[i].Prefab.name == prefab.name)
                return i;
        return -1;
    }
}