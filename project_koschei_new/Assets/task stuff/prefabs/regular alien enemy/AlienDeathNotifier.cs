using System;
using UnityEngine;

/// <summary>
/// Sits on the alien prefab.
/// ScavengerRaidTask subscribes to OnDied at spawn time.
/// AlienMovement calls TriggerDeath() when the alien dies.
/// </summary>
public class AlienDeathNotifier : MonoBehaviour
{
    public event Action OnDied;

    /// <summary>
    /// Called by AlienMovement.Die() to fire the death event.
    /// </summary>
    public void TriggerDeath()
    {
        OnDied?.Invoke();
    }
}