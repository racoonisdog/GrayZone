using UnityEngine;

/// <summary>
/// 시설 레벨별 건물 비주얼을 전환하는 표시 전용 컴포넌트
/// </summary>
/// <remarks>
/// 업그레이드나 시설 도메인 규칙을 알지 않고, 인스펙터에 연결된 <c>levelRoots[i]</c> 중 현재 레벨에 해당하는 오브젝트만 켜기.
/// </remarks>
public class FacilityLevelVisuals : MonoBehaviour
{
    [SerializeField] private GameObject[] levelRoots;

    /// <summary>등록된 레벨 비주얼 개수</summary>
    public int LevelCount => levelRoots != null ? levelRoots.Length : 0;

    /// <summary>
    /// 지정한 레벨의 비주얼만 켜고 나머지는 끄기. 범위를 벗어난 값은 가능한 레벨로 보정
    /// </summary>
    /// <param name="level">표시할 시설 레벨 인덱스</param>
    public void ShowLevel(int level)
    {
        if (levelRoots == null || levelRoots.Length == 0)
            return;

        int target = Mathf.Clamp(level, 0, levelRoots.Length - 1);
        for (int i = 0; i < levelRoots.Length; i++)
        {
            GameObject root = levelRoots[i];
            if (root == null)
                continue;

            bool active = (i == target);
            if (root.activeSelf != active)
                root.SetActive(active);
        }
    }

    /// <summary>
    /// 모든 레벨 비주얼을 끄기. 시설 잠금 상태처럼 표시할 레벨이 없을 때 사용
    /// </summary>
    public void HideAll()
    {
        if (levelRoots == null)
            return;

        foreach (GameObject root in levelRoots)
        {
            if (root != null && root.activeSelf)
                root.SetActive(false);
        }
    }
}
