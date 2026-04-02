using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CanDeliveryZone : NetworkBehaviour
{
    [Header("Setup")]
    [SerializeField] private int requiredCans = 12;
    [SerializeField] private Transform dropOffPoint;
    [SerializeField]
    private List<string> foodCanPrefabNames = new List<string>
    {
        "soda can", "spam can", "meat can", "can 2", "can small",
        "can tall 2", "can tall", "meat can box old", "meat can round", "meat can box"
    };

    [Header("Zone Marker")]
    [SerializeField] private GameObject zoneMarkerSprite;

    [Header("Deposit Prompt")]
    [SerializeField] private GameObject depositPromptUI;
    [SerializeField] private TMPro.TextMeshProUGUI depositPromptText;

    // Task1 listens to this (server-side only)
    public event Action OnCansComplete;

    // Progress event for live HUD counts (server-side only)
    public event Action<int, int> OnCanProgressChanged;

    private NetworkVariable<int> collectedCans = new NetworkVariable<int>(0);

    private bool playerInZone = false;
    private GameObject playerInTrigger = null;

    public int GetCollectedCans() => collectedCans.Value;
    public int GetRequiredCans() => requiredCans;

    // ================================================================
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        collectedCans.OnValueChanged += OnCansChanged;

        if (zoneMarkerSprite != null)
            zoneMarkerSprite.SetActive(collectedCans.Value < requiredCans);

        if (depositPromptUI != null)
            depositPromptUI.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        collectedCans.OnValueChanged -= OnCansChanged;
    }

    // ================================================================
    // NetworkVariable callback
    // ================================================================

    void OnCansChanged(int oldValue, int newValue)
    {
        Debug.Log($"Cans delivered: {newValue}/{requiredCans}");

        if (zoneMarkerSprite != null)
            zoneMarkerSprite.SetActive(newValue < requiredCans);

        if (newValue >= requiredCans)
        {
            Debug.Log("All cans delivered!");
            if (IsServer) OnCansComplete?.Invoke();
            CansCompleteClientRpc();
        }

        if (IsServer) OnCanProgressChanged?.Invoke(newValue, requiredCans);
    }

    // ================================================================
    // Trigger
    // ================================================================

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInZone = true;
        playerInTrigger = other.gameObject;

        var inv = other.GetComponent<PlayerInventory>();
        if (inv != null) inv.SetDropLocked(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInZone = false;
        playerInTrigger = null;

        var inv = other.GetComponent<PlayerInventory>();
        if (inv != null) inv.SetDropLocked(false);

        if (depositPromptUI != null)
            depositPromptUI.SetActive(false);
    }

    // ================================================================
    // Update
    // ================================================================

    void Update()
    {
        if (!playerInZone) return;

        PlayerInventory inventory = playerInTrigger != null
            ? playerInTrigger.GetComponent<PlayerInventory>()
            : null;

        UpdateDepositPrompt(inventory);

        if (!Input.GetKeyDown(KeyCode.E)) return;

        if (inventory != null && inventory.IsHoldingItem())
        {
            GameObject heldPrefab = inventory.GetHeldPrefab();
            if (heldPrefab != null && IsFoodCan(heldPrefab))
                DepositCanServerRpc(NetworkManager.Singleton.LocalClientId);
            else
                Debug.Log("Deposit option: NO. Item is not a valid food can.");
        }
        else
        {
            Debug.Log("Deposit option: NO. Not holding anything.");
        }
    }

    void UpdateDepositPrompt(PlayerInventory inv)
    {
        if (depositPromptUI == null) return;

        if (inv == null || !inv.IsHoldingItem())
        {
            depositPromptUI.SetActive(false);
            return;
        }

        GameObject held = inv.GetHeldPrefab();
        bool canDeposit = held != null && IsFoodCan(held) && collectedCans.Value < requiredCans;

        depositPromptUI.SetActive(canDeposit);
        if (canDeposit && depositPromptText != null)
            depositPromptText.text = "Press [E] to deposit Food Can";
    }

    // ================================================================
    // ServerRpc + helpers
    // ================================================================

    bool IsFoodCan(GameObject itemPrefab)
    {
        string cleanName = itemPrefab.name.Replace("(Clone)", "").Trim();
        return foodCanPrefabNames.Contains(cleanName);
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositCanServerRpc(ulong playerId)
    {
        if (collectedCans.Value >= requiredCans) return;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client)) return;

        PlayerInventory inventory = client.PlayerObject.GetComponent<PlayerInventory>();
        if (inventory == null || !inventory.IsHoldingItem()) return;

        GameObject canType = inventory.GetHeldPrefab();
        if (canType == null || !IsFoodCan(canType)) return;

        inventory.DepositItem();
        SpawnCanInZone(canType);
        collectedCans.Value++;
    }

    void SpawnCanInZone(GameObject canPrefabToSpawn)
    {
        if (canPrefabToSpawn == null) return;

        int canCount = collectedCans.Value;
        Vector3 spawnPos;

        if (dropOffPoint != null)
        {
            float offsetX = (canCount % 4) * 0.4f;
            float offsetZ = (canCount / 4) * 0.4f;
            spawnPos = dropOffPoint.position + new Vector3(offsetX, 0.5f, offsetZ);
        }
        else
        {
            BoxCollider box = GetComponent<BoxCollider>();
            Vector3 center = box != null ? transform.TransformPoint(box.center) : transform.position;
            spawnPos = center + new Vector3((canCount % 4) * 0.4f - 0.6f, 0.5f, (canCount / 4) * 0.4f - 0.6f);
        }

        GameObject depositedCan = Instantiate(canPrefabToSpawn, spawnPos, Quaternion.identity);

        // Disable pickup so deposited cans can't be picked back up
        var pickup = depositedCan.GetComponent<PickupObject>();
        if (pickup != null) pickup.enabled = false;

        foreach (Collider col in depositedCan.GetComponents<Collider>())
            if (col.isTrigger) col.enabled = false;

        NetworkObject netObj = depositedCan.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn();
    }

    [ClientRpc]
    void CansCompleteClientRpc()
    {
        Debug.Log("All cans delivered to the church!");
    }

    // ================================================================
    // Public helpers
    // ================================================================

    public void ActivateTask()
    {
        Debug.Log("[CanDeliveryZone] Task activated.");
    }

    public void ForceCompleteCanDelivery()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[CanDeliveryZone] ForceCompleteCanDelivery can only be called on server!");
            return;
        }
        collectedCans.Value = requiredCans;
        Debug.Log($"[CanDeliveryZone] Can delivery force completed! ({requiredCans}/{requiredCans})");
    }
}