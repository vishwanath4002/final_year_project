using System;
using UnityEngine;

/// <summary>
/// Simple health component for the scientist NPC (NPC 2nd).
/// ScavengerRaidTask auto-adds this if not already present.
/// Hook TakeDamage() into AlienMovement.OnAttackHit() via Animation Events.
/// </summary>
public class ScientistHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth { get; private set; }

    /// <summary>Fired when health reaches 0.</summary>
    public event Action OnDeath;

    /// <summary>Fired on any damage — passes normalised health (0–1).</summary>
    public event Action<float> OnHealthChanged;

    void Awake() => currentHealth = maxHealth;

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(1f);
    }

    public void TakeDamage(float amount)
    {
        if (currentHealth <= 0f) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        OnHealthChanged?.Invoke(currentHealth / maxHealth);

        Debug.Log($"[ScientistHealth] Scientist took {amount} dmg — HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f)
            OnDeath?.Invoke();
    }
}
