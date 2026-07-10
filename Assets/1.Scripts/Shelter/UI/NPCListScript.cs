using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NPC 목록 행 프리팹이 구현해야 하는 바인딩 계약
/// </summary>
public interface INPCListItemView
{
    /// <summary>
    /// NPC 런타임 데이터와 클릭 콜백을 행 UI에 바인딩
    /// </summary>
    /// <param name="npcData">표시할 NPC 런타임 데이터</param>
    /// <param name="onClicked">행 클릭 시 NPC 정의 ID를 전달할 콜백</param>
    void Bind(NPCRuntimeData npcData, Action<string> onClicked);

    /// <summary>
    /// 행 UI에 표시된 데이터와 클릭 상태를 비움.
    /// </summary>
    void Clear();
}

/// <summary>
/// NPC 후보 목록을 행 프리팹으로 생성하고 제거하는 UI 목록 컨트롤러
/// </summary>
public class NPCListScript : MonoBehaviour
{
    [SerializeField] private Transform contentRoot;
    [SerializeField] private GameObject rowPrefab;

    private readonly List<GameObject> spawnedRows = new List<GameObject>();

    /// <summary>
    /// NPC 목록을 다시 만들고 각 행에 클릭 콜백을 연결
    /// </summary>
    /// <param name="characters">표시할 NPC 후보 목록</param>
    /// <param name="onClicked">행 클릭 시 호출할 콜백</param>
    public void Bind(IReadOnlyList<NPCRuntimeData> characters, Action<string> onClicked)
    {
        Clear();

        if (characters == null || rowPrefab == null)
            return;

        Transform parent = ResolveContentRoot();
        for (int i = 0; i < characters.Count; i++)
        {
            NPCRuntimeData character = characters[i];
            if (character == null || string.IsNullOrWhiteSpace(character.DefinitionId))
                continue;

            GameObject rowObject = Instantiate(rowPrefab, parent);
            if (!TryGetItemView(rowObject, out INPCListItemView rowView))
            {
                Debug.LogWarning($"[{nameof(NPCListScript)}] Row prefab must contain a component implementing {nameof(INPCListItemView)}.", this);
                Destroy(rowObject);
                continue;
            }

            rowObject.SetActive(true);
            rowView.Bind(character, onClicked);
            spawnedRows.Add(rowObject);
        }
    }

    /// <summary>
    /// 현재 생성된 모든 NPC 행을 제거
    /// </summary>
    public void Clear()
    {
        for (int i = spawnedRows.Count - 1; i >= 0; i--)
        {
            if (spawnedRows[i] != null)
                Destroy(spawnedRows[i]);
        }

        spawnedRows.Clear();
    }

    private Transform ResolveContentRoot()
    {
        return contentRoot != null ? contentRoot : transform;
    }

    private static bool TryGetItemView(GameObject rowObject, out INPCListItemView itemView)
    {
        itemView = null;
        if (rowObject == null)
            return false;

        MonoBehaviour[] components = rowObject.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is INPCListItemView view)
            {
                itemView = view;
                return true;
            }
        }

        return false;
    }
}
