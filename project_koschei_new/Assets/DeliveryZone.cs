using Unity.Netcode;
using UnityEngine;

public class DeliveryZone : NetworkBehaviour
{
    [SerializeField] private int requiredCans = 12;
    [SerializeField] private GameObject canVisualPrefab; // Assign FoodCan prefab here
    [SerializeField] private Transform dropOffPoint; // Optional: specific spawn point

    private NetworkVariable<int> collectedCans = new NetworkVariable<int>(0);
    private bool playerInZone = false;
    private GameObject playerInTrigger = null;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        collectedCans.OnValueChanged += OnCansChanged;
        UpdateUI(0, collectedCans.Value);
    }

    void OnCansChanged(int oldValue, int newValue)
    {
        UpdateUI(oldValue, newValue);

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
            Debug.Log("Player in delivery zone - Press E to deposit can");
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
                    DepositCanServerRpc(NetworkManager.Singleton.LocalClientId);
                }
                else
                {
                    Debug.Log("You're not holding anything to deposit!");
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void DepositCanServerRpc(ulong playerId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out var client))
        {
            PlayerInventory inventory = client.PlayerObject.GetComponent<PlayerInventory>();

            if (inventory != null && inventory.IsHoldingItem())
            {
                // Remove from inventory
                inventory.DepositFoodCan();

                // Spawn visual can in zone
                SpawnCanInZone();

                // Increment counter
                collectedCans.Value++;

                Debug.Log($"Can deposited! Total: {collectedCans.Value}/{requiredCans}");
            }
        }
    }

    void SpawnCanInZone()
    {
        if (canVisualPrefab == null)
        {
            Debug.LogError("Can Visual Prefab not assigned to DeliveryZone!");
            return;
        }

        Vector3 spawnPos;

        if (dropOffPoint != null)
        {
            // Use drop-off point if assigned
            int canCount = collectedCans.Value;
            float offsetX = (canCount % 4) * 0.4f;
            float offsetZ = (canCount / 4) * 0.4f;
            spawnPos = dropOffPoint.position + new Vector3(offsetX, 0.5f, offsetZ);
        }
        else
        {
            // Use DeliveryZone's center if no drop-off point
            BoxCollider boxCollider = GetComponent<BoxCollider>();

            if (boxCollider != null)
            {
                // Get the center of the box collider in world space
                Vector3 zoneCenter = transform.TransformPoint(boxCollider.center);

                // Stack cans in a grid inside the zone
                int canCount = collectedCans.Value;
                float offsetX = (canCount % 4) * 0.4f - 0.6f; // Center the grid
                float offsetZ = (canCount / 4) * 0.4f - 0.6f;

                spawnPos = zoneCenter + new Vector3(offsetX, 0.5f, offsetZ);
            }
            else
            {
                // Fallback: use transform position
                spawnPos = transform.position + Vector3.up * 0.5f;
            }
        }

        Debug.Log($"Spawning can at: {spawnPos}");

        // Instantiate the can
        GameObject depositedCan = Instantiate(canVisualPrefab, spawnPos, Quaternion.identity);

        // Remove pickup script so it can't be picked up again
        FoodCan pickupScript = depositedCan.GetComponent<FoodCan>();
        if (pickupScript != null)
        {
            Destroy(pickupScript);
        }

        // Disable the trigger collider so players can't interact with it
        Collider[] colliders = depositedCan.GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            if (col.isTrigger)
            {
                col.enabled = false;
            }
        }

        // Spawn on network so all players see it
        NetworkObject netObj = depositedCan.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            Debug.Log($"✓ Can spawned in delivery zone at {spawnPos}");
        }
        else
        {
            Debug.LogError("Can prefab missing NetworkObject component!");
        }
    }

    [ClientRpc]
    void TaskCompleteClientRpc()
    {
        Debug.Log("=== TASK COMPLETE! All 12 cans delivered! ===");
    }

    void UpdateUI(int oldValue, int newValue)
    {
        Debug.Log($"Supply Cache Progress: {newValue}/{requiredCans}");
    }
}
