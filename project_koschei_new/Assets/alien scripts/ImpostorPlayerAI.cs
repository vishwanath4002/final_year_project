using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ImpostorPlayerAI : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3f;
    public float runSpeed = 5f;
    public float acceleration = 5f;

    [Header("Physics")]
    public float gravity = -9.81f;
    public float groundedGravity = -5f;

    [Header("AI Behavior - Social Distances")]
    public float detectionRadius = 25f;
    public float approachDistance = 8f;
    public float conversationDistance = 2.5f;
    public float personalSpaceDistance = 1.2f;

    [Header("AI Behavior - Timers")]
    public float minSocialTime = 8f;
    public float maxSocialTime = 20f;
    public float idleWanderInterval = 5f;
    public float socialWanderInterval = 8f;

    [Header("AI Behavior - Movement")]
    public float nearPlayerWanderRadius = 4f;
    public float farWanderRadius = 15f;
    public float arrivalThreshold = 0.8f;
    public float stopMovingThreshold = 0.3f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public LayerMask groundMask;
    public float groundDistance = 0.2f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private CharacterController controller;
    private Animator anim;
    private Vector3 velocity;
    private Vector3 lastPosition;
    private bool isGrounded;

    private float currentSpeed = 0f;
    private float currentDirection = 0f;
    private float movementMagnitude = 0f;

    private enum AIState
    {
        Wandering,
        Approaching,
        Socializing,
        Stopping
    }

    private AIState currentState = AIState.Wandering;
    private Transform targetPlayer;
    private Vector3 wanderTarget;
    private float stateTimer;
    private float socialTimer;
    private bool hasApproachedPlayer = false;
    private bool isInitialized = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
        {
            Debug.LogError("ImpostorPlayerAI: CharacterController not found!");
            enabled = false;
            return;
        }

        anim = GetComponentInChildren<Animator>();
        if (anim == null)
            Debug.LogWarning("ImpostorPlayerAI: Animator not found in children.");

        lastPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Initialize AI after network spawn
            Invoke(nameof(InitializeAI), 0.5f); // Small delay to ensure everything is set up
        }
    }

    void InitializeAI()
    {
        stateTimer = idleWanderInterval;
        socialTimer = 0f;

        // Pick initial wander point away from spawn
        PickWanderPoint(transform.position, farWanderRadius);
        isInitialized = true;

        Debug.Log($"[ImpostorAI] Initialized at {transform.position}, wander target: {wanderTarget}");
    }

    void Update()
    {
        if (!IsServer || !isInitialized)
        {
            return;
        }

        if (controller == null) return;

        CheckGrounded();
        UpdateAIBehavior();
        HandleGravity();
        UpdateAnimations();

        // Debug output every 2 seconds
        if (showDebugInfo && Time.frameCount % 120 == 0)
        {
            Debug.Log($"[ImpostorAI] State: {currentState} | Target: {(targetPlayer ? targetPlayer.name : "NONE")} | Pos: {transform.position:F1} | WanderTarget: {wanderTarget:F1} | Dist: {Vector3.Distance(transform.position, wanderTarget):F1}");
        }
    }

    void CheckGrounded()
    {
        if (groundCheck != null)
        {
            isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        }
        else
        {
            isGrounded = Physics.Raycast(
                transform.position + Vector3.up * 0.1f,
                Vector3.down,
                0.3f,
                groundMask
            );
        }
    }

    void UpdateAIBehavior()
    {
        targetPlayer = FindNearestPlayer();

        if (targetPlayer != null)
        {
            float distToPlayer = Vector3.Distance(transform.position, targetPlayer.position);
            HandlePlayerInteraction(distToPlayer);
        }
        else
        {
            HandleSoloWandering();
        }

        stateTimer -= Time.deltaTime;
    }

    void HandlePlayerInteraction(float distToPlayer)
    {
        // PERSONAL SPACE: Too close - stop and face player
        if (distToPlayer < personalSpaceDistance)
        {
            if (currentState != AIState.Stopping)
            {
                ChangeState(AIState.Stopping);
                Debug.Log("[ImpostorAI] 🛑 Entered personal space - stopping");
            }
            FaceTarget(targetPlayer.position);
            socialTimer += Time.deltaTime;
            return;
        }

        // CONVERSATION DISTANCE: Perfect spot for interaction
        if (distToPlayer < conversationDistance)
        {
            socialTimer += Time.deltaTime;

            if (currentState != AIState.Socializing)
            {
                ChangeState(AIState.Socializing);
                socialTimer = 0f;
                stateTimer = socialWanderInterval;
                PickWanderPoint(targetPlayer.position, nearPlayerWanderRadius);
                Debug.Log("[ImpostorAI] 💬 Started socializing");
            }

            // Stay for minimum time, then can wander slightly
            if (socialTimer < minSocialTime)
            {
                FaceTarget(targetPlayer.position);
            }
            else
            {
                float distToWander = Vector3.Distance(transform.position, wanderTarget);

                if (distToWander < stopMovingThreshold)
                {
                    FaceTarget(targetPlayer.position);
                    if (stateTimer <= 0f)
                    {
                        PickWanderPoint(targetPlayer.position, nearPlayerWanderRadius);
                        stateTimer = socialWanderInterval;
                    }
                }
                else
                {
                    MoveTowards(wanderTarget, walkSpeed * 0.6f);
                }
            }

            // Exit if socializing too long
            if (socialTimer > maxSocialTime)
            {
                Debug.Log("[ImpostorAI] ⏰ Social time exceeded, wandering off");
                ChangeState(AIState.Wandering);
                PickWanderPoint(transform.position, farWanderRadius);
                stateTimer = idleWanderInterval;
                hasApproachedPlayer = false;
                socialTimer = 0f;
            }
            return;
        }

        // APPROACH DISTANCE: Walk toward player
        if (distToPlayer < approachDistance)
        {
            if (currentState != AIState.Approaching && currentState != AIState.Socializing)
            {
                ChangeState(AIState.Approaching);
                hasApproachedPlayer = true;
                Debug.Log("[ImpostorAI] 🚶 Approaching player");
            }

            if (currentState == AIState.Approaching)
            {
                MoveTowards(targetPlayer.position, walkSpeed);
            }
            return;
        }

        // DETECTION RANGE: Can see player from afar
        if (distToPlayer < detectionRadius)
        {
            if (currentState == AIState.Wandering)
            {
                // 30% chance per second to approach visible player
                if (!hasApproachedPlayer && Random.value < 0.3f * Time.deltaTime)
                {
                    ChangeState(AIState.Approaching);
                    hasApproachedPlayer = true;
                    Debug.Log("[ImpostorAI] 👀 Detected player, approaching");
                }
            }
            else if (currentState == AIState.Approaching)
            {
                MoveTowards(targetPlayer.position, walkSpeed);
            }
            return;
        }

        // Player moved out of range
        if (currentState != AIState.Wandering)
        {
            Debug.Log("[ImpostorAI] 🚶 Player left range, resuming wandering");
            HandleSoloWandering();
        }
    }

    void HandleSoloWandering()
    {
        if (currentState != AIState.Wandering)
        {
            ChangeState(AIState.Wandering);
            PickWanderPoint(transform.position, farWanderRadius);
            stateTimer = idleWanderInterval;
            socialTimer = 0f;
            hasApproachedPlayer = false;
        }

        float distToWander = Vector3.Distance(transform.position, wanderTarget);

        // Validate wander target
        if (float.IsNaN(wanderTarget.x) || wanderTarget == Vector3.zero)
        {
            PickWanderPoint(transform.position, farWanderRadius);
            stateTimer = idleWanderInterval;
            Debug.LogWarning("[ImpostorAI] Invalid wander target detected, picking new one");
            return;
        }

        if (distToWander < stopMovingThreshold)
        {
            // Reached destination, pick new one after timer
            if (stateTimer <= 0f)
            {
                PickWanderPoint(transform.position, farWanderRadius);
                stateTimer = idleWanderInterval;
                if (showDebugInfo)
                    Debug.Log($"[ImpostorAI] ✅ Reached wander point, picking new target");
            }
        }
        else if (distToWander < arrivalThreshold)
        {
            // Close to target, slow down
            MoveTowards(wanderTarget, walkSpeed * 0.6f);
        }
        else
        {
            // Normal wandering speed
            MoveTowards(wanderTarget, walkSpeed);
        }
    }

    void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        if (showDebugInfo)
            Debug.Log($"[ImpostorAI] 🔄 State: {currentState} → {newState}");

        currentState = newState;
    }

    Transform FindNearestPlayer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return null;

        Transform closest = null;
        float closestDist = detectionRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            // Make sure it's not this AI itself
            if (client.PlayerObject.gameObject == gameObject)
                continue;

            float d = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                closest = client.PlayerObject.transform;
            }
        }

        return closest;
    }

    void PickWanderPoint(Vector3 centerPos, float radius)
    {
        // Ensure minimum distance from current position
        float minDistance = 2f;
        int maxAttempts = 10;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * Random.Range(radius * 0.5f, radius);
            Vector3 potentialTarget = centerPos + new Vector3(randomCircle.x, 0f, randomCircle.y);
            potentialTarget.y = centerPos.y;

            // Make sure it's not too close to current position
            if (Vector3.Distance(transform.position, potentialTarget) >= minDistance)
            {
                wanderTarget = potentialTarget;

                if (showDebugInfo)
                {
                    Debug.Log($"[ImpostorAI] 🎯 New wander target: {wanderTarget:F1} (radius: {radius:F1}, dist: {Vector3.Distance(transform.position, wanderTarget):F1})");
                    Debug.DrawLine(transform.position, wanderTarget, Color.cyan, 3f);
                }
                return;
            }
        }

        // Fallback: just pick something
        Vector2 fallbackCircle = Random.insideUnitCircle * radius;
        wanderTarget = centerPos + new Vector3(fallbackCircle.x, 0f, fallbackCircle.y);
        wanderTarget.y = centerPos.y;
    }

    void FaceTarget(Vector3 targetPos)
    {
        Vector3 direction = targetPos - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * 5f
            );
        }
    }

    void MoveTowards(Vector3 target, float speed)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance < 0.1f)
            return;

        // Rotate toward target
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * 8f
            );
        }

        // Move forward
        Vector3 moveDirection = direction.normalized;
        Vector3 movement = moveDirection * speed * Time.deltaTime;

        // Use SimpleMove if grounded for better CharacterController behavior
        if (isGrounded)
        {
            controller.SimpleMove(moveDirection * speed);
        }
        else
        {
            controller.Move(movement);
        }

        if (showDebugInfo && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[ImpostorAI] 🚶 Moving toward {target:F1} at speed {speed:F1}, distance: {distance:F1}");
        }

        if (showDebugInfo)
        {
            Debug.DrawRay(transform.position, moveDirection * 2f, Color.green, 0.1f);
        }
    }

    void HandleGravity()
    {
        // Only apply manual gravity if not using SimpleMove
        if (!isGrounded)
        {
            if (velocity.y < 0f)
            {
                velocity.y = groundedGravity;
            }
            else
            {
                velocity.y += gravity * Time.deltaTime;
            }

            controller.Move(velocity * Time.deltaTime);
        }
        else
        {
            velocity.y = groundedGravity;
        }
    }

    void UpdateAnimations()
    {
        if (anim == null) return;

        anim.SetBool("IsGrounded", isGrounded);

        Vector3 worldVelocity = Vector3.zero;
        if (Time.deltaTime > 0)
        {
            worldVelocity = (transform.position - lastPosition) / Time.deltaTime;
        }
        lastPosition = transform.position;

        Vector3 localVel = transform.InverseTransformDirection(worldVelocity);
        localVel.y = 0f;

        float targetForward = Mathf.Clamp(localVel.z / runSpeed, -1f, 1f);
        float targetRight = Mathf.Clamp(localVel.x / runSpeed, -1f, 1f);

        currentSpeed = Mathf.Lerp(currentSpeed, targetForward, Time.deltaTime * acceleration);
        currentDirection = Mathf.Lerp(currentDirection, targetRight, Time.deltaTime * acceleration);
        movementMagnitude = new Vector2(currentDirection, currentSpeed).magnitude;

        anim.SetFloat("Speed", currentSpeed);
        anim.SetBool("IsMoving", movementMagnitude > 0.05f);
        anim.SetFloat("Direction", currentDirection);
    }

    void OnDrawGizmos()
    {
        if (!showDebugInfo) return;

        // Detection radius (yellow)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Approach distance (cyan)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, approachDistance);

        // Conversation distance (green)
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, conversationDistance);

        // Personal space (red)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, personalSpaceDistance);

        // Current wander target
        if (wanderTarget != Vector3.zero)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(wanderTarget, 0.5f);
            Gizmos.DrawLine(transform.position, wanderTarget);
        }

        // Target player
        if (targetPlayer != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, targetPlayer.position);
        }
    }
}