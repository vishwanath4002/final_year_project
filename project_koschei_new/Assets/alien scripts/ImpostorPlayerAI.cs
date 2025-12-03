using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class ImpostorPlayerAI : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float runSpeed = 10f;
    public float acceleration = 5f;

    [Header("Physics")]
    public float gravity = -9.81f;

    [Header("AI Behavior")]
    public float detectionRadius = 15f;
    public float followDistance = 3f;
    public float wanderRadius = 8f;
    public float stateChangeInterval = 3f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public LayerMask groundMask;
    public float groundDistance = 0.2f;

    private CharacterController controller;
    private Animator anim;
    private Vector3 velocity;
    private Vector3 lastPosition;
    private bool isGrounded;

    private float currentSpeed = 0f;
    private float currentDirection = 0f;
    private float movementMagnitude = 0f;

    private Transform targetPlayer;
    private Vector3 wanderTarget;
    private float stateTimer;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
            Debug.LogError("ImpostorPlayerAI: CharacterController not found.");

        anim = GetComponentInChildren<Animator>();
        if (anim == null)
            Debug.LogWarning("ImpostorPlayerAI: Animator not found in children.");

        lastPosition = transform.position;
        stateTimer = stateChangeInterval;
    }

    void Update()
    {
        if (!IsServer) return;

        HandleAI();
        HandleGravity();
        UpdateAnimations();
    }

    void HandleAI()
    {
        targetPlayer = FindNearestPlayer();

        if (targetPlayer != null)
        {
            float dist = Vector3.Distance(transform.position, targetPlayer.position);

            if (dist > followDistance)
            {
                MoveTowards(targetPlayer.position);
            }
            else
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f || Vector3.Distance(transform.position, wanderTarget) < 1f)
                {
                    PickWanderPoint(targetPlayer.position);
                    stateTimer = stateChangeInterval;
                }

                MoveTowards(wanderTarget);
            }
        }
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

            if (client.PlayerObject.GetComponent<PlayerController>() == null)
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

    void PickWanderPoint(Vector3 centerPos)
    {
        Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
        wanderTarget = centerPos + new Vector3(randomCircle.x, 0f, randomCircle.y);
    }

    void MoveTowards(Vector3 target)
    {
        if (groundCheck != null)
            isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        else
            isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.25f, groundMask);

        Vector3 dir = target - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);

        Vector3 move = dir.normalized;
        float speed = walkSpeed;
        Vector3 movementThisFrame = move * speed * Time.deltaTime;

        if (controller != null)
            controller.Move(movementThisFrame);
    }

    void HandleGravity()
    {
        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;
        if (controller != null)
            controller.Move(velocity * Time.deltaTime);
    }

    void UpdateAnimations()
    {
        if (anim == null) return;

        anim.SetBool("IsGrounded", isGrounded);

        Vector3 worldVelocity = Vector3.zero;
        if (Time.deltaTime > 0)
            worldVelocity = (transform.position - lastPosition) / Time.deltaTime;
        lastPosition = transform.position;

        Vector3 localVel = transform.InverseTransformDirection(worldVelocity);
        localVel.y = 0f;

        float targetForward = Mathf.Clamp(localVel.z / runSpeed, -1f, 1f);
        float targetRight = Mathf.Clamp(localVel.x / runSpeed, -1f, 1f);

        currentSpeed = Mathf.Lerp(currentSpeed, targetForward, Time.deltaTime * acceleration);
        currentDirection = Mathf.Lerp(currentDirection, targetRight, Time.deltaTime * acceleration);

        movementMagnitude = new Vector2(currentDirection, currentSpeed).magnitude;

        anim.SetFloat("Speed", currentSpeed);
        anim.SetBool("IsMoving", movementMagnitude > 0.1f);
        anim.SetFloat("Direction", currentDirection);
    }
}
