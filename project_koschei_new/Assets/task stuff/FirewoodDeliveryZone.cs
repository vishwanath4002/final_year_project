using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class FirewoodDeliveryZone : NetworkBehaviour
{
    [Header("Setup")]
    [SerializeField] private string firewoodPrefabName = "FireWood";
    [SerializeField] private List<GameObject> firewoodPileObjects;
    [SerializeField] private GameObject firePrefab;
    [SerializeField] private int requiredWood = 5;
    [SerializeField] private int requiredMushrooms = 13;

    [Header("Zone Marker")]
    [SerializeField] private GameObject zoneMarkerSprite;

    [Header("Deposit Prompt")]
    [SerializeField] private GameObject depositPromptUI;
    [SerializeField] private TMPro.TextMeshProUGUI depositPromptText;

    // Task1 listens to these (server-side only)
    public event Action OnFireLit;
    public event Action OnMushroomsComplete;

    // Progress events for live HUD counts (server-side only)
    public event Action<int, int> OnWoodProgressChanged;
    public event Action<int, int> OnMushroomProgressChanged;

    private NetworkVariable<int> depositedWood = new NetworkVariable<int>(0);
    private NetworkVariable<int> burnedMushrooms = new NetworkVariable<int>(0);
    private NetworkVariable<bool> fireIsActive = new NetworkVariable<bool>(false);

    private bool playerInZone = false;
    private GameObject playerInTrigger = null;

    // Public getters -- readable on all clients via NetworkVariable sync
    public int GetDepositedWood() => depositedWood.Value;
    public int GetBurnedMushrooms() => burnedMushrooms.Value;
    public bool GetFireIsActive() => fireIsActive.Value;
    public int GetRequiredWood() => requiredWood;
    public int GetRequiredMushrooms() => requiredMushrooms;

    // ================================================================
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        depositedWood.OnValueChanged += OnWoodChanged;
        burnedMushrooms.OnValueChanged += OnMushroomChanged;
        fireIsActive.OnValueChanged += OnFireActiveChanged;

        if (firePrefab != null)
            firePrefab.SetActive(fireIsActive.Value);

        if (zoneMarkerSprite != null)
            zoneMarkerSprite.SetActive(depositedWood.Value == 0);

        if (depositPromptUI != null)
            depositPromptUI.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        depositedWood.OnValueChanged -= OnWoodChanged;
        burnedMushrooms.OnValueChanged -= OnMushroomChanged;
        fireIsActive.OnValueChanged -= OnFireActiveChanged;
    }

    // ================================================================
    // NetworkVariable callbacks (run on ALL clients)
    // ================================================================

    void OnWoodChanged(int oldValue, int newValue)
    {
        for (int i = 0; i < firewoodPileObjects.Count; i++)
            if (firewoodPileObjects[i] != null)
                firewoodPileObjects[i].SetActive(i < newValue);

        if (zoneMarkerSprite != null)
            zoneMarkerSprite.SetActive(newValue == 0);

        Debug.Log($"Pile updated: {newValue}/{requiredWood} wood pieces active.");

        if (IsServer) OnWoodProgressChanged?.Invoke(newValue, requiredWood);
    }

    void OnMushroomChanged(int oldValue, int newValue)
    {
        Debug.Log($"Mushrooms burned: {newValue}/{requiredMushrooms}");
        if (newValue >= requiredMushrooms)
        {
            Debug.Log("All mushrooms burned!");
            if (IsServer) OnMushroomsComplete?.Invoke();
        }

        if (IsServer) OnMushroomProgressChanged?.Invoke(newValue, requiredMushrooms);
    }

    void OnFireActiveChanged(bool oldValue, bool newValue)
    {
        ApplyFireVisual(newValue);
        Debug.Log($"[FirewoodDeliveryZone] Fire visual set to: {newValue}");
    }

    private void ApplyFireVisual(bool active)
    {
        if (firePrefab != null)
            firePrefab.SetActive(active);
    }

    [ClientRpc]
    private void SetFireVisualClientRpc(bool active)
    {
        ApplyFireVisual(active);
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

        PlayerInventory inv = playerInTrigger != null
            ? playerInTrigger.GetComponent<PlayerInventory>()
            : null;

        UpdateDepositPrompt(inv);

        if (!Input.GetKeyDown(KeyCode.E)) return;

        if (inv != null && inv.IsHoldingItem() && depositedWood.Value < requiredWood)
        {
            GameObject held = inv.GetHeldPrefab();
            string heldName = held != null ? held.name.Replace("(Clone)", "").Trim() : "";
            if (heldName == firewoodPrefabName)
                DepositWoodServerRpc(NetworkManager.Singleton.LocalClientId);
            else
                Debug.Log("Deposit option: NO. Not holding valid firewood.");
        }
        else if ((inv == null || !inv.IsHoldingItem()) && depositedWood.Value >= requiredWood && !fireIsActive.Value)
        {
            LightFireServerRpc();
        }
        else if (inv != null && inv.IsHoldingItem() && fireIsActive.Value && burnedMushrooms.Value < requiredMushrooms)
        {
            GameObject held = inv.GetHeldPrefab();
            string heldName = held != null ? held.name.Replace("(Clone)", "").Trim() : "";
            bool isMushroom = heldName.StartsWith("mushroom", StringComparison.OrdinalIgnoreCase);
            if (isMushroom)
                DepositMushroomServerRpc(NetworkManager.Singleton.LocalClientId);
            else
                Debug.Log("Mushroom deposit option: NO. Not holding a mushroom.");
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
        string heldName = held != null ? held.name.Replace("(Clone)", "").Trim() : "";

        bool canDeposit = false;
        string itemLabel = "";

        if (heldName == firewoodPrefabName && depositedWood.Value < requiredWood)
        {
            canDeposit = true;
            itemLabel = "Firewood";
        }
        else if (fireIsActive.Value
            && heldName.StartsWith("mushroom", StringComparison.OrdinalIgnoreCase)
            && burnedMushrooms.Value < requiredMushrooms)
        {
            canDeposit = true;
            itemLabel = "Mushroom";
        }

        depositPromptUI.SetActive(canDeposit);
        if (canDeposit && depositPromptText != null)
            depositPromptText.text = $"Press [E] to deposit {itemLabel}";
    }

    // ================================================================
    // ServerRpcs
    // ================================================================

    [ServerRpc(RequireOwnership = false)]
    void DepositWoodServerRpc(ulong playerId)
    {
        if (depositedWood.Value >= requiredWood) return;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client)) return;

        var inv = client.PlayerObject.GetComponent<PlayerInventory>();
        if (inv == null || !inv.IsHoldingItem()) return;

        GameObject held = inv.GetHeldPrefab();
        if (held != null && held.name.Replace("(Clone)", "").Trim() == firewoodPrefabName)
        {
            inv.DepositItem();
            depositedWood.Value++;
            Debug.Log("Firewood deposited. Total: " + depositedWood.Value);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositMushroomServerRpc(ulong playerId)
    {
        if (!fireIsActive.Value || burnedMushrooms.Value >= requiredMushrooms) return;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client)) return;

        var inv = client.PlayerObject.GetComponent<PlayerInventory>();
        if (inv == null || !inv.IsHoldingItem()) return;

        GameObject held = inv.GetHeldPrefab();
        if (held != null && held.name.StartsWith("mushroom", StringComparison.OrdinalIgnoreCase))
        {
            inv.DepositItem();
            burnedMushrooms.Value++;
            Debug.Log($"Mushroom burned! {burnedMushrooms.Value}/{requiredMushrooms}");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void LightFireServerRpc()
    {
        if (fireIsActive.Value) return;
        fireIsActive.Value = true;
        SetFireVisualClientRpc(true);
        Debug.Log("Fire activated!");
        OnFireLit?.Invoke();
    }

    // ================================================================
    // Public helpers
    // ================================================================

    public void ActivateTask()
    {
        if (zoneMarkerSprite != null)
            zoneMarkerSprite.SetActive(depositedWood.Value == 0);
    }

    public void ForceCompleteMushroomBurning()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[FirewoodDeliveryZone] ForceCompleteMushroomBurning can only be called on server!");
            return;
        }
        burnedMushrooms.Value = requiredMushrooms;
        Debug.Log($"[FirewoodDeliveryZone] Mushroom burning force completed! ({requiredMushrooms}/{requiredMushrooms})");
    }
}