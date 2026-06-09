using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    [Header("Bullet")]
    [SerializeField]
    private Transform bulletpoint;
    [SerializeField]
    private GameObject bulletObj;
    [SerializeField]
    private float maxShootDelay = 0.2f;
    [SerializeField]
    private float currentShootDelay = 0.2f;
    [SerializeField]
    private Text bulletText;
    private int maxBullet = 30;
    private int currentBullet = 0;
    [SerializeField] private GameObject muzzleFlashPrefab; // Project의 MuzzleFlash 프리팹
    private GameObject muzzleFlashInstance;
    private FollowTransform muzzleFollow;
    private ParticleSystem[] muzzleParticles;

    [Header("Weapon FX")]
    [SerializeField]
    private GameObject weaponFlashFX;
    [SerializeField]
    private Transform bulletCasePoint;
    [SerializeField]
    private GameObject bulletCaseFX;
    [SerializeField]
    private Transform weaponClipPoint;
    [SerializeField]
    private GameObject weaponClipFX;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        instance = this;

        currentShootDelay = 0;

        InitBullet();

        // --- MuzzleFlash Init (1회) ---
        if (muzzleFlashInstance != null)
            return; // 이미 만들었으면 또 만들지 않음

        if (muzzleFlashPrefab == null)
        {
            Debug.LogError("[GameManager] muzzleFlashPrefab이 할당되지 않았습니다.", this);
            return;
        }

        muzzleFlashInstance = Instantiate(muzzleFlashPrefab);
        muzzleFollow = muzzleFlashInstance.GetComponent<FollowTransform>();
        if (muzzleFollow == null) muzzleFollow = muzzleFlashInstance.AddComponent<FollowTransform>();

        muzzleFollow.Bind(bulletpoint, Vector3.zero, true);

        muzzleParticles = muzzleFlashInstance.GetComponentsInChildren<ParticleSystem>(true);
        StopMuzzleFlash();
    }

    // Update is called once per frame
    void Update()
    {
        bulletText.text = currentBullet + " / " + maxBullet;
    }

    public void Shooting(Vector3 targetPosition, Enemy enemy, AudioSource weaponSound, AudioClip shootingSound)
    {
        currentShootDelay += Time.deltaTime;

        if (currentShootDelay < maxShootDelay || currentBullet <= 0)
            return;

        currentBullet -= 1;
        currentShootDelay = 0;

        weaponSound.clip = shootingSound;
        weaponSound.Play();

        PlayMuzzleFlash();
        Vector3 aim = (targetPosition - bulletpoint.position).normalized;

        // Instantiate(weaponFlashFX, bulletpoint);
        GameObject flashFX = PoolManager.instance.ActivateObj(1);
        SetObjPosition(flashFX, bulletpoint);
        flashFX.transform.rotation = Quaternion.LookRotation(aim, Vector3.up);

        // Instantiate(bulletCaseFX, bulletCasePoint);
        GameObject caseFX = PoolManager.instance.ActivateObj(2);
        SetObjPosition(caseFX, bulletCasePoint);

        // Instantiate(bulletObj, bulletpoint.position, Quaternion.LookRotation(aim,Vector3.up));
        
        GameObject prefabToSpawn = PoolManager.instance.ActivateObj(0);
        SetObjPosition(prefabToSpawn, bulletpoint);
        prefabToSpawn.transform.rotation = Quaternion.LookRotation(aim, Vector3.up);
        
        // Raycast
        /*
        if(enemy != null && enemy.enemyCurrentHP > 0)
        {
            enemy.enemyCurrentHP -= 1;
            Debug.Log("enemy HP : " + enemy.enemyCurrentHP);
        }
        */

    }

    public void ReloadClip()
    {
        // Instantiate(weaponClipFX, weaponClipPoint);
        GameObject clipFX = PoolManager.instance.ActivateObj(3);
        SetObjPosition(clipFX, weaponClipPoint);

        InitBullet();
    }

    private void InitBullet()
    {
        currentBullet = maxBullet;
    }

    private void SetObjPosition(GameObject obj, Transform targetTransform)
    {
        obj.transform.position = targetTransform.position;
    }

    private void PlayMuzzleFlash()
    {
        if (muzzleParticles == null || muzzleFlashInstance == null) return;

        // 혹시 꺼져있으면 다시 켜기
        if (!muzzleFlashInstance.activeInHierarchy)
            muzzleFlashInstance.SetActive(true);

        foreach (var ps in muzzleParticles)
        {
            if (ps == null) continue;

            // ps가 붙은 오브젝트가 꺼져있을 수도 있어서 이것도 켜기
            if (!ps.gameObject.activeInHierarchy)
                ps.gameObject.SetActive(true);

            ps.Clear(true);
            ps.Play(true);
        }
    }

    private void StopMuzzleFlash()
    {
        if (muzzleParticles == null) return;

        foreach (var ps in muzzleParticles)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

}
