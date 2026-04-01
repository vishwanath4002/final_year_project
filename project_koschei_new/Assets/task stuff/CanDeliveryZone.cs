using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CanDeliveryZone : NetworkBehaviour
{
    [SerializeField] private int requiredCans = 12;
    [SerializeField] private Transform dropOffPoint;
    [SerializeField] private GameObject zoneMarkerSprite;

    [SerializeField]
    private List<string> foodCanPrefabNames = new List<string>
    {
        "soda can", "spam can", "meat can", "can 2", "can small", "can tall 2", "can tall",
        "meat can box old", "meat can round", "meat can box"
    };

    // ── Task1 listens to this (server-side only) ──
    public event Action OnCansComplete;

    private NetworkVariable<int> collectedCans = new NetworkVariable<int>(0);
    private NetworkVariable<bool> taskIsActive = new NetworkVariable<bool>(false);

    private bool playerInZone = false;
    private GameObject playerInTrigger = null;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        collectedCans.OnValueChanged += OnCansChanged;
        taskIsActive.OnValueChanged += OnTaskActiveChanged;

        RefreshMarkerVisual();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        collectedCans.OnValueChanged -= OnCansChanged;
        taskIsActive.OnValueChanged -= OnTaskActiveChanged;
    }

    void OnCansChanged(int oldValue, int newValue)
    {
        Debug.Log($"Cans delivered: {newValue}/{requiredCans}");
        RefreshMarkerVisual();

        if (newValue >= requiredCans)
        {
            Debug.Log("All cans delivered!");
            if (IsServer) OnCansComplete?.Invoke();
            CansCompleteClientRpc();
        }
    }

    void OnTaskActiveChanged(bool oldValue, bool newValue)
    {
        RefreshMarkerVisual();
    }

    void RefreshMarkerVisual()
    {
        if (zoneMarkerSprite != null)
            zoneMarkerSprite.SetActive(taskIsActive.Value && collectedCans.Value < requiredCans);
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
        if (playerInZone && Input.GetKeyDown(KeyCode.E))
        {
            if (!taskIsActive.Value) return;

            if (playerInTrigger != null)
            {
                PlayerInventory inventory = playerInTrigger.GetComponent<PlayerInventory>();
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
        }
    }

    bool IsFoodCan(GameObject itemPrefab)
    {
        string cleanName = itemPrefab.name.Replace("(Clone)", "").Trim();
        return foodCanPrefabNames.Contains(cleanName);
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositCanServerRpc(ulong playerId)
    {
        if (!taskIsActive.Value) return;
        if (collectedCans.Value >= requiredCans) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            PlayerInventory inventory = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inventory != null && inventory.IsHoldingItem())
            {
                GameObject canType = inventory.GetHeldPrefab();
                if (canType != null && IsFoodCan(canType))
                {
                    inventory.DepositItem();
                    SpawnCanInZone(canType);
                    collectedCans.Value++;
                }
            }
        }
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
    // TASK ACTIVATION
    // ================================================================

    public void ActivateTask()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[CanDeliveryZone] ActivateTask can only be called on server!");
            return;
        }

        taskIsActive.Value = true;
        Debug.Log("[CanDeliveryZone] Task activated.");
    }

    // ================================================================
    // TESTING HELPER
    // ================================================================

    /// <summary>
    /// Force completes the can delivery objective (testing only).
    /// Called by TaskManager.ForceCompleteFoodCanTask() via GameFlowTester.
    /// </summary>
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