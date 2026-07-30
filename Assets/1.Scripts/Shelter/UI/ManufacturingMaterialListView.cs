using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 재료 행을 미리 생성해 캐시하고, 호출자가 전달한 견적만 화면에 투영합니다.
/// </summary>
/// <remarks>
/// 창고나 제조 매니저를 직접 조회하지 않습니다. 수량 또는 보유 자원이 바뀌면
/// 상위 CreateView가 새 견적 목록으로 <see cref="Bind"/>를 다시 호출해야 합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class ManufacturingMaterialListView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform m_contentRoot;
    [SerializeField] private ManufacturingMaterialRowView m_rowPrefab;
    [SerializeField] private ResourceDefinitionCatalog m_presentationCatalog;

    [Header("Capacity")]
    [Min(1)]
    [SerializeField] private int m_maxMaterialRows = 3;

    private readonly List<ManufacturingMaterialRowView> m_rows = new();
    private readonly HashSet<string> m_missingPresentationsLogged =
        new(System.StringComparer.Ordinal);
    private bool m_isPrewarmed;
    private bool m_setupErrorLogged;
    private bool m_capacityErrorLogged;

    public int Capacity => Mathf.Max(1, m_maxMaterialRows);
    public int CachedRowCount => m_rows.Count;

    private void Awake()
    {
        Prewarm();
    }

    /// <summary>
    /// 설정된 최대 행 수만큼 프리팹을 한 번만 생성하고 비활성 캐시로 보관합니다.
    /// 비활성 CreateView도 활성 상위 컨트롤러가 이 메서드를 직접 호출해 미리 준비할 수 있습니다.
    /// </summary>
    public bool Prewarm()
    {
        if (m_isPrewarmed)
            return true;

        RectTransform contentRoot = m_contentRoot != null
            ? m_contentRoot
            : transform as RectTransform;
        if (contentRoot == null || m_rowPrefab == null)
        {
            LogSetupErrorOnce();
            return false;
        }

        int capacity = Capacity;
        for (int i = 0; i < capacity; i++)
        {
            ManufacturingMaterialRowView row = Instantiate(
                m_rowPrefab,
                contentRoot,
                false);
            row.name = $"{m_rowPrefab.name}_{i + 1:00}";
            row.Clear();
            row.gameObject.SetActive(false);
            m_rows.Add(row);
        }

        m_isPrewarmed = true;
        return true;
    }

    /// <summary>
    /// 현재 견적에 필요한 행만 활성화하고 캐시된 행을 재바인딩합니다.
    /// </summary>
    /// <returns>모든 견적 행을 정상적으로 표시했으면 true입니다.</returns>
    public bool Bind(IReadOnlyList<ManufacturingMaterialQuoteLine> quoteLines)
    {
        if (quoteLines == null)
        {
            Clear();
            return false;
        }

        if (!Prewarm() || m_presentationCatalog == null)
        {
            LogSetupErrorOnce();
            Clear();
            return false;
        }

        if (quoteLines.Count > m_rows.Count)
        {
            if (!m_capacityErrorLogged)
            {
                Debug.LogError(
                    $"[{nameof(ManufacturingMaterialListView)}] "
                    + $"Material count {quoteLines.Count} exceeds cached capacity "
                    + $"{m_rows.Count}. Increase {nameof(m_maxMaterialRows)}.",
                    this);
                m_capacityErrorLogged = true;
            }

            Clear();
            return false;
        }

        bool allPresentationsResolved = true;
        for (int i = 0; i < m_rows.Count; i++)
        {
            ManufacturingMaterialRowView row = m_rows[i];
            if (i >= quoteLines.Count)
            {
                row.Clear();
                row.gameObject.SetActive(false);
                continue;
            }

            ManufacturingMaterialQuoteLine quoteLine = quoteLines[i];
            if (!m_presentationCatalog.TryGetPresentation(
                    quoteLine.ResourceId,
                    out ResourcePresentation presentation))
            {
                LogMissingPresentationOnce(quoteLine.ResourceId);
                row.Clear();
                row.gameObject.SetActive(false);
                allPresentationsResolved = false;
                continue;
            }

            row.Bind(presentation, quoteLine);
            row.gameObject.SetActive(true);
        }

        return allPresentationsResolved;
    }

    /// <summary>
    /// 생성된 행은 유지하면서 모든 표시를 비우고 비활성화합니다.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < m_rows.Count; i++)
        {
            ManufacturingMaterialRowView row = m_rows[i];
            if (row == null)
                continue;

            row.Clear();
            row.gameObject.SetActive(false);
        }
    }

    private void OnValidate()
    {
        m_maxMaterialRows = Mathf.Max(1, m_maxMaterialRows);
    }

    private void LogSetupErrorOnce()
    {
        if (m_setupErrorLogged)
            return;

        Debug.LogError(
            $"[{nameof(ManufacturingMaterialListView)}] "
            + "Content root, row prefab, and presentation catalog must be assigned.",
            this);
        m_setupErrorLogged = true;
    }

    private void LogMissingPresentationOnce(string resourceId)
    {
        if (!m_missingPresentationsLogged.Add(resourceId))
            return;

        Debug.LogError(
            $"[{nameof(ManufacturingMaterialListView)}] "
            + $"No shelter presentation is registered for '{resourceId}'.",
            this);
    }
}
