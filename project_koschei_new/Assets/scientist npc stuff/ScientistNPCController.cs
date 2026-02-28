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
    public float interactionRange = 3f;
    public float talkDuration = 5f;
    public float facePlayerSpeed = 5f;

    [Header("Random Patrol")]
    public float patrolRadius = 20f;

    [Header("Animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string talkingParam = "IsTalking";

    NavMeshAgent agent;
    ScientistState state = ScientistState.Idle;
    float stateTimer = 0f;
    Vector3 homePosition;
    bool isTalking = false;
    float talkTimer = 0f;
    Transform talkingToPlayer;

    private ScientistNPCDialogue _dialogue;
    private bool _playerWasNearby = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        homePosition = transform.position;
        state = ScientistState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();

        _dialogue = GetComponent<ScientistNPCDialogue>();
    }

    void Update()
    {
        if (!isTalking)
            CheckForPlayerInteraction();

        switch (state)
        {
            case ScientistState.Patrol: UpdatePatrol(); break;
            case ScientistState.Idle: UpdateIdle(); break;
            case ScientistState.Talk: UpdateTalk(); break;
        }

        UpdateAnimationFromAgent();
    }

    void CheckForPlayerInteraction()
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, interactionRange);

        GameObject closestPlayer = null;
        float closestDist = interactionRange;

        foreach (Collider col in nearbyColliders)
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

        if (closestPlayer != null)
        {
            if (!_playerWasNearby)
            {
                _playerWasNearby = true;
                // Only notify local owned player
                var netObj = closestPlayer.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null && netObj.IsOwner && _dialogue != null)
                    _dialogue.SetPlayerNearby(true);
            }

            StarterAssetsInputs playerInput = closestPlayer.GetComponent<StarterAssetsInputs>();
            if (playerInput != null && playerInput.interact)
            {
                playerInput.interact = false;

                var netObj = closestPlayer.GetComponent<Unity.Netcode.NetworkObject>();
                if (_dialogue != null && netObj != null)
                    _dialogue.RequestInteract(netObj.OwnerClientId);

                StartTalking(closestPlayer.transform);
            }
        }
        else
        {
            if (_playerWasNearby)
            {
                _playerWasNearby = false;
                if (_dialogue != null)
                    _dialogue.SetPlayerNearby(false);
            }
        }
    }

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

    void UpdateIdle()
    {
        stateTimer -= Time.deltaTime;
        transform.Rotate(Vector3.up, 30f * Time.deltaTime);

        if (stateTimer <= 0f)
        {
            state = ScientistState.Patrol;
            agent.speed = patrolSpeed;
            SetRandomPatrolDestination();
        }
    }

    void StartTalking(Transform player)
    {
        if (isTalking) return;

        isTalking = true;
        talkingToPlayer = player;
        state = ScientistState.Talk;
        talkTimer = talkDuration;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        Debug.Log("[Scientist] Started talking");
    }

    void UpdateTalk()
    {
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        if (talkingToPlayer != null)
            FaceTarget(talkingToPlayer);

        talkTimer -= Time.deltaTime;

        if (talkTimer <= 0f)
            StopTalking();
    }

    void FaceTarget(Transform target)
    {
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

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
        talkingToPlayer = null;
        agent.isStopped = false;
        state = ScientistState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();

        if (_dialogue != null)
            _dialogue.SetPlayerNearby(false);

        Debug.Log("[Scientist] Stopped talking");
    }

    void UpdateAnimationFromAgent()
    {
        if (animator == null) return;

        float speed = isTalking ? 0f : agent.velocity.magnitude;
        animator.SetFloat(speedParam, speed);
        animator.SetBool(talkingParam, isTalking);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);

        Gizmos.color = Color.yellow;
        Vector3 home = Application.isPlaying ? homePosition : transform.position;
        Gizmos.DrawWireSphere(home, patrolRadius);
    }
}
