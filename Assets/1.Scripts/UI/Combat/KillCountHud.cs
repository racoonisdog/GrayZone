using TMPro;
using UnityEngine;

/// <summary>
/// 좌상단 목표 패널 아래 줄에 이번 필드에서 처치한 적 수를 실시간으로 표시합니다.
/// </summary>
/// <remarks>
/// 레이아웃(배경, 아이콘, 라벨)은 씬에 미리 배치돼 있고, 이 컴포넌트는 수량 텍스트만 채웁니다.
/// 수량은 결과창과 같은 값(<see cref="CombatSceneDataManager.KillCount"/>)을 읽습니다.
/// 데이터 매니저가 없는 씬에서는 텍스트를 바꾸지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class KillCountHud : MonoBehaviour
{
    [Tooltip("처치 수를 표시할 텍스트입니다.")]
    [SerializeField] private TMP_Text m_amountText;

    private int m_shownCount = int.MinValue;

    private void LateUpdate()
    {
        if (m_amountText == null)
        {
            return;
        }

        CombatSceneDataManager dataManager = CombatSceneDataManager.Instance;
        if (dataManager == null)
        {
            return;
        }

        int count = dataManager.KillCount;
        if (count == m_shownCount)
        {
            return;
        }

        m_shownCount = count;
        m_amountText.text = count.ToString();
    }
}
