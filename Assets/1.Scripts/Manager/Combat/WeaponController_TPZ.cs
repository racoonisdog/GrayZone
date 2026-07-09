using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class WeaponController_TPZ : MonoBehaviour
{
    [Header("Bullet")]
    [SerializeField] private int currentBullet = 30;
    [SerializeField] private int maxBullet = 30;
    [SerializeField] private float shootDelay = 0.12f;
    [SerializeField] private float reloadTime = 1.5f;

    [Header("Spawn Points")]
    [SerializeField] private Transform firePos;
    [SerializeField] private Transform shellPos;
    [SerializeField] private Transform clipPos;
    [SerializeField] private Transform muzzleFlashPos;

    [Header("Pool Index")]
    [SerializeField] private int bulletPoolIndex = 0;
    [SerializeField] private int shellPoolIndex = 1;
    [SerializeField] private int clipPoolIndex = 2;
    [SerializeField] private int muzzleFlashPoolIndex = 3;

    [Header("UI")]
    [SerializeField] private Text bulletText;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shootClip;
    [SerializeField] private AudioClip reloadClip;

    [SerializeField] private ParticleSystem muzzleFlashParticle;

    private bool canShoot = true;
    private bool isReloading = false;

    public int CurrentBullet => currentBullet;
    public int MaxBullet => maxBullet;
    public bool CanShoot => canShoot;
    public bool IsReloading => isReloading;

    private void Start()
    {
        UpdateBulletUI();
    }

    public bool TryShoot(Vector3 targetPosition)
    {
        if (!canShoot) return false;
        if (isReloading) return false;
        if (currentBullet <= 0) return false;
        if (PoolManager_TPZ.instance == null) return false;

        currentBullet--;
        canShoot = false;

        SpawnBullet(targetPosition);
        SpawnShell();
        SpawnMuzzleFlash();
        PlayShootSound();
        UpdateBulletUI();

        Invoke(nameof(ResetShoot), shootDelay);
        return true;
    }

    public void StartReload()
    {
        if (isReloading) return;
        if (currentBullet >= maxBullet) return;

        isReloading = true;
        PlayReloadSound();
    }

    public void CompleteReload()
    {
        currentBullet = maxBullet;
        isReloading = false;
        UpdateBulletUI();
    }

    public void CancelReload()
    {
        isReloading = false;
        UpdateBulletUI();
    }

    private void ResetShoot()
    {
        canShoot = true;
    }

    private void SpawnBullet(Vector3 targetPosition)
    {
        if (firePos == null) return;

        Vector3 shootDirection = (targetPosition - firePos.position).normalized;

        if (shootDirection.sqrMagnitude < 0.0001f)
        {
            shootDirection = firePos.forward;
        }

        Quaternion bulletRotation = Quaternion.LookRotation(shootDirection);

        GameObject bullet = PoolManager_TPZ.instance.GetObject(
            bulletPoolIndex,
            firePos.position,
            bulletRotation
        );

        if (bullet == null) return;
    }

    private void SpawnShell()
    {
        if (shellPos == null) return;

        GameObject shell = PoolManager_TPZ.instance.GetObject(
            shellPoolIndex,
            shellPos.position,
            shellPos.rotation
        );

        if (shell == null) return;
    }

    private void SpawnClip()
    {
        if (clipPos == null) return;

        GameObject clip = PoolManager_TPZ.instance.GetObject(
            clipPoolIndex,
            clipPos.position,
            clipPos.rotation
        );

        if (clip == null) return;
    }

    private void SpawnMuzzleFlash()
    {
        if (muzzleFlashParticle == null) return;

        muzzleFlashParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        muzzleFlashParticle.Play();
    }

    public void OnReloadMagOut()
    {
        SpawnClip();
    }

    private void PlayShootSound()
    {
        if (audioSource != null && shootClip != null)
        {
            audioSource.PlayOneShot(shootClip);
        }
    }

    private void PlayReloadSound()
    {
        if (audioSource != null && reloadClip != null)
        {
            audioSource.PlayOneShot(reloadClip);
        }
    }

    public void UpdateBulletUI()
    {
        if (bulletText != null)
        {
            bulletText.text = currentBullet.ToString();
        }
    }

    public void SetBulletUI(Text targetUI)
    {
        bulletText = targetUI;
        UpdateBulletUI();
    }
}
