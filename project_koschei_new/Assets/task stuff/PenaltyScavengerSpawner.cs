using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class PenaltyScavengerSpawner : MonoBehaviour
{
    [Header("Alien")]
    public GameObject alienPrefab;

    [Header("Waves")]
    public int aliensPerWave = 3;
    public float timeBetweenWaves = 15f;
    public float spawnStaggerInterval = 0.4f;
    public int maxLiveAliens = 30;

    [Header("Spawn Area")]
    public float spawnRadiusAroundPlayer = 15f;
    public float spawnRadiusJitter = 3f;
    public string playerTag = "Player";

    readonly List<GameObject> spawnedAliens = new();
    bool isSpawning = false;
    Coroutine spawnCoroutine;

    public void StartSpawning()
    {
        if (alienPrefab == null)
        {
            Debug.LogError("[PenaltySpawner] Alien Prefab is not assigned! Cannot spawn.");
            return;
        }
        if (isSpawning)
        {
            Debug.LogWarning("[PenaltySpawner] StartSpawning called but already spawning.");
            return;
        }
        isSpawning = true;
        spawnCoroutine = StartCoroutine(SpawnWavesRoutine());
        Debug.Log("[PenaltySpawner] Penalty spawning STARTED.");
    }

    public void StopSpawning()
    {
        if (!isSpawning)
        {
            Debug.LogWarning("[PenaltySpawner] StopSpawning called but not currently spawning.");
            return;
        }
        isSpawning = false;
        if (spawnCoroutine != null) { StopCoroutine(spawnCoroutine); spawnCoroutine = null; }
        ClearAliens();
        Debug.Log("[PenaltySpawner] Penalty spawning STOPPED and aliens cleared.");
    }

    IEnumerator SpawnWavesRoutine()
    {
        int wave = 0;
        while (isSpawning)
        {
            wave++;
            spawnedAliens.RemoveAll(a => a == null);
            int toSpawn = Mathf.Min(aliensPerWave, maxLiveAliens - spawnedAliens.Count);

            if (toSpawn > 0)
            {
                Debug.Log($"[PenaltySpawner] Wave {wave} -- spawning {toSpawn} penalty aliens. Live: {spawnedAliens.Count}/{maxLiveAliens}");
                for (int i = 0; i < toSpawn; i++)
                {
                    if (!isSpawning) yield break;
                    SpawnAlienNearPlayer();
                    yield return new WaitForSeconds(spawnStaggerInterval);
                }
            }
            else
            {
                Debug.Log($"[PenaltySpawner] Wave {wave} skipped -- live alien cap reached ({spawnedAliens.Count}/{maxLiveAliens}). Waiting for kills.");
            }

            Debug.Log($"[PenaltySpawner] Next penalty wave in {timeBetweenWaves}s.");
            yield return new WaitForSeconds(timeBetweenWaves);
        }
    }

    void SpawnAlienNearPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);
        if (players.Length == 0)
        {
            Debug.LogWarning("[PenaltySpawner] No players found with tag: " + playerTag);
            return;
        }

        GameObject target = players[Random.Range(0, players.Length)];
        Vector3 spawnPos = GetNavMeshPointNear(target.transform.position);
        GameObject alien = Instantiate(alienPrefab, spawnPos, Quaternion.identity);

        AlienMovement movement = alien.GetComponent<AlienMovement>();
        if (movement != null)
        {
            movement.scientistTarget = null;
            Debug.Log($"[PenaltySpawner] Alien spawned near player '{target.name}' at {spawnPos}. Scientist target: null.");
        }
        else
        {
            Debug.LogWarning("[PenaltySpawner] Spawned alien has no AlienMovement component!");
        }

        AlienDeathNotifier notifier = alien.AddComponent<AlienDeathNotifier>();
        notifier.OnDied += () =>
        {
            spawnedAliens.Remove(alien);
            Debug.Log($"[PenaltySpawner] Penalty alien killed. Remaining live: {spawnedAliens.Count}");
        };

        spawnedAliens.Add(alien);
    }

    Vector3 GetNavMeshPointNear(Vector3 center)
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float radius = spawnRadiusAroundPlayer + Random.Range(-spawnRadiusJitter, spawnRadiusJitter);
        Vector3 candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas))
            return hit.position;

        Debug.LogWarning($"[PenaltySpawner] NavMesh sample failed near {center} -- using raw position.");
        return candidate;
    }

    void ClearAliens()
    {
        int count = spawnedAliens.Count;
        foreach (var alien in spawnedAliens)
            if (alien != null) Destroy(alien);
        spawnedAliens.Clear();
        Debug.Log($"[PenaltySpawner] Cleared {count} penalty aliens.");
    }
}
