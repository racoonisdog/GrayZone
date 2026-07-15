using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 여러 시설 UI가 공용으로 쓰는 NPC 후보 목록 패널.
/// 시설 UI(요청자)가 후보 목록과 클릭 콜백을 넘겨 열고, 연 요청자만 닫을 수 있다.
/// 다른 요청자가 새로 열면 이전 요청자는 <see cref="ClosedBy"/>로 통지받고 밀려난다.
/// </summary>
public class NpcCandidateListPanel : MonoBehaviour
{
    [SerializeField] private GameObject m_root;          // 표시/숨김 대상. 없으면 자기 자신
    [SerializeField] private NPCListScript m_listScript;
    [SerializeField] private bool m_hideOnAwake = true;

    private object m_currentRequester;   // null = 닫힘

    /// <summary>패널이 현재 열려 있는지 여부</summary>
    public bool IsOpen => m_currentRequester != null;

    /// <summary>요청자의 목록 표시가 끝났을 때(닫힘 또는 다른 요청자로 교체) 해당 요청자를 전달</summary>
    public event Action<object> ClosedBy;

    private void Awake()
    {
        CacheReferences();

        // Open 중 활성화로 Awake가 늦게 도는 경우(비활성 시작)에는 숨기지 않는다.
        if (m_hideOnAwake && m_currentRequester == null)
            SetVisible(false);
    }

    private void Reset()
    {
        CacheReferences();
    }

    /// <summary>
    /// 지정한 요청자가 연 상태인지 여부
    /// </summary>
    /// <param name="requester">검사할 요청자</param>
    public bool IsOpenFor(object requester)
        => m_currentRequester != null && ReferenceEquals(m_currentRequester, requester);

    /// <summary>
    /// 후보 목록을 표시. 이미 다른 요청자가 열어 두었다면 새 요청자가 이기고 이전 요청자에게 교체를 통지
    /// </summary>
    /// <param name="requester">목록을 여는 시설 UI(소유권 식별용)</param>
    /// <param name="candidates">표시할 NPC 후보 목록</param>
    /// <param name="onSelected">행 클릭 시 NPC 정의 ID를 전달할 콜백</param>
    public void Open(object requester, IReadOnlyList<NPCRuntimeData> candidates, Action<string> onSelected)
    {
        if (requester == null)
            return;

        CacheReferences();

        object previous = m_currentRequester;
        m_currentRequester = requester;

        SetVisible(true);

        if (m_listScript != null)
            m_listScript.Bind(candidates, onSelected);

        if (previous != null && !ReferenceEquals(previous, requester))
            ClosedBy?.Invoke(previous);
    }

    /// <summary>
    /// 요청자가 자기가 연 목록을 닫음. 소유자가 아니면 무시
    /// </summary>
    /// <param name="requester">닫기를 요청한 시설 UI</param>
    public void Close(object requester)
    {
        if (!IsOpenFor(requester))
            return;

        object previous = m_currentRequester;
        m_currentRequester = null;

        if (m_listScript != null)
            m_listScript.Clear();

        SetVisible(false);
        ClosedBy?.Invoke(previous);
    }

    private void SetVisible(bool visible)
    {
        GameObject root = m_root != null ? m_root : gameObject;
        if (root.activeSelf != visible)
            root.SetActive(visible);
    }

    private void CacheReferences()
    {
        if (m_root == null)
            m_root = gameObject;

        if (m_listScript == null)
            m_listScript = GetComponentInChildren<NPCListScript>(true);
    }
}
