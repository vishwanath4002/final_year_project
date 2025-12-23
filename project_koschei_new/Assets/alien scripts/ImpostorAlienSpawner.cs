using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class ImpostorAlienSpawner : NetworkBehaviour
{
    [Header("Impostor Settings")]
    public GameObject impostorAlienPrefab;
    public float minSpawnDistanceFromPlayers = 20f;
    public float maxSpawnDistanceFromPlayers = 35f;
    public float navmeshSampleRadius = 3f;
    public LayerMask groundMask;

    [Header("Auto Spawn")]
    public bool autoSpawnEnabled = true;
    public float spawnIntervalSeconds = 30f;

    [Header("Spawn Area")]
    public Vector3 center;
    public float searchRadius = 80f;
    public float spawnHeightOffset = 0.1f;
    public float maxRaycastHeight = 500f;

    [Header("Debug")]
    public bool showDebugGizmos = true;

    [Header("Group Integration")]
    public PlayerGroupManager groupManager;
    public ImpostorBackendConnector backendConnector;  // NEW: Backend integration

    // Track last target group to avoid repeats
    private PlayerGroup lastTargetGroup = null;

    NetworkObject currentImpostor;
    float nextSpawnTime = 0f;
    Vector3 lastAttemptedSpawnPos = Vector3.zero;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        nextSpawnTime = Time.time + spawnIntervalSeconds;
    }

    void Update()
    {
        if (!IsServer) return;
        if (!autoSpawnEnabled) return;
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsListening) return;

        // Check if current impostor is still alive
        if (currentImpostor == null || !currentImpostor.IsSpawned)
        {
            currentImpostor = null;
        }

        // Don't spawn new impostor if one already exists
        if (currentImpostor != null) return;

        // Check if it's time to spawn
        if (Time.time >= nextSpawnTime)
        {
            TrySpawnNearTargetGroup();
            nextSpawnTime = Time.time + spawnIntervalSeconds;
        }
    }

    [ContextMenu("Spawn Impostor Now (Server Only)")]
    void SpawnImpostorContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Must be in Play mode.");
            return;
        }

        if (!IsServer)
        {
            Debug.LogWarning("SpawnImpostorNow must be called on server/host.");
            return;
        }

        TrySpawnNearTargetGroup();
    }

    void TrySpawnNearTargetGroup()
    {
        if (impostorAlienPrefab == null)
        {
            Debug.LogError("[ImpostorSpawner] impostorAlienPrefab not assigned.");
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        // CRITICAL: Use group system
        if (groupManager == null)
        {
            Debug.LogError("[ImpostorSpawner] PlayerGroupManager not assigned!");
            return;
        }

        // Get target group using existing logic
        var targetGroup = groupManager.GetTargetGroupForImpostor(
            transform.position,
            lastTargetGroup
        );

        if (targetGroup == null)
        {
            Debug.Log("[ImpostorSpawner] No valid groups found for impostor spawn.");
            return;
        }

        Debug.Log($"[ImpostorSpawner] ═══════════════════════════════════");
        Debug.Log($"[ImpostorSpawner] 🎯 TARGET GROUP SELECTED");
        Debug.Log($"[ImpostorSpawner]    Group ID: {targetGroup.groupId}");
        Debug.Log($"[ImpostorSpawner]    Members: {string.Join(", ", targetGroup.playerIds)}");
        Debug.Log($"[ImpostorSpawner]    Size: {targetGroup.playerIds.Count}");
        Debug.Log($"[ImpostorSpawner]    Location: {targetGroup.centerPosition}");
        Debug.Log($"[ImpostorSpawner] ═══════════════════════════════════");

        lastTargetGroup = targetGroup;

        // Find spawn position near group
        if (!TryFindSpawnPositionAroundPoint(targetGroup.centerPosition, out Vector3 spawnPos))
        {
            Debug.LogWarning("[ImpostorSpawner] Could not find spawn position near target group.");
            return;
        }

        // Spawn the impostor
        SpawnImpostorAt(spawnPos);

        // NEW: Notify backend about the impostor spawning
        if (backendConnector != null)
        {
            backendConnector.NotifyImpostorSpawned(targetGroup);
        }
        else
        {
            Debug.LogWarning("[ImpostorSpawner] ⚠️ No ImpostorBackendConnector assigned! Backend won't know about impostor.");
        }
    }

    void SpawnImpostorAt(Vector3 spawnPos)
    {
        // Despawn existing impostor if any
        if (currentImpostor != null && currentImpostor.IsSpawned)
        {
            // Notify backend before despawning
            if (backendConnector != null)
            {
                backendConnector.NotifyImpostorDespawned();
            }
            
            currentImpostor.Despawn(true);
            currentImpostor = null;
        }

        lastAttemptedSpawnPos = spawnPos;

        GameObject impostor = Instantiate(impostorAlienPrefab, spawnPos, Quaternion.identity);

        NetworkObject netObj = impostor.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[ImpostorSpawner] impostorAlienPrefab is missing NetworkObject component!");
            Destroy(impostor);
            return;
        }

        // Handle CharacterController positioning
        CharacterController cc = impostor.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            float heightOffset = cc.center.y;
            impostor.transform.position = spawnPos + Vector3.up * heightOffset;
            cc.enabled = true;
            cc.Move(Vector3.zero);
            Debug.Log($"[ImpostorSpawner] Spawned with CC offset: {heightOffset}");
        }

        netObj.Spawn(true);
        currentImpostor = netObj;

        Debug.Log($"[ImpostorSpawner] ✅ Impostor spawned at {spawnPos}");
    }

    bool TryFindSpawnPositionAroundPoint(Vector3 groupPos, out Vector3 result)
    {
        Vector3 origin = (center == Vector3.zero) ? transform.position : center;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector2 dir2D = Random.insideUnitCircle.normalized;
            float dist = Random.Range(minSpawnDistanceFromPlayers, maxSpawnDistanceFromPlayers);
            Vector3 candidate = groupPos + new Vector3(dir2D.x, 0f, dir2D.y) * dist;

            if (Vector3.Distance(origin, candidate) > searchRadius)
                continue;

            if (TryProjectToGround(candidate, out Vector3 groundPos))
            {
                result = groundPos;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    bool TryProjectToGround(Vector3 candidate, out Vector3 groundPos)
    {
        // STRATEGY 1: NavMesh
        if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, navmeshSampleRadius, NavMesh.AllAreas))
        {
            groundPos = navHit.position + Vector3.up * spawnHeightOffset;
            return true;
        }

        // STRATEGY 2: Raycast from high above
        Vector3 worldTop = new Vector3(candidate.x, maxRaycastHeight, candidate.z);
        if (Physics.Raycast(worldTop, Vector3.down, out RaycastHit hit, maxRaycastHeight * 2f, groundMask))
        {
            groundPos = hit.point + Vector3.up * spawnHeightOffset;
            return true;
        }

        // STRATEGY 3: Raycast from elevated candidate
        if (Physics.Raycast(candidate + Vector3.up * 50f, Vector3.down, out RaycastHit downHit, 100f, groundMask))
        {
            groundPos = downHit.point + Vector3.up * spawnHeightOffset;
            return true;
        }

        groundPos = Vector3.zero;
        return false;
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        Vector3 origin = (center == Vector3.zero) ? transform.position : center;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, searchRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(origin, minSpawnDistanceFromPlayers);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(origin, maxSpawnDistanceFromPlayers);

        if (lastAttemptedSpawnPos != Vector3.zero)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(lastAttemptedSpawnPos, 2f);
            Gizmos.DrawLine(lastAttemptedSpawnPos, lastAttemptedSpawnPos + Vector3.up * 5f);
        }

        if (currentImpostor != null && currentImpostor.IsSpawned)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(currentImpostor.transform.position, 1.5f);
        }
    }

    // Call this when impostor is manually despawned
    public void OnImpostorDespawned()
    {
        if (backendConnector != null)
        {
            backendConnector.NotifyImpostorDespawned();
        }
        currentImpostor = null;
        lastTargetGroup = null;
    }
}