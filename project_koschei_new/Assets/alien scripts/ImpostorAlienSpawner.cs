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
    public int maxPlayersInGroup = 3;
    public float groupRadius = 12f;

    [Header("Spawn Area")]
    public Vector3 center;
    public float searchRadius = 80f;
    public float spawnHeightOffset = 0.1f; // REDUCED: Just a tiny offset to prevent clipping
    public float maxRaycastHeight = 500f;

    [Header("Debug")]
    public bool showDebugGizmos = true;

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

        if (currentImpostor == null || !currentImpostor.IsSpawned)
        {
            currentImpostor = null;
        }

        if (currentImpostor != null) return;

        if (Time.time >= nextSpawnTime)
        {
            TrySpawnNearSmallGroup();
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

        TrySpawnImpostorAnywhere();
    }

    void TrySpawnNearSmallGroup()
    {
        if (impostorAlienPrefab == null)
        {
            Debug.LogError("ImpostorAlienSpawner: impostorAlienPrefab not assigned.");
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients == null || clients.Count == 0)
        {
            Debug.Log("No players connected, cannot spawn impostor.");
            return;
        }

        List<Transform> players = new List<Transform>();
        foreach (var c in clients)
        {
            if (c.PlayerObject != null)
                players.Add(c.PlayerObject.transform);
        }

        if (players.Count == 0)
        {
            Debug.Log("No player objects found.");
            return;
        }

        Transform groupCenter = null;
        int smallestGroupSize = int.MaxValue;

        foreach (var p in players)
        {
            int nearbyCount = 0;
            foreach (var q in players)
            {
                if (Vector3.Distance(p.position, q.position) <= groupRadius)
                    nearbyCount++;
            }

            if (nearbyCount > 0 && nearbyCount <= maxPlayersInGroup && nearbyCount < smallestGroupSize)
            {
                smallestGroupSize = nearbyCount;
                groupCenter = p;
            }
        }

        if (groupCenter == null)
        {
            groupCenter = players[Random.Range(0, players.Count)];
            Debug.Log("No small group found, targeting random player.");
        }

        if (!TryFindSpawnPositionAroundPoint(groupCenter.position, out Vector3 spawnPos))
        {
            Debug.LogWarning("ImpostorAlienSpawner: could not find spawn position near small group.");
            return;
        }

        SpawnImpostorAt(spawnPos);
    }

    void TrySpawnImpostorAnywhere()
    {
        if (impostorAlienPrefab == null)
        {
            Debug.LogError("ImpostorAlienSpawner: impostorAlienPrefab not assigned.");
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        List<Vector3> playerPositions = new List<Vector3>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
                playerPositions.Add(client.PlayerObject.transform.position);
        }

        if (!TryFindSpawnPositionAwayFromPlayers(playerPositions, out Vector3 spawnPos))
        {
            Debug.LogWarning("ImpostorAlienSpawner: could not find suitable spawn position.");
            return;
        }

        SpawnImpostorAt(spawnPos);
    }

    void SpawnImpostorAt(Vector3 spawnPos)
    {
        if (currentImpostor != null && currentImpostor.IsSpawned)
        {
            currentImpostor.Despawn(true);
            currentImpostor = null;
        }

        lastAttemptedSpawnPos = spawnPos;

        GameObject impostor = Instantiate(impostorAlienPrefab, spawnPos, Quaternion.identity);

        NetworkObject netObj = impostor.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("impostorAlienPrefab is missing NetworkObject component!");
            Destroy(impostor);
            return;
        }

        // IMPROVED: Properly ground the CharacterController
        CharacterController cc = impostor.GetComponent<CharacterController>();
        if (cc != null)
        {
            // Disable controller to set position
            cc.enabled = false;

            // Account for CharacterController's center offset
            // If your CC has center.y = 1, this ensures feet are on ground
            float heightOffset = cc.center.y;
            impostor.transform.position = spawnPos + Vector3.up * heightOffset;

            // Re-enable and do a small Move to ensure proper physics state
            cc.enabled = true;
            cc.Move(Vector3.zero); // Initialize physics state

            Debug.Log($"Spawned impostor with CC center offset: {heightOffset}");
        }

        netObj.Spawn(true);
        currentImpostor = netObj;

        Debug.Log($"✅ Spawned impostor alien at {spawnPos}");
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

    bool TryFindSpawnPositionAwayFromPlayers(List<Vector3> playerPositions, out Vector3 result)
    {
        Vector3 origin = (center == Vector3.zero) ? transform.position : center;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            Vector2 circle = Random.insideUnitCircle.normalized *
                             Random.Range(minSpawnDistanceFromPlayers, maxSpawnDistanceFromPlayers);
            Vector3 candidate = origin + new Vector3(circle.x, 0f, circle.y);

            if (Vector3.Distance(origin, candidate) > searchRadius)
                continue;

            if (!TryProjectToGround(candidate, out Vector3 groundPos))
                continue;

            bool tooClose = false;
            foreach (var p in playerPositions)
            {
                if (Vector3.Distance(groundPos, p) < minSpawnDistanceFromPlayers)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            result = groundPos;
            return true;
        }

        result = Vector3.zero;
        return false;
    }

    bool TryProjectToGround(Vector3 candidate, out Vector3 groundPos)
    {
        // STRATEGY 1: NavMesh - returns exact walkable surface position
        if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, navmeshSampleRadius, NavMesh.AllAreas))
        {
            groundPos = navHit.position + Vector3.up * spawnHeightOffset;
            Debug.DrawLine(candidate, groundPos, Color.green, 2f);
            return true;
        }

        // STRATEGY 2: Raycast from high above
        Vector3 worldTop = new Vector3(candidate.x, maxRaycastHeight, candidate.z);

        if (Physics.Raycast(worldTop, Vector3.down, out RaycastHit hit, maxRaycastHeight * 2f, groundMask))
        {
            groundPos = hit.point + Vector3.up * spawnHeightOffset;
            Debug.DrawLine(worldTop, groundPos, Color.cyan, 2f);
            return true;
        }

        // STRATEGY 3: Raycast from elevated candidate position
        if (Physics.Raycast(candidate + Vector3.up * 50f, Vector3.down, out RaycastHit downHit, 100f, groundMask))
        {
            groundPos = downHit.point + Vector3.up * spawnHeightOffset;
            Debug.DrawLine(candidate, groundPos, Color.yellow, 2f);
            return true;
        }

        Debug.DrawLine(candidate, candidate + Vector3.down * 10f, Color.red, 2f);
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
}