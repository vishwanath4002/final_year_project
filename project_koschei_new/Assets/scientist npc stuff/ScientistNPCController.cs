using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using StarterAssets;

public enum ScientistState { Idle, Patrol, Talk }

public class ScientistNPCController : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float patrolSpeed = 2f;

    [Header("Idle / Talk")]
    public float idleTimeAtPatrolPoint = 2f;
    public float interactionRange = 3f;
    public float facePlayerSpeed = 5f;

    [Header("Random Patrol")]
    public float patrolRadius = 20f;

    [Header("Animation")]
    public Animator animator;
    public string speedParam = "Speed";

    NavMeshAgent agent;
    ScientistState state = ScientistState.Idle;
    float stateTimer = 0f;
    Vector3 homePosition;
    bool isTalking = false;
    Transform talkingToPlayer;

    private ScientistNPCDialogue _dialogue;
    private bool _playerWasNearby = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        _dialogue = GetComponent<ScientistNPCDialogue>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        homePosition = transform.position;
        state = ScientistState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();
    }

    void Update()
    {
        // Proximity check always runs so UI stays accurate on all clients
        CheckForPlayerProximity();

        // Interact input only checked when not talking
        if (!(_dialogue != null && _dialogue.IsTalking))
            CheckForInteractPress();

        switch (state)
        {
            case ScientistState.Patrol: UpdatePatrol(); break;
            case ScientistState.Idle: UpdateIdle(); break;
            case ScientistState.Talk: UpdateTalk(); break;
        }

        // Only the server drives speed -- NetworkVariable syncs it to all clients
        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        if (isServer)
        {
            float speed = isTalking ? 0f : agent.velocity.magnitude;
            if (_dialogue != null) _dialogue.ServerUpdateSpeed(speed);
            UpdateAnimationFromAgent();
        }
    }

    // Runs every frame on every client -- keeps IsNearNPC and UI hint accurate
    void CheckForPlayerProximity()
    {
        Collider[] nearby = Physics.OverlapSphere(transform.position, interactionRange);

        GameObject localPlayer = null;
        foreach (Collider col in nearby)
        {
            if (!col.CompareTag("Player")) continue;
            var netObj = col.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                localPlayer = col.gameObject;
                break;
            }
        }

        if (localPlayer != null)
        {
            if (!_playerWasNearby)
            {
                _playerWasNearby = true;
                if (_dialogue != null)
                    _dialogue.SetPlayerNearby(true);
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

    // Runs every frame only when not talking -- handles E / interact press
    void CheckForInteractPress()
    {
        Collider[] nearby = Physics.OverlapSphere(transform.position, interactionRange);

        GameObject closestPlayer = null;
        float closestDist = interactionRange;

        foreach (Collider col in nearby)
        {
            if (!col.CompareTag("Player")) continue;
            float dist = Vector3.Distance(transform.position, col.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestPlayer = col.gameObject;
            }
        }

        if (closestPlayer == null) return;

        StarterAssetsInputs playerInput = closestPlayer.GetComponent<StarterAssetsInputs>();
        if (playerInput != null && playerInput.interact)
        {
            playerInput.interact = false;

            var netObj = closestPlayer.GetComponent<NetworkObject>();
            if (_dialogue != null && netObj != null)
                _dialogue.RequestInteractFromController(netObj.OwnerClientId);

            StartTalking(closestPlayer.transform);
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

    public void StartTalking(Transform player)
    {
        if (isTalking) return;

        isTalking = true;
        talkingToPlayer = player;
        state = ScientistState.Talk;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    void UpdateTalk()
    {
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        if (talkingToPlayer != null)
            FaceTarget(talkingToPlayer);
    }

    void FaceTarget(Transform target)
    {
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation,
                Time.deltaTime * facePlayerSpeed);
        }
    }

    public void StopTalking()
    {
        if (!isTalking) return;

        isTalking = false;
        talkingToPlayer = null;
        agent.isStopped = false;
        state = ScientistState.Patrol;
        agent.speed = patrolSpeed;
        SetRandomPatrolDestination();

        // Reset so proximity check re-evaluates cleanly next frame
        _playerWasNearby = false;
    }

    void UpdateAnimationFromAgent()
    {
        if (animator == null) return;
        float speed = isTalking ? 0f : agent.velocity.magnitude;
        animator.SetFloat(speedParam, speed);
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