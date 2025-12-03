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
    public int maxPlayersInGroup = 3;          // "less than 4"
    public float groupRadius = 12f;            // how close players must be to count as a group

    [Header("Spawn Area (optional)")]
    public Vector3 center;                     // if zero, uses spawner position
    public float searchRadius = 80f;

    NetworkObject currentImpostor;             // track the single impostor

    float nextSpawnTime = 0f;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        nextSpawnTime = Time.time + spawnIntervalSeconds;
    }

    void Update()
    {
        if (!IsServer) return;
        if (!autoSpawnEnabled) return;

        // Clean up reference if impostor was despawned/destroyed
        if (currentImpostor == null)
        {
            currentImpostor = null;
        }

        // Only try to spawn if there is no active impostor
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
        if (!IsServer)
        {
            Debug.LogWarning("SpawnImpostorNow must be called on server/host.");
            return;
        }

        // Manual debug spawn: ignore groups, just use old logic
        TrySpawnImpostorAnywhere();
    }

    // ================== Group-based spawn ==================
    void TrySpawnNearSmallGroup()
    {
        if (impostorAlienPrefab == null)
        {
            Debug.LogError("ImpostorAlienSpawner: impostorAlienPrefab not assigned.");
            return;
        }

        // Gather all players
        var clients = NetworkManager.Singleton?.ConnectedClientsList;
        if (clients == null || clients.Count == 0) return;

        List<Transform> players = new List<Transform>();
        foreach (var c in clients)
        {
            if (c.PlayerObject != null)
                players.Add(c.PlayerObject.transform);
        }

        if (players.Count == 0) return;

        // Find a "small group": any player around whom there are <= maxPlayersInGroup players within groupRadius
        Transform groupCenter = null;
        foreach (var p in players)
        {
            int nearbyCount = 0;
            foreach (var q in players)
            {
                if (Vector3.Distance(p.position, q.position) <= groupRadius)
                    nearbyCount++;
            }

            if (nearbyCount > 0 && nearbyCount <= maxPlayersInGroup)
            {
                groupCenter = p;
                break;
            }
        }

        if (groupCenter == null)
        {
            // No small group found this interval
            return;
        }

        // Find spawn position near that groupCenter but still outside line-of-sight distance
        if (!TryFindSpawnPositionAroundPoint(groupCenter.position, out Vector3 spawnPos))
        {
            Debug.LogWarning("ImpostorAlienSpawner: could not find spawn position near small group.");
            return;
        }

        SpawnImpostorAt(spawnPos);
    }

    // Fallback: old "anywhere in area but away from all players"
    void TrySpawnImpostorAnywhere()
    {
        if (impostorAlienPrefab == null)
        {
            Debug.LogError("ImpostorAlienSpawner: impostorAlienPrefab not assigned.");
            return;
        }

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
        // Despawn any existing impostor just in case (single impostor rule)
        if (currentImpostor != null && currentImpostor.IsSpawned)
        {
            currentImpostor.Despawn(true);
            currentImpostor = null;
        }

        GameObject impostor = Instantiate(impostorAlienPrefab, spawnPos, Quaternion.identity);
        NetworkObject netObj = impostor.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("impostorAlienPrefab is missing NetworkObject.");
            Destroy(impostor);
            return;
        }

        netObj.Spawn(true);
        currentImpostor = netObj;

        Debug.Log($"Spawned impostor alien at {spawnPos}");
    }

    // ================== Position helpers ==================
    bool TryFindSpawnPositionAroundPoint(Vector3 groupPos, out Vector3 result)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            // Pick a random direction and distance around the group
            Vector2 dir2D = Random.insideUnitCircle.normalized;
            float dist = Random.Range(minSpawnDistanceFromPlayers, maxSpawnDistanceFromPlayers);
            Vector3 candidate = groupPos + new Vector3(dir2D.x, 0f, dir2D.y) * dist;

            // Optional large-area clamp
            Vector3 origin = (center == Vector3.zero) ? transform.position : center;
            if (Vector3.Distance(origin, candidate) > searchRadius)
                continue;

            if (TryProjectToGround(candidate, out Vector3 samplePos))
            {
                result = samplePos;
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

            if (!TryProjectToGround(candidate, out Vector3 samplePos))
                continue;

            bool tooClose = false;
            foreach (var p in playerPositions)
            {
                if (Vector3.Distance(samplePos, p) < minSpawnDistanceFromPlayers)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            result = samplePos;
            return true;
        }

        result = Vector3.zero;
        return false;
    }

    bool TryProjectToGround(Vector3 candidate, out Vector3 samplePos)
    {
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navmeshSampleRadius, NavMesh.AllAreas))
        {
            samplePos = hit.position;
            return true;
        }

        if (Physics.Raycast(candidate + Vector3.up * 30f, Vector3.down, out RaycastHit groundHit, 60f, groundMask))
        {
            samplePos = groundHit.point;
            return true;
        }

        samplePos = Vector3.zero;
        return false;
    }
}
