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

    [Header("Marker")]
    [SerializeField] private GameObject zoneMarkerSprite; // Visible only when task is active and no wood has been deposited yet

    // Task1 listens to these (server-side only)
    public event Action OnFireLit;
    public event Action OnMushroomsComplete;

    private NetworkVariable<int> depositedWood = new NetworkVariable<int>(0);
    private NetworkVariable<int> burnedMushrooms = new NetworkVariable<int>(0);
    private NetworkVariable<bool> fireIsActive = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> taskIsActive = new NetworkVariable<bool>(false);

    private bool playerInZone = false;
    private GameObject playerInTrigger = null;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        depositedWood.OnValueChanged += OnWoodChanged;
        burnedMushrooms.OnValueChanged += OnMushroomChanged;
        fireIsActive.OnValueChanged += OnFireActiveChanged;
        taskIsActive.OnValueChanged += OnTaskActiveChanged;

        RefreshWoodPileVisual(depositedWood.Value);
        ApplyFireVisual(fireIsActive.Value);
        RefreshMarkerVisual();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        depositedWood.OnValueChanged -= OnWoodChanged;
        burnedMushrooms.OnValueChanged -= OnMushroomChanged;
        fireIsActive.OnValueChanged -= OnFireActiveChanged;
        taskIsActive.OnValueChanged -= OnTaskActiveChanged;
    }

    private void OnWoodChanged(int oldValue, int newValue)
    {
        RefreshWoodPileVisual(newValue);
        RefreshMarkerVisual();
        Debug.Log($"Pile updated: {newValue}/{requiredWood} wood pieces active.");
    }

    private void OnMushroomChanged(int oldValue, int newValue)
    {
        Debug.Log($"Mushrooms burned: {newValue}/{requiredMushrooms}");

        if (newValue >= requiredMushrooms)
        {
            Debug.Log("All mushrooms burned!");
            if (IsServer)
                OnMushroomsComplete?.Invoke();
        }
    }

    private void OnFireActiveChanged(bool oldValue, bool newValue)
    {
        ApplyFireVisual(newValue);
        Debug.Log($"[FirewoodDeliveryZone] Fire visual set to: {newValue}");
    }

    private void OnTaskActiveChanged(bool oldValue, bool newValue)
    {
        RefreshMarkerVisual();
        Debug.Log($"[FirewoodDeliveryZone] Task active set to: {newValue}");
    }

    private void RefreshWoodPileVisual(int woodCount)
    {
        for (int i = 0; i < firewoodPileObjects.Count; i++)
        {
            if (firewoodPileObjects[i] != null)
                firewoodPileObjects[i].SetActive(i < woodCount);
        }
    }

    private void ApplyFireVisual(bool active)
    {
        if (firePrefab != null)
            firePrefab.SetActive(active);
    }

    private void RefreshMarkerVisual()
    {
        if (zoneMarkerSprite != null)
            zoneMarkerSprite.SetActive(taskIsActive.Value && depositedWood.Value == 0);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            playerInTrigger = other.gameObject;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;
            playerInTrigger = null;
        }
    }

    void Update()
    {
        if (!playerInZone) return;
        if (playerInTrigger == null) return;

        PlayerInventory inv = playerInTrigger.GetComponent<PlayerInventory>();
        if (inv == null) return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (inv.IsHoldingItem() && depositedWood.Value < requiredWood)
            {
                GameObject held = inv.GetHeldPrefab();
                string heldName = held != null ? held.name.Replace("(Clone)", "").Trim() : "";
                bool isFirewood = heldName == firewoodPrefabName;

                if (isFirewood)
                    DepositWoodServerRpc(NetworkManager.Singleton.LocalClientId);
                else
                    Debug.Log("Deposit option: NO. Not holding valid firewood.");
            }
            else if (!inv.IsHoldingItem() && depositedWood.Value >= requiredWood && !fireIsActive.Value)
            {
                LightFireServerRpc();
            }
            else if (inv.IsHoldingItem() && fireIsActive.Value && burnedMushrooms.Value < requiredMushrooms)
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
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositWoodServerRpc(ulong playerId)
    {
        if (!taskIsActive.Value) return;
        if (depositedWood.Value >= requiredWood) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            var inv = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inv != null && inv.IsHoldingItem())
            {
                GameObject held = inv.GetHeldPrefab();
                if (held != null && held.name.Replace("(Clone)", "").Trim() == firewoodPrefabName)
                {
                    inv.DepositItem();
                    depositedWood.Value++;
                    Debug.Log("Firewood deposited. Total: " + depositedWood.Value);
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositMushroomServerRpc(ulong playerId)
    {
        if (!taskIsActive.Value) return;
        if (!fireIsActive.Value) return;
        if (burnedMushrooms.Value >= requiredMushrooms) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            var inv = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inv != null && inv.IsHoldingItem())
            {
                GameObject held = inv.GetHeldPrefab();
                if (held != null && held.name.StartsWith("mushroom", StringComparison.OrdinalIgnoreCase))
                {
                    inv.DepositItem();
                    burnedMushrooms.Value++;
                    Debug.Log($"Mushroom burned! {burnedMushrooms.Value}/{requiredMushrooms}");
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void LightFireServerRpc()
    {
        if (!taskIsActive.Value) return;
        if (fireIsActive.Value) return;
        if (depositedWood.Value < requiredWood) return;

        fireIsActive.Value = true;
        Debug.Log("Fire activated!");
        OnFireLit?.Invoke();
    }

    // ================================================================
    // TASK ACTIVATION API
    // Call this from Task1 when the firewood objective becomes active
    // ================================================================

    public void ActivateTask()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[FirewoodDeliveryZone] ActivateTask can only be called on server!");
            return;
        }

        taskIsActive.Value = true;
        Debug.Log("[FirewoodDeliveryZone] Task activated.");
    }

    public void DeactivateTask()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[FirewoodDeliveryZone] DeactivateTask can only be called on server!");
            return;
        }

        taskIsActive.Value = false;
        Debug.Log("[FirewoodDeliveryZone] Task deactivated.");
    }

    [ServerRpc(RequireOwnership = false)]
    public void ActivateTaskServerRpc()
    {
        taskIsActive.Value = true;
        Debug.Log("[FirewoodDeliveryZone] Task activated via ServerRpc.");
    }

    [ServerRpc(RequireOwnership = false)]
    public void DeactivateTaskServerRpc()
    {
        taskIsActive.Value = false;
        Debug.Log("[FirewoodDeliveryZone] Task deactivated via ServerRpc.");
    }

    // ================================================================
    // TESTING HELPERS
    // ================================================================

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

    public void ForceActivateFire()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[FirewoodDeliveryZone] ForceActivateFire can only be called on server!");
            return;
        }

        fireIsActive.Value = true;
        Debug.Log("[FirewoodDeliveryZone] Fire force activated.");
        OnFireLit?.Invoke();
    }
}