using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using StarterAssets;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using Unity.Netcode;

public class ThirdPersonShooterController : NetworkBehaviour
{
    [SerializeField] private Rig aimRig;
    [SerializeField] private CinemachineVirtualCamera aimVirtualCamera;
    [SerializeField] private float normalSensitivity = 1f;
    [SerializeField] private float aimSensitivity = 0.5f;
    [SerializeField] private LayerMask aimColliderLayerMask;
    [SerializeField] private Transform debugTransform;
    [SerializeField] private GameObject vfxHitGreen;
    [SerializeField] private GameObject vfxHitRed;

    [Header("Weapon Settings")]
    [SerializeField] private float fireRate = 0.1f;
    [SerializeField] private int magazineSize = 30;
    [SerializeField] private int currentAmmo;
    [SerializeField] private float reloadTime = 2f;

    [Header("Interaction Settings")]
    [SerializeField] private float interactRange = 5f;
    [SerializeField] private LayerMask interactLayerMask;

    private bool isReloading = false;

    private ThirdPersonController thirdPersonController;
    private StarterAssetsInputs starterAssetsInputs;
    private PlayerInventory playerInventory;
    private Animator animator;
    private float aimRigWeight = 0f;
    private float nextTimeToFire = 0f;
    private int bulletsFired = 0;

    private Transform currentHitTransform = null;
    private RaycastHit currentRaycastHit;

    private void Awake()
    {
        thirdPersonController = GetComponent<ThirdPersonController>();
        starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        animator = GetComponent<Animator>();
        playerInventory = GetComponent<PlayerInventory>();
    }

    private void Start()
    {
        currentAmmo = magazineSize;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            // Disable aim camera for non-owners
            if (aimVirtualCamera != null)
            {
                aimVirtualCamera.Priority = 0;
                aimVirtualCamera.gameObject.SetActive(false);
            }
        }
    }


    private void Update()
    {
        // CRITICAL: Only run for the local player who owns this object
        if (!IsOwner) return;

        aimRig.weight = Mathf.Lerp(aimRig.weight, aimRigWeight, Time.deltaTime * 20f);

        Vector3 mouseWorldPosition = Vector3.zero;
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);
        currentHitTransform = null;

        if (Physics.Raycast(ray, out currentRaycastHit, 999f, aimColliderLayerMask))
        {
            if (debugTransform != null)
                debugTransform.position = currentRaycastHit.point;
            mouseWorldPosition = currentRaycastHit.point;
            currentHitTransform = currentRaycastHit.transform;
        }

        if (playerInventory != null)
        {
            HandlePickup();
            HandleDrop();
        }

        if (starterAssetsInputs != null && starterAssetsInputs.reload && currentAmmo < magazineSize && !isReloading)
        {
            StartCoroutine(Reload());
        }

        if (starterAssetsInputs != null && starterAssetsInputs.aim)
        {
            starterAssetsInputs.sprint = false;
            if (aimVirtualCamera != null)
                aimVirtualCamera.gameObject.SetActive(true);
            if (thirdPersonController != null)
            {
                thirdPersonController.SetSensitivity(aimSensitivity);
                thirdPersonController.SetRotateOnMove(false);
            }
            if (animator != null)
            {
                animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 1f, Time.deltaTime * 10f));
                animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 0f, Time.deltaTime * 10f));
            }

            Vector3 worldAimTarget = mouseWorldPosition;
            worldAimTarget.y = transform.position.y;
            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;

            transform.forward = Vector3.Lerp(transform.forward, aimDirection, Time.deltaTime * 20f);
            aimRigWeight = 1f;
        }
        else if (starterAssetsInputs != null)
        {
            if (aimVirtualCamera != null)
                aimVirtualCamera.gameObject.SetActive(false);
            if (thirdPersonController != null)
            {
                thirdPersonController.SetSensitivity(normalSensitivity);
                thirdPersonController.SetRotateOnMove(true);
            }
            if (animator != null)
            {
                animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 0f, Time.deltaTime * 10f));
                animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 1f, Time.deltaTime * 10f));
                animator.SetBool("shooting", false);
            }
            aimRigWeight = 0f;
            starterAssetsInputs.shoot = false;
        }

        if (starterAssetsInputs != null && starterAssetsInputs.aim && !isReloading)
        {
            starterAssetsInputs.sprint = false;
            if (starterAssetsInputs.shoot && Time.time >= nextTimeToFire)
            {
                if (currentAmmo > 0)
                {
                    Shoot(currentHitTransform, currentRaycastHit);
                    nextTimeToFire = Time.time + fireRate;
                }
                else
                {
                    if (animator != null)
                        animator.SetBool("shooting", false);
                    Debug.Log("Out of ammo! Reload needed.");
                }
            }
            else if (!starterAssetsInputs.shoot)
            {
                if (animator != null)
                    animator.SetBool("shooting", false);
            }
        }
        else if (starterAssetsInputs != null)
        {
            starterAssetsInputs.shoot = false;
            if (animator != null)
                animator.SetBool("shooting", false);
        }
    }

    private void HandlePickup()
    {
        if (starterAssetsInputs == null) return;

        if (starterAssetsInputs.interact)
        {
            starterAssetsInputs.interact = false;

            if (playerInventory == null) return;

            if (!playerInventory.IsHoldingItem())
            {
                if (currentHitTransform != null && Vector3.Distance(transform.position, currentRaycastHit.point) <= interactRange)
                {
                    PickupObject pickup = currentHitTransform.GetComponent<PickupObject>();
                    if (pickup != null)
                    {
                        pickup.TryPickup(gameObject);
                        Debug.Log($"Picked up: {currentHitTransform.name}");
                    }
                }
            }
        }
    }

    private void HandleDrop()
    {
        if (starterAssetsInputs == null) return;

        if (starterAssetsInputs.drop)
        {
            starterAssetsInputs.drop = false;

            if (playerInventory == null) return;

            if (playerInventory.IsHoldingItem())
            {
                Vector3 dropPos = transform.position + Vector3.up * 1f + transform.forward * 2f;
                playerInventory.DropItemServerRpc(dropPos);
                Debug.Log("Dropped item");
            }
        }
    }

    private void Shoot(Transform hitTransform, RaycastHit raycastHit)
    {
        if (isReloading) return;

        if (animator != null)
            animator.SetBool("shooting", true);

        currentAmmo--;
        bulletsFired++;

        Debug.Log($"Bullet #{bulletsFired} fired! Ammo remaining: {currentAmmo}/{magazineSize}");

        if (hitTransform != null)
        {
            if (hitTransform.GetComponent<BulletTarget>() != null)
            {
                Instantiate(vfxHitGreen, raycastHit.point, Quaternion.identity);
            }
            else
            {
                Instantiate(vfxHitRed, raycastHit.point, Quaternion.identity);
            }
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

        Debug.Log("Reloading...");

        if (starterAssetsInputs != null)
            starterAssetsInputs.shoot = false;

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = magazineSize;
        isReloading = false;

        Debug.Log("Reload complete!");

        if (animator != null)
            animator.ResetTrigger("reload");

        if (starterAssetsInputs != null)
            starterAssetsInputs.reload = false;
    }
}
