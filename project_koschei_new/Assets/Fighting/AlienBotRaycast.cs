using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class AlienBotRaycast : NetworkBehaviour
{
    [Header("Components")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator animator;

    [Header("Vision")]
    [SerializeField] private float detectionRadius = 25f;
    [SerializeField] private float fieldOfView = 110f;        // degrees
    [SerializeField] private LayerMask visionMask;            // layers that can block vision
    [SerializeField] private Transform eyePoint;              // where raycasts originate (e.g., head)

    [Header("Combat")]
    [SerializeField] private float attackRange = 2.2f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private float blockChance = 0.2f;        // 20% chance to block when "hit"
    [SerializeField] private float health = 100f;

    [Header("Roaming")]
    [SerializeField] private float roamRadius = 15f;
    [SerializeField] private float roamWaitTime = 3f;

    [Header("Animator Parameters")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string rageTrigger = "Rage";
    [SerializeField] private string attackTrigger = "Attack";
    [SerializeField] private string blockTrigger = "Block";
    [SerializeField] private string getHitTrigger = "GetHit";
    [SerializeField] private string dieTrigger = "Die";

    private enum State { IdleRoam, Rage, Chase, Attack, Dead }
    private State currentState = State.IdleRoam;

    private Transform currentTarget;
    private float stateTimer;
    private float attackTimer;
    private Vector3 roamDestination;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Server-authoritative AI
        if (!IsServer)
        {
            if (agent != null) agent.enabled = false;
            enabled = false;
            return;
        }

        if (eyePoint == null) eyePoint = transform; // fallback
        SetRandomRoamDestination();
    }

    void Update()
    {
        if (!IsServer) return;
        if (currentState == State.Dead) return;

        attackTimer -= Time.deltaTime;

        switch (currentState)
        {
            case State.IdleRoam:
                IdleRoamUpdate();
                break;
            case State.Rage:
                RageUpdate();
                break;
            case State.Chase:
                ChaseUpdate();
                break;
            case State.Attack:
                AttackUpdate();
                break;
        }

        UpdateAnimation();
    }

    // ---------- STATE UPDATES ----------

    void IdleRoamUpdate()
    {
        // Check for target via vision first
        if (TryFindVisiblePlayer(out Transform target))
        {
            currentTarget = target;
            TriggerRage();
            return;
        }

        // Roaming logic
        if (agent.remainingDistance <= agent.stoppingDistance)
        {
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                SetRandomRoamDestination();
            }
        }
    }

    void RageUpdate()
    {
        // Short rage delay, then go to chase
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            SwitchState(State.Chase);
        }
    }

    void ChaseUpdate()
    {
        if (currentTarget == null)
        {
            SwitchState(State.IdleRoam);
            return;
        }

        // Check if still visible; if not, fall back to roam
        if (!CanSeeTarget(currentTarget))
        {
            currentTarget = null;
            SwitchState(State.IdleRoam);
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.position);

        if (dist <= attackRange)
        {
            SwitchState(State.Attack);
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(currentTarget.position);
    }

    void AttackUpdate()
    {
        if (currentTarget == null)
        {
            SwitchState(State.IdleRoam);
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.position);

        // If target leaves range, chase again
        if (dist > attackRange * 1.2f)
        {
            SwitchState(State.Chase);
            return;
        }

        // Face the target
        Vector3 dir = (currentTarget.position - transform.position);
        dir.y = 0;
        if (dir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 7f
            );
        }

        agent.isStopped = true;

        if (attackTimer <= 0f)
        {
            PerformAttack();
            attackTimer = attackCooldown;
        }
    }

    // ---------- VISION ----------

    bool TryFindVisiblePlayer(out Transform target)
    {
        target = null;
        float bestDist = detectionRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            var playerObj = client.Value.PlayerObject;
            if (playerObj == null) continue;

            Transform p = playerObj.transform;
            if (!CanSeeTarget(p)) continue;

            float dist = Vector3.Distance(transform.position, p.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                target = p;
            }
        }

        return target != null;
    }

    bool CanSeeTarget(Transform target)
    {
        Vector3 origin = eyePoint.position;
        Vector3 toTarget = (target.position + Vector3.up * 1.0f) - origin;
        float dist = toTarget.magnitude;

        if (dist > detectionRadius) return false;

        // Field of view check
        Vector3 forward = transform.forward;
        Vector3 dirNorm = toTarget.normalized;
        float angle = Vector3.Angle(forward, dirNorm);
        if (angle > fieldOfView * 0.5f) return false;

        // Raycast LOS check
        if (Physics.Raycast(origin, dirNorm, out RaycastHit hit, dist, visionMask))
        {
            // Need to hit the player collider
            if (!hit.collider.CompareTag("Player"))
            {
                return false; // Blocked by something else
            }
        }
        else
        {
            // No hit at all – assume blocked
            return false;
        }

        return true;
    }

    // ---------- ROAMING ----------

    void SetRandomRoamDestination()
    {
        Vector3 randomDir = Random.insideUnitSphere * roamRadius + transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDir, out hit, roamRadius, NavMesh.AllAreas))
        {
            roamDestination = hit.position;
            agent.isStopped = false;
            agent.SetDestination(roamDestination);
        }
        stateTimer = roamWaitTime;
    }

    // ---------- COMBAT / DAMAGE ----------

    void PerformAttack()
    {
        Debug.Log($"{name} attacks {currentTarget?.name ?? "null"}");
        if (animator != null)
        {
            animator.SetTrigger(attackTrigger);
        }

        // TODO: Apply damage here (server-side)
        // Example:
        // var hp = currentTarget.GetComponent<PlayerHealth>();
        // if (hp != null) hp.TakeDamageServerRpc(attackDamage);
    }

    /// <summary>
    /// Call this from elsewhere when player hits the alien.
    /// </summary>
    /// <param name="damage"></param>
    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float damage)
    {
        if (currentState == State.Dead) return;
        if (Random.value < blockChance)
        {
            // Successful block animation
            if (animator != null) animator.SetTrigger(blockTrigger);
            Debug.Log($"{name} blocked the attack!");
            return;
        }

        health -= damage;
        Debug.Log($"{name} took {damage} damage. HP: {health}");

        if (health <= 0f)
        {
            Die();
        }
        else
        {
            if (animator != null) animator.SetTrigger(getHitTrigger);
        }
    }

    void Die()
    {
        currentState = State.Dead;
        agent.isStopped = true;
        agent.enabled = false;

        if (animator != null) animator.SetTrigger(dieTrigger);

        Debug.Log($"{name} died.");

        // Optionally despawn after some seconds
        Invoke(nameof(DespawnSelf), 5f);
    }

    void DespawnSelf()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    // ---------- HELPERS ----------

    void TriggerRage()
    {
        SwitchState(State.Rage);
        stateTimer = 1.5f; // Rage duration before chase
        if (animator != null) animator.SetTrigger(rageTrigger);
        Debug.Log($"{name} enraged! Found a target.");
    }

    void SwitchState(State newState)
    {
        if (currentState == newState) return;
        Debug.Log($"{name} state: {currentState} -> {newState}");
        currentState = newState;

        if (newState == State.IdleRoam)
        {
            agent.isStopped = false;
            SetRandomRoamDestination();
        }
        else if (newState == State.Chase)
        {
            agent.isStopped = false;
        }
    }

    void UpdateAnimation()
    {
        if (animator == null || agent == null) return;
        float speed = agent.velocity.magnitude;
        animator.SetFloat(speedParam, speed);
    }

    // ---------- GIZMOS ----------

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, roamRadius);
    }
}
