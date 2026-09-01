using UnityEngine;

/// <summary>
/// 무기 한 종류의 기본 스탯을 보관하는 정의 데이터입니다.
/// </summary>
/// <remarks>
/// 여기 값은 파츠 보정이 붙기 전의 <b>기본치</b>입니다. 실제 사용 수치는
/// <see cref="WeaponPart"/>가 가진 <see cref="StatModifier"/>를 적용한 결과입니다.
/// <para>
/// 런타임 전투 수치의 정본은 <see cref="GunBalanceSO"/>이며 <see cref="BindManager"/>가 주입합니다.
/// 이 에셋은 셸터에서 무기를 고르고 조립할 때 쓰는 카탈로그 성격의 데이터입니다.
/// </para>
/// </remarks>
[CreateAssetMenu(fileName = "Weapon", menuName = "Scriptable Objects/Weapon")]
public class Weapon : ScriptableObject
{
    /// <summary>저장과 조회에 사용하는 고정 무기 ID입니다.</summary>
    [Tooltip("저장과 조회에 사용하는 고정 무기 ID입니다. 에셋 이름과 별개로 유지합니다.")]
    public string weaponId;

    /// <summary>무기 분류입니다. 장착 슬롯과 사용 가능한 파츠를 가릅니다.</summary>
    [Tooltip("무기 분류입니다. 장착 슬롯과 사용 가능한 파츠를 가릅니다.")]
    public WeaponType weaponType;

    /// <summary>UI에 표시할 무기 이름입니다.</summary>
    [Tooltip("UI에 표시할 무기 이름입니다.")]
    public string weaponName;

    /// <summary>파츠 보정 전 기본 공격력입니다.</summary>
    [Tooltip("파츠 보정 전 기본 공격력입니다.")]
    public float baseDamage;

    /// <summary>파츠 보정 전 기본 연사 속도입니다.</summary>
    [Tooltip("파츠 보정 전 기본 연사 속도입니다. 클수록 빠르게 연사합니다.")]
    public float baseFireRate;

    /// <summary>파츠 보정 전 기본 재장전 속도입니다.</summary>
    [Tooltip("파츠 보정 전 기본 재장전 속도입니다. 클수록 빨리 장전합니다.")]
    public float baseReloadSpeed;

    /// <summary>파츠 보정 전 기본 탄퍼짐입니다.</summary>
    [Tooltip("파츠 보정 전 기본 탄퍼짐입니다. 클수록 탄착군이 넓어집니다.")]
    public float baseBulletSpray;

    /// <summary>파츠 보정 전 기본 반동 수치입니다.</summary>
    [Tooltip("파츠 보정 전 기본 반동 수치입니다. 클수록 조준점이 크게 밀립니다.")]
    public float baseRecoil;

    /// <summary>파츠 보정 전 기본 탄창 용량입니다.</summary>
    [Tooltip("파츠 보정 전 기본 탄창 용량입니다.")]
    public float baseAmmo;

    /// <summary>파츠 보정 전 기본 조준 전환 속도입니다.</summary>
    [Tooltip("파츠 보정 전 기본 조준(ADS) 전환 속도입니다. 클수록 빨리 조준 자세로 들어갑니다.")]
    public float baseADSSpeed;

    /// <summary>파츠 보정 전 기본 소음량입니다.</summary>
    [Tooltip("파츠 보정 전 기본 소음량입니다. 클수록 변이체가 멀리서도 듣습니다.")]
    public float baseNoiseLevel;

    /// <summary>파츠 보정 전 기본 기동성입니다.</summary>
    [Tooltip("파츠 보정 전 기본 기동성입니다. 클수록 무기를 든 채 빠르게 움직입니다.")]
    public float baseMobility;

    /// <summary>파츠 보정 전 기본 탄환 속도입니다.</summary>
    [Tooltip("파츠 보정 전 기본 탄환 속도입니다. 투사체 무기에만 의미가 있습니다.")]
    public float baseBulletSpeed;
}
