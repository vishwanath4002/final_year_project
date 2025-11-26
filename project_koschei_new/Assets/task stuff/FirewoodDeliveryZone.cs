using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class FirewoodDeliveryZone : NetworkBehaviour
{
    [Header("Setup")]
    [SerializeField] private string firewoodPrefabName = "FireWood"; // Must match the registered prefab name
    [SerializeField] private List<GameObject> firewoodPileObjects;   // Assign exact pile pieces in Inspector
    [SerializeField] private GameObject firePrefab;                  // Assign fire visual GameObject
    [SerializeField] private int requiredWood = 5;
    [SerializeField] private int requiredMushrooms = 13;

    private NetworkVariable<int> depositedWood = new NetworkVariable<int>(0);
    private NetworkVariable<int> burnedMushrooms = new NetworkVariable<int>(0);
    private NetworkVariable<bool> fireIsActive = new NetworkVariable<bool>(false);

    private bool playerInZone = false;
    private GameObject playerInTrigger = null;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        depositedWood.OnValueChanged += OnWoodChanged;
        burnedMushrooms.OnValueChanged += OnMushroomChanged;
    }

    void OnWoodChanged(int oldValue, int newValue)
    {
        for (int i = 0; i < firewoodPileObjects.Count; i++)
        {
            if (firewoodPileObjects[i] != null)
                firewoodPileObjects[i].SetActive(i < newValue);
        }
        Debug.Log($"Pile updated: {newValue}/{requiredWood} wood pieces are now active.");
    }

    void OnMushroomChanged(int oldValue, int newValue)
    {
        Debug.Log($"Mushrooms burned: {newValue}/{requiredMushrooms}");
        if (newValue >= requiredMushrooms)
            Debug.Log("All mushrooms burned! Task complete!");
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            playerInTrigger = other.gameObject;
            Debug.Log("Player entered FirewoodDeliveryZone. Press E to deposit firewood, or light fire when pile is full.");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;
            playerInTrigger = null;
            Debug.Log("Player exited FirewoodDeliveryZone.");
        }
    }

    void Update()
    {
        if (!playerInZone) return;
        PlayerInventory inv = playerInTrigger != null ? playerInTrigger.GetComponent<PlayerInventory>() : null;

        if (Input.GetKeyDown(KeyCode.E))
        {
            // 1. Deposit firewood if holding item and wood still needed
            if (inv != null && inv.IsHoldingItem() && depositedWood.Value < requiredWood)
            {
                GameObject held = inv.GetHeldPrefab();
                string heldName = held != null ? held.name.Replace("(Clone)", "").Trim() : "";
                bool isFirewood = heldName == firewoodPrefabName;
                Debug.Log($"Player pressed E inside FirewoodDeliveryZone. Holding item: {heldName}. Is firewood: {isFirewood}");

                if (isFirewood)
                {
                    Debug.Log("Deposit option: YES. Depositing firewood piece.");
                    DepositWoodServerRpc(NetworkManager.Singleton.LocalClientId);
                }
                else
                {
                    Debug.Log("Deposit option: NO. You are not holding a valid firewood piece.");
                }
            }
            // 2. Light fire ONLY if not holding anything, after all wood deposited and fire not yet active
            else if ((inv == null || !inv.IsHoldingItem()) && depositedWood.Value >= requiredWood && firePrefab != null && !firePrefab.activeSelf)
            {
                Debug.Log("Fire pile is full and hands are empty. Lighting fire.");
                LightFireServerRpc();
            }
            // 3. Deposit mushrooms, but only after fire is lit
            else if (inv != null && inv.IsHoldingItem() && fireIsActive.Value && burnedMushrooms.Value < requiredMushrooms)
            {
                GameObject held = inv.GetHeldPrefab();
                string heldName = held != null ? held.name.Replace("(Clone)", "").Trim() : "";
                bool isMushroom = heldName.StartsWith("mushroom", System.StringComparison.OrdinalIgnoreCase);
                Debug.Log($"Fire is burning. Holding item: {heldName}. Is mushroom: {isMushroom}");

                if (isMushroom)
                {
                    Debug.Log("Mushroom deposit option: YES. Depositing mushroom.");
                    DepositMushroomServerRpc(NetworkManager.Singleton.LocalClientId);
                }
                else
                {
                    Debug.Log("Mushroom deposit option: NO. You are not holding a mushroom.");
                }
            }
            // 4. Catch-all
            else if (inv != null && inv.IsHoldingItem())
            {
                Debug.Log("Deposit option: NO. You are holding an item, and can't light the fire right now.");
            }
            else
            {
                Debug.Log("Deposit option: NO. You are not holding anything, and can't light the fire yet.");
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositWoodServerRpc(ulong playerId)
    {
        if (depositedWood.Value >= requiredWood)
        {
            Debug.Log("All firewood already deposited.");
            return;
        }

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            var inv = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inv != null && inv.IsHoldingItem())
            {
                GameObject held = inv.GetHeldPrefab();
                bool isFirewood = held != null && held.name.Replace("(Clone)", "").Trim() == firewoodPrefabName;
                if (isFirewood)
                {
                    inv.DepositItem();
                    depositedWood.Value++;
                    Debug.Log("Firewood piece deposited. Total pile: " + depositedWood.Value);
                }
                else
                {
                    Debug.Log("Deposit failed: held item is not firewood.");
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositMushroomServerRpc(ulong playerId)
    {
        if (!fireIsActive.Value)
        {
            Debug.Log("Mushrooms can only be burned after fire is lit.");
            return;
        }
        if (burnedMushrooms.Value >= requiredMushrooms)
        {
            Debug.Log("All mushrooms already burned.");
            return;
        }
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            var inv = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inv != null && inv.IsHoldingItem())
            {
                GameObject held = inv.GetHeldPrefab();
                bool isMushroom = held != null && held.name.StartsWith("mushroom", System.StringComparison.OrdinalIgnoreCase);
                if (isMushroom)
                {
                    inv.DepositItem();
                    burnedMushrooms.Value++;
                    Debug.Log($"Mushroom burned! Total: {burnedMushrooms.Value}/{requiredMushrooms}");
                }
                else
                {
                    Debug.Log("Failed to burn: Item is not a mushroom.");
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void LightFireServerRpc()
    {
        if (firePrefab != null && !firePrefab.activeSelf)
        {
            firePrefab.SetActive(true);
            fireIsActive.Value = true;
            Debug.Log("Fire activated!");
            TaskCompleteClientRpc();
        }
    }

    [ClientRpc]
    void TaskCompleteClientRpc()
    {
        Debug.Log("Firewood delivery and lighting task complete!");
    }
}
