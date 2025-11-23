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

    private NetworkVariable<int> depositedWood = new NetworkVariable<int>(0);
    private bool playerInZone = false;
    private GameObject playerInTrigger = null;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        depositedWood.OnValueChanged += OnWoodChanged;
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
            if (inv != null && inv.IsHoldingItem() && depositedWood.Value < requiredWood)
            {
                GameObject held = inv.GetHeldPrefab();
                bool isFirewood = held != null && held.name.Replace("(Clone)", "").Trim() == firewoodPrefabName;
                Debug.Log($"Player pressed E inside FirewoodDeliveryZone. Holding item: {(held != null ? held.name : "None")}. Is firewood: {isFirewood}");

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
            else if (depositedWood.Value >= requiredWood && firePrefab != null && !firePrefab.activeSelf)
            {
                Debug.Log("Fire pile is full. Lighting fire.");
                LightFireServerRpc();
            }
            else if (inv == null || !inv.IsHoldingItem())
            {
                Debug.Log("Deposit option: NO. You are not holding anything.");
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
    void LightFireServerRpc()
    {
        if (firePrefab != null && !firePrefab.activeSelf)
        {
            firePrefab.SetActive(true);
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
