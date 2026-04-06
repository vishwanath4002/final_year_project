using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Attach to the Impostor ROOT GameObject alongside ImpostorPlayerAI.
/// Call OnShot() from ImpostorBulletDetector when a bullet hits.
/// Disables ImpostorPlayerAI, flees, then despawns.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class ImpostorFleeOnHit : NetworkBehaviour
{
    [Header("Flee Settings")]
    public float fleeSpeed = 8f;
    public float fleeDespawnDelay = 4f;
    public float fleeDistance = 60f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private NavMeshAgent agent;
    private ImpostorPlayerAI impostorAI;

    private bool isFleeing = false;
    private float fleeTimer = 0f;
    private Vector3 fleeDestination;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        impostorAI = GetComponent<ImpostorPlayerAI>();
    }

    private void Update()
    {
        if (!IsServer || !isFleeing) return;

        fleeTimer += Time.deltaTime;
        agent.speed = fleeSpeed;

        // If agent reaches destination, keep pushing further in the same direction
        if (!agent.hasPath || agent.remainingDistance < 1f)
        {
            Vector3 dir = (fleeDestination - transform.position);
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;

            Vector3 furtherPoint = transform.position + dir.normalized * fleeDistance;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(furtherPoint, out hit, 15f, NavMesh.AllAreas))
                fleeDestination = hit.position;

            agent.SetDestination(fleeDestination);
        }

        if (showDebugInfo && Time.frameCount % 60 == 0)
            Debug.Log($"[ImpostorFleeOnHit] Fleeing... {fleeTimer:F1}s / {fleeDespawnDelay:F1}s");

        if (fleeTimer >= fleeDespawnDelay)
            DespawnImpostor();
    }

    /// <summary>
    /// Called by ImpostorBulletDetector when a bullet hits the impostor.
    /// </summary>
    public void OnShot()
    {
        if (!IsServer) return;
        if (isFleeing) return;

        isFleeing = true;
        fleeTimer = 0f;

        if (showDebugInfo)
            Debug.Log("[ImpostorFleeOnHit] Shot! Disabling AI and fleeing...");

        // Stop ImpostorPlayerAI from overriding the NavMeshAgent
        if (impostorAI != null)
            impostorAI.enabled = false;

        // Run away from the nearest player
        Vector3 threatOrigin = transform.position + transform.forward;
        Transform nearest = FindNearestPlayer();
        if (nearest != null)
            threatOrigin = nearest.position;

        Vector3 awayDir = (transform.position - threatOrigin).normalized;
        if (awayDir == Vector3.zero)
            awayDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;

        Vector3 fleePoint = transform.position + awayDir * fleeDistance;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(fleePoint, out hit, 15f, NavMesh.AllAreas))
            fleeDestination = hit.position;
        else
            fleeDestination = fleePoint;

        agent.speed = fleeSpeed;
        agent.SetDestination(fleeDestination);

        if (showDebugInfo)
            Debug.Log($"[ImpostorFleeOnHit] Fleeing to {fleeDestination:F1}");
    }

    private void DespawnImpostor()
    {
        if (showDebugInfo)
            Debug.Log("[ImpostorFleeOnHit] Despawning impostor.");

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
        else
            Destroy(gameObject);
    }

    private Transform FindNearestPlayer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return null;

        Transform closest = null;
        float closestDist = float.MaxValue;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            if (client.PlayerObject.gameObject == gameObject) continue;

            float d = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                closest = client.PlayerObject.transform;
            }
        }

        return closest;
    }
}
