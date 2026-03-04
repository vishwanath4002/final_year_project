using System;
using UnityEngine;

public class AlienDeathNotifier : MonoBehaviour
{
    public event Action OnDied;
    bool notified = false;

    public void Die()
    {
        if (notified) return;
        notified = true;
        OnDied?.Invoke();
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (!notified)
        {
            notified = true;
            OnDied?.Invoke();
        }
    }
}
