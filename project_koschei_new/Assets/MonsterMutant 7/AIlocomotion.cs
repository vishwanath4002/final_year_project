using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class AILocomotion : MonoBehaviour
{
    enum AIState { Patrol, Chase, Search }
    [SerializeField] AIState state = AIState.Patrol;

    [Header("Detection")]
    public float viewDistance = 15f;
    [Range(0, 180)] public float viewAngle = 90f;
    public float proximityRange = 3f;
    public LayerMask sightMask;
    public string playerTag = "Player";

    [Header("Movement")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 5f;
    public float patrolRadius = 10f;
    public float attackRange = 2f;

    [Header("Combat")]
    public float attackCooldown = 2f;
    public float attackDamage = 25f;
    public float attackDuration = 1.2f;

    [Header("Animation")]
    public string speedParam = "Speed";

    NavMeshAgent agent;
    Animator animator;

    Vector3 homePosition;
    Vector3 lastSeenPlayerPos;
    float searchTimer;

    Transform currentTarget;
    float lastAttackTime;
    bool isAttacking;
    bool hasDealtDamage;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        homePosition = transform.position;
        state = AIState.Patrol;
    }

    void Update()
    {
        if (agent == null || animator == null)
            return;

        Transform detected = DetectPlayer();

        if (detected != null)
        {
            currentTarget = detected;
            lastSeenPlayerPos = detected.position;
            state = AIState.Chase;
        }

        switch (state)
        {
            case AIState.Patrol:
                UpdatePatrol();
                break;

            case AIState.Chase:
                UpdateChase(detected != null);
                break;

            case AIState.Search:
                UpdateSearch();
                break;
        }

        animator.SetFloat(speedParam, agent.velocity.magnitude);
    }

    // ===================== DETECTION =====================
    Transform DetectPlayer()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, viewDistance);

        foreach (Collider col in hits)
        {
            if (!col.CompareTag(playerTag))
                continue;

            Vector3 dir = col.transform.position - transform.position;
            float distance = dir.magnitude;

            if (distance <= proximityRange)
                return col.transform;

            float angle = Vector3.Angle(transform.forward, dir);
            if (angle > viewAngle * 0.5f)
                continue;

            if (Physics.Raycast(transform.position + Vector3.up,
                                dir.normalized,
                                out RaycastHit hit,
                                viewDistance,
                                sightMask))
            {
                if (hit.collider.CompareTag(playerTag))
                    return hit.transform;
            }
        }

        return null;
    }

    // ===================== CHASE =====================
    void UpdateChase(bool seesPlayer)
    {
        if (currentTarget == null)
        {
            EnterSearch();
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.position);
        agent.speed = chaseSpeed;

        if (dist <= attackRange)
        {
            if (!isAttacking && Time.time >= lastAttackTime + attackCooldown)
            {
                StartCoroutine(PerformAttack());
            }
        }
        else
        {
            if (!isAttacking)
            {
                agent.isStopped = false;
                agent.SetDestination(currentTarget.position);
            }
        }

        if (!seesPlayer)
        {
            EnterSearch();
        }
    }

    // ===================== ATTACK =====================
    IEnumerator PerformAttack()
    {
        isAttacking = true;
        hasDealtDamage = false;
        lastAttackTime = Time.time;

        agent.isStopped = true;
        agent.ResetPath();

        TriggerRandomAttack();

        float timer = 0f;

        while (timer < attackDuration)
        {
            timer += Time.deltaTime;

            if (currentTarget != null)
                FaceTarget(currentTarget.position); // continuous facing

            TryDealDamage();

            yield return null;
        }

        agent.isStopped = false;
        isAttacking = false;
    }

    void TryDealDamage()
    {
        if (hasDealtDamage) return;
        if (currentTarget == null) return;

        float dist = Vector3.Distance(transform.position, currentTarget.position);

        if (dist <= attackRange + 0.5f)
        {
            Health h = currentTarget.GetComponent<Health>();
            if (h != null)
            {
                h.TakeDamage(attackDamage);
                hasDealtDamage = true;
            }
        }
    }

    void TriggerRandomAttack()
    {
        int attackIndex = Random.Range(1, 13);

        switch (attackIndex)
        {
            case 1: animator.SetTrigger("Attack1"); break;
            case 2: animator.SetTrigger("Attack1LSpike"); break;
            case 3: animator.SetTrigger("Attack1RSpike"); break;
            case 4: animator.SetTrigger("Attack2"); break;
            case 5: animator.SetTrigger("Attack2LSpike"); break;
            case 6: animator.SetTrigger("Attack2RLSpike"); break;
            case 7: animator.SetTrigger("Attack3"); break;
            case 8: animator.SetTrigger("Attack3RSpike"); break;
            case 9: animator.SetTrigger("Attack4"); break;
            case 10: animator.SetTrigger("Attack4RSpike"); break;
            case 11: animator.SetTrigger("Attack5"); break;
            case 12: animator.SetTrigger("Attack5LSpike"); break;
        }
    }

    // ===================== PATROL =====================
    void UpdatePatrol()
    {
        agent.speed = patrolSpeed;

        if (!agent.pathPending &&
            agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            SetRandomPatrolPoint();
        }
    }

    // ===================== SEARCH =====================
    void UpdateSearch()
    {
        agent.SetDestination(lastSeenPlayerPos);

        if (agent.remainingDistance <= 0.5f)
        {
            searchTimer -= Time.deltaTime;
            transform.Rotate(Vector3.up, 150f * Time.deltaTime);

            if (searchTimer <= 0f)
            {
                currentTarget = null;
                state = AIState.Patrol;
            }
        }
    }

    void EnterSearch()
    {
        state = AIState.Search;
        searchTimer = 5f;
    }

    // ===================== HELPERS =====================
    void SetRandomPatrolPoint()
    {
        Vector2 rand = Random.insideUnitCircle * patrolRadius;
        Vector3 target = homePosition + new Vector3(rand.x, 0, rand.y);
        agent.SetDestination(target);
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 dir = (target - transform.position).normalized;
        dir.y = 0;

        if (dir != Vector3.zero)
        {
            Quaternion rot = Quaternion.LookRotation(dir);
            transform.rotation =
                Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 10f);
        }
    }
}
