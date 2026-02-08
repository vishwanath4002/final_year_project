using System.Collections;
using UnityEngine;
using Cinemachine;
using StarterAssets;
using UnityEngine.Animations.Rigging;
using Unity.Netcode;

public class ThirdPersonShooterController : NetworkBehaviour
{
    [SerializeField] private Rig aimRig;
    [SerializeField] private CinemachineVirtualCamera aimVirtualCamera; // Reference to YOUR aim camera
    [SerializeField] private float normalSensitivity = 1f;
    [SerializeField] private float aimSensitivity = 0.5f;
    [SerializeField] private LayerMask aimColliderLayerMask = ~0; // What can be aimed at
    [SerializeField] private Transform debugTransform; // Optional: visual debug sphere

    [Header("Weapon Settings")]
    [SerializeField] private float fireRate = 0.1f;
    [SerializeField] private int magazineSize = 30;
    [SerializeField] private int currentAmmo;
    [SerializeField] private float reloadTime = 2f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxHitGreen;
    [SerializeField] private GameObject vfxHitRed;

    private ThirdPersonController thirdPersonController;
    private StarterAssetsInputs starterAssetsInputs;
    private Animator animator;

    // Synced state - only changes when value actually changes
    private bool _aiming = false;
    private bool _lastAiming = false;
    private Vector3 _aimTarget = Vector3.zero;
    private Vector3 _lastAimTarget = Vector3.zero;

    private float aimRigWeight = 0f;
    private float nextTimeToFire = 0f;
    private bool isReloading = false;

    private void Awake()
    {
        thirdPersonController = GetComponent<ThirdPersonController>();
        starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        animator = GetComponent<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Disable aim camera and debug sphere for non-owners
        if (!IsOwner)
        {
            if (aimVirtualCamera != null)
            {
                aimVirtualCamera.gameObject.SetActive(false);
                aimVirtualCamera.Priority = 0;
            }

            if (debugTransform != null)
            {
                debugTransform.gameObject.SetActive(false);
            }
        }
        else
        {
            // Owner: aim camera starts disabled, enabled when aiming
            if (aimVirtualCamera != null)
            {
                aimVirtualCamera.gameObject.SetActive(false);
            }

            if (debugTransform != null)
            {
                debugTransform.gameObject.SetActive(true);
            }
        }
    }

    private void Start()
    {
        currentAmmo = magazineSize;
    }

    private void Update()
    {
        // Apply aim rig for ALL players (uses synced _aiming value)
        if (aimRig != null)
        {
            aimRigWeight = Mathf.Lerp(aimRigWeight, _aiming ? 1f : 0f, 10f * Time.deltaTime);
            aimRig.weight = aimRigWeight;
        }

        // Apply animator layers for ALL players
        if (animator != null)
        {
            if (_aiming)
            {
                animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 1f, Time.deltaTime * 10f));
                animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 0f, Time.deltaTime * 10f));
            }
            else
            {
                animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 0f, Time.deltaTime * 10f));
                animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 1f, Time.deltaTime * 10f));
            }
        }

        // Remote players: use synced aim target for rotation
        if (!IsOwner && _aiming)
        {
            Vector3 worldAimTarget = _aimTarget;
            worldAimTarget.y = transform.position.y;
            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;

            if (aimDirection != Vector3.zero)
            {
                transform.forward = Vector3.Lerp(transform.forward, aimDirection, Time.deltaTime * 20f);
            }
        }

        // Only owner processes input below this point
        if (!IsOwner) return;

        // Calculate aim target from screen center raycast
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);

        if (Physics.Raycast(ray, out RaycastHit hit, 999f, aimColliderLayerMask))
        {
            _aimTarget = hit.point;

            // Update debug sphere position
            if (debugTransform != null && debugTransform.gameObject.activeSelf)
            {
                debugTransform.position = hit.point;
            }
        }
        else
        {
            // No hit, aim far away
            _aimTarget = ray.GetPoint(100f);
        }

        // Sync aim target ONLY when it changes significantly (optimization)
        if (Vector3.Distance(_aimTarget, _lastAimTarget) > 0.01f)
        {
            OnAimTargetChangedServerRpc(_aimTarget);
            _lastAimTarget = _aimTarget;
        }

        // Handle reload input
        if (starterAssetsInputs != null && starterAssetsInputs.reload && !isReloading && currentAmmo < magazineSize)
        {
            StartCoroutine(Reload());
        }

        // Aiming logic
        if (starterAssetsInputs != null && starterAssetsInputs.aim)
        {
            starterAssetsInputs.sprint = false;

            // Enable aim camera
            if (aimVirtualCamera != null)
            {
                aimVirtualCamera.gameObject.SetActive(true);
            }

            // Change sensitivity
            if (thirdPersonController != null)
            {
                thirdPersonController.SetSensitivity(aimSensitivity);
                thirdPersonController.SetRotateOnMove(false);
            }

            // Rotate player to face aim target
            Vector3 worldAimTarget = _aimTarget;
            worldAimTarget.y = transform.position.y;
            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;

            if (aimDirection != Vector3.zero)
            {
                transform.forward = Vector3.Lerp(transform.forward, aimDirection, Time.deltaTime * 20f);
            }

            _aiming = true;
        }
        else if (starterAssetsInputs != null)
        {
            // Stop aiming
            if (aimVirtualCamera != null)
            {
                aimVirtualCamera.gameObject.SetActive(false);
            }

            if (thirdPersonController != null)
            {
                thirdPersonController.SetSensitivity(normalSensitivity);
                thirdPersonController.SetRotateOnMove(true);
            }

            _aiming = false;
            starterAssetsInputs.shoot = false;
        }

        // Sync aiming state ONLY when it changes
        if (_aiming != _lastAiming)
        {
            OnAimingChangedServerRpc(_aiming);
            _lastAiming = _aiming;
        }

        // Shooting
        if (starterAssetsInputs != null && _aiming && !isReloading)
        {
            if (starterAssetsInputs.shoot && Time.time >= nextTimeToFire)
            {
                if (currentAmmo > 0)
                {
                    Shoot();
                    nextTimeToFire = Time.time + fireRate;
                }
            }
        }
    }

    // Sync aim target
    [ServerRpc]
    private void OnAimTargetChangedServerRpc(Vector3 value)
    {
        _aimTarget = value;
        OnAimTargetChangedClientRpc(value);
    }

    [ClientRpc]
    private void OnAimTargetChangedClientRpc(Vector3 value)
    {
        if (!IsOwner)
        {
            _aimTarget = value;
        }
    }

    // Sync aiming state
    [ServerRpc]
    private void OnAimingChangedServerRpc(bool value)
    {
        _aiming = value;
        OnAimingChangedClientRpc(value);
    }

    [ClientRpc]
    private void OnAimingChangedClientRpc(bool value)
    {
        if (!IsOwner)
        {
            _aiming = value;
        }
    }

    private void Shoot()
    {
        if (isReloading) return;

        currentAmmo--;

        // Use the aim target we already calculated
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);

        if (Physics.Raycast(ray, out RaycastHit hit, 999f, aimColliderLayerMask))
        {
            bool isTarget = hit.transform.GetComponent<BulletTarget>() != null;
            ShootServerRpc(hit.point, isTarget);
        }
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 hitPoint, bool isTarget)
    {
        ShootClientRpc(hitPoint, isTarget);
    }

    [ClientRpc]
    private void ShootClientRpc(Vector3 hitPoint, bool isTarget)
    {
        if (animator != null)
        {
            animator.SetBool("shooting", true);
        }

        GameObject vfx = isTarget ? vfxHitGreen : vfxHitRed;
        if (vfx != null)
        {
            Instantiate(vfx, hitPoint, Quaternion.identity);
        }

        StartCoroutine(StopShootingAnimation());
    }

    private IEnumerator StopShootingAnimation()
    {
        yield return new WaitForSeconds(0.1f);
        if (animator != null)
        {
            animator.SetBool("shooting", false);
        }
    }

    private IEnumerator Reload()
    {
        isReloading = true;

        if (animator != null)
        {
            animator.SetTrigger("reload");
            animator.SetBool("shooting", false);
        }

        if (starterAssetsInputs != null)
        {
            starterAssetsInputs.shoot = false;
        }

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = magazineSize;
        isReloading = false;

        if (animator != null)
        {
            animator.ResetTrigger("reload");
        }

        if (starterAssetsInputs != null)
        {
            starterAssetsInputs.reload = false;
        }
    }
}
