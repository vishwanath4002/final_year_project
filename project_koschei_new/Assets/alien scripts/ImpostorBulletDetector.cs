using UnityEngine;

/// <summary>
/// Attach to the SAME child GameObject that has BulletTarget (the hitbox).
/// Detects bullet collisions and notifies ImpostorFleeOnHit on the root.
/// BulletTarget is left completely untouched.
///
/// Setup:
///   - Tag your bullet prefab with "Bullet" in Unity.
///   - Make sure the hitbox has a Collider with Is Trigger checked
///     (or use OnCollisionEnter if it's a non-trigger collider).
/// </summary>
public class ImpostorBulletDetector : MonoBehaviour
{
    // Trigger-based detection (use if the hitbox collider has "Is Trigger" ON)
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Bullet")) return;
        NotifyRoot();
    }

    // Collision-based detection (use if the hitbox collider has "Is Trigger" OFF)
    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Bullet")) return;
        NotifyRoot();
    }

    private void NotifyRoot()
    {
        ImpostorFleeOnHit flee = GetComponentInParent<ImpostorFleeOnHit>();
        if (flee != null)
            flee.OnShot();
        else
            Debug.LogWarning("[ImpostorBulletDetector] Could not find ImpostorFleeOnHit on parent!");
    }
}
