using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

public class Health : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float defence = 15f;
    [SerializeField] private float destroyAfterSeconds = 0f; // 0 = don't destroy

    [Header("UI")]
    [SerializeField] private HealthBar healthBar;

    [Header("Debug")]
    [SerializeField] private bool enableDebugDamage = false;
    [SerializeField] private float debugDamageAmount = 25f;
    [SerializeField] private KeyCode debugDamageKey = KeyCode.K;

    private NetworkVariable<float> currentHealth = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> isDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // =========================
    // UNITY EVENTS
    // =========================

    private void Update()
    {
        if (!enableDebugDamage) return;

        if (Input.GetKeyDown(debugDamageKey))
        {
            if (IsServer)
            {
                Debug.Log($"[DEBUG] Applying {debugDamageAmount} damage to {gameObject.name}");
                TakeDamage(debugDamageAmount);
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            isDead.Value = false;
        }

        currentHealth.OnValueChanged += OnHealthChanged;
        isDead.OnValueChanged += OnDeathStateChanged;

        if (healthBar != null)
        {
            healthBar.SetMaxHealth(maxHealth);
            healthBar.SetHealth(currentHealth.Value);
        }

        Debug.Log($"[Health] {gameObject.name} spawned - Health: {currentHealth.Value}, IsServer: {IsServer}");
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        currentHealth.OnValueChanged -= OnHealthChanged;
        isDead.OnValueChanged -= OnDeathStateChanged;
    }

    private void OnDeathStateChanged(bool previousValue, bool newValue)
    {
        if (newValue && !previousValue)
        {
            Debug.Log($"[Health] {gameObject.name} death state changed on client");
            HandleDeathOnClient();
        }
    }

    // =========================
    // DAMAGE
    // =========================

    public void TakeDamage(float damage)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"[Health] TakeDamage called on client - ignored");
            return;
        }

        if (isDead.Value) return;

        if (damage > 0)
            damage = Mathf.Max(damage - defence, 0f);

        currentHealth.Value -= damage;

        Debug.Log($"[Health] {gameObject.name} took {damage} damage. Remaining HP: {currentHealth.Value}");

        if (currentHealth.Value <= 0f)
        {
            currentHealth.Value = 0f;
            Die();
        }
    }

    private void OnHealthChanged(float oldValue, float newValue)
    {
        Debug.Log($"[Health] {gameObject.name} health changed: {newValue}");

        if (healthBar != null)
            healthBar.SetHealth(newValue);

        HealthMaterialChanger changer = GetComponent<HealthMaterialChanger>();
        if (changer != null)
            changer.UpdateMaterial();
    }

    public float GetCurrentHealth()  => currentHealth.Value;
    public float GetMaxHealth()      => maxHealth;
    public float GetHealthPercent()  => maxHealth <= 0f ? 0f : currentHealth.Value / maxHealth;
    public bool  IsDead()            => isDead.Value;

    public void ResetHealth()
    {
        if (!IsServer) return;
        currentHealth.Value = maxHealth;
        isDead.Value = false;
    }

    private void Die()
    {
        if (!IsServer) return;
        if (isDead.Value) return;

        isDead.Value = true;
        Debug.Log($"[Health] {gameObject.name} died on server!");
        // HandleDeathOnClient() fires automatically via OnDeathStateChanged on all clients
    }

    private void HandleDeathOnClient()
    {
        Debug.Log($"[Health] {gameObject.name} handling death on client");

        // Delegate player death to PlayerHealthHandler
        PlayerHealthHandler playerHandler = GetComponent<PlayerHealthHandler>();
        if (playerHandler != null)
        {
            Debug.Log("[Health] Player detected - delegating death handling to PlayerHealthHandler");
            return;
        }

        // Below runs for AI/NPCs only

        // 1. Stop NavMeshAgent — ONLY if the agent is actually on the NavMesh.
        //    On clients, agents are not placed on the NavMesh, so we must guard
        //    against calling isStopped on an inactive agent (causes "Stop" error).
        NavMeshAgent agent;
        if (TryGetComponent(out agent) && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        // 2. Disable AILocomotion (if exists)
        AILocomotion ai;
        if (TryGetComponent(out ai))
            ai.enabled = false;

        // 3. Stop Rigidbody (if exists)
        Rigidbody rb;
        if (TryGetComponent(out rb))
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // 4. Disable all colliders
        foreach (Collider col in GetComponentsInChildren<Collider>())
            col.enabled = false;

        // 5. Trigger death animation
        Animator animator;
        if (TryGetComponent(out animator))
        {
            foreach (var param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Trigger && param.name == "die")
                {
                    animator.SetTrigger("die");
                    break;
                }
            }
        }

        // 6. Destroy on server only
        if (IsServer && destroyAfterSeconds > 0f)
            Destroy(gameObject, destroyAfterSeconds);
    }
}