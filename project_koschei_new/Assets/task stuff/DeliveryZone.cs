using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class DeliveryZone : NetworkBehaviour
{
    [SerializeField] private int requiredCans = 12;
    [SerializeField] private Transform dropOffPoint;
    [SerializeField] private List<string> foodCanPrefabNames = new List<string> { "soda can", "spam can", "meat can", "can 2", "can small", "can tall 2", "can tall", "meat can box old", "meat can round", "meat can box"};

    private NetworkVariable<int> collectedCans = new NetworkVariable<int>(0);
    private bool playerInZone = false;
    private GameObject playerInTrigger = null;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        collectedCans.OnValueChanged += OnCansChanged;
    }

    void OnCansChanged(int oldValue, int newValue)
    {
        if (newValue >= requiredCans)
        {
            TaskCompleteClientRpc();
        }
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
            if (playerInTrigger != null)
            {
                PlayerInventory inventory = playerInTrigger.GetComponent<PlayerInventory>();
                if (inventory != null && inventory.IsHoldingItem())
                {
                    GameObject heldPrefab = inventory.GetHeldPrefab();
                    if (heldPrefab != null && IsFoodCan(heldPrefab))
                    {
                        DepositCanServerRpc(NetworkManager.Singleton.LocalClientId);
                    }
                    // else: item not a valid food can, do nothing
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
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            PlayerInventory inventory = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inventory != null && inventory.IsHoldingItem())
            {
                GameObject canType = inventory.GetHeldPrefab();
                inventory.DepositItem();
                SpawnCanInZone(canType);
                collectedCans.Value++;
            }
        }
    }

    void SpawnCanInZone(GameObject canPrefabToSpawn)
    {
        if (canPrefabToSpawn == null) return;

        Vector3 spawnPos;
        int canCount = collectedCans.Value;

        if (dropOffPoint != null)
        {
            float offsetX = (canCount % 4) * 0.4f;
            float offsetZ = (canCount / 4) * 0.4f;
            spawnPos = dropOffPoint.position + new Vector3(offsetX, 0.5f, offsetZ);
        }
        else
        {
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                Vector3 zoneCenter = transform.TransformPoint(boxCollider.center);
                float offsetX = (canCount % 4) * 0.4f - 0.6f;
                float offsetZ = (canCount / 4) * 0.4f - 0.6f;
                spawnPos = zoneCenter + new Vector3(offsetX, 0.5f, offsetZ);
            }
            else
            {
                spawnPos = transform.position + Vector3.up * 0.5f;
            }
        }

        GameObject depositedCan = Instantiate(canPrefabToSpawn, spawnPos, Quaternion.identity);

        // Remove pickup script so it can't be picked up again
        var pickupScript = depositedCan.GetComponent<PickupObject>();
        if (pickupScript != null)
        {
            Destroy(pickupScript);
        }

        Collider[] colliders = depositedCan.GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            if (col.isTrigger)
            {
                col.enabled = false;
            }
        }

        NetworkObject netObj = depositedCan.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
    }

    [ClientRpc]
    void TaskCompleteClientRpc()
    {
        // No logic needed unless you want completion events.
    }
}
