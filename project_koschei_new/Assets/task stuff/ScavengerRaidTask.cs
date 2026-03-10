using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class ScavengerRaidTask : MonoBehaviour, IGameTask
{
    [Header("Scene References")]
    public Transform buildingCenter;
    public Transform scientistNPC;

    [Header("Alien Prefab & Spawning")]
    public GameObject alienPrefab;
    public int totalAliens = 50;
    public int aliensPerWave = 5;
    public float timeBetweenWaves = 12f;
    public float spawnStaggerInterval = 0.35f;

    [Header("Spawn Ring")]
    public float spawnRadius = 20f;
    public float spawnRadiusJitter = 3f;

    [Header("Alien Behaviour Overrides")]
    public float alienPatrolRadius = 8f;

    public event Action OnTaskCompleted;
    public event Action OnTaskFailed;

    bool taskActive = false;
    int aliensSpawnedSoFar = 0;
    readonly List<GameObject> livingAliens = new();
    ScientistHealth scientistHealth;
    Coroutine spawnCoroutine;

    // ================================================================
    // IGameTask -- Public API
    // ================================================================

    public void StartTask()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[ScavengerRaidTask] StartTask must only be called on the server.");
            return;
        }

        if (taskActive)
        {
            Debug.LogWarning("[ScavengerRaidTask] StartTask called but task is already running.");
            return;
        }

        if (!ValidateReferences()) return;

        taskActive = true;
        aliensSpawnedSoFar = 0;
        livingAliens.Clear();

        scientistHealth = scientistNPC.GetComponent<ScientistHealth>()
            ?? scientistNPC.gameObject.AddComponent<ScientistHealth>();

        scientistHealth.ResetHealth();
        scientistHealth.OnDeath += HandleScientistDied;

        Debug.Log("[ScavengerRaidTask] Task STARTED");
        spawnCoroutine = StartCoroutine(SpawnWavesRoutine());
    }

    public void EndTask()
    {
        if (!taskActive) return;
        taskActive = false;

        if (spawnCoroutine != null) { StopCoroutine(spawnCoroutine); spawnCoroutine = null; }

        if (scientistHealth != null)
            scientistHealth.OnDeath -= HandleScientistDied;

        ClearAllAliens();
        Debug.Log("[ScavengerRaidTask] Task force-ended and cleaned up.");
    }

    // ================================================================
    // Wave Spawning
    // ================================================================

    IEnumerator SpawnWavesRoutine()
    {
        int wave = 0;

        while (aliensSpawnedSoFar < totalAliens && taskActive)
        {
            wave++;
            int count = Mathf.Min(aliensPerWave, totalAliens - aliensSpawnedSoFar);
            Debug.Log($"[ScavengerRaidTask] Wave {wave} -- spawning {count} aliens (total spawned so far: {aliensSpawnedSoFar})");

            for (int i = 0; i < count; i++)
            {
                if (!taskActive) yield break;
                SpawnAlien();
                yield return new WaitForSeconds(spawnStaggerInterval);
            }

            if (aliensSpawnedSoFar < totalAliens && taskActive)
            {
                Debug.Log($"[ScavengerRaidTask] Next wave in {timeBetweenWaves}s...");
                yield return new WaitForSeconds(timeBetweenWaves);
            }
        }

        Debug.Log("[ScavengerRaidTask] All waves dispatched -- waiting for players to clear the raid...");
    }

    void SpawnAlien()
    {
        Vector3 pos = GetNavMeshSpawnPoint();
        GameObject alien = Instantiate(alienPrefab, pos, Quaternion.identity);

        // ✅ Network-spawn so Health (NetworkBehaviour) initialises on all clients
        NetworkObject netObj = alien.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
        else
        {
            Debug.LogError("[ScavengerRaidTask] alienPrefab has no NetworkObject component! Health will not work on clients.");
        }

        AlienMovement movement = alien.GetComponent<AlienMovement>();
        if (movement != null)
        {
            movement.scientistTarget = scientistNPC;
            movement.patrolRadius = alienPatrolRadius;
        }
        else
        {
            Debug.LogWarning("[ScavengerRaidTask] Spawned alien prefab has no AlienMovement component!");
        }

        // ✅ AlienDeathNotifier must already be on the prefab — don't AddComponent
        AlienDeathNotifier notifier = alien.GetComponent<AlienDeathNotifier>();
        if (notifier != null)
        {
            notifier.OnDied += () => HandleAlienDied(alien);
        }
        else
        {
            Debug.LogWarning("[ScavengerRaidTask] Spawned alien has no AlienDeathNotifier component!");
        }

        livingAliens.Add(alien);
        aliensSpawnedSoFar++;
    }

    Vector3 GetNavMeshSpawnPoint()
    {
        float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float radius = spawnRadius + UnityEngine.Random.Range(-spawnRadiusJitter, spawnRadiusJitter);

        Vector3 candidate = buildingCenter.position
            + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

        return NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas)
            ? hit.position
            : candidate;
    }

    // ================================================================
    // Win / Fail Conditions
    // ================================================================

    void HandleAlienDied(GameObject alien)
    {
        livingAliens.Remove(alien);
        if (!taskActive) return;

        int remaining = livingAliens.Count;
        int yetToSpawn = totalAliens - aliensSpawnedSoFar;
        Debug.Log($"[ScavengerRaidTask] Alien killed -- alive: {remaining} yet to spawn: {yetToSpawn}");

        if (yetToSpawn <= 0 && remaining == 0)
        {
            taskActive = false;
            if (scientistHealth != null)
                scientistHealth.OnDeath -= HandleScientistDied;

            Debug.Log("[ScavengerRaidTask] TASK COMPLETE -- all aliens defeated! Scientist is safe.");
            OnTaskCompleted?.Invoke();
        }
    }

    void HandleScientistDied()
    {
        if (!taskActive) return;
        taskActive = false;

        if (spawnCoroutine != null) { StopCoroutine(spawnCoroutine); spawnCoroutine = null; }

        Debug.Log("[ScavengerRaidTask] TASK FAILED -- the scientist was killed!");
        OnTaskFailed?.Invoke();

        foreach (var alien in livingAliens)
        {
            if (alien == null) continue;
            var m = alien.GetComponent<AlienMovement>();
            if (m != null) m.scientistTarget = null;
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    void ClearAllAliens()
    {
        foreach (var alien in livingAliens)
        {
            if (alien == null) continue;
            var m = alien.GetComponent<AlienMovement>();
            if (m != null) m.scientistTarget = null;

            // ✅ Use NetworkObject.Despawn for network-spawned objects
            NetworkObject netObj = alien.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn();
            else
                Destroy(alien);
        }
        livingAliens.Clear();
    }

    bool ValidateReferences()
    {
        if (buildingCenter == null) { Debug.LogError("[ScavengerRaidTask] buildingCenter is not assigned!"); return false; }
        if (scientistNPC == null) { Debug.LogError("[ScavengerRaidTask] scientistNPC is not assigned!"); return false; }
        if (alienPrefab == null) { Debug.LogError("[ScavengerRaidTask] alienPrefab is not assigned!"); return false; }
        if (alienPrefab.GetComponent<AlienMovement>() == null)
            Debug.LogWarning("[ScavengerRaidTask] alienPrefab has no AlienMovement -- scientist targeting won't work.");
        if (alienPrefab.GetComponent<NetworkObject>() == null)
            Debug.LogError("[ScavengerRaidTask] alienPrefab has no NetworkObject -- health damage will be broken on clients!");
        return true;
    }

    void OnDestroy()
    {
        if (taskActive) EndTask();
    }

    // ================================================================
    // TESTING HELPER
    // ================================================================

    public void ForceCompleteTask()
    {
        if (!taskActive)
        {
            Debug.LogWarning("[ScavengerRaidTask] ForceCompleteTask called but task is not active!");
            return;
        }

        taskActive = false;

        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        if (scientistHealth != null)
            scientistHealth.OnDeath -= HandleScientistDied;

        foreach (var alien in livingAliens)
        {
            if (alien == null) continue;
            NetworkObject netObj = alien.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn();
            else
                Destroy(alien);
        }
        livingAliens.Clear();

        Debug.Log("[ScavengerRaidTask] Task force completed!");
        OnTaskCompleted?.Invoke();
    }
}
