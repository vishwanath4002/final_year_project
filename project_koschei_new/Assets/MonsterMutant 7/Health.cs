using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float Defence = 15f;
    private float currentHealth;
    private bool isDead = false;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        if (Defence > damage)
        {
            damage = 0;
        }
        else
        {
            damage -= Defence;
        }
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

        // 1 Stop NavMesh movement
        UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // 2️ Disable AI locomotion
        Ailocomotion aiLocomotion = GetComponent<Ailocomotion>();
        if (aiLocomotion != null)
        {
            aiLocomotion.enabled = false;
        }

        // 3️ Stop Rigidbody motion
        // Rigidbody rb = GetComponent<Rigidbody>();
        // if (rb != null)
        // {
        //     rb.velocity = Vector3.zero;
        //     rb.angularVelocity = Vector3.zero;
        //     rb.isKinematic = true;
        // }

        // 4️ Trigger death animation
        Animator animator = GetComponent<Animator>();
        if (animator != null)
        {
            animator.SetTrigger("die");
        }

        // 35 Disable Capsule Collider (prevents blocking & re-hits)
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            capsule.enabled = false;
        }

        // 6️ Optional: destroy after delay
        // Destroy(gameObject, 5f);
    }
}
