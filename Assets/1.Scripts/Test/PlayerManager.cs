using StarterAssets;
using Cinemachine;
using UnityEngine.Animations.Rigging;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    private StarterAssetsInputs input;
    private ThirdPersonController controller;
    private Animator anim;
    private WeaponController weaponController;

    [Header("Aim")]
    [SerializeField] private CinemachineVirtualCamera aimCam;
    [SerializeField] private GameObject aimImage;
    [SerializeField] private GameObject aimObj;
    [SerializeField] private float aimObjDis = 10f;
    [SerializeField] private LayerMask targetLayer;

    [Header("IK")]
    [SerializeField] private Rig handRig;
    [SerializeField] private Rig aimRig;

    [Header("Weapon Sound Effect")]
    [SerializeField] private AudioClip shootingSound;
    [SerializeField] private AudioClip[] reloadSound;
    private AudioSource weaponSound;

    private Enemy enemy;

    void Start()
    {
        input = GetComponent<StarterAssetsInputs>();
        controller = GetComponent<ThirdPersonController>();
        anim = GetComponent<Animator>();
        weaponSound = GetComponent<AudioSource>();
        weaponController = GetComponentInChildren<WeaponController>();
    }

    void Update()
    {
        if (input.aim)
        {
            aimCam.gameObject.SetActive(true);
            aimImage.SetActive(true);
        }
        else
        {
            aimCam.gameObject.SetActive(false);
            aimImage.SetActive(false);
        }

        AimCheck();
    }

    private void AimCheck()
    {
        if (input.reload)
        {
            input.reload = false;

            if (controller.isReload)
            {
                return;
            }

            AimControll(false);
            SetRigWeight(0);
            anim.SetLayerWeight(1, 1);
            anim.SetTrigger("Reload");
            controller.isReload = true;

            if (weaponController != null)
            {
                weaponController.StartReload();
            }
        }

        if (controller.isReload)
        {
            return;
        }

        if (input.aim)
        {
            AimControll(true);

            anim.SetLayerWeight(1, 1);

            Vector3 targetPosition = Vector3.zero;
            Transform camTransform = Camera.main.transform;
            RaycastHit hit;

            if (Physics.Raycast(camTransform.position, camTransform.forward, out hit, Mathf.Infinity, targetLayer))
            {
                targetPosition = hit.point;
                aimObj.transform.position = hit.point;

                enemy = hit.collider.GetComponentInParent<Enemy>();
            }
            else
            {
                targetPosition = camTransform.position + camTransform.forward * aimObjDis;
                aimObj.transform.position = camTransform.position + camTransform.forward * aimObjDis;
                enemy = null;
            }

            Vector3 targetAim = targetPosition;
            targetAim.y = transform.position.y;
            Vector3 aimDir = (targetAim - transform.position).normalized;

            transform.forward = Vector3.Lerp(transform.forward, aimDir, Time.deltaTime * 50f);

            SetRigWeight(1);

            if (input.shoot)
            {
                anim.SetBool("Shoot", true);

                if (weaponController != null)
                {
                    weaponController.TryShoot(targetPosition);
                }
            }
            else
            {
                anim.SetBool("Shoot", false);
            }
        }
        else
        {
            AimControll(false);
            SetRigWeight(0);
            anim.SetLayerWeight(1, 0);
            anim.SetBool("Shoot", false);
        }
    }

    private void AimControll(bool isCheck)
    {
        aimCam.gameObject.SetActive(isCheck);
        aimImage.SetActive(isCheck);
        controller.isAimMove = isCheck;
    }

    public void Reload()
    {
        controller.isReload = false;
        SetRigWeight(1);
        anim.SetLayerWeight(1, 0);

        if (weaponController != null)
        {
            weaponController.CompleteReload();
        }

        PlayWeaponSound(reloadSound[2]);
    }

    private void SetRigWeight(float weight)
    {
        aimRig.weight = weight;
        handRig.weight = weight;
    }

    public void ReloadWeaponClip()
    {
        if (weaponController != null)
        {
            weaponController.OnReloadMagOut();
        }

        PlayWeaponSound(reloadSound[0]);
    }

    public void ReloadInsertClip()
    {
        PlayWeaponSound(reloadSound[1]);
    }

    private void PlayWeaponSound(AudioClip sound)
    {
        if (weaponSound == null || sound == null) return;

        weaponSound.clip = sound;
        weaponSound.Play();
    }

    public void ForceStopAim()
    {
        if (aimCam != null)
            aimCam.gameObject.SetActive(false);

        if (aimImage != null)
            aimImage.SetActive(false);

        if (controller != null)
            controller.isAimMove = false;

        if (anim != null)
        {
            anim.SetLayerWeight(1, 0);
            anim.SetBool("Shoot", false);
        }

        SetRigWeight(0);
    }
}