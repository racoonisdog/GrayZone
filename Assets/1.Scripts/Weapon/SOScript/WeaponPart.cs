using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무기에 장착해 스탯을 보정하는 파츠 한 종류의 정의 데이터입니다.
/// </summary>
/// <remarks>
/// 파츠 자체는 수치를 소유하지 않고 <b>보정 규칙만</b> 가집니다.
/// 기준이 되는 기본치는 <see cref="Weapon"/>에 있고, 최종 수치는 그 기본치에
/// <see cref="modifiers"/>를 순서대로 적용해 얻습니다.
/// </remarks>
[CreateAssetMenu(fileName = "WeaponPart", menuName = "Scriptable Objects/WeaponPart")]
public class WeaponPart : ScriptableObject
{
    /// <summary>저장과 조회에 사용하는 고정 파츠 ID입니다.</summary>
    [Tooltip("저장과 조회에 사용하는 고정 파츠 ID입니다. 에셋 이름과 별개로 유지합니다.")]
    public string partId;

    /// <summary>파츠 분류입니다. 장착 가능한 슬롯을 가릅니다.</summary>
    [Tooltip("파츠 분류입니다. 장착 가능한 슬롯을 가릅니다.")]
    public PartType partType;

    /// <summary>UI에 표시할 파츠 이름입니다.</summary>
    [Tooltip("UI에 표시할 파츠 이름입니다.")]
    public string partName;

    /// <summary>이 파츠가 무기 스탯에 가하는 보정 목록입니다.</summary>
    /// <remarks>같은 스탯에 여러 보정이 걸리면 목록 순서대로 적용됩니다.</remarks>
    [Tooltip("이 파츠가 무기 스탯에 가하는 보정 목록입니다. 같은 스탯에 여러 개가 걸리면 목록 순서대로 적용됩니다.")]
    public List<StatModifier> modifiers = new List<StatModifier>();
}
