using UnityEngine;
using UnityEngine.AI;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float defence = 15f;
    [SerializeField] private float destroyAfterSeconds = 0f; // 0 = don't destroy

    private float currentHealth;
    private bool isDead = false;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        if (isDead) return;

        damage = Mathf.Max(damage - defence, 0f);
        currentHealth -= damage;

        Debug.Log($"{gameObject.name} took {damage} damage. Remaining HP: {currentHealth}");

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        Debug.Log($"{gameObject.name} died!");

        // =============================
        // 1️⃣ Stop NavMeshAgent (if exists)
        // =============================
        NavMeshAgent agent;
        if (TryGetComponent(out agent))
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // =============================
        // 2️⃣ Disable AILocomotion (if exists)
        // =============================
        AILocomotion ai;
        if (TryGetComponent(out ai))
        {
            ai.enabled = false;
        }

        // =============================
        // 3️⃣ Stop Rigidbody (if exists)
        // =============================
        Rigidbody rb;
        if (TryGetComponent(out rb))
        {
            rb.velocity = Vector3.zero;     // use velocity (safe for all versions)
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // =============================
        // 4️⃣ Disable ALL Colliders (safe for AI & non-AI)
        // =============================
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }

        // =============================
        // 5️⃣ Trigger death animation safely
        // =============================
        Animator animator;
        if (TryGetComponent(out animator))
        {
            bool hasDieTrigger = false;

            foreach (var param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Trigger &&
                    param.name == "die")
                {
                    hasDieTrigger = true;
                    break;
                }
            }

            if (hasDieTrigger)
            {
                animator.SetTrigger("die");
            }
        }

        // =============================
        // 6️⃣ Optional destroy
        // =============================
        if (destroyAfterSeconds > 0f)
        {
            Destroy(gameObject, destroyAfterSeconds);
        }
    }
}
