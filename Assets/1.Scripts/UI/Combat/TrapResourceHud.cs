using TMPro;
using UnityEngine;

/// <summary>
/// 좌상단 목표 패널 아래 줄에 지금 가진 함정 설치 자원 수를 표시합니다(Figma <c>Reviving HUD_Description_Test02</c>의 "함정 설치 자원").
/// </summary>
/// <remarks>
/// 레이아웃(배경, 아이콘, 라벨)은 씬에 미리 배치돼 있고, 이 컴포넌트는 수량 텍스트만 채웁니다.
/// 수량은 함정 설치 비용을 치르는 곳(<see cref="Trap"/>)과 같은 보관소(<see cref="ShelterSceneDataManager.Storage"/>)에서 읽습니다.
/// 보관소가 없는 씬에서는 텍스트를 바꾸지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TrapResourceHud : MonoBehaviour
{
    [Tooltip("자원 수량을 표시할 텍스트입니다.")]
    [SerializeField] private TMP_Text m_amountText;

    [Tooltip("표시할 자원 ID입니다. 함정 설치 비용과 같은 자원(TrapMaterial)을 씁니다.")]
    [SerializeField] private string m_resourceId = ResourceIds.TrapMaterial;

    private int m_shownAmount = int.MinValue;

    private void LateUpdate()
    {
        if (m_amountText == null)
        {
            return;
        }

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        if (storage == null)
        {
            return;
        }

        int amount = storage.GetResourceAmount(m_resourceId);
        if (amount == m_shownAmount)
        {
            return;
        }

        m_shownAmount = amount;
        m_amountText.text = amount.ToString();
    }
}
