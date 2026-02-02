using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using StarterAssets;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;

public class ThirdPersonShooterController : MonoBehaviour
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

    private bool isReloading = false;

    private ThirdPersonController thirdPersonController;
    private StarterAssetsInputs starterAssetsInputs;
    private Animator animator;
    private float aimRigWeight = 0f;
    private float nextTimeToFire = 0f;
    private int bulletsFired = 0;

    private void Awake()
    {
        thirdPersonController = GetComponent<ThirdPersonController>();
        starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        animator = GetComponent<Animator>();
    }

    private void Start()
    {
        currentAmmo = magazineSize;
    }

    private void Update()
    {
        aimRig.weight = Mathf.Lerp(aimRig.weight, aimRigWeight, Time.deltaTime * 20f);

        Vector3 mouseWorldPosition = Vector3.zero;
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenterPoint);
        Transform hitTransform = null;
        RaycastHit raycastHit;

        if (Physics.Raycast(ray, out raycastHit, 999f, aimColliderLayerMask))
        {
            debugTransform.position = raycastHit.point;
            mouseWorldPosition = raycastHit.point;
            hitTransform = raycastHit.transform;
        }

        if (starterAssetsInputs.reload && currentAmmo < magazineSize && !isReloading)
        {
            StartCoroutine(Reload());
        }

        if (starterAssetsInputs.aim)
        {
            starterAssetsInputs.sprint = false;
            aimVirtualCamera.gameObject.SetActive(true);
            thirdPersonController.SetSensitivity(aimSensitivity);
            thirdPersonController.SetRotateOnMove(false);
            animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 1f, Time.deltaTime * 10f));
            animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 0f, Time.deltaTime * 10f));

            Vector3 worldAimTarget = mouseWorldPosition;
            worldAimTarget.y = transform.position.y;
            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;

            transform.forward = Vector3.Lerp(transform.forward, aimDirection, Time.deltaTime * 20f);
            aimRigWeight = 1f;
        }
        else
        {
            aimVirtualCamera.gameObject.SetActive(false);
            thirdPersonController.SetSensitivity(normalSensitivity);
            thirdPersonController.SetRotateOnMove(true);
            animator.SetLayerWeight(1, Mathf.Lerp(animator.GetLayerWeight(1), 0f, Time.deltaTime * 10f));
            animator.SetLayerWeight(2, Mathf.Lerp(animator.GetLayerWeight(2), 1f, Time.deltaTime * 10f));
            aimRigWeight = 0f;
            animator.SetBool("shooting", false);
            starterAssetsInputs.shoot = false;
        }

        if (starterAssetsInputs.aim && !isReloading)
        {
            starterAssetsInputs.sprint = false;
            if (starterAssetsInputs.shoot && Time.time >= nextTimeToFire)
            {
                if (currentAmmo > 0)
                {
                    Shoot(hitTransform, raycastHit);
                    nextTimeToFire = Time.time + fireRate;
                }
                else
                {
                    animator.SetBool("shooting", false);
                    Debug.Log("Out of ammo! Reload needed.");
                }
            }
            else if (!starterAssetsInputs.shoot)
            {
                animator.SetBool("shooting", false);
            }
        }
        else
        {
            starterAssetsInputs.shoot = false;
            animator.SetBool("shooting", false);
        }
    }

    private void Shoot(Transform hitTransform, RaycastHit raycastHit)
    {
        if (isReloading) return;

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
        animator.SetTrigger("reload");
        Debug.Log("Reloading...");

        animator.SetBool("shooting", false);
        starterAssetsInputs.shoot = false;

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = magazineSize;
        isReloading = false;

        Debug.Log("Reload complete!");

        animator.ResetTrigger("reload");
        starterAssetsInputs.reload = false;
    }
}
