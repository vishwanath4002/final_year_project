using UnityEngine;
using StarterAssets;

public class PlayerHealthHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Health healthComponent;

    [Header("Animation Settings")]
    [SerializeField] private string hitTriggerName = "takehit";
    [SerializeField] private string dieTriggerName = "die";

    [Header("Debug Testing")]
    [SerializeField] private bool enableDebugDamage = true;
    [SerializeField] private KeyCode debugDamageKey = KeyCode.T;
    [SerializeField] private float debugDamageAmount = 20f;
    [SerializeField] private KeyCode debugKillKey = KeyCode.K;

    private Animator animator;
    private ThirdPersonController thirdPersonController;
    private ThirdPersonShooterController shooterController;
    private CharacterController characterController;

    private float lastHealth;
    private bool isDead = false;

    private void Start()
    {
        // Auto-find Health component if not assigned
        if (healthComponent == null)
            healthComponent = GetComponent<Health>();

        // Get player components
        animator = GetComponent<Animator>();
        thirdPersonController = GetComponent<ThirdPersonController>();
        shooterController = GetComponent<ThirdPersonShooterController>();
        characterController = GetComponent<CharacterController>();

        // Initialize health tracking
        if (healthComponent != null)
            lastHealth = healthComponent.GetCurrentHealth();
    }

    private void Update()
    {
        if (healthComponent == null) return;

        // DEBUG: Test damage
        if (enableDebugDamage)
        {
            if (Input.GetKeyDown(debugDamageKey))
            {
                Debug.Log($"[DEBUG] Applying {debugDamageAmount} damage to player");
                healthComponent.TakeDamage(debugDamageAmount);
            }

            if (Input.GetKeyDown(debugKillKey))
            {
                Debug.Log($"[DEBUG] Instantly killing player");
                healthComponent.TakeDamage(999f);
            }
        }

        if (isDead) return;

        float currentHealth = healthComponent.GetCurrentHealth();

        // Check if health decreased (player took damage)
        if (currentHealth < lastHealth)
        {
            OnPlayerTookDamage(lastHealth - currentHealth);
        }

        // Check if player died
        if (currentHealth <= 0 && !isDead)
        {
            OnPlayerDeath();
        }

        lastHealth = currentHealth;
    }

    private void OnPlayerTookDamage(float damageAmount)
    {
        Debug.Log($"Player took {damageAmount} damage! Current HP: {healthComponent.GetCurrentHealth()}");

        // Play hit animation
        if (animator != null)
        {
            animator.SetTrigger(hitTriggerName);
        }
    }

    private void OnPlayerDeath()
    {
        isDead = true;
        Debug.Log("Player died!");

        // Disable player controls
        if (thirdPersonController != null)
            thirdPersonController.enabled = false;

        if (shooterController != null)
            shooterController.enabled = false;

        // Disable character controller
        if (characterController != null)
            characterController.enabled = false;

        // Play death animation
        if (animator != null)
        {
            animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(2), 0f, Time.deltaTime * 100f));
            animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 0f, Time.deltaTime * 100f));
            animator.SetTrigger(dieTriggerName);
        }

        // Optional: Add respawn logic here or call game manager
    }

    // Public methods if needed
    public bool IsDead() => isDead;

    public float GetCurrentHealth()
    {
        return healthComponent != null ? healthComponent.GetCurrentHealth() : 0;
    }
}
