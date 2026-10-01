using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 시설 업그레이드 연출. 검은 화면 → 시설 뷰(이전 레벨) → 검은 화면(업그레이드 적용) → 시설 뷰(새 레벨)
/// → 검은 화면 → 원래 카메라 순으로 보여주기
/// </summary>
/// <remarks>
/// 화면이 가려진 시점에 <see cref="FacilityManager.TryUpgrade"/>를 호출해, 비용 차감과
/// <see cref="FacilityLevelVisuals"/> 건물 교체가 검은 화면 뒤에서 일어나도록 하기.
/// 카메라 전환은 모두 검은 화면 뒤에서 일어나므로, 연출 동안 Brain 기본 블렌드를 Cut으로 바꿨다가 끝나면 되돌리기.
/// 입력 잠금과 UI 숨김은 <see cref="Started"/>/<see cref="Finished"/>를 받는 쪽에서 처리.
/// </remarks>
[DisallowMultipleComponent]
public sealed class FacilityUpgradeCutscene : MonoBehaviour
{
    [Header("References")]
    [Tooltip("업그레이드할 시설 ID입니다. FacilityManager에 등록된 ID와 같아야 합니다.")]
    [SerializeField] private string m_facilityId;
    [Tooltip("시설을 비추는 전용 카메라입니다. 평소에는 비활성 상태로 둡니다.")]
    [SerializeField] private CinemachineCamera m_viewCamera;
    [Tooltip("비어 있으면 페이드 없이 즉시 업그레이드를 적용합니다.")]
    [SerializeField] private ScreenFader m_fader;
    [Tooltip("카메라 전환을 Cut으로 바꿀 Brain입니다. 비어 있으면 Main Camera에서 찾습니다.")]
    [SerializeField] private CinemachineBrain m_brain;

    [Header("Camera")]
    [Tooltip("플레이어 카메라보다 높아야 전용 카메라가 화면을 가져옵니다.")]
    [SerializeField] private int m_cameraPriority = 100;

    [Header("Timing (unscaled seconds)")]
    [Tooltip("화면을 가리는 시간입니다. 세 번의 검은 화면 전환에 모두 사용합니다. 0이면 바로 가립니다.")]
    [SerializeField, Min(0f)] private float m_fadeOutDuration = 0.4f;
    [Tooltip("검은 화면을 유지하는 시간입니다. 카메라 전환과 업그레이드는 이 구간에 적용됩니다.")]
    [SerializeField, Min(0f)] private float m_blackHold = 0.3f;
    [Tooltip("가린 화면을 다시 보여주는 시간입니다.")]
    [SerializeField, Min(0f)] private float m_fadeInDuration = 0.4f;
    [Tooltip("시설 뷰에서 이전 레벨 건물을 보여주는 시간입니다.")]
    [SerializeField, Min(0f)] private float m_beforeHold = 1.5f;
    [Tooltip("시설 뷰에서 새 레벨 건물을 보여주는 시간입니다.")]
    [SerializeField, Min(0f)] private float m_afterHold = 1.5f;

    // 활성화된 연출을 시설 ID로 찾기 위한 등록부
    private static readonly Dictionary<string, FacilityUpgradeCutscene> s_byFacilityId = new();

    private Coroutine m_routine;
    private bool m_hasSavedBlend;
    private CinemachineBlendDefinition m_savedBlend;

    /// <summary>
    /// 시설 ID에 연결된 활성 연출을 찾기
    /// </summary>
    /// <param name="facilityId">찾을 시설 ID</param>
    /// <param name="cutscene">찾은 연출. 없으면 <c>null</c></param>
    /// <returns>연출을 찾았으면 <c>true</c></returns>
    public static bool TryGet(string facilityId, out FacilityUpgradeCutscene cutscene)
    {
        cutscene = null;
        return !string.IsNullOrWhiteSpace(facilityId)
            && s_byFacilityId.TryGetValue(facilityId, out cutscene)
            && cutscene != null;
    }

    /// <summary>연출 재생 중 여부</summary>
    public bool IsPlaying => m_routine != null;

    /// <summary>연출이 연결된 시설 ID</summary>
    public string FacilityId => m_facilityId;

    /// <summary>연출 시작 시 발생. 입력 잠금·UI 숨김 용도</summary>
    public event Action Started;

    /// <summary>연출 종료 시 발생. 인자는 업그레이드 성공 여부</summary>
    public event Action<bool> Finished;

    private void Awake()
    {
        SetCameraActive(false);
    }

    private void OnEnable()
    {
        if (string.IsNullOrWhiteSpace(m_facilityId))
            return;

        if (s_byFacilityId.TryGetValue(m_facilityId, out FacilityUpgradeCutscene other) && other != null && other != this)
            Debug.LogWarning($"[FacilityUpgradeCutscene] '{m_facilityId}' 연출이 중복 등록되었습니다. 마지막 연출을 사용합니다.", this);

        s_byFacilityId[m_facilityId] = this;
    }

    private void OnDisable()
    {
        if (!string.IsNullOrWhiteSpace(m_facilityId)
            && s_byFacilityId.TryGetValue(m_facilityId, out FacilityUpgradeCutscene registered)
            && registered == this)
        {
            s_byFacilityId.Remove(m_facilityId);
        }

        // 연출 도중 비활성화되면 카메라와 페이드가 남지 않도록 정리
        if (m_routine == null)
            return;

        m_routine = null;
        SetCameraActive(false);
        RestoreBrainBlend();
        if (m_fader != null)
            m_fader.SetAlpha(0f);
        Finished?.Invoke(false);
    }

    /// <summary>
    /// 업그레이드 가능 여부를 확인한 뒤 연출을 시작
    /// </summary>
    /// <returns>연출을 시작했으면 <c>true</c>. 재생 중이거나 업그레이드할 수 없으면 <c>false</c></returns>
    public bool Play()
    {
        if (IsPlaying || !isActiveAndEnabled)
            return false;

        FacilityManager fm = FacilityManager.Instance;
        if (fm == null || !fm.CanAffordUpgrade(m_facilityId))
            return false;

        m_routine = StartCoroutine(PlayRoutine());
        return true;
    }

    private IEnumerator PlayRoutine()
    {
        Started?.Invoke();
        OverrideBrainBlendToCut();

        // 1) 검은 화면 뒤에서 시설 뷰로 즉시 전환 → 이전 레벨 건물 보여주기
        yield return FadeOut();
        SetCameraActive(true);
        yield return null; // Brain이 LateUpdate에서 새 카메라로 전환할 한 프레임
        yield return Wait(m_blackHold);
        yield return FadeIn();
        yield return Wait(m_beforeHold);

        // 2) 검은 화면 뒤에서 비용 차감 + 레벨 증가 + LevelVisuals 교체 → 새 레벨 건물 보여주기
        yield return FadeOut();
        bool upgraded = FacilityManager.Instance != null && FacilityManager.Instance.TryUpgrade(m_facilityId);
        if (!upgraded)
            Debug.LogWarning($"[FacilityUpgradeCutscene] '{m_facilityId}' 업그레이드에 실패했습니다.", this);
        yield return Wait(m_blackHold);
        yield return FadeIn();
        yield return Wait(m_afterHold);

        // 3) 검은 화면 뒤에서 원래 카메라로 즉시 복귀
        yield return FadeOut();
        SetCameraActive(false);
        yield return null;
        RestoreBrainBlend();
        yield return Wait(m_blackHold);
        yield return FadeIn();

        m_routine = null;
        Finished?.Invoke(upgraded);
    }

    private IEnumerator FadeOut()
    {
        if (m_fader != null)
            yield return m_fader.FadeOut(m_fadeOutDuration);
    }

    private IEnumerator FadeIn()
    {
        if (m_fader != null)
            yield return m_fader.FadeIn(m_fadeInDuration);
    }

    // 연출 동안 카메라 전환이 날아가지 않도록 Brain 기본 블렌드를 Cut으로 바꾸기
    private void OverrideBrainBlendToCut()
    {
        if (m_brain == null && Camera.main != null)
            m_brain = Camera.main.GetComponent<CinemachineBrain>();

        if (m_brain == null || m_hasSavedBlend)
            return;

        m_savedBlend = m_brain.DefaultBlend;
        m_hasSavedBlend = true;
        m_brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
    }

    private void RestoreBrainBlend()
    {
        if (!m_hasSavedBlend)
            return;

        if (m_brain != null)
            m_brain.DefaultBlend = m_savedBlend;
        m_hasSavedBlend = false;
    }

    private void SetCameraActive(bool active)
    {
        if (m_viewCamera == null)
            return;

        if (active)
        {
            m_viewCamera.Priority.Enabled = true;
            m_viewCamera.Priority.Value = m_cameraPriority;
        }

        if (m_viewCamera.gameObject.activeSelf != active)
            m_viewCamera.gameObject.SetActive(active);
    }

    private static IEnumerator Wait(float seconds)
    {
        if (seconds > 0f)
            yield return new WaitForSecondsRealtime(seconds);
    }
}
