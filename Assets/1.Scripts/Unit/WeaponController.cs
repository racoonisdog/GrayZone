using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VInspector;

/// <summary>
/// 무기의 탄약, 사격, 재장전, 탄피/탄창/머즐 이펙트 생성, 탄약 UI 갱신을 담당하는 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 실제 탄환, 탄피, 탄창 오브젝트 생성은 <c>PoolManager</c>를 통해 수행합니다.
/// 사격 입력 자체는 외부 컨트롤러에서 판단하고, 이 컴포넌트는 <see cref="TryShoot"/> 호출을 통해 사격 가능 여부와 발사 처리를 담당합니다.
/// </remarks>
public class WeaponController : MonoBehaviour
{
    /// <summary>
    /// 조준 중 계산된 총구 기준 히트스캔 사격 정보를 담습니다.
    /// </summary>
    /// <remarks>
    /// <see cref="AimController"/>가 조준 프레임에서 한 번 계산하고, 마커 표시와 사격 처리에서 같은 값을 사용합니다.
    /// </remarks>
    public struct HitscanShotInfo
    {
        public bool IsValid;
        public bool HasHit;
        public bool IsObstructed;
        public Vector3 Origin;
        public Vector3 Direction;
        public Vector3 AimPoint;
        public Vector3 EndPoint;
        public RaycastHit Hit;
        public int FrameCount;
    }

    [Foldout("Bullet Options")]
    [Tooltip("현재 탄약 수입니다.")]
    [FormerlySerializedAs("currentBullet")]
    [SerializeField] private int m_currentBullet = 30;

    [Tooltip("최대 탄약 수입니다.")]
    [FormerlySerializedAs("maxBullet")]
    [SerializeField] private int m_maxBullet = 30;

    [Tooltip("사격 후 다음 사격이 가능해질 때까지의 지연 시간입니다.")]
    [FormerlySerializedAs("shootDelay")]
    [SerializeField] private float m_shootDelay = 0.12f;

    [Tooltip("재장전에 필요한 시간입니다. 현재 스크립트에서는 상태값 용도로 보관하며, 실제 완료 타이밍은 애니메이션 이벤트에서 처리할 수 있습니다.")]
    [FormerlySerializedAs("reloadTime")]
    [SerializeField] private float m_reloadTime = 1.5f;

    [Foldout("Spawn Points")]
    [Tooltip("탄환이 생성될 위치입니다.")]
    [FormerlySerializedAs("firePos")]
    [SerializeField] private Transform m_firePos;

    [Tooltip("탄피가 생성될 위치입니다.")]
    [FormerlySerializedAs("shellPos")]
    [SerializeField] private Transform m_shellPos;

    [Tooltip("탄창이 떨어질 위치입니다.")]
    [FormerlySerializedAs("clipPos")]
    [SerializeField] private Transform m_clipPos;

    [Tooltip("머즐 플래시 위치입니다. 파티클 방식 사용 시 보조 참조로 사용합니다.")]
    [FormerlySerializedAs("muzzleFlashPos")]
    [SerializeField] private Transform m_muzzleFlashPos;

    [Foldout("Pool Index")]
    [Tooltip("탄환 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("bulletPoolIndex")]
    [SerializeField] private int m_bulletPoolIndex = 0;

    [Tooltip("탄피 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("shellPoolIndex")]
    [SerializeField] private int m_shellPoolIndex = 1;

    [Tooltip("탄창 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("clipPoolIndex")]
    [SerializeField] private int m_clipPoolIndex = 2;

    [Tooltip("머즐 플래시 오브젝트 풀 인덱스입니다. 현재 파티클 방식에서는 사용하지 않습니다.")]
    [FormerlySerializedAs("muzzleFlashPoolIndex")]
    [SerializeField] private int m_muzzleFlashPoolIndex = 3;

    [Foldout("UI Options")]
    [Tooltip("현재 탄약 수를 표시할 UI 텍스트입니다.")]
    [FormerlySerializedAs("bulletText")]
    [SerializeField] private Text m_bulletText;

    [Foldout("Audio Options")]
    [Tooltip("무기 효과음을 재생할 AudioSource입니다. 비어 있으면 같은 GameObject에서 자동 탐색합니다.")]
    [FormerlySerializedAs("audioSource")]
    [SerializeField] private AudioSource m_audioSource;

    [Tooltip("사격 효과음입니다.")]
    [FormerlySerializedAs("shootClip")]
    [SerializeField] private AudioClip m_shootClip;

    [Tooltip("재장전 효과음입니다.")]
    [FormerlySerializedAs("reloadClip")]
    [SerializeField] private AudioClip m_reloadClip;

    [Foldout("Effect Options")]
    [Tooltip("사격 시 재생할 머즐 플래시 파티클입니다.")]
    [FormerlySerializedAs("muzzleFlashParticle")]
    [SerializeField] private ParticleSystem m_muzzleFlashParticle;

    [Foldout("Hitscan Options")]
    [SerializeField] private float m_hitscanRange = 100.0f;

    [SerializeField] private LayerMask m_hitscanLayerMask = ~0;

    public const float HitscanAimTolerance = 0.05f;

    private bool m_canShoot = true;
    private bool m_isReloading;
    private bool m_hasRequiredReferences;

    /// <summary>현재 탄약 수입니다.</summary>
    public int CurrentBullet => m_currentBullet;

    /// <summary>최대 탄약 수입니다.</summary>
    public int MaxBullet => m_maxBullet;

    /// <summary>현재 사격 가능한 상태인지 여부입니다.</summary>
    public bool CanShoot => m_canShoot;

    /// <summary>현재 재장전 중인지 여부입니다.</summary>
    public bool IsReloading => m_isReloading;

    /// <summary>사격 지연 시간입니다.</summary>
    public float ShootDelay => m_shootDelay;

    /// <summary>재장전 시간입니다.</summary>
    public float ReloadTime => m_reloadTime;

    /// <summary>탄환 생성 위치입니다.</summary>
    public Transform FirePos => m_firePos;

    /// <summary>탄피 생성 위치입니다.</summary>
    public Transform ShellPos => m_shellPos;

    /// <summary>탄창 생성 위치입니다.</summary>
    public Transform ClipPos => m_clipPos;

    /// <summary>머즐 플래시 위치입니다.</summary>
    public Transform MuzzleFlashPos => m_muzzleFlashPos;

    /// <summary>탄약 UI 텍스트입니다.</summary>
    public Text BulletText => m_bulletText;

    /// <summary>히트스캔 판정에 사용할 최대 사거리입니다.</summary>
    public float HitscanRange => m_hitscanRange;

    /// <summary>히트스캔 레이캐스트가 충돌 검사할 레이어 마스크입니다.</summary>
    public LayerMask HitscanLayerMask => m_hitscanLayerMask;

    /// <summary>
    /// 컴포넌트 참조를 캐싱하고 필수 참조를 검증합니다.
    /// </summary>
    private void Awake()
    {
        CacheReferences();

        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        m_hasRequiredReferences = true;
    }

    /// <summary>
    /// 초기 탄약 UI를 갱신합니다.
    /// </summary>
    private void Start()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        ClampBulletValues();
        UpdateBulletUI();
    }

    /// <summary>
    /// 예약된 사격 쿨다운 호출을 정리합니다.
    /// </summary>
    private void OnDisable()
    {
        CancelInvoke(nameof(ResetShoot));
    }

    /// <summary>
    /// 자동으로 찾을 수 있는 내부 참조를 캐싱합니다.
    /// </summary>
    private void CacheReferences()
    {
        if (m_audioSource == null)
        {
            m_audioSource = GetComponent<AudioSource>();
        }
    }

    /// <summary>
    /// 필수 참조가 올바르게 설정되어 있는지 확인합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 유효하면 <c>true</c>, 하나라도 누락되면 <c>false</c>입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_firePos == null)
        {
            Debug.LogError("[WeaponController] FirePos가 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_shellPos == null)
        {
            Debug.LogWarning("[WeaponController] ShellPos가 할당되지 않았습니다. 탄피 생성은 생략됩니다.", this);
        }

        if (m_clipPos == null)
        {
            Debug.LogWarning("[WeaponController] ClipPos가 할당되지 않았습니다. 탄창 드롭은 생략됩니다.", this);
        }

        if (m_audioSource == null)
        {
            Debug.LogWarning("[WeaponController] AudioSource가 없습니다. 무기 효과음은 재생되지 않습니다.", this);
        }
        /*
        if (PoolManager.instance == null)
        {
            Debug.LogError("[WeaponController] PoolManager 인스턴스를 찾지 못했습니다.", this);
            isValid = false;
        }*/

        return isValid;
    }

    /// <summary>
    /// 탄약 관련 수치가 유효 범위를 벗어나지 않도록 보정합니다.
    /// </summary>
    private void ClampBulletValues()
    {
        m_maxBullet = Mathf.Max(0, m_maxBullet);
        m_currentBullet = Mathf.Clamp(m_currentBullet, 0, m_maxBullet);
        m_shootDelay = Mathf.Max(0.0f, m_shootDelay);
        m_reloadTime = Mathf.Max(0.0f, m_reloadTime);
    }

    /// <summary>
    /// 지정한 목표 위치를 향해 사격을 시도합니다.
    /// </summary>
    /// <param name="targetPosition">탄환이 향할 월드 좌표입니다.</param>
    /// <returns>사격에 성공하면 <c>true</c>, 사격 불가능 상태면 <c>false</c>입니다.</returns>
    public bool TryShoot(Vector3 targetPosition)
    {
        if (!m_hasRequiredReferences)
        {
            return false;
        }

        if (!m_canShoot || m_isReloading || m_currentBullet <= 0)
        {
            return false;
        }

        if (PoolManager.instance == null)
        {
            Debug.LogWarning("[WeaponController] PoolManager 인스턴스가 없어 사격을 처리할 수 없습니다.", this);
            return false;
        }

        m_currentBullet--;
        m_canShoot = false;
        // 오브젝트 풀링
        SpawnBullet(targetPosition);
        SpawnShell();


        SpawnMuzzleFlash();
        PlayShootSound();
        UpdateBulletUI();

        Invoke(nameof(ResetShoot), m_shootDelay);
        return true;
    }

    /// <summary>
    /// 조준 컨트롤러가 계산한 히트스캔 사격 정보로 사격을 시도합니다.
    /// </summary>
    /// <param name="shotInfo">총구 기준 원점, 방향, 최종 탄착점, 충돌 정보를 포함한 히트스캔 사격 정보입니다.</param>
    /// <returns>사격에 성공하면 <c>true</c>, 필수 참조 누락, 무효 사격 정보, 쿨다운, 재장전, 탄약 부족 상태면 <c>false</c>입니다.</returns>
    public bool TryLayShoot(HitscanShotInfo shotInfo)
    {
        if (!m_hasRequiredReferences)
        {
            return false;
        }

        if (!shotInfo.IsValid)
        {
            return false;
        }

        if (!m_canShoot || m_isReloading || m_currentBullet <= 0)
        {
            return false;
        }

        m_currentBullet--;
        m_canShoot = false;

        LayShoot(shotInfo);
        SpawnShell();
        SpawnMuzzleFlash();
        PlayShootSound();
        UpdateBulletUI();

        Invoke(nameof(ResetShoot), m_shootDelay);
        return true;
    }

    /// <summary>
    /// 재장전을 시작합니다.
    /// </summary>
    public void StartReload()
    {
        if (m_isReloading || m_currentBullet >= m_maxBullet)
        {
            return;
        }

        m_isReloading = true;
        PlayReloadSound();
    }

    /// <summary>
    /// 재장전을 완료하고 탄약을 최대치로 채웁니다.
    /// </summary>
    public void CompleteReload()
    {
        m_currentBullet = m_maxBullet;
        m_isReloading = false;
        UpdateBulletUI();
    }

    /// <summary>
    /// 진행 중인 재장전을 취소합니다.
    /// </summary>
    public void CancelReload()
    {
        m_isReloading = false;
        UpdateBulletUI();
    }

    /// <summary>
    /// 계산된 히트스캔 사격 정보를 소비해 즉시 사격 결과를 처리합니다.
    /// </summary>
    /// <param name="shotInfo">조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    /// <remarks>현재는 디버그 라인만 표시하며, 피해 적용과 Health 연결은 아직 수행하지 않습니다.</remarks>
    private void LayShoot(HitscanShotInfo shotInfo)
    {
        if (!shotInfo.IsValid)
        {
            return;
        }

        if (shotInfo.HasHit)
        {
            Debug.DrawLine(shotInfo.Origin, shotInfo.EndPoint, Color.red, 1.0f, false);
            return;
        }

        Debug.DrawLine(shotInfo.Origin, shotInfo.EndPoint, Color.yellow, 1.0f, false);
    }

    /// <summary>
    /// 사격 쿨다운을 종료하고 다시 사격 가능한 상태로 전환합니다.
    /// </summary>
    private void ResetShoot()
    {
        m_canShoot = true;
    }

    /// <summary>
    /// 탄환 오브젝트를 풀에서 꺼내 목표 방향으로 생성합니다.
    /// </summary>
    /// <param name="targetPosition">탄환이 향할 월드 좌표입니다.</param>
    private void SpawnBullet(Vector3 targetPosition)
    {
        if (m_firePos == null || PoolManager.instance == null)
        {
            return;
        }

        Vector3 shootDirection = targetPosition - m_firePos.position;

        if (shootDirection.sqrMagnitude < 0.0001f)
        {
            shootDirection = m_firePos.forward;
        }
        else
        {
            shootDirection.Normalize();
        }

        Quaternion bulletRotation = Quaternion.LookRotation(shootDirection);

        PoolManager.instance.GetObject(
            m_bulletPoolIndex,
            m_firePos.position,
            bulletRotation);
    }

    /// <summary>
    /// 탄피 오브젝트를 풀에서 생성합니다.
    /// </summary>
    private void SpawnShell()
    {
        if (m_shellPos == null || PoolManager.instance == null)
        {
            return;
        }

        PoolManager.instance.GetObject(
            m_shellPoolIndex,
            m_shellPos.position,
            m_shellPos.rotation);
    }

    /// <summary>
    /// 탄창 오브젝트를 풀에서 생성합니다.
    /// </summary>
    private void SpawnClip()
    {
        if (m_clipPos == null || PoolManager.instance == null)
        {
            return;
        }

        PoolManager.instance.GetObject(
            m_clipPoolIndex,
            m_clipPos.position,
            m_clipPos.rotation);
    }

    /// <summary>
    /// 머즐 플래시 파티클을 재생합니다.
    /// </summary>
    private void SpawnMuzzleFlash()
    {
        if (m_muzzleFlashParticle == null)
        {
            return;
        }

        m_muzzleFlashParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        m_muzzleFlashParticle.Play();
    }

    /// <summary>
    /// 재장전 애니메이션에서 탄창이 빠지는 타이밍에 호출되는 이벤트입니다.
    /// </summary>
    public void OnReloadMagOut()
    {
        SpawnClip();
    }

    /// <summary>
    /// 사격 효과음을 재생합니다.
    /// </summary>
    private void PlayShootSound()
    {
        if (m_audioSource == null || m_shootClip == null)
        {
            return;
        }

        m_audioSource.PlayOneShot(m_shootClip);
    }

    /// <summary>
    /// 재장전 효과음을 재생합니다.
    /// </summary>
    private void PlayReloadSound()
    {
        if (m_audioSource == null || m_reloadClip == null)
        {
            return;
        }

        m_audioSource.PlayOneShot(m_reloadClip);
    }

    /// <summary>
    /// 현재 탄약 UI를 갱신합니다.
    /// </summary>
    public void UpdateBulletUI()
    {
        if (m_bulletText == null)
        {
            return;
        }

        m_bulletText.text = $"{m_currentBullet} / {m_maxBullet}";
    }

    /// <summary>
    /// 탄약 표시용 UI 텍스트를 교체하고 즉시 갱신합니다.
    /// </summary>
    /// <param name="targetUI">새로 사용할 탄약 UI 텍스트입니다.</param>
    public void SetBulletUI(Text targetUI)
    {
        m_bulletText = targetUI;
        UpdateBulletUI();
    }

    /// <summary>
    /// 현재 탄약 수를 설정합니다.
    /// </summary>
    /// <param name="value">새 현재 탄약 수입니다.</param>
    public void SetCurrentBullet(int value)
    {
        m_currentBullet = Mathf.Clamp(value, 0, m_maxBullet);
        UpdateBulletUI();
    }

    /// <summary>
    /// 최대 탄약 수를 설정합니다.
    /// </summary>
    /// <param name="value">새 최대 탄약 수입니다.</param>
    public void SetMaxBullet(int value)
    {
        m_maxBullet = Mathf.Max(0, value);
        m_currentBullet = Mathf.Clamp(m_currentBullet, 0, m_maxBullet);
        UpdateBulletUI();
    }

    /// <summary>
    /// 사격 지연 시간을 설정합니다.
    /// </summary>
    /// <param name="value">새 사격 지연 시간입니다.</param>
    public void SetShootDelay(float value)
    {
        m_shootDelay = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 재장전 시간을 설정합니다.
    /// </summary>
    /// <param name="value">새 재장전 시간입니다.</param>
    public void SetReloadTime(float value)
    {
        m_reloadTime = Mathf.Max(0.0f, value);
    }
}
