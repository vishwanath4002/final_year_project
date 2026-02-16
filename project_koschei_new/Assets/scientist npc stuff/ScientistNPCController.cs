using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using StarterAssets;

public enum ScientistState { Idle, Patrol, Talk }

public class ScientistNPCController : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float patrolSpeed = 2f;

    [Header("Idle / Talk")]
    public float idleTimeAtPatrolPoint = 2f;
    public float interactionRange = 3f;  // player must be within this range to press E
    public float talkDuration = 5f;      // how long the talk animation plays
    public float facePlayerSpeed = 5f;   // how fast NPC rotates to face player while talking

    [Header("Random Patrol")]
    public float patrolRadius = 20f;

    [Header("Animation")]
    public Animator animator;
    public string speedParam = "Speed";       // float for walk blend
    public string talkingParam = "IsTalking"; // bool for talk layer

    [Header("Interaction Prompt")]
    //public GameObject interactionPrompt;  // UI hint (optional, e.g., "Press E")

    NavMeshAgent agent;
    ScientistState state = ScientistState.Idle;
    float stateTimer = 0f;
    Vector3 homePosition;
    bool isTalking = false;
    float talkTimer = 0f;
    Transform talkingToPlayer;  // Reference to player we're talking to

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        homePosition = transform.position;
        state = ScientistState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();

        //if (interactionPrompt != null)
            //interactionPrompt.SetActive(false);
    }

    void Update()
    {
        // Check for player interaction if not talking
        if (!isTalking)
        {
            CheckForPlayerInteraction();
        }

        // State machine
        switch (state)
        {
            case ScientistState.Patrol:
                UpdatePatrol();
                break;
            case ScientistState.Idle:
                UpdateIdle();
                break;
            case ScientistState.Talk:
                UpdateTalk();
                break;
        }

        UpdateAnimationFromAgent();
    }

    // ========== Player Interaction Detection ==========
    void CheckForPlayerInteraction()
    {
        // Find all nearby colliders with Player tag
        Collider[] nearbyPlayers = Physics.OverlapSphere(transform.position, interactionRange);

        GameObject closestPlayer = null;
        float closestDist = interactionRange;

        foreach (Collider col in nearbyPlayers)
        {
            if (col.CompareTag("Player"))
            {
                float dist = Vector3.Distance(transform.position, col.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestPlayer = col.gameObject;
                }
            }
        }

        // Show/hide prompt based on nearby player
        if (closestPlayer != null)
        {
            //if (interactionPrompt != null)
                //interactionPrompt.SetActive(true);

            // Check if player pressed E
            StarterAssetsInputs playerInput = closestPlayer.GetComponent<StarterAssetsInputs>();
            if (playerInput != null && playerInput.interact)
            {
                playerInput.interact = false;  // consume input
                StartTalking(closestPlayer.transform);
            }
        }
        else
        {
            //if (interactionPrompt != null)
                //interactionPrompt.SetActive(false);
        }
    }

    // ========== Random Patrol ==========
    void UpdatePatrol()
    {
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            state = ScientistState.Idle;
            stateTimer = idleTimeAtPatrolPoint;
            agent.isStopped = true;
        }
    }

    void SetRandomPatrolDestination()
    {
        Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
        Vector3 candidate = homePosition + new Vector3(randomCircle.x, 0f, randomCircle.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
        else
            agent.SetDestination(candidate);

        agent.isStopped = false;
    }

    // ========== Idle ==========
    void UpdateIdle()
    {
        stateTimer -= Time.deltaTime;
        transform.Rotate(Vector3.up, 30f * Time.deltaTime);  // look around

        if (stateTimer <= 0f)
        {
            state = ScientistState.Patrol;
            agent.speed = patrolSpeed;
            SetRandomPatrolDestination();
        }
    }

    // ========== Talk State ==========
    void StartTalking(Transform player)
    {
        if (isTalking) return;

        isTalking = true;
        talkingToPlayer = player;  // Store reference to player
        state = ScientistState.Talk;
        talkTimer = talkDuration;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        //if (interactionPrompt != null)
            //interactionPrompt.SetActive(false);

        Debug.Log("[Scientist] Started talking");

        // Trigger your dialogue system here:
        // DialogueManager.Instance.StartConversation(this);
    }

    void UpdateTalk()
    {
        // LOCKED: cannot move while talking
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        // Face the player while talking
        if (talkingToPlayer != null)
        {
            FaceTarget(talkingToPlayer);
        }

        // Count down talk timer
        talkTimer -= Time.deltaTime;

        if (talkTimer <= 0f)
        {
            // 5 seconds passed - stop talking automatically
            StopTalking();
        }
    }

    void FaceTarget(Transform target)
    {
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;  // Keep rotation only on Y axis (don't tilt up/down)

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * facePlayerSpeed);
        }
    }

    public void StopTalking()
    {
        if (!isTalking) return;

        isTalking = false;
        talkTimer = 0f;
        talkingToPlayer = null;  // Clear player reference
        agent.isStopped = false;
        state = ScientistState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();

        Debug.Log("[Scientist] Stopped talking");
    }

    // ========== Animation from NavMeshAgent velocity ==========
    void UpdateAnimationFromAgent()
    {
        if (animator == null) return;

        // Speed parameter (0 = idle, >0 = walking)
        float speed = isTalking ? 0f : agent.velocity.magnitude;
        animator.SetFloat(speedParam, speed);

        // Talking parameter (bool for talk animation layer)
        animator.SetBool(talkingParam, isTalking);
    }

    // ========== Gizmos ==========
    void OnDrawGizmosSelected()
    {
        // Interaction range (cyan)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);

        // Patrol area (yellow)
        Gizmos.color = Color.yellow;
        Vector3 home = Application.isPlaying ? homePosition : transform.position;
        Gizmos.DrawWireSphere(home, patrolRadius);
    }
}
