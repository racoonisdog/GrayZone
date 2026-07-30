using UnityEngine;

/// <summary>
/// 하나의 지붕 구획을 구성하는 Renderer들을 묶고,
/// 카메라 가림 정도를 MaterialPropertyBlock으로 전달합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class RoofOcclusionTarget : MonoBehaviour
{
    private const string DefaultFadeProperty = "_OcclusionFade";

    [Header("Roof Visuals")]
    [Tooltip("이 지붕 구획과 함께 페이드할 Renderer들입니다. 비어 있으면 Awake에서 자식 Renderer를 자동 수집합니다.")]
    [SerializeField] private Renderer[] m_renderers = new Renderer[0];

    [Tooltip("Renderer 배열이 비어 있을 때 비활성 자식까지 포함해 자동 수집합니다.")]
    [SerializeField] private bool m_collectChildRenderersWhenEmpty = true;

    [Header("Shader")]
    [Tooltip("지붕 셰이더가 가림 정도(0=표시, 1=가림)를 받을 Float 프로퍼티 이름입니다.")]
    [SerializeField] private string m_fadeProperty = DefaultFadeProperty;

    private MaterialPropertyBlock m_propertyBlock;
    private int m_fadePropertyId;
    private float m_currentFade;
    private bool m_initialized;

    public float CurrentFade => m_currentFade;

    private void Reset()
    {
        CollectChildRenderers();
    }

    private void Awake()
    {
        Initialize();
        ApplyFade(0f);
    }

    private void OnDisable()
    {
        ApplyFade(0f);
    }

    /// <summary>
    /// 0이면 완전히 표시하고 1이면 셰이더가 정의한 최대 가림 상태로 만듭니다.
    /// </summary>
    public void ApplyFade(float fade)
    {
        Initialize();

        m_currentFade = Mathf.Clamp01(fade);

        for (int i = 0; i < m_renderers.Length; i++)
        {
            Renderer targetRenderer = m_renderers[i];
            if (targetRenderer == null)
                continue;

            m_propertyBlock.Clear();
            targetRenderer.GetPropertyBlock(m_propertyBlock);
            m_propertyBlock.SetFloat(m_fadePropertyId, m_currentFade);
            targetRenderer.SetPropertyBlock(m_propertyBlock);
        }
    }

    public void RestoreImmediately()
    {
        ApplyFade(0f);
    }

    [ContextMenu("Collect Child Renderers")]
    private void CollectChildRenderers()
    {
        m_renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Initialize()
    {
        if (m_initialized)
            return;

        if (m_collectChildRenderersWhenEmpty &&
            (m_renderers == null || m_renderers.Length == 0))
        {
            CollectChildRenderers();
        }

        if (m_renderers == null)
            m_renderers = new Renderer[0];

        if (string.IsNullOrWhiteSpace(m_fadeProperty))
            m_fadeProperty = DefaultFadeProperty;

        m_fadePropertyId = Shader.PropertyToID(m_fadeProperty);
        m_propertyBlock = new MaterialPropertyBlock();
        m_initialized = true;
    }
}
